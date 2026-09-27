using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Models;

/// <summary>
/// 写入 data/session.lock 的快照：程序被强杀 / 断电 / 蓝屏后，下次启动可以"继续"本次专注，
/// 中断时间不额外惩罚（只有主动点退出才算中断）。
/// </summary>
public sealed class SessionSnapshot
{
    /// <summary>一次自律从开始到最终结束的唯一 ID；崩溃恢复时沿用，用于关联 Kill 事件。</summary>
    public string SessionId { get; set; } = string.Empty;

    public FocusPlan? Plan { get; set; }

    public string Phase { get; set; } = nameof(SessionPhase.Focusing);

    public int CurrentRound { get; set; } = 1;

    public int TotalRounds { get; set; } = 1;

    public int RoundsCompleted { get; set; }

    /// <summary>已经记到任务上的累计成绩（崩溃恢复后继续累计，不丢）。</summary>
    public List<TaskAward> TaskAwards { get; set; } = new();

    /// <summary>当前阶段剩余秒数（每隔约 10 秒刷新一次）。</summary>
    public double PhaseSecondsRemaining { get; set; }

    /// <summary>已经完成的专注秒数（不含休息）。</summary>
    public double FocusSecondsDone { get; set; }

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;

    public int Pid { get; set; }

    public bool IsValid => Plan is not null && TotalRounds > 0 && CurrentRound >= 1;
}
