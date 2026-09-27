namespace MikoBarrier.Core.Models;

/// <summary>界面主题（白天 / 黑夜）。</summary>
public enum ThemeKind
{
    Light = 0,
    Dark = 1,
}

/// <summary>专注会话所处的阶段。</summary>
public enum SessionPhase
{
    Idle = 0,
    Focusing = 1,
    Breaking = 2,
    Finished = 3,
    Aborted = 4,
}

/// <summary>规则归属的名单。</summary>
public enum RuleKind
{
    Blacklist = 0,
    Whitelist = 1,
}

/// <summary>规则的匹配目标类型。</summary>
public enum RuleTarget
{
    File = 0,
    Folder = 1,
    ProcessName = 2,
}

/// <summary>网络封锁档位（休息时间也生效）。</summary>
public enum NetworkTier
{
    /// <summary>不断网。</summary>
    Off = 0,

    /// <summary>温和档：hosts 屏蔽名单里的网站。</summary>
    Gentle = 1,

    /// <summary>狠人档：直接禁用网卡，整机断网。</summary>
    Hardcore = 2,
}
