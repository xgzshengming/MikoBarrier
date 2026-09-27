using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

public static class ConfigStore
{
    public static AppSettings Load()
    {
        var settings = JsonStore.Load(StoragePaths.ConfigFile, static () => new AppSettings());
        settings.EnsureDefaults();
        return settings;
    }

    public static void Save(AppSettings settings) => JsonStore.Save(StoragePaths.ConfigFile, settings);
}

public static class StatsStore
{
    public static List<SessionRecord> Load() =>
        JsonStore.Load(StoragePaths.StatsFile, static () => new List<SessionRecord>());

    public static void Save(List<SessionRecord> records)
    {
        // 只保留最近 2000 条
        if (records.Count > 2000)
        {
            records.RemoveRange(0, records.Count - 2000);
        }

        JsonStore.Save(StoragePaths.StatsFile, records);
    }

    public static int CompletedCount(IEnumerable<SessionRecord> records) => records.Count(r => r.Completed);

    public static TimeSpan TotalFocusTime(IEnumerable<SessionRecord> records) =>
        TimeSpan.FromSeconds(records.Sum(r => r.ActualFocusSeconds));
}
