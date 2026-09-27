using System.Text;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>白名单体检结果（同时带结构化计数，便于自检断言）。</summary>
public sealed class WhitelistAuditResult
{
    public required string Report { get; init; }

    public int Total { get; init; }
    public int CoreProtected { get; init; }
    public int ComponentExempt { get; init; }
    public int SystemDirectory { get; init; }
    public int UserWhitelisted { get; init; }
    public int ChainAllowed { get; init; }
    public int OtherAllowed { get; init; }
    public int BlockedWindowed { get; init; }
    public int BackgroundSafe { get; init; }
    public int UnknownPath { get; init; }
    public int HostTotal { get; init; }
    public int HostRunning { get; init; }
    public int HostAbsent { get; init; }
    public int HostPassed { get; init; }
    public IReadOnlyList<string> HostWarnings { get; init; } = Array.Empty<string>();

    public bool HasRisk => BlockedWindowed > 0 || HostWarnings.Count > 0;
}

/// <summary>
/// 白名单体检：按照「白名单模式」把当前机器上正在运行的进程整体裁决一遍，回答三个问题：
///   1. 哪些系统 / 组件 / 白名单程序会被放行（为什么放行）；
///   2. 哪些「有窗口」程序会被拦截（真正需要用户确认的名单）；
///   3. 必要宿主进程（explorer / svchost / dllhost / taskhostw / 计划任务 / COM 宿主…）是否都安全。
///
/// 用户可以在「脏」电脑上运行 `MikoBarrier.App.exe --whitelist-audit` 或在名单页点「白名单体检」，
/// 报告写入 logs\whitelist-audit.txt，用来验证白名单不会把系统弄坏。
/// </summary>
public static class WhitelistAudit
{
    /// <summary>这些宿主承载了 DLL 注入、COM 组件、服务、计划任务、输入法、显卡/音频驱动等非 exe 链路。</summary>
    private static readonly string[] InfrastructureHosts =
    {
        "explorer", "svchost", "dllhost", "taskhostw", "services", "wmiprvse",
        "spoolsv", "audiodg", "fontdrvhost", "sihost", "ctfmon", "TextInputHost",
        "RuntimeBroker", "sppsvc", "msiexec", "SearchIndexer", "SearchHost",
        "StartMenuExperienceHost", "ShellExperienceHost", "ApplicationFrameHost",
        "userinit", "winlogon",
    };

    private const int MaxBlockedListed = 50;

    public static WhitelistAuditResult Run(
        AppSettings settings,
        FocusPlan? plan = null,
        IReadOnlyList<ProcessInfo>? snapshot = null)
    {
        plan ??= new FocusPlan { BlockApplications = true, UseWhitelistMode = true };
        settings.EnsureDefaults();

        var processes = snapshot ?? ProcessPatrol.Snapshot();
        var byPid = new Dictionary<int, ProcessInfo>();
        foreach (var process in processes)
        {
            byPid[process.Pid] = process;
        }

        var coreProtected = 0;
        var componentExempt = 0;
        var systemDirectory = 0;
        var userWhitelisted = 0;
        var chainAllowed = 0;
        var otherAllowed = 0;
        var blockedWindowed = new List<ProcessInfo>();
        var backgroundSafe = 0;
        var unknownPath = 0;

        foreach (var info in processes)
        {
            if (string.IsNullOrWhiteSpace(info.FilePath))
            {
                unknownPath++;
            }

            var direct = RuleEngine.Evaluate(info.Name, info.FilePath, settings, plan);
            var final = RuleEngine.EvaluateProcess(info, byPid, settings, plan);

            if (!final.ShouldBlock)
            {
                if (RuleEngine.IsSystemCritical(info.Name, settings))
                {
                    coreProtected++;
                }
                else if (plan.UseWhitelistMode && SystemAllowPolicy.IsComponentExempt(info.Name, settings))
                {
                    componentExempt++;
                }
                else if (plan.UseWhitelistMode && SystemAllowPolicy.IsPathProtected(info.FilePath))
                {
                    systemDirectory++;
                }
                else if (RuleEngine.IsUserWhitelisted(info, settings))
                {
                    userWhitelisted++;
                }
                else if (direct.ShouldBlock)
                {
                    // 直接裁决要拦，但启动链路把它救了回来。
                    chainAllowed++;
                }
                else
                {
                    otherAllowed++;
                }

                continue;
            }

            if (info.HasWindow)
            {
                blockedWindowed.Add(info);
            }
            else
            {
                backgroundSafe++;
            }
        }

        var hostLines = new List<string>();
        var hostWarnings = new List<string>();
        var hostPassed = 0;
        var hostRunning = 0;
        var hostAbsent = 0;

        foreach (var host in InfrastructureHosts)
        {
            var running = processes
                .Where(p => string.Equals(
                    RuleEngine.Normalize(p.Name),
                    RuleEngine.Normalize(host),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (running.Count == 0)
            {
                hostAbsent++;
                hostLines.Add($"  · {host}：当前未运行（正常）");
                continue;
            }

            hostRunning++;
            var bad = running
                .Where(p => RuleEngine.EvaluateProcess(p, byPid, settings, plan).ShouldBlock)
                .ToList();

            if (bad.Count == 0)
            {
                hostPassed++;
                hostLines.Add($"  ✔ {host}：放行");
            }
            else
            {
                hostWarnings.Add($"{host}（PID {string.Join(",", bad.Select(p => p.Pid))}）会被拦截");
                hostLines.Add($"  ⚠ {host}：会被拦截，可能破坏系统！");
            }
        }

        var report = BuildReport(
            settings,
            plan,
            processes.Count,
            coreProtected,
            componentExempt,
            systemDirectory,
            userWhitelisted,
            chainAllowed,
            otherAllowed,
            blockedWindowed,
            backgroundSafe,
            unknownPath,
            hostLines,
            hostWarnings,
            hostPassed,
            hostRunning,
            hostAbsent);

        return new WhitelistAuditResult
        {
            Report = report,
            Total = processes.Count,
            CoreProtected = coreProtected,
            ComponentExempt = componentExempt,
            SystemDirectory = systemDirectory,
            UserWhitelisted = userWhitelisted,
            ChainAllowed = chainAllowed,
            OtherAllowed = otherAllowed,
            BlockedWindowed = blockedWindowed.Count,
            BackgroundSafe = backgroundSafe,
            UnknownPath = unknownPath,
            HostTotal = InfrastructureHosts.Length,
            HostRunning = hostRunning,
            HostAbsent = hostAbsent,
            HostPassed = hostPassed,
            HostWarnings = hostWarnings,
        };
    }

    private static string BuildReport(
        AppSettings settings,
        FocusPlan plan,
        int total,
        int coreProtected,
        int componentExempt,
        int systemDirectory,
        int userWhitelisted,
        int chainAllowed,
        int otherAllowed,
        IReadOnlyList<ProcessInfo> blockedWindowed,
        int backgroundSafe,
        int unknownPath,
        IReadOnlyList<string> hostLines,
        IReadOnlyList<string> hostWarnings,
        int hostPassed,
        int hostRunning,
        int hostAbsent)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MikoBarrier 白名单体检报告");
        builder.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"管理员权限：{(AdminHelper.IsElevated ? "是" : "否（读不到路径的进程会变多，建议右键以管理员身份运行后再体检一次）")}");
        builder.AppendLine($"进程总数：{total}");
        builder.AppendLine($"体检模式：白名单（只允许名单内程序及其启动链路；系统组件自动放行）");
        builder.AppendLine($"白名单规则：{settings.Rules.Count(r => r.Kind == RuleKind.Whitelist)} 条    " +
                           $"黑名单规则：{settings.Rules.Count(r => r.Kind == RuleKind.Blacklist)} 条    " +
                           $"按文件名匹配：{(settings.MatchByFileName ? "开" : "关")}");
        builder.AppendLine();
        builder.AppendLine("[已放行]");
        builder.AppendLine($"  系统核心进程名（两种模式都放行）：{coreProtected}");
        builder.AppendLine($"  内置必需组件（输入法 / 显卡 / 音频 / 杀软等）：{componentExempt}");
        builder.AppendLine($"  Windows 系统目录（System32 / SysWOW64 / WinSxS / SystemApps / Defender）：{systemDirectory}");
        builder.AppendLine($"  用户白名单规则命中：{userWhitelisted}");
        builder.AppendLine($"  启动链路（祖先命中用户白名单）：{chainAllowed}");
        builder.AppendLine($"  其他（自律锁自身 / 未开启屏蔽等）：{otherAllowed}");
        builder.AppendLine();
        builder.AppendLine($"[会被拦截的有窗口程序] {blockedWindowed.Count} 个");
        for (var i = 0; i < Math.Min(blockedWindowed.Count, MaxBlockedListed); i++)
        {
            var info = blockedWindowed[i];
            var path = string.IsNullOrWhiteSpace(info.FilePath) ? "(读不到路径)" : info.FilePath;
            builder.AppendLine($"  {i + 1}. {info.Name} (PID {info.Pid})  {path}");
        }

        if (blockedWindowed.Count > MaxBlockedListed)
        {
            builder.AppendLine($"  …还有 {blockedWindowed.Count - MaxBlockedListed} 个未列出。");
        }

        if (blockedWindowed.Count == 0)
        {
            builder.AppendLine("  （没有有窗口程序会被拦截）");
        }

        builder.AppendLine();
        builder.AppendLine("[不会拦截的部分]");
        builder.AppendLine($"  无窗口的后台程序：{backgroundSafe} 个（白名单模式只拦有窗口程序；服务 / COM / 计划任务 / 驱动宿主安全）");
        builder.AppendLine($"  读不到路径的进程：{unknownPath} 个");
        builder.AppendLine();
        builder.AppendLine($"[必要宿主自检] 运行中的宿主 {hostPassed}/{hostRunning} 放行；{hostAbsent} 个当前未运行（正常）");
        foreach (var line in hostLines)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine();
        builder.AppendLine("[结论]");
        if (hostWarnings.Count > 0)
        {
            builder.AppendLine($"  ⚠ 发现系统宿主可能被拦截：{string.Join("；", hostWarnings)}");
            builder.AppendLine("    请把本报告发给开发者，这属于白名单保护逻辑的漏洞。");
        }
        else
        {
            builder.AppendLine("  ✔ 没有发现会被拦截的系统必需宿主。");
        }

        if (blockedWindowed.Count == 0)
        {
            builder.AppendLine("  ✔ 没有有窗口程序会被拦截。");
        }
        else
        {
            builder.AppendLine($"  ℹ 有 {blockedWindowed.Count} 个有窗口程序会被拦截；如果其中有工作必需的软件，请在名单页加入白名单。");
        }

        builder.AppendLine();
        builder.AppendLine("说明：");
        builder.AppendLine("  · 白名单里的程序启动的子程序会沿启动链路自动放行；");
        builder.AppendLine("  · DLL 注入 / COM 组件 / 计划任务最终都依附于某个宿主进程，宿主放行即可；");
        builder.AppendLine("  · 白名单模式只拦截“有窗口”的程序，后台服务与驱动不会被杀；");
        builder.AppendLine("  · 系统与内置组件保护是安全底线，来自 config.json 的内置默认项会自动补齐。");

        return builder.ToString();
    }
}
