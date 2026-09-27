using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MikoBarrier.Controls;

/// <summary>
/// 无边框窗口的两个外观修正：
///  1) Windows 11 圆角（DWM 属性，Win10 上自动忽略）；
///  2) 最大化时不超出工作区（不会盖住任务栏）。
/// </summary>
internal static class WindowFx
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmcpRound = 2;

    private static int _foregroundRequestCount;

    /// <summary>自检探针用：请求过多少次“把窗口拉到最前”。只增不减。</summary>
    public static int ForegroundRequestCount => Volatile.Read(ref _foregroundRequestCount);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    /// <summary>
    /// 一次性把窗口显示到最前：用于首次启动和托盘恢复。
    /// 只做短暂 Topmost 脉冲（立刻恢复 false），绝不常驻置顶、绝不周期性抢焦点。
    /// </summary>
    public static void BringToForeground(Window window)
    {
        if (window is null)
        {
            return;
        }

        try
        {
            if (!window.IsVisible)
            {
                window.Show();
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            Interlocked.Increment(ref _foregroundRequestCount);

            // 任务计划程序 / SYSTEM 看门狗拉起的进程会失去前台权限：
            // 先短暂置顶保证可见，再恢复 false，避免变成长期置顶。
            window.Topmost = true;
            window.Topmost = false;
            window.Activate();

            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            // 用 AttachThreadInput 绕过系统的前台锁定限制。
            var foreground = GetForegroundWindow();
            var targetThread = GetWindowThreadProcessId(foreground, out _);
            var currentThread = GetCurrentThreadId();
            var attached = targetThread != 0 &&
                           targetThread != currentThread &&
                           AttachThreadInput(currentThread, targetThread, true);

            try
            {
                SetForegroundWindow(handle);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThread, targetThread, false);
                }
            }

            window.Focus();
        }
        catch
        {
            // 前台权限失败不影响程序其余功能；窗口仍然可见。
        }
    }

    public static void ApplyRoundedCorners(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var preference = DwmcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // Windows 10 不支持该属性，忽略。
        }
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    public static void AttachMaximizeFix(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
        }
        catch
        {
        }
    }

    private static IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        minMax.MaxSize.X = info.Work.Right - info.Work.Left;
        minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        minMax.MaxTrackSize.X = minMax.MaxSize.X;
        minMax.MaxTrackSize.Y = minMax.MaxSize.Y;
        Marshal.StructureToPtr(minMax, lParam, true);

        handled = true;
        return IntPtr.Zero;
    }
}