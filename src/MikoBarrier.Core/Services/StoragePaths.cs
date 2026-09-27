namespace MikoBarrier.Core.Services;

/// <summary>
/// 程序数据目录。优先使用用户指定的 MikoBarrier_HOME，否则在 D/F/E 盘中寻找已有的 MikoBarrier 目录，
/// 最后兜底到 %LocalAppData%（数据尽量不放在系统盘）。
/// </summary>
public static class StoragePaths
{
    public static string Root { get; } = ResolveRoot();

    public static string DataDir => Path.Combine(Root, "data");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    public static string StatsFile => Path.Combine(DataDir, "stats.json");

    public static string SessionLockFile => Path.Combine(DataDir, "session.lock");

    /// <summary>DPAPI CurrentUser 加密保存的恢复码副本。</summary>
    public static string RecoveryCodeFile => Path.Combine(DataDir, "recovery-code.dat");

    /// <summary>界面引导是否看过的状态（只影响新手引导，不参与自律规则）。</summary>
    public static string UiStateFile => Path.Combine(DataDir, "ui-state.json");

    /// <summary>正常关机 / 注销时写入，Guard 看到后不把“主程序退出”当成被 Kill。</summary>
    public static string ShutdownMarkerFile => Path.Combine(DataDir, "shutdown.flag");

    public static string RuntimeDir => Path.Combine(DataDir, "runtime");

    public static string IncidentDir => Path.Combine(DataDir, "incidents");

    public static string NetworkStateFile => Path.Combine(DataDir, "network-state.json");

    public static string PatrolLogFile => Path.Combine(DataDir, "patrol-log.json");

    public static string TasksFile => Path.Combine(DataDir, "tasks.json");

    public static string LogDir => Path.Combine(Root, "logs");

    public static string GuardLogFile => Path.Combine(LogDir, "guard.log");

    public static string AppLogFile => Path.Combine(LogDir, "app.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(RuntimeDir);
        Directory.CreateDirectory(IncidentDir);
    }

    private static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable("MikoBarrier_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        foreach (var drive in new[] { "D", "F", "E" })
        {
            var candidate = $@"{drive}:\MikoBarrier";
            try
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // 忽略无权限的盘符
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MikoBarrier");
    }
}
