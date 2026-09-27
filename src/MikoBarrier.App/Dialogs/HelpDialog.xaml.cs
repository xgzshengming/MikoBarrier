using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MikoBarrier.Controls;

namespace MikoBarrier.Dialogs;

/// <summary>
/// 帮助中心：从标题栏问号按钮打开，提供「重新演示当前页引导」和「文字版说明」。
/// 非常驻置顶，不阻塞关机 / 注销。
/// </summary>
public partial class HelpDialog : Window
{
    private readonly string _key;

    public HelpDialog(string key)
    {
        InitializeComponent();

        _key = string.IsNullOrWhiteSpace(key) ? HelpContent.Welcome : key;
        Title = "使用帮助 · " + HelpContent.GetTopicTitle(_key);
        TitleBarControl.Title = Title;
        TopicText.Text = "当前页面：" + HelpContent.GetTopicTitle(_key);
        VersionText.Text = "MikoBarrier " + AppInfo.DisplayVersion;
        TextHelpBlock.Text = HelpContent.GetFullTextHelp();

        SourceInitialized += (_, _) =>
        {
            WindowFx.ApplyRoundedCorners(this);
            ClampToWorkArea();
        };
    }

    /// <summary>用户选择重新演示当前页时触发；主窗口会调用对应页的 ReplayCurrentGuide()。</summary>
    public event EventHandler? ReplayRequested;

    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        ReplayRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void ShowText_Click(object sender, RoutedEventArgs e)
    {
        HomePanel.Visibility = Visibility.Collapsed;
        TextPanel.Visibility = Visibility.Visible;
        BackToHomeButton.Visibility = Visibility.Visible;
    }

    private void BackToHome_Click(object sender, RoutedEventArgs e)
    {
        TextPanel.Visibility = Visibility.Collapsed;
        BackToHomeButton.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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

        MaxWidth = Math.Max(360, workWidth - 24);
        MaxHeight = Math.Max(260, workHeight - 40);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }
}
