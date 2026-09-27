using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MikoBarrier.Controls;

/// <summary>
/// 无边框窗口的自绘标题栏：可拖动、双击最大化（对话框模式隐藏最小化/最大化按钮）。
/// </summary>
public partial class WindowTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(WindowTitleBar), new PropertyMetadata("MikoBarrier"));

    public static readonly DependencyProperty IsDialogModeProperty = DependencyProperty.Register(
        nameof(IsDialogMode), typeof(bool), typeof(WindowTitleBar), new PropertyMetadata(false, OnIsDialogModeChanged));

    public WindowTitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool IsDialogMode
    {
        get => (bool)GetValue(IsDialogModeProperty);
        set => SetValue(IsDialogModeProperty, value);
    }

    /// <summary>点击标题栏问号按钮时触发；由主窗口打开帮助中心。</summary>
    public event EventHandler? HelpRequested;

    private static void OnIsDialogModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WindowTitleBar bar)
        {
            return;
        }

        var visibility = bar.IsDialogMode ? Visibility.Collapsed : Visibility.Visible;
        bar.HelpButton.Visibility = visibility;
        bar.MinButton.Visibility = visibility;
        bar.MaxButton.Visibility = visibility;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is null)
        {
            return;
        }

        window.StateChanged -= OnWindowStateChanged;
        window.StateChanged += OnWindowStateChanged;
        UpdateMaximizeGlyph(window);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            UpdateMaximizeGlyph(window);
        }
    }

    private void UpdateMaximizeGlyph(Window window) =>
        MaxButton.Content = window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is null)
        {
            return;
        }

        if (e.ClickCount == 2 && !IsDialogMode && window.ResizeMode != ResizeMode.NoResize)
        {
            ToggleMaximize(window);
            return;
        }

        try
        {
            window.DragMove();
        }
        catch
        {
            // 拖动过程中窗口被关闭等情况，忽略。
        }
    }

    private void Min_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is not null)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void Max_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is not null && window.ResizeMode != ResizeMode.NoResize)
        {
            ToggleMaximize(window);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();

    private void Help_Click(object sender, RoutedEventArgs e) => HelpRequested?.Invoke(this, EventArgs.Empty);

    private static void ToggleMaximize(Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
