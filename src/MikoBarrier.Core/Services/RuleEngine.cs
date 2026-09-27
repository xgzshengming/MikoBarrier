using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

public sealed record RuleDecision(bool ShouldBlock, string Reason);

/// <summary>
/// 名单裁决：黑名单模式拦截名单内程序；白名单模式只放行名单内程序。
///
/// 白名单模式的放行顺序（只扩大放行范围，尽量不误伤系统）：
///   1. 自律锁自身；
///   2. SystemWhitelist 系统核心进程（黑名单模式同样保护）；
///   3. 用户白名单规则；
///   4. ComponentWhitelist 内置组件（输入法 / 显卡 / 音频 / 杀软等）；
///   5. SystemAllowPolicy 系统目录（Windows\System32、WinSxS、Defender 等）；
///   6. 启动链路：任一祖先进程命中用户白名单规则（整条链路一起放行）。
/// 以上都不满足时，白名单模式才判为拦截；ProcessPatrol 还只会对「有窗口」的程序动手。
/// </summary>
public static class RuleEngine
{
    /// <summary>启动链路最多向上追溯的层数，防止异常数据造成环或过深遍历。</summary>
    public const int MaxChainDepth = 8;

    public static RuleDecision Evaluate(string processName, string filePath, AppSettings settings, FocusPlan plan)
    {
        var name = Normalize(processName);
        var path = filePath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return new RuleDecision(false, "无法识别进程名");
        }

        if (string.Equals(name, "MikoBarrier.app", StringComparison.OrdinalIgnoreCase) || IsSelfPath(path))
        {
            return new RuleDecision(false, "自律锁自身");
        }

        if (IsSystemCritical(name, settings))
        {
            return new RuleDecision(false, "系统必需进程");
        }

        if (!plan.BlockApplications)
        {
            return new RuleDecision(false, "本次未开启应用屏蔽");
        }

        var matches = settings.Rules.Where(r => Matches(r, name, path, settings.MatchByFileName)).ToList();

        if (plan.UseWhitelistMode)
        {
            if (matches.Any(r => r.Kind == RuleKind.Whitelist))
            {
                return new RuleDecision(false, "在白名单内");
            }

            if (SystemAllowPolicy.IsComponentExempt(name, settings))
            {
                return new RuleDecision(false, "内置必需组件");
            }

            if (SystemAllowPolicy.IsPathProtected(path))
            {
                return new RuleDecision(false, "系统自带目录");
            }

            return new RuleDecision(true, "不在白名单内");
        }

        var blocked = matches.Any(r => r.Kind == RuleKind.Blacklist);
        return blocked
            ? new RuleDecision(true, "命中黑名单")
            : new RuleDecision(false, "不在黑名单内");
    }

    /// <summary>进程是否直接命中用户白名单规则（不包含系统/组件等内置豁免）。</summary>
    public static bool IsUserWhitelisted(ProcessInfo info, AppSettings settings) =>
        settings.Rules.Any(r =>
            r.Kind == RuleKind.Whitelist &&
            Matches(r, info.Name, info.FilePath, settings.MatchByFileName));

    /// <summary>
    /// 启动链路裁决：先做直接裁决；白名单模式下若直接裁决要拦截，
    /// 再沿父进程链向上找，只要任一祖先命中用户白名单，就放行整条链路。
    ///
    /// 只认可「用户白名单」作为链路根：从 explorer / cmd / svchost 等系统进程启动的程序
    /// 不会因为父进程是系统进程就被自动放行——否则用户随便开个命令行就能绕过白名单。
    /// </summary>
    public static RuleDecision EvaluateProcess(
        ProcessInfo info,
        IReadOnlyDictionary<int, ProcessInfo>? snapshot,
        AppSettings settings,
        FocusPlan plan)
    {
        var direct = Evaluate(info.Name, info.FilePath, settings, plan);
        if (!direct.ShouldBlock || !plan.UseWhitelistMode || snapshot is null || snapshot.Count == 0)
        {
            return direct;
        }

        var visited = new HashSet<int> { info.Pid };
        var current = info;

        for (var depth = 0; depth < MaxChainDepth; depth++)
        {
            var parentPid = current.ParentPid;
            if (parentPid <= 0 || !snapshot.TryGetValue(parentPid, out var parent))
            {
                break;
            }

            // 环形父子关系 / PID 复用：不再向上追溯。
            if (!visited.Add(parent.Pid))
            {
                break;
            }

            // 父子时间倒挂（父进程比子进程晚启动）说明 PID 很可能被复用，停止追溯。
            if (parent.StartedUtc is { } parentStarted &&
                current.StartedUtc is { } currentStarted &&
                parentStarted > currentStarted)
            {
                break;
            }

            if (IsUserWhitelisted(parent, settings))
            {
                return new RuleDecision(false, $"启动链路：祖先进程「{parent.Name}」在白名单内");
            }

            current = parent;
        }

        return direct;
    }

    public static bool IsSystemCritical(string processName, AppSettings settings)
    {
        var name = Normalize(processName);
        if (string.IsNullOrWhiteSpace(name) || settings.SystemWhitelist is not { Count: > 0 })
        {
            return false;
        }

        foreach (var item in settings.SystemWhitelist)
        {
            if (string.Equals(Normalize(item), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Matches(AppRule rule, string processName, string filePath) =>
        Matches(rule, processName, filePath, matchByFileName: true);

    public static bool Matches(AppRule rule, string processName, string filePath, bool matchByFileName)
    {
        if (string.IsNullOrWhiteSpace(rule.Path))
        {
            return false;
        }

        var pathKnown = !string.IsNullOrWhiteSpace(filePath);

        return rule.Target switch
        {
            RuleTarget.ProcessName => string.Equals(
                TryGetFileNameWithoutExtension(rule.Path),
                processName,
                StringComparison.OrdinalIgnoreCase),

            RuleTarget.Folder => pathKnown && IsUnderFolder(filePath, rule.Path),

            // 文件规则：完整路径相同，或者（允许时）文件名相同即算命中。
            // 读不到路径的进程（游戏 / 反作弊 / 高权限进程）也按文件名兜底。
            // 路径异常（含非法字符等）时只按文件名兜底，绝不让巡逻因单条坏规则整体失败。
            _ => (pathKnown && TrySameFullPath(rule.Path, filePath))
                 || (matchByFileName && string.Equals(
                        TryGetFileNameWithoutExtension(rule.Path),
                        processName,
                        StringComparison.OrdinalIgnoreCase)),
        };
    }

    /// <summary>filePath 是否位于 folder 目录下（同时兼容 \ 与 /，不区分大小写）。</summary>
    private static bool IsUnderFolder(string filePath, string folder)
    {
        var file = filePath.TrimEnd('\\', '/');
        var dir = folder.TrimEnd('\\', '/');
        if (file.Length <= dir.Length || dir.Length == 0 || !file.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return file[dir.Length] is '\\' or '/';
    }

    private static bool TrySameFullPath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetFileNameWithoutExtension(string path)
    {
        try
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            return null;
        }
    }

    public static string Normalize(string value)
    {
        var name = value.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }

    private static bool IsSelfPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            var self = Environment.ProcessPath;
            return !string.IsNullOrWhiteSpace(self) &&
                   string.Equals(Path.GetFullPath(self!), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
