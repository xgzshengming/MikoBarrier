using System.Runtime.InteropServices;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 低级键盘钩子（WH_KEYBOARD_LL）：自律期间吞掉 Alt+Tab / Alt+Esc / Alt+F4 / Ctrl+Esc / Ctrl+Shift+Esc；
/// Win 键按产品规则放行（Win+V / Win+空格），保证用户剪贴板和输入法可用。
/// 必须在有消息循环的线程（WPF UI 线程）上安装；进程退出时钩子自动失效，不会把用户永久锁死。
/// </summary>
public sealed class KeyboardBlocker : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int LlkhfAltDown = 0x20;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const int VkLeftShift = 0xA0;
    private const int VkRightShift = 0xA1;
    private const int VkLeftMenu = 0xA4;
    private const int VkRightMenu = 0xA5;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hook = IntPtr.Zero;

    public KeyboardBlocker() => _callback = HookCallback;

    public bool IsActive => _hook != IntPtr.Zero;

    // 自己跟踪修饰键状态：不依赖 GetAsyncKeyState（跨权限场景会返回 0，导致拦不住 Alt+Tab / Win）。
    private bool _altDown;
    private bool _ctrlDown;
    private bool _shiftDown;
    private bool _winDown;

    /// <summary>钩子收到过的按键总数（用于自检）。</summary>
    public int SeenKeyCount { get; private set; }

    public int BlockedCount { get; private set; }

    public string LastBlocked { get; private set; } = string.Empty;

    public event Action<BlockedKeyKind>? KeyBlocked;

    public bool Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void ResetCount()
    {
        BlockedCount = 0;
        LastBlocked = string.Empty;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var message = wParam.ToInt32();
        var isDown = message is WmKeyDown or WmSysKeyDown;

        SeenKeyCount++;

        // 判断这次按键时，修饰键是否按着：用钩子自己维护的状态 + 系统给的 LLKHF_ALTDOWN 标志。
        var alt = _altDown || (info.Flags & LlkhfAltDown) != 0;
        var ctrl = _ctrlDown;
        var shift = _shiftDown;
        var win = _winDown;

        UpdateModifierState(info.VirtualKey, isDown);

        var kind = KeyboardPolicy.Evaluate(info.VirtualKey, alt, ctrl, shift, win);
        // Win 键放行：Win+V（剪贴板）、Win+空格（输入法）都是刚需，只保留 Alt+Tab / Alt+F4 这类拦截。
        if (kind == BlockedKeyKind.WindowsKey)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (kind != BlockedKeyKind.None)
        {
            if (isDown)
            {
                BlockedCount++;
                LastBlocked = KeyboardPolicy.Describe(kind);
                KeyBlocked?.Invoke(kind);
            }

            return 1; // 吞掉这次按键，系统收不到
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void UpdateModifierState(int virtualKey, bool isDown)
    {
        switch (virtualKey)
        {
            case KeyboardPolicy.VkMenu:
            case VkLeftMenu:
            case VkRightMenu:
                _altDown = isDown;
                break;

            case KeyboardPolicy.VkControl:
            case VkLeftControl:
            case VkRightControl:
                _ctrlDown = isDown;
                break;

            case KeyboardPolicy.VkShift:
            case VkLeftShift:
            case VkRightShift:
                _shiftDown = isDown;
                break;

            case KeyboardPolicy.VkLeftWindows:
            case KeyboardPolicy.VkRightWindows:
                _winDown = isDown;
                break;
        }
    }

    public void Dispose() => Uninstall();
}