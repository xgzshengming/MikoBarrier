using System.Runtime.InteropServices;
using MikoBarrier.Core.Services;

namespace MikoBarrier;

/// <summary>
/// 自检用工具：向系统注入按键，验证低级键盘钩子到底有没有收到、有没有吞掉。
/// 先注入 F24（几乎所有程序都不响应，即使没拦住也没有副作用），再注入 Alt+Tab（验证吞键），
/// 最后注入 Win 键（验证按产品规则放行）。
/// </summary>
internal static class KeyboardTestHelper
{
    private const int InputKeyboard = 1;
    private const int KeyEventKeyUp = 0x0002;
    private const int VkF24 = 0x87;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamL;
        public ushort ParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion Union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static List<string> Run(KeyboardBlocker blocker)
    {
        var results = new List<string>();

        // 1) 先注入一个"没副作用"的 F24，看看钩子能不能收到按键
        var seenBefore = blocker.SeenKeyCount;
        SendKey(VkF24);
        Thread.Sleep(300);
        results.Add(blocker.SeenKeyCount > seenBefore
            ? "OK   键盘自检：钩子能收到注入的按键"
            : "FAIL 键盘自检：钩子完全收不到按键（钩子没生效）");

        // 2) 再注入 Alt+Tab，验证是否被吞掉
        var blockedBefore = blocker.BlockedCount;
        SendAltTab();
        Thread.Sleep(400);
        results.Add(blocker.BlockedCount > blockedBefore
            ? "OK   键盘自检：Alt+Tab 已被吞掉"
            : "FAIL 键盘自检：Alt+Tab 没有被吞掉");

        // 3) Win 键：硬性规则要求放行（Win+V 剪贴板、Win+空格 输入法都是刚需）
        var seenBeforeWin = blocker.SeenKeyCount;
        var blockedBeforeWin = blocker.BlockedCount;
        SendKey(KeyboardPolicy.VkLeftWindows);
        Thread.Sleep(300);

        if (blocker.SeenKeyCount <= seenBeforeWin)
        {
            results.Add("FAIL 键盘自检：没有收到注入的 Win 键，无法确认放行");
        }
        else
        {
            results.Add(blocker.BlockedCount <= blockedBeforeWin
                ? "OK   键盘自检：Win 键已放行（Win+V 可用）"
                : "FAIL 键盘自检：Win 键被吞掉了（规则要求放行）");
        }

        return results;
    }

    private static void SendKey(int virtualKey)
    {
        var inputs = new[]
        {
            MakeKey(virtualKey, down: true),
            MakeKey(virtualKey, down: false),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static void SendAltTab()
    {
        var inputs = new[]
        {
            MakeKey(KeyboardPolicy.VkMenu, true),
            MakeKey(KeyboardPolicy.VkTab, true),
            MakeKey(KeyboardPolicy.VkTab, false),
            MakeKey(KeyboardPolicy.VkMenu, false),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input MakeKey(int virtualKey, bool down) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = (ushort)virtualKey,
                Flags = down ? 0u : KeyEventKeyUp,
            },
        },
    };
}