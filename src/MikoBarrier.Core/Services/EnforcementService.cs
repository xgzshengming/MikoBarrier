using System.Text;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 强制策略的执行入口：进入专注时 Apply，结束 / 中断 / 崩溃恢复时 Revert。
///
/// 应用进程巡逻（杀 / 隐藏名单内程序）由 MikoBarrier.Guard 在 v0.3 接入，
/// 这里保留"专注阶段开、休息阶段关"的开关接口：休息时间放开应用屏蔽、保留断网。
/// </summary>
public sealed class EnforcementService
{
    public bool Elevated => AdminHelper.IsElevated;

    /// <summary>应用屏蔽当前是否处于生效状态（休息时间会临时关闭）。</summary>
    public bool AppBlockingActive { get; private set; }

    public IReadOnlyList<string> Apply(AppSettings settings, FocusPlan plan)
    {
        var messages = new List<string>();

        if (!AdminHelper.IsElevated)
        {
            messages.Add("⚠ 未以管理员身份运行，网络封锁不会生效");
        }

        switch (plan.NetworkTier)
        {
            case NetworkTier.Gentle:
                messages.Add(NetworkGuard.TryApplyWebBlock(settings.BlockedSites, out var gentleMessage)
                    ? $"✔ 温和档：{gentleMessage}"
                    : $"✘ 温和档失败：{gentleMessage}（可检查网站名单是否为空）");
                break;

            case NetworkTier.Hardcore:
                messages.Add(NetworkGuard.TryApplyWebBlock(settings.BlockedSites, out var hardMessage)
                    ? $"✔ 狠人档：{hardMessage}"
                    : $"· 狠人档：网站名单未生效（{hardMessage}）");

                messages.Add(NetworkGuard.TrySetNetworkAdaptersEnabled(false, out var netMessage)
                    ? $"✔ 狠人档：{netMessage}"
                    : $"✘ 狠人档失败：{netMessage}");
                break;

            default:
                messages.Add("· 网络：本次不断网");
                break;
        }

        if (plan.BlockApplications)
        {
            var mode = plan.UseWhitelistMode ? "白名单" : "黑名单";
            SetAppBlockingActive(true, plan);
            var (ok, guardMessage) = GuardLauncher.TryStartPatrol();

            if (!AdminHelper.IsElevated)
            {
                messages.Add(" 未以管理员身份运行：受保护的游戏 / 反作弊程序可能拦不住，建议先提权");
            }
            messages.Add(ok ? $"\u2714 进程巡逻已启动：{mode}模式（中场休息自动停火）" : $"\u2718 {guardMessage}");
        }
        else
        {
            SetAppBlockingActive(false, plan);
            messages.Add("· 应用：本次不屏蔽");
        }

        return messages;
    }

    /// <summary>
    /// 中场休息时关闭应用屏蔽、回到专注时重新打开；
    /// 网络封锁不受影响（按开始前选择的档位一直生效）。
    /// </summary>
    public void SetAppBlockingActive(bool active, FocusPlan? plan)
    {
        AppBlockingActive = active;
        // v0.3：这里会通知 Guard 启动 / 停止进程巡逻。
    }

    public IReadOnlyList<string> Revert()
    {
        var messages = new List<string>();

        AppBlockingActive = false;
        GuardLauncher.StopPatrol();

        if (NetworkGuard.HasWebBlock())
        {
            messages.Add(NetworkGuard.TryRevertWebBlock(out var webMessage)
                ? $"\u2714 {webMessage}"
                : $"\u2718 {webMessage}");
        }
        else
        {
            messages.Add(" 网站封锁：本次没有启用");
        }

        if (File.Exists(StoragePaths.NetworkStateFile))
        {
            messages.Add(NetworkGuard.TrySetNetworkAdaptersEnabled(true, out var netMessage)
                ? $"\u2714 {netMessage}"
                : $"\u2718 {netMessage}");
        }
        else
        {
            messages.Add(" 网卡：本次没有禁用");
        }

        return messages;
    }
    public static string Join(IEnumerable<string> messages)
    {
        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            if (builder.Length > 0)
            {
                builder.Append("   ");
            }

            builder.Append(message);
        }

        return builder.ToString();
    }
}
