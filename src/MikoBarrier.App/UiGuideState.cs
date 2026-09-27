using MikoBarrier.Core.Services;

namespace MikoBarrier;

/// <summary>
/// 新手引导状态：只记录哪些功能页的首次演示已经看过。
/// 存 data\ui-state.json，和 config.json 的自律规则完全隔离，损坏时自动回退为空状态。
/// </summary>
public sealed class UiGuideState
{
    public List<string> SeenGuides { get; set; } = new();

    public bool HasSeen(string key) =>
        !string.IsNullOrWhiteSpace(key) &&
        SeenGuides.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));

    public void MarkSeen(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || HasSeen(key))
        {
            return;
        }

        SeenGuides.Add(key);
        Save();
    }

    public void ResetAll()
    {
        SeenGuides.Clear();
        Save();
    }

    public void Save()
    {
        try
        {
            JsonStore.Save(StoragePaths.UiStateFile, this);
        }
        catch
        {
            // 引导状态写失败不影响任何自律功能，下次最多再演示一遍。
        }
    }

    public static UiGuideState Load()
    {
        var state = JsonStore.Load(StoragePaths.UiStateFile, () => new UiGuideState());
        if (state.SeenGuides is null)
        {
            state.SeenGuides = new List<string>();
        }

        return state;
    }
}
