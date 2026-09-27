using System.Windows;
using System.Windows.Media;
using MikoBarrier.Core.Models;

namespace MikoBarrier;

/// <summary>
/// 主题应用：根据设置生成完整的资源字典（10 个颜色刷 + 界面字体），
/// 再替换 Application.Resources.MergedDictionaries[0]，其余 Shared.xaml 资源保持不动。
/// </summary>
public static class ThemeManager
{
    /// <summary>默认界面字体：等线（DengXian）。Windows 10/11 自带，中文界面清爽细腻。</summary>
    public const string DefaultFontFamily = "DengXian";

    /// <summary>默认字体的中文显示名。</summary>
    public const string DefaultFontDisplayName = "等线";

    private static readonly Color InkDark = Color.FromRgb(0x10, 0x12, 0x16);
    private static readonly Color InkLight = Color.FromRgb(0xF7, 0xF8, 0xFA);

    public static ThemePreset Resolve(AppSettings settings) =>
        ThemeCatalog.Resolve(settings.ThemePreset, settings.Theme, settings.MikoModeUnlocked);

    /// <summary>给设置页 / 主题对话框用的一句话摘要。</summary>
    public static string Describe(AppSettings settings)
    {
        var preset = Resolve(settings);
        var font = string.IsNullOrWhiteSpace(settings.UiFontFamily)
            ? $"默认{DefaultFontDisplayName}"
            : settings.UiFontFamily.Trim();
        var background = ThemeColorMath.IsValidHex(settings.CustomBackgroundColor) ? "自定义背景" : "预设背景";
        return $"{preset.Name} \u00B7 {font} \u00B7 {background}";
    }

    public static void Apply(AppSettings settings)
    {
        var dictionary = Build(settings);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(dictionary);
        }
        else
        {
            merged[0] = dictionary;
        }
    }

    /// <summary>生成主题资源字典；独立成方法便于自检，不会改动当前界面。</summary>
    public static ResourceDictionary Build(AppSettings settings)
    {
        var preset = Resolve(settings);

        var hasCustomBackground = ThemeColorMath.TryParse(settings.CustomBackgroundColor, out var customWindow);
        var window = hasCustomBackground ? customWindow : preset.WindowColor;

        // 自定义背景时：正文颜色按对比度自动选深色 / 浅色，再推导面板、控件、边框。
        var useLightText = ThemeColorMath.ContrastRatio(window, InkLight) >
                           ThemeColorMath.ContrastRatio(window, InkDark);
        var text = hasCustomBackground ? (useLightText ? InkLight : InkDark) : preset.TextColor;
        var subText = hasCustomBackground
            ? ThemeColorMath.Mix(text, window, 0.42)
            : preset.SubTextColor;

        Color panel;
        Color control;
        Color border;
        if (!hasCustomBackground)
        {
            panel = preset.PanelColor;
            control = preset.ControlColor;
            border = preset.BorderColor;
        }
        else if (useLightText)
        {
            // 深色背景：面板 / 控件 / 边框依次向白色提亮。
            panel = ThemeColorMath.Mix(window, Colors.White, 0.06);
            control = ThemeColorMath.Mix(window, Colors.White, 0.13);
            border = ThemeColorMath.Mix(window, Colors.White, 0.20);
        }
        else
        {
            // 浅色背景：面板向白色提亮，控件 / 边框向黑色压深。
            panel = ThemeColorMath.Mix(window, Colors.White, 0.55);
            control = ThemeColorMath.Mix(window, Colors.Black, 0.05);
            border = ThemeColorMath.Mix(window, Colors.Black, 0.14);
        }

        return new ResourceDictionary
        {
            ["Brush.Window"] = Brush(window),
            ["Brush.Panel"] = Brush(panel),
            ["Brush.Control"] = Brush(control),
            ["Brush.Text"] = Brush(text),
            ["Brush.SubText"] = Brush(subText),
            ["Brush.Border"] = Brush(border),
            ["Brush.Accent"] = Brush(preset.AccentColor),
            ["Brush.AccentText"] = Brush(preset.AccentTextColor),
            ["Brush.Danger"] = Brush(preset.DangerColor),
            ["Brush.Success"] = Brush(preset.SuccessColor),
            ["Font.UI"] = new FontFamily(
                string.IsNullOrWhiteSpace(settings.UiFontFamily) ? DefaultFontFamily : settings.UiFontFamily.Trim()),
        };
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
