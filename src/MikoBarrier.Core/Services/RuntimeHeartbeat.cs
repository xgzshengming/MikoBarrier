using System.Diagnostics;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// App ↔ Guard 互保心跳：各自周期性写 data\runtime\*-heartbeat.json，
/// 对方通过心跳新鲜度 + PID + 进程名 + 启动时间判断是否还活着。
/// </summary>
public static class RuntimeHeartbeat
{
    public const string AppRole = "app";
    public const string GuardRole = "guard";

    public static void Write(string role, string sessionId)
    {
        try
        {
            StoragePaths.EnsureCreated();
            var self = Process.GetCurrentProcess();
            var record = new HeartbeatRecord
            {
                Role = role,
                Pid = self.Id,
                ProcessStartUtc = SafeStartTime(self),
                SessionId = sessionId,
                WrittenUtc = DateTime.UtcNow,
            };

            JsonStore.Save(PathFor(role), record);
            self.Dispose();
        }
        catch
        {
            // 心跳只是防绕过辅助信息，失败不影响主流程。
        }
    }

    public static HeartbeatRecord? Read(string role)
    {
        try
        {
            var record = JsonStore.Load<HeartbeatRecord?>(PathFor(role), static () => null);
            if (record is null || record.Pid <= 0)
            {
                return null;
            }

            return record;
        }
        catch
        {
            return null;
        }
    }

    public static void Clear(string role)
    {
        try
        {
            var path = PathFor(role);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    /// <summary>判断心跳对应的进程是否仍然活着、是不是同一个会话、心跳是否足够新。</summary>
    public static bool IsAlive(
        HeartbeatRecord? record,
        string expectedProcessName,
        string? expectedSessionId,
        TimeSpan maxAge,
        out string reason)
    {
        if (record is null || record.Pid <= 0)
        {
            reason = "没有心跳文件";
            return false;
        }

        if (!string.IsNullOrEmpty(expectedSessionId) &&
            !string.Equals(record.SessionId, expectedSessionId, StringComparison.Ordinal))
        {
            reason = "心跳属于旧会话";
            return false;
        }

        if (DateTime.UtcNow - record.WrittenUtc > maxAge)
        {
            reason = $"心跳过期（{Math.Round((DateTime.UtcNow - record.WrittenUtc).TotalSeconds)} 秒前）";
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(record.Pid);
            var name = process.ProcessName;
            if (!string.Equals(Normalize(name), Normalize(expectedProcessName), StringComparison.OrdinalIgnoreCase))
            {
                reason = $"PID {record.Pid} 已被 {name} 复用";
                return false;
            }

            // 部分系统进程可能读不到 StartTime；读不到时以“进程名 + 心跳”为准。
            try
            {
                var start = SafeStartTime(process);
                if (record.ProcessStartUtc != default && start != default &&
                    Math.Abs((start - record.ProcessStartUtc).TotalSeconds) > 5)
                {
                    reason = "进程启动时间与心跳不一致（PID 复用）";
                    return false;
                }
            }
            catch
            {
            }

            reason = string.Empty;
            return true;
        }
        catch (ArgumentException)
        {
            reason = "心跳对应的进程已不存在";
            return false;
        }
        catch (InvalidOperationException)
        {
            reason = "心跳对应的进程已退出";
            return false;
        }
        catch
        {
            // 权限不足：只要心跳新、名字对得上，先视为活着，避免误杀 / 误重启。
            reason = string.Empty;
            return true;
        }
    }

    private static string PathFor(string role) => Path.Combine(StoragePaths.RuntimeDir, $"{role}-heartbeat.json");

    private static string Normalize(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    private static DateTime SafeStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return default;
        }
    }
}
