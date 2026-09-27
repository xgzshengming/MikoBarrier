using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 被确认 Kill 的递进惩罚欠债：
///   本自然月第 n 次：+5n 分钟（5、10、15…），当月未偿还余额封顶 60 分钟。
///   余额被下一次自律第一轮“偿还”清零后，本月后续 Kill 仍可再次累加 / 补满到 60 分钟。
///   跨月清零 Kill 次数与未偿还 Kill 欠债；提前退出产生的 DebtSeconds 由 ExitDebtPolicy 单独按月清空。
/// </summary>
public static class KillPenaltyPolicy
{
    public static readonly TimeSpan MonthlyCap = TimeSpan.FromMinutes(60);
    public const int FirstStepMinutes = 5;

    public static string LocalMonthKey(DateTime? localNow = null) =>
        (localNow ?? DateTime.Now).ToString("yyyy-MM");

    /// <summary>跨月时清零 Kill 计数和未偿还的 Kill 欠债；系统时间异常时不刷新。</summary>
    public static void EnsureMonth(AppSettings settings, DateTime? localNow = null)
    {
        var month = LocalMonthKey(localNow);
        if (string.Equals(settings.KillPenaltyMonth, month, StringComparison.Ordinal))
        {
            return;
        }

        if (!SessionExitPolicy.IsClockSane(settings))
        {
            return;
        }

        settings.KillPenaltyMonth = month;
        settings.KillPenaltyCount = 0;
        settings.KillPenaltyDebtSeconds = 0;
        settings.KillPenaltyCapReached = false;
    }

    /// <summary>
    /// 给一次新的 Kill 事件入账；返回本次实际增加的秒数。
    /// 本月一旦达到过 60 分钟封顶，后续任意新 Kill 都会把已偿还的余额直接补满回 60 分钟。
    /// </summary>
    public static int RegisterKill(AppSettings settings, DateTime? localNow = null)
    {
        EnsureMonth(settings, localNow);

        settings.KillPenaltyCount = Math.Max(0, settings.KillPenaltyCount) + 1;

        var capSeconds = (int)MonthlyCap.TotalSeconds;
        var before = Math.Clamp(settings.KillPenaltyDebtSeconds, 0, capSeconds);
        int after;

        if (settings.KillPenaltyCapReached)
        {
            // 封顶过：消耗多少就补满多少，后续 Kill 不再按 5/10/15 慢慢加。
            after = capSeconds;
        }
        else
        {
            var stepSeconds = FirstStepMinutes * 60 * settings.KillPenaltyCount;
            after = (int)Math.Min(capSeconds, (long)before + stepSeconds);
            if (after >= capSeconds)
            {
                settings.KillPenaltyCapReached = true;
            }
        }

        settings.KillPenaltyDebtSeconds = after;
        return after - before;
    }

    /// <summary>把 Kill 欠债并入一次自律的开场欠债，并标记偿还清零。</summary>
    public static void ConsumeIntoSession(AppSettings settings, DateTime? localNow = null)
    {
        EnsureMonth(settings, localNow);
        settings.KillPenaltyDebtSeconds = 0;
    }
}
