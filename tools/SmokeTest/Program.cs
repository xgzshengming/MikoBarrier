using System.Diagnostics;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

var failures = new List<string>();
var passed = 0;
var skippedSections = 0;

// 全部测试数据重定向到临时目录，避免覆盖用户真实的 data\config.json。
// 必须在第一次访问 StoragePaths 之前设置 MikoBarrier_HOME。
var smokeHome = Path.Combine(
    Path.GetTempPath(),
    "MikoBarrier-SmokeTest",
    $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
Environment.SetEnvironmentVariable("MikoBarrier_HOME", smokeHome);

// 检测是否已有用户自己的看门狗在跑：如果有，跳过端到端巡逻，绝不误杀正在进行的自律。
var existingGuard = GuardLauncher.IsRunning();
if (existingGuard)
{
    Console.WriteLine("注意：检测到已有 MikoBarrier.Guard 在运行，将跳过第 10 节端到端巡逻。");
    Console.WriteLine("      其余测试在临时目录隔离运行，不会动你的真实配置；如需完整回归，请先从托盘退出 MikoBarrier。");
    Console.WriteLine();
}

void Check(string name, bool ok)
{
    if (ok)
    {
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failures.Add(name);
        Console.WriteLine($"  FAIL  {name}");
    }
}

// 第 9 / 10 节的真拦截测试需要一个会被结束的「靶子进程」。
// 优先用环境变量显式指定，否则在仓库里找 tools\PatrolVictim 的构建产物
// （dotnet build MikoBarrier.sln -c Release 会一并编译它）。
// 一个都找不到时这两节会明确 SKIP，而不是把「缺靶子」误报成功能失败。
string? ResolvePatrolVictim()
{
    var fromEnv = Environment.GetEnvironmentVariable("MIKOBARRIER_PATROL_VICTIM");
    if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
    {
        return Path.GetFullPath(fromEnv);
    }

    var configurations = new[] { "Release", "Debug" };
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
    {
        foreach (var configuration in configurations)
        {
            var candidate = Path.Combine(
                dir.FullName, "tools", "PatrolVictim", "bin", configuration, "net8.0-windows", "PatrolVictim.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return null;
}

var patrolVictimExe = ResolvePatrolVictim();
var patrolVictimName = patrolVictimExe is null ? "PatrolVictim" : Path.GetFileNameWithoutExtension(patrolVictimExe);

Console.WriteLine("== 1. 密码策略 ==");
Check("15 位密码应被拒绝", !PasswordService.Validate("Abcdef123456!@#").IsValid);
Check("16 位且含四类字符应通过", PasswordService.Validate("Abcdef123456!@#$").IsValid);
Check("纯小写+数字（无大写/特殊字符）应被拒绝", !PasswordService.Validate("abcdef123456!@#$").IsValid);
Check("缺少数字应被拒绝", !PasswordService.Validate("AbcdefGHIJKL!@#$").IsValid);

var vault = new AppSettings();
PasswordService.SetPassword(vault, "Abcdef123456!@#$");
Check("正确密码可验证", PasswordService.VerifyPassword(vault, "Abcdef123456!@#$"));
Check("错误密码不可验证", !PasswordService.VerifyPassword(vault, "Abcdef123456!@#X"));
Check("只保存哈希、不存明文", vault.PasswordHash != "Abcdef123456!@#$" && vault.PasswordSalt.Length > 0);

var recovery = PasswordService.GenerateRecoveryCode();
PasswordService.SetRecoveryCode(vault, recovery);
Check("恢复码可验证", PasswordService.VerifyRecoveryCode(vault, recovery.ToLowerInvariant()));
Check("错误恢复码不可验证", !PasswordService.VerifyRecoveryCode(vault, "AAAA-BBBB-CCCC-DDDD"));

Console.WriteLine("== 2. 时长与轮数上限 ==");
var wild = new FocusPlan { FocusMinutes = 999, Rounds = 99, BreakMinutes = 10 };
Check("专注时长被限制在 120 分钟", wild.ClampFocusMinutes() == 120);
Check("轮数被限制在 12 轮", wild.ClampRounds() == 12);
Check("单轮时中场休息无效", !new FocusPlan { Rounds = 1, BreakMinutes = 10 }.HasBreak);
Check("多轮时中场休息有效", new FocusPlan { Rounds = 3, BreakMinutes = 5 }.HasBreak);

Console.WriteLine("== 3. 会话状态机 / 中断欠债 ==");
var engine = new SessionEngine();
SessionRecord? ended = null;
engine.SessionEnded += (_, record) => ended = record;

var settings = new AppSettings();
settings.EnsureDefaults();
engine.Start(settings, new FocusPlan { FocusMinutes = 1, Rounds = 2, BreakMinutes = 5 });
Check("开始后处于专注阶段", engine.Phase == SessionPhase.Focusing && engine.IsActive);
Check("第一轮计时 60 秒", Math.Abs(engine.PhaseTotal.TotalSeconds - 60) < 1);
Check("债务清零", settings.DebtSeconds == 0);

engine.Abort(settings, "单元测试中断");
Check("中断后不再是活动会话", !engine.IsActive && engine.Phase == SessionPhase.Aborted);
Check("中断产生欠债（剩余 2 轮 ≈ 120 秒）", Math.Abs(settings.DebtSeconds - 120) <= 2);
Check("中断记录标记为未完成", ended is { Completed: false });
Check("中断不计入完成轮数", ended!.RoundsCompleted == 0);

var second = new SessionEngine();
second.Start(settings, new FocusPlan { FocusMinutes = 1, Rounds = 1 });
Check("欠债顺延到下一次第一轮（60 + 120 = 180 秒）",
    Math.Abs(second.PhaseTotal.TotalSeconds - 180) < 1);
Check("开始后欠债清零", settings.DebtSeconds == 0);
second.Reset();

Console.WriteLine("== 4. 名单裁决 ==");
var ruleSettings = new AppSettings();
ruleSettings.EnsureDefaults();
ruleSettings.Rules.Add(new AppRule { Kind = RuleKind.Blacklist, Target = RuleTarget.File, Path = @"C:\Games\game.exe" });
ruleSettings.Rules.Add(new AppRule { Kind = RuleKind.Blacklist, Target = RuleTarget.Folder, Path = @"D:\SteamLibrary" });
ruleSettings.Rules.Add(new AppRule { Kind = RuleKind.Whitelist, Target = RuleTarget.ProcessName, Path = "notepad" });

var blackPlan = new FocusPlan { BlockApplications = true, UseWhitelistMode = false };
Check("黑名单命中的程序被拦截", RuleEngine.Evaluate("game", @"C:\Games\game.exe", ruleSettings, blackPlan).ShouldBlock);
Check("黑名单目录下的程序被拦截", RuleEngine.Evaluate("cs2", @"D:\SteamLibrary\cs2\cs2.exe", ruleSettings, blackPlan).ShouldBlock);
Check("不在黑名单里的程序放行", !RuleEngine.Evaluate("calc", @"C:\Windows\System32\calc.exe", ruleSettings, blackPlan).ShouldBlock);
Check("系统进程 explorer 永远放行", !RuleEngine.Evaluate("explorer", @"C:\Windows\explorer.exe", ruleSettings, blackPlan).ShouldBlock);
Check("MikoBarrier 自身永远放行", !RuleEngine.Evaluate("MikoBarrier.App", @"C:\Apps\MikoBarrier\MikoBarrier.App.exe", ruleSettings, blackPlan).ShouldBlock);

var whitePlan = new FocusPlan { BlockApplications = true, UseWhitelistMode = true };
Check("白名单模式拦截名单外程序", RuleEngine.Evaluate("steam", @"C:\Steam\steam.exe", ruleSettings, whitePlan).ShouldBlock);
Check("白名单模式放行名单内程序", !RuleEngine.Evaluate("notepad", @"C:\Windows\System32\notepad.exe", ruleSettings, whitePlan).ShouldBlock);
Check("白名单模式仍放行系统进程", !RuleEngine.Evaluate("dwm", @"C:\Windows\System32\dwm.exe", ruleSettings, whitePlan).ShouldBlock);

Console.WriteLine("== 5. JSON 持久化 ==");
StoragePaths.EnsureCreated();
var probePath = Path.Combine(StoragePaths.DataDir, "smoke-probe.json");
var probe = new AppSettings { Theme = ThemeKind.Light, DebtSeconds = 42 };
probe.Rules.Add(new AppRule { DisplayName = "测试程序", Path = @"C:\测试 中文路径\x.exe" });
probe.BlockedSites.Add("bilibili.com");
JsonStore.Save(probePath, probe);
var loaded = JsonStore.Load(probePath, static () => new AppSettings());
Check("主题可往返", loaded.Theme == ThemeKind.Light);
Check("欠债秒数可往返", loaded.DebtSeconds == 42);
Check("中文规则名可往返", loaded.Rules.Count == 1 && loaded.Rules[0].DisplayName == "测试程序");
Check("网站名单可往返", loaded.BlockedSites.Contains("bilibili.com"));
File.Delete(probePath);

Console.WriteLine("== 6. 断网档位（温和档 / 狠人档） ==");
var tierOff = new FocusPlan { NetworkTier = NetworkTier.Off };
Check("不断网：两个开关都关闭", !tierOff.BlocksSites && !tierOff.BlocksAdapters);
var tierGentle = new FocusPlan { NetworkTier = NetworkTier.Gentle };
Check("温和档：屏蔽网站但不禁用网卡", tierGentle.BlocksSites && !tierGentle.BlocksAdapters);
var tierHardcore = new FocusPlan { NetworkTier = NetworkTier.Hardcore };
Check("狠人档：屏蔽网站且禁用网卡", tierHardcore.BlocksSites && tierHardcore.BlocksAdapters);
Check("狠人档文案正确", tierHardcore.ToString().Contains("狠人档"));
Check("温和档文案正确", tierGentle.ToString().Contains("温和档"));

Console.WriteLine("== 7. 安全问题（忘记密码） ==");
var secure = new AppSettings();
secure.EnsureDefaults();
PasswordService.SetSecurityQuestions(secure, new List<(string Question, string Answer)>
{
    ("你小学班主任的名字？", "王老师"),
    ("最喜欢的动漫角色？", "Miko"),
    ("第一台电脑的品牌？", "Lenovo"),
});
Check("3 个问题已保存", secure.HasSecurityQuestions);
Check("答案全对可验证（忽略大小写）", PasswordService.VerifySecurityAnswers(secure, new[] { "王老师", "miko", "LENOVO" }));
Check("答案忽略空格", PasswordService.VerifySecurityAnswers(secure, new[] { " 王 老师 ", "M I K O", "lenovo" }));
Check("答错其中一题不通过", !PasswordService.VerifySecurityAnswers(secure, new[] { "王老师", "Miko", "Apple" }));
Check("只回答 2 题不通过", !PasswordService.VerifySecurityAnswers(secure, new[] { "王老师", "Miko" }));
Check("答案只存哈希不存明文", secure.SecurityQuestions.All(q => q.AnswerHash.Length > 0 && q.AnswerHash != "Miko"));
Check("重设密码后原密码失效", ResetAndCheck(secure));

bool ResetAndCheck(AppSettings s)
{
    var before = s.PasswordHash;
    PasswordService.SetPassword(s, "NewPassw0rd!@#$%");
    return s.PasswordHash != before && PasswordService.VerifyPassword(s, "NewPassw0rd!@#$%");
}

Console.WriteLine("== 8. 崩溃恢复快照（断电 / 崩溃可继续） ==");
var snapSettings = new AppSettings();
snapSettings.EnsureDefaults();
var snapEngine = new SessionEngine();
snapEngine.Start(snapSettings, new FocusPlan { FocusMinutes = 2, Rounds = 2, BreakMinutes = 5 });

var snapshot = snapEngine.CaptureSnapshot();
Check("快照包含完整计划", snapshot is { IsValid: true });
Check("快照剩余时间约 120 秒", Math.Abs(snapshot!.PhaseSecondsRemaining - 120) < 2);

var resumed = new SessionEngine();
Check("可以用快照恢复", resumed.Resume(new AppSettings(), snapshot));
Check("恢复后处于专注且计时继续", resumed.IsActive && resumed.Phase == SessionPhase.Focusing);
Check("恢复后剩余时间一致", Math.Abs(resumed.PhaseTotal.TotalSeconds - snapshot.PhaseSecondsRemaining) < 2);
Check("恢复后轮次一致", resumed.TotalRounds == 2 && resumed.CurrentRound == 1);
resumed.Reset();
Check("Reset 后回到空闲", !resumed.IsActive && resumed.Plan is null);

JsonStore.Save(StoragePaths.SessionLockFile, snapshot);
Check("session.lock 可以读回快照", SessionRecovery.TryLoad() is { IsValid: true });
SessionRecovery.Discard();
Check("丢弃后 session.lock 消失", SessionRecovery.TryLoad() is null);

Console.WriteLine();
Console.WriteLine("== 9. 进程巡逻（真拦截） ==");
var patrolSettings = new AppSettings();
patrolSettings.EnsureDefaults();
var patrolPlan = new FocusPlan { BlockApplications = true, UseWhitelistMode = false };

if (patrolVictimExe is null)
{
    skippedSections++;
    Console.WriteLine("  SKIP  未找到靶子进程，本节跳过 3 项真拦截断言。");
    Console.WriteLine("        先执行  dotnet build MikoBarrier.sln -c Release （会一并编译 tools\\PatrolVictim），");
    Console.WriteLine("        或用环境变量 MIKOBARRIER_PATROL_VICTIM 指定一个长驻程序。");
}
else
{
    var victim = Process.Start(new ProcessStartInfo(patrolVictimExe, "-n 300 127.0.0.1")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
    });
    Thread.Sleep(1500);

    var victimPath = string.Empty;
    try
    {
        victimPath = victim?.MainModule?.FileName ?? string.Empty;
    }
    catch
    {
    }

    Check("能读取测试进程路径", victimPath.Length > 0);

    patrolSettings.Rules.Add(new AppRule { Kind = RuleKind.Blacklist, Target = RuleTarget.File, Path = victimPath });

    var sweep = ProcessPatrol.Sweep(patrolSettings, patrolPlan);
    Check("巡逻把黑名单进程标记为 Kill", sweep.Any(d => d.Pid == victim!.Id && d.Action == PatrolActionKind.Kill));

    ProcessPatrol.Execute(sweep.Where(d => d.Pid == victim!.Id), null);
    Check("黑名单进程确实被结束", victim!.WaitForExit(5000));
}

var whiteSettings = new AppSettings();
whiteSettings.EnsureDefaults();
var patrolWhitePlan = new FocusPlan { BlockApplications = true, UseWhitelistMode = true };
var backgroundProbe = new List<ProcessInfo> { new(999999, "somebackground", @"C:\fake\somebackground.exe", string.Empty) };
Check("白名单模式不误杀无窗口的后台进程", ProcessPatrol.Sweep(whiteSettings, patrolWhitePlan, backgroundProbe).Count == 0);

var protectedProbe = new List<ProcessInfo>
{
    new(1, "explorer", @"C:\Windows\explorer.exe", "Program Manager"),
    new(2, "MikoBarrier.App", @"C:\Apps\MikoBarrier\MikoBarrier.App.exe", "MikoBarrier"),
    new(3, "svchost", @"C:\Windows\System32\svchost.exe", string.Empty),
};
Check("系统进程与自身永不被巡逻处理", ProcessPatrol.Sweep(patrolSettings, patrolPlan, protectedProbe).Count == 0);
var noPathProbe = new List<ProcessInfo> { new(888888, "game", string.Empty, "Game Window") };
var noPathSettings = new AppSettings();
noPathSettings.EnsureDefaults();
noPathSettings.Rules.Add(new AppRule { Kind = RuleKind.Blacklist, Target = RuleTarget.File, Path = @"D:\Games\game.exe" });
Check("读不到路径的进程按文件名兜底拦截（游戏类）", ProcessPatrol.Sweep(noPathSettings, patrolPlan, noPathProbe).Count == 1);
var nameProbe = new List<ProcessInfo> { new(777777, "foo", @"D:\Games\foo.exe", "Foo Game") };

var nameSettings = new AppSettings();
nameSettings.EnsureDefaults();
nameSettings.Rules.Add(new AppRule { Kind = RuleKind.Blacklist, Target = RuleTarget.File, Path = @"C:\Launcher\foo.exe" });
Check("同一个程序换目录也能拦（按文件名）", ProcessPatrol.Sweep(nameSettings, patrolPlan, nameProbe).Count == 1);

var strictSettings = new AppSettings();
strictSettings.EnsureDefaults();
strictSettings.MatchByFileName = false;
strictSettings.Rules.Add(new AppRule { Kind = RuleKind.Blacklist, Target = RuleTarget.File, Path = @"C:\Launcher\foo.exe" });
Check("关掉文件名匹配后只认完整路径", ProcessPatrol.Sweep(strictSettings, patrolPlan, nameProbe).Count == 0);



if (existingGuard)
{
    Console.WriteLine("  SKIP  端到端巡逻：检测到已有 MikoBarrier.Guard 在运行。");
    Console.WriteLine("        请先从托盘退出 MikoBarrier，再重新运行 SmokeTest 以获得完整回归。");
}
else if (patrolVictimExe is null)
{
    skippedSections++;
    Console.WriteLine("== 10. 看门狗端到端巡逻（真拦截） ==");
    Console.WriteLine("  SKIP  未找到靶子进程，本节跳过 4 项端到端断言（原因同第 9 节）。");
}
else
{

Console.WriteLine("== 10. 看门狗端到端巡逻（真拦截） ==");
var e2eVictim = patrolVictimExe;
var configBackup = StoragePaths.ConfigFile + ".test-backup";
var hadConfig = File.Exists(StoragePaths.ConfigFile);
if (hadConfig)
{
    File.Copy(StoragePaths.ConfigFile, configBackup, true);
}

try
{
    var e2eSettings = new AppSettings();
    e2eSettings.EnsureDefaults();
    e2eSettings.Rules.Add(new AppRule { Kind = RuleKind.Blacklist, Target = RuleTarget.File, Path = e2eVictim });
    ConfigStore.Save(e2eSettings);

    JsonStore.Save(StoragePaths.SessionLockFile, new SessionSnapshot
    {
        Plan = new FocusPlan { FocusMinutes = 5, Rounds = 1, BlockApplications = true, UseWhitelistMode = false },
        Phase = nameof(SessionPhase.Focusing),
        CurrentRound = 1,
        TotalRounds = 1,
        PhaseSecondsRemaining = 300,
        StartedUtc = DateTime.UtcNow,
        SavedUtc = DateTime.UtcNow,
    });

    var e2eProcess = Process.Start(new ProcessStartInfo(e2eVictim, "-n 300 127.0.0.1")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
    });

    Thread.Sleep(1200);
    Check("端到端：靶子进程已启动", e2eProcess is { HasExited: false });

    var (guardOk, guardMessage) = GuardLauncher.TryStartPatrol();
    Check("端到端：看门狗能启动（" + guardMessage + "）", guardOk);

    var expectedSessionKey = SessionRecovery.GetSessionKey(JsonStore.Load<SessionSnapshot?>(
        StoragePaths.SessionLockFile,
        static () => null));
    HeartbeatRecord? guardHeartbeat = null;
    var heartbeatDeadline = DateTime.UtcNow.AddSeconds(6);
    while (DateTime.UtcNow < heartbeatDeadline)
    {
        var heartbeatProbe = RuntimeHeartbeat.Read(RuntimeHeartbeat.GuardRole);
        if (heartbeatProbe is not null && string.Equals(heartbeatProbe.SessionId, expectedSessionKey, StringComparison.Ordinal))
        {
            guardHeartbeat = heartbeatProbe;
            break;
        }

        Thread.Sleep(200);
    }

    Check("端到端：看门狗写出互保心跳（用于 App 侧检测看门狗被 Kill）",
        guardHeartbeat is not null);

    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (DateTime.UtcNow < deadline && e2eProcess is { HasExited: false })
    {
        Thread.Sleep(400);
    }

    Check("端到端：自律期间看门狗真的把黑名单进程杀掉了", e2eProcess is { HasExited: true });
    Thread.Sleep(800);
    Check("端到端：拦截明细已写入 patrol-log（用于结束战报）",
        PatrolLog.Load().Any(e => e.ProcessName.StartsWith(patrolVictimName, StringComparison.OrdinalIgnoreCase)));

}
finally
{
    GuardLauncher.StopPatrol();
    SessionRecovery.Discard();
    if (hadConfig)
    {
        File.Copy(configBackup, StoragePaths.ConfigFile, true);
        File.Delete(configBackup);
    }
    else if (File.Exists(StoragePaths.ConfigFile))
    {
        File.Delete(StoragePaths.ConfigFile);
    }
}
}


Console.WriteLine("== 11. 拦截战报 ==");
PatrolLog.Clear();
Check("清空后没有拦截记录", PatrolLog.Load().Count == 0);

PatrolLog.Add(new List<PatrolEvent>
{
    new() { ProcessName = "game", Action = "Kill", Reason = "命中黑名单" },
    new() { ProcessName = "steam", Action = "Kill", Reason = "命中黑名单" },
});
Check("拦截记录能写入并读回", PatrolLog.Load().Count == 2);
Check("战报包含程序名与次数", PatrolLog.Summarize(PatrolLog.Load()).Contains("game") && PatrolLog.Summarize(PatrolLog.Load()).Contains("2"));
Check("无拦截时的战报文案正确", PatrolLog.Summarize(new List<PatrolEvent>()).Contains("没有拦截"));
PatrolLog.Add(new List<PatrolEvent> { new() { ProcessName = "launcher", Action = "Failed", Reason = "Access Denied" } });
Check("战报会提示拦截失败（权限不足）", PatrolLog.Summarize(PatrolLog.Load()).Contains("失败"));
Check("失败不计入成功拦截次数", PatrolLog.Load().Count(e => e.Action != "Failed") == 2);

PatrolLog.Clear();

Console.WriteLine("== 12. 键盘封锁策略 ==");
Check("Alt+Tab 会被吞掉", KeyboardPolicy.Evaluate(KeyboardPolicy.VkTab, true, false, false, false) == BlockedKeyKind.AltTab);
Check("普通 Tab 不受影响", KeyboardPolicy.Evaluate(KeyboardPolicy.VkTab, false, false, false, false) == BlockedKeyKind.None);
Check("Alt+Esc 会被吞掉", KeyboardPolicy.Evaluate(KeyboardPolicy.VkEscape, true, false, false, false) == BlockedKeyKind.AltEsc);
Check("Ctrl+Esc 会被吞掉", KeyboardPolicy.Evaluate(KeyboardPolicy.VkEscape, false, true, false, false) == BlockedKeyKind.CtrlEsc);
Check("Ctrl+Shift+Esc（任务管理器）会被吞掉", KeyboardPolicy.Evaluate(KeyboardPolicy.VkEscape, false, true, true, false) == BlockedKeyKind.TaskManager);
Check("Win 键被策略识别为需放行（由 Blocker 放行）", KeyboardPolicy.Evaluate(KeyboardPolicy.VkLeftWindows, false, false, false, true) == BlockedKeyKind.WindowsKey);
Check("右键 Win 键被策略识别为需放行（由 Blocker 放行）", KeyboardPolicy.Evaluate(KeyboardPolicy.VkRightWindows, false, false, false, true) == BlockedKeyKind.WindowsKey);
Check("Alt+F4 会被吞掉", KeyboardPolicy.Evaluate(KeyboardPolicy.VkF4, true, false, false, false) == BlockedKeyKind.AltF4);
Check("普通 F4 不受影响", KeyboardPolicy.Evaluate(KeyboardPolicy.VkF4, false, false, false, false) == BlockedKeyKind.None);
Check("密码框还能正常打字（字母不受影响）", KeyboardPolicy.Evaluate(0x41, false, false, false, false) == BlockedKeyKind.None);
Check("Ctrl+C / Ctrl+V 不受影响", KeyboardPolicy.Evaluate(0x43, false, true, false, false) == BlockedKeyKind.None && KeyboardPolicy.Evaluate(0x56, false, true, false, false) == BlockedKeyKind.None);
Check("计划文案会写出键盘封锁", new FocusPlan { BlockKeyboard = true }.ToString().Contains("键盘封锁"));

Console.WriteLine();
Console.WriteLine("== 13. 白名单系统保护（脏机兼容） ==");

static AppSettings W13Settings()
{
    var s = new AppSettings();
    s.EnsureDefaults();
    return s;
}

static ProcessInfo W13Proc(int pid, string name, string path, string title = "", int parent = 0) =>
    new(pid, name, path, title, parent);

static AppSettings W13SettingsWithBlacklist(string path)
{
    var s = W13Settings();
    s.Rules.Add(new AppRule { Kind = RuleKind.Blacklist, Target = RuleTarget.File, Path = path });
    return s;
}

// 从内置保护名单里按前缀取名字，避免测试程序集里出现完整的杀软产品名字面量
// （某些杀软会对“包含杀软名单 + 进程终止逻辑”的测试程序误报并直接隔离 DLL）。
static string W13Component(string prefix) =>
    AppSettings.DefaultComponentWhitelist.First(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

static string W13System(string prefix) =>
    AppSettings.DefaultSystemWhitelist.First(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

var w13Plan = new FocusPlan { BlockApplications = true, UseWhitelistMode = true };
var w13Black = new FocusPlan { BlockApplications = true, UseWhitelistMode = false };

// ---- 13.1 系统目录保护 ----
var w13Sys = W13Settings();
Check("System32 里的程序白名单模式放行（cmd）",
    !RuleEngine.Evaluate("cmd", @"C:\Windows\System32\cmd.exe", w13Sys, w13Plan).ShouldBlock);
Check("SysWOW64 里的程序放行",
    !RuleEngine.Evaluate("legacy", @"C:\Windows\SysWOW64\legacy.exe", w13Sys, w13Plan).ShouldBlock);
Check("WinSxS 里的程序放行",
    !RuleEngine.Evaluate("sxs", @"C:\Windows\WinSxS\amd64_microsoft-windows-shell_31bf3856ad364e35\sxs.exe", w13Sys, w13Plan).ShouldBlock);
Check("SystemApps 里的外壳程序放行（开始菜单宿体）",
    !RuleEngine.Evaluate("StartMenuExperienceHost", @"C:\Windows\SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe", w13Sys, w13Plan).ShouldBlock);
Check("System32\\DriverStore 里的驱动组件放行（显卡 / 音频）",
    !RuleEngine.Evaluate("igfxEM", @"C:\Windows\System32\DriverStore\FileRepository\igdlh64.inf_amd64_abc\igfxEM.exe", w13Sys, w13Plan).ShouldBlock);
var w13MpCmd = W13System("mpc");
var w13DefenderPf = SystemAllowPolicy.ProtectedDirectories.FirstOrDefault(d =>
    d.Contains("Program Files", StringComparison.OrdinalIgnoreCase) &&
    d.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase));
var w13DefenderPd = SystemAllowPolicy.ProtectedDirectories.FirstOrDefault(d =>
    d.Contains("ProgramData", StringComparison.OrdinalIgnoreCase) &&
    d.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase));
Check("Windows Defender 目录放行（Program Files）",
    w13DefenderPf is not null &&
    !RuleEngine.Evaluate(w13MpCmd, Path.Combine(w13DefenderPf, w13MpCmd + ".exe"), w13Sys, w13Plan).ShouldBlock);
Check("Windows Defender 平台目录放行（ProgramData）",
    w13DefenderPd is not null &&
    !RuleEngine.Evaluate(w13MpCmd, Path.Combine(w13DefenderPd, "Platform", "4.18.24080.9-0", w13MpCmd + ".exe"), w13Sys, w13Plan).ShouldBlock);
Check("Windows\\Temp 不放行（避免恶意程序藏在可写目录）",
    RuleEngine.Evaluate("evil", @"C:\Windows\Temp\evil.exe", w13Sys, w13Plan).ShouldBlock);
Check("路径里的 .. 会被规范化（System32\\..\\Temp 不放行）",
    RuleEngine.Evaluate("evil", @"C:\Windows\System32\..\Temp\evil.exe", w13Sys, w13Plan).ShouldBlock);
Check("WindowsApps 商店应用不放行（Spotify 仍可被拦截）",
    RuleEngine.Evaluate("Spotify", @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.234.5_x64__kzf8qxf38zg5c\Spotify.exe", w13Sys, w13Plan).ShouldBlock);
Check("下载目录里的游戏不放行",
    RuleEngine.Evaluate("game", @"C:\Users\me\Downloads\game.exe", w13Sys, w13Plan).ShouldBlock);
Check("系统目录保护只在白名单模式生效（黑名单仍可主动屏蔽 System32 小工具）",
    RuleEngine.Evaluate("notepad", @"C:\Windows\System32\notepad.exe",
        W13SettingsWithBlacklist(@"C:\Windows\System32\notepad.exe"), w13Black).ShouldBlock);
Check("系统目录保护清单已展开可用（>= 10 个目录）",
    SystemAllowPolicy.ProtectedDirectories.Count >= 10);

// ---- 13.2 内置组件豁免（输入法 / 显卡 / 音频 / 杀软） ----
var w13Comp = W13Settings();
var w13Ime = W13Component("sogou");
Check("第三方输入法白名单模式放行（取内置名单首项）",
    !RuleEngine.Evaluate(w13Ime, $@"C:\ComponentTest\Ime\{w13Ime}.exe", w13Comp, w13Plan).ShouldBlock);
var w13Qq = W13Component("qqpinyin");
Check("QQ 输入法放行",
    !RuleEngine.Evaluate(w13Qq, $@"C:\ComponentTest\Ime\{w13Qq}.exe", w13Comp, w13Plan).ShouldBlock);
var w13Gpu = W13Component("nvtray");
var w13GpuPath = $@"C:\ComponentTest\Gpu\{w13Gpu}.exe";
Check("显卡组件放行（取内置名单首项）",
    !RuleEngine.Evaluate(w13Gpu, w13GpuPath, w13Comp, w13Plan).ShouldBlock);
var w13Audio = W13Component("rtkaud");
Check("音频组件放行（取内置名单首项）",
    !RuleEngine.Evaluate(w13Audio, $@"C:\ComponentTest\Audio\{w13Audio}.exe", w13Comp, w13Plan).ShouldBlock);
var w13Security = W13Component("hipsd");
Check("第三方杀软组件放行（取内置名单首项）",
    !RuleEngine.Evaluate(w13Security, $@"C:\ComponentTest\Security\{w13Security}.exe", w13Comp, w13Plan).ShouldBlock);
var w13Security2 = W13Component("360");
Check("另一款安全软件放行（取内置名单首项）",
    !RuleEngine.Evaluate(w13Security2, $@"C:\ComponentTest\Security\{w13Security2}.exe", w13Comp, w13Plan).ShouldBlock);
var w13Av = W13Component("av");
Check("国外杀软组件放行（取内置名单首项）",
    !RuleEngine.Evaluate(w13Av, $@"C:\ComponentTest\Security\{w13Av}.exe", w13Comp, w13Plan).ShouldBlock);
Check("不在内置组件里的普通程序仍会被拦截",
    RuleEngine.Evaluate("game", @"C:\Games\game.exe", w13Comp, w13Plan).ShouldBlock);
Check("组件豁免不影响黑名单模式（仍可主动屏蔽，例如换回微软输入法）",
    RuleEngine.Evaluate(w13Gpu, w13GpuPath, W13SettingsWithBlacklist(w13GpuPath), w13Black).ShouldBlock);

// ---- 13.3 启动链路（父子 / 祖先） ----
var w13Chain = W13Settings();
w13Chain.Rules.Add(new AppRule { Kind = RuleKind.Whitelist, Target = RuleTarget.File, Path = @"C:\Tools\launcher.exe" });
var w13ChainList = new List<ProcessInfo>
{
    W13Proc(100, "launcher", @"C:\Tools\launcher.exe", "Launcher"),
    W13Proc(101, "helper", @"C:\Tools\helper.exe", "Helper", 100),
    W13Proc(102, "grandchild", @"C:\Other\grandchild.exe", "Grandchild", 101),
    W13Proc(103, "game", @"C:\Games\game.exe", "Game"),
    W13Proc(104, "explorer", @"C:\Windows\explorer.exe"),
    W13Proc(105, "explorer-child", @"C:\Games\explorer-child.exe", "Game2", 104),
    W13Proc(106, "cmd", @"C:\Windows\System32\cmd.exe"),
    W13Proc(107, "cmd-child", @"C:\Games\cmd-child.exe", "Game3", 106),
    W13Proc(108, "cyclic-a", @"C:\X\a.exe", "A", 109),
    W13Proc(109, "cyclic-b", @"C:\X\b.exe", "B", 108),
    W13Proc(110, "orphan", @"C:\Games\orphan.exe", "Game4", 999999),
};
var w13ChainMap = w13ChainList.ToDictionary(p => p.Pid);
Check("白名单启动器的直接子程序放行（整条启动链路）",
    !RuleEngine.EvaluateProcess(w13ChainList[1], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
Check("白名单启动器的孙程序也放行",
    !RuleEngine.EvaluateProcess(w13ChainList[2], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
Check("链路中间层即使自己有窗口、会被拦截，也不截断白名单祖先",
    RuleEngine.Evaluate("helper", @"C:\Tools\helper.exe", w13Chain, w13Plan).ShouldBlock &&
    !RuleEngine.EvaluateProcess(w13ChainList[2], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
Check("没有白名单祖先的游戏仍被拦截",
    RuleEngine.EvaluateProcess(w13ChainList[3], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
Check("explorer 启动的程序不会因为 explorer 是系统进程就自动放行",
    RuleEngine.EvaluateProcess(w13ChainList[5], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
Check("cmd 启动的程序不会自动放行（防止命令行绕过白名单）",
    RuleEngine.EvaluateProcess(w13ChainList[7], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
Check("System32 的 cmd 自身按系统目录放行",
    !RuleEngine.EvaluateProcess(w13ChainList[6], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
Check("环形父子关系不会死循环（仍判为拦截）",
    RuleEngine.EvaluateProcess(w13ChainList[8], w13ChainMap, w13Chain, w13Plan).ShouldBlock &&
    RuleEngine.EvaluateProcess(w13ChainList[9], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
Check("父进程已退出（读不到父进程）时不会误放行",
    RuleEngine.EvaluateProcess(w13ChainList[10], w13ChainMap, w13Chain, w13Plan).ShouldBlock);
var w13Folder = W13Settings();
w13Folder.Rules.Add(new AppRule { Kind = RuleKind.Whitelist, Target = RuleTarget.Folder, Path = @"C:\WorkApps" });
var w13FolderList = new List<ProcessInfo>
{
    W13Proc(200, "work", @"C:\WorkApps\work.exe", "Work"),
    W13Proc(201, "work-helper", @"C:\Other\helper.exe", "Helper", 200),
};
Check("启动链路支持文件夹白名单规则",
    !RuleEngine.EvaluateProcess(w13FolderList[1], w13FolderList.ToDictionary(p => p.Pid), w13Folder, w13Plan).ShouldBlock);

// ---- 13.4 多套「脏机」档案（从内置名单动态取样 + 通用名字，避免测试 exe 堆真实进程名） ----
var w13SysSample = AppSettings.DefaultSystemWhitelist.Take(14).ToList();
var w13CompSample = AppSettings.DefaultComponentWhitelist.Take(12).ToList();

var w13Dirty = W13Settings();
w13Dirty.Rules.Add(new AppRule { Kind = RuleKind.Whitelist, Target = RuleTarget.File, Path = @"C:\WorkApps\workapp.exe" });
var w13DirtyList = new List<ProcessInfo>();
var w13Pid = 1000;
foreach (var name in w13SysSample)
{
    w13DirtyList.Add(W13Proc(w13Pid++, name, $@"C:\Windows\System32\{name}.exe", string.Empty, 0));
}
foreach (var name in w13CompSample)
{
    w13DirtyList.Add(W13Proc(w13Pid++, name, $@"C:\Program Files\VendorApp\{name}.exe", "tray", 0));
}
w13DirtyList.Add(W13Proc(w13Pid++, "workapp", @"C:\WorkApps\workapp.exe", "work window", 0));
w13DirtyList.Add(W13Proc(w13Pid++, "blockedapp", @"C:\TestApps\blockedapp.exe", "blocked window", 0));
w13DirtyList.Add(W13Proc(w13Pid++, "backgroundsvc", @"C:\Program Files\VendorApp\backgroundsvc.exe", string.Empty, 0));
w13DirtyList.Add(W13Proc(w13Pid++, "taskrunner", @"C:\Windows\System32\taskhostw.exe", string.Empty, 0));
w13DirtyList.Add(W13Proc(w13Pid++, "systrayhelper", @"C:\Windows\System32\systrayhelper.exe", string.Empty, 0));
w13DirtyList.Add(W13Proc(w13Pid++, "vendor-updater", @"C:\Program Files\VendorApp\updater.exe", string.Empty, 0));
var w13DirtyDecisions = ProcessPatrol.Sweep(w13Dirty, w13Plan, w13DirtyList);
Check("脏机档案：只有名单外的有窗口程序被拦截",
    w13DirtyDecisions.Count == 1 &&
    w13DirtyDecisions[0].Name.Equals("blockedapp", StringComparison.OrdinalIgnoreCase));
Check("脏机档案：内置系统 / 组件名单里的样例全部放行（即使有托盘窗口）",
    !w13DirtyDecisions.Any(d => w13SysSample.Contains(d.Name) || w13CompSample.Contains(d.Name)));
Check("脏机档案：白名单程序 / 系统目录组件 / 无窗口后台 / 计划任务宿主不被拦截",
    !w13DirtyDecisions.Any(d => d.Name is "workapp" or "systrayhelper" or "backgroundsvc" or "taskrunner" or "vendor-updater"));
Check("脏机档案：必要宿主自检无警告",
    WhitelistAudit.Run(w13Dirty, w13Plan, w13DirtyList).HostWarnings.Count == 0);

var w13Gamer = W13Settings();
w13Gamer.Rules.Add(new AppRule { Kind = RuleKind.Whitelist, Target = RuleTarget.File, Path = @"C:\Games\launcher.exe" });
var w13GamerList = new List<ProcessInfo>
{
    W13Proc(2000, "launcher", @"C:\Games\launcher.exe", "Launcher"),
    W13Proc(2001, "helper", @"C:\Games\helper.exe", "Helper", 2000),
    W13Proc(2002, "game", @"C:\Games\game.exe", "Game", 2001),
    W13Proc(2003, "anticheat", @"C:\Games\anticheat.exe", string.Empty, 2002),
    W13Proc(2004, "capture", @"C:\Tools\capture.exe", "Capture", 0),
    W13Proc(2005, "chat", @"C:\Tools\chat.exe", "Chat", 0),
    W13Proc(2006, "othergame", @"C:\Games\other.exe", "Other", 0),
    W13Proc(2007, "gamehelper", @"C:\Games\gamehelper.exe", string.Empty, 2001),
};
var w13GamerDecisions = ProcessPatrol.Sweep(w13Gamer, w13Plan, w13GamerList);
Check("游戏档案：白名单启动器的整条链路（helper / 游戏 / 反作弊）放行",
    !w13GamerDecisions.Any(d => d.Name is "launcher" or "helper" or "game" or "anticheat" or "gamehelper"));
Check("游戏档案：名单外的 capture / chat / 其他游戏仍被拦截",
    w13GamerDecisions.Count == 3 &&
    w13GamerDecisions.All(d => d.Name is "capture" or "chat" or "othergame"));

var w13Enterprise = W13Settings();
var w13SysSample2 = AppSettings.DefaultSystemWhitelist.Skip(3).Take(5).ToList();
var w13CompSample2 = AppSettings.DefaultComponentWhitelist.Skip(15).Take(5).ToList();
var w13EnterpriseList = new List<ProcessInfo>();
var w13Pid2 = 3000;
foreach (var name in w13SysSample2)
{
    w13EnterpriseList.Add(W13Proc(w13Pid2++, name, $@"C:\Program Files\Legacy\{name}.exe", string.Empty, 0));
}
foreach (var name in w13CompSample2)
{
    w13EnterpriseList.Add(W13Proc(w13Pid2++, name, $@"C:\Program Files\Legacy\{name}.exe", string.Empty, 0));
}
w13EnterpriseList.Add(W13Proc(w13Pid2++, "sccmagent", @"C:\Windows\CCM\sccmagent.exe", string.Empty, 0));
w13EnterpriseList.Add(W13Proc(w13Pid2++, "vpnui", @"C:\Program Files\VendorVpn\vpnui.exe", "VPN", 0));
w13EnterpriseList.Add(W13Proc(w13Pid2++, "mail", @"C:\Program Files\Office\mail.exe", "Mail", 0));
w13EnterpriseList.Add(W13Proc(w13Pid2++, "chat", @"C:\Program Files\Chat\chat.exe", "Chat", 0));
var w13EnterpriseDecisions = ProcessPatrol.Sweep(w13Enterprise, w13Plan, w13EnterpriseList);
Check("企业脏机：内置系统 / 组件样例与无窗口代理不被拦截",
    !w13EnterpriseDecisions.Any(d => w13SysSample2.Contains(d.Name) || w13CompSample2.Contains(d.Name) || d.Name == "sccmagent"));
Check("企业脏机：有窗口的办公程序不在内置放行里时仍会被拦截，交给用户决定",
    w13EnterpriseDecisions.Any(d => d.Name == "vpnui") &&
    w13EnterpriseDecisions.Any(d => d.Name == "mail") &&
    w13EnterpriseDecisions.Any(d => d.Name == "chat"));
Check("所有档案中没有任何系统核心 / 内置组件 / 系统目录程序被拦截",
    new[] { w13DirtyDecisions, w13GamerDecisions, w13EnterpriseDecisions }
        .SelectMany(list => list)
        .All(d => !RuleEngine.IsSystemCritical(d.Name, w13Dirty) &&
                  !SystemAllowPolicy.IsComponentExempt(d.Name, w13Dirty) &&
                  !SystemAllowPolicy.IsPathProtected(d.FilePath)));
// ---- 13.5 一键白名单 ----
var w13Batch = W13Settings();
w13Batch.Rules.Add(new AppRule { Kind = RuleKind.Whitelist, Target = RuleTarget.File, Path = @"C:\Tools\already.exe" });
var w13BatchApps = new List<RunningApp>
{
    new() { ProcessName = "explorer", FilePath = @"C:\Windows\explorer.exe" },
    new() { ProcessName = "dllhost", FilePath = @"C:\Windows\System32\dllhost.exe" },
    new() { ProcessName = w13Ime, FilePath = $@"C:\ComponentTest\Ime\{w13Ime}.exe" },
    new() { ProcessName = "already", FilePath = @"C:\Tools\already.exe" },
    new() { ProcessName = "newapp", FilePath = @"C:\Tools\newapp.exe" },
    new() { ProcessName = "newapp", FilePath = @"C:\Other\newapp.exe" },
    new() { ProcessName = "noPath", FilePath = string.Empty },
};
var (w13BatchNew, w13BatchCovered, w13BatchSkipped) = ProcessCatalog.BuildWhitelistBatch(w13Batch, w13BatchApps);
Check("一键白名单：跳过系统核心 / 系统目录 / 内置组件与无路径进程，且仍按文件名去重",
    w13BatchNew.Count == 1 &&
    w13BatchNew[0].Path == @"C:\Tools\newapp.exe" &&
    w13BatchSkipped == 3 &&
    w13BatchCovered >= 2);

// ---- 13.6 配置兼容 / 迁移 ----
var w13Legacy = new AppSettings { SystemWhitelist = new List<string> { "explorer", "my-custom-app" } };
w13Legacy.EnsureDefaults();
Check("旧配置迁移：内置保护补齐且保留用户自定义项",
    w13Legacy.SystemWhitelist.Contains("my-custom-app") &&
    w13Legacy.SystemWhitelist.Count >= AppSettings.DefaultSystemWhitelist.Length &&
    AppSettings.DefaultSystemWhitelist.Contains("dllhost") &&
    AppSettings.DefaultSystemWhitelist.Contains("spoolsv"));
Check("旧配置迁移：内置组件名单补齐（输入法 / 杀软 / 显卡 / 音频）",
    AppSettings.DefaultComponentWhitelist.Any(n => n.StartsWith("hips", StringComparison.OrdinalIgnoreCase)) &&
    AppSettings.DefaultComponentWhitelist.Any(n => n.StartsWith("sogou", StringComparison.OrdinalIgnoreCase)) &&
    AppSettings.DefaultComponentWhitelist.Any(n => n.StartsWith("nvtray", StringComparison.OrdinalIgnoreCase)) &&
    AppSettings.DefaultComponentWhitelist.Any(n => n.StartsWith("rtkaud", StringComparison.OrdinalIgnoreCase)) &&
    w13Legacy.ComponentWhitelist.Count >= AppSettings.DefaultComponentWhitelist.Length);
var w13LegacyCount = w13Legacy.SystemWhitelist.Count;
w13Legacy.EnsureDefaults();
Check("重复 EnsureDefaults 不会产生重复项", w13Legacy.SystemWhitelist.Count == w13LegacyCount);

// ---- 13.7 白名单体检报告 ----
var w13AuditDirty = WhitelistAudit.Run(w13Dirty, w13Plan, w13DirtyList);
Check("体检报告：统计到有窗口会被拦截的程序",
    w13AuditDirty.BlockedWindowed == 1 && w13AuditDirty.Report.Contains("blockedapp"));
Check("体检报告：系统核心 / 系统目录 / 内置组件 / 无窗口后台均有分类计数",
    w13AuditDirty.CoreProtected > 0 &&
    w13AuditDirty.SystemDirectory > 0 &&
    w13AuditDirty.ComponentExempt > 0 &&
    w13AuditDirty.BackgroundSafe > 0);
Check("体检报告：必要宿主自检通过且无警告",
    w13AuditDirty.HostWarnings.Count == 0 &&
    w13AuditDirty.HostRunning > 0 &&
    w13AuditDirty.HostPassed == w13AuditDirty.HostRunning &&
    w13AuditDirty.Report.Contains("必要宿主自检"));
var w13AuditGamer = WhitelistAudit.Run(w13Gamer, w13Plan, w13GamerList);
Check("体检报告：启动链路放行被单独统计",
    w13AuditGamer.ChainAllowed >= 3 && w13AuditGamer.Report.Contains("启动链路"));
var w13AuditUnknown = WhitelistAudit.Run(W13Settings(), w13Plan,
    new List<ProcessInfo> { W13Proc(4000, "protected", string.Empty, "Secret") });
Check("体检报告：读不到路径的进程单独提示",
    w13AuditUnknown.UnknownPath == 1 && w13AuditUnknown.BlockedWindowed == 1);
var w13AuditEmpty = WhitelistAudit.Run(W13Settings(), w13Plan, new List<ProcessInfo>());
Check("体检报告：空机器也不会崩溃且结论正常",
    w13AuditEmpty.Total == 0 && w13AuditEmpty.BlockedWindowed == 0 &&
    w13AuditEmpty.HostWarnings.Count == 0 && w13AuditEmpty.Report.Contains("MikoBarrier 白名单体检报告"));

Console.WriteLine();
Console.WriteLine("== 14. 真机白名单体检（当前机器的真实进程） ==");
var w14Live = ProcessPatrol.Snapshot();
var w14Audit = WhitelistAudit.Run(W13Settings(), w13Plan, w14Live);
Check("真机快照能枚举到足够多的进程", w14Live.Count > 20);
Check("真机快照能读到父子进程链（Toolhelp）", w14Live.Any(p => p.ParentPid > 0));
Check("真机体检：没有系统必需宿主会被白名单模式拦截", w14Audit.HostWarnings.Count == 0);
Check("真机体检：报告包含宿主自检与系统目录统计",
    w14Audit.Report.Contains("必要宿主自检") && w14Audit.SystemDirectory > 0);
Console.WriteLine($"    真机体检摘要：进程 {w14Audit.Total}，系统核心 {w14Audit.CoreProtected}，内置组件 {w14Audit.ComponentExempt}，" +
                  $"系统目录 {w14Audit.SystemDirectory}，启动链路 {w14Audit.ChainAllowed}，" +
                  $"有窗口会拦截 {w14Audit.BlockedWindowed}，无窗口不拦截 {w14Audit.BackgroundSafe}，" +
                  $"宿主 {w14Audit.HostPassed}/{w14Audit.HostRunning}（{w14Audit.HostAbsent} 个未运行）");
if (w14Audit.HostWarnings.Count > 0)
{
    Console.WriteLine("    宿主风险：" + string.Join("；", w14Audit.HostWarnings));
}


Console.WriteLine("== 15. 防绕过：密码冷却 / 恢复码 / Kill 欠债 ==");

// 15.1 密码退出：1 次→1h，2 次→2h，3 次→当天冻结
var pwdPolicy = new AppSettings();
Check("密码退出初始可用", SessionExitPolicy.GetPasswordAllowance(pwdPolicy).CanExit);
SessionExitPolicy.RecordPasswordExit(pwdPolicy);
var afterFirstPwd = SessionExitPolicy.GetPasswordAllowance(pwdPolicy);
Check("第 1 次密码退出后冷却 1 小时",
    pwdPolicy.PasswordExitCount == 1 &&
    afterFirstPwd.State == ExitAllowanceState.Cooldown &&
    afterFirstPwd.Remaining > TimeSpan.FromMinutes(55));
pwdPolicy.PasswordExitCooldownUntilUtc = DateTime.UtcNow.AddSeconds(-1);
SessionExitPolicy.RecordPasswordExit(pwdPolicy);
var afterSecondPwd = SessionExitPolicy.GetPasswordAllowance(pwdPolicy);
Check("第 2 次密码退出后冷却 2 小时",
    pwdPolicy.PasswordExitCount == 2 &&
    afterSecondPwd.State == ExitAllowanceState.Cooldown &&
    afterSecondPwd.Remaining > TimeSpan.FromMinutes(115));
pwdPolicy.PasswordExitCooldownUntilUtc = DateTime.UtcNow.AddSeconds(-1);
SessionExitPolicy.RecordPasswordExit(pwdPolicy);
var afterThirdPwd = SessionExitPolicy.GetPasswordAllowance(pwdPolicy);
Check("第 3 次密码退出后当天直接冻结",
    pwdPolicy.PasswordExitCount == 3 &&
    afterThirdPwd.State == ExitAllowanceState.Frozen &&
    afterThirdPwd.Remaining > TimeSpan.Zero);
Check("冻结剩余时间不超过 24 小时",
    afterThirdPwd.Remaining <= TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));

// 15.2 跨天清零 / 重设密码不解除当天冷却
pwdPolicy.PasswordExitDate = "2000-01-01";
pwdPolicy.PasswordExitCooldownUntilUtc = null;
Check("跨天后密码退出额度清零", SessionExitPolicy.GetPasswordAllowance(pwdPolicy).CanExit);
SessionExitPolicy.RecordPasswordExit(pwdPolicy);
PasswordService.SetPassword(pwdPolicy, "AnotherPwd123!@#$");
Check("重设密码不会解除当天冷却",
    !SessionExitPolicy.GetPasswordAllowance(pwdPolicy).CanExit &&
    pwdPolicy.PasswordExitCount == 1);

// 15.3 恢复码退出：每天 1 次，密码冷却时可绕过；重置密码不限次数且不影响冷却
var recPolicy = new AppSettings();
Check("恢复码退出初始可用", SessionExitPolicy.GetRecoveryExitAllowance(recPolicy).CanExit);
SessionExitPolicy.RecordRecoveryExit(recPolicy);
Check("恢复码退出当天用完后冻结", !SessionExitPolicy.GetRecoveryExitAllowance(recPolicy).CanExit);
recPolicy.RecoveryExitDate = "2000-01-01";
Check("跨天后恢复码退出额度恢复", SessionExitPolicy.GetRecoveryExitAllowance(recPolicy).CanExit);
var recCode = "ABCD-EFGH-JKLM-NPQR";
Check("恢复码可 DPAPI 加密保存到本机", RecoveryCodeStore.Save(recCode) && RecoveryCodeStore.Exists);
Check("恢复码可从本机解密读取",
    RecoveryCodeStore.TryLoad(out var loadedRecCode) && loadedRecCode == recCode);

// 15.4 心跳互保基础能力
RuntimeHeartbeat.Write(RuntimeHeartbeat.AppRole, "smoke-session");
var heartbeat = RuntimeHeartbeat.Read(RuntimeHeartbeat.AppRole);
var currentProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
Check("心跳可写入并读取当前进程",
    heartbeat is not null && heartbeat.SessionId == "smoke-session" && heartbeat.Pid == Environment.ProcessId);
Check("心跳可按会话 + 进程名判断存活",
    RuntimeHeartbeat.IsAlive(heartbeat, currentProcessName, "smoke-session", TimeSpan.FromMinutes(1), out _));
Check("旧会话心跳不会被误判为存活",
    !RuntimeHeartbeat.IsAlive(heartbeat, currentProcessName, "another-session", TimeSpan.FromMinutes(1), out _));
RuntimeHeartbeat.Clear(RuntimeHeartbeat.AppRole);
Check("心跳文件可清理", RuntimeHeartbeat.Read(RuntimeHeartbeat.AppRole) is null);

// 15.5 Kill 递进欠债：5/10/15… 封顶 60；封顶后消耗可补满；跨月清空

var killPolicy = new AppSettings { LastSeenUtc = DateTime.UtcNow };
Check("第 1 次 Kill 加 5 分钟",
    KillPenaltyPolicy.RegisterKill(killPolicy) == 300 && killPolicy.KillPenaltyDebtSeconds == 300);
KillPenaltyPolicy.RegisterKill(killPolicy);
KillPenaltyPolicy.RegisterKill(killPolicy);
Check("Kill 递进 5 + 10 + 15 = 30 分钟", killPolicy.KillPenaltyDebtSeconds == 1800);
KillPenaltyPolicy.RegisterKill(killPolicy);
KillPenaltyPolicy.RegisterKill(killPolicy);
Check("本月 Kill 余额封顶 60 分钟",
    killPolicy.KillPenaltyDebtSeconds == 3600 && killPolicy.KillPenaltyCapReached);
killPolicy.KillPenaltyDebtSeconds = 0; // 模拟下一次自律第一轮已偿还
var refilled = KillPenaltyPolicy.RegisterKill(killPolicy);
Check("封顶后消耗，再 Kill 自动补满到 60 分钟",
    refilled == 3600 && killPolicy.KillPenaltyDebtSeconds == 3600);

killPolicy.DebtSeconds = 42;
killPolicy.DebtMonth = ExitDebtPolicy.LocalMonthKey();
KillPenaltyPolicy.EnsureMonth(killPolicy, DateTime.Now.AddMonths(-1));
ExitDebtPolicy.EnsureMonth(killPolicy, DateTime.Now.AddMonths(-1));
Check("跨月清空 Kill 惩罚与提前退出欠债",
    killPolicy.KillPenaltyCount == 0 &&
    killPolicy.KillPenaltyDebtSeconds == 0 &&
    !killPolicy.KillPenaltyCapReached &&
    killPolicy.DebtSeconds == 0);

// 15.5b 提前退出欠债：单次封顶 120 分钟；按自然月清空；时间异常不清理；旧配置迁移不丢历史值。
var exitDebt = new AppSettings { LastSeenUtc = DateTime.UtcNow };
ExitDebtPolicy.Register(exitDebt, 30 * 60);
Check("提前退出欠债按剩余时长记录（30 分钟）", exitDebt.DebtSeconds == 1800);
ExitDebtPolicy.Register(exitDebt, 150 * 60);
Check("提前退出欠债单次封顶 120 分钟", exitDebt.DebtSeconds == 7200);
ExitDebtPolicy.EnsureMonth(exitDebt, DateTime.Now); // 首次登记月份（迁移，不清空）
var sameMonthAgain = ExitDebtPolicy.EnsureMonth(exitDebt, DateTime.Now);
Check("同一自然月内提前退出欠债保留", !sameMonthAgain && exitDebt.DebtSeconds == 7200);
var crossedExitDebt = ExitDebtPolicy.EnsureMonth(exitDebt, DateTime.Now.AddMonths(-1));
Check("跨月自动清空提前退出欠债", crossedExitDebt && exitDebt.DebtSeconds == 0);

var legacyExitDebt = new AppSettings { DebtSeconds = 600, LastSeenUtc = DateTime.UtcNow };
ExitDebtPolicy.EnsureMonth(legacyExitDebt, DateTime.Now);
Check("旧配置首次登记月份时不丢历史欠债", legacyExitDebt.DebtSeconds == 600);
ExitDebtPolicy.EnsureMonth(legacyExitDebt, DateTime.Now.AddMonths(-1));
Check("旧欠债在之后跨月时正常清空", legacyExitDebt.DebtSeconds == 0);

var rollbackExitDebt = new AppSettings
{
    DebtSeconds = 900,
    DebtMonth = ExitDebtPolicy.LocalMonthKey(),
    LastSeenUtc = DateTime.UtcNow.AddHours(1),
};
ExitDebtPolicy.EnsureMonth(rollbackExitDebt, DateTime.Now.AddMonths(-1));
Check("系统时间异常时不按月清空提前退出欠债", rollbackExitDebt.DebtSeconds == 900);

var cappedDebtSettings = new AppSettings
{
    DebtSeconds = 200 * 60,
    DebtMonth = ExitDebtPolicy.LocalMonthKey(),
    LastSeenUtc = DateTime.UtcNow,
};
var cappedDebtEngine = new SessionEngine();
cappedDebtEngine.Start(cappedDebtSettings, new FocusPlan { FocusMinutes = 1, Rounds = 1 });
Check("存量超限欠债开场先夹到 120 分钟后计入第一轮",
    Math.Abs(cappedDebtEngine.PhaseTotal.TotalSeconds - (60 + 7200)) < 1);
cappedDebtEngine.Reset();

// 15.6 事件账本：同会话 15 秒双杀算一次；重复扫描不重复罚
var ledgerSettings = new AppSettings { LastSeenUtc = DateTime.UtcNow };
TamperLedger.Record(TamperIncidentKinds.AppKilled, "ledger-session", Environment.ProcessId, "smoke-test");
Thread.Sleep(20);
TamperLedger.Record(TamperIncidentKinds.GuardKilled, "ledger-session", Environment.ProcessId, "smoke-test");
var ledgerAddedCount = TamperLedger.ApplyPending(ledgerSettings, out var ledgerAddedSeconds);
Check("同一会话 15 秒内双杀只算一次 Kill",
    ledgerAddedCount == 1 && ledgerAddedSeconds == 300 && ledgerSettings.KillPenaltyCount == 1);
var secondScan = TamperLedger.ApplyPending(ledgerSettings, out _);
Check("Kill 事件重复扫描不会重复罚",
    secondScan == 0 && ledgerSettings.KillPenaltyDebtSeconds == 300);
TamperLedger.CleanupApplied(ledgerSettings);
Check("已入账事件文件会被清理", TamperLedger.LoadPending().Count == 0);

// 15.7 会话开场债务合并与快照会话 ID
var debtSettings = new AppSettings
{
    DebtSeconds = 60,
    KillPenaltyDebtSeconds = 120,
    KillPenaltyMonth = KillPenaltyPolicy.LocalMonthKey(),
    LastSeenUtc = DateTime.UtcNow,
};
var debtEngine = new SessionEngine();
debtEngine.Start(debtSettings, new FocusPlan { FocusMinutes = 1, Rounds = 1 });
Check("普通欠债 + Kill 欠债一起加到第一轮（60 + 120）",
    Math.Abs(debtEngine.PhaseTotal.TotalSeconds - 240) < 1);
Check("开场后两种欠债都清零",
    debtSettings.DebtSeconds == 0 && debtSettings.KillPenaltyDebtSeconds == 0);
Check("会话有唯一 ID", debtEngine.SessionId.Length >= 16);
var debtSnapshot = debtEngine.CaptureSnapshot();
Check("快照携带会话 ID", debtSnapshot is not null && debtSnapshot.SessionId == debtEngine.SessionId);
debtEngine.Reset();

// 旧版快照没有 SessionId：恢复时必须沿用与 Guard 相同的 legacy key，否则互保心跳对不上。
var legacySnapshot = new SessionSnapshot
{
    Plan = new FocusPlan { FocusMinutes = 1, Rounds = 1 },
    Phase = nameof(SessionPhase.Focusing),
    CurrentRound = 1,
    TotalRounds = 1,
    PhaseSecondsRemaining = 60,
    StartedUtc = DateTime.UtcNow.AddMinutes(-5),
    SavedUtc = DateTime.UtcNow.AddMinutes(-5),
};
var legacyEngine = new SessionEngine();
legacyEngine.Resume(new AppSettings { LastSeenUtc = DateTime.UtcNow }, legacySnapshot);
Check("旧快照恢复沿用 legacy 会话 key（互保心跳能对上）",
    legacyEngine.SessionId == SessionRecovery.GetSessionKey(legacySnapshot));
legacyEngine.Reset();

// 15.8 正常关机标记
SessionRecovery.MarkGracefulShutdown();
Check("正常关机标记会阻止看门狗误判", SessionRecovery.IsGracefulShutdownPending());
SessionRecovery.ClearGracefulShutdownMarker();
Check("关机标记可清理", !SessionRecovery.IsGracefulShutdownPending());

Console.WriteLine("== 16. 巫女模式解锁与持久化 ==");
var mikoSettings = new AppSettings();
Check("巫女模式默认未解锁、未启用",
    !mikoSettings.MikoModeUnlocked && !mikoSettings.MikoModeEnabled);
Check("未解锁时不能直接切到巫女模式",
    !MikoModePolicy.TrySetEnabled(mikoSettings, true) && !mikoSettings.MikoModeEnabled);

MikoModePolicy.Unlock(mikoSettings);
Check("通过裂缝解锁后自动进入巫女模式",
    mikoSettings.MikoModeUnlocked && mikoSettings.MikoModeEnabled);
Check("解锁后可随时切回普通模式",
    MikoModePolicy.TrySetEnabled(mikoSettings, false) &&
    !mikoSettings.MikoModeEnabled &&
    mikoSettings.MikoModeUnlocked);

var mikoProbePath = Path.Combine(StoragePaths.DataDir, "miko-smoke-probe.json");
JsonStore.Save(mikoProbePath, mikoSettings);
var mikoLoaded = JsonStore.Load(mikoProbePath, static () => new AppSettings());
Check("巫女模式解锁 / 启用标记可持久化",
    mikoLoaded.MikoModeUnlocked && !mikoLoaded.MikoModeEnabled);
File.Delete(mikoProbePath);

// 清理隔离的临时测试目录；看门狗刚退出时文件可能还被占用，删不掉就保留并提示。
Thread.Sleep(300);
try
{
    if (Directory.Exists(smokeHome))
    {
        Directory.Delete(smokeHome, recursive: true);
    }
}
catch
{
    Console.WriteLine($"（临时测试目录未能删除，可手动清理：{smokeHome}）");
}


Console.WriteLine(skippedSections == 0
    ? $"===== 通过 {passed} 项，失败 {failures.Count} 项 ====="
    : $"===== 通过 {passed} 项，失败 {failures.Count} 项，跳过 {skippedSections} 节 =====");
foreach (var failure in failures)
{
    Console.WriteLine("  未通过：" + failure);
}

return failures.Count == 0 ? 0 : 1;
