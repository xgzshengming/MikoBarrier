using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MikoBarrier;

/// <summary>
/// 应用图标的统一入口：窗口 / 任务栏 / 最小化 / 托盘都用同一份 Assets\MikoBarrier.ico。
/// 图标由 tools\make-icon.ps1 从母图生成，编译时嵌入 exe。
/// </summary>
internal static class AppIcon
{
    private const string ResourceUri = "pack://application:,,,/MikoBarrier.App;component/Assets/MikoBarrier.ico";

    private static bool _windowIconLoaded;
    private static ImageSource? _windowIcon;

    /// <summary>窗口图标：取 ico 里最大的一帧，任务栏、Alt+Tab、最小化按钮共用。</summary>
    public static ImageSource? WindowIcon
    {
        get
        {
            if (!_windowIconLoaded)
            {
                _windowIconLoaded = true;
                _windowIcon = LoadWindowIcon();
            }

            return _windowIcon;
        }
    }

    /// <summary>打开 ico 资源流（托盘 NotifyIcon 等 System.Drawing 场景使用），由调用方释放。</summary>
    public static Stream? OpenIconStream()
    {
        try
        {
            return Application.GetResourceStream(new Uri(ResourceUri, UriKind.Absolute))?.Stream;
        }
        catch
        {
            // 图标缺失 / 资源 URI 失效时静默降级，不影响启动。
            return null;
        }
    }

    private static ImageSource? LoadWindowIcon()
    {
        using var stream = OpenIconStream();
        if (stream is null)
        {
            return null;
        }

        try
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight)
                .FirstOrDefault();
            if (frame is null)
            {
                return null;
            }

            try
            {
                frame.Freeze();
            }
            catch
            {
                // 个别解码器产生的帧不支持冻结；不冻结也能正常当窗口图标用。
            }

            return frame;
        }
        catch
        {
            return null;
        }
    }
}
