using System.Windows;
using MikoBarrier.Controls;
using MikoBarrier.Dialogs;

namespace MikoBarrier;

/// <summary>
/// 新手引导触发入口：每个功能子界面第一次进入时调用一次，引导窗口关闭后写入状态。
/// 自动化自检模式不会弹出引导，避免影响 --ui-smoke-test 的窗口生命周期。
/// </summary>
public static class GuideHooks
{
    public static void ShowOnFirstVisit(Window? owner, string key, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(key) || App.IsAutomatedMode)
        {
            return;
        }

        if (App.State?.Engine.IsActive == true)
        {
            return;
        }

        if (!force && App.State?.Guides.HasSeen(key) == true)
        {
            return;
        }

        try
        {
            var existing = Application.Current?.Windows
                .OfType<GuideDialog>()
                .FirstOrDefault(w => w.IsVisible);
            existing?.Close();

            var dialog = new GuideDialog(key);
            if (owner is { IsVisible: true })
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.Show();
            WindowFx.BringToForeground(dialog);
        }
        catch
        {
            // 引导失败不能影响功能本身。
        }
    }
}
