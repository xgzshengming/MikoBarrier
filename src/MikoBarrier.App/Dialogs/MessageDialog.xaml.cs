using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MikoBarrier.Controls;

namespace MikoBarrier.Dialogs;

public enum MessageDialogKind
{
    Info,
    Warning,
    Question,
    Error,
}

/// <summary>应用内自绘的提示框：跟随白天 / 黑夜主题，不用系统 MessageBox 的老式外观。</summary>
public partial class MessageDialog : Window
{
    public MessageDialog(string title, string message, MessageDialogKind kind, bool showCancel, string okText, string cancelText)
    {
        InitializeComponent();

        Title = title;
        TitleBarControl.Title = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        CancelButton.Content = cancelText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

        var (glyph, brushKey) = kind switch
        {
            MessageDialogKind.Warning => ("⚠", "Brush.Accent"),
            MessageDialogKind.Error => ("✖", "Brush.Danger"),
            MessageDialogKind.Question => ("❓", "Brush.Accent"),
            _ => ("ℹ", "Brush.SubText"),
        };

        IconText.Text = glyph;
        if (TryFindResource(brushKey) is Brush brush)
        {
            IconText.Foreground = brush;
        }

        SourceInitialized += (_, _) =>
        {
            WindowFx.ApplyRoundedCorners(this);
            ClampToWorkArea();
        };
        Loaded += (_, _) => OkButton.Focus();
    }

    /// <summary>
    /// 按窗口所在屏幕的工作区限制最大尺寸：长文本会被 ScrollViewer 收住，
    /// 底部按钮永远留在屏幕内（修复小屏 / 竖屏上确认键被顶出屏幕的问题）。
    /// </summary>
    private void ClampToWorkArea()
    {
        double workWidth;
        double workHeight;

        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var dpi = VisualTreeHelper.GetDpi(this);
            workWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
            workHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
        }
        catch
        {
            workWidth = SystemParameters.WorkArea.Width;
            workHeight = SystemParameters.WorkArea.Height;
        }

        MaxWidth = Math.Max(240, workWidth - 24);
        MaxHeight = Math.Max(200, workHeight - 40);
        if (Width > MaxWidth)
        {
            Width = MaxWidth;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>提示框的快捷调用（替代系统 MessageBox）。</summary>
public static class AppMessage
{
    public static void Info(Window? owner, string message, string title = "MikoBarrier") =>
        Show(owner, title, message, MessageDialogKind.Info, showCancel: false, "知道了", string.Empty);

    public static void Warn(Window? owner, string message, string title = "MikoBarrier") =>
        Show(owner, title, message, MessageDialogKind.Warning, showCancel: false, "知道了", string.Empty);

    public static void Error(Window? owner, string message, string title = "MikoBarrier") =>
        Show(owner, title, message, MessageDialogKind.Error, showCancel: false, "知道了", string.Empty);

    public static bool Confirm(Window? owner, string message, string title = "MikoBarrier",
        string okText = "确定", string cancelText = "取消") =>
        Show(owner, title, message, MessageDialogKind.Question, showCancel: true, okText, cancelText);

    private static bool Show(Window? owner, string title, string message, MessageDialogKind kind,
        bool showCancel, string okText, string cancelText)
    {
        title = MikoText.T(title);
        message = MikoText.T(message);
        okText = MikoText.T(okText);
        cancelText = MikoText.T(cancelText);

        var dialog = new MessageDialog(title, message, kind, showCancel, okText, cancelText);

        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }
}
