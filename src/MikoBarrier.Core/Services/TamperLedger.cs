using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 防绕过事件账本：App / Guard 任何一方发现另一方被结束或失联时，
/// 只写一个独立事件文件（不做跨进程共享配置写入）；统一由 App 在启动 / 会话开始时入账。
/// </summary>
public static class TamperLedger
{
    private const int MaxRememberedIds = 200;
    private static readonly TimeSpan SameKillGroupWindow = TimeSpan.FromSeconds(15);

    public static void Record(string kind, string sessionId, int sourcePid, string detail)
    {
        try
        {
            StoragePaths.EnsureCreated();
            var incident = new TamperIncident
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = sessionId ?? string.Empty,
                Kind = kind ?? string.Empty,
                Utc = DateTime.UtcNow,
                SourcePid = sourcePid,
                Detail = detail ?? string.Empty,
            };

            var fileName = $"{incident.Utc:yyyyMMddHHmmssfff}-{incident.Id}.json";
            JsonStore.Save(Path.Combine(StoragePaths.IncidentDir, fileName), incident);
        }
        catch
        {
            // 事件文件写失败不阻塞互保重启；下一次仍有心跳/进程检测机会。
        }
    }

    public static List<TamperIncident> LoadPending()
    {
        var result = new List<TamperIncident>();
        try
        {
            if (!Directory.Exists(StoragePaths.IncidentDir))
            {
                return result;
            }

            foreach (var file in Directory.EnumerateFiles(StoragePaths.IncidentDir, "*.json"))
            {
                try
                {
                    var incident = JsonStore.Load<TamperIncident?>(file, static () => null);
                    if (incident is not null &&
                        !string.IsNullOrWhiteSpace(incident.Id) &&
                        incident.Utc != default)
                    {
                        result.Add(incident);
                    }
                }
                catch
                {
                    // 单个坏文件忽略。
                }
            }
        }
        catch
        {
        }

        return result.OrderBy(i => i.Utc).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
    }

    public static bool HasPendingForSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return LoadPending().Any(i => string.Equals(i.SessionId, sessionId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 把所有未入账事件折算成 Kill 惩罚欠债。同一会话 15 秒内的多条事件（App+Guard 双杀）合并算一次。
    /// 返回值表示本次新增入账的事件数，addedSeconds 为实际增加欠债秒数。
    /// 调用方应先保存 settings，再调用 CleanupApplied 删除已入账文件。
    /// </summary>
    public static int ApplyPending(AppSettings settings, out int addedSeconds)
    {
        addedSeconds = 0;
        KillPenaltyPolicy.EnsureMonth(settings);

        var pending = LoadPending();
        if (pending.Count == 0)
        {
            return 0;
        }

        var applied = new HashSet<string>(
            (settings.AppliedKillIncidentIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        var appliedOrder = (settings.AppliedKillIncidentIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .TakeLast(MaxRememberedIds)
            .ToList();

        var newCount = 0;
        var lastSession = settings.KillPenaltyLastSessionId ?? string.Empty;
        var lastUtc = settings.KillPenaltyLastUtc;

        foreach (var incident in pending)
        {
            var id = string.IsNullOrWhiteSpace(incident.Id)
                ? Guid.NewGuid().ToString("N")
                : incident.Id;

            if (!applied.Add(id))
            {
                continue;
            }

            appliedOrder.Add(id);
            if (appliedOrder.Count > MaxRememberedIds)
            {
                appliedOrder.RemoveRange(0, appliedOrder.Count - MaxRememberedIds);
            }

            var sameBurst = !string.IsNullOrEmpty(lastSession) &&
                            string.Equals(lastSession, incident.SessionId, StringComparison.Ordinal) &&
                            incident.Utc >= lastUtc &&
                            incident.Utc - lastUtc <= SameKillGroupWindow;

            if (sameBurst)
            {
                if (incident.Utc > lastUtc)
                {
                    lastUtc = incident.Utc;
                }

                continue;
            }

            addedSeconds += KillPenaltyPolicy.RegisterKill(settings);
            newCount++;
            lastSession = incident.SessionId;
            lastUtc = incident.Utc;
        }

        settings.AppliedKillIncidentIds = appliedOrder;
        settings.KillPenaltyLastSessionId = lastSession;
        settings.KillPenaltyLastUtc = lastUtc;
        return newCount;
    }

    /// <summary>删除已入账的事件文件；应在 settings 保存成功后调用。</summary>
    public static void CleanupApplied(AppSettings settings)
    {
        var applied = new HashSet<string>(
            (settings.AppliedKillIncidentIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);

        try
        {
            if (!Directory.Exists(StoragePaths.IncidentDir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(StoragePaths.IncidentDir, "*.json"))
            {
                try
                {
                    var incident = JsonStore.Load<TamperIncident?>(file, static () => null);
                    if (incident is not null && applied.Contains(incident.Id))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // 文件被占用 / 已删除：下次再清理。
                }
            }
        }
        catch
        {
        }
    }
}
