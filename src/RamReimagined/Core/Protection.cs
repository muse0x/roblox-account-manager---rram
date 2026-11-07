using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

namespace RamReimagined.Core;

/// <summary>
/// Windows DPAPI (CurrentUser) wrappers. Cookies are encrypted with the Windows
/// account that saved them, so the account file is useless if copied elsewhere.
/// </summary>
internal static class Protection
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public uint cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string Protect(string plain) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(plain), true));

    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(protectedBase64), false));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inBlob = new DataBlob();
        var outBlob = new DataBlob();
        var buffer = Marshal.AllocHGlobal(Math.Max(input.Length, 1));
        try
        {
            Marshal.Copy(input, 0, buffer, input.Length);
            inBlob.cbData = (uint)input.Length;
            inBlob.pbData = buffer;

            var ok = protect
                ? CryptProtectData(ref inBlob, "RRAM", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob);

            if (!ok)
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (outBlob.pbData != IntPtr.Zero)
            {
                LocalFree(outBlob.pbData);
            }

            Marshal.FreeHGlobal(buffer);
        }
    }
}
