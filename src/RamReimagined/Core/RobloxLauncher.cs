using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RamReimagined.Core;

public sealed record LaunchResult(bool Ok, string Message, int? ProcessId);

/// <summary>
/// Everything needed to start a signed-in Roblox client on current builds:
/// a fresh auth ticket, the modern place launcher URL, multi-instance object
/// names, optional fast flags and an optional window grid.
/// </summary>
public sealed class RobloxLauncher
{
    public RobloxLauncher(RobloxApi api, AccountStore store, AppSettings settings)
    {
        Api = api;
        Store = store;
        Settings = settings;
        MultiInstance = new MultiInstanceLock();
    }

    public RobloxApi Api { get; }

    public AccountStore Store { get; }

    public AppSettings Settings { get; }

    public MultiInstanceLock MultiInstance { get; }

    public string? PlayerPath => FindPlayerExecutable(Settings.LaunchVersion);

    public string? VersionFolder => PlayerPath is null ? null : Path.GetDirectoryName(PlayerPath);

    public static string? FindPlayerExecutable(string? VersionFolder = null)
    {
        var VersionsDirectory = Path.Combine(AppPaths.LocalRobloxDirectory, "Versions");
        if (!Directory.Exists(VersionsDirectory))
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(VersionFolder))
            {
                var Selected = Path.Combine(VersionsDirectory, VersionFolder, "RobloxPlayerBeta.exe");
                if (File.Exists(Selected))
                {
                    return Selected;
                }
            }

            return Directory.EnumerateDirectories(VersionsDirectory)
                .Select(dir => Path.Combine(dir, "RobloxPlayerBeta.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string BuildLaunchUrl(long placeId, string? jobId, string? launchData = null)
    {
        var url = $"https://www.roblox.com/games/start?placeId={placeId}";
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            url += $"&gameInstanceId={Uri.EscapeDataString(jobId)}";
        }

        if (!string.IsNullOrWhiteSpace(launchData))
        {
            url += $"&launchData={Uri.EscapeDataString(launchData)}";
        }

        return url;
    }

    public async Task<LaunchResult> LaunchAsync(Account account, long placeId, string? jobId = null, CancellationToken ct = default)
    {
        var cookie = Store.CookieFor(account);
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return new LaunchResult(false, "No session stored for this account.", null);
        }

        var exe = FindPlayerExecutable(Settings.LaunchVersion);
        if (exe is null)
        {
            return new LaunchResult(false, "Roblox is not installed on this account of Windows.", null);
        }

        if (placeId <= 0)
        {
            return new LaunchResult(false, "Set a place ID first.", null);
        }

        var ticket = await Api.GetAuthTicketAsync(cookie, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return new LaunchResult(false, "Session expired. Re-add the account.", null);
        }

        if (!string.Equals(ticket.Cookie, cookie, StringComparison.Ordinal))
        {
            Store.UpdateCookie(account, ticket.Cookie);
        }

        var tracker = $"https://www.roblox.com/games/{placeId}/";
        var arguments = $"--app -t \"{ticket.Ticket}\" -j \"{BuildLaunchUrl(placeId, jobId)}\" -b \"{tracker}\"";

        try
        {
            var info = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppPaths.LocalRobloxDirectory
            };

            var process = Process.Start(info);
            Store.TouchLaunched(account);
            return new LaunchResult(true, $"Launched {account.Name}", process?.Id);
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, ex.Message, null);
        }
    }

    public static IReadOnlyList<Process> RunningClients()
    {
        try
        {
            return Process.GetProcessesByName("RobloxPlayerBeta")
                .Where(p => p.MainWindowHandle != IntPtr.Zero)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<Process>();
        }
    }
}

/// <summary>
/// Roblox only allows one client per Windows session because of two named
/// kernel objects. Holding them here makes extra clients start. Nothing is
/// injected into the client and no game files are touched.
/// </summary>
public sealed class MultiInstanceLock : IDisposable
{
    private readonly List<IDisposable> _handles = new();

    public bool Active => _handles.Count > 0;

    public string Enable()
    {
        if (Active)
        {
            return "Multi-instance is already active.";
        }

        try
        {
            // Names must match the kernel object types Roblox creates: a mutex and
            // an auto-reset event. Creating one under the wrong type throws, and a
            // failure here must never stop the app from starting.
            _handles.Add(new Mutex(false, "ROBLOX_singletonMutex"));
            _handles.Add(new EventWaitHandle(false, EventResetMode.AutoReset, "ROBLOX_singletonEvent"));
            return "Multi-instance is active: extra clients will be allowed.";
        }
        catch (Exception ex)
        {
            Disable();
            return $"Could not take the singleton objects: {ex.Message}";
        }
    }

    public void Disable()
    {
        foreach (var handle in _handles)
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception)
            {
                // Releasing a kernel object is best-effort.
            }
        }

        _handles.Clear();
    }

    public void Dispose() => Disable();
}

public static class RobloxSettings
{
    /// <summary>Writes ClientAppSettings.json into the selected client's ClientSettings folder.</summary>
    public static string? ApplyFastFlags(IReadOnlyDictionary<string, string> flags, out string message,
        string? VersionFolder = null)
    {
        var versions = Path.Combine(AppPaths.LocalRobloxDirectory, "Versions");
        if (!Directory.Exists(versions))
        {
            message = "No Roblox install found.";
            return null;
        }

        string? settingsDir = null;
        if (!string.IsNullOrWhiteSpace(VersionFolder))
        {
            var selected = Path.Combine(versions, VersionFolder, "ClientSettings");
            if (Directory.Exists(selected))
            {
                settingsDir = selected;
            }
        }

        settingsDir ??= Directory.EnumerateDirectories(versions)
            .Select(dir => Path.Combine(dir, "ClientSettings"))
            .Where(Directory.Exists)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (settingsDir is null)
        {
            message = "This Roblox build has no ClientSettings folder.";
            return null;
        }

        var target = Path.Combine(settingsDir, "ClientAppSettings.json");
        try
        {
            if (File.Exists(target))
            {
                var backup = target + ".ram-backup";
                if (!File.Exists(backup))
                {
                    File.Copy(target, backup);
                }
            }

            if (flags.Count == 0)
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                message = "Fast flags cleared.";
                return target;
            }

            File.WriteAllText(target, System.Text.Json.JsonSerializer.Serialize(
                flags, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            message = $"Wrote {flags.Count} fast flag(s) to {Path.GetFileName(settingsDir)}.";
            return target;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return null;
        }
    }

    public static Dictionary<string, string> Presets() => new()
    {
        ["DFIntTaskSchedulerTargetFps"] = "240",
        ["FFlagTaskSchedulerLimitTargetFpsTo2402"] = "False",
        ["FIntRenderShadowIntensity"] = "0",
        ["DFIntDebugFRMQualityLevelOverride"] = "1",
        ["FIntRenderGrassDetailStrands"] = "0",
        ["FFlagDisablePostFx"] = "True",
        ["FIntROMarkPlacePremiumUpsellRatePerThousand"] = "0"
    };

    /// <summary>Sets the quality level in Roblox's own global settings, keeping a backup.</summary>
    public static bool TrySetGraphicsQuality(int level, out string message)
    {
        var path = Path.Combine(AppPaths.LocalRobloxDirectory, "GlobalBasicSettings_13.xml");
        if (!File.Exists(path))
        {
            message = "Roblox global settings file not found; start Roblox once, then retry.";
            return false;
        }

        try
        {
            var backup = path + ".ram-backup";
            if (!File.Exists(backup))
            {
                File.Copy(path, backup);
            }

            var text = File.ReadAllText(path);
            var updated = System.Text.RegularExpressions.Regex.Replace(
                text,
                "(<int name=\"SavedQualityLevel\">)(-?\\d+)(</int>)",
                m => m.Groups[1].Value + Math.Clamp(level, 1, 10) + m.Groups[3].Value);

            if (ReferenceEquals(updated, text) || updated == text)
            {
                message = "Quality field not present; nothing changed.";
                return false;
            }

            File.WriteAllText(path, updated);
            message = $"Graphics quality set to {level}.";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}

public static class WindowTiler
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static int Tile()
    {
        var clients = RobloxLauncher.RunningClients().OrderBy(p => p.Id).ToList();
        if (clients.Count == 0)
        {
            return 0;
        }

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var columns = (int)Math.Ceiling(Math.Sqrt(clients.Count));
        var rows = (int)Math.Ceiling(clients.Count / (double)columns);
        var width = screen.Width / columns;
        var height = screen.Height / rows;

        for (var index = 0; index < clients.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            MoveWindow(clients[index].MainWindowHandle, screen.Left + column * width, screen.Top + row * height,
                width, height, true);
        }

        return clients.Count;
    }
}
