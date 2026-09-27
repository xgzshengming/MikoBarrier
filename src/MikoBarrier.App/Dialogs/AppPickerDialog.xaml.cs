using System.Windows;
using System.Windows.Controls;
using MikoBarrier.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

namespace MikoBarrier.Dialogs;

public partial class AppPickerDialog : Window
{
    private List<RunningApp> _apps = new();

    public AppPickerDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowFx.ApplyRoundedCorners(this);
        Loaded += async (_, _) => await LoadAppsAsync();
    }

    public RunningApp? SelectedApp { get; private set; }

    /// <summary>多选结果（Ok 后有效）；SelectedApp 为其中第一个，兼容旧调用。</summary>
    public List<RunningApp> SelectedApps { get; } = new();

    public RuleKind SelectedKind => BlackRadio.IsChecked == true ? RuleKind.Blacklist : RuleKind.Whitelist;

    private async Task LoadAppsAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "正在枚举进程";

        var apps = await Task.Run(() => ProcessCatalog.GetRunningApps());
        _apps = apps.ToList();
        ApplyFilter();

        StatusText.Text = $"共 {_apps.Count} 个可识别程序";
        RefreshButton.IsEnabled = true;
    }

    private void ApplyFilter()
    {
        var keyword = SearchBox.Text.Trim();
        var view = string.IsNullOrWhiteSpace(keyword)
            ? _apps
            : _apps.Where(a =>
                    a.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    a.ProcessName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    a.WindowTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        AppList.ItemsSource = view;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAppsAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var selected = AppList.SelectedItems
            .Cast<RunningApp>()
            .Where(a => !string.IsNullOrWhiteSpace(a.FilePath))
            .ToList();

        if (selected.Count == 0)
        {
            ErrorText.Text = "请先选择至少一个带路径的程序（可按住 Ctrl / Shift 多选）。";
            return;
        }

        SelectedApps.Clear();
        SelectedApps.AddRange(selected);
        SelectedApp = selected[0];
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SelectAll_Click(object sender, RoutedEventArgs e) => AppList.SelectAll();

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => AppList.UnselectAll();
}