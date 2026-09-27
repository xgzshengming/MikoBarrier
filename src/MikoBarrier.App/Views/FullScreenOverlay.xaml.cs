
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using System.Windows.Threading;


using MikoBarrier.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;
using MikoBarrier.Dialogs;

namespace MikoBarrier.Views;

/// <summary>
/// 全屏置顶遮罩：
///  1) 盖住整个屏幕（含任务栏），别人点不到下面的程序；
///  2) 每 500ms 检查一次，如果被 Alt+Tab / 点其他地方切走了，就把焦点抢回来（这是键盘封锁失效时的兜底）；
///  3) 只保留一个"提前结束（需密码）"按钮作为逃生通道。
/// </summary>
public partial class FullScreenOverlay : Window
{
    private readonly DispatcherTimer _guardTimer;
    private bool _allowClose;
    private readonly DispatcherTimer _safetyTimer;
    private DispatcherTimer? _saveTimer;
    private AppSettings? _ambienceSettings;
    private bool _initializingAmbience = true;
    private string? _appliedBackgroundId;
    private bool _panelCollapsed;
    private int _mikoCrackClicks;

    /// <summary>自检用：只读观察裂缝是否显示，不触发解锁流程。</summary>
    internal Visibility MikoCrackVisibility => MikoCrackButton.Visibility;

    /// <summary>自检用：第三次点击后跳过确认框和落盘，直接验证解锁动作。</summary>
    internal bool AutoConfirmMikoUnlockForTest { get; set; }

    public FullScreenOverlay()
    {
        InitializeComponent();
        _initializingAmbience = false;

        _guardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _guardTimer.Tick += (_, _) => EnsureAlive();

        // 永不休眠的安全定时器：会话一结束就强制解除遮罩（不依赖任何其它逻辑）。
        _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _safetyTimer.Tick += (_, _) => EnsureAlive();

        Loaded += (_, _) =>
        {
            _guardTimer.Start();
            _safetyTimer.Start();
            ApplyScrimColor();
            LoadLauncher();
            RefreshMikoCrackVisibility();
        };
        Closed += (_, _) =>
        {
            _guardTimer.Stop();
            _safetyTimer.Stop();
            _saveTimer?.Stop();
            FlushPendingSettings();
        };
        SourceInitialized += (_, _) => WindowFx.ApplyRoundedCorners(this);

        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Log("遮罩被尝试关闭（已拦截）");
            }
        };
    }

    /// <summary>是否启用"抢焦点"（自检时可以关掉，避免干扰）。</summary>
    public bool GuardEnabled { get; set; } = true;

    /// <summary>主界面注入：会话是否还在进行。返回 false 时遮罩立刻自行解除（安全底线）。</summary>
    public Func<bool>? SessionActiveProvider { get; set; }

    /// <summary>立刻解除一切限制并关闭遮罩。</summary>
    public void ReleaseAndClose()
    {
        try
        {
            _guardTimer.Stop();
            GuardEnabled = false;
            _allowClose = true;
            AmbiencePanel.Visibility = Visibility.Collapsed;
            Log("遮罩已解除");

            if (IsVisible)
            {
                Hide();
            }

            Close();
        }
        catch
        {
        }
    }

    public event Action? AbortRequested;

    public void AllowClose() => _allowClose = true;

    /// <summary>主界面在会话开始时调用：初始化全屏背景控件。</summary>
    public void InitializeBackground(AppSettings settings)
    {
        _ambienceSettings = settings;
        _initializingAmbience = true;
        try
        {
            BuildBackgroundItems();
            SelectByTag(BackgroundBox, settings.FullScreenBackgroundId);
            ApplyBackground(settings.FullScreenBackgroundId);
            ApplyScrimColor();
            SetPanelCollapsed(settings.FullScreenPanelCollapsed);
            RefreshMikoCrackVisibility();
        }
        finally
        {
            _initializingAmbience = false;
        }
    }

    private void BuildBackgroundItems()
    {
        if (BackgroundBox.Items.Count > 0)
        {
            return;
        }

        BackgroundBox.Items.Add(new ComboBoxItem { Content = "无背景", Tag = AmbienceCatalog.NoneId });
        foreach (var option in AmbienceCatalog.Backgrounds)
        {
            BackgroundBox.Items.Add(new ComboBoxItem { Content = option.DisplayName, Tag = option.Id });
        }
    }

    private static void SelectByTag(ComboBox box, string? id)
    {
        var target = id ?? AmbienceCatalog.NoneId;
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, target, StringComparison.Ordinal))
            {
                box.SelectedItem = item;
                return;
            }
        }

        if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private static string SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string ?? AmbienceCatalog.NoneId;

    private void BackgroundBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingAmbience)
        {
            return;
        }

        var id = AmbienceCatalog.ResolveBackgroundId(SelectedTag(BackgroundBox));
        if (_ambienceSettings is not null)
        {
            _ambienceSettings.FullScreenBackgroundId = id;
        }

        ApplyBackground(id);
        ScheduleSettingsSave();
    }

    private void ScheduleSettingsSave()
    {
        if (_ambienceSettings is null || !ReferenceEquals(_ambienceSettings, App.State.Settings))
        {
            return;
        }

        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Stop();
        _saveTimer.Tick -= SaveTimer_Tick;
        _saveTimer.Tick += SaveTimer_Tick;
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        FlushPendingSettings();
    }

    private void FlushPendingSettings()
    {
        try
        {
            if (_ambienceSettings is not null &&
                ReferenceEquals(_ambienceSettings, App.State.Settings) &&
                !App.ForceExit &&
                !App.IsSessionEnding)
            {
                App.State.Save();
            }
        }
        catch
        {
            // 氛围设置保存失败不影响自律主流程；下次改选会再次尝试。
        }
    }

    private void ApplyBackground(string? id)
    {
        var resolved = AmbienceCatalog.ResolveBackgroundId(id);
        if (string.Equals(_appliedBackgroundId, resolved, StringComparison.Ordinal))
        {
            return;
        }

        _appliedBackgroundId = resolved;
        BackgroundImage.Source = null;
        BackgroundPreview.Source = null;

        if (string.IsNullOrEmpty(resolved) ||
            !AmbienceCatalog.TryGetBackground(resolved, out var option))
        {
            BackgroundImage.Visibility = Visibility.Collapsed;
            BackgroundScrim.Visibility = Visibility.Collapsed;
            BackgroundNoneText.Visibility = Visibility.Visible;
            ApplyPanelBackdrop(hasBackground: false);
            return;
        }

        BackgroundImage.Source = LoadBitmap(option.ResourcePath);
        BackgroundPreview.Source = LoadBitmap(option.ThumbnailPath);
        var hasImage = BackgroundImage.Source is not null;
        BackgroundImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        BackgroundScrim.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        BackgroundNoneText.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        ApplyPanelBackdrop(hasImage);
    }

    /// <summary>有背景图时把主卡片调成半透明主题色，让照片透出来；无背景时恢复不透明卡片。</summary>
    private void ApplyPanelBackdrop(bool hasBackground)
    {
        var panel = TryFindResource("Brush.Panel") as SolidColorBrush;
        if (panel is null)
        {
            return;
        }

        var alpha = hasBackground ? (byte)0xC8 : (byte)0xFF;
        var color = panel.Color;
        ContentPanel.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private void ApplyScrimColor()
    {
        var text = TryFindResource("Brush.Text") as SolidColorBrush;
        var luminance = text is null
            ? 1.0
            : (0.299 * text.Color.R + 0.587 * text.Color.G + 0.114 * text.Color.B) / 255.0;
        var color = luminance > 0.55
            ? Color.FromArgb(0x40, 0, 0, 0)
            : Color.FromArgb(0x40, 255, 255, 255);
        BackgroundScrim.Background = new SolidColorBrush(color);
    }

    private static BitmapImage? LoadBitmap(string resourcePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(resourcePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void SetPanelCollapsed(bool collapsed)
    {
        _panelCollapsed = collapsed;
        ContentPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseCardButton.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CompactBar.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;

        if (!_initializingAmbience && _ambienceSettings is not null)
        {
            _ambienceSettings.FullScreenPanelCollapsed = collapsed;
            ScheduleSettingsSave();
        }
    }

    private void CollapseCardButton_Click(object sender, RoutedEventArgs e) => SetPanelCollapsed(true);

    private void ExpandCardButton_Click(object sender, RoutedEventArgs e) => SetPanelCollapsed(false);

    private void AmbienceButton_Click(object sender, RoutedEventArgs e)
    {
        AmbiencePanel.Visibility = AmbiencePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void AmbienceClose_Click(object sender, RoutedEventArgs e)
    {
        AmbiencePanel.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------ 巫女模式解锁

    private void RefreshMikoCrackVisibility()
    {
        MikoCrackButton.Visibility = App.State.Settings.MikoModeUnlocked
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void MikoCrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.State.Settings.MikoModeUnlocked)
        {
            RefreshMikoCrackVisibility();
            return;
        }

        _mikoCrackClicks++;

        if (_mikoCrackClicks < 3)
        {
            Log($"巫女模式裂缝被轻触（{_mikoCrackClicks}/3）");
            return;
        }

        _mikoCrackClicks = 0;

        if (AutoConfirmMikoUnlockForTest)
        {
            ApplyMikoUnlock(persist: false, showInfo: false);
            return;
        }

        if (!AppMessage.Confirm(
                this,
                "你引起了巫女小姐的注意！是否进入真·巫女结界？",
                "MikoBarrier",
                okText: "是",
                cancelText: "否"))
        {
            Log("巫女模式解锁被拒绝，裂缝继续保留");
            return;
        }

        ApplyMikoUnlock(persist: true, showInfo: true);
    }

    private void ApplyMikoUnlock(bool persist, bool showInfo)
    {
        MikoModeService.Unlock(App.State.Settings);
        ThemeManager.Apply(App.State.Settings);
        if (persist)
        {
            App.State.Save();
        }

        MikoCrackButton.Visibility = Visibility.Collapsed;
        MikoText.RefreshAllWindows();
        Log("已进入真·巫女结界");

        if (showInfo)
        {
            AppMessage.Info(
                this,
                "真·巫女结界已展开。\n\n可以在「系统设置 → 外观与启动」里随时切回普通模式；" +
                "主题「真·巫女」也已解锁。");
        }
    }


    public static void Log(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(StoragePaths.LogDir);
            System.IO.File.AppendAllText(StoragePaths.AppLogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 全屏计时：{message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public void SuspendGuard() => _guardTimer.Stop();

    public void ResumeGuard()
    {
        if (!_guardTimer.IsEnabled)
        {
            _guardTimer.Start();
        }
    }

    public void UpdateState(SessionEngine engine, AppSettings settings, string guardText)
    {
        var remaining = Format(engine.Remaining);
        CountdownText.Text = remaining;
        CompactCountdownText.Text = remaining;

        MikoText.SetText(PhaseText, engine.PhaseText);
        MikoText.SetText(CompactPhaseText, engine.PhaseText);
        MikoText.SetText(PlanText, engine.Plan?.ToString() ?? string.Empty);
        Progress.Value = engine.OverallProgress * 100;
        MikoText.SetText(GuardText, guardText);
    }

    /// <summary>安全巡检：只做两件事会话结束就自毁、必要时重新置顶。绝不抢焦点。</summary>
    private void EnsureAlive()
    {
        if (!GuardEnabled || !IsVisible)
        {
            if (SessionActiveProvider is not null && !SessionActiveProvider() && IsVisible)
            {
                ReleaseAndClose();
            }

            return;
        }

        if (SessionActiveProvider is not null && !SessionActiveProvider())
        {
            Log("检测到会话已结束，遮罩自动解除");
            ReleaseAndClose();
            return;
        }

        // 不再置顶：任何程序（含任务管理器）都可以盖在遮罩之上，绝不遮挡用户。
    }

    private DispatcherTimer? _graceTimer;

    /// <summary>"启动台"：列出桌面快捷方式，点一下就能打开；遮罩暂时收起 2 分钟后自动回来。</summary>
    private void LoadLauncher()
    {
        LauncherPanel.Children.Clear();

        var files = new List<string>();
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                 })
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(folder) && System.IO.Directory.Exists(folder))
                {
                    files.AddRange(System.IO.Directory.GetFiles(folder, "*.lnk"));
                    files.AddRange(System.IO.Directory.GetFiles(folder, "*.url"));
                }
            }
            catch
            {
            }
        }

        foreach (var file in files.Distinct().Take(14))
        {
            var button = new Button
            {
                Content = System.IO.Path.GetFileNameWithoutExtension(file),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 7, 14, 7),
            };

            var path = file;
            button.Click += (_, _) => Launch(path);
            LauncherPanel.Children.Add(button);
        }

        if (LauncherPanel.Children.Count == 0)
        {
            LauncherPanel.Children.Add(new TextBlock
            {
                Text = "桌面上没有找到快捷方式，可以用下面的按钮浏览。",
                Style = (Style)FindResource("Muted"),
            });
        }
    }

    private void Launch(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            Log("启动台打开：" + path);
        }
        catch (Exception ex)
        {
            Log("启动台打开失败：" + ex.Message);
            return;
        }

        HideForGrace();
    }

    /// <summary>暂时收起遮罩（不解除自律），2 分钟后自动回来。</summary>
    private void HideForGrace()
    {
        _guardTimer.Stop();
        GuardEnabled = false;
        AmbiencePanel.Visibility = Visibility.Collapsed;
        Hide();
        Log("遮罩暂时收起 2 分钟");

        _graceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _graceTimer.Stop();
        _graceTimer.Tick -= GraceTimer_Tick;
        _graceTimer.Tick += GraceTimer_Tick;
        _graceTimer.Start();
    }

    private void GraceTimer_Tick(object? sender, EventArgs e)
    {
        _graceTimer?.Stop();

        if (SessionActiveProvider is not null && !SessionActiveProvider())
        {
            ReleaseAndClose();
            return;
        }

        Show();
        GuardEnabled = true;
        _guardTimer.Start();
        Log("遮罩已自动回来");
    }

    private void BrowseProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要打开的程序",
            Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            Launch(dialog.FileName);
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择要打开的文件夹" };

        if (dialog.ShowDialog(this) == true)
        {
            Launch(dialog.FolderName);
        }
    }

    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        Keyboard.ClearFocus();
        _guardTimer.Stop();
        _allowClose = true;
        AmbiencePanel.Visibility = Visibility.Collapsed;
        Hide();
        AbortRequested?.Invoke();
    }

    private static string Format(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
        : $"{span.Minutes:00}:{span.Seconds:00}";
}