namespace MikoBarrier.Core.Models;

/// <summary>黑名单 / 白名单中的一条规则。</summary>
public sealed class AppRule
{
    public RuleKind Kind { get; set; } = RuleKind.Blacklist;

    public RuleTarget Target { get; set; } = RuleTarget.File;

    /// <summary>exe 完整路径、文件夹路径，或进程名。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>界面上显示的名称（默认取文件名）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>去重用的稳定键。</summary>
    public string Key => $"{Kind}|{Target}|{Path}".ToLowerInvariant();

    public override string ToString()
    {
        var kind = Kind == RuleKind.Whitelist ? "白名单" : "黑名单";
        return $"[{kind}] {DisplayName}  ({Path})";
    }
}

/// <summary>当前正在运行、可以作为规则来源的程序。</summary>
public sealed class RunningApp
{
    public string DisplayName { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public string WindowTitle { get; init; } = string.Empty;

    public override string ToString()
    {
        var title = string.IsNullOrWhiteSpace(WindowTitle) ? "(后台进程)" : WindowTitle;
        return $"{DisplayName}  |  {title}";
    }
}
