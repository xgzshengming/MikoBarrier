using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 主动中断（提前结束）欠债：
///   把本次未完成的专注时长记入下一场自律的第一轮；单次入账封顶 120 分钟。
///   按本地自然月（yyyy-MM）自动清空；检测到系统时间回拨时不刷新。
///   旧配置没有 DebtMonth 时，首次调用只登记当前月份、保留历史欠债，之后跨月才清空。
/// </summary>
public static class ExitDebtPolicy
{
    public static readonly TimeSpan MonthlyCap = TimeSpan.FromMinutes(120);

    public static int MaxSeconds => (int)MonthlyCap.TotalSeconds;

    public static string LocalMonthKey(DateTime? localNow = null) =>
        (localNow ?? DateTime.Now).ToString("yyyy-MM");

    /// <summary>把提前结束剩余的专注秒数记为欠债（覆盖式，沿用原语义），并按 120 分钟封顶。</summary>
    public static int Register(AppSettings settings, double remainingSeconds)
    {
        settings.DebtSeconds = Clamp(remainingSeconds);
        return settings.DebtSeconds;
    }

    /// <summary>把存量欠债夹到 [0, 120 分钟]。</summary>
    public static int Clamp(double seconds) =>
        (int)Math.Clamp(Math.Round(Math.Max(0, seconds)), 0, MaxSeconds);

    /// <summary>
    /// 维护提前退出欠债的月度状态：跨自然月清空，并把存量值夹到 120 分钟以内。
    /// 返回 true 表示本次改动了需要持久化的内容（跨月 / 首次登记月份 / 存量超限）。
    /// </summary>
    public static bool EnsureMonth(AppSettings settings, DateTime? localNow = null)
    {
        var clamped = Clamp(settings.DebtSeconds);
        var changed = settings.DebtSeconds != clamped;
        settings.DebtSeconds = clamped;

        var month = LocalMonthKey(localNow);
        if (string.Equals(settings.DebtMonth, month, StringComparison.Ordinal))
        {
            return changed;
        }

        if (!SessionExitPolicy.IsClockSane(settings))
        {
            return changed;
        }

        // 迁移：旧配置第一次登记月份时保留已有欠债，避免升级即清零。
        var isMigration = string.IsNullOrEmpty(settings.DebtMonth);
        settings.DebtMonth = month;
        if (!isMigration)
        {
            settings.DebtSeconds = 0;
            changed = true;
        }

        return true;
    }
}
