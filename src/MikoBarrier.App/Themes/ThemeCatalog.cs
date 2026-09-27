using System.Globalization;
using System.Windows.Media;
using MikoBarrier.Core.Models;

namespace MikoBarrier;

/// <summary>
/// 一套完整的界面配色。预设只定义固定搭配；用户还可以在预设基础上覆盖界面字体和窗口背景色。
/// </summary>
public sealed record ThemePreset(
    string Id,
    string Name,
    string Tagline,
    bool IsDark,
    Color WindowColor,
    Color PanelColor,
    Color ControlColor,
    Color TextColor,
    Color SubTextColor,
    Color BorderColor,
    Color AccentColor,
    Color AccentTextColor,
    Color DangerColor,
    Color SuccessColor);

/// <summary>主题预设目录。颜色全部手工搭配，保证正文对比度与整体观感。</summary>
public static class ThemeCatalog
{
    public const string DefaultLightId = "light-navy";
    public const string DefaultDarkId = "dark-ember";

    /// <summary>巫女模式解锁的「真·巫女」主题：白底 + 高饱和橙粉朱红。</summary>
    public const string MikoThemeId = "miko-true";

    public static IReadOnlyList<ThemePreset> All { get; } = new[]
    {
        new ThemePreset("light-navy", "藏青白昼", "藏青点缀", false,
            C(0xF2F5F9), C(0xFFFFFF), C(0xE9EEF6), C(0x15202E), C(0x5A6B80), C(0xD3DDEA),
            C(0x1F3A5F), C(0xFFFFFF), C(0xC0392B), C(0x1E7F4F)),

        new ThemePreset("dark-ember", "橙红黑夜", "橙红点缀", true,
            C(0x121317), C(0x1B1D22), C(0x262A31), C(0xF2F3F5), C(0x9AA1AC), C(0x2E333C),
            C(0xFF6B45), C(0x1B1D22), C(0xFF5C5C), C(0x4CC38A)),

        new ThemePreset("light-sakura", "樱粉", "柔和樱花", false,
            C(0xFFF5F7), C(0xFFFFFF), C(0xFBE7EC), C(0x2B1B22), C(0x8A6673), C(0xF0CDD6),
            C(0xC24470), C(0xFFFFFF), C(0xB5432F), C(0x1E7F4F)),

        new ThemePreset("light-matcha", "抹茶", "抹茶拿铁", false,
            C(0xF5F8F1), C(0xFFFFFF), C(0xE9F1E0), C(0x1F2A1B), C(0x64735C), C(0xD7E3CA),
            C(0x4F7A38), C(0xFFFFFF), C(0xB5432F), C(0x2E7D5B)),

        new ThemePreset("light-ocean", "海盐", "海盐清风", false,
            C(0xF0F6FA), C(0xFFFFFF), C(0xE3F0F7), C(0x122530), C(0x5B7383), C(0xCADEE9),
            C(0x1B7290), C(0xFFFFFF), C(0xC0392B), C(0x1E7F4F)),

        new ThemePreset("light-sand", "暖沙", "暖沙胡桃", false,
            C(0xFAF6F0), C(0xFFFFFF), C(0xF2E9DC), C(0x2E251A), C(0x7E6E58), C(0xE5D7C2),
            C(0x9A6528), C(0xFFFFFF), C(0xB5432F), C(0x2E7D5B)),

        new ThemePreset("dark-violet", "紫夜", "紫罗兰夜", true,
            C(0x141221), C(0x1D1A2E), C(0x292544), C(0xF0EDFA), C(0xA49DC4), C(0x332E50),
            C(0xA78BFA), C(0x1D1A2E), C(0xFF5C7A), C(0x4CC38A)),

        new ThemePreset("dark-forest", "森夜", "松林夜色", true,
            C(0x101613), C(0x18211C), C(0x24312A), C(0xECF4EF), C(0x94A99C), C(0x2D3B33),
            C(0x3DD68C), C(0x101613), C(0xFF6B6B), C(0x66D9A0)),

        new ThemePreset("dark-ocean", "深海", "深海蓝调", true,
            C(0x0F151B), C(0x17202A), C(0x22313F), C(0xEAF2F8), C(0x90A6B6), C(0x2B3C4B),
            C(0x38BDF8), C(0x0F151B), C(0xFF6B6B), C(0x34D399)),

        new ThemePreset("dark-graphite", "石墨", "石墨暖金", true,
            C(0x141416), C(0x1D1D20), C(0x28282C), C(0xF1F1F3), C(0x9E9EA6), C(0x313136),
            C(0xE5B567), C(0x141416), C(0xFF5C5C), C(0x4CC38A)),

        // 巫女模式专属：白底 + 高饱和、略带橙粉调的朱红。未解锁时不参与选择。
        new ThemePreset("miko-true", "真·巫女", "朱红结界", false,
            C(0xFFFFFF), C(0xFFF7F4), C(0xFBE3DD), C(0x351114), C(0x8A4B45), C(0xF0C3B9),
            C(0xE6301F), C(0xFFFFFF), C(0xC62828), C(0x2E7D5B)),
    };

    public static ThemePreset ById(string id) =>
        All.First(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static ThemePreset DefaultLight => ById(DefaultLightId);

    public static ThemePreset DefaultDark => ById(DefaultDarkId);

    /// <summary>巫女模式未解锁时，主题对话框只显示普通预设。</summary>
    public static IReadOnlyList<ThemePreset> VisiblePresets(bool mikoUnlocked) =>
        All.Where(p => mikoUnlocked || !string.Equals(p.Id, MikoThemeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static bool IsMikoTheme(string? presetId) =>
        !string.IsNullOrWhiteSpace(presetId) &&
        string.Equals(presetId.Trim(), MikoThemeId, StringComparison.OrdinalIgnoreCase);

    /// <summary>按保存的预设 id 解析；为空或旧字段迁移时按 Light / Dark 回退到默认预设。</summary>
    public static ThemePreset Resolve(string? presetId, ThemeKind legacy, bool mikoUnlocked = true)
    {
        if (!string.IsNullOrWhiteSpace(presetId))
        {
            var match = All.FirstOrDefault(p =>
                string.Equals(p.Id, presetId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null &&
                (mikoUnlocked || !string.Equals(match.Id, MikoThemeId, StringComparison.OrdinalIgnoreCase)))
            {
                return match;
            }
        }

        return legacy == ThemeKind.Light ? DefaultLight : DefaultDark;
    }

    private static Color C(uint rgb) =>
        Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

/// <summary>颜色解析 / 混色 / 对比度计算，供自定义背景色推导使用。</summary>
public static class ThemeColorMath
{
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = Color.FromRgb(r, g, b);
        return true;
    }

    public static bool IsValidHex(string? text) => TryParse(text, out _);

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static double RelativeLuminance(Color color)
    {
        var r = Linear(color.R / 255.0);
        var g = Linear(color.G / 255.0);
        var b = Linear(color.B / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>把 from 向 to 混合 amount（0~1），用于从背景色推导面板 / 控件 / 边框色。</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static double Linear(double value) =>
        value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
}
