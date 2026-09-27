using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

public enum ExitAllowanceState
{
    Allowed,
    Cooldown,
    Frozen,
    RecoveryUsed,
}

/// <summary>某个退出通道（密码 / 恢复码）当前是否允许使用。</summary>
public sealed record ExitAllowance(
    ExitAllowanceState State,
    TimeSpan Remaining,
    int UsedToday,
    int DailyLimit,
    string Message)
{
    public bool CanExit => State == ExitAllowanceState.Allowed;
}

/// <summary>
/// 提前退出规则：
///   密码：每天最多 3 次。第 1 次成功后冷却 1 小时；第 2 次成功后冷却 2 小时；
///         第 3 次成功后当天直接冻结，次日清零。
///   恢复码退出：每天最多 1 次，可绕过密码冷却；当天用完后冻结到次日。
///   恢复码重置密码不受次数限制，但不会重置密码退出计数 / 冷却。
/// 只依赖系统 UTC + 本地日期；检测到系统时间被回拨时不刷新每日额度。
/// </summary>
public static class SessionExitPolicy
{
    public const int PasswordDailyLimit = 3;
    public const int RecoveryExitDailyLimit = 1;

    /// <summary>提前结束弹窗中展示给用户的固定规则文案（首启引导 / 帮助中心也复用它）。</summary>
    public const string PasswordRuleSummary =
        "每日仅可提前结束 3 次，前 2 次冷却时间分别为 1 小时和 2 小时，第 3 次后本日提前结束功能冻结。";

    public static readonly TimeSpan FirstCooldown = TimeSpan.FromHours(1);
    public static readonly TimeSpan SecondCooldown = TimeSpan.FromHours(2);

    public static string LocalDateKey(DateTime? localNow = null) =>
        (localNow ?? DateTime.Now).ToString("yyyy-MM-dd");

    public static void Touch(AppSettings settings)
    {
        if (settings.LastSeenUtc == default)
        {
            settings.LastSeenUtc = DateTime.UtcNow;
            return;
        }

        var now = DateTime.UtcNow;
        if (now > settings.LastSeenUtc)
        {
            settings.LastSeenUtc = now;
        }
    }

    /// <summary>供月度清零等功能复用：系统时间正常时才允许“跨月 / 跨天刷新”。</summary>
    public static bool IsClockSane(AppSettings settings)
    {
        ObserveClock(settings, out var timeAnomaly);
        return !timeAnomaly;
    }

    private static bool ObserveClock(AppSettings settings, out bool timeAnomaly)
    {
        var now = DateTime.UtcNow;
        if (settings.LastSeenUtc == default)
        {
            settings.LastSeenUtc = now;
            timeAnomaly = false;
            return true;
        }

        // 系统时间被回拨：不刷新每日额度，也不提前解除任何冷却。
        // 只做保守的回拨检测，避免笔记本休眠 / 唤醒被误判成“改时间”而锁住密码出口。
        if (now + TimeSpan.FromMinutes(2) < settings.LastSeenUtc)
        {
            timeAnomaly = true;
            return true;
        }

        if (now > settings.LastSeenUtc)
        {
            settings.LastSeenUtc = now;
        }

        timeAnomaly = false;
        return true;
    }

    private static void EnsureDay(AppSettings settings, bool timeAnomaly)
    {
        if (timeAnomaly)
        {
            return;
        }

        var today = LocalDateKey();
        if (!string.Equals(settings.PasswordExitDate, today, StringComparison.Ordinal))
        {
            settings.PasswordExitDate = today;
            settings.PasswordExitCount = 0;
            settings.PasswordExitCooldownUntilUtc = null;
        }

        if (!string.Equals(settings.RecoveryExitDate, today, StringComparison.Ordinal))
        {
            settings.RecoveryExitDate = today;
            settings.RecoveryExitUsed = false;
        }
    }

    public static ExitAllowance GetPasswordAllowance(AppSettings settings)
    {
        ObserveClock(settings, out var timeAnomaly);
        EnsureDay(settings, timeAnomaly);

        if (timeAnomaly)
        {
            return new ExitAllowance(ExitAllowanceState.Frozen, TimeSpan.Zero,
                settings.PasswordExitCount, PasswordDailyLimit,
                "检测到系统时间异常，密码退出已暂停；可使用恢复码退出，或把系统时间调回正确时间。");
        }

        var now = DateTime.UtcNow;
        if (settings.PasswordExitCount >= PasswordDailyLimit)
        {
            return new ExitAllowance(ExitAllowanceState.Frozen, TimeUntilNextLocalMidnight(),
                settings.PasswordExitCount, PasswordDailyLimit,
                $"今天已经用密码提前退出 {PasswordDailyLimit} 次，密码退出已冻结到明天 00:00。");
        }

        if (settings.PasswordExitCooldownUntilUtc is { } until && until > now)
        {
            return new ExitAllowance(ExitAllowanceState.Cooldown, until - now,
                settings.PasswordExitCount, PasswordDailyLimit,
                $"密码退出冷却中，还剩 {Format(until - now)}；可用恢复码提前结束（每天 1 次）。");
        }

        return new ExitAllowance(ExitAllowanceState.Allowed, TimeSpan.Zero,
            settings.PasswordExitCount, PasswordDailyLimit, string.Empty);
    }

    public static ExitAllowance GetRecoveryExitAllowance(AppSettings settings)
    {
        ObserveClock(settings, out var timeAnomaly);
        EnsureDay(settings, timeAnomaly);

        var today = LocalDateKey();
        if (settings.RecoveryExitUsed &&
            string.Equals(settings.RecoveryExitDate, today, StringComparison.Ordinal))
        {
            return new ExitAllowance(ExitAllowanceState.RecoveryUsed, TimeUntilNextLocalMidnight(),
                1, RecoveryExitDailyLimit,
                $"今天已经用恢复码提前结束过自律，明天 00:00 后才会恢复（还剩 {Format(TimeUntilNextLocalMidnight())}）。");
        }

        return new ExitAllowance(ExitAllowanceState.Allowed, TimeSpan.Zero,
            settings.RecoveryExitUsed ? 1 : 0, RecoveryExitDailyLimit, string.Empty);
    }

    /// <summary>记录一次成功的密码提前退出，并按次数设置冷却 / 冻结。</summary>
    public static void RecordPasswordExit(AppSettings settings)
    {
        ObserveClock(settings, out var timeAnomaly);
        EnsureDay(settings, timeAnomaly);

        settings.PasswordExitCount = Math.Min(PasswordDailyLimit, settings.PasswordExitCount + 1);
        var now = DateTime.UtcNow;

        settings.PasswordExitCooldownUntilUtc = settings.PasswordExitCount switch
        {
            1 => now + FirstCooldown,
            2 => now + SecondCooldown,
            _ => null, // 第 3 次之后直接冻结到次日，不再有冷却结束后的下一次
        };
    }

    /// <summary>记录一次成功的恢复码提前退出。不影响密码退出计数 / 冷却。</summary>
    public static void RecordRecoveryExit(AppSettings settings)
    {
        ObserveClock(settings, out var timeAnomaly);
        EnsureDay(settings, timeAnomaly);

        settings.RecoveryExitDate = LocalDateKey();
        settings.RecoveryExitUsed = true;
    }

    public static TimeSpan TimeUntilNextLocalMidnight()
    {
        var now = DateTime.Now;
        var next = now.Date.AddDays(1);
        return next - now;
    }

    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }
}
