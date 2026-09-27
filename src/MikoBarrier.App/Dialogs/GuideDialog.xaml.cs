using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MikoBarrier.Controls;

namespace MikoBarrier.Dialogs;

/// <summary>分步引导窗口：每次只展示一页，支持上一步 / 下一步 / 跳过，非常驻置顶。</summary>
public partial class GuideDialog : Window
{
    private readonly string _key;
    private readonly IReadOnlyList<GuidePage> _pages;
    private int _index;

    public GuideDialog(string key)
    {
        InitializeComponent();

        _key = key;
        _pages = HelpContent.GetPages(key);
        SourceInitialized += (_, _) =>
        {
            WindowFx.ApplyRoundedCorners(this);
            ClampToWorkArea();
        };
        Closed += (_, _) =>
        {
            if (!App.IsAutomatedMode)
            {
                App.State?.Guides.MarkSeen(_key);
            }
        };
        Loaded += (_, _) => UpdatePage();
        Title = HelpContent.GetTopicTitle(key);
        TitleBarControl.Title = Title;
        UpdatePage();
    }

    /// <summary>长正文 / 小屏时限制窗口尺寸，底部按钮始终留在屏幕内。</summary>
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

        MaxWidth = Math.Max(320, workWidth - 24);
        MaxHeight = Math.Max(240, workHeight - 40);
        if (Width > MaxWidth)
        {
            Width = MaxWidth;
        }
    }

    private void UpdatePage()
    {
        if (_pages.Count == 0)
        {
            Close();
            return;
        }

        _index = Math.Clamp(_index, 0, _pages.Count - 1);
        var page = _pages[_index];
        PageTitleText.Text = page.Title;
        PageBodyText.Text = page.Body;
        PageIndicatorText.Text = $"第 {_index + 1} / {_pages.Count} 页";
        PrevButton.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _index >= _pages.Count - 1 ? "完成" : "下一步";
        SkipButton.Visibility = _index >= _pages.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0)
        {
            _index--;
            UpdatePage();
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index >= _pages.Count - 1)
        {
            Close();
            return;
        }

        _index++;
        UpdatePage();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Close();
}
