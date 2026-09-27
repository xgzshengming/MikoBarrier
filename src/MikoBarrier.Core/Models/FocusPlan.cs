namespace MikoBarrier.Core.Models;

/// <summary>
/// 一次专注的完整计划。开始后会被克隆并锁定，直到本次规划结束（或凭密码提前终止）。
/// </summary>
public sealed class FocusPlan
{
    public const int MaxFocusMinutes = 120;
    public const int MaxRounds = 12;
    public const int MinFocusMinutes = 1;

    /// <summary>单轮专注时长（分钟，1-120）。</summary>
    public int FocusMinutes { get; set; } = 25;

    /// <summary>轮数（1-12）。</summary>
    public int Rounds { get; set; } = 1;

    /// <summary>
    /// 每轮配置（时长 + 关联任务）。索引 0 对应第 1 轮。
    /// 旧配置里没有这个字段时为空列表，由 GetRoundPlans() 按旧的统一时长回退。
    /// </summary>
    public List<FocusRoundPlan> RoundPlans { get; set; } = new();

    /// <summary>true = 每轮单独设置时长；false = 所有轮都用 FocusMinutes。</summary>
    public bool PerRoundDuration { get; set; }

    /// <summary>true = 所有轮次共用同一组任务；false = 每轮单独选择任务。</summary>
    public bool UniformTasks { get; set; }

    /// <summary>中场休息（分钟），只允许 0 / 5 / 10。</summary>
    public int BreakMinutes { get; set; }

    /// <summary>是否启用应用屏蔽（黑名单 / 白名单）。</summary>
    public bool BlockApplications { get; set; } = true;

    /// <summary>true = 白名单模式（只允许名单内程序），false = 黑名单模式。</summary>
    public bool UseWhitelistMode { get; set; }

    /// <summary>
    /// 键盘封锁：自律期间吞掉 Alt+Tab / Alt+Esc / Ctrl+Esc / Win / Alt+F4 / Ctrl+Shift+Esc。
    /// 注意：Ctrl+Alt+Del 属于系统安全序列，任何用户态程序都拦不住。
    /// </summary>
    public bool BlockKeyboard { get; set; } = true;

    /// <summary>
    /// 全屏遮罩：自律期间用置顶遮罩盖住屏幕，并用定时器把焦点抢回来。
    /// 这是键盘封锁被绕开（Alt+Tab 仍然生效）时的兜底手段。
    /// </summary>
    public bool EnforceOverlay { get; set; } = true;

    /// <summary>网络封锁档位：不断网 / 温和档（hosts 屏蔽网站）/ 狠人档（禁用网卡）。休息时间同样生效。</summary>
    public NetworkTier NetworkTier { get; set; } = NetworkTier.Off;

    public bool BlocksSites => NetworkTier != NetworkTier.Off;

    public bool BlocksAdapters => NetworkTier == NetworkTier.Hardcore;

    public string NetworkText => NetworkTier switch
    {
        NetworkTier.Gentle => "温和档（屏蔽网站）",
        NetworkTier.Hardcore => "狠人档（禁用网卡）",
        _ => "不断网",
    };

    public int ClampFocusMinutes() => Math.Clamp(FocusMinutes, MinFocusMinutes, MaxFocusMinutes);

    public int ClampRounds() => Math.Clamp(Rounds, 1, MaxRounds);

    /// <summary>多轮模式下才允许中场休息。</summary>
    public bool HasBreak => BreakMinutes > 0 && ClampRounds() > 1;

    /// <summary>把所有轮次配置规范化/补齐：兼容旧配置（RoundPlans 为空）。</summary>
    public List<FocusRoundPlan> GetRoundPlans()
    {
        var count = ClampRounds();
        var result = new List<FocusRoundPlan>(count);

        // 勾选“全部轮次都使用同一组任务”时，以第 1 轮的任务作为所有轮次的统一任务。
        List<string>? uniformTaskIds = null;
        if (UniformTasks && RoundPlans is { Count: > 0 })
        {
            uniformTaskIds = RoundPlans[0].TaskIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? new List<string>();
        }

        for (var i = 0; i < count; i++)
        {
            var source = RoundPlans is not null && i < RoundPlans.Count ? RoundPlans[i] : null;
            var minutes = PerRoundDuration
                ? (source is null ? ClampFocusMinutes() : Math.Clamp(source.Minutes, MinFocusMinutes, MaxFocusMinutes))
                : ClampFocusMinutes();

            var taskIds = uniformTaskIds is not null
                ? new List<string>(uniformTaskIds)
                : source?.TaskIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? new List<string>();

            result.Add(new FocusRoundPlan { Minutes = minutes, TaskIds = taskIds });
        }

        return result;
    }

    public int GetRoundMinutes(int roundIndex)
    {
        var plans = GetRoundPlans();
        return roundIndex >= 0 && roundIndex < plans.Count ? plans[roundIndex].Minutes : ClampFocusMinutes();
    }

    public IReadOnlyList<string> GetRoundTaskIds(int roundIndex)
    {
        var plans = GetRoundPlans();
        return roundIndex >= 0 && roundIndex < plans.Count ? plans[roundIndex].TaskIds : Array.Empty<string>();
    }

    public int TotalFocusSeconds => GetRoundPlans().Sum(p => p.Minutes) * 60;

    public FocusPlan Clone()
    {
        var clone = (FocusPlan)MemberwiseClone();
        clone.RoundPlans = GetRoundPlans().Select(p => p.Clone()).ToList();
        return clone;
    }

    public override string ToString()
    {
        var mode = BlockApplications ? (UseWhitelistMode ? "白名单模式" : "黑名单模式") : "不屏蔽应用";
        var brk = HasBreak ? $"，中场休息 {BreakMinutes} 分钟" : "";
        var plans = GetRoundPlans();
        var distinctMinutes = plans.Select(p => p.Minutes).Distinct().ToList();
        var durationText = distinctMinutes.Count == 1
            ? $"{distinctMinutes[0]} 分钟 \u00D7 {plans.Count} 轮"
            : $"每轮 {string.Join("/", plans.Select(p => p.Minutes))} 分钟（合计 {plans.Sum(p => p.Minutes)} 分钟）";
        var taskCount = plans.SelectMany(p => p.TaskIds).Distinct(StringComparer.Ordinal).Count();
        var taskText = taskCount > 0 ? $"，关联 {taskCount} 个任务" : string.Empty;
        return $"{durationText}{brk}，{mode}，{NetworkText}{taskText}{(BlockKeyboard ? "，键盘封锁" : string.Empty)}{(EnforceOverlay ? "，全屏计时" : string.Empty)}";
    }
}
