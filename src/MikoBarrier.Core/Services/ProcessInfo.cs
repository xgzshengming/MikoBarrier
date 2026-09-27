namespace MikoBarrier.Core.Services;

/// <summary>
/// 巡逻看到的进程快照。
///
/// ParentPid：父进程 PID，用于白名单模式的「整条启动链路」判定。
/// StartedUtc：尽力采集的进程启动时间，用于避免 PID 复用造成的错误父子关系（读不到时为 null）。
/// </summary>
public sealed record ProcessInfo(
    int Pid,
    string Name,
    string FilePath,
    string WindowTitle,
    int ParentPid = 0,
    DateTime? StartedUtc = null)
{
    /// <summary>是否有可见的主窗口标题。白名单模式只拦截「有窗口」的程序。</summary>
    public bool HasWindow => !string.IsNullOrWhiteSpace(WindowTitle);
}
