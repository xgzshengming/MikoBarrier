namespace MikoBarrier.Core.Models;

/// <summary>一次专注结束后写入统计的记录。中途退出的记录会标记 Completed = false，且不计入自律次数。</summary>
public sealed class SessionRecord
{
    public DateTime StartedUtc { get; set; }

    public DateTime EndedUtc { get; set; }

    /// <summary>计划专注总秒数。</summary>
    public int PlannedFocusSeconds { get; set; }

    /// <summary>实际完成的专注秒数（休息时间不计入）。</summary>
    public int ActualFocusSeconds { get; set; }

    public int RoundsPlanned { get; set; }

    public int RoundsCompleted { get; set; }

    public bool Completed { get; set; }

    public string AbortReason { get; set; } = string.Empty;

    public string PlanSummary { get; set; } = string.Empty;

    /// <summary>本次自律中被真正拦截（结束 / 隐藏）的程序次数。</summary>
    public int BlockedProcessCount { get; set; }

    public string BlockedProcessNames { get; set; } = string.Empty;

    /// <summary>各待办任务本次应累计的回合 / 秒数（按每轮任务安排计算）。</summary>
    public List<TaskAward> TaskAwards { get; set; } = new();

    /// <summary>本次自律中被吞掉的按键次数（Alt+Tab / Win 等）。</summary>
    public int BlockedKeyCount { get; set; }

    /// <summary>拦截失败的次数（通常是权限不足）。</summary>
    public int BlockedProcessFailedCount { get; set; }

    public TimeSpan ActualFocus => TimeSpan.FromSeconds(ActualFocusSeconds);

    public string ToDisplayString()
    {
        var local = StartedUtc.ToLocalTime().ToString("MM-dd HH:mm");
        var status = Completed ? "完成 ✓" : "中断 ✗";
        var blocked = BlockedProcessCount > 0 ? $"   拦截 {BlockedProcessCount} 次" : string.Empty;
        var failed = BlockedProcessFailedCount > 0 ? $"（失败 {BlockedProcessFailedCount} 次）" : string.Empty;
        var keys = BlockedKeyCount > 0 ? $"   按键拦截 {BlockedKeyCount} 次" : string.Empty;
        return $"{local}   {PlanSummary}   实际自律 {ActualFocus.TotalMinutes:F1} 分钟   {status}{blocked}{failed}{keys}";
    }
}
