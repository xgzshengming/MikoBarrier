namespace MikoBarrier.Core.Models;

/// <summary>
/// 单轮自律的配置：时长 + 本轮关联的待办任务（可 0 个、1 个或多个）。
/// 同一个任务可以出现在多轮里；同一轮也可以关联多个任务。
/// </summary>
public sealed class FocusRoundPlan
{
    /// <summary>本轮专注时长（分钟，1-120）。</summary>
    public int Minutes { get; set; } = 25;

    /// <summary>本轮关联的待办任务 Id 列表；空 = 本轮不关联任务。</summary>
    public List<string> TaskIds { get; set; } = new();

    public FocusRoundPlan Clone() => new()
    {
        Minutes = Minutes,
        TaskIds = new List<string>(TaskIds ?? new List<string>()),
    };

    public override string ToString() => $"{Minutes} 分钟";
}
