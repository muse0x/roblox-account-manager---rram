using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RamReimagined.Core;

public sealed class RobloxResourceManager : IDisposable
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectCpuRateControlInformation = 15;
    private const uint JobObjectLimitProcessMemory = 0x100;
    private const uint JobObjectCpuRateEnable = 0x1;
    private const uint JobObjectCpuRateHardCap = 0x4;
    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessTerminate = 0x0001;

    private readonly object Sync = new();
    private readonly AppSettings Settings;
    private readonly HashSet<int> AssignedProcessIds = new();
    private readonly HashSet<int> FailedProcessIds = new();
    private SafeFileHandle? JobHandle;
    private DateTime LastWorkingSetCleanupUtc = DateTime.MinValue;

    public RobloxResourceManager(AppSettings Settings)
    {
        this.Settings = Settings;
        LastWorkingSetCleanupUtc = DateTime.UtcNow;
    }

    public (int Applied, int Failed) ApplySettings()
    {
        lock (Sync)
        {
            LastWorkingSetCleanupUtc = DateTime.UtcNow;
            FailedProcessIds.Clear();
            if (!ConfigureJob())
            {
                return (0, 1);
            }

            return ApplyToRunningClients();
        }
    }

    public void TrackProcess(Process Process)
    {
        lock (Sync)
        {
            if (!HasLimits || JobHandle is null || JobHandle.IsInvalid || JobHandle.IsClosed)
            {
                return;
            }

            try
            {
                Assign(Process.Id);
            }
            catch (Exception)
            {
                // Resource control must never turn a successful Roblox launch into a launch failure.
            }
        }
    }

    public void Tick()
    {
        lock (Sync)
        {
            var Now = DateTime.UtcNow;
            var ShouldClean = Settings.RobloxWorkingSetCleanupEnabled &&
                (Now - LastWorkingSetCleanupUtc).TotalMinutes >= Math.Clamp(Settings.RobloxCleanupIntervalMinutes, 1, 120);
            if (ShouldClean)
            {
                LastWorkingSetCleanupUtc = Now;
            }

            Process[] Processes;
            try
            {
                Processes = Process.GetProcessesByName("RobloxPlayerBeta");
            }
            catch (Exception)
            {
                return;
            }

            var RunningIds = Processes.Select(GetProcessId).Where(ProcessId => ProcessId.HasValue)
                .Select(ProcessId => ProcessId.GetValueOrDefault()).ToHashSet();
            AssignedProcessIds.RemoveWhere(ProcessId => !RunningIds.Contains(ProcessId));
            FailedProcessIds.RemoveWhere(ProcessId => !RunningIds.Contains(ProcessId));

            foreach (var Process in Processes)
            {
                using (Process)
                {
                    var ProcessId = GetProcessId(Process);
                    if (ProcessId is null)
                    {
                        continue;
                    }

                    if (HasLimits && !AssignedProcessIds.Contains(ProcessId.Value) && !FailedProcessIds.Contains(ProcessId.Value))
                    {
                        Assign(ProcessId.Value);
                    }

                    if (ShouldClean)
                    {
                        TrimWorkingSet(ProcessId.Value);
                    }
                }
            }
        }
    }

    private bool HasLimits => Settings.RobloxCpuLimitPercent is > 0 and < 100 ||
        Settings.RobloxMemoryLimitMb > 0;

    private (int Applied, int Failed) ApplyToRunningClients()
    {
        Process[] Processes;
        try
        {
            Processes = Process.GetProcessesByName("RobloxPlayerBeta");
        }
        catch (Exception)
        {
            return (0, 1);
        }

        var Applied = 0;
        var Failed = 0;
        foreach (var Process in Processes)
        {
            using (Process)
            {
                if (!HasLimits)
                {
                    continue;
                }

                var ProcessId = GetProcessId(Process);
                if (ProcessId is null)
                {
                    Failed++;
                    continue;
                }

                if (AssignedProcessIds.Contains(ProcessId.Value))
                {
                    Applied++;
                    continue;
                }

                if (Assign(ProcessId.Value))
                {
                    Applied++;
                }
                else
                {
                    Failed++;
                }
            }
        }

        return (Applied, Failed);
    }

    private bool ConfigureJob()
    {
        if (!HasLimits)
        {
            if (JobHandle is null || JobHandle.IsInvalid || JobHandle.IsClosed)
            {
                return true;
            }

            return SetJobLimits(0, 0);
        }

        JobHandle ??= CreateJobObject(IntPtr.Zero, null);
        if (JobHandle.IsInvalid)
        {
            return false;
        }

        var MemoryBytes = Settings.RobloxMemoryLimitMb > 0
            ? (ulong)Math.Clamp(Settings.RobloxMemoryLimitMb, 512, 131_072) * 1024 * 1024
            : 0;
        return SetJobLimits(MemoryBytes, (uint)Math.Clamp(Settings.RobloxCpuLimitPercent, 0, 100) * 100);
    }

    private bool SetJobLimits(ulong MemoryBytes, uint CpuRate)
    {
        if (JobHandle is null || JobHandle.IsInvalid || JobHandle.IsClosed)
        {
            return false;
        }

        var Limits = new ExtendedLimitInformation();
        if (MemoryBytes > 0)
        {
            Limits.BasicLimitInformation.LimitFlags = JobObjectLimitProcessMemory;
            Limits.ProcessMemoryLimit = (UIntPtr)MemoryBytes;
        }

        if (!SetInformationJobObject(JobHandle, JobObjectExtendedLimitInformation, ref Limits,
                (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            return false;
        }

        var CpuLimits = new CpuRateControlInformation();
        if (CpuRate is > 0 and < 10_000)
        {
            CpuLimits.ControlFlags = JobObjectCpuRateEnable | JobObjectCpuRateHardCap;
            CpuLimits.CpuRate = CpuRate;
        }

        return SetInformationJobObject(JobHandle, JobObjectCpuRateControlInformation, ref CpuLimits,
            (uint)Marshal.SizeOf<CpuRateControlInformation>());
    }

    private bool Assign(int ProcessId)
    {
        if (JobHandle is null || JobHandle.IsInvalid || JobHandle.IsClosed)
        {
            return false;
        }

        try
        {
            using var ProcessHandle = OpenProcess(ProcessSetQuota | ProcessTerminate, false, (uint)ProcessId);
            if (ProcessHandle.IsInvalid || !AssignProcessToJobObject(JobHandle, ProcessHandle))
            {
                FailedProcessIds.Add(ProcessId);
                return false;
            }

            AssignedProcessIds.Add(ProcessId);
            FailedProcessIds.Remove(ProcessId);
            return true;
        }
        catch (Exception)
        {
            FailedProcessIds.Add(ProcessId);
            return false;
        }
    }

    private static void TrimWorkingSet(int ProcessId)
    {
        using var ProcessHandle = OpenProcess(ProcessSetQuota | ProcessQueryInformation, false, (uint)ProcessId);
        if (!ProcessHandle.IsInvalid)
        {
            EmptyWorkingSet(ProcessHandle);
        }
    }

    private static int? GetProcessId(Process Process)
    {
        try
        {
            return Process.Id;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (Sync)
        {
            if (JobHandle is { IsInvalid: false, IsClosed: false })
            {
                SetJobLimits(0, 0);
                JobHandle.Dispose();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuRateControlInformation
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr JobAttributes, string? Name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle Job, uint InformationClass,
        ref ExtendedLimitInformation Information, uint InformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle Job, uint InformationClass,
        ref CpuRateControlInformation Information, uint InformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenProcess(uint DesiredAccess, bool InheritHandle, uint ProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle Job, SafeFileHandle Process);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(SafeFileHandle Process);
}
