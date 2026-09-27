using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

namespace MikoBarrier;

/// <summary>全局状态：设置、统计、会话状态机、崩溃恢复快照。</summary>
public sealed class AppState
{
    public AppSettings Settings { get; }

    public List<SessionRecord> Records { get; }

    public SessionEngine Engine { get; } = new();

    /// <summary>新手引导状态（哪些功能页已经看过首次演示）。</summary>
    public UiGuideState Guides { get; } = UiGuideState.Load();

    public EnforcementService Enforcement { get; } = new();

    /// <summary>启动时发现的异常中断快照（由主窗口询问是否继续）。</summary>
    public SessionSnapshot? PendingRecovery { get; set; }

    /// <summary>设置被保存时触发（主窗口据此更新托盘显隐等）。</summary>
    public event Action? SettingsChanged;

    public AppState()
    {
        Settings = ConfigStore.Load();
        Records = StatsStore.Load();

        Engine.SessionEnded += (_, record) =>
        {
            record.BlockedKeyCount = KeyboardGuard.BlockedCount;
            if (record.TaskAwards is { Count: > 0 })
            {
                foreach (var award in record.TaskAwards)
                {
                    if (!string.IsNullOrWhiteSpace(award.TaskId))
                    {
                        TaskStore.ApplySession(award.TaskId, award.Rounds, award.FocusSeconds);
                    }
                }
            }
            Records.Add(record);
            Save();
        };
    }

    public void Save()
    {
        SessionExitPolicy.Touch(Settings);
        ConfigStore.Save(Settings);
        StatsStore.Save(Records);
        SettingsChanged?.Invoke();
    }

    /// <summary>继续上次异常中断的专注，并把网络封锁重新应用上。</summary>
    public (bool Ok, string Message) ResumePendingSession()
    {
        var snapshot = PendingRecovery;
        PendingRecovery = null;

        if (snapshot?.Plan is null)
        {
            return (false, "没有可恢复的专注快照。");
        }

        if (!Engine.Resume(Settings, snapshot))
        {
            return (false, "快照内容不完整，无法恢复。");
        }

        var messages = Enforcement.Apply(Settings, Engine.Plan!);
        return (true, EnforcementService.Join(messages));
    }

    /// <summary>放弃恢复：丢弃快照，不计入自律次数，也不产生欠债。</summary>
    public void DiscardPendingSession()
    {
        PendingRecovery = null;
        SessionRecovery.Discard();
    }
}
