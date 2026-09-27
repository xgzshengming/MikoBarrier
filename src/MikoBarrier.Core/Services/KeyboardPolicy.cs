namespace MikoBarrier.Core.Services;

public enum BlockedKeyKind
{
    None = 0,
    AltTab = 1,
    AltEsc = 2,
    CtrlEsc = 3,
    WindowsKey = 4,
    AltF4 = 5,
    TaskManager = 6,
}

/// <summary>
/// 键盘封锁的判定逻辑（纯函数，方便测试）。
/// 拦不住的东西：Ctrl+Alt+Del（系统安全序列）、拔电源、安全模式。
/// </summary>
public static class KeyboardPolicy
{
    public const int VkTab = 0x09;
    public const int VkEscape = 0x1B;
    public const int VkControl = 0x11;
    public const int VkShift = 0x10;
    public const int VkMenu = 0x12;
    public const int VkF4 = 0x73;
    public const int VkLeftWindows = 0x5B;
    public const int VkRightWindows = 0x5C;

    public static BlockedKeyKind Evaluate(int virtualKey, bool altDown, bool ctrlDown, bool shiftDown, bool winDown)
    {
        // 识别 Win 键（单独的 Win 或 Win+其他键）。产品规则要求放行：
        // KeyboardBlocker 会把 WindowsKey 直接交给系统，保证 Win+V / Win+空格 可用。
        if (virtualKey is VkLeftWindows or VkRightWindows || winDown && virtualKey != VkControl && virtualKey != VkMenu && virtualKey != VkShift)
        {
            return BlockedKeyKind.WindowsKey;
        }

        if (ctrlDown && shiftDown && virtualKey == VkEscape)
        {
            return BlockedKeyKind.TaskManager;
        }

        if (altDown && virtualKey == VkTab)
        {
            return BlockedKeyKind.AltTab;
        }

        if (altDown && virtualKey == VkEscape)
        {
            return BlockedKeyKind.AltEsc;
        }

        if (ctrlDown && virtualKey == VkEscape)
        {
            return BlockedKeyKind.CtrlEsc;
        }

        if (altDown && virtualKey == VkF4)
        {
            return BlockedKeyKind.AltF4;
        }

        return BlockedKeyKind.None;
    }

    public static string Describe(BlockedKeyKind kind) => kind switch
    {
        BlockedKeyKind.AltTab => "Alt+Tab",
        BlockedKeyKind.AltEsc => "Alt+Esc",
        BlockedKeyKind.CtrlEsc => "Ctrl+Esc",
        BlockedKeyKind.WindowsKey => "Windows 键",
        BlockedKeyKind.AltF4 => "Alt+F4",
        BlockedKeyKind.TaskManager => "Ctrl+Shift+Esc",
        _ => string.Empty,
    };
}