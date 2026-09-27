using System.Windows;
using System.Windows.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

namespace MikoBarrier.Views;

/// <summary>任务清单页：新增 / 删除 / 查看进度（自律结束后自动累计到任务上）。</summary>
public sealed partial class TasksView : UserControl, IGuidedView
{
    public string CurrentGuideKey => HelpContent.Tasks;

    public void ReplayCurrentGuide() =>
        GuideHooks.ShowOnFirstVisit(Window.GetWindow(this), HelpContent.Tasks, force: true);

    private readonly ListBox _list = new();
    private readonly TextBox _title = new() { Width = 280, Margin = new Thickness(0, 0, 14, 0) };
    private readonly TextBox _rounds = new() { Width = 60, Text = "1" };

    internal Button SelectAllButton { get; } = new() { Name = "TasksSelectAllButton", Content = "全选" };

    internal Button ClearSelectionButton { get; } = new() { Name = "TasksClearSelectionButton", Content = "取消选择" };
    private readonly TextBox _bulk = new()
    {
        AcceptsReturn = true,
        Height = 90,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    public TasksView()
    {
        var add = new Button { Content = "添加任务", Style = (Style)Application.Current.FindResource("PrimaryButton") };
        add.Click += (_, _) => AddTask();

        var remove = new Button
        {
            Content = "删除选中",
            Style = (Style)Application.Current.FindResource("DangerButton"),
            Margin = new Thickness(10, 0, 0, 0),
        };
        remove.Click += (_, _) => RemoveTask();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Label("任务名"));
        row.Children.Add(_title);
        row.Children.Add(Label("预计回合"));
        row.Children.Add(_rounds);
        row.Children.Add(add);
        row.Children.Add(remove);

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = "任务清单", Style = (Style)Application.Current.FindResource("H1") });
        root.Children.Add(new Border
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Margin = new Thickness(0, 14, 0, 0),
            Child = row,
        });

        var bulkButton = new Button
        {
            Content = "按行批量添加",
            Style = (Style)Application.Current.FindResource("PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
        };
        bulkButton.Click += (_, _) => AddTasksBulk();

        var bulkPanel = new StackPanel();
        bulkPanel.Children.Add(new TextBlock { Text = "批量添加（每行一个任务名）", FontWeight = FontWeights.SemiBold });
        bulkPanel.Children.Add(new TextBlock
        {
            Text = "每行一个任务名，预计回合默认 1；之后可以在「任务模式」里给每轮选择任务。",
            Style = (Style)Application.Current.FindResource("Muted"),
            Margin = new Thickness(0, 6, 0, 8),
        });
        bulkPanel.Children.Add(_bulk);
        bulkPanel.Children.Add(bulkButton);

        root.Children.Add(new Border
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Margin = new Thickness(0, 14, 0, 0),
            Child = bulkPanel,
        });

        _list.SelectionMode = SelectionMode.Extended;
        _list.Height = 420;
        SelectAllButton.Click += (_, _) => _list.SelectAll();
        ClearSelectionButton.Click += (_, _) => _list.UnselectAll();
        var listToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        listToolbar.Children.Add(SelectAllButton);
        listToolbar.Children.Add(ClearSelectionButton);
        var listPanel = new StackPanel();
        listPanel.Children.Add(listToolbar);
        listPanel.Children.Add(_list);
        root.Children.Add(new Border
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Margin = new Thickness(0, 14, 0, 0),
            Child = listPanel,
        });

        Content = root;
        Loaded += (_, _) =>
        {
            Refresh();
            GuideHooks.ShowOnFirstVisit(Window.GetWindow(this), HelpContent.Tasks);
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    };

    public void Refresh()
    {
        _list.ItemsSource = TaskStore.Load()
            .OrderBy(t => t.State)
            .ThenBy(t => t.CreatedUtc)
            .ToList();
    }

    private void AddTask()
    {
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            return;
        }

        int.TryParse(_rounds.Text, out var rounds);
        TaskStore.Add(_title.Text, rounds <= 0 ? 1 : rounds);
        _title.Clear();
        Refresh();
    }

    private void RemoveTask()
    {
        var selected = _list.SelectedItems.Cast<FocusTask>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var task in selected)
        {
            TaskStore.Remove(task.Id);
        }

        Refresh();
    }

    private void AddTasksBulk()
    {
        var titles = _bulk.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (titles.Count == 0)
        {
            return;
        }

        foreach (var title in titles)
        {
            TaskStore.Add(title, 1);
        }

        _bulk.Clear();
        Refresh();
        MikoBarrier.Dialogs.AppMessage.Info(
            Window.GetWindow(this),
            $"已批量添加 {titles.Count} 个任务（预计回合默认 1）。");
    }
}