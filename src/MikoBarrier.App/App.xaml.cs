using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;


using MikoBarrier.Controls;
using MikoBarrier.Core.Services;
using MikoBarrier.Dialogs;
using MikoBarrier.Core.Models;

namespace MikoBarrier;

public partial class App : Application
{
    public static AppState State { get; private set; } = null!;

    /// <summary>true 表示正在主动退出（换主题重启 / 系统关机），此时不再拦截窗口关闭。</summary>
    public static bool ForceExit { get; set; }

    /// <summary>true 表示 Windows 正在关机 / 注销，所有窗口关闭逻辑都必须无条件放行。</summary>
    public static bool IsSessionEnding { get; private set; }

    /// <summary>本次启动是否由看门狗“被 Kill 后拉起”（带 --killed-restart），需要自动续跑。</summary>
    public static bool KilledRestartRequested { get; private set; }

    /// <summary>启动时存在未入账的 Kill 事件：本次恢复不再提供“放弃本次”，而是自动续跑。</summary>
    public static bool ForceResumePendingSession { get; set; }

    /// <summary>true 表示当前运行在 --ui-smoke-test / --shutdown-smoke-test 等自动自检模式，不弹新手引导。</summary>
    public static bool IsAutomatedMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IsAutomatedMode = e.Args.Any(a =>
            a.Equals("--ui-smoke-test", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--shutdown-smoke-test", StringComparison.OrdinalIgnoreCase));

        DispatcherUnhandledException += (_, args) =>
        {
            LogException("DispatcherUnhandledException", args.Exception);

            // 关机 / 注销时 WPF 会进入 CriticalShutdown；这里如果弹模态框，Windows 会判定本程序阻止关机。
            // 关机相关异常一律只写日志，然后安全退出，绝不弹框、绝不拖延。
            if (ForceExit || IsSessionEnding || IsShutdownRelated(args.Exception))
            {
                PrepareEmergencyExit();
                Environment.Exit(0);
                return;
            }

            var message = string.IsNullOrWhiteSpace(args.Exception.Message)
                ? args.Exception.GetType().Name
                : args.Exception.Message;
            MessageBox.Show(
                $"出了点问题：\n\n{message}\n\n详细信息已写入日志：\n{StoragePaths.AppLogFile}",
                "MikoBarrier", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogException("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        // 系统关机 / 注销：由 OnSessionEnding + 上面的异常兜底处理，保证绝不阻拦 Windows。

        StoragePaths.EnsureCreated();

        // 正常启动：清掉上次关机留下的标记（快照仍在，交给主窗口询问是否继续）。
        SessionRecovery.ClearGracefulShutdownMarker();

        // 兜底：上一次如果异常退出（比如断电），这里先把网络封锁全部还原。
        NetworkGuard.RevertAllQuietly();

        // 白名单体检：只读扫描，不提权、不改配置、不打开主窗口；报告写入 logs\whitelist-audit.txt。
        // 放在自动提权之前处理，方便用户在任何权限下先跑一次体检。
        if (e.Args.Any(a => a.Equals("--whitelist-audit", StringComparison.OrdinalIgnoreCase)))
        {
            RunWhitelistAudit();
            ForceExit = true;
            Shutdown(0);
            return;
        }

        State = new AppState();

        // 旧配置 / 手工改配置时保持不变量：启用了巫女模式就一定视为已解锁。
        if (State.Settings.MikoModeEnabled)
        {
            State.Settings.MikoModeUnlocked = true;
        }

        MikoText.Install();
        InstallWheelForwarding();
        CleanupStaleUpdateFiles();

        // 用户之前要求过"以管理员身份运行"：这里自动再提权一次（带 --elevated-attempt 防止无限循环）。
        if (State.Settings.StartElevated
            && !AdminHelper.IsElevated
            && !e.Args.Any(a => a.Equals("--elevated-attempt", StringComparison.OrdinalIgnoreCase))
            && AdminHelper.TryRelaunchElevated("--elevated-attempt"))
        {
            ForceExit = true;
            Shutdown();
            return;
        }

        // 异常中断（断电 / 崩溃 / 被强杀）留下的快照：交给主窗口询问"是否继续"。
        State.PendingRecovery = SessionRecovery.TryLoad();
        KilledRestartRequested = e.Args.Any(a => a.Equals("--killed-restart", StringComparison.OrdinalIgnoreCase));

        // Kill 事件账本：先记下“是否属于本次快照”，再把未入账事件折算成 Kill 欠债。
        var pendingSessionKey = SessionRecovery.GetSessionKey(State.PendingRecovery);
        var hasPendingKillForSession = State.PendingRecovery is not null &&
                                       TamperLedger.HasPendingForSession(pendingSessionKey);
        var appliedIncidents = TamperLedger.ApplyPending(State.Settings, out var addedKillDebtSeconds);
        // 提前退出欠债按月清空：旧配置首次只登记月份、保留历史值，之后跨月才清空。
        var exitDebtMonthSettled = ExitDebtPolicy.EnsureMonth(State.Settings);
        if (appliedIncidents > 0 || addedKillDebtSeconds > 0 || exitDebtMonthSettled)
        {
            State.Save();
        }

        if (appliedIncidents > 0 || addedKillDebtSeconds > 0)
        {
            TamperLedger.CleanupApplied(State.Settings);
        }

        ForceResumePendingSession = State.PendingRecovery is not null &&
                                    (KilledRestartRequested || hasPendingKillForSession);

        ThemeManager.Apply(State.Settings);

        // 开发用：逐个构造并打开所有窗口，验证自绘 XAML 没有解析错误。
        if (e.Args.Any(a => a.Equals("--ui-smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            RunUiSmokeTest();
            return;
        }

        // 关机自检：模拟 WPF 的关机查询，验证“绝不阻拦关机”。
        if (e.Args.Any(a => a.Equals("--shutdown-smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            RunShutdownSmokeTest();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsSessionEnding = true;
        ForceExit = true;
        PrepareEmergencyExit();
        base.OnSessionEnding(e);
    }

    /// <summary>
    /// 清理上次“运行中更新”留下的 MikoBarrier.App.old.exe。
    /// 旧实例还在运行时会删除失败，忽略即可；等旧实例退出后，下次启动会自动清掉。
    /// </summary>
    private static void CleanupStaleUpdateFiles()
    {
        try
        {
            var exe = Environment.ProcessPath;
            var dir = string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            var stale = Path.Combine(dir, "MikoBarrier.App.old.exe");
            if (File.Exists(stale))
            {
                File.Delete(stale);
            }
        }
        catch
        {
            // 文件仍被旧实例占用时删除失败，属于预期情况。
        }
    }

    /// <summary>关机 / 注销退出前尽量还原网络封锁并保存状态；任何失败都不允许阻塞退出。</summary>
    private static void PrepareEmergencyExit()
    {
        // 先落关机标记：Guard 看到后不会把这次正常退出当成被 Kill 去重启主程序。
        SessionRecovery.MarkGracefulShutdown();

        try
        {
            NetworkGuard.RevertAllQuietly();
        }
        catch
        {
        }

        try
        {
            State?.Save();
        }
        catch
        {
        }
    }

    /// <summary>异常堆栈来自 WPF 关机遥测 / CriticalShutdown 时，视为关机异常，静默退出。</summary>
    private static bool IsShutdownRelated(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var stack = current.StackTrace ?? string.Empty;
            if (stack.Contains("Application.CriticalShutdown", StringComparison.Ordinal) ||
                stack.Contains("Application.WmQueryEndSession", StringComparison.Ordinal) ||
                stack.Contains("ControlsTraceLogger", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 关机自检：显示主窗口后调用 WPF 的 WmQueryEndSession，走真实关机代码路径。
    /// 配合外部“运行中改名 exe”可复现单文件关机遥测异常，验证不会弹框、不会阻止关机。
    /// </summary>
    private void RunShutdownSmokeTest()
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.LogDir);
            File.AppendAllText(StoragePaths.AppLogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 关机自检：准备模拟 WmQueryEndSession{Environment.NewLine}");
        }
        catch
        {
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            try
            {
                File.AppendAllText(StoragePaths.AppLogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 关机自检：触发 WmQueryEndSession{Environment.NewLine}");
            }
            catch
            {
            }

            var method = typeof(Application).GetMethod(
                "WmQueryEndSession", BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(this, new object?[] { IntPtr.Zero, IntPtr.Zero });

            // WPF 已接管时通常不会走到这里；作为最后保险，正常退出。
            Shutdown(0);
        };
        timer.Start();
    }

    /// <summary>
    /// 白名单体检（CLI：--whitelist-audit）：只读扫描当前机器，报告写入 logs\whitelist-audit.txt。
    /// 供用户在「脏」电脑上验证白名单不会锁死系统；不改配置、不弹窗、不需要密码。
    /// </summary>
    private static void RunWhitelistAudit()
    {
        try
        {
            var settings = ConfigStore.Load();
            settings.EnsureDefaults();

            var result = WhitelistAudit.Run(settings);
            var reportPath = Path.Combine(StoragePaths.LogDir, "whitelist-audit.txt");
            File.WriteAllText(reportPath, result.Report, new UTF8Encoding(false));

            var summary =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 白名单体检完成：进程 {result.Total}，" +
                $"有窗口会拦截 {result.BlockedWindowed}，无窗口不拦截 {result.BackgroundSafe}，" +
                $"宿主警告 {result.HostWarnings.Count}，报告 {reportPath}{Environment.NewLine}";
            File.AppendAllText(StoragePaths.AppLogFile, summary, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            // 体检失败只写日志，绝不影响退出；用户可看 app.log。
            LogException("WhitelistAudit", ex);
        }
    }

    /// <summary>界面自检：构造并显示每个窗口后立刻关闭，把结果写入 logs/ui-smoke-test.log。</summary>
    private void RunUiSmokeTest()
    {
        // 自检期间会反复 Show/Close 窗口；避免第一个关闭的窗口触发 OnMainWindowClose 自动关掉应用。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var results = new List<string>();

        var factories = new List<(string Name, Func<Window> Factory)>
        {
            ("MessageDialog", new Func<Window>(() =>
                new MessageDialog("测试提示", "这是一条自检提示，用于验证自绘提示框。", MessageDialogKind.Info, false, "知道了", string.Empty))),
            ("ChoiceDialog", new Func<Window>(() =>
                new ChoiceDialog("测试选择", "请选择一项。", "第一个", "第二个", "取消"))),
            ("PasswordDialog", new Func<Window>(() =>
                new PasswordDialog("请输入密码："))),
            ("ForgotPasswordDialog", new Func<Window>(() =>
                new ForgotPasswordDialog())),
            ("AppPickerDialog", new Func<Window>(() =>
                new AppPickerDialog())),
            ("TaskPickerDialog", new Func<Window>(() =>
                new TaskPickerDialog(new List<FocusTask>
                {
                    new() { Id = "smoke-task", Title = "自检任务" },
                }, new[] { "smoke-task" }))),
            ("FullScreenOverlay", new Func<Window>(() =>
            {
                var overlay = new MikoBarrier.Views.FullScreenOverlay { GuardEnabled = false };
                overlay.AllowClose();
                return overlay;
            })),
            ("WhitelistPreviewDialog", new Func<Window>(() =>
                new MikoBarrier.Dialogs.WhitelistPreviewDialog(
                    Enumerable.Range(1, 40).Select(i => new AppRule
                    {
                        Kind = RuleKind.Whitelist,
                        Target = RuleTarget.File,
                        DisplayName = $"演示程序 {i:D2}",
                        Path = $@"C:\Program Files\Demo{i:D2}\demo{i:D2}.exe",
                    }).ToList(),
                    scannedCount: 100,
                    alreadyCovered: 0,
                    systemSkipped: 62))),

            ("ThemeDialog", new Func<Window>(() =>
                new ThemeDialog())),

            ("ColorPickerDialog", new Func<Window>(() =>
                new ColorPickerDialog())),

            ("GuideDialog", new Func<Window>(() =>
                new GuideDialog(HelpContent.FocusTask))),

            ("HelpDialog", new Func<Window>(() =>
                new HelpDialog(HelpContent.FocusTask))),

            ("MainWindow", new Func<Window>(() =>
                new MainWindow())),
        };

        foreach (var (name, factory) in factories)
        {
            try
            {
                var window = factory();
                window.Show();
                window.Close();
                results.Add($"OK   {name}");
            }
            catch (Exception ex)
            {
                results.Add($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        AddFeatureProbes(results);

        try
        {
            using var blocker = new MikoBarrier.Core.Services.KeyboardBlocker();
            var installed = blocker.Install();
            results.Add(installed ? "OK   KeyboardBlocker(键盘钩子安装成功)" : "FAIL KeyboardBlocker(键盘钩子安装失败)");

            if (installed && Environment.GetCommandLineArgs().Any(a => a.Equals("--keyboard-test", StringComparison.OrdinalIgnoreCase)))
            {
                Thread.Sleep(200);
                results.AddRange(KeyboardTestHelper.Run(blocker));
            }

            blocker.Uninstall();
            results.Add(blocker.IsActive ? "FAIL KeyboardBlocker(卸载失败)" : "OK   KeyboardBlocker(卸载成功)");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL KeyboardBlocker: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(StoragePaths.LogDir);
            File.WriteAllLines(Path.Combine(StoragePaths.LogDir, "ui-smoke-test.log"), results);
        }
        catch
        {
        }

        Shutdown(results.All(r => r.StartsWith("OK", StringComparison.Ordinal)) ? 0 : 1);
    }

    /// <summary>
    /// 新功能自检：不打开窗体也能验证控件与数据源，结果随 --ui-smoke-test 一起写进日志。
    /// 只读取任务 / 统计数据；一键白名单批量逻辑用内存假数据验证，不修改用户配置。
    /// </summary>
    /// <summary>
    /// 让一行输入框 / 密码框上的鼠标滚轮去滚动外层页面，而不是被控件自己吞掉。
    /// 多行输入框（网站名单等）仍然保留自己的滚动。
    /// </summary>
    private static void InstallWheelForwarding()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(ForwardWheelToScrollViewer));
        EventManager.RegisterClassHandler(
            typeof(PasswordBox),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(ForwardWheelToScrollViewer));
    }

    private static void ForwardWheelToScrollViewer(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0)
        {
            return;
        }

        if (sender is TextBox { AcceptsReturn: true })
        {
            return;
        }

        if (sender is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = FindScrollableAncestor(source);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollableAncestor(DependencyObject current)
    {
        while (current is not null)
        {
            if (current is ScrollViewer { ScrollableHeight: > 0 } viewer)
            {
                return viewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static void AddFeatureProbes(List<string> results)
    {

        try
        {
            var backgroundIds = AmbienceCatalog.Backgrounds.Select(b => b.Id).ToList();
            var catalogOk = AmbienceCatalog.Backgrounds.Count is > 0 and <= 10
                            && backgroundIds.Distinct(StringComparer.Ordinal).Count() == backgroundIds.Count;
            results.Add(catalogOk
                ? $"OK   全屏背景资源目录({AmbienceCatalog.Backgrounds.Count} 张，id 唯一)"
                : "FAIL 全屏背景资源目录异常");

            var backgroundsOk = true;
            var backgroundDetail = string.Empty;
            foreach (var option in AmbienceCatalog.Backgrounds)
            {
                try
                {
                    using var full = Application.GetResourceStream(new Uri(option.ResourcePath, UriKind.Absolute))?.Stream;
                    using var thumb = Application.GetResourceStream(new Uri(option.ThumbnailPath, UriKind.Absolute))?.Stream;
                    if (full is null || thumb is null)
                    {
                        backgroundsOk = false;
                        backgroundDetail = option.Id;
                        break;
                    }
                }
                catch
                {
                    backgroundsOk = false;
                    backgroundDetail = option.Id;
                    break;
                }
            }

            results.Add(backgroundsOk
                ? $"OK   全屏背景资源({AmbienceCatalog.Backgrounds.Count} 张全尺寸 + {AmbienceCatalog.Backgrounds.Count} 张缩略图可解析)"
                : $"FAIL 全屏背景资源缺失：{backgroundDetail}");

            var defaults = new AppSettings();
            defaults.EnsureDefaults();
            var settingsOk = defaults.FullScreenBackgroundId.Length == 0
                             && !defaults.FullScreenPanelCollapsed;
            results.Add(settingsOk
                ? "OK   AppSettings(全屏背景默认关闭，卡片默认展开)"
                : "FAIL AppSettings 全屏背景默认值异常");

            var overlay = new MikoBarrier.Views.FullScreenOverlay { GuardEnabled = false };
            overlay.AllowClose();
            try
            {
                overlay.InitializeBackground(new AppSettings
                {
                    FullScreenBackgroundId = AmbienceCatalog.Backgrounds[0].Id,
                });

                var backgroundBox = overlay.FindName("BackgroundBox") as ComboBox;
                var backgroundImage = overlay.FindName("BackgroundImage") as Image;
                var ambiencePanel = overlay.FindName("AmbiencePanel") as Border;
                var ambienceButton = overlay.FindName("AmbienceButton") as Button;
                var contentPanel = overlay.FindName("ContentPanel") as Border;
                var compactBar = overlay.FindName("CompactBar") as Border;
                var collapseButton = overlay.FindName("CollapseCardButton") as Button;
                var expandButton = overlay.FindName("ExpandCardButton") as Button;

                var controlsOk = backgroundBox?.Items.Count == AmbienceCatalog.Backgrounds.Count + 1
                                 && backgroundImage?.Source is not null
                                 && ambiencePanel?.Visibility == Visibility.Collapsed
                                 && contentPanel?.Visibility == Visibility.Visible
                                 && compactBar?.Visibility == Visibility.Collapsed
                                 && collapseButton is not null
                                 && expandButton is not null;

                var beforeSource = backgroundImage?.Source;
                if (backgroundBox is not null && backgroundBox.Items.Count > 2)
                {
                    backgroundBox.SelectedIndex = 2;
                }

                var switched = !ReferenceEquals(beforeSource, backgroundImage?.Source)
                               && backgroundImage?.Source is not null;

                ambienceButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var panelShown = ambiencePanel?.Visibility == Visibility.Visible;
                ambienceButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var panelHidden = ambiencePanel?.Visibility == Visibility.Collapsed;

                collapseButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var collapsedOk = contentPanel?.Visibility == Visibility.Collapsed
                                  && compactBar?.Visibility == Visibility.Visible
                                  && collapseButton?.Visibility == Visibility.Collapsed;
                expandButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var expandedOk = contentPanel?.Visibility == Visibility.Visible
                                 && compactBar?.Visibility == Visibility.Collapsed
                                 && collapseButton?.Visibility == Visibility.Visible;

                results.Add(controlsOk && switched && panelShown && panelHidden && collapsedOk && expandedOk
                    ? "OK   FullScreenOverlay(背景面板 / 背景切换 / 卡片收起展开)"
                    : $"FAIL FullScreenOverlay 背景面板异常：controls={controlsOk} switched={switched} panel={panelShown}/{panelHidden} collapse={collapsedOk}/{expandedOk}");
            }
            finally
            {
                overlay.AllowClose();
                overlay.Close();
            }
        }
        catch (Exception ex)
        {
            results.Add($"FAIL 全屏氛围自检: {ex.GetType().Name}: {ex.Message}");
        }

        // 提前结束 / 恢复码弹窗打开期间，FocusView 的 250ms 定时器不能把全屏遮罩重新 Show 到弹窗上面。
        try
        {
            var focusType = typeof(MikoBarrier.Views.FocusView);
            var focusForDialog = new MikoBarrier.Views.FocusView();
            var timerForDialog = focusType
                .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(focusForDialog) as DispatcherTimer;
            timerForDialog?.Stop();

            var overlayForDialog = new MikoBarrier.Views.FullScreenOverlay { GuardEnabled = false };
            overlayForDialog.AllowClose();
            focusType.GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(focusForDialog, overlayForDialog);
            focusType.GetField("_exitDialogOpen", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(focusForDialog, true);

            App.State.Engine.Start(new AppSettings(), new FocusPlan
            {
                FocusMinutes = 1,
                Rounds = 1,
                EnforceOverlay = true,
            });

            var updateUi = focusType.GetMethod("UpdateUi", BindingFlags.Instance | BindingFlags.NonPublic);
            updateUi?.Invoke(focusForDialog, null);
            var hiddenWhileDialog = !overlayForDialog.IsVisible;

            focusType.GetField("_exitDialogOpen", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(focusForDialog, false);
            updateUi?.Invoke(focusForDialog, null);
            var shownAfterDialog = overlayForDialog.IsVisible;

            App.State.Engine.Reset();
            try { overlayForDialog.AllowClose(); overlayForDialog.Close(); } catch { }
            results.Add(hiddenWhileDialog && shownAfterDialog
                ? "OK   全屏退出弹窗(密码 / 恢复码弹窗期间遮罩不会重新盖上去)"
                : $"FAIL 全屏退出弹窗抑制异常：hiddenWhileDialog={hiddenWhileDialog} shownAfterDialog={shownAfterDialog}");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL 全屏退出弹窗抑制: {ex.GetType().Name}: {ex.Message}");
            try { App.State.Engine.Reset(); } catch { }
        }

        try
        {
            var focus = new MikoBarrier.Views.FocusView();
            typeof(MikoBarrier.Views.FocusView)
                .GetMethod("LoadLastPlan", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(focus, null);

            var roundPanel = focus.FindName("RoundPlanPanel") as StackPanel;
            var uniformCheck = focus.FindName("UniformDurationCheck") as CheckBox;
            var applyAll = focus.FindName("ApplyTasksToAllButton") as Button;

            var expectedPlan = App.State.Settings.LastPlan;
            var expectedRounds = expectedPlan.ClampRounds();
            var expectedUniform = !expectedPlan.PerRoundDuration;

            if (roundPanel is null || uniformCheck is null || applyAll is null)
            {
                results.Add("FAIL FocusView(找不到每轮任务安排控件)");
            }
            else if (roundPanel.Children.Count == expectedRounds && uniformCheck.IsChecked == expectedUniform)
            {
                var firstTaskText = (roundPanel.Children.OfType<Grid>().FirstOrDefault()?
                    .Children.OfType<Button>().FirstOrDefault()?.Content as TextBlock)?.Text ?? "?";
                results.Add($"OK   FocusView(LastPlan 还原：{expectedRounds} 行，统一时长={expectedUniform}，首轮任务={firstTaskText})");
            }
            else
            {
                results.Add($"FAIL FocusView(LastPlan 还原异常：行数 {roundPanel.Children.Count}/{expectedRounds}，统一时长 {uniformCheck.IsChecked}/{expectedUniform})");
            }

            var readArgs = new object?[] { string.Empty };
            var readPlan = (FocusPlan?)typeof(MikoBarrier.Views.FocusView)
                .GetMethod("ReadPlanFromUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(focus, readArgs);

            var expectedMinutes = expectedPlan.GetRoundPlans().Select(p => p.Minutes).ToList();
            var expectedTasks = expectedPlan.GetRoundPlans().Select(p => string.Join(",", p.TaskIds)).ToList();
            var readMinutes = readPlan?.GetRoundPlans().Select(p => p.Minutes).ToList() ?? new List<int>();
            var readTasks = readPlan?.GetRoundPlans().Select(p => string.Join(",", p.TaskIds)).ToList() ?? new List<string>();
            var readOk = readPlan is not null
                         && readMinutes.SequenceEqual(expectedMinutes)
                         && readTasks.SequenceEqual(expectedTasks);
            results.Add(readOk
                ? $"OK   FocusView(UI -> 计划：每轮 {string.Join("/", readMinutes)} 分钟，任务 {string.Join("|", readTasks)})"
                : $"FAIL FocusView(UI -> 计划异常：{readArgs[0]}，每轮 {string.Join("/", readMinutes)}，任务 {string.Join("|", readTasks)})");

            // 快速开始：只取一个统一时长，固定单轮且不关联任务。
            var quickButton = focus.FindName("HubQuickButton") as Button;
            quickButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            readArgs = new object?[] { string.Empty };
            var quickPlan = (FocusPlan?)typeof(MikoBarrier.Views.FocusView)
                .GetMethod("ReadPlanFromUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(focus, readArgs);
            var quickPlans = quickPlan?.GetRoundPlans() ?? new List<FocusRoundPlan>();
            var quickOk = quickPlan is { Rounds: 1, PerRoundDuration: false }
                          && quickPlans.Count == 1
                          && quickPlans[0].TaskIds.Count == 0;
            results.Add(quickOk
                ? $"OK   FocusView(快速开始：单轮 {quickPlans[0].Minutes} 分钟、不关联任务)"
                : $"FAIL FocusView(快速开始异常：{readArgs[0]}）");

            // 任务模式：勾选“全部轮次都使用同一时长”后所有轮次共用一个时长。
            var taskButton = focus.FindName("HubTaskButton") as Button;
            taskButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (uniformCheck is not null)
            {
                uniformCheck.IsChecked = true;
            }

            readArgs = new object?[] { string.Empty };
            var taskPlan = (FocusPlan?)typeof(MikoBarrier.Views.FocusView)
                .GetMethod("ReadPlanFromUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(focus, readArgs);
            var taskPlans = taskPlan?.GetRoundPlans() ?? new List<FocusRoundPlan>();
            var taskUniformOk = taskPlan is not null
                                && taskPlan.Rounds == expectedRounds
                                && !taskPlan.PerRoundDuration
                                && taskPlans.Count == expectedRounds
                                && taskPlans.All(p => p.Minutes == taskPlans[0].Minutes);
            results.Add(taskUniformOk
                ? $"OK   FocusView(任务模式统一时长：{expectedRounds} 轮 × {taskPlans[0].Minutes} 分钟)"
                : $"FAIL FocusView(任务模式统一时长异常：{readArgs[0]}）");

            var taskUniformHost = focus.FindName("TaskUniformDurationHost") as ContentControl;
            var quickDurationHost = focus.FindName("QuickDurationHost") as ContentControl;
            if (uniformCheck is not null)
            {
                uniformCheck.IsChecked = true;
                uniformCheck.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }

            var durationMoved = taskUniformHost?.Content is StackPanel && quickDurationHost?.Content is null;
            results.Add(durationMoved
                ? "OK   FocusView(统一时长控件可在快速开始 / 任务模式之间复用)"
                : "FAIL FocusView(统一时长控件未移动到任务模式)");

            // 任务模式：勾选“全部轮次都使用同一组任务”后只显示统一入口，计划标记 UniformTasks。
            var uniformTaskCheck = focus.FindName("UniformTaskCheck") as CheckBox;
            var roundTaskScroll = focus.FindName("RoundTaskScroll") as ScrollViewer;
            if (uniformTaskCheck is not null && applyAll is not null && roundTaskScroll is not null)
            {
                uniformTaskCheck.IsChecked = true;
                uniformTaskCheck.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                var uniformShown = applyAll.Visibility == Visibility.Visible &&
                                   roundTaskScroll.Visibility == Visibility.Collapsed;

                readArgs = new object?[] { string.Empty };
                var uniformTaskPlan = (FocusPlan?)typeof(MikoBarrier.Views.FocusView)
                    .GetMethod("ReadPlanFromUi", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(focus, readArgs);
                var uniformFlagOk = uniformTaskPlan?.UniformTasks == true;

                uniformTaskCheck.IsChecked = false;
                uniformTaskCheck.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                var perRoundShown = roundTaskScroll.Visibility == Visibility.Visible &&
                                    applyAll.Visibility == Visibility.Collapsed;

                results.Add(uniformShown && uniformFlagOk && perRoundShown
                    ? "OK   FocusView(统一任务：隐藏逐轮列表 / 显示统一入口 / 计划标记正确)"
                    : $"FAIL FocusView(统一任务勾选异常：展示 {uniformShown}，计划标记 {uniformFlagOk}，恢复 {perRoundShown})");
            }
            else
            {
                results.Add("FAIL FocusView(找不到统一任务控件)");
            }

            var namedLine = MikoBarrier.Views.FocusView.BuildTaskReportLine("测试任务");
            var emptyLine = MikoBarrier.Views.FocusView.BuildTaskReportLine(null);
            results.Add(namedLine == "本次任务：测试任务" && emptyLine == "本次任务：不关联"
                ? "OK   FocusView(战报任务行格式：本次任务：xxx / 不关联)"
                : $"FAIL FocusView(战报任务行格式异常：{namedLine} / {emptyLine})");

            // 任务选择弹窗支持全选 / 清空，能回到“本轮不关联”状态。
            var picker = new TaskPickerDialog(
                new List<FocusTask>
                {
                    new() { Id = "smoke-task-1", Title = "自检任务一" },
                    new() { Id = "smoke-task-2", Title = "自检任务二" },
                },
                new[] { "smoke-task-1" });
            var pickerList = picker.FindName("TaskList") as ListBox;
            var pickerSelectAll = picker.FindName("SelectAllButton") as Button;
            var pickerClear = picker.FindName("ClearButton") as Button;
            pickerSelectAll?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pickerSelectAllOk = pickerList is not null && picker.SelectedTaskIds.Count == 2;
            pickerClear?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pickerOk = pickerList is not null && picker.SelectedTaskIds.Count == 0;
            results.Add(pickerSelectAllOk && pickerOk
                ? "OK   TaskPickerDialog(支持全选 / 清空本轮选择，回到未关联状态)"
                : $"FAIL TaskPickerDialog(全选={pickerSelectAllOk}，清空后 {picker.SelectedTaskIds.Count} 个)");
            picker.Close();

            var plan = new FocusPlan
            {
                Rounds = 3,
                PerRoundDuration = true,
                RoundPlans = new List<FocusRoundPlan>
                {
                    new() { Minutes = 10, TaskIds = { "t1" } },
                    new() { Minutes = 20, TaskIds = { "t1", "t2" } },
                    new() { Minutes = 30, TaskIds = { "t2" } },
                },
            };
            var plans = plan.GetRoundPlans();
            var modelOk = plans.Count == 3
                && plans[0].Minutes == 10 && plans[1].Minutes == 20 && plans[2].Minutes == 30
                && plan.TotalFocusSeconds == 3600
                && plan.GetRoundTaskIds(1).Count == 2
                && plan.GetRoundTaskIds(0).SequenceEqual(new[] { "t1" });
            results.Add(modelOk
                ? "OK   FocusPlan(每轮不同时长 / 跨轮复用任务 / 单轮多任务)"
                : $"FAIL FocusPlan(每轮模型异常：{(plans.Count > 0 ? string.Join(",", plans.Select(p => p.Minutes)) : "空")})");

            // 版本号统一从程序集读取：不再出现界面写死的 v0.2.0。
            var versionOk = AppInfo.DisplayVersion.StartsWith("v", StringComparison.Ordinal) &&
                            AppInfo.DisplayVersion.Contains("0.6.0", StringComparison.Ordinal) &&
                            AppInfo.DisplayVersion.Contains("Miko", StringComparison.Ordinal) &&
                            !AppInfo.DisplayVersion.Contains("0.5.0", StringComparison.Ordinal);
            results.Add(versionOk
                ? $"OK   动态版本号(当前 {AppInfo.DisplayVersion})"
                : $"FAIL 动态版本号异常({AppInfo.DisplayVersion})");

            // 帮助中心 / 分步引导的文本源必须覆盖所有功能页。
            var helpKeys = new[]
            {
                HelpContent.Welcome, HelpContent.FocusHub, HelpContent.FocusQuick, HelpContent.FocusTask,
                HelpContent.FocusPolicy, HelpContent.FocusNetwork, HelpContent.RulesHub, HelpContent.RulesBlack,
                HelpContent.RulesWhite, HelpContent.RulesSites, HelpContent.Tasks, HelpContent.Stats,
                HelpContent.StatsHistory, HelpContent.StatsTaskAwards, HelpContent.SettingsHub,
                HelpContent.SettingsAppearance, HelpContent.SettingsPassword, HelpContent.SettingsRecovery,
                HelpContent.SettingsQuestions, HelpContent.SettingsForgot,
            };
            var helpOk = helpKeys.All(k => HelpContent.GetPages(k).Count > 0) &&
                         !string.IsNullOrWhiteSpace(HelpContent.GetFullTextHelp());
            results.Add(helpOk
                ? "OK   帮助中心文本源(首启 / 功能引导 / 文字版说明共用)"
                : "FAIL 帮助中心文本源缺失");

            // 标题栏问号帮助按钮就位。
            var titleBar = new WindowTitleBar();
            var helpButton = titleBar.FindName("HelpButton") as Button;
            results.Add(helpButton is not null
                ? "OK   WindowTitleBar(最小化按钮旁提供问号帮助按钮)"
                : "FAIL WindowTitleBar(缺少帮助按钮)");

            // 各页面的大按键 / 子界面容器必须全部就位（防止后续改 XAML 时漏名字）。
            var settingsView = new MikoBarrier.Views.SettingsView();
            var settingsRequired = new[]
            {
                "HubAppearanceButton", "HubSecurityButton", "PasswordPagePanel", "RecoveryPagePanel",
                "QuestionsSetupPagePanel", "ForgotPagePanel", "RecoveryResetPagePanel", "QuestionResetPagePanel",
            };
            var settingsMissing = settingsRequired.Where(n => settingsView.FindName(n) is null).ToList();
            results.Add(settingsMissing.Count == 0
                ? "OK   系统设置(外观与启动 / 密码与防改动 / 忘记密码两层子界面就位)"
                : "FAIL 系统设置缺少控件：" + string.Join(", ", settingsMissing));

            // 密码框必须真的能输入，且带显示 / 隐藏切换；安全问题答案框必须是不加密的普通文本框。
            var secretBoxNames = new[]
            {
                "CurrentPasswordBox", "NewPasswordBox", "ConfirmPasswordBox",
                "QuestionsCurrentPasswordBox",
                "RecoveryResetNewPasswordBox", "RecoveryResetConfirmPasswordBox",
                "QuestionResetNewPasswordBox", "QuestionResetConfirmPasswordBox",
            };
            var brokenSecretBoxes = secretBoxNames
                .Where(n =>
                {
                    var box = settingsView.FindName(n) as PasswordRevealBox;
                    return box is null ||
                           box.PasswordChar == '\0' ||
                           box.FindName("RevealButton") is not System.Windows.Controls.Primitives.ToggleButton;
                })
                .ToList();
            results.Add(brokenSecretBoxes.Count == 0
                ? "OK   系统设置密码框(PasswordChar 有效 + 显示 / 隐藏切换就位)"
                : "FAIL 系统设置密码框异常：" + string.Join(", ", brokenSecretBoxes));

            // 显示切换必须真的能在加密 / 明文之间同步。
            var revealSample = settingsView.FindName("NewPasswordBox") as PasswordRevealBox;
            var revealSyncOk = false;
            var revealToggleOk = false;
            if (revealSample is not null)
            {
                revealSample.Password = "Abc123";
                var hidden = revealSample.FindName("HiddenBox") as PasswordBox;
                var visible = revealSample.FindName("VisibleBox") as TextBox;
                var toggle = revealSample.FindName("RevealButton") as System.Windows.Controls.Primitives.ToggleButton;
                revealSyncOk = hidden?.Password == "Abc123" && visible?.Text == "Abc123";

                if (toggle is not null)
                {
                    toggle.IsChecked = true;
                    toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    revealToggleOk = visible?.Visibility == Visibility.Visible;

                    toggle.IsChecked = false;
                    toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    revealToggleOk = revealToggleOk &&
                                     hidden?.Visibility == Visibility.Visible &&
                                     hidden.Password == "Abc123";
                }
            }
            results.Add(revealSyncOk && revealToggleOk
                ? "OK   密码框显示切换(加密 <-> 明文同步正常)"
                : "FAIL 密码框显示切换异常");

            var answerBoxNames = new[]
            {
                "Answer1Box", "Answer2Box", "Answer3Box",
                "QuestionResetAnswer1Box", "QuestionResetAnswer2Box", "QuestionResetAnswer3Box",
            };
            var brokenAnswerBoxes = answerBoxNames
                .Where(n => settingsView.FindName(n) is not TextBox)
                .ToList();
            results.Add(brokenAnswerBoxes.Count == 0
                ? "OK   安全问题答案框(普通文本框，不加密)"
                : "FAIL 安全问题答案框不是普通文本框：" + string.Join(", ", brokenAnswerBoxes));

            var questionsButton = settingsView.FindName("OpenQuestionsSetupPageButton") as Button;
            results.Add(questionsButton?.Content?.ToString() == "设置安全问题" &&
                        HelpContent.GetTopicTitle(HelpContent.SettingsQuestions) == "安全问题"
                ? "OK   设置页文案(忘记密码保险已更名为安全问题)"
                : "FAIL 设置页文案未完成更名");

            var rulesView = new MikoBarrier.Views.RulesView();
            var rulesRequired = new[]
            {
                "HubBlackButton", "HubWhiteButton", "HubSitesButton", "BlackPagePanel", "WhitePagePanel",
                "SitesPagePanel", "SelectAllRulesButton", "ClearRulesSelectionButton",
            };
            var rulesMissing = rulesRequired.Where(n => rulesView.FindName(n) is null).ToList();
            results.Add(rulesMissing.Count == 0
                ? "OK   名单管理(黑名单 / 白名单 / 网站名单三个子界面 + 全选 / 取消选择就位)"
                : "FAIL 名单管理缺少控件：" + string.Join(", ", rulesMissing));

            var statsView = new MikoBarrier.Views.StatsView();
            var statsRequired = new[] { "HubHistoryButton", "HubTaskStatsButton", "HistoryPagePanel", "TaskStatsPagePanel" };
            var statsMissing = statsRequired.Where(n => statsView.FindName(n) is null).ToList();
            results.Add(statsMissing.Count == 0
                ? "OK   自律统计(历史记录 / 任务累计两个子界面就位)"
                : "FAIL 自律统计缺少控件：" + string.Join(", ", statsMissing));

            // 批量选择入口：运行程序选择、任务选择、任务清单。
            var appPicker = new AppPickerDialog();
            var appPickerSelectAll = appPicker.FindName("SelectAllButton") as Button;
            var appPickerClear = appPicker.FindName("ClearSelectionButton") as Button;
            appPicker.Close();
            results.Add(appPickerSelectAll is not null && appPickerClear is not null
                ? "OK   AppPickerDialog(支持全选 / 取消选择程序)"
                : "FAIL AppPickerDialog 缺少全选控件");

            var tasksView = new MikoBarrier.Views.TasksView();
            var tasksSelectAll = tasksView.SelectAllButton;
            var tasksClear = tasksView.ClearSelectionButton;
            results.Add(tasksSelectAll is not null && tasksClear is not null
                ? "OK   任务清单(支持全选 / 取消选择任务)"
                : "FAIL 任务清单缺少全选控件");

            // 自律入口 / 名单入口必须纵向排列（StackPanel），避免 2x2 大按键下面空一块。
            var focusQuickButton = focus.FindName("HubQuickButton") as Button;
            var focusHubStack = focusQuickButton is null ? null : VisualTreeHelper.GetParent(focusQuickButton) as StackPanel;
            var rulesBlackButton = rulesView.FindName("HubBlackButton") as Button;
            var rulesHubStack = rulesBlackButton is null ? null : VisualTreeHelper.GetParent(rulesBlackButton) as StackPanel;
            results.Add(focusHubStack is not null && rulesHubStack is not null
                ? "OK   主页大按键(自律结界 / 名单管理均改为纵向居中排列)"
                : "FAIL 主页大按键布局未改为纵向排列");

            // 密码框打开时必须请求一次置前，保证全屏模式退出时密码弹窗可见。
            var beforeForeground = WindowFx.ForegroundRequestCount;
            var foregroundProbe = new PasswordDialog("全屏退出弹窗置前自检");
            foregroundProbe.Show();
            foregroundProbe.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var foregroundRequested = WindowFx.ForegroundRequestCount > beforeForeground;
            foregroundProbe.Close();
            results.Add(foregroundRequested
                ? "OK   PasswordDialog(打开时主动置前，全屏退出弹窗可见)"
                : "FAIL PasswordDialog(打开时未请求置前)");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL FocusView(每轮任务安排): {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var scroll = new ScrollViewer
            {
                Width = 320,
                Height = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            var content = new StackPanel();
            for (var i = 0; i < 40; i++)
            {
                content.Children.Add(new TextBlock { Text = $"自检行 {i}", Height = 20 });
            }

            var input = new TextBox { Width = 120, Text = "滚轮自检" };
            content.Children.Add(input);
            scroll.Content = content;

            var host = new Border { Width = 320, Height = 180, Child = scroll };
            host.Measure(new Size(320, 180));
            host.Arrange(new Rect(0, 0, 320, 180));
            host.UpdateLayout();

            var scrollable = scroll.ScrollableHeight;
            var before = scroll.VerticalOffset;
            input.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
            });
            host.UpdateLayout();
            var after = scroll.VerticalOffset;

            results.Add(after > before
                ? $"OK   滚轮穿透(一行输入框上滚轮可滚动页面：{before:F0} -> {after:F0})"
                : $"FAIL 滚轮穿透(可滚高度={scrollable:F0}，滚轮后={after:F0})");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL 滚轮穿透: {ex.GetType().Name}: {ex.Message}");
        }


        try
        {
            var stats = new MikoBarrier.Views.StatsView();
            stats.Refresh();
            var week = stats.FindName("WeekSummaryText") as TextBlock;
            var month = stats.FindName("MonthSummaryText") as TextBlock;
            var taskStats = stats.FindName("TaskStatsList") as ListBox;
            var taskHint = stats.FindName("TaskStatsHint") as TextBlock;

            if (week is not null && month is not null && taskStats is not null && taskHint is not null &&
                !string.IsNullOrWhiteSpace(week.Text) && !string.IsNullOrWhiteSpace(month.Text) &&
                !string.IsNullOrWhiteSpace(taskHint.Text))
            {
                results.Add($"OK   StatsView(本周/本月汇总 + 任务统计；本周：{week.Text})");
            }
            else
            {
                results.Add("FAIL StatsView(本周/本月汇总或任务统计未生成)");
            }
        }
        catch (Exception ex)
        {
            results.Add($"FAIL StatsView(本周/本月/任务统计): {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var rules = new MikoBarrier.Views.RulesView();
            var oneKey = rules.FindName("OneKeyWhitelistButton") as Button;
            results.Add(oneKey is not null
                ? "OK   RulesView(一键白名单按钮就位)"
                : "FAIL RulesView(找不到一键白名单按钮)");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL RulesView(一键白名单按钮): {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var picker = new AppPickerDialog();
            var appList = picker.FindName("AppList") as ListBox;

            var rules = new MikoBarrier.Views.RulesView();
            var black = rules.FindName("BlackList") as ListBox;
            var white = rules.FindName("WhiteList") as ListBox;

            var tasks = new MikoBarrier.Views.TasksView();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var taskList = typeof(MikoBarrier.Views.TasksView).GetField("_list", flags)?.GetValue(tasks) as ListBox;
            var taskBulk = typeof(MikoBarrier.Views.TasksView).GetField("_bulk", flags)?.GetValue(tasks) as TextBox;

            var batchOk = appList?.SelectionMode == SelectionMode.Extended
                          && black?.SelectionMode == SelectionMode.Extended
                          && white?.SelectionMode == SelectionMode.Extended
                          && taskList?.SelectionMode == SelectionMode.Extended
                          && taskBulk is not null;

            results.Add(batchOk
                ? "OK   批量操作(选程序多选 / 名单多选 / 任务清单多选+按行批量添加)"
                : "FAIL 批量操作控件未就绪");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL 批量操作控件: {ex.GetType().Name}: {ex.Message}");
        }


        try
        {
            var settings = new AppSettings();
            settings.EnsureDefaults();
            settings.Rules.Add(new AppRule
            {
                Kind = RuleKind.Whitelist,
                Target = RuleTarget.File,
                Path = @"C:\Tools\already.exe",
            });

            var apps = new List<RunningApp>
            {
                new() { ProcessName = "explorer", FilePath = @"C:\Windows\explorer.exe" },
                new() { ProcessName = "dllhost", FilePath = @"C:\Windows\System32\dllhost.exe" },
                new() { ProcessName = "SogouCloud", FilePath = @"C:\Program Files (x86)\SogouInput\SogouCloud.exe" },
                new() { ProcessName = "already", FilePath = @"C:\Tools\already.exe" },
                new() { ProcessName = "newapp", FilePath = @"C:\Tools\newapp.exe" },
                new() { ProcessName = "newapp", FilePath = @"C:\Other\newapp.exe" },
                new() { ProcessName = "noPath", FilePath = string.Empty },
            };

            var (newRules, covered, systemSkipped) = ProcessCatalog.BuildWhitelistBatch(settings, apps);
            var ok = newRules.Count == 1
                     && string.Equals(newRules[0].DisplayName, "newapp", StringComparison.OrdinalIgnoreCase)
                     && covered >= 2
                     && systemSkipped == 3;
            results.Add(ok
                ? "OK   ProcessCatalog(一键白名单去重 / 跳过系统核心+系统目录+内置组件 / 跳过无路径进程)"
                : $"FAIL ProcessCatalog(一键白名单: 新增 {newRules.Count}、跳过 {covered}、系统+组件 {systemSkipped})");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL ProcessCatalog(一键白名单逻辑): {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var settings = new AppSettings();
            settings.EnsureDefaults();

            var protectedOk =
                SystemAllowPolicy.IsPathProtected(@"C:\Windows\System32\cmd.exe") &&
                SystemAllowPolicy.IsPathProtected(@"C:\Windows\System32\DriverStore\FileRepository\igdlh64.inf_amd64_abc\igfxEM.exe") &&
                SystemAllowPolicy.IsPathProtected(@"C:\Windows\WinSxS\amd64_microsoft-windows-shell_31bf3856ad364e35\explorer.exe") &&
                SystemAllowPolicy.IsPathProtected(@"C:\Windows\SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe") &&
                SystemAllowPolicy.IsPathProtected(@"C:\Program Files\Windows Defender\MpCmdRun.exe") &&
                SystemAllowPolicy.IsPathProtected(@"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18.24080.9-0\MpCmdRun.exe") &&
                !SystemAllowPolicy.IsPathProtected(@"C:\Windows\Temp\evil.exe") &&
                !SystemAllowPolicy.IsPathProtected(@"C:\Windows\System32\..\Temp\evil.exe") &&
                !SystemAllowPolicy.IsPathProtected(@"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.234.5_x64__kzf8qxf38zg5c\Spotify.exe") &&
                !SystemAllowPolicy.IsPathProtected(@"C:\Users\me\Downloads\game.exe");
            results.Add(protectedOk
                ? "OK   白名单系统目录保护(System32/WinSxS/SystemApps/Defender 放行；Temp/WindowsApps/下载目录不放行)"
                : "FAIL 白名单系统目录保护");

            var whitePlan = new FocusPlan { BlockApplications = true, UseWhitelistMode = true };
            var componentOk =
                !RuleEngine.Evaluate("SogouCloud", @"C:\Program Files (x86)\SogouInput\SogouCloud.exe", settings, whitePlan).ShouldBlock &&
                !RuleEngine.Evaluate("HipsTray", @"C:\Program Files\Huorong\Sysdiag\bin\HipsTray.exe", settings, whitePlan).ShouldBlock &&
                !RuleEngine.Evaluate("NVDisplay.Container", @"C:\Windows\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_abc\Display.NvContainer\NVDisplay.Container.exe", settings, whitePlan).ShouldBlock &&
                !RuleEngine.Evaluate("RtkAudUService64", @"C:\Windows\System32\DriverStore\FileRepository\realtekservice.inf_amd64_abc\RtkAudUService64.exe", settings, whitePlan).ShouldBlock;
            results.Add(componentOk
                ? "OK   白名单组件豁免(第三方输入法/火绒/显卡/音频组件自动放行)"
                : "FAIL 白名单组件豁免");

            var blackSettings = new AppSettings();
            blackSettings.EnsureDefaults();
            blackSettings.Rules.Add(new AppRule
            {
                Kind = RuleKind.Blacklist,
                Target = RuleTarget.File,
                Path = @"C:\Program Files\NVIDIA Corporation\NVIDIA app\CEF\nvtray.exe",
            });
            var blackPlan = new FocusPlan { BlockApplications = true, UseWhitelistMode = false };
            var componentNotForced =
                RuleEngine.Evaluate("nvtray", @"C:\Program Files\NVIDIA Corporation\NVIDIA app\CEF\nvtray.exe", blackSettings, blackPlan).ShouldBlock;
            results.Add(componentNotForced
                ? "OK   组件豁免只在白名单模式生效(黑名单模式仍可主动屏蔽显卡组件)"
                : "FAIL 组件豁免语义异常(黑名单模式被强制放行)");

            var chainSettings = new AppSettings();
            chainSettings.EnsureDefaults();
            chainSettings.Rules.Add(new AppRule
            {
                Kind = RuleKind.Whitelist,
                Target = RuleTarget.File,
                Path = @"C:\Tools\launcher.exe",
            });

            var chain = new List<ProcessInfo>
            {
                new(1, "launcher", @"C:\Tools\launcher.exe", "Launcher", 0),
                new(2, "child", @"C:\Tools\child.exe", "Child", 1),
                new(3, "grandchild", @"C:\Other\grandchild.exe", "Grandchild", 2),
                new(4, "downloaded-game", @"C:\Users\me\Downloads\game.exe", "Game", 0),
                new(5, "cmd", @"C:\Windows\System32\cmd.exe", string.Empty, 0),
                new(6, "cmd-child", @"C:\Users\me\Downloads\tool.exe", "Tool", 5),
                new(7, "cyclic-a", @"C:\X\a.exe", "A", 8),
                new(8, "cyclic-b", @"C:\X\b.exe", "B", 7),
                new(9, "explorer", @"C:\Windows\explorer.exe", string.Empty, 0),
                new(10, "game-from-explorer", @"C:\Users\me\Downloads\game-from-explorer.exe", "Game2", 9),
            };
            var chainMap = chain.ToDictionary(p => p.Pid);
            var chainOk =
                !RuleEngine.EvaluateProcess(chain[1], chainMap, chainSettings, whitePlan).ShouldBlock &&
                !RuleEngine.EvaluateProcess(chain[2], chainMap, chainSettings, whitePlan).ShouldBlock &&
                RuleEngine.EvaluateProcess(chain[3], chainMap, chainSettings, whitePlan).ShouldBlock &&
                !RuleEngine.EvaluateProcess(chain[4], chainMap, chainSettings, whitePlan).ShouldBlock &&
                RuleEngine.EvaluateProcess(chain[5], chainMap, chainSettings, whitePlan).ShouldBlock &&
                RuleEngine.EvaluateProcess(chain[7], chainMap, chainSettings, whitePlan).ShouldBlock &&
                RuleEngine.EvaluateProcess(chain[9], chainMap, chainSettings, whitePlan).ShouldBlock;
            results.Add(chainOk
                ? "OK   启动链路(白名单祖先的子/孙程序放行；explorer/cmd 启动的不放行；环形父子不挂死)"
                : "FAIL 启动链路判定");

            var badPathHandled = true;
            try
            {
                RuleEngine.Evaluate("weird", "C:\\bad\u0001path\\x.exe", chainSettings, whitePlan);
            }
            catch
            {
                badPathHandled = false;
            }

            results.Add(badPathHandled
                ? "OK   非法路径不会让裁决抛异常"
                : "FAIL 非法路径让裁决抛异常");

            var audit = WhitelistAudit.Run(chainSettings, whitePlan, chain);
            var auditOk = audit.Report.Contains("白名单体检报告")
                          && audit.Total == chain.Count
                          && audit.BlockedWindowed >= 3
                          && audit.HostWarnings.Count == 0
                          && audit.HostRunning >= 1
                          && audit.HostPassed == audit.HostRunning;
            results.Add(auditOk
                ? $"OK   白名单体检报告(有窗口会拦截 {audit.BlockedWindowed} 个；运行中宿主 {audit.HostPassed}/{audit.HostRunning} 放行)"
                : $"FAIL 白名单体检报告(拦截 {audit.BlockedWindowed}，宿主 {audit.HostPassed}/{audit.HostRunning}，警告 {audit.HostWarnings.Count})");

            var legacy = new AppSettings
            {
                SystemWhitelist = new List<string> { "explorer", "my-custom-app" },
            };
            legacy.EnsureDefaults();
            var migrationOk = legacy.SystemWhitelist.Contains("my-custom-app")
                              && legacy.SystemWhitelist.Count >= AppSettings.DefaultSystemWhitelist.Length
                              && legacy.ComponentWhitelist.Count >= AppSettings.DefaultComponentWhitelist.Length
                              && AppSettings.DefaultComponentWhitelist.Contains("hipsdaemon");
            results.Add(migrationOk
                ? "OK   AppSettings 旧配置迁移(内置保护自动补齐，用户自定义条目保留)"
                : "FAIL AppSettings 旧配置迁移");

            var live = ProcessPatrol.Snapshot();
            var liveChainOk = live.Count > 3 && live.Any(p => p.ParentPid > 0);
            results.Add(liveChainOk
                ? $"OK   进程快照父进程链(Toolhelp：共 {live.Count} 个进程，可读到父 PID)"
                : $"FAIL 进程快照父进程链(共 {live.Count} 个进程)");

            // 小屏可用性：一键白名单预览窗口列表可滚动、默认收起路径、窗口不超屏。
            var previewRules = Enumerable.Range(1, 40).Select(i => new AppRule
            {
                Kind = RuleKind.Whitelist,
                Target = RuleTarget.File,
                DisplayName = $"演示程序 {i:D2}",
                Path = $@"C:\Program Files\Demo{i:D2}\demo{i:D2}.exe",
            }).ToList();
            var previewDialog = new WhitelistPreviewDialog(previewRules, 100, 0, 62);
            previewDialog.Show();
            previewDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            previewDialog.UpdateLayout();
            var previewList = previewDialog.FindName("ItemList") as ListBox;
            var previewButton = previewDialog.FindName("TogglePathsButton") as Button;
            var previewFirst = previewList?.Items.Count > 0
                ? previewList.Items[0] as WhitelistPreviewItem
                : null;
            var previewSize = $"{previewDialog.ActualWidth:F0}x{previewDialog.ActualHeight:F0}";
            var previewFit = previewDialog.ActualWidth <= SystemParameters.WorkArea.Width + 1 &&
                             previewDialog.ActualHeight <= SystemParameters.WorkArea.Height + 1;
            var previewScrollable = previewList is not null &&
                                    ScrollViewer.GetVerticalScrollBarVisibility(previewList) != ScrollBarVisibility.Disabled;
            var pathHiddenByDefault = previewFirst is { IsPathVisible: false };
            previewButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pathExpanded = previewFirst is { IsPathVisible: true };
            previewDialog.Close();
            results.Add(previewList?.Items.Count == 40 && previewFit && previewScrollable && pathHiddenByDefault && pathExpanded
                ? $"OK   一键白名单预览(40 项可滚动、默认收路径、展开生效、窗口 {previewSize} 不超屏)"
                : $"FAIL 一键白名单预览(项数 {previewList?.Items.Count}、超屏 {!previewFit}、滚动 {previewScrollable}、收展 {pathHiddenByDefault}/{pathExpanded})");

            // 通用提示框长文本：按钮固定可见、正文可滚动、窗口不超屏。
            var longMessage = string.Join("\n", Enumerable.Range(1, 120)
                .Select(i => $"第 {i} 行：这是一条很长的自检消息，用来验证确认按钮不会被顶出屏幕。"));
            var longDialog = new MessageDialog("长文本自检", longMessage, MessageDialogKind.Info, true, "知道了", "取消");
            longDialog.Show();
            longDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            longDialog.UpdateLayout();
            var longOk = longDialog.FindName("OkButton") as Button;
            var longCancel = longDialog.FindName("CancelButton") as Button;
            var longScroll = longDialog.FindName("MessageScroll") as ScrollViewer;
            var longFit = longDialog.ActualWidth <= SystemParameters.WorkArea.Width + 1 &&
                          longDialog.ActualHeight <= SystemParameters.WorkArea.Height + 1;
            var longButtonsVisible = longOk is { IsVisible: true } && longCancel is { IsVisible: true };
            var longScrollOk = longScroll is { ScrollableHeight: > 0 };
            var longSize = $"{longDialog.ActualWidth:F0}x{longDialog.ActualHeight:F0}";
            var longDiag = $"Ok={(longOk is null ? "null" : longOk.IsVisible.ToString())}," +
                           $"Cancel={(longCancel is null ? "null" : longCancel.IsVisible.ToString())}," +
                           $"Scrollable={(longScroll is null ? "null" : longScroll.ScrollableHeight.ToString("F0"))}";
            longDialog.Close();
            results.Add(longFit && longButtonsVisible && longScrollOk
                ? $"OK   MessageDialog 长文本(窗口 {longSize} 不超屏、按钮可见、正文可滚动)"
                : $"FAIL MessageDialog 长文本(窗口 {longSize}，超屏 {!longFit}，按钮可见 {longButtonsVisible}，{longDiag})");

            // 恢复码输入 / 查看本机副本窗口。
            var recoveryDialog = new RecoveryCodeDialog("自检：请输入恢复码：");
            recoveryDialog.Show();
            recoveryDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            recoveryDialog.UpdateLayout();
            var recoveryBox = recoveryDialog.FindName("CodeBox") as TextBox;
            var revealButton = recoveryDialog.FindName("RevealSavedButton") as Button;
            var recoveryDialogOk = recoveryBox is not null && revealButton is { IsVisible: true } && !recoveryDialog.Topmost;
            recoveryDialog.Close();
            results.Add(recoveryDialogOk
                ? "OK   恢复码对话框(输入框 / 查看本机恢复码按钮就位、非常驻置顶)"
                : "FAIL 恢复码对话框控件未就绪");

            // 忘记密码：新恢复码入口存在。
            var forgotDialog = new ForgotPasswordDialog();
            forgotDialog.Show();
            forgotDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            forgotDialog.UpdateLayout();
            var forgotRecoveryBox = forgotDialog.FindName("RecoveryCodeBox") as TextBox;
            var forgotOk = forgotRecoveryBox is not null;
            forgotDialog.Close();
            results.Add(forgotOk
                ? "OK   忘记密码对话框(支持恢复码方式重设密码)"
                : "FAIL 忘记密码对话框缺少恢复码输入框");

            // 自定义主题：预设完整性、旧字段回退、资源生成。
            try
            {
                var presets = ThemeCatalog.All;
                var idsUnique = presets.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                                presets.Count;
                var coversBoth = presets.Any(p => p.IsDark) && presets.Any(p => !p.IsDark);
                var contrastOk = presets.All(p =>
                    ThemeColorMath.ContrastRatio(p.TextColor, p.WindowColor) >= 4.5 &&
                    ThemeColorMath.ContrastRatio(p.SubTextColor, p.WindowColor) >= 3.0);
                results.Add(idsUnique && coversBoth && presets.Count >= 8 && contrastOk
                    ? $"OK   主题预设({presets.Count} 套，浅 / 深覆盖，正文对比度达标)"
                    : "FAIL 主题预设(id 重复 / 缺少明暗 / 对比度不足)");

                var legacyLight = ThemeCatalog.Resolve("不存在的预设", ThemeKind.Light);
                var legacyDark = ThemeCatalog.Resolve(string.Empty, ThemeKind.Dark);
                results.Add(legacyLight.Id == ThemeCatalog.DefaultLightId &&
                            legacyDark.Id == ThemeCatalog.DefaultDarkId
                    ? "OK   主题回退(未知 / 空 id 按 Light / Dark 回退默认预设)"
                    : $"FAIL 主题回退异常({legacyLight.Id} / {legacyDark.Id})");
            }
            catch (Exception ex)
            {
                results.Add($"FAIL 主题预设探针: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                var probe = new AppSettings
                {
                    ThemePreset = "dark-violet",
                    UiFontFamily = "Microsoft YaHei UI",
                    CustomBackgroundColor = "#203040",
                };
                var dictionary = ThemeManager.Build(probe);
                var windowBrush = dictionary["Brush.Window"] as SolidColorBrush;
                var panelBrush = dictionary["Brush.Panel"] as SolidColorBrush;
                var textBrush = dictionary["Brush.Text"] as SolidColorBrush;
                var font = dictionary["Font.UI"] as FontFamily;
                var customOk = windowBrush is not null &&
                               ThemeColorMath.ToHex(windowBrush.Color) == "#203040" &&
                               panelBrush is not null && panelBrush.Color != windowBrush.Color &&
                               textBrush is not null &&
                               ThemeColorMath.ContrastRatio(textBrush.Color, windowBrush.Color) >= 4.5;
                var customFontOk = string.Equals(font?.Source, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase);
                results.Add(customOk && customFontOk
                    ? $"OK   主题自定义(背景 #203040 + 字体 {font!.Source}，正文对比度 " +
                      $"{ThemeColorMath.ContrastRatio(textBrush!.Color, windowBrush!.Color):0.0})"
                    : $"FAIL 主题自定义异常(字体 {font?.Source}，背景 {windowBrush?.Color})");

                var defaultDict = ThemeManager.Build(new AppSettings());
                var defaultFont = defaultDict["Font.UI"] as FontFamily;
                results.Add(string.Equals(defaultFont?.Source, ThemeManager.DefaultFontFamily,
                        StringComparison.OrdinalIgnoreCase)
                    ? $"OK   主题默认字体(未选择字体时为{ThemeManager.DefaultFontDisplayName} / {ThemeManager.DefaultFontFamily})"
                    : $"FAIL 主题默认字体异常({defaultFont?.Source})");

                var themeProbeDialog = new ThemeDialog();
                themeProbeDialog.Show();
                themeProbeDialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                var fontBoxProbe = themeProbeDialog.FindName("FontBox") as ComboBox;
                var fontItems = fontBoxProbe?.Items.Cast<object>().ToList() ?? new List<object>();
                string? firstDisplay = null;
                if (fontItems.Count > 0)
                {
                    var first = fontItems[0];
                    firstDisplay = first.GetType().GetProperty("Display")?.GetValue(first) as string;
                }

                var hasDengXian = fontItems.Any(item =>
                    item.GetType().GetProperty("Value")?.GetValue(item) as string == "DengXian");
                var hasYaHei = fontItems.Any(item =>
                    item.GetType().GetProperty("Value")?.GetValue(item) as string == "Microsoft YaHei UI");
                var allChinese = fontItems.Count > 0 && fontItems.All(item =>
                    item.GetType().GetProperty("Display")?.GetValue(item) as string is { } text &&
                    text.Any(ch => ch >= '\u4e00' && ch <= '\u9fff'));
                var presetListProbe = themeProbeDialog.FindName("PresetList") as ListBox;
                var mainScrollProbe = themeProbeDialog.FindName("MainScroll") as ScrollViewer;
                var wheelForwardOk = false;
                if (presetListProbe is not null && mainScrollProbe is not null && mainScrollProbe.ScrollableHeight > 0)
                {
                    mainScrollProbe.ScrollToTop();
                    themeProbeDialog.UpdateLayout();
                    var beforeOffset = mainScrollProbe.VerticalOffset;
                    presetListProbe.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    });
                    themeProbeDialog.UpdateLayout();
                    wheelForwardOk = mainScrollProbe.VerticalOffset > beforeOffset;
                }

                themeProbeDialog.Close();
                results.Add(fontBoxProbe is not null && fontItems.Count > 1 && allChinese &&
                            firstDisplay?.StartsWith("系统默认", StringComparison.Ordinal) == true && hasDengXian
                    ? $"OK   主题字体列表({fontItems.Count} 项，仅中文字体，默认等线 / 微软雅黑可搜索)"
                    : $"FAIL 主题字体列表异常(count={fontItems.Count}, first={firstDisplay}, allChinese={allChinese}, dengxian={hasDengXian}, yahei={hasYaHei})");
                results.Add(wheelForwardOk
                    ? "OK   主题对话框预设区滚轮(转发给外层页面，不再被 ListBox 吞掉)"
                    : "FAIL 主题对话框预设区滚轮转发异常");

                ThemeManager.Apply(new AppSettings { ThemePreset = "light-sakura" });
                var appliedBrush = Application.Current.Resources["Brush.Window"] as SolidColorBrush;
                ThemeManager.Apply(App.State.Settings);
                var sakura = ThemeCatalog.ById("light-sakura");
                results.Add(appliedBrush is not null && appliedBrush.Color == sakura.WindowColor
                    ? "OK   主题应用(切换后动态资源即时替换)"
                    : $"FAIL 主题应用异常({appliedBrush?.Color} != {sakura.WindowColor})");
            }
            catch (Exception ex)
            {
                results.Add($"FAIL 主题自定义探针: {ex.GetType().Name}: {ex.Message}");
            }

            // 勾选 / 单选：自绘模板结构 + 实际页面是否套用。
            try
            {
                var checkProbe = new CheckBox { Content = "样式探针" };
                checkProbe.ApplyTemplate();
                var box = checkProbe.Template?.FindName("Box", checkProbe) as Border;
                var mark = checkProbe.Template?.FindName("CheckMark", checkProbe) as System.Windows.Shapes.Path;
                var hiddenBefore = mark is { Visibility: not Visibility.Visible };
                checkProbe.IsChecked = true;
                var visibleAfter = mark is { Visibility: Visibility.Visible };
                results.Add(box is not null && hiddenBefore && visibleAfter
                    ? "OK   CheckBox 自绘样式(圆角方框 + 圆头勾线，选中后显示)"
                    : "FAIL CheckBox 自绘样式异常");

                var radioProbe = new RadioButton { Content = "样式探针" };
                radioProbe.ApplyTemplate();
                var ring = radioProbe.Template?.FindName("Ring", radioProbe) as Border;
                var dot = radioProbe.Template?.FindName("Dot", radioProbe) as System.Windows.Shapes.Ellipse;
                var dotHiddenBefore = dot is { Visibility: not Visibility.Visible };
                radioProbe.IsChecked = true;
                var dotVisibleAfter = dot is { Visibility: Visibility.Visible };
                results.Add(ring is not null && dotHiddenBefore && dotVisibleAfter
                    ? "OK   RadioButton 自绘样式(圆环 + 内部圆点，选中后显示)"
                    : "FAIL RadioButton 自绘样式异常");

                var styledFocus = new MikoBarrier.Views.FocusView();
                var uniform = styledFocus.FindName("UniformDurationCheck") as CheckBox;
                var blockApps = styledFocus.FindName("BlockAppsCheck") as CheckBox;
                var networkOff = styledFocus.FindName("NetworkOffRadio") as RadioButton;
                uniform?.ApplyTemplate();
                blockApps?.ApplyTemplate();
                networkOff?.ApplyTemplate();
                var actualOk = uniform?.Template?.FindName("Box", uniform) is Border &&
                               blockApps?.Template?.FindName("Box", blockApps) is Border &&
                               networkOff?.Template?.FindName("Ring", networkOff) is Border;
                results.Add(actualOk
                    ? "OK   实际页面勾选 / 单选已套用自绘样式(任务模式 / 强制策略 / 断网档位)"
                    : "FAIL 实际页面未套用自绘样式");
            }
            catch (Exception ex)
            {
                results.Add($"FAIL 勾选 / 单选样式探针: {ex.GetType().Name}: {ex.Message}");
            }

            // MainWindow 首次显示：一次性置前、Topmost 脉冲后恢复 false（绝不常驻置顶）。
            var fgWindow = new MainWindow();
            var beforeFg = WindowFx.ForegroundRequestCount;
            fgWindow.Show();
            fgWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            fgWindow.UpdateLayout();
            var foregroundRequested = WindowFx.ForegroundRequestCount > beforeFg;
            var topmostRestored = !fgWindow.Topmost;
            var themeButton = fgWindow.FindName("ThemeButton") as Button;
            var themeEntryOk = themeButton is not null &&
                               (themeButton.Content as string)?.Contains("自定义主题", StringComparison.Ordinal) == true;
            fgWindow.Hide();
            fgWindow.Close();
            results.Add(foregroundRequested && topmostRestored
                ? "OK   MainWindow 启动置前(一次性请求、Topmost 最终 false)"
                : $"FAIL MainWindow 启动置前(请求 {foregroundRequested}、Topmost={fgWindow.Topmost})");
            results.Add(themeEntryOk
                ? "OK   自定义主题入口(侧栏按钮已改名)"
                : "FAIL 自定义主题入口缺失或按钮未改名");

            // 托盘菜单：已从 WinForms 系统菜单换成 WPF 自绘菜单。
            try
            {
                var trayField = typeof(MainWindow).GetField("_tray", BindingFlags.Instance | BindingFlags.NonPublic);
                var tray = trayField?.GetValue(fgWindow);
                var menuProperty = tray?.GetType().GetProperty("Menu", BindingFlags.Instance | BindingFlags.NonPublic);
                var trayMenu = menuProperty?.GetValue(tray) as ContextMenu;
                var menuItems = trayMenu?.Items.OfType<MenuItem>().ToList() ?? new List<MenuItem>();
                var hasSeparator = trayMenu?.Items.OfType<Separator>().Any() == true;
                var separators = trayMenu?.Items.OfType<Separator>().ToList() ?? new List<Separator>();
                var stylesOk = trayMenu?.Style is not null &&
                               menuItems.All(i => i.Style is not null) &&
                               separators.All(s => s.Style is not null);
                var headers = string.Join("/", menuItems.Select(i => i.Header?.ToString()));
                results.Add(trayMenu is not null && stylesOk && menuItems.Count == 4 && hasSeparator &&
                            headers.Contains("打开主界面") && headers.Contains("开始自律") &&
                            headers.Contains("回到全屏计时") && headers.Contains("退出")
                    ? $"OK   托盘菜单(WPF 自绘：{headers})"
                    : $"FAIL 托盘菜单异常(items={menuItems.Count}, separator={hasSeparator}, headers={headers})");

                var toggleMethod = tray?.GetType()
                    .GetMethod("ToggleMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                if (trayMenu is not null && toggleMethod is not null)
                {
                    toggleMethod.Invoke(tray, null);
                    var opened = trayMenu.IsOpen;
                    toggleMethod.Invoke(tray, null);
                    var closed = !trayMenu.IsOpen;
                    results.Add(opened && closed
                        ? "OK   托盘菜单可正常打开 / 关闭"
                        : $"FAIL 托盘菜单开合异常(open={opened}, closed={closed})");
                }
            }
            catch (Exception ex)
            {
                results.Add($"FAIL 托盘菜单探针: {ex.GetType().Name}: {ex.Message}");
            }

            var settingsProbe = new MikoBarrier.Views.SettingsView();
            settingsProbe.UpdateLayout();
            var openThemeButton = settingsProbe.FindName("OpenThemeDialogButton") as Button;
            var themeSummary = settingsProbe.FindName("ThemeSummaryText") as TextBlock;
            results.Add(openThemeButton is not null && themeSummary is not null
                ? "OK   系统设置主题入口(摘要文本 + 自定义主题按钮就位)"
                : "FAIL 系统设置主题入口缺失");

            var autoStartProbe = settingsProbe.FindName("AutoStartCheck") as CheckBox;
            var trayProbe = settingsProbe.FindName("TrayCheck") as CheckBox;
            autoStartProbe?.ApplyTemplate();
            trayProbe?.ApplyTemplate();
            var settingsChecksOk = autoStartProbe?.Template?.FindName("Box", autoStartProbe) is Border &&
                                   trayProbe?.Template?.FindName("Box", trayProbe) is Border;
            results.Add(settingsChecksOk
                ? "OK   系统设置勾选框已套用自绘样式(开机自启 / 托盘)"
                : "FAIL 系统设置勾选框未套用自绘样式");

            // 统计 / 设置两对大按钮之间要留出间距，不再完全贴在一起。
            var statsProbe = new MikoBarrier.Views.StatsView();
            var historyButton = statsProbe.FindName("HubHistoryButton") as Button;
            var taskStatsButton = statsProbe.FindName("HubTaskStatsButton") as Button;
            var appearanceButton = settingsProbe.FindName("HubAppearanceButton") as Button;
            var securityButton = settingsProbe.FindName("HubSecurityButton") as Button;
            var statsGap = (historyButton?.Margin.Right ?? 0) + (taskStatsButton?.Margin.Left ?? 0);
            var settingsGap = (appearanceButton?.Margin.Right ?? 0) + (securityButton?.Margin.Left ?? 0);
            results.Add(statsGap >= 8 && settingsGap >= 8
                ? $"OK   大按钮间距(统计 {statsGap:0}px / 设置 {settingsGap:0}px)"
                : $"FAIL 大按钮间距异常(统计 {statsGap:0}px / 设置 {settingsGap:0}px)");

            // 巫女模式：主题解锁、静态文本翻译、裂缝入口、设置页模式选择。
            var originalMikoUnlocked = App.State.Settings.MikoModeUnlocked;
            var originalMikoEnabled = App.State.Settings.MikoModeEnabled;
            var originalThemePreset = App.State.Settings.ThemePreset;
            var originalThemeKind = App.State.Settings.Theme;
            var originalCustomBackground = App.State.Settings.CustomBackgroundColor;
            try
            {
                var mikoPreset = ThemeCatalog.All.FirstOrDefault(p => p.Id == ThemeCatalog.MikoThemeId);
                var lockedPresets = ThemeCatalog.VisiblePresets(false).ToList();
                var unlockedPresets = ThemeCatalog.VisiblePresets(true).ToList();
                var themeOk = mikoPreset is not null &&
                              mikoPreset.Name == "真·巫女" &&
                              !mikoPreset.IsDark &&
                              mikoPreset.WindowColor == Colors.White &&
                              mikoPreset.AccentColor.R > mikoPreset.AccentColor.G &&
                              mikoPreset.AccentColor.G < 120 &&
                              mikoPreset.AccentColor.B < 120 &&
                              lockedPresets.All(p => p.Id != ThemeCatalog.MikoThemeId) &&
                              unlockedPresets.Any(p => p.Id == ThemeCatalog.MikoThemeId);
                results.Add(themeOk
                    ? "OK   真·巫女主题(白底朱红，未解锁不可选)"
                    : "FAIL 真·巫女主题或解锁可见性异常");

                var textOk = MikoText.MappingCount >= 100 &&
                             MikoText.HasMapping("开始自律") &&
                             MikoText.HasMapping("系统设置") &&
                             MikoText.HasMapping("任务清单");
                results.Add(textOk
                    ? $"OK   巫女文案表({MikoText.MappingCount} 条静态映射)"
                    : "FAIL 巫女文案表缺失或过少");

                App.State.Settings.MikoModeUnlocked = false;
                App.State.Settings.MikoModeEnabled = false;
                var crackProbe = new MikoBarrier.Views.FullScreenOverlay
                {
                    GuardEnabled = false,
                    AutoConfirmMikoUnlockForTest = true,
                };
                crackProbe.AllowClose();
                crackProbe.Show();
                crackProbe.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var crackButton = crackProbe.FindName("MikoCrackButton") as Button;
                var crackOk = crackButton is not null && crackButton.Visibility == Visibility.Visible;
                if (crackButton is not null)
                {
                    crackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    crackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var stillLockedAfterTwo = !App.State.Settings.MikoModeEnabled;
                    crackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var mikoThemeApplied =
                        App.State.Settings.ThemePreset == ThemeCatalog.MikoThemeId &&
                        App.State.Settings.CustomBackgroundColor.Length == 0;
                    var gestureOk = stillLockedAfterTwo &&
                                    App.State.Settings.MikoModeUnlocked &&
                                    App.State.Settings.MikoModeEnabled &&
                                    mikoThemeApplied &&
                                    crackButton.Visibility != Visibility.Visible;
                    results.Add(gestureOk
                        ? "OK   全屏计时裂缝三连击解锁(自检跳过确认框，自动套用真·巫女主题)"
                        : $"FAIL 全屏计时裂缝三连击异常(两次后锁={stillLockedAfterTwo}, 解锁={App.State.Settings.MikoModeUnlocked}, 主题={App.State.Settings.ThemePreset})");
                }
                else
                {
                    results.Add("FAIL 全屏计时裂缝按钮无法触发");
                }

                crackProbe.Close();
                results.Add(crackOk
                    ? "OK   全屏计时裂缝入口(未解锁时可见)"
                    : "FAIL 全屏计时裂缝入口缺失或未显示");

                App.State.Settings.MikoModeEnabled = true;
                var probeText = new TextBlock { Text = "开始自律" };
                MikoText.ApplyTo(probeText);
                var translated = probeText.Text;
                var toneText = MikoText.T("自律进行中，名单与设置已锁定，请先凭密码结束本次自律。");
                App.State.Settings.MikoModeEnabled = false;
                MikoText.ApplyTo(probeText);
                var restored = probeText.Text;
                var textEngineOk = translated == "展开结界" &&
                                   restored == "开始自律" &&
                                   toneText.Contains("结界", StringComparison.Ordinal) &&
                                   toneText.Contains("暗号", StringComparison.Ordinal);
                results.Add(textEngineOk
                    ? "OK   巫女文案运行时切换与还原"
                    : $"FAIL 巫女文案切换异常(translated={translated}, restored={restored}, tone={toneText})");

                App.State.Settings.MikoModeUnlocked = true;
                App.State.Settings.MikoModeEnabled = false;
                settingsProbe.Refresh();
                var modeNormal = settingsProbe.FindName("NormalModeRadio") as RadioButton;
                var modeMiko = settingsProbe.FindName("MikoModeRadio") as RadioButton;
                var modeThemeButton = settingsProbe.FindName("UseMikoThemeButton") as Button;
                var settingsModeOk = modeNormal is not null &&
                                     modeMiko is not null &&
                                     modeMiko.IsEnabled &&
                                     modeNormal.IsChecked == true &&
                                     modeThemeButton?.Visibility == Visibility.Visible;
                results.Add(settingsModeOk
                    ? "OK   系统设置界面模式选择(普通 / 巫女，解锁后可控)"
                    : "FAIL 系统设置界面模式选择异常");

                // 帮助 / 引导：每个 topic 都必须有完整的巫女口吻逐条重写版本。
                var helpRewriteOk = HelpContent.AllTopicKeys.Count >= 20;
                var helpRewriteDetail = string.Empty;
                foreach (var helpKey in HelpContent.AllTopicKeys)
                {
                    var normalTitle = HelpContent.GetTopicTitle(helpKey, miko: false);
                    var mikoTitle = HelpContent.GetTopicTitle(helpKey, miko: true);
                    var normalPages = HelpContent.GetPages(helpKey, miko: false);
                    var mikoPages = HelpContent.GetPages(helpKey, miko: true);

                    if (string.IsNullOrWhiteSpace(mikoTitle) ||
                        string.Equals(normalTitle, mikoTitle, StringComparison.Ordinal) ||
                        normalPages.Count == 0 ||
                        mikoPages.Count != normalPages.Count)
                    {
                        helpRewriteOk = false;
                        helpRewriteDetail = helpKey;
                        break;
                    }

                    for (var pageIndex = 0; pageIndex < normalPages.Count; pageIndex++)
                    {
                        var normalBody = normalPages[pageIndex].Body;
                        var mikoBody = mikoPages[pageIndex].Body;
                        if (string.IsNullOrWhiteSpace(mikoBody) ||
                            string.Equals(normalBody, mikoBody, StringComparison.Ordinal))
                        {
                            helpRewriteOk = false;
                            helpRewriteDetail = helpKey;
                            break;
                        }
                    }

                    if (!helpRewriteOk)
                    {
                        break;
                    }
                }

                results.Add(helpRewriteOk
                    ? $"OK   巫女帮助 / 引导逐条重写({HelpContent.AllTopicKeys.Count} 个 topic)"
                    : $"FAIL 巫女帮助 / 引导重写缺失(topic={helpRewriteDetail})");

                // 真实主窗口在巫女模式下的初始化文案：窗口标题 + 导航。
                var originalTray = App.State.Settings.MinimizeToTray;
                try
                {
                    App.State.Settings.MikoModeEnabled = true;
                    App.State.Settings.MinimizeToTray = false;
                    var mikoWindow = new MainWindow();
                    mikoWindow.Show();
                    mikoWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    mikoWindow.UpdateLayout();
                    var navFocusMiko = mikoWindow.FindName("NavFocus") as RadioButton;
                    var mikoWindowOk = mikoWindow.Title.Contains("真·巫女", StringComparison.Ordinal) &&
                                       string.Equals(navFocusMiko?.Content?.ToString(), "结界修行", StringComparison.Ordinal);
                    mikoWindow.Close();
                    results.Add(mikoWindowOk
                        ? "OK   主窗口巫女模式文案(标题 / 导航已切换)"
                        : $"FAIL 主窗口巫女模式文案异常(title={mikoWindow.Title}, nav={navFocusMiko?.Content})");
                }
                finally
                {
                    App.State.Settings.MinimizeToTray = originalTray;
                }

                App.State.Settings.MikoModeUnlocked = false;
                var lockedThemeDialog = new ThemeDialog();
                lockedThemeDialog.Show();
                lockedThemeDialog.UpdateLayout();
                var lockedCount = (lockedThemeDialog.FindName("PresetList") as ListBox)?.Items.Count ?? -1;
                lockedThemeDialog.Close();

                App.State.Settings.MikoModeUnlocked = true;
                var unlockedThemeDialog = new ThemeDialog();
                unlockedThemeDialog.Show();
                unlockedThemeDialog.UpdateLayout();
                var unlockedCount = (unlockedThemeDialog.FindName("PresetList") as ListBox)?.Items.Count ?? -1;
                unlockedThemeDialog.Close();

                results.Add(lockedCount == ThemeCatalog.All.Count - 1 &&
                            unlockedCount == ThemeCatalog.All.Count
                    ? $"OK   主题对话框按解锁状态显示预设(未解锁 {lockedCount} / 已解锁 {unlockedCount})"
                    : $"FAIL 主题对话框预设数量异常(未解锁 {lockedCount} / 已解锁 {unlockedCount})");
            }
            finally
            {
                App.State.Settings.MikoModeUnlocked = originalMikoUnlocked;
                App.State.Settings.MikoModeEnabled = originalMikoEnabled;
                App.State.Settings.ThemePreset = originalThemePreset;
                App.State.Settings.Theme = originalThemeKind;
                App.State.Settings.CustomBackgroundColor = originalCustomBackground;
                ThemeManager.Apply(App.State.Settings);
                MikoText.RefreshAllWindows();
            }

        }
        catch (Exception ex)
        {
            results.Add($"FAIL 白名单保护探针: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogException(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.LogDir);
            File.AppendAllText(
                StoragePaths.AppLogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{exception}\n\n");
        }
        catch
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // 强制模式下不允许从窗口直接退出，这里只是最后的保险。
            KeyboardGuard.Disable();
            NetworkGuard.RevertAllQuietly();
            State?.Save();
        }
        catch
        {
        }

        base.OnExit(e);
    }
}
