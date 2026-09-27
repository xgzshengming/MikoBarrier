using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 巫女模式的解锁与切换规则。模式本身只翻转设置；真正的文案 / 主题变化由 App 层执行。
/// </summary>
public static class MikoModePolicy
{
    /// <summary>通过全屏计时的“裂缝”解锁巫女模式，并立即进入巫女模式。</summary>
    public static void Unlock(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.MikoModeUnlocked = true;
        settings.MikoModeEnabled = true;
    }

    /// <summary>
    /// 切换普通 / 巫女模式。未解锁时不允许切到巫女模式，返回 false。
    /// </summary>
    public static bool TrySetEnabled(AppSettings settings, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (enabled && !settings.MikoModeUnlocked)
        {
            return false;
        }

        settings.MikoModeEnabled = enabled;
        return true;
    }
}
