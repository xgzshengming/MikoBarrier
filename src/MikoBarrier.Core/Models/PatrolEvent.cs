namespace MikoBarrier.Core.Models;

/// <summary>一次实际的拦截记录（由看门狗写入，主程序在结束时汇总成战报）。</summary>
public sealed class PatrolEvent
{
    public DateTime Utc { get; set; } = DateTime.UtcNow;

    public string ProcessName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    /// <summary>Kill = 结束进程，Hide = 隐藏窗口。</summary>
    public string Action { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string ToDisplayString() =>
        $"{Utc.ToLocalTime():HH:mm:ss}  {ProcessName}  {(Action == "Kill" ? "已结束" : "已隐藏")}  ({Reason})";
}