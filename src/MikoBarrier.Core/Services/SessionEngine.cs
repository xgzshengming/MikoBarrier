using System.Diagnostics;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 专注会话状态机：专注 → （中场休息 → 专注）… → 完成。
///
/// 计时基于 Stopwatch（单调时钟），用户修改系统时间无法延长或缩短专注。
/// 每约 10 秒把完整快照写入 data/session.lock：断电 / 蓝屏 / 被强杀后可以"继续"，
/// 只有主动点退出才算中断（不计入自律次数，剩余时间记为欠债）。
/// </summary>
public sealed class SessionEngine
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(3);

    private readonly Stopwatch _phaseWatch = new();
    private double _phaseSeconds;
    private double _focusSecondsDone;
    private readonly List<TaskAward> _taskAwards = new();
    private DateTime _startedUtc;
    private DateTime _lastSnapshotUtc = DateTime.MinValue;

    public SessionPhase Phase { get; private set; } = SessionPhase.Idle;

    /// <summary>本次自律的唯一 ID；崩溃恢复后沿用，用于关联互保 Kill 事件。</summary>
    public string SessionId { get; private set; } = string.Empty;

    public FocusPlan? Plan { get; private set; }

    public int CurrentRound { get; private set; }

    public int TotalRounds { get; private set; }

    public int RoundsCompleted { get; private set; }

    public DateTime StartedUtc => _startedUtc;

    public bool IsActive => Phase is SessionPhase.Focusing or SessionPhase.Breaking;

    public bool IsBreak => Phase == SessionPhase.Breaking;

    public TimeSpan Remaining
    {
        get
        {
            var seconds = _phaseSeconds - _phaseWatch.Elapsed.TotalSeconds;
            return TimeSpan.FromSeconds(Math.Max(0, seconds));
        }
    }

    public TimeSpan PhaseTotal => TimeSpan.FromSeconds(_phaseSeconds);

    public double OverallProgress
    {
        get
        {
            if (Plan is null)
            {
                return 0;
            }

            var total = Plan.TotalFocusSeconds;
            if (total <= 0)
            {
                return 0;
            }

            var done = _focusSecondsDone + (Phase == SessionPhase.Focusing ? _phaseWatch.Elapsed.TotalSeconds : 0);
            return Math.Clamp(done / total, 0, 1);
        }
    }

    public event EventHandler? Changed;

    public event EventHandler<SessionRecord>? SessionEnded;

    public void Start(AppSettings settings, FocusPlan plan)
    {
        Plan = plan.Clone();
        TotalRounds = Plan.ClampRounds();
        CurrentRound = 1;
        RoundsCompleted = 0;
        _taskAwards.Clear();
        _focusSecondsDone = 0;
        _startedUtc = DateTime.UtcNow;
        SessionId = Guid.NewGuid().ToString("N");

        // 主动中断欠债 + 被 Kill 的惩罚欠债一起加在本次第一轮，还完即清零。
        // 跨月时两类欠债各自按月度规则清零（提前退出欠债另见 ExitDebtPolicy）。
        KillPenaltyPolicy.EnsureMonth(settings);
        ExitDebtPolicy.EnsureMonth(settings);
        var debt = Math.Max(0, settings.DebtSeconds) + Math.Max(0, settings.KillPenaltyDebtSeconds);
        _phaseSeconds = (Plan.GetRoundMinutes(0) * 60.0) + debt;
        settings.DebtSeconds = 0;
        settings.KillPenaltyDebtSeconds = 0;

        Phase = SessionPhase.Focusing;
        _phaseWatch.Restart();
        PersistSnapshot(force: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>断电 / 蓝屏 / 被强杀之后，用 session.lock 里的快照继续本次专注。</summary>
    public bool Resume(AppSettings settings, SessionSnapshot snapshot)
    {
        if (!snapshot.IsValid || snapshot.Plan is null)
        {
            return false;
        }

        Plan = snapshot.Plan.Clone();
        // 兼容旧快照：没有 SessionId 时沿用与 Guard 相同的 legacy key，保证互保心跳能对上。
        SessionId = SessionRecovery.GetSessionKey(snapshot);
        if (string.IsNullOrWhiteSpace(SessionId))
        {
            SessionId = Guid.NewGuid().ToString("N");
        }
        TotalRounds = Math.Max(1, snapshot.TotalRounds);
        CurrentRound = Math.Clamp(snapshot.CurrentRound, 1, TotalRounds);
        RoundsCompleted = Math.Clamp(snapshot.RoundsCompleted, 0, TotalRounds);
        _focusSecondsDone = Math.Max(0, snapshot.FocusSecondsDone);
        _taskAwards.Clear();
        _taskAwards.AddRange((snapshot.TaskAwards ?? new List<TaskAward>())
            .Where(a => !string.IsNullOrWhiteSpace(a.TaskId))
            .Select(a => a.Clone()));
        _startedUtc = snapshot.StartedUtc == default ? DateTime.UtcNow : snapshot.StartedUtc;

        Phase = Enum.TryParse<SessionPhase>(snapshot.Phase, out var phase) && phase == SessionPhase.Breaking
            ? SessionPhase.Breaking
            : SessionPhase.Focusing;

        _phaseSeconds = Math.Max(1, snapshot.PhaseSecondsRemaining);
        _phaseWatch.Restart();
        PersistSnapshot(force: true);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>由界面定时器（约 250ms）调用，负责推进阶段。</summary>
    public void Tick()
    {
        if (!IsActive || Plan is null)
        {
            return;
        }

        while (IsActive && _phaseWatch.Elapsed.TotalSeconds >= _phaseSeconds)
        {
            if (Phase == SessionPhase.Focusing)
            {
                _focusSecondsDone += _phaseSeconds;
                AwardRound(CurrentRound - 1, _phaseSeconds);
                RoundsCompleted++;

                if (RoundsCompleted >= TotalRounds)
                {
                    Finish();
                    return;
                }

                if (Plan.HasBreak)
                {
                    Phase = SessionPhase.Breaking;
                    _phaseSeconds = Plan.BreakMinutes * 60;
                }
                else
                {
                    CurrentRound++;
                    _phaseSeconds = Plan.GetRoundMinutes(CurrentRound - 1) * 60.0;
                }
            }
            else
            {
                CurrentRound++;
                Phase = SessionPhase.Focusing;
                _phaseSeconds = Plan.GetRoundMinutes(CurrentRound - 1) * 60.0;
            }

            _phaseWatch.Restart();
            PersistSnapshot(force: true);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        PersistSnapshot(force: false);
    }

    /// <summary>凭密码提前结束：未完成的专注时间记为"欠债"，顺延到下一次专注；本次不计入自律次数。</summary>
    public bool Abort(AppSettings settings, string reason)
    {
        if (!IsActive || Plan is null)
        {
            return false;
        }

        var remainingFocus = ComputeRemainingFocusSeconds();
        ExitDebtPolicy.Register(settings, remainingFocus);

        var record = BuildRecord(completed: false, abortReason: reason);

        _phaseWatch.Reset();
        _phaseSeconds = 0;
        Phase = SessionPhase.Aborted;
        ClearSnapshot();
        SessionEnded?.Invoke(this, record);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>完成 / 中断后回到空闲状态（名单、设置解锁）。</summary>
    public void Reset()
    {
        _phaseWatch.Reset();
        _phaseSeconds = 0;
        _focusSecondsDone = 0;
        _taskAwards.Clear();
        Plan = null;
        SessionId = string.Empty;
        CurrentRound = 0;
        TotalRounds = 0;
        RoundsCompleted = 0;
        Phase = SessionPhase.Idle;
        ClearSnapshot();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string PhaseText => Phase switch
    {
        SessionPhase.Idle => "未开始",
        SessionPhase.Focusing when TotalRounds > 1 => $"专注中 · 第 {CurrentRound}/{TotalRounds} 轮",
        SessionPhase.Focusing => "专注中",
        SessionPhase.Breaking => $"中场休息 · 下一轮 {CurrentRound + 1}/{TotalRounds}",
        SessionPhase.Finished => "已完成 ✓",
        SessionPhase.Aborted => "已中断",
        _ => string.Empty,
    };

    public SessionSnapshot? CaptureSnapshot() => Plan is null
        ? null
        : new SessionSnapshot
        {
            SessionId = SessionId,
            Plan = Plan.Clone(),
            Phase = Phase.ToString(),
            CurrentRound = CurrentRound,
            TotalRounds = TotalRounds,
            RoundsCompleted = RoundsCompleted,
            TaskAwards = _taskAwards.Select(a => a.Clone()).ToList(),
            PhaseSecondsRemaining = Math.Max(0, _phaseSeconds - _phaseWatch.Elapsed.TotalSeconds),
            FocusSecondsDone = _focusSecondsDone,
            StartedUtc = _startedUtc,
            SavedUtc = DateTime.UtcNow,
            Pid = Environment.ProcessId,
        };

    /// <summary>把一轮完成的成绩记到本轮关联的任务上；同一轮多个任务时按任务数均分秒数。</summary>
    private void AwardRound(int roundIndex, double focusedSeconds)
    {
        if (Plan is null)
        {
            return;
        }

        var taskIds = Plan.GetRoundTaskIds(roundIndex);
        if (taskIds.Count == 0)
        {
            return;
        }

        var total = (int)Math.Round(Math.Max(0, focusedSeconds));
        var baseSeconds = total / taskIds.Count;
        var remainder = total % taskIds.Count;

        for (var i = 0; i < taskIds.Count; i++)
        {
            var id = taskIds[i];
            var award = _taskAwards.FirstOrDefault(a => string.Equals(a.TaskId, id, StringComparison.Ordinal));
            if (award is null)
            {
                award = new TaskAward { TaskId = id };
                _taskAwards.Add(award);
            }

            award.Rounds += 1;
            award.FocusSeconds += baseSeconds + (i < remainder ? 1 : 0);
        }
    }


    private double ComputeRemainingFocusSeconds()
    {
        if (Plan is null)
        {
            return 0;
        }

        // 当前轮之后的每一轮时长之和（每轮可以不同）。
        var perRound = 0.0;
        for (var i = CurrentRound; i < TotalRounds; i++)
        {
            perRound += Plan.GetRoundMinutes(i) * 60.0;
        }
        return Phase == SessionPhase.Focusing
            ? Math.Max(0, _phaseSeconds - _phaseWatch.Elapsed.TotalSeconds) + ((TotalRounds - CurrentRound) * perRound)
            : (TotalRounds - CurrentRound) * perRound;
    }

    private void Finish()
    {
        var record = BuildRecord(completed: true, abortReason: string.Empty);
        _phaseWatch.Reset();
        _phaseSeconds = 0;
        Phase = SessionPhase.Finished;
        ClearSnapshot();
        SessionEnded?.Invoke(this, record);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private SessionRecord BuildRecord(bool completed, string abortReason)
    {
        var patrolEvents = PatrolLog.Load();

        return new SessionRecord
        {
            BlockedProcessCount = patrolEvents.Count(e => !string.Equals(e.Action, "Failed", StringComparison.OrdinalIgnoreCase)),
            BlockedProcessFailedCount = patrolEvents.Count(e => string.Equals(e.Action, "Failed", StringComparison.OrdinalIgnoreCase)),
            BlockedProcessNames = string.Join("、", patrolEvents
                .Select(e => e.ProcessName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)),
        StartedUtc = _startedUtc,
        EndedUtc = DateTime.UtcNow,
        PlannedFocusSeconds = Plan?.TotalFocusSeconds ?? 0,
        ActualFocusSeconds = (int)Math.Round(_focusSecondsDone),
        RoundsPlanned = TotalRounds,
        RoundsCompleted = RoundsCompleted,
        TaskAwards = _taskAwards.Select(a => a.Clone()).ToList(),
        Completed = completed,
            AbortReason = abortReason,
            PlanSummary = Plan?.ToString() ?? string.Empty,
        };
    }

    private void PersistSnapshot(bool force)
    {
        if (Plan is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && now - _lastSnapshotUtc < SnapshotInterval)
        {
            return;
        }

        _lastSnapshotUtc = now;

        try
        {
            var snapshot = CaptureSnapshot();
            if (snapshot is not null)
            {
                JsonStore.Save(StoragePaths.SessionLockFile, snapshot);
            }
        }
        catch
        {
            // 快照只是崩溃恢复用的辅助信息，失败不影响主流程。
        }
    }

    private static void ClearSnapshot()
    {
        try
        {
            if (File.Exists(StoragePaths.SessionLockFile))
            {
                File.Delete(StoragePaths.SessionLockFile);
            }
        }
        catch
        {
        }
    }
}
