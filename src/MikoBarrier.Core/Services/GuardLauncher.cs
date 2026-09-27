using System.Diagnostics;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 启动 / 停止看门狗（MikoBarrier.Guard.exe）。
/// 看门狗负责两件事：自律期间保证主程序不被杀，以及按名单做进程巡逻。
/// 自律结束后它会自己退出（session.lock 消失即退出）。
/// </summary>
public static class GuardLauncher
{
    private const string GuardProcessName = "MikoBarrier.Guard";

    public static bool IsRunning() => Process.GetProcessesByName(GuardProcessName).Length > 0;

    /// <summary>启动看门狗（幂等：已经在跑就直接返回成功）。</summary>
    public static (bool Ok, string Message) TryStartPatrol()
    {
        if (IsRunning())
        {
            return (true, "看门狗已在运行");
        }

        var watchArg = Environment.ProcessPath is { } appPath ? $" --watch \"{appPath}\"" : string.Empty;
        var arguments = "--patrol" + watchArg;

        try
        {
            // 正式版：Guard 和主程序在同一个目录（自包含单文件）
            var sibling = Path.Combine(AppContext.BaseDirectory, "MikoBarrier.Guard.exe");
            if (File.Exists(sibling))
            {
                Start(sibling, arguments, AppContext.BaseDirectory);
                return (true, "看门狗已启动");
            }

            // 开发版：Guard 还是"框架依赖"的，需要用 dotnet 主机来跑
            var devGuard = ResolveDevGuard();
            var host = ResolveDotnetHost();
            if (devGuard is not null && host is not null)
            {
                Start(host, $"\"{devGuard}\" {arguments}", Path.GetDirectoryName(devGuard)!);
                return (true, "看门狗已启动（开发模式）");
            }

            return (false, "未找到 MikoBarrier.Guard.exe，进程巡逻没有启动");
        }
        catch (Exception ex)
        {
            return (false, "看门狗启动失败：" + ex.Message);
        }
    }

    /// <summary>自律结束后调用；看门狗通常已自行退出，这里只是兜底。</summary>
    public static void StopPatrol()
    {
        foreach (var process in Process.GetProcessesByName(GuardProcessName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void Start(string fileName, string arguments, string workingDirectory) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

    private static string? ResolveDevGuard()
    {
        // 优先使用与当前调用方相同的生成配置；当前进程是 Release 时不要误用旧的 Debug 产物。
        var preferDebug = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
        var configurations = preferDebug
            ? new[] { "Debug", "Release" }
            : new[] { "Release", "Debug" };

        var relatives = new List<string>();
        foreach (var configuration in configurations)
        {
            relatives.Add(Path.Combine("MikoBarrier.Guard", "bin", configuration, "net8.0-windows", "MikoBarrier.Guard.dll"));
            relatives.Add(Path.Combine("src", "MikoBarrier.Guard", "bin", configuration, "net8.0-windows", "MikoBarrier.Guard.dll"));
        }

        var ups = new[] { "..", @"..\..", @"..\..\..", @"..\..\..\..", @"..\..\..\..\.." };

        foreach (var up in ups)
        {
            foreach (var relative in relatives)
            {
                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, up, relative));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }
        }

        // 兜底：仓库根目录下 app\ 里已发布的自包含看门狗。
        // 从当前目录逐级向上找，避免写死任何绝对路径。
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory);
             dir is not null;
             dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "app", "MikoBarrier.Guard.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
    private static string? ResolveDotnetHost()
    {
        // 1) 本项目专用覆盖：便携 / 非默认位置安装的 .NET 主机。
        //    例：setx MIKOBARRIER_DOTNET D:\dotnet\dotnet.exe
        var explicitHost = Environment.GetEnvironmentVariable("MIKOBARRIER_DOTNET");
        if (!string.IsNullOrWhiteSpace(explicitHost) && File.Exists(explicitHost))
        {
            return explicitHost;
        }

        // 2) 官方约定的 DOTNET_ROOT。
        var fromEnv = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var candidate = Path.Combine(fromEnv, "dotnet.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 3) 默认安装位置。
        foreach (var programFiles in new[]
                 {
                     Environment.GetEnvironmentVariable("ProgramFiles"),
                     Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                 })
        {
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                continue;
            }

            var candidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), "dotnet.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }
}
