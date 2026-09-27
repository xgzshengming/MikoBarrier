namespace MikoBarrier.Core.Models;

public enum FocusTaskState
{
    Pending = 0,
    Active = 1,
    Done = 2,
}

/// <summary>待办任务：可以关联到自律会话，累计回合数与自律时长。</summary>
public sealed class FocusTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    /// <summary>预计需要几个回合。</summary>
    public int PlannedRounds { get; set; } = 1;

    public int CompletedRounds { get; set; }

    public int TotalFocusSeconds { get; set; }

    public FocusTaskState State { get; set; } = FocusTaskState.Pending;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public double Progress => PlannedRounds <= 0 ? 0 : Math.Clamp((double)CompletedRounds / PlannedRounds, 0, 1);

    public override string ToString() =>
        $"{Title}   {CompletedRounds}/{PlannedRounds} 回合  {TotalFocusSeconds / 60.0:F0} 分钟" +
        (State == FocusTaskState.Done ? "   [已完成]" : string.Empty);
}