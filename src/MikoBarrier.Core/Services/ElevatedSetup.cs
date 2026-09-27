using System.IO;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 用 Windows 计划任务实现两件事：
///   1) 主程序登录时以"最高权限"启动不再每次弹 UAC，也不用手动点"以管理员身份重启"；
///   2) 看门狗以 SYSTEM 身份开机启动（--always 常驻），任务管理器里普通用户杀不掉它。
/// 需要管理员权限才能创建。
/// </summary>
public static class ElevatedSetup
{
    private const string AppTask = @"MikoBarrier\App";
    private const string GuardTask = @"MikoBarrier\Guard";

    public static bool IsElevated => AdminHelper.IsElevated;

    public static bool TaskExists(string taskName) =>
        AdminHelper.RunCommand("schtasks", $"/Query /TN \"{taskName}\"", out var code) is var _ && code == 0;

    public static bool AreTasksInstalled() => TaskExists(AppTask) && TaskExists(GuardTask);

    public static bool TryInstall(out string message)
    {
        if (!AdminHelper.IsElevated)
        {
            message = "需要以管理员身份运行 MikoBarrier 才能创建计划任务";
            return false;
        }

        var app = Environment.ProcessPath;
        var guard = Path.Combine(AppContext.BaseDirectory, "MikoBarrier.Guard.exe");
        if (string.IsNullOrWhiteSpace(app) || !File.Exists(guard))
        {
            message = "找不到主程序或 MikoBarrier.Guard.exe";
            return false;
        }

        var notes = new List<string>();

        AdminHelper.RunCommand("schtasks",
            $"/Create /TN \"{AppTask}\" /TR \"\\\"{app}\\\"\" /SC ONLOGON /RL HIGHEST /F", out var appCode);
        notes.Add(appCode == 0 ? "主程序：登录时静默提权启动 OK" : "主程序任务创建失败");

        AdminHelper.RunCommand("schtasks",
            $"/Create /TN \"{GuardTask}\" /TR \"\\\"{guard}\\\" --patrol --always --watch \\\"{app}\\\"\" /SC ONSTART /RU SYSTEM /RL HIGHEST /F", out var guardCode);
        notes.Add(guardCode == 0 ? "看门狗：SYSTEM 开机启动 OK" : "看门狗任务创建失败");

        message = string.Join("；", notes);
        return appCode == 0 && guardCode == 0;
    }

    public static bool TryRemove(out string message)
    {
        if (!AdminHelper.IsElevated)
        {
            message = "需要以管理员身份运行才能删除计划任务";
            return false;
        }

        AdminHelper.RunCommand("schtasks", $"/Delete /TN \"{AppTask}\" /F", out _);
        AdminHelper.RunCommand("schtasks", $"/Delete /TN \"{GuardTask}\" /F", out var guardCode);

        message = guardCode == 0 ? "已删除 MikoBarrier 计划任务" : "计划任务已删除（或原本不存在）";
        return true;
    }
}