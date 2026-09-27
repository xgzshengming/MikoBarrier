using System.IO;
using MikoBarrier.Core.Services;

namespace MikoBarrier;

/// <summary>
/// 键盘封锁的统一入口：自律期间安装低级键盘钩子，中场休息 / 自律结束时卸载。
/// 钩子随进程存在，进程退出即失效，不会把用户永久锁死。
/// </summary>
internal static class KeyboardGuard
{
    private static KeyboardBlocker? _blocker;

    public static KeyboardBlocker? Blocker => _blocker;

    public static int BlockedCount => _blocker?.BlockedCount ?? 0;

    public static string LastBlocked => _blocker?.LastBlocked ?? string.Empty;

    public static (bool Ok, string Message) Enable()
    {
        _blocker ??= new KeyboardBlocker();

        if (_blocker.IsActive)
        {
            return (true, "键盘封锁已生效");
        }

        var installed = _blocker.Install();
        Log(installed
            ? "键盘封锁：安装成功（Alt+Tab / Alt+Esc / Alt+F4 / Ctrl+Esc / Ctrl+Shift+Esc 将被吞掉；Win 键放行）"
            : "键盘封锁：安装失败");

        return installed
            ? (true, "键盘封锁已生效（Alt+Tab / Alt+Esc / Alt+F4 / Ctrl+Esc 已失效；Win 键保留，Win+V / Win+空格 可用）")
            : (false, "键盘封锁安装失败（可能被杀软拦截，或当前线程没有消息循环）");
    }

    public static void Disable()
    {
        if (_blocker?.IsActive != true)
        {
            return;
        }

        var count = _blocker.BlockedCount;
        _blocker.Uninstall();
        Log($"键盘封锁：已解除（本次共吞掉 {count} 次组合键）");
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.LogDir);
            File.AppendAllText(StoragePaths.AppLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public static void ResetCount() => _blocker?.ResetCount();
}