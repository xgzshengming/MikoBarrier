namespace MikoBarrier.Core.Services;

/// <summary>
/// 开机自启（框架阶段）：在"启动"文件夹放一个 MikoBarrier.cmd。
/// 后续接入 SYSTEM 服务守护时，会换成任务计划程序（最高权限、崩溃自动重启）。
/// </summary>
public static class AutoStartService
{
    private static string StartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    private static string LauncherPath => Path.Combine(StartupFolder, "MikoBarrier.cmd");

    public static bool IsEnabled => File.Exists(LauncherPath);

    public static bool Enable(out string message)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                message = "无法确定当前程序路径";
                return false;
            }

            Directory.CreateDirectory(StartupFolder);
            var content = "@echo off\r\n" +
                          "rem MikoBarrier 开机自启（框架阶段使用启动文件夹，后续替换为计划任务）\r\n" +
                          $"start \"\" \"{exe}\"\r\n";
            File.WriteAllText(LauncherPath, content);

            message = $"已创建开机启动项：{LauncherPath}";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public static bool Disable(out string message)
    {
        try
        {
            if (File.Exists(LauncherPath))
            {
                File.Delete(LauncherPath);
            }

            message = "已移除开机启动项";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}
