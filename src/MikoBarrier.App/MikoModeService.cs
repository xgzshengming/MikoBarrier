using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

namespace MikoBarrier;

/// <summary>
/// 巫女模式开关的应用层封装：切换模式的同时处理「真·巫女」主题自动套用。
/// 普通模式不会自动恢复旧主题；如果用户之后想换回去，在主题对话框里自己选即可。
/// </summary>
public static class MikoModeService
{
    /// <summary>通过全屏裂缝解锁巫女模式，并自动套用「真·巫女」主题。</summary>
    public static void Unlock(AppSettings settings)
    {
        MikoModePolicy.Unlock(settings);
        ApplyMikoTheme(settings);
    }

    /// <summary>在设置页切换普通 / 巫女模式；未解锁时不能切到巫女模式。</summary>
    public static bool TrySetEnabled(AppSettings settings, bool enabled)
    {
        if (!MikoModePolicy.TrySetEnabled(settings, enabled))
        {
            return false;
        }

        if (enabled)
        {
            ApplyMikoTheme(settings);
        }

        return true;
    }

    /// <summary>把外观设置改成「真·巫女」：白底 + 高饱和橙粉朱红。</summary>
    public static void ApplyMikoTheme(AppSettings settings)
    {
        settings.Theme = ThemeKind.Light;
        settings.ThemePreset = ThemeCatalog.MikoThemeId;
        settings.CustomBackgroundColor = string.Empty;
    }
}
