using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MikoBarrier;

/// <summary>
/// 托盘图标（用 WinForms 的 NotifyIcon 提供系统托盘入口，菜单用 WPF 自绘以跟随主题）。
/// 左键单击直接打开主界面；右键弹出菜单；自律进行中"退出"不可用。
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _appIcon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _openItem;
    private readonly MenuItem _startFocusItem;
    private readonly MenuItem _backToTimerItem;
    private readonly MenuItem _exitItem;
    private Window? _menuAnchor;
    private DateTime _menuClosedAtUtc = DateTime.MinValue;

    public TrayIconHost(Action open, Action startFocus, Action backToTimer, Action exit)
    {
        _menu = new ContextMenu();
        _menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenu");
        _menu.SetResourceReference(Control.FontFamilyProperty, "Font.UI");
        _menu.Closed += (_, _) =>
        {
            _menuAnchor?.Hide();
            _menuClosedAtUtc = DateTime.UtcNow;
        };
        _openItem = CreateItem("打开主界面", open);
        _startFocusItem = CreateItem("开始自律", startFocus);
        _backToTimerItem = CreateItem("回到全屏计时", backToTimer);
        _menu.Items.Add(_openItem);
        _menu.Items.Add(_startFocusItem);
        _menu.Items.Add(_backToTimerItem);
        var separator = new Separator();
        separator.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuSeparator");
        _menu.Items.Add(separator);

        _exitItem = CreateItem("退出 MikoBarrier", exit);
        _menu.Items.Add(_exitItem);

        _appIcon = LoadAppIcon();
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _appIcon ?? System.Drawing.SystemIcons.Shield,
            Text = MikoText.T("MikoBarrier · 巫女结界"),
            Visible = false,
        };

        // 左键单击：直接打开 / 恢复主界面（双击也走这里，恢复操作是幂等的）。
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                open();
            }
        };

        // 右键：显示 WPF 自绘菜单（不再用 WinForms 的系统菜单外观）。
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                ToggleMenu();
            }
        };
    }

    /// <summary>自检用：确认托盘菜单已经是 WPF 自绘菜单。</summary>
    internal ContextMenu Menu => _menu;

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    public void SetExitEnabled(bool enabled) => _exitItem.IsEnabled = enabled;

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(2500);
    }

    private static MenuItem CreateItem(string header, Action action)
    {
        var item = new MenuItem { Header = MikoText.T(header) };
        item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItem");
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>模式切换后刷新托盘菜单 / 图标的文案。</summary>
    public void RefreshModeTexts()
    {
        _openItem.Header = MikoText.T("打开主界面");
        _startFocusItem.Header = MikoText.T("开始自律");
        _backToTimerItem.Header = MikoText.T("回到全屏计时");
        _exitItem.Header = MikoText.T("退出 MikoBarrier");
        _icon.Text = MikoText.T("MikoBarrier · 巫女结界");
    }

    /// <summary>
    /// WPF 的 PlacementMode.MousePoint 在托盘图标上没有可用的 WPF 鼠标输入，
    /// 所以用 1x1 的隐藏窗口作为锚点，把菜单放到物理光标所在的位置。
    /// </summary>
    private void ToggleMenu()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            if (_menu.IsOpen)
            {
                _menu.IsOpen = false;
                return;
            }

            // 点托盘图标让已打开的菜单先关闭后，NotifyIcon 还会补发一次 MouseUp；
            // 250ms 内不重新打开，避免出现“关不掉 / 闪烁”的手感。
            if ((DateTime.UtcNow - _menuClosedAtUtc).TotalMilliseconds < 250)
            {
                return;
            }

            _menuAnchor ??= CreateMenuAnchor();
            var cursor = System.Windows.Forms.Cursor.Position;
            var (left, top) = ToDeviceIndependent(cursor);
            _menuAnchor.Left = left;
            _menuAnchor.Top = top;
            if (!_menuAnchor.IsVisible)
            {
                _menuAnchor.Show();
            }

            _menu.PlacementTarget = _menuAnchor;
            _menu.Placement = PlacementMode.Bottom;
            // 模板四周留了 10px 阴影空间，抵消后让菜单可见部分贴着光标。
            _menu.HorizontalOffset = -10;
            _menu.VerticalOffset = -10;
            _menu.IsOpen = true;
        });
    }

    /// <summary>托盘图标优先用应用图标（Assets\MikoBarrier.ico），取不到时退回系统盾牌。</summary>
    private static System.Drawing.Icon? LoadAppIcon()
    {
        using var stream = AppIcon.OpenIconStream();
        if (stream is null)
        {
            return null;
        }

        try
        {
            return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        }
        catch
        {
            return null;
        }
    }

    private static Window CreateMenuAnchor() => new()
    {
        Width = 1,
        Height = 1,
        WindowStyle = WindowStyle.None,
        AllowsTransparency = true,
        Background = Brushes.Transparent,
        Opacity = 0,
        ShowInTaskbar = false,
        ShowActivated = false,
        ResizeMode = ResizeMode.NoResize,
    };

    private static (double Left, double Top) ToDeviceIndependent(System.Drawing.Point pixel)
    {
        try
        {
            var window = Application.Current?.MainWindow;
            if (window is not null)
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                return (pixel.X / dpi.DpiScaleX, pixel.Y / dpi.DpiScaleY);
            }
        }
        catch
        {
            // 窗口尚未连接 PresentationSource 时按 100% 处理，宁可略有偏差也不要打不开菜单。
        }

        return (pixel.X, pixel.Y);
    }

    public void Dispose()
    {
        _menu.IsOpen = false;
        _menuAnchor?.Close();
        _menuAnchor = null;
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon?.Dispose();
    }
}
