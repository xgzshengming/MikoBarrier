using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 拦截记录：看门狗把每次真正处理掉的程序写进 data/patrol-log.json，
/// 主程序在自律结束时读出来，生成"本次拦截 N 个程序"的战报。
/// </summary>
public static class PatrolLog
{
    private const int MaxEntries = 500;

    public static List<PatrolEvent> Load() =>
        JsonStore.Load(StoragePaths.PatrolLogFile, static () => new List<PatrolEvent>());

    public static void Save(List<PatrolEvent> events)
    {
        if (events.Count > MaxEntries)
        {
            events.RemoveRange(0, events.Count - MaxEntries);
        }

        JsonStore.Save(StoragePaths.PatrolLogFile, events);
    }

    public static void Add(IEnumerable<PatrolEvent> events)
    {
        var list = Load();
        list.AddRange(events);
        Save(list);
    }

    /// <summary>开始新的自律时清空，这样战报只统计本次。</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(StoragePaths.PatrolLogFile))
            {
                File.Delete(StoragePaths.PatrolLogFile);
            }
        }
        catch
        {
        }
    }

    public static string Summarize(IReadOnlyList<PatrolEvent> events)
    {
        if (events.Count == 0)
        {
            return "本次没有拦截到任何程序";
        }

        var handled = events.Where(e => !string.Equals(e.Action, "Failed", StringComparison.OrdinalIgnoreCase)).ToList();
        var failed = events.Count - handled.Count;

        var text = handled.Count == 0
            ? "本次没有成功拦截到程序"
            : $"共拦截 {handled.Count} 次：" + string.Join("、", handled
                .Select(e => e.ProcessName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6));

        if (failed > 0)
        {
            text += $"；另有 {failed} 次拦截失败（多为权限不足，建议以管理员身份运行）";
        }

        return text;
    }
}