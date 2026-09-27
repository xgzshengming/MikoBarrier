using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 崩溃恢复：读取 data/session.lock 里的快照。只有主动退出才算中断，
/// 断电 / 蓝屏 / 被强杀都会留下快照，下次启动询问是否继续。
/// </summary>
public static class SessionRecovery
{
    /// <summary>取兼容旧快照的会话 key：没有 SessionId 的旧快照用 StartedUtc 代替。</summary>
    public static string GetSessionKey(SessionSnapshot? snapshot) =>
        snapshot is null
            ? string.Empty
            : !string.IsNullOrWhiteSpace(snapshot.SessionId)
                ? snapshot.SessionId
                : $"legacy-{snapshot.StartedUtc.Ticks}";

    public static SessionSnapshot? TryLoad()
    {
        try
        {
            if (!File.Exists(StoragePaths.SessionLockFile))
            {
                return null;
            }

            var snapshot = JsonStore.Load<SessionSnapshot?>(StoragePaths.SessionLockFile, static () => null);
            return snapshot is { IsValid: true } ? snapshot : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Discard()
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

        ClearGracefulShutdownMarker();
    }

    /// <summary>关机 / 注销前写入标记：Guard 看到后不把主程序退出当成被 Kill，也不重启主程序。</summary>
    public static void MarkGracefulShutdown()
    {
        try
        {
            StoragePaths.EnsureCreated();
            File.WriteAllText(StoragePaths.ShutdownMarkerFile,
                DateTime.UtcNow.ToString("O"),
                System.Text.Encoding.UTF8);
        }
        catch
        {
        }
    }

    public static void ClearGracefulShutdownMarker()
    {
        try
        {
            if (File.Exists(StoragePaths.ShutdownMarkerFile))
            {
                File.Delete(StoragePaths.ShutdownMarkerFile);
            }
        }
        catch
        {
        }
    }

    /// <summary>是否处于正常关机 / 注销流程。超过 10 分钟的旧标记视为过期，避免影响下次启动。</summary>
    public static bool IsGracefulShutdownPending(TimeSpan? maxAge = null)
    {
        try
        {
            if (!File.Exists(StoragePaths.ShutdownMarkerFile))
            {
                return false;
            }

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(StoragePaths.ShutdownMarkerFile);
            if (age > (maxAge ?? TimeSpan.FromMinutes(10)))
            {
                ClearGracefulShutdownMarker();
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
