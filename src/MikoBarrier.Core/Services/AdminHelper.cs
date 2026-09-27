using System.Diagnostics;
using System.Security.Principal;

namespace MikoBarrier.Core.Services;

public static class AdminHelper
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>以管理员身份重新启动自己（会弹 UAC）。</summary>
    public static bool TryRelaunchElevated(string? arguments = null)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas",
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string RunCommand(string fileName, string arguments, out int exitCode)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                exitCode = -1;
                return "无法启动进程";
            }

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(20_000);
            exitCode = process.HasExited ? process.ExitCode : -1;
            return output.Trim();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return ex.Message;
        }
    }
}
