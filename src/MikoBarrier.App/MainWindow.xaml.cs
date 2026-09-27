using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MikoBarrier.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;
using MikoBarrier.Dialogs;
using MikoBarrier.Views;

namespace MikoBarrier;

public partial class MainWindow : Window
{
    private readonly FocusView _focusView = new();
    private readonly RulesView _rulesView = new();
    private readonly TasksView _tasksView = new();
    private readonly StatsView _statsView = new();
    private readonly SettingsView _settingsView = new();

    private TrayIconHost? _tray;
    private bool _reallyExit;
    private string _currentTag = "focus";

    public MainWindow()
    {
        InitializeComponent();

        // 任务栏 / 最小化时显示应用图标（exe 图标之外的兜底，保证窗口级图标一致）。
        Icon = AppIcon.WindowIcon;

        ContentHost.Content = _focusView;
        VersionText.Text = AppInfo.DisplayVersion;
        TitleBar.HelpRequested += (_, _) => ShowHelp();
        AdminHint.Text = AdminHelper.IsElevated
            ? "已获得管理员权限"
            : "未获得管理员权限";

        InitTray();
        UpdateChrome();

        App.State.Engine.Changed += (_, _) => Dispatcher.Invoke(RefreshNavigationState);
        App.State.SettingsChanged += () => Dispatcher.Invoke(() =>
        {
            ApplyTrayVisibility();
            _tray?.RefreshModeTexts();
        });

        SourceInitialized += (_, _) =>
        {
            WindowFx.ApplyRoundedCorners(this);
            WindowFx.AttachMaximizeFix(this);
        };

        StateChanged += (_, _) => UpdateChrome();
        Closing += OnClosing;
        Loaded += OnLoadedFirstTime;
    }

    private void UpdateChrome() =>
        RootBorder.CornerRadius = WindowState == WindowState.Maximized
            ? new CornerRadius(0)
            : new CornerRadius(14);

    // ------------------------------------------------------------ 启动后的崩溃恢复

    private void OnLoadedFirstTime(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedFirstTime;
        WindowFx.BringToForeground(this);

        // 开机启动 / 任务计划程序拉起时，前台权限可能被系统拒绝；
        // 600ms 后再补一次，但如果用户已经开始操作、窗口已在前台就不再抢。
        var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        retry.Tick += (_, _) =>
        {
            retry.Stop();

            // 如果此时已经有本程序的模态窗口（例如异常恢复确认框）在前台，绝不能再把主窗口抢上来。
            var otherActive = Application.Current.Windows
                .OfType<Window>()
                .Any(w => w != this && w.IsActive);
            if (!IsActive && IsVisible && !otherActive)
            {
                WindowFx.BringToForeground(this);
            }
        };
        retry.Start();

        TryRecoverSession();
        TryShowWelcomeGuide();
    }

    private void TryShowWelcomeGuide()
    {
        if (App.IsAutomatedMode || App.State.Guides.HasSeen(HelpContent.Welcome))
        {
            return;
        }

        GuideHooks.ShowOnFirstVisit(this, HelpContent.Welcome);
    }

    private void ShowHelp()
    {
        var key = (ContentHost.Content as IGuidedView)?.CurrentGuideKey ?? HelpContent.FocusHub;
        var dialog = new HelpDialog(key);
        if (IsVisible)
        {
            dialog.Owner = this;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ReplayRequested += (_, _) =>
            (ContentHost.Content as IGuidedView)?.ReplayCurrentGuide();
        dialog.Show();
        WindowFx.BringToForeground(dialog);
    }

    private void TryRecoverSession()
    {
        var snapshot = App.State.PendingRecovery;
        if (snapshot?.Plan is null)
        {
            return;
        }

        // 被 Kill 后自动续跑：不再给“放弃本次”的绕过选项；欠债已由事件账本记入。
        if (App.ForceResumePendingSession)
        {
            App.ForceResumePendingSession = false;
            var (autoOk, autoMessage) = App.State.ResumePendingSession();
            if (!autoOk)
            {
                AppMessage.Warn(this, "自动继续失败：" + autoMessage);
                return;
            }

            NavFocus.IsChecked = true;
            _currentTag = "focus";
            ContentHost.Content = _focusView;
            _focusView.ResumeAfterRecovery();
            _focusView.SetStatus("检测到上次自律被强制结束，已自动继续并记入 Kill 欠债。" + autoMessage);
            return;
        }

        var phase = snapshot.Phase == nameof(SessionPhase.Breaking) ? "中场休息" : "自律";
        var keep = AppMessage.Confirm(this,
            "检测到一次异常中断的自律（断电 / 崩溃 / 被强制结束）：\n\n" +
            $"　计划：{snapshot.Plan}\n" +
            $"　进度：第 {snapshot.CurrentRound}/{snapshot.TotalRounds} 轮（{phase}，本轮剩余 {Format(snapshot.PhaseSecondsRemaining)}）\n\n" +
            "「继续上次的自律」= 接着上次的进度继续，不额外惩罚。\n" +
            "「放弃本次」= 不计入次数，也不产生欠债。",
            "MikoBarrier", okText: "继续上次的自律", cancelText: "放弃本次");

        if (keep)
        {
            var (ok, message) = App.State.ResumePendingSession();
            if (!ok)
            {
                AppMessage.Warn(this, "恢复失败：" + message);
                return;
            }

            NavFocus.IsChecked = true;
            _currentTag = "focus";
            ContentHost.Content = _focusView;
            _focusView.ResumeAfterRecovery();
            _focusView.SetStatus("已继续上次中断的自律。" + message);
        }
        else
        {
            App.State.DiscardPendingSession();
        }
    }

    // ------------------------------------------------------------ 导航

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        // 强制模式：自律进行中禁止离开自律页去改配置。
        if (App.State.Engine.IsActive && tag != "focus")
        {
            AppMessage.Info(this, "自律进行中，名单与设置已锁定，请先凭密码结束本次自律。");
            NavFocus.IsChecked = true;
            return;
        }

        _currentTag = tag;
        var view = tag switch
        {
            "focus" => (FrameworkElement)_focusView,
            "rules" => _rulesView,
            "tasks" => _tasksView,
            "stats" => _statsView,
            "settings" => _settingsView,
            _ => _focusView,
        };
        ContentHost.Content = view;

        switch (tag)
        {
            case "focus":
                if (!App.State.Engine.IsActive)
                {
                    _focusView.RefreshTasks();
                }
                break;
            case "rules":
                _rulesView.Refresh();
                break;
            case "tasks":
                _tasksView.Refresh();
                break;
            case "stats":
                _statsView.Refresh();
                break;
            case "settings":
                _settingsView.Refresh();
                break;
        }

        // 页面自己的 Refresh 可能刚写入动态文案，先刷新完再统一翻译。
        MikoText.ApplyTo(view);
    }

    private void RefreshNavigationState()
    {
        var locked = App.State.Engine.IsActive;
        NavRules.IsEnabled = !locked;
        NavTasks.IsEnabled = !locked;
        NavStats.IsEnabled = !locked;
        NavSettings.IsEnabled = !locked;
        _tray?.SetExitEnabled(!locked);

        if (locked)
        {
            NavFocus.IsChecked = true;
            _currentTag = "focus";
            ContentHost.Content = _focusView;
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ThemeDialog { Owner = this };
        dialog.ShowDialog();
    }

    // ------------------------------------------------------------ 托盘

    private void InitTray()
    {
        _tray = new TrayIconHost(
            open: RestoreFromTray,
            startFocus: () =>
            {
                RestoreFromTray();
                NavFocus.IsChecked = true;
                _currentTag = "focus";
                ContentHost.Content = _focusView;
            },
            backToTimer: () => _focusView.ShowOverlayNow(),
            exit: TryExitFromTray);

        ApplyTrayVisibility();
    }

    private void ApplyTrayVisibility()
    {
        if (_tray is null)
        {
            return;
        }

        _tray.Visible = App.State.Settings.MinimizeToTray;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        WindowFx.BringToForeground(this);
    }

    private void TryExitFromTray()
    {
        if (App.State.Engine.IsActive)
        {
            RestoreFromTray();
            AppMessage.Warn(this, "自律进行中，请先在\u201C自律结界\u201D页凭密码结束本次自律。");
            return;
        }

        _reallyExit = true;
        Close();
    }

    // ------------------------------------------------------------ 关闭行为

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // 系统关机 / 注销：无条件放行，绝不弹密码框、绝不缩到托盘。
        if (_reallyExit || App.ForceExit || App.IsSessionEnding)
        {
            _tray?.Dispose();
            return;
        }

        if (App.State.Engine.IsActive)
        {
            e.Cancel = true;
            AppMessage.Warn(this, "自律进行中，请先在\u201C自律结界\u201D页凭密码结束本次自律。");
            return;
        }

        if (App.State.Settings.MinimizeToTray && _tray is not null)
        {
            // 最小化到托盘：静默隐藏，不弹系统通知（用户反馈过通知太吵）。
            e.Cancel = true;
            Hide();
        }
    }

    private static string Format(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }
}
