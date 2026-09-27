using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MikoBarrier.Controls;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Dialogs;

/// <summary>白名单预览列表的一行：默认只显示名称，路径按需展开。</summary>
public sealed class WhitelistPreviewItem : INotifyPropertyChanged
{
    private bool _pathVisible;

    public WhitelistPreviewItem(string displayName, string filePath)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "(未命名程序)" : displayName.Trim();
        FilePath = filePath ?? string.Empty;
    }

    public string DisplayName { get; }

    public string FilePath { get; }

    public bool IsPathVisible
    {
        get => _pathVisible;
        set
        {
            if (_pathVisible == value)
            {
                return;
            }

            _pathVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PathVisibility));
        }
    }

    public Visibility PathVisibility => _pathVisible ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// 一键白名单预览：固定底部按钮 + 可滚动列表 + 名称优先（路径可单条或全部展开），
/// 窗口尺寸按所在屏幕工作区自动限制，避免在小屏 / 竖屏上把“继续”按钮顶出屏幕。
/// </summary>
public partial class WhitelistPreviewDialog : Window
{
    private readonly List<WhitelistPreviewItem> _items;
    private bool _allPathsVisible;

    public WhitelistPreviewDialog(
        IReadOnlyList<AppRule> rules,
        int scannedCount,
        int alreadyCovered,
        int systemSkipped)
    {
        InitializeComponent();

        _items = rules
            .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new WhitelistPreviewItem(r.DisplayName, r.Path))
            .ToList();

        ItemList.ItemsSource = _items;
        SummaryText.Text = $"将新增 {_items.Count} 个程序到白名单";
        SubSummaryText.Text =
            $"扫描到 {scannedCount} 个运行中的程序；其中 {systemSkipped} 个系统组件自动放行，{alreadyCovered} 个已覆盖或重复。";
        UpdateToggleButton();

        SourceInitialized += (_, _) =>
        {
            WindowFx.ApplyRoundedCorners(this);
            ClampToWorkArea();
        };

        Loaded += (_, _) => OkButton.Focus();
    }

    public int ItemCount => _items.Count;

    public bool AllPathsVisible => _allPathsVisible;

    /// <summary>全部展开 / 收起路径（自检也会调用）。</summary>
    public void SetAllPathsVisible(bool visible)
    {
        _allPathsVisible = visible;
        foreach (var item in _items)
        {
            item.IsPathVisible = visible;
        }

        UpdateToggleButton();
    }

    private void UpdateToggleButton() =>
        TogglePathsButton.Content = _allPathsVisible ? "收起路径" : "展开路径";

    private void TogglePaths_Click(object sender, RoutedEventArgs e) => SetAllPathsVisible(!_allPathsVisible);

    private void ItemPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WhitelistPreviewItem item })
        {
            item.IsPathVisible = !item.IsPathVisible;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>
    /// 用窗口所在屏幕的工作区限制最大尺寸，再给窗口一个不超屏的初始大小。
    /// 注意 WPF 的 Width/Height 是 DIP，Screen.WorkingArea 是物理像素，需要除以 DPI 缩放。
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
        MaxHeight = Math.Max(240, workHeight - 32);
        Width = Math.Min(560, MaxWidth);
        Height = Math.Min(620, MaxHeight);
    }
}
