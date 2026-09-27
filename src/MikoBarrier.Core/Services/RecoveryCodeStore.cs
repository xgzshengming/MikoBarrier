using System.Runtime.InteropServices;
using System.Text;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 恢复码的本机副本：用 Windows DPAPI（CurrentUser）加密后写入 data\recovery-code.dat。
/// 只能被同一 Windows 用户的程序解密；拷贝 data 目录到别的电脑 / 别的用户下无法还原。
/// 文件里保存的是恢复码明文加密后的数据，不参与登录校验；忘记密码时可在应用内查看。
/// </summary>
public static class RecoveryCodeStore
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int CbData;
        public IntPtr PbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static bool Exists
    {
        get
        {
            try
            {
                return File.Exists(StoragePaths.RecoveryCodeFile);
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool Save(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        try
        {
            StoragePaths.EnsureCreated();
            var plain = Encoding.UTF8.GetBytes(code.Trim());
            var encrypted = Protect(plain);
            if (encrypted is null)
            {
                return false;
            }

            var path = StoragePaths.RecoveryCodeFile;
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryLoad(out string code)
    {
        code = string.Empty;
        try
        {
            if (!File.Exists(StoragePaths.RecoveryCodeFile))
            {
                return false;
            }

            var encrypted = File.ReadAllBytes(StoragePaths.RecoveryCodeFile);
            var plain = Unprotect(encrypted);
            if (plain is null || plain.Length == 0)
            {
                return false;
            }

            code = Encoding.UTF8.GetString(plain).Trim();
            return code.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? Protect(byte[] plain)
    {
        var input = ToBlob(plain);
        try
        {
            if (!CryptProtectData(ref input, "MikoBarrier recovery code", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
            {
                return null;
            }

            try
            {
                return FromBlob(output);
            }
            finally
            {
                if (output.PbData != IntPtr.Zero)
                {
                    LocalFree(output.PbData);
                }
            }
        }
        finally
        {
            if (input.PbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(input.PbData);
            }
        }
    }

    private static byte[]? Unprotect(byte[] encrypted)
    {
        var input = ToBlob(encrypted);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
            {
                return null;
            }

            try
            {
                return FromBlob(output);
            }
            finally
            {
                if (output.PbData != IntPtr.Zero)
                {
                    LocalFree(output.PbData);
                }
            }
        }
        finally
        {
            if (input.PbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(input.PbData);
            }
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var blob = new DataBlob { CbData = bytes.Length, PbData = Marshal.AllocHGlobal(bytes.Length) };
        Marshal.Copy(bytes, 0, blob.PbData, bytes.Length);
        return blob;
    }

    private static byte[] FromBlob(DataBlob blob)
    {
        var bytes = new byte[blob.CbData];
        Marshal.Copy(blob.PbData, bytes, 0, blob.CbData);
        return bytes;
    }
}
