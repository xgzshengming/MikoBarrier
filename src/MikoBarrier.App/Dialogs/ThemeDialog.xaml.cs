using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MikoBarrier.Controls;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Dialogs;

/// <summary>
/// 自定义主题：预设卡片 + 单独的界面字体 / 窗口背景色设置。
/// 所有改动立即应用到全局并保存，关闭窗口不需要再点确认。
/// </summary>
public partial class ThemeDialog : Window
{
    private static readonly Dictionary<string, string> DisplayNameOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft YaHei UI"] = "微软雅黑 UI",
        ["Microsoft YaHei"] = "微软雅黑",
        ["SimHei"] = "黑体",
        ["SimSun"] = "宋体",
        ["DengXian"] = "等线",
        ["SimKai"] = "楷体",
        ["FangSong"] = "仿宋",
        ["Microsoft JhengHei UI"] = "微软正黑体 UI",
    };

    private bool _loading;

    private sealed record FontChoice(string Display, string Value);

    public ThemeDialog()
    {
        InitializeComponent();

        _loading = true;
        BuildPresetCards();
        LoadFonts();
        SyncFromSettings();
        _loading = false;

        SourceInitialized += (_, _) =>
        {
            WindowFx.ApplyRoundedCorners(this);
            ClampToWorkArea();
        };
    }

    private void BuildPresetCards()
    {
        foreach (var preset in ThemeCatalog.VisiblePresets(App.State.Settings.MikoModeUnlocked))
        {
            PresetList.Items.Add(new ListBoxItem
            {
                Tag = preset.Id,
                Content = BuildPresetCard(preset),
            });
        }
    }

    private static Border BuildPresetCard(ThemePreset preset)
    {
        var strip = new Grid { Height = 30 };
        var colors = new[] { preset.WindowColor, preset.PanelColor, preset.ControlColor, preset.AccentColor };
        for (var i = 0; i < colors.Length; i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = new Border
            {
                Background = new SolidColorBrush(colors[i]),
                CornerRadius = i == 0
                    ? new CornerRadius(9, 0, 0, 0)
                    : i == colors.Length - 1
                        ? new CornerRadius(0, 9, 0, 0)
                        : new CornerRadius(0),
            };
            Grid.SetColumn(cell, i);
            strip.Children.Add(cell);
        }

        var content = new StackPanel();
        content.Children.Add(strip);
        content.Children.Add(new TextBlock
        {
            Text = preset.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(preset.TextColor),
            Margin = new Thickness(10, 7, 10, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = (preset.IsDark ? "深色" : "浅色") + " \u00B7 " + preset.Tagline,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(preset.SubTextColor),
            Margin = new Thickness(10, 2, 10, 0),
        });

        return new Border
        {
            Width = 184,
            Height = 92,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(preset.PanelColor),
            BorderBrush = new SolidColorBrush(preset.BorderColor),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = content,
        };
    }

    private void LoadFonts()
    {
        var items = new List<FontChoice>
        {
            new($"系统默认（{ThemeManager.DefaultFontDisplayName}）", string.Empty),
        };

        var preferredOrder = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimHei", "SimSun" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var families = new List<(string Value, string Display, int Rank)>();

        try
        {
            foreach (var family in Fonts.SystemFontFamilies)
            {
                var value = family.Source;
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                {
                    continue;
                }

                // 只保留有中文名、且显示名确实含汉字的字体（等线 / 雅黑 / 黑体 / 宋体等）。
                if (!TryGetChineseDisplayName(family, out var display) || !ContainsChinese(display))
                {
                    continue;
                }

                var rank = Array.FindIndex(preferredOrder,
                    p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));
                families.Add((value, display, rank < 0 ? int.MaxValue : rank));
            }
        }
        catch
        {
            // 字体枚举失败时至少保留“系统默认”。
        }

        foreach (var family in families
                     .OrderBy(f => f.Rank)
                     .ThenBy(f => f.Display, StringComparer.CurrentCulture))
        {
            items.Add(new FontChoice(family.Display, family.Value));
        }

        FontBox.ItemsSource = items;
        FontBox.DisplayMemberPath = nameof(FontChoice.Display);
        FontBox.SelectedValuePath = nameof(FontChoice.Value);
    }

    private static bool TryGetChineseDisplayName(FontFamily family, out string display)
    {
        if (DisplayNameOverrides.TryGetValue(family.Source, out var known))
        {
            display = known;
            return true;
        }

        // WPF 的 FamilyNames 键是 XmlLanguage；只认中文名，取不到就整项丢弃。
        foreach (var tag in new[] { "zh-CN", "zh-Hans", "zh-Hant" })
        {
            var match = family.FamilyNames.FirstOrDefault(kv =>
                string.Equals(kv.Key.IetfLanguageTag, tag, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                display = match.Value;
                return true;
            }
        }

        display = family.Source;
        return false;
    }

    private static bool ContainsChinese(string text) =>
        text.Any(ch => ch >= '\u4e00' && ch <= '\u9fff');

    private static string ResolveFontName(string? fontFamily) =>
        string.IsNullOrWhiteSpace(fontFamily) ? ThemeManager.DefaultFontFamily : fontFamily.Trim();

    private void SyncFromSettings()
    {
        _loading = true;
        var settings = App.State.Settings;
        var preset = ThemeManager.Resolve(settings);

        foreach (var item in PresetList.Items.OfType<ListBoxItem>())
        {
            item.IsSelected = string.Equals((string?)item.Tag, preset.Id, StringComparison.OrdinalIgnoreCase);
        }

        var fontName = settings.UiFontFamily?.Trim() ?? string.Empty;
        FontBox.SelectedValue = fontName;
        if (FontBox.SelectedIndex < 0)
        {
            FontBox.SelectedIndex = 0;
        }

        FontPreviewText.FontFamily = new FontFamily(ResolveFontName(fontName));
        UpdateBackgroundUi();
        _loading = false;
    }

    private void UpdateBackgroundUi()
    {
        var settings = App.State.Settings;
        var hasCustom = ThemeColorMath.TryParse(settings.CustomBackgroundColor, out var custom);
        var effective = hasCustom ? custom : ThemeManager.Resolve(settings).WindowColor;

        BackgroundSwatch.Background = new SolidColorBrush(effective);
        BackgroundHexBox.Text = ThemeColorMath.ToHex(effective);
        ClearBackgroundButton.IsEnabled = hasCustom;
    }

    private void ApplyAndSave()
    {
        ThemeManager.Apply(App.State.Settings);
        App.State.Save();
    }

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetList.SelectedItem is not ListBoxItem { Tag: string id })
        {
            return;
        }

        var preset = ThemeCatalog.ById(id);
        var settings = App.State.Settings;
        settings.ThemePreset = preset.Id;
        settings.Theme = preset.IsDark ? ThemeKind.Dark : ThemeKind.Light;
        settings.CustomBackgroundColor = string.Empty;

        ApplyAndSave();
        UpdateBackgroundUi();
    }

    private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var name = FontBox.SelectedItem is FontChoice choice ? choice.Value : string.Empty;
        App.State.Settings.UiFontFamily = name;
        FontPreviewText.FontFamily = new FontFamily(ResolveFontName(name));
        ApplyAndSave();
    }

    // 预设卡片是 ListBox，会吞掉滚轮；字体下拉框在关闭状态下也会被滚轮误改选项。
    // 这里统一把滚轮转给对话框自己的滚动容器。
    private void PresetList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        ForwardWheelToMainScroll(e);

    private void FontBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FontBox.IsDropDownOpen)
        {
            return;
        }

        ForwardWheelToMainScroll(e);
    }

    private void ForwardWheelToMainScroll(MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0)
        {
            return;
        }

        MainScroll.ScrollToVerticalOffset(MainScroll.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        var current = ThemeColorMath.TryParse(settings.CustomBackgroundColor, out var custom)
            ? custom
            : ThemeManager.Resolve(settings).WindowColor;

        var dialog = new ColorPickerDialog(current) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            settings.CustomBackgroundColor = ThemeColorMath.ToHex(dialog.SelectedColor);
            ApplyAndSave();
            UpdateBackgroundUi();
        }
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        App.State.Settings.CustomBackgroundColor = string.Empty;
        ApplyAndSave();
        UpdateBackgroundUi();
    }

    private void BackgroundHexBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ApplyBackgroundText();
    }

    private void BackgroundHexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyBackgroundText();
        e.Handled = true;
    }

    private void ApplyBackgroundText()
    {
        if (ThemeColorMath.TryParse(BackgroundHexBox.Text, out var color))
        {
            App.State.Settings.CustomBackgroundColor = ThemeColorMath.ToHex(color);
            ApplyAndSave();
        }

        UpdateBackgroundUi();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        settings.Theme = ThemeKind.Dark;
        settings.ThemePreset = string.Empty;
        settings.UiFontFamily = string.Empty;
        settings.CustomBackgroundColor = string.Empty;

        ApplyAndSave();
        SyncFromSettings();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>按窗口所在屏幕的工作区限制尺寸，避免小屏幕上按钮被顶出屏幕。</summary>
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

        MaxWidth = Math.Max(560, workWidth - 24);
        MaxHeight = Math.Max(420, workHeight - 40);
        if (Width > MaxWidth)
        {
            Width = MaxWidth;
        }

        if (Height > MaxHeight)
        {
            Height = MaxHeight;
        }
    }
}
