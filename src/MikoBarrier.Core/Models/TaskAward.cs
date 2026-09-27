namespace MikoBarrier.Core.Models;

/// <summary>一次自律结束后，某个任务应累计的回合数与专注秒数。</summary>
public sealed class TaskAward
{
    public string TaskId { get; set; } = string.Empty;

    public int Rounds { get; set; }

    public int FocusSeconds { get; set; }

    public TaskAward Clone() => new()
    {
        TaskId = TaskId,
        Rounds = Rounds,
        FocusSeconds = FocusSeconds,
    };
}
