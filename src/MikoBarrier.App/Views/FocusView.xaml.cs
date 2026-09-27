using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MikoBarrier.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;
using MikoBarrier.Dialogs;

namespace MikoBarrier.Views;

public partial class FocusView : UserControl, IGuidedView
{
    private enum SetupPage
    {
        Hub,
        Quick,
        Task,
        Policy,
        Network,
    }

    private enum StartMode
    {
        Quick,
        Task,
    }

    private SetupPage _setupPage = SetupPage.Hub;
    private StartMode _startMode = StartMode.Task;

    private static readonly (string Label, int Minutes)[] Presets =
    {
        ("5 分钟", 5),
        ("25 分钟", 25),
        ("40 分钟", 40),
        ("60 分钟", 60),
        ("自定义", 0),
    };

    private readonly DispatcherTimer _timer;
    private readonly EnforcementService _enforcement = new();
    private SessionPhase _lastPhase = SessionPhase.Idle;
    private bool _suppressTierCheck;
    private bool _enforcementApplied;
    private bool _loaded;
    private FullScreenOverlay? _overlay;
    private bool _closingOverlay;
    private bool _exitDialogOpen;
    private readonly List<FocusTask> _tasks = new();
    private readonly List<int> _roundMinutes = new();
    private readonly List<List<string>> _roundTasks = new();
    private readonly List<TextBox> _roundMinuteBoxes = new();
    private readonly List<Button> _roundTaskButtons = new();
    private readonly List<string> _uniformTaskIds = new();
    private bool _suppressRoundEvents;
    private bool _loadingPlan;

    // 互保心跳 / 看门狗监测
    private DateTime _lastAppHeartbeatUtc = DateTime.MinValue;
    private DateTime _lastGuardCheckUtc = DateTime.MinValue;
    private bool _guardSeenThisSession;
    private DateTime _guardFirstMissingUtc = DateTime.MinValue;

    public FocusView()
    {
        InitializeComponent();

        foreach (var (label, _) in Presets)
        {
            PresetBox.Items.Add(label);
        }

        PresetBox.SelectedIndex = 1;
        CustomMinutesBox.TextChanged += CustomMinutesBox_TextChanged;

        for (var i = 1; i <= FocusPlan.MaxRounds; i++)
        {
            RoundsBox.Items.Add(i == 1 ? "1 轮（单轮）" : $"{i} 轮");
        }

        RoundsBox.SelectedIndex = 0;

        BreakBox.Items.Add("无中场休息");
        BreakBox.Items.Add("中场休息 5 分钟");
        BreakBox.Items.Add("中场休息 10 分钟");
        BreakBox.SelectedIndex = 0;

        // 默认每轮单独设置时长；勾选后表示所有轮次使用同一时长。
        UniformDurationCheck.IsChecked = false;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _timer.Tick += (_, _) =>
        {
            App.State.Engine.Tick();
            UpdateUi();
            PumpMutualWatchdog();
        };
        _timer.Start();

        App.State.Engine.Changed += (_, _) => Dispatcher.Invoke(OnEngineChanged);
        RebuildRoundPlanPanel();
        RefreshTasks();
        UpdateTaskUniformPlacement();
        ShowSetupPage(SetupPage.Hub);

        Loaded += (_, _) =>
        {
            if (!_loaded)
            {
                LoadLastPlan();
                _loaded = true;
            }

            RefreshTasks();
            UpdateUi();
        };
    }

    public void SetStatus(string text) => MikoText.SetText(StatusText, text);

    /// <summary>重新读取任务，刷新每轮任务按钮；任务被删除时自动从各轮移除。</summary>
    public void RefreshTasks()
    {
        var tasks = TaskStore.Load()
            .OrderBy(t => t.State)
            .ThenBy(t => t.CreatedUtc)
            .ToList();

        _tasks.Clear();
        _tasks.AddRange(tasks);

        var validIds = new HashSet<string>(_tasks.Select(t => t.Id), StringComparer.Ordinal);
        foreach (var round in _roundTasks)
        {
            round.RemoveAll(id => !validIds.Contains(id));
        }

        _uniformTaskIds.RemoveAll(id => !validIds.Contains(id));

        UpdateRoundTaskButtons();
    }

    /// <summary>按当前轮数重建「每轮任务安排」面板（尽量保留已填的分钟数与任务选择）。</summary>
    private void RebuildRoundPlanPanel()
    {
        if (RoundPlanPanel is null)
        {
            return;
        }

        var count = RoundsBox.SelectedIndex + 1;
        NormalizeRoundLists(count);

        _suppressRoundEvents = true;
        try
        {
            RoundPlanPanel.Children.Clear();
            _roundMinuteBoxes.Clear();
            _roundTaskButtons.Clear();
            var showPerRoundMinutes = IsTaskPerRoundDuration;

            for (var i = 0; i < count; i++)
            {
                var index = i;
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = $"第 {i + 1} 轮",
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 58,
                };
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                var minutePanel = new StackPanel { Orientation = Orientation.Horizontal };
                var minuteBox = new TextBox
                {
                    Width = 52,
                    Text = _roundMinutes[i].ToString(),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = index,
                };
                minuteBox.TextChanged += RoundMinuteBox_TextChanged;
                minutePanel.Children.Add(minuteBox);
                minutePanel.Children.Add(new TextBlock
                {
                    Text = "分钟",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 12, 0),
                });
                // 勾选「全部轮次都使用同一时长」时，统一时长编辑控件已经在上面显示，
                // 每轮不再重复出现时长输入框，只保留任务选择。
                minutePanel.Visibility = showPerRoundMinutes ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetColumn(minutePanel, 1);
                row.Children.Add(minutePanel);

                var taskButton = new Button
                {
                    Tag = index,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                taskButton.Click += RoundTasksButton_Click;
                Grid.SetColumn(taskButton, 2);
                row.Children.Add(taskButton);

                _roundMinuteBoxes.Add(minuteBox);
                _roundTaskButtons.Add(taskButton);
                RoundPlanPanel.Children.Add(row);
            }
        }
        finally
        {
            _suppressRoundEvents = false;
        }

        UpdateRoundDurationInputs();
        UpdateRoundTaskButtons();
    }

    private void NormalizeRoundLists(int count)
    {
        while (_roundMinutes.Count < count)
        {
            _roundMinutes.Add(SelectedMinutes());
        }

        if (_roundMinutes.Count > count)
        {
            _roundMinutes.RemoveRange(count, _roundMinutes.Count - count);
        }

        while (_roundTasks.Count < count)
        {
            _roundTasks.Add(new List<string>());
        }

        if (_roundTasks.Count > count)
        {
            _roundTasks.RemoveRange(count, _roundTasks.Count - count);
        }
    }

    private void RoundMinuteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressRoundEvents || !IsTaskPerRoundDuration)
        {
            return;
        }

        if (sender is TextBox { Tag: int index } textBox &&
            index >= 0 && index < _roundMinutes.Count &&
            int.TryParse(textBox.Text.Trim(), out var minutes))
        {
            _roundMinutes[index] = minutes;
        }
    }

    private void UpdateRoundDurationInputs()
    {
        var perRound = IsTaskPerRoundDuration;
        var uniform = SelectedMinutes();

        _suppressRoundEvents = true;
        try
        {
            for (var i = 0; i < _roundMinuteBoxes.Count; i++)
            {
                var box = _roundMinuteBoxes[i];
                box.IsEnabled = perRound && !App.State.Engine.IsActive;
                if (!perRound)
                {
                    box.Text = uniform.ToString();
                }
                else if (string.IsNullOrWhiteSpace(box.Text))
                {
                    box.Text = _roundMinutes[i].ToString();
                }
            }
        }
        finally
        {
            _suppressRoundEvents = false;
        }
    }

    private void UpdateRoundTaskButtons()
    {
        var lookup = new Dictionary<string, FocusTask>(StringComparer.Ordinal);
        foreach (var task in _tasks)
        {
            lookup[task.Id] = task;
        }

        for (var i = 0; i < _roundTaskButtons.Count; i++)
        {
            var ids = i < _roundTasks.Count ? _roundTasks[i] : new List<string>();
            var titles = ids
                .Select(id => lookup.TryGetValue(id, out var task) ? task.Title : "（任务已删除）")
                .ToList();

            var summary = titles.Count == 0 ? "未关联任务" : string.Join("、", titles);
            var toolTip = titles.Count == 0
                ? "点击选择本轮任务（可多选）"
                : "本轮：" + string.Join("、", titles);

            var button = _roundTaskButtons[i];
            button.Content = new TextBlock
            {
                Text = summary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            button.ToolTip = toolTip;
        }

        UpdateUniformTaskButton(lookup);
    }

    private void UpdateUniformTaskButton(IReadOnlyDictionary<string, FocusTask>? lookup = null)
    {
        lookup ??= _tasks.ToDictionary(t => t.Id, t => t, StringComparer.Ordinal);
        var titles = _uniformTaskIds
            .Select(id => lookup.TryGetValue(id, out var task) ? task.Title : "（任务已删除）")
            .ToList();
        var summary = titles.Count == 0 ? "未关联任务" : string.Join("、", titles);
        ApplyTasksToAllButton.Content = new TextBlock
        {
            Text = summary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ApplyTasksToAllButton.ToolTip = titles.Count == 0
            ? "点击选择所有轮次共用任务（可多选）"
            : "统一任务：" + summary;
    }

    private void RoundTasksButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index } && index >= 0 && index < _roundTasks.Count)
        {
            OpenTaskPicker(index);
        }
    }

    private void ApplyTasksToAll_Click(object sender, RoutedEventArgs e)
    {
        if (_tasks.Count == 0)
        {
            AppMessage.Info(HostWindow, "还没有任务。请先到「任务清单」页添加任务，再进行选择。");
            return;
        }

        var dialog = new TaskPickerDialog(_tasks, _uniformTaskIds);
        var owner = HostWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var ids = dialog.SelectedTaskIds.Distinct(StringComparer.Ordinal).ToList();
        _uniformTaskIds.Clear();
        _uniformTaskIds.AddRange(ids);

        // 统一任务同时写回每一轮：取消勾选后仍能看到同一组任务。
        NormalizeRoundLists(RoundsBox.SelectedIndex + 1);
        for (var i = 0; i < _roundTasks.Count; i++)
        {
            _roundTasks[i] = new List<string>(ids);
        }

        UpdateRoundTaskButtons();
    }

    private void UniformTaskCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressRoundEvents)
        {
            return;
        }

        if (UniformTaskCheck.IsChecked == true)
        {
            // 以第 1 轮现有任务作为统一任务的起点，之后可点统一任务按钮修改。
            _uniformTaskIds.Clear();
            if (_roundTasks.Count > 0)
            {
                _uniformTaskIds.AddRange(_roundTasks[0]);
            }
        }

        UpdateTaskUniformPlacement();
        UpdateRoundTaskButtons();
        UpdateUi();
    }

    /// <summary>任务卡的两种模式：勾选统一任务时隐藏逐轮列表，只留统一任务按钮。</summary>
    private void UpdateTaskUniformPlacement()
    {
        var uniform = _startMode == StartMode.Task && UniformTaskCheck.IsChecked == true;
        ApplyTasksToAllButton.Visibility = uniform ? Visibility.Visible : Visibility.Collapsed;
        RoundTaskScroll.Visibility = uniform ? Visibility.Collapsed : Visibility.Visible;
        UpdateUniformTaskButton();
    }

    private void OpenTaskPicker(int roundIndex)
    {
        if (_tasks.Count == 0)
        {
            AppMessage.Info(HostWindow, "还没有任务。请先到「任务清单」页添加任务，再进行选择。");
            return;
        }

        var dialog = new TaskPickerDialog(_tasks, _roundTasks[roundIndex]);
        var owner = HostWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _roundTasks[roundIndex] = dialog.SelectedTaskIds.Distinct(StringComparer.Ordinal).ToList();
        UpdateRoundTaskButtons();
    }

    private void UniformDurationCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressRoundEvents)
        {
            return;
        }

        UpdateDurationEditorPlacement();
        RebuildRoundPlanPanel();
        UpdateRoundDurationInputs();
        UpdateUi();
    }

    /// <summary>战报里的任务行：汇总每轮关联到的任务（含轮数与分钟数）。</summary>
    private static string BuildTaskSummary(FocusPlan? plan)
    {
        if (plan is null)
        {
            return "本次任务：不关联";
        }

        var rounds = plan.GetRoundPlans();
        var ids = rounds.SelectMany(r => r.TaskIds).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return "本次任务：不关联";
        }

        var tasks = TaskStore.Load();
        var parts = new List<string>();
        foreach (var id in ids)
        {
            var title = tasks.FirstOrDefault(t => t.Id == id)?.Title ?? "（任务已删除）";
            var count = rounds.Count(r => r.TaskIds.Contains(id, StringComparer.Ordinal));
            var minutes = rounds
                .Where(r => r.TaskIds.Contains(id, StringComparer.Ordinal))
                .Sum(r => r.Minutes);
            parts.Add($"{title}（{count} 轮 / {minutes} 分钟）");
        }

        return "本次任务：" + string.Join("、", parts);
    }

    private static string FormatTaskAssignment(FocusPlan plan)
    {
        var summary = BuildTaskSummary(plan);
        return summary.StartsWith("本次任务：", StringComparison.Ordinal)
            ? "任务安排：" + summary["本次任务：".Length..]
            : summary;
    }
    /// <summary>战报里的任务行（独立成纯函数，便于界面自检直接断言）。</summary>
    internal static string BuildTaskReportLine(string? taskTitle) =>
        string.IsNullOrWhiteSpace(taskTitle) ? "本次任务：不关联" : $"本次任务：{taskTitle}";



    private Window? HostWindow => Window.GetWindow(this);

    // ---------------------------------------------------------------- 界面读取

    private int SelectedMinutes()
    {
        var index = PresetBox.SelectedIndex;
        if (index >= 0 && index < Presets.Length - 1)
        {
            return Presets[index].Minutes;
        }

        return int.TryParse(CustomMinutesBox.Text.Trim(), out var minutes) ? minutes : 0;
    }

    /// <summary>未开始倒计时：任务模式逐轮时长时显示第 1 轮，其余显示当前选择的统一时长。</summary>
    private int PreviewMinutes()
    {
        if (_startMode == StartMode.Task &&
            IsTaskPerRoundDuration &&
            _roundMinuteBoxes.Count > 0 &&
            int.TryParse(_roundMinuteBoxes[0].Text.Trim(), out var minutes))
        {
            return Math.Clamp(minutes, FocusPlan.MinFocusMinutes, FocusPlan.MaxFocusMinutes);
        }

        return SelectedMinutes();
    }


    private NetworkTier SelectedTier()
    {
        if (NetworkGentleRadio.IsChecked == true)
        {
            return NetworkTier.Gentle;
        }

        return NetworkHardcoreRadio.IsChecked == true ? NetworkTier.Hardcore : NetworkTier.Off;
    }

    private void SelectTier(NetworkTier tier)
    {
        _suppressTierCheck = true;
        NetworkOffRadio.IsChecked = tier == NetworkTier.Off;
        NetworkGentleRadio.IsChecked = tier == NetworkTier.Gentle;
        NetworkHardcoreRadio.IsChecked = tier == NetworkTier.Hardcore;
        _suppressTierCheck = false;
    }

    private FocusPlan? ReadPlanFromUi(out string error) =>
        BuildPlanFromUi(_startMode, out error);

    private FocusPlan? BuildPlanFromUi(StartMode mode, out string error)
    {
        error = string.Empty;
        var rounds = mode == StartMode.Task ? RoundsBox.SelectedIndex + 1 : 1;
        var perRound = mode == StartMode.Task && UniformDurationCheck.IsChecked != true;
        var uniformTasks = mode == StartMode.Task && UniformTaskCheck.IsChecked == true;
        var uniform = SelectedMinutes();

        if (!perRound && uniform is < FocusPlan.MinFocusMinutes or > FocusPlan.MaxFocusMinutes)
        {
            error = "统一时长必须在 1 - 120 分钟之间。";
            return null;
        }

        var roundPlans = new List<FocusRoundPlan>(rounds);
        for (var i = 0; i < rounds; i++)
        {
            var minutes = uniform;
            if (perRound)
            {
                if (i >= _roundMinuteBoxes.Count ||
                    !int.TryParse(_roundMinuteBoxes[i].Text.Trim(), out minutes) ||
                    minutes is < FocusPlan.MinFocusMinutes or > FocusPlan.MaxFocusMinutes)
                {
                    error = $"第 {i + 1} 轮的时长必须在 1 - 120 分钟之间。";
                    return null;
                }
            }

            var taskIds = mode == StartMode.Quick
                ? new List<string>()
                : uniformTasks
                    ? _uniformTaskIds
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
                    : (i < _roundTasks.Count
                        ? _roundTasks[i].Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList()
                        : new List<string>());

            roundPlans.Add(new FocusRoundPlan { Minutes = minutes, TaskIds = taskIds });
        }

        return new FocusPlan
        {
            FocusMinutes = roundPlans[0].Minutes,
            Rounds = rounds,
            RoundPlans = roundPlans,
            PerRoundDuration = perRound,
            UniformTasks = uniformTasks,
            BreakMinutes = mode == StartMode.Task
                ? BreakBox.SelectedIndex switch { 1 => 5, 2 => 10, _ => 0 }
                : 0,
            BlockApplications = BlockAppsCheck.IsChecked == true,
            UseWhitelistMode = WhitelistCheck.IsChecked == true,
            BlockKeyboard = KeyboardCheck.IsChecked == true,
            EnforceOverlay = OverlayCheck.IsChecked == true,
            NetworkTier = SelectedTier(),
        };
    }

    private void LoadLastPlan()
    {
        var plan = App.State.Settings.LastPlan;
        _loadingPlan = true;
        try
        {
            var presetIndex = Array.FindIndex(Presets, p => p.Minutes == plan.FocusMinutes);
            if (presetIndex >= 0 && presetIndex < Presets.Length - 1)
            {
                PresetBox.SelectedIndex = presetIndex;
            }
            else
            {
                PresetBox.SelectedIndex = Presets.Length - 1;
                CustomMinutesBox.Text = plan.ClampFocusMinutes().ToString();
            }

            RoundsBox.SelectedIndex = plan.ClampRounds() - 1;
            BreakBox.SelectedIndex = plan.BreakMinutes switch { 5 => 1, 10 => 2, _ => 0 };
            UniformDurationCheck.IsChecked = !plan.PerRoundDuration;
            UniformTaskCheck.IsChecked = plan.UniformTasks;
            _startMode = StartMode.Task;

            _roundMinutes.Clear();
            _roundTasks.Clear();
            foreach (var round in plan.GetRoundPlans())
            {
                _roundMinutes.Add(round.Minutes);
                _roundTasks.Add(round.TaskIds.ToList());
            }

            _uniformTaskIds.Clear();
            _uniformTaskIds.AddRange(plan.GetRoundTaskIds(0));

            BlockAppsCheck.IsChecked = plan.BlockApplications;
            WhitelistCheck.IsChecked = plan.UseWhitelistMode;
            KeyboardCheck.IsChecked = plan.BlockKeyboard;
            OverlayCheck.IsChecked = plan.EnforceOverlay;
            SelectTier(plan.NetworkTier);
        }
        finally
        {
            _loadingPlan = false;
        }

        UpdateDurationEditorPlacement();
        RebuildRoundPlanPanel();
        RefreshTasks();
        UpdateTaskUniformPlacement();
    }

    // ---------------------------------------------------------------- 交互

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var custom = PresetBox.SelectedIndex == Presets.Length - 1;
        if (CustomMinutesBox is not null)
        {
            CustomMinutesBox.IsEnabled = custom && !App.State.Engine.IsActive;
        }

        if (!_loadingPlan && _startMode == StartMode.Task && UniformDurationCheck.IsChecked == true)
        {
            UpdateRoundDurationInputs();
        }

        if (!_loadingPlan)
        {
            UpdateUi();
        }
    }

    private void CustomMinutesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingPlan && _startMode == StartMode.Task && UniformDurationCheck.IsChecked == true)
        {
            UpdateRoundDurationInputs();
        }
    }

    private void RoundsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BreakBox is not null)
        {
            BreakBox.IsEnabled = RoundsBox.SelectedIndex > 0 && !App.State.Engine.IsActive;
        }

        if (!_loadingPlan)
        {
            RebuildRoundPlanPanel();
            UpdateUi();
        }
    }

    // ---------------------------------------------------------------- 功能入口 / 子界面

    private bool IsTaskPerRoundDuration =>
        _startMode == StartMode.Task && UniformDurationCheck.IsChecked != true;

    public string CurrentGuideKey => _setupPage switch
    {
        SetupPage.Quick => HelpContent.FocusQuick,
        SetupPage.Task => HelpContent.FocusTask,
        SetupPage.Policy => HelpContent.FocusPolicy,
        SetupPage.Network => HelpContent.FocusNetwork,
        _ => HelpContent.FocusHub,
    };

    public void ReplayCurrentGuide() =>
        GuideHooks.ShowOnFirstVisit(HostWindow, CurrentGuideKey, force: true);

    private void HubQuick_Click(object sender, RoutedEventArgs e) => ShowSetupPage(SetupPage.Quick);

    private void HubTask_Click(object sender, RoutedEventArgs e) => ShowSetupPage(SetupPage.Task);

    private void HubPolicy_Click(object sender, RoutedEventArgs e) => ShowSetupPage(SetupPage.Policy);

    private void HubNetwork_Click(object sender, RoutedEventArgs e) => ShowSetupPage(SetupPage.Network);

    private void BackToHub_Click(object sender, RoutedEventArgs e) => ShowSetupPage(SetupPage.Hub);

    private void ShowSetupPage(SetupPage page)
    {
        _setupPage = page;
        var hubVisible = page == SetupPage.Hub;
        SetupHubPanel.Visibility = hubVisible ? Visibility.Visible : Visibility.Collapsed;
        SetupDetailPanel.Visibility = hubVisible ? Visibility.Collapsed : Visibility.Visible;

        QuickPagePanel.Visibility = page == SetupPage.Quick ? Visibility.Visible : Visibility.Collapsed;
        TaskPagePanel.Visibility = page == SetupPage.Task ? Visibility.Visible : Visibility.Collapsed;
        PolicyPagePanel.Visibility = page == SetupPage.Policy ? Visibility.Visible : Visibility.Collapsed;
        NetworkPagePanel.Visibility = page == SetupPage.Network ? Visibility.Visible : Visibility.Collapsed;

        switch (page)
        {
            case SetupPage.Quick:
                _startMode = StartMode.Quick;
                UpdateDurationEditorPlacement();
                UpdateTaskUniformPlacement();
                MikoText.SetText(DetailTitleText, "快速开始");
                MikoText.SetText(DetailHintText, "选好时长后，点右侧「开始自律」即可开始。");
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.FocusQuick);
                break;
            case SetupPage.Task:
                _startMode = StartMode.Task;
                UpdateDurationEditorPlacement();
                UpdateTaskUniformPlacement();
                MikoText.SetText(DetailTitleText, "任务模式");
                MikoText.SetText(DetailHintText, "设置轮数、休息、每轮时长与任务。");
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.FocusTask);
                break;
            case SetupPage.Policy:
                MikoText.SetText(DetailTitleText, "强制策略");
                MikoText.SetText(DetailHintText, "这些策略会应用到接下来的启动方式，开始后锁定。");
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.FocusPolicy);
                break;
            case SetupPage.Network:
                MikoText.SetText(DetailTitleText, "断网档位");
                MikoText.SetText(DetailHintText, "休息时间同样生效；需要管理员权限。");
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.FocusNetwork);
                break;
            default:
                UpdateStartSummary();
                break;
        }

        UpdateUi();
    }

    /// <summary>统一时长编辑器的控件只有一份，在「快速开始」和「任务模式统一时长」之间移动复用。</summary>
    private void UpdateDurationEditorPlacement()
    {
        var useTaskHost = _setupPage == SetupPage.Task && UniformDurationCheck.IsChecked == true;
        if (useTaskHost)
        {
            if (ReferenceEquals(TaskUniformDurationHost.Content, DurationEditor))
            {
                return;
            }

            QuickDurationHost.Content = null;
            TaskUniformDurationHost.Content = DurationEditor;
        }
        else
        {
            if (ReferenceEquals(QuickDurationHost.Content, DurationEditor))
            {
                return;
            }

            TaskUniformDurationHost.Content = null;
            QuickDurationHost.Content = DurationEditor;
        }
    }

    private void UpdateStartSummary()
    {
        MikoText.SetText(PlanSummaryText, BuildStartSummary());
    }

    private string BuildStartSummary()
    {
        var minutes = SelectedMinutes();
        if (_startMode == StartMode.Quick)
        {
            return $"当前启动：快速开始 · {Math.Clamp(minutes, FocusPlan.MinFocusMinutes, FocusPlan.MaxFocusMinutes)} 分钟单轮。";
        }

        var rounds = RoundsBox.SelectedIndex + 1;
        if (IsTaskPerRoundDuration)
        {
            var parts = new List<string>(rounds);
            for (var i = 0; i < rounds; i++)
            {
                var value = i < _roundMinutes.Count ? _roundMinutes[i] : minutes;
                parts.Add(Math.Clamp(value, FocusPlan.MinFocusMinutes, FocusPlan.MaxFocusMinutes).ToString());
            }

            return $"当前启动：任务模式 · 每轮 {string.Join("/", parts)} 分钟（{rounds} 轮）。";
        }

        return $"当前启动：任务模式 · 统一 {Math.Clamp(minutes, FocusPlan.MinFocusMinutes, FocusPlan.MaxFocusMinutes)} 分钟 × {rounds} 轮。";
    }

    /// <summary>选择断网档位：提醒"休息时间也会断网"，确认后才生效。</summary>
    private void NetworkTier_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressTierCheck || !IsLoaded || App.State.Engine.IsActive)
        {
            return;
        }

        var tier = SelectedTier();
        if (tier == NetworkTier.Off)
        {
            return;
        }

        var name = tier == NetworkTier.Gentle ? "温和档（只屏蔽网站名单）" : "狠人档（直接禁用网卡）";
        var confirmed = AppMessage.Confirm(HostWindow,
            $"一旦开启，休息时间也会断网！\n\n档位：{name}\n中场休息只会放开应用屏蔽，断网不会解除。",
            "MikoBarrier", okText: "确认开启", cancelText: "还是不断网");

        if (!confirmed)
        {
            SelectTier(NetworkTier.Off);
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;

        if (!settings.HasPassword)
        {
            AppMessage.Warn(HostWindow,
                "请先到\u201C系统设置\u201D页设置密码。\n无密码情况下，无法提前结束自律。");
            return;
        }

        var plan = ReadPlanFromUi(out var planError);
        if (plan is null)
        {
            AppMessage.Warn(HostWindow, string.IsNullOrWhiteSpace(planError)
                ? "时长必须在 1 - 120 分钟之间。"
                : planError);
            return;
        }

        // 拦截提权程序（游戏启动器、反作弊）必须有管理员权限，这里主动引导一次。
        if (plan.BlockApplications && !AdminHelper.IsElevated)
        {
            var choice = new ChoiceDialog(
                "建议以管理员身份运行",
                "当前不是管理员身份。\n\n像游戏启动器、带反作弊的程序属于提权程序，非管理员可以识别到，但结束它们会被系统拒绝。\n\n要现在以管理员身份重启吗？",
                "以管理员身份重启",
                "仍然开始自律",
                "取消");

            var promptOwner = HostWindow;
            if (promptOwner is not null)
            {
                choice.Owner = promptOwner;
            }

            switch (choice.ShowDialog())
            {
                case true:
                    if (AdminHelper.TryRelaunchElevated())
                    {
                        App.ForceExit = true;
                        Application.Current.Shutdown();
                    }

                    return;
                case false:
                    break;
                default:
                    return;
            }
        }

        var taskHint = "\n" + FormatTaskAssignment(plan);
        var modeHint = plan.UseWhitelistMode
            ? $"\n白名单模式：只允许名单里的 {settings.Rules.Count(r => r.Kind == RuleKind.Whitelist)} 个程序及其启动链路的子程序运行；系统组件自动放行。"
            : $"\n黑名单模式：拦截名单里的 {settings.Rules.Count(r => r.Kind == RuleKind.Blacklist)} 个程序。";

        if (!AppMessage.Confirm(HostWindow,
                $"即将开始：\n{plan}{modeHint}{taskHint}\n\n开始后名单与策略会锁定，只能凭密码提前结束。确定开始吗？",
                "MikoBarrier", okText: "开始自律", cancelText: "再想想"))
        {
            return;
        }

        settings.LastPlan = plan.Clone();
        App.State.Save();

        LockInputs(true);

        // 先启动会话（写出 session.lock），再启动巡逻，
        // 否则看门狗会以为"没有进行中的自律"而立刻退出。
        PatrolLog.Clear();
        App.State.Engine.Start(settings, plan);
        _lastAppHeartbeatUtc = DateTime.MinValue;
        _lastGuardCheckUtc = DateTime.MinValue;
        _guardSeenThisSession = false;
        _guardFirstMissingUtc = DateTime.MinValue;
        var messages = _enforcement.Apply(settings, plan);
        var extraMessages = new List<string>();

        if (plan.BlockKeyboard)
        {
            KeyboardGuard.ResetCount();
            var (keyboardOk, keyboardMessage) = KeyboardGuard.Enable();
            extraMessages.Add((keyboardOk ? "\u2714 " : "\u2718 ") + keyboardMessage);
        }
        else
        {
            extraMessages.Add(" 键盘封锁：本次未开启");
        }

        if (plan.EnforceOverlay)
        {
            CreateOverlay();
        }
        else
        {
            FullScreenOverlay.Log("本次自律未开启遮罩");
        }

        messages = messages.Concat(extraMessages).ToList();

        MikoText.SetText(StatusText, EnforcementService.Join(messages));
        UpdateUi();
    }

    /// <summary>
    /// 异常中断 / 被 Kill 自动续跑后，由主窗口调用：重新安装键盘封锁、重建全屏计时。
    /// 正常从界面点「开始自律」时的流程不受影响。
    /// </summary>
    public void ResumeAfterRecovery()
    {
        var plan = App.State.Engine.Plan;
        if (plan is null || !App.State.Engine.IsActive)
        {
            return;
        }

        LockInputs(true);
        _enforcementApplied = true;

        if (plan.BlockKeyboard)
        {
            KeyboardGuard.ResetCount();
            KeyboardGuard.Enable();
        }

        if (plan.EnforceOverlay)
        {
            if (_overlay is null)
            {
                CreateOverlay();
            }
            else
            {
                _overlay.Show();
                _overlay.ResumeGuard();
                _overlay.InitializeBackground(App.State.Settings);
            }
        }

        UpdateUi();
    }

    /// <summary>遮罩上的"提前结束"：先让遮罩退场，把密码框显示出来；取消则遮罩回来。</summary>
    /// <summary>托盘菜单：把全屏计时拉回来（自律进行中且开了全屏计时才有意义）。</summary>
    public void ShowOverlayNow()
    {
        if (!App.State.Engine.IsActive || _exitDialogOpen)
        {
            return;
        }

        if (_overlay is null)
        {
            CreateOverlay();
            return;
        }

        _overlay.Show();
        _overlay.ResumeGuard();
    }

    private void CreateOverlay()
    {
        _overlay = new FullScreenOverlay();
        _overlay.SessionActiveProvider = () => App.State.Engine.IsActive;
        _overlay.AbortRequested += OnOverlayAbortRequested;

        _overlay.Closed += (_, _) =>
        {
            FullScreenOverlay.Log("已关闭");

            if (App.State.Engine.IsActive && !_closingOverlay)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (App.State.Engine.IsActive && _overlay is null && !_closingOverlay)
                    {
                        FullScreenOverlay.Log("检测到遮罩消失，自动重建");
                        CreateOverlay();
                    }
                }));
            }
        };

        _overlay.Show();
        _overlay.InitializeBackground(App.State.Settings);
        FullScreenOverlay.Log("已显示");
    }

    private void OnOverlayAbortRequested()
    {
        if (_overlay is null)
        {
            return;
        }

        if (!App.State.Engine.IsActive)
        {
            _closingOverlay = true;
            _overlay.ReleaseAndClose();
            _overlay = null;
            _closingOverlay = false;
            return;
        }

        _overlay.SuspendGuard();
        _overlay.Hide();

        try
        {
            AbortButton_Click(this, new RoutedEventArgs());
        }
        finally
        {
            if (App.State.Engine.IsActive && _overlay is not null)
            {
                _overlay.Show();
                _overlay.ResumeGuard();
            }
        }
    }

    /// <summary>打开退出相关弹窗前，把可能在托盘 / 最小化的主窗口恢复并置前，避免弹窗被全屏或其它窗口挡住。</summary>
    private void PrepareExitDialogOwner()
    {
        var owner = HostWindow;
        if (owner is null)
        {
            return;
        }

        if (owner.WindowState == WindowState.Minimized)
        {
            owner.WindowState = WindowState.Normal;
        }

        if (!owner.IsVisible)
        {
            owner.Show();
        }

        owner.Activate();
        WindowFx.BringToForeground(owner);
    }

    private void AbortButton_Click(object sender, RoutedEventArgs e) =>
        WithExitDialogSuppressed(AbortButtonCore);

    private void WithExitDialogSuppressed(Action action)
    {
        if (_exitDialogOpen)
        {
            action();
            return;
        }

        _exitDialogOpen = true;
        try
        {
            action();
        }
        finally
        {
            _exitDialogOpen = false;
        }
    }

    private T WithExitDialogSuppressed<T>(Func<T> action)
    {
        if (_exitDialogOpen)
        {
            return action();
        }

        _exitDialogOpen = true;
        try
        {
            return action();
        }
        finally
        {
            _exitDialogOpen = false;
        }
    }

    private void AbortButtonCore()
    {
        var engine = App.State.Engine;
        var settings = App.State.Settings;

        if (!engine.IsActive)
        {
            return;
        }

        PrepareExitDialogOwner();

        var allowance = SessionExitPolicy.GetPasswordAllowance(settings);
        if (!allowance.CanExit)
        {
            var recovery = SessionExitPolicy.GetRecoveryExitAllowance(settings);
            if (recovery.CanExit && AppMessage.Confirm(HostWindow,
                    SessionExitPolicy.PasswordRuleSummary + "\n\n" + allowance.Message +
                    "\n\n是否改用恢复码提前结束？（每天 1 次）",
                    "MikoBarrier", okText: "用恢复码", cancelText: "继续坚持"))
            {
                TryAbortWithRecoveryCode();
            }
            else if (!recovery.CanExit)
            {
                AppMessage.Info(HostWindow,
                    SessionExitPolicy.PasswordRuleSummary + "\n\n" +
                    allowance.Message + "\n\n" + recovery.Message);
            }

            return;
        }

        var dialog = new PasswordDialog(
            "提前结束需要输入密码：\n\n" +
            SessionExitPolicy.PasswordRuleSummary)
        {
            Validator = password => PasswordService.VerifyPassword(settings, password),
        };

        var owner = HostWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!ConfirmAbort(engine))
        {
            return;
        }

        SessionExitPolicy.RecordPasswordExit(settings);
        engine.Abort(settings, "用户凭密码提前结束");
        App.State.Save();
    }

    private void RecoveryAbortButton_Click(object sender, RoutedEventArgs e) => TryAbortWithRecoveryCode();

    private bool TryAbortWithRecoveryCode() =>
        WithExitDialogSuppressed(TryAbortWithRecoveryCodeCore);

    private bool TryAbortWithRecoveryCodeCore()
    {
        var engine = App.State.Engine;
        var settings = App.State.Settings;

        if (!engine.IsActive)
        {
            return false;
        }

        PrepareExitDialogOwner();

        var allowance = SessionExitPolicy.GetRecoveryExitAllowance(settings);
        if (!allowance.CanExit)
        {
            AppMessage.Info(HostWindow, allowance.Message);
            return false;
        }

        var dialog = new RecoveryCodeDialog("请输入恢复码（每天只能用于提前结束 1 次）：")
        {
            Validator = code => PasswordService.VerifyRecoveryCode(settings, code),
        };

        var owner = HostWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        if (!ConfirmAbort(engine))
        {
            return false;
        }

        SessionExitPolicy.RecordRecoveryExit(settings);
        engine.Abort(settings, "用户凭恢复码提前结束");
        App.State.Save();
        return true;
    }

    private bool ConfirmAbort(SessionEngine engine) => AppMessage.Confirm(HostWindow,
        $"确认提前结束吗？\n\n本次不计入自律次数；剩余的 {Format(engine.Remaining)} 会记为欠债，顺延到下一次自律。",
        "MikoBarrier", okText: "确认结束", cancelText: "继续坚持");

    /// <summary>
    /// 互保心跳：App 每约 1 秒写心跳；Guard 被结束 / 心跳过期时记录 Kill 事件并重新拉起。
    /// 只在自律进行中工作，关机 / 注销和正常结束不会触发。
    /// </summary>
    private void PumpMutualWatchdog()
    {
        var engine = App.State.Engine;
        var settings = App.State.Settings;

        if (!engine.IsActive)
        {
            if (_lastAppHeartbeatUtc != DateTime.MinValue)
            {
                RuntimeHeartbeat.Clear(RuntimeHeartbeat.AppRole);
            }

            _lastAppHeartbeatUtc = DateTime.MinValue;
            _lastGuardCheckUtc = DateTime.MinValue;
            _guardSeenThisSession = false;
            _guardFirstMissingUtc = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastAppHeartbeatUtc >= TimeSpan.FromSeconds(1))
        {
            _lastAppHeartbeatUtc = now;
            RuntimeHeartbeat.Write(RuntimeHeartbeat.AppRole, engine.SessionId);
        }

        if (now - _lastGuardCheckUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastGuardCheckUtc = now;
        var guard = RuntimeHeartbeat.Read(RuntimeHeartbeat.GuardRole);
        var alive = RuntimeHeartbeat.IsAlive(
            guard,
            "MikoBarrier.Guard",
            engine.SessionId,
            TimeSpan.FromSeconds(6),
            out var reason);

        if (alive)
        {
            _guardSeenThisSession = true;
            _guardFirstMissingUtc = DateTime.MinValue;
            return;
        }

        var guardProcessRunning = GuardLauncher.IsRunning();

        // 进程还在、只是心跳文件刚好被替换 / 读失败：先给宽限，绝不抢在 Guard 启动前误罚。
        if (guardProcessRunning && guard is null)
        {
            _guardFirstMissingUtc = DateTime.MinValue;
            if (!_guardSeenThisSession)
            {
                GuardLauncher.TryStartPatrol();
            }

            return;
        }

        if (!_guardSeenThisSession)
        {
            // 刚开始自律时 Guard 还在启动：给启动宽限，不记 Kill。
            if (_guardFirstMissingUtc == default)
            {
                _guardFirstMissingUtc = now;
            }

            GuardLauncher.TryStartPatrol();
            return;
        }

        if (_guardFirstMissingUtc == default)
        {
            _guardFirstMissingUtc = now;
        }

        var grace = guardProcessRunning ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(3);
        if (now - _guardFirstMissingUtc < grace)
        {
            return;
        }

        var missingSeconds = Math.Round((now - _guardFirstMissingUtc).TotalSeconds);
        TamperLedger.Record(
            TamperIncidentKinds.GuardKilled,
            engine.SessionId,
            Environment.ProcessId,
            $"看门狗失联（{reason}，约 {missingSeconds} 秒）");

        _guardSeenThisSession = false;
        _guardFirstMissingUtc = now;

        var applied = TamperLedger.ApplyPending(settings, out var addedSeconds);
        GuardLauncher.TryStartPatrol();
        if (applied > 0 || addedSeconds > 0)
        {
            App.State.Save();
            TamperLedger.CleanupApplied(settings);
        }
    }

    // ---------------------------------------------------------------- 状态刷新

    private void OnEngineChanged()
    {
        var engine = App.State.Engine;

        if (engine.IsActive)
        {
            if (!_enforcementApplied)
            {
                _enforcementApplied = true;
                LockInputs(true);
            }

            UpdateUi();
            return;
        }

        if (_enforcementApplied)
        {
            _enforcementApplied = false;
            _lastPhase = SessionPhase.Idle;
            var revertMessages = _enforcement.Revert();
            LockInputs(false);
            MikoText.SetText(StatusText, EnforcementService.Join(revertMessages));

            var summary = engine.Phase switch
            {
                SessionPhase.Finished => "恭喜，本次自律计划全部完成！",
                SessionPhase.Aborted => "本次自律已主动中断（不计入自律次数）。",
                _ => "本次自律已结束。",
            };

            _closingOverlay = true;
            if (_overlay is not null)
            {
                _overlay.ReleaseAndClose();
                _overlay = null;
            }

            _closingOverlay = false;

            // 硬性规则：必须先解除遮罩，再解除键盘封锁，然后才允许弹任何模态窗口。
            KeyboardGuard.Disable();

            var debtParts = new List<string>();
            if (App.State.Settings.DebtSeconds > 0)
            {
                debtParts.Add($"{App.State.Settings.DebtSeconds / 60.0:F1} 分钟");
            }

            if (App.State.Settings.KillPenaltyDebtSeconds > 0)
            {
                debtParts.Add($"Kill 惩罚 {App.State.Settings.KillPenaltyDebtSeconds / 60.0:F1} 分钟");
            }

            var patrolReport = PatrolLog.Summarize(PatrolLog.Load());
            var keyReport = KeyboardGuard.BlockedCount > 0
                ? $"本次拦截按键：{KeyboardGuard.BlockedCount} 次（最后一次：{KeyboardGuard.LastBlocked}）"
                : "本次没有拦截到按键";
            AppMessage.Info(HostWindow,
                $"{summary}\n\n" +
                BuildTaskSummary(engine.Plan) + "\n" +
                $"计划：{engine.Plan}\n" +
                $"完成轮数：{engine.RoundsCompleted}/{engine.TotalRounds}\n" +
                $"欠债：{(debtParts.Count > 0 ? string.Join("；", debtParts) : "无")}\n\n" +
                $"本次拦截：{patrolReport}\n" +
                $"{keyReport}\n" +
                $"封锁已解除：\n{StatusText.Text}");

            engine.Reset();
            RefreshTasks();
        }

        UpdateUi();
    }

    private void LockInputs(bool locked)
    {
        var taskPerRound = _startMode == StartMode.Task && UniformDurationCheck.IsChecked != true;
        PresetBox.IsEnabled = !locked && !taskPerRound;
        CustomMinutesBox.IsEnabled = !locked && !taskPerRound && PresetBox.SelectedIndex == Presets.Length - 1;
        RoundsBox.IsEnabled = !locked;
        BreakBox.IsEnabled = !locked && RoundsBox.SelectedIndex > 0;
        UniformDurationCheck.IsEnabled = !locked;
        UniformTaskCheck.IsEnabled = !locked;
        ApplyTasksToAllButton.IsEnabled = !locked;
        BlockAppsCheck.IsEnabled = !locked;
        WhitelistCheck.IsEnabled = !locked;
        KeyboardCheck.IsEnabled = !locked;
        OverlayCheck.IsEnabled = !locked;
        NetworkOffRadio.IsEnabled = !locked;
        NetworkGentleRadio.IsEnabled = !locked;
        NetworkHardcoreRadio.IsEnabled = !locked;
        StartButton.IsEnabled = !locked;
        AbortButton.IsEnabled = locked;
        RecoveryAbortButton.IsEnabled = locked;
        SetupHubPanel.IsEnabled = !locked;
        SetupDetailPanel.IsEnabled = !locked;

        foreach (var box in _roundMinuteBoxes)
        {
            box.IsEnabled = !locked && taskPerRound;
        }

        foreach (var button in _roundTaskButtons)
        {
            button.IsEnabled = !locked;
        }
    }

    private void UpdateUi()
    {
        var engine = App.State.Engine;
        var settings = App.State.Settings;

        // 提前退出欠债按月清空：跨月时立即刷新界面并落盘（正常月份只做字符串比较，不写盘）。
        if (ExitDebtPolicy.EnsureMonth(settings))
        {
            App.State.Save();
        }

        var active = engine.IsActive;

        MikoText.SetText(PhaseText, engine.PhaseText);
        MikoText.SetText(RoundText, engine.Plan?.ToString() ?? BuildStartSummary());
        MikoText.SetText(PlanSummaryText, BuildStartSummary());
        CountdownText.Text = Format(active ? engine.Remaining : TimeSpan.FromMinutes(PreviewMinutes()));
        Progress.Value = engine.OverallProgress * 100;

        var keyboardStatus = active
            ? (KeyboardGuard.Blocker?.IsActive == true
                ? $"键盘封锁：生效中（已拦 {KeyboardGuard.BlockedCount} 次{(string.IsNullOrEmpty(KeyboardGuard.LastBlocked) ? string.Empty : "，最后：" + KeyboardGuard.LastBlocked)}）"
                : "键盘封锁：未生效（可能被安全软件拦截）")
            : string.Empty;
        MikoText.SetText(KeyboardStatusText, keyboardStatus);

        if (_overlay is not null)
        {
            if (!active)
            {
                _closingOverlay = true;
                _overlay.AllowClose();
                _overlay.Close();
                _overlay = null;
                _closingOverlay = false;
            }
            else
            {
                if (engine.Phase == SessionPhase.Breaking)
                {
                    if (_overlay.IsVisible)
                    {
                        _overlay.Hide();
                    }
                }
                else if (!_overlay.IsVisible && !_exitDialogOpen)
                {
                    _overlay.Show();
                }

                _overlay.UpdateState(engine, settings, keyboardStatus);
            }
        }

        var debtParts = new List<string>();
        if (settings.DebtSeconds > 0)
        {
            debtParts.Add($"中断欠债 {settings.DebtSeconds / 60.0:F1} 分钟（每月封顶 120 分钟）");
        }

        if (settings.KillPenaltyDebtSeconds > 0)
        {
            debtParts.Add($"Kill 惩罚欠债 {settings.KillPenaltyDebtSeconds / 60.0:F1} 分钟（本月封顶 60）");
        }

        MikoText.SetText(
            DebtText,
            debtParts.Count > 0
                ? string.Join("；", debtParts) + "（会加在本次第一轮）"
                : "无欠债");

        // 中场休息  自律：放开 / 恢复应用屏蔽（断网不受影响）。
        if (active && engine.Phase != _lastPhase && _lastPhase != SessionPhase.Idle)
        {
            var focusing = engine.Phase == SessionPhase.Focusing;
            _enforcement.SetAppBlockingActive(focusing, engine.Plan);
            MikoText.SetText(
                StatusText,
                focusing
                    ? "回到自律：应用屏蔽已恢复，继续加油。"
                    : "中场休息：应用屏蔽已暂时放开，断网档位仍然生效。");
        }

        _lastPhase = engine.Phase;

        if (!active && string.IsNullOrEmpty(StatusText.Text))
        {
            MikoText.SetText(StatusText, "准备就绪。");
        }
    }

    private static string Format(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
        : $"{span.Minutes:00}:{span.Seconds:00}";
}
