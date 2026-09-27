namespace MikoBarrier;

/// <summary>全屏计时的背景图（资源随程序一起发布，全部为免费可商用素材）。</summary>
public sealed record BackgroundOption(
    string Id,
    string DisplayName,
    string ResourcePath,
    string ThumbnailPath,
    string SourceNote,
    string LicenseName,
    string LicenseUrl);

/// <summary>
/// 全屏背景图资源目录。id 会持久化到 config.json；旧配置缺字段或 id 失效时按“无”处理。
/// 新增资源时只改这里和 Assets 目录，不要在窗口里硬编码资源路径。
/// </summary>
public static class AmbienceCatalog
{
    public const string NoneId = "";

    public static IReadOnlyList<BackgroundOption> Backgrounds { get; } = new[]
    {
        new BackgroundOption("mountain-snow", "雪山晨光",
            Resource("Backgrounds/mountain-snow.jpg"), Resource("Thumbnails/mountain-snow.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("lake-louise", "露易丝湖",
            Resource("Backgrounds/lake-louise.jpg"), Resource("Thumbnails/lake-louise.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("canyon", "红岩峡谷",
            Resource("Backgrounds/canyon.jpg"), Resource("Thumbnails/canyon.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("forest-winter-river", "冬日林间河",
            Resource("Backgrounds/forest-winter-river.jpg"), Resource("Thumbnails/forest-winter-river.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("river-sunrise", "河畔日出",
            Resource("Backgrounds/river-sunrise.jpg"), Resource("Thumbnails/river-sunrise.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("lake-sunset", "湖畔落霞",
            Resource("Backgrounds/lake-sunset.jpg"), Resource("Thumbnails/lake-sunset.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("waterfall-forest", "林间瀑布",
            Resource("Backgrounds/waterfall-forest.jpg"), Resource("Thumbnails/waterfall-forest.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("ocean-coast", "海岸浪花",
            Resource("Backgrounds/ocean-coast.jpg"), Resource("Thumbnails/ocean-coast.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("misty-mountains", "云海群山",
            Resource("Backgrounds/misty-mountains.jpg"), Resource("Thumbnails/misty-mountains.jpg"),
            "NegativeSpace CC0", "CC0 1.0", "https://negativespace.co/license/"),
        new BackgroundOption("bright-library", "明亮自习室",
            Resource("Backgrounds/bright-library.jpg"), Resource("Thumbnails/bright-library.jpg"),
            "LibreShot Public Domain", "Public Domain", "https://libreshot.com/license/"),
    };

    private static readonly Dictionary<string, BackgroundOption> BackgroundMap =
        Backgrounds.ToDictionary(o => o.Id, StringComparer.Ordinal);

    public static bool TryGetBackground(string? id, out BackgroundOption option)
    {
        if (!string.IsNullOrWhiteSpace(id) && BackgroundMap.TryGetValue(id.Trim(), out var found))
        {
            option = found;
            return true;
        }

        option = null!;
        return false;
    }

    public static string ResolveBackgroundId(string? id) =>
        TryGetBackground(id, out var option) ? option.Id : NoneId;

    public static string BackgroundName(string? id) =>
        TryGetBackground(id, out var option) ? option.DisplayName : "无背景";

    private static string Resource(string relativePath) =>
        "pack://application:,,,/MikoBarrier.App;component/Assets/" + relativePath;
}
