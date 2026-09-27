namespace MikoBarrier.Core.Models;

/// <summary>一次“互保进程被结束 / 失联”的事件记录。App 与 Guard 都会写，统一由 App 入账。</summary>
public sealed class TamperIncident
{
    public string Id { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    /// <summary>AppKilled / GuardKilled / AppUnresponsive / GuardUnresponsive。</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTime Utc { get; set; } = DateTime.UtcNow;

    public int SourcePid { get; set; }

    public string Detail { get; set; } = string.Empty;
}

public static class TamperIncidentKinds
{
    public const string AppKilled = "AppKilled";
    public const string GuardKilled = "GuardKilled";
    public const string AppUnresponsive = "AppUnresponsive";
    public const string GuardUnresponsive = "GuardUnresponsive";
}
