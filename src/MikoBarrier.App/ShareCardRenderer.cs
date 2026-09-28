using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

namespace MikoBarrier;

/// <summary>
/// 战绩分享卡：把今日 / 累计统计画成一张竖版 PNG，方便用户发到聊天或社交平台。
/// 只读取本机统计，不联网、不上传、不改配置；图片写到数据根目录的 share\ 下。
/// </summary>
public static class ShareCardRenderer
{
    private const double Width = 900;
    private const double Height = 1200;
    private const double Margin = 56;
    private const double Gap = 20;

    /// <summary>生成今日战绩卡并返回 PNG 的完整路径。</summary>
    public static string SaveTodayCard(AppSettings settings, IReadOnlyList<SessionRecord> records, DateTime? now = null)
    {
        var localNow = now ?? DateTime.Now;
        var path = Path.Combine(StoragePaths.Root, "share");
        Directory.CreateDirectory(path);
        var file = Path.Combine(path, $"MikoBarrier-战绩卡-{localNow:yyyyMMdd-HHmmss}.png");
        Render(settings, records, localNow, file);
        return file;
    }

    /// <summary>只渲染不涉及主题之外的 UI：独立成方法，方便自检 / 单测。</summary>
    public static void Render(AppSettings settings, IReadOnlyList<SessionRecord> records, DateTime localNow, string file)
    {
        var preset = ThemeManager.Resolve(settings);
        var window = preset.WindowColor;
        var panel = preset.PanelColor;
        var text = preset.TextColor;
        var sub = preset.SubTextColor;
        var accent = preset.AccentColor;
        var accentText = preset.AccentTextColor;
        var border = preset.BorderColor;

        var today = records.Where(r => r.StartedUtc.ToLocalTime().Date == localNow.Date).ToList();
        var todayCompleted = today.Count(r => r.Completed);
        var todaySeconds = today.Sum(r => Math.Max(0, r.ActualFocusSeconds));
        var todayBlockedProcesses = today.Sum(r => Math.Max(0, r.BlockedProcessCount));
        var todayBlockedKeys = today.Sum(r => Math.Max(0, r.BlockedKeyCount));
        var todayAborted = today.Count(r => !r.Completed);

        var totalCompleted = records.Count(r => r.Completed);
        var totalSeconds = records.Sum(r => Math.Max(0, r.ActualFocusSeconds));
        var monthStart = new DateTime(localNow.Year, localNow.Month, 1);
        var monthCompleted = records.Count(r => r.Completed &&
            r.StartedUtc.ToLocalTime().Date >= monthStart && r.StartedUtc.ToLocalTime().Date <= localNow.Date);
        var streak = CalculateStreak(records, localNow.Date);

        var tasks = today
            .Where(r => !string.IsNullOrWhiteSpace(r.PlanSummary))
            .Select(r => r.PlanSummary.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

        var fontFamily = Application.Current?.TryFindResource("Font.UI") as FontFamily
                         ?? new FontFamily(ThemeManager.DefaultFontFamily);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var windowBrush = new SolidColorBrush(window);
            var panelBrush = new SolidColorBrush(panel);
            var textBrush = new SolidColorBrush(text);
            var subBrush = new SolidColorBrush(sub);
            var accentBrush = new SolidColorBrush(accent);
            var accentTextBrush = new SolidColorBrush(accentText);
            var borderPen = new Pen(new SolidColorBrush(border), 2);
            var softAccent = new SolidColorBrush(Blend(accent, window, 0.90));

            dc.DrawRectangle(windowBrush, null, new Rect(0, 0, Width, Height));
            dc.DrawRectangle(accentBrush, null, new Rect(0, 0, Width, 14));

            // 顶部：品牌 + 日期
            Draw(dc, "MikoBarrier · 巫女结界", 34, accentBrush, fontFamily, Margin, 62, bold: true);
            DrawRightAligned(dc, $"{localNow:yyyy-MM-dd} {WeekdayText(localNow)}", 24, subBrush, fontFamily, Width - Margin, 70);

            // 主卡：今日自律
            var hero = new Rect(Margin, 148, Width - Margin * 2, 296);
            dc.DrawRoundedRectangle(panelBrush, borderPen, hero, 24, 24);
            Draw(dc, "今日自律", 28, subBrush, fontFamily, hero.X + 44, hero.Y + 44);
            Draw(dc, FormatDuration(todaySeconds), 86, textBrush, fontFamily, hero.X + 40, hero.Y + 88, bold: true);
            Draw(dc, $"完成 {todayCompleted} 次", 30, accentBrush, fontFamily, hero.X + 44, hero.Y + 222, bold: true);

            DrawRightAligned(dc, "连续自律", 26, subBrush, fontFamily, hero.Right - 44, hero.Y + 48);
            DrawRightAligned(dc, streak > 0 ? $"{streak} 天" : "—", 60, accentBrush, fontFamily, hero.Right - 44, hero.Y + 92, bold: true);

            // 三张小卡：拦截程序 / 吞掉按键 / 中断
            var cardW = (Width - Margin * 2 - Gap * 2) / 3;
            DrawStatCard(dc, new Rect(Margin, 476, cardW, 168), "拦截程序", $"{todayBlockedProcesses} 次",
                panelBrush, borderPen, textBrush, subBrush, fontFamily);
            DrawStatCard(dc, new Rect(Margin + cardW + Gap, 476, cardW, 168), "吞掉按键", $"{todayBlockedKeys} 次",
                panelBrush, borderPen, textBrush, subBrush, fontFamily);
            DrawStatCard(dc, new Rect(Margin + (cardW + Gap) * 2, 476, cardW, 168), "中途中断", $"{todayAborted} 次",
                panelBrush, borderPen, textBrush, subBrush, fontFamily);

            // 累计卡
            var totalCard = new Rect(Margin, 676, Width - Margin * 2, 176);
            dc.DrawRoundedRectangle(new SolidColorBrush(Blend(accent, window, 0.94)), null, totalCard, 24, 24);
            Draw(dc, "累计成绩", 26, subBrush, fontFamily, totalCard.X + 40, totalCard.Y + 36);
            Draw(dc, $"{totalCompleted} 次 · {FormatDuration(totalSeconds)}", 44, textBrush, fontFamily,
                totalCard.X + 36, totalCard.Y + 74, bold: true);
            DrawRightAligned(dc, $"本月完成 {monthCompleted} 次", 24, subBrush, fontFamily,
                totalCard.Right - 40, totalCard.Y + 122);

            // 今日任务 / 引导
            var taskLine = tasks.Count == 0
                ? "今天还没有展开结界，随时可以开始。"
                : "今日任务：" + string.Join("；", tasks);
            DrawWrapped(dc, taskLine, 26, subBrush, fontFamily, Margin, 900, Width - Margin * 2);

            // 底部：巫女口吻 + 仓库地址
            var pill = new Rect(Margin, Height - 150, 300, 62);
            dc.DrawRoundedRectangle(accentBrush, null, pill, 31, 31);
            var motto = settings.MikoModeEnabled ? "巫女小姐盯着你哦" : "自律的一天，值得记录";
            DrawCentered(dc, motto, 26, accentTextBrush, fontFamily, pill);

            Draw(dc, "github.com/xgzshengming/MikoBarrier", 24, subBrush, fontFamily, Margin, Height - 68);
            DrawRightAligned(dc, "本机生成 · 无遥测 · Apache-2.0", 22, subBrush, fontFamily, Width - Margin, Height - 66);
        }

        var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    private static void DrawStatCard(DrawingContext dc, Rect rect, string label, string value,
        Brush panel, Pen border, Brush text, Brush sub, FontFamily font)
    {
        dc.DrawRoundedRectangle(panel, border, rect, 20, 20);
        Draw(dc, label, 24, sub, font, rect.X + 28, rect.Y + 34);
        Draw(dc, value, 42, text, font, rect.X + 24, rect.Y + 80, bold: true);
    }

    private static int CalculateStreak(IReadOnlyList<SessionRecord> records, DateTime today)
    {
        var days = records
            .Where(r => r.Completed)
            .Select(r => r.StartedUtc.ToLocalTime().Date)
            .ToHashSet();

        var cursor = today;
        if (!days.Contains(cursor))
        {
            cursor = cursor.AddDays(-1); // 今天还没开始自律，不算断签
        }

        var streak = 0;
        while (days.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    private static string WeekdayText(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日",
    };

    private static string FormatDuration(int seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalSeconds <= 0)
        {
            return "0 分钟";
        }

        if (span.TotalMinutes < 1)
        {
            return "不足 1 分钟";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)Math.Round(span.TotalMinutes)} 分钟";
        }

        return $"{(int)span.TotalHours} 小时 {span.Minutes} 分钟";
    }

    private static Color Blend(Color foreground, Color background, double backgroundWeight)
    {
        backgroundWeight = Math.Clamp(backgroundWeight, 0, 1);
        var foregroundWeight = 1 - backgroundWeight;
        return Color.FromRgb(
            (byte)Math.Round(foreground.R * foregroundWeight + background.R * backgroundWeight),
            (byte)Math.Round(foreground.G * foregroundWeight + background.G * backgroundWeight),
            (byte)Math.Round(foreground.B * foregroundWeight + background.B * backgroundWeight));
    }

    private static FormattedText Build(string content, double size, Brush brush, FontFamily font, bool bold) =>
        new(content, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(font, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size, brush, 1.0);

    private static void Draw(DrawingContext dc, string content, double size, Brush brush, FontFamily font,
        double x, double y, bool bold = false) =>
        dc.DrawText(Build(content, size, brush, font, bold), new Point(x, y));

    private static void DrawRightAligned(DrawingContext dc, string content, double size, Brush brush, FontFamily font,
        double right, double y, bool bold = false)
    {
        var formatted = Build(content, size, brush, font, bold);
        dc.DrawText(formatted, new Point(right - formatted.Width, y));
    }

    private static void DrawCentered(DrawingContext dc, string content, double size, Brush brush, FontFamily font, Rect rect)
    {
        var formatted = Build(content, size, brush, font, false);
        dc.DrawText(formatted, new Point(rect.X + (rect.Width - formatted.Width) / 2, rect.Y + (rect.Height - formatted.Height) / 2));
    }

    private static void DrawWrapped(DrawingContext dc, string content, double size, Brush brush, FontFamily font,
        double x, double y, double maxWidth)
    {
        var formatted = Build(content, size, brush, font, false);
        formatted.MaxTextWidth = maxWidth;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        formatted.MaxLineCount = 2;
        dc.DrawText(formatted, new Point(x, y));
    }
}
