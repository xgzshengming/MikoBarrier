using System.Diagnostics;
using System.Text;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

namespace MikoBarrier.Guard;

/// <summary>
/// 看门狗 + 进程巡逻（v0.3）。
///
/// 用法：
///   MikoBarrier.Guard.exe --patrol --watch "D:\...\MikoBarrier.App.exe" [--interval 1500] [--once]
///
/// 职责：
///   1. 只要 data/session.lock 存在（自律进行中），就保证主程序一直在运行，被强杀立刻拉起；
///   2. 按名单做进程巡逻：黑名单模式拦截名单内程序；白名单模式只拦截"有窗口"的名单外程序；
///      中场休息（phase = Breaking）自动停火；
///   3. 自律结束（session.lock 消失）后自动退出。
/// </summary>
internal static class Program
{
    private static readonly TimeSpan ConfigCacheLifetime = TimeSpan.FromSeconds(10);
    private static AppSettings? _cachedSettings;
    private static DateTime _configLoadedUtc = DateTime.MinValue;
    private static string _lastRepeated = string.Empty;

    private static string _currentSessionKey = string.Empty;
    private static bool _appSeenThisSession;
    private static bool _everHadAppHeartbeat;
    private static DateTime _appHeartbeatMissingSinceUtc = DateTime.MinValue;

    private static int Main(string[] args)
    {
        var watchPath = GetArg(args, "--watch");
        var patrol = args.Any(a => a.Equals("--patrol", StringComparison.OrdinalIgnoreCase));
        var intervalMs = int.TryParse(GetArg(args, "--interval"), out var parsed) ? Math.Max(400, parsed) : 1500;
        var once = args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase));
        var always = args.Any(a => a.Equals("--always", StringComparison.OrdinalIgnoreCase));
        var checkTarget = GetArg(args, "--check");

        if (!string.IsNullOrWhiteSpace(checkTarget))
        {
            return RunCheck(checkTarget);
        }

        if (!patrol && string.IsNullOrWhiteSpace(watchPath))
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  巡逻: MikoBarrier.Guard.exe --patrol --watch \"<MikoBarrier.App.exe 路径>\" [--interval 1500] [--once]");
            Console.WriteLine("  诊断: MikoBarrier.Guard.exe --check \"<exe 路径或进程名>\"");
            return 2;
        }

        Log($"看门狗启动：巡逻={patrol}，监视={(string.IsNullOrWhiteSpace(watchPath) ? "无" : watchPath)}，间隔={intervalMs}ms");

        // 主程序是"先写 session.lock 再启动巡逻"的，但为了稳妥，这里最多等 15 秒。
        if (!always && !WaitForSession(TimeSpan.FromSeconds(15)))
        {
            Log("等待 15 秒仍未发现进行中的自律（session.lock 不存在），看门狗退出。");
            return 0;
        }

        var appName = string.IsNullOrWhiteSpace(watchPath) ? null : Path.GetFileNameWithoutExtension(watchPath);

        while (true)
        {
            if (SessionRecovery.IsGracefulShutdownPending())
            {
                RuntimeHeartbeat.Clear(RuntimeHeartbeat.GuardRole);
                ResetSessionWatchState();
                LogOnce("检测到正常关机 / 注销标记，看门狗停止重启主程序与进程巡逻。");
                if (!always)
                {
                    return 0;
                }

                if (once)
                {
                    return 0;
                }

                Thread.Sleep(intervalMs);
                continue;
            }

            var snapshot = SessionRecovery.TryLoad();
            if (snapshot?.Plan is null)
            {
                RuntimeHeartbeat.Clear(RuntimeHeartbeat.GuardRole);
                ResetSessionWatchState();

                if (!always)
                {
                    Log("没有进行中的自律（session.lock 不存在），看门狗退出。");
                    return 0;
                }

                LogOnce("待命中：等待自律开始...");
                if (once)
                {
                    return 0;
                }

                Thread.Sleep(intervalMs);
                continue;
            }

            var sessionKey = SessionRecovery.GetSessionKey(snapshot);
            if (!string.Equals(_currentSessionKey, sessionKey, StringComparison.Ordinal))
            {
                _currentSessionKey = sessionKey;
                ResetAppWatchState();
            }

            RuntimeHeartbeat.Write(RuntimeHeartbeat.GuardRole, sessionKey);

            if (appName is not null && watchPath is not null)
            {
                CheckAppProcess(appName, watchPath, sessionKey);
            }

            if (patrol)
            {
                if (snapshot.Phase == nameof(SessionPhase.Breaking))
                {
                    LogOnce("中场休息：应用屏蔽已停火，断网档位仍然生效。");
                }
                else
                {
                    TryPatrol(snapshot.Plan);
                }
            }

            if (once)
            {
                return 0;
            }

            Thread.Sleep(intervalMs);
        }
    }

    private static bool WaitForSession(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (SessionRecovery.TryLoad()?.Plan is not null)
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    private static bool _patrolAnnounced;

    /// <summary>
    /// 诊断：把名单规则和"当前正在运行、且与目标匹配的进程"逐条打印出来，
    /// 用来判断"为什么没拦住"是规则没匹配上，还是进程读不到路径，还是权限不够。
    /// </summary>
    private static int RunCheck(string target)
    {
        var settings = ConfigStore.Load();
        var plan = SessionRecovery.TryLoad()?.Plan
                   ?? new FocusPlan { BlockApplications = true, UseWhitelistMode = settings.LastPlan.UseWhitelistMode };

        Log("======= 拦截规则诊断 =======");
        Log($"诊断目标：{target}");
        Log($"名单 {settings.Rules.Count} 条，(按文件名匹配：{(settings.MatchByFileName ? "开" : "关")})：");
        foreach (var rule in settings.Rules)
        {
            Log($"  [{rule.Kind}] {rule.Target}  {rule.Path}");
        }

        Log($"内置保护：系统核心 {settings.SystemWhitelist.Count} 项，白名单组件豁免 {settings.ComponentWhitelist.Count} 项，系统目录 {SystemAllowPolicy.ProtectedDirectories.Count} 个");

        var snapshot = ProcessPatrol.Snapshot();
        var byPid = new Dictionary<int, ProcessInfo>();
        foreach (var process in snapshot)
        {
            byPid[process.Pid] = process;
        }

        var hit = 0;

        foreach (var info in snapshot)
        {
            var matchesTarget =
                info.Name.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                info.FilePath.Contains(target, StringComparison.OrdinalIgnoreCase);

            if (!matchesTarget)
            {
                continue;
            }

            hit++;
            var verdict = RuleEngine.EvaluateProcess(info, byPid, settings, plan);
            Log($"  进程：{info.Name} (PID {info.Pid})");
            Log($"    路径：{(string.IsNullOrEmpty(info.FilePath) ? "读不到（受保护/权限不足）" : info.FilePath)}");
            Log($"    裁决：{(verdict.ShouldBlock ? "会拦截" : "放行")}（{verdict.Reason}）");
        }

        if (hit == 0)
        {
            Log("  没有找到与目标匹配的正在运行的进程。");
        }

        Log($"  读不到路径的进程数：{ProcessPatrol.UnknownPathCount}");
        Log($"  当前是否管理员：{(AdminHelper.IsElevated ? "是" : "否（受保护的游戏可能杀不掉）")}");
        Log("======= 诊断结束 =======");
        return 0;
    }

    private static void TryPatrol(FocusPlan plan)
    {
        try
        {
            var settings = LoadSettingsCached();

            if (!_patrolAnnounced)
            {
                _patrolAnnounced = true;
                var mode = plan.UseWhitelistMode ? "白名单" : "黑名单";
                var action = settings.KillBlockedProcesses ? "结束进程" : "隐藏窗口";
                Log($"进程巡逻已就绪：{mode}模式，规则 {settings.Rules.Count} 条，执行方式：{action}");
            }
            var decisions = ProcessPatrol.Sweep(settings, plan);
            if (decisions.Count == 0)
            {
                return;
            }

            var outcome = ProcessPatrol.ExecuteDetailed(decisions, Log);
            if (outcome.Handled.Count > 0 || outcome.Failures.Count > 0)
            {
                var events = new List<PatrolEvent>();
                events.AddRange(outcome.Handled.Select(d => new PatrolEvent
                {
                    Utc = DateTime.UtcNow,
                    ProcessName = d.Name,
                    FilePath = d.FilePath,
                    Action = d.Action.ToString(),
                    Reason = d.Reason,
                }));

                events.AddRange(outcome.Failures.Select(f => new PatrolEvent
                {
                    Utc = DateTime.UtcNow,
                    ProcessName = f.Decision.Name,
                    FilePath = f.Decision.FilePath,
                    Action = "Failed",
                    Reason = f.Error,
                }));

                PatrolLog.Add(events);
                Log($"本次巡逻：成功 {outcome.Handled.Count} 个，失败 {outcome.Failures.Count} 个。");
            }
        }
        catch (Exception ex)
        {
            Log("巡逻异常：" + ex.Message);
        }
    }

    private static AppSettings LoadSettingsCached()
    {
        if (_cachedSettings is not null && DateTime.UtcNow - _configLoadedUtc < ConfigCacheLifetime)
        {
            return _cachedSettings;
        }

        _cachedSettings = ConfigStore.Load();
        _configLoadedUtc = DateTime.UtcNow;
        return _cachedSettings;
    }

    /// <summary>主程序进程存在性 + 心跳双重检查；确认被 Kill / 失联时写事件并自动拉起。</summary>
    private static void CheckAppProcess(string appName, string watchPath, string sessionKey)
    {
        if (!IsRunning(appName, watchPath))
        {
            if (_appSeenThisSession)
            {
                Log("检测到主程序被结束，记录 Kill 事件并自动续跑...");
                TamperLedger.Record(
                    TamperIncidentKinds.AppKilled,
                    sessionKey,
                    Environment.ProcessId,
                    "Guard 检测到主程序不在运行");
                TryStart(watchPath, killedRestart: true);
            }
            else
            {
                Log("检测到主程序不在运行，正在重新拉起...");
                TryStart(watchPath, killedRestart: false);
            }

            ResetAppWatchState();
            return;
        }

        var heartbeat = RuntimeHeartbeat.Read(RuntimeHeartbeat.AppRole);
        var alive = RuntimeHeartbeat.IsAlive(
            heartbeat,
            "MikoBarrier.App",
            sessionKey,
            TimeSpan.FromSeconds(6),
            out var reason);

        if (alive)
        {
            // 只有“进程存在 + 心跳新鲜”才算真正见过主程序；
            // 拉起后立刻崩溃、还没写心跳就再次消失，不会被反复记成新的 Kill。
            _appSeenThisSession = true;
            _everHadAppHeartbeat = true;
            _appHeartbeatMissingSinceUtc = default;
            return;
        }

        if (!_everHadAppHeartbeat)
        {
            // 刚启动 / 旧版主程序可能还没有心跳文件，先观察，绝不误杀。
            LogOnce("等待主程序心跳...");
            return;
        }

        if (_appHeartbeatMissingSinceUtc == default)
        {
            _appHeartbeatMissingSinceUtc = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _appHeartbeatMissingSinceUtc < TimeSpan.FromSeconds(15))
        {
            return;
        }

        Log("检测到主程序心跳长时间过期（可能卡死），记录 Kill 事件并强制重启...");
        TamperLedger.Record(
            TamperIncidentKinds.AppUnresponsive,
            sessionKey,
            Environment.ProcessId,
            reason);

        try
        {
            foreach (var process in Process.GetProcessesByName(appName))
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
        catch (Exception ex)
        {
            Log("结束无响应主程序失败：" + ex.Message);
        }

        TryStart(watchPath, killedRestart: true);
        ResetAppWatchState();
    }

    private static void ResetAppWatchState()
    {
        _appSeenThisSession = false;
        _everHadAppHeartbeat = false;
        _appHeartbeatMissingSinceUtc = DateTime.MinValue;
    }

    private static void ResetSessionWatchState()
    {
        _currentSessionKey = string.Empty;
        ResetAppWatchState();
    }

    private static bool IsRunning(string processName, string appPath)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) &&
                            string.Equals(path, appPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("枚举进程失败：" + ex.Message);
        }

        return false;
    }

    private static void TryStart(string appPath, bool killedRestart = false)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = appPath,
                Arguments = killedRestart ? "--killed-restart" : string.Empty,
                WorkingDirectory = Path.GetDirectoryName(appPath) ?? string.Empty,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log("拉起主程序失败：" + ex.Message);
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void LogOnce(string message)
    {
        if (_lastRepeated == message)
        {
            return;
        }

        _lastRepeated = message;
        Log(message);
    }

    private static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(line);

        try
        {
            Directory.CreateDirectory(StoragePaths.LogDir);
            File.AppendAllText(StoragePaths.GuardLogFile, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }
}