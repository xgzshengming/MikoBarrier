namespace MikoBarrier.Core.Models;

/// <summary>App / Guard 互保心跳。写入 data\runtime 下的独立文件。</summary>
public sealed class HeartbeatRecord
{
    public string Role { get; set; } = string.Empty;

    public int Pid { get; set; }

    /// <summary>进程启动 UTC 时间，用来防止 PID 复用后误判为“还活着”。</summary>
    public DateTime ProcessStartUtc { get; set; }

    /// <summary>当前自律的会话 ID；会话切换或结束后不能再用旧心跳。</summary>
    public string SessionId { get; set; } = string.Empty;

    public DateTime WrittenUtc { get; set; } = DateTime.UtcNow;
}
