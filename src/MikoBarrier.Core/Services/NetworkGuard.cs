using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 网络封锁：
///  1) hosts 屏蔽指定网站（需要管理员）；
///  2) 直接禁用 / 恢复所有正在使用的网卡（最强制，需要管理员）。
/// 恢复信息写在 data/network-state.json，程序启动时会自动尝试还原，避免崩溃后留下断网状态。
/// </summary>
public static class NetworkGuard
{
    private const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string BeginMarker = "# >>> MikoBarrier blocked sites begin >>>";
    private const string EndMarker = "# >>> MikoBarrier blocked sites end <<<";

    public static bool TryApplyWebBlock(IEnumerable<string> domains, out string message)
    {
        try
        {
            var list = NormalizeDomains(domains);
            if (list.Count == 0)
            {
                message = "网站名单为空，未做任何修改";
                return false;
            }

            var text = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : string.Empty;
            text = RemoveBlock(text);

            var sb = new StringBuilder();
            sb.Append(text.TrimEnd());
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine(BeginMarker);
            foreach (var domain in list)
            {
                sb.AppendLine($"0.0.0.0 {domain}");
                sb.AppendLine($"::1 {domain}");
            }

            sb.AppendLine(EndMarker);
            File.WriteAllText(HostsPath, sb.ToString());

            AdminHelper.RunCommand("ipconfig", "/flushdns", out _);
            message = $"已屏蔽 {list.Count} 个域名";
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            message = "需要管理员权限才能修改 hosts";
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    /// <summary>当前 hosts 里是否存在我们写入的封锁条目。</summary>
    public static bool HasWebBlock()
    {
        try
        {
            return File.Exists(HostsPath) &&
                   File.ReadAllText(HostsPath).Contains(BeginMarker, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryRevertWebBlock(out string message)
    {
        try
        {
            if (!File.Exists(HostsPath))
            {
                message = "hosts 不存在，跳过";
                return true;
            }

            var text = File.ReadAllText(HostsPath);
            if (!text.Contains(BeginMarker, StringComparison.Ordinal))
            {
                message = "没有发现 MikoBarrier 写入的封锁条目";
                return true;
            }

            File.WriteAllText(HostsPath, RemoveBlock(text));
            AdminHelper.RunCommand("ipconfig", "/flushdns", out _);
            message = "已清除 hosts 封锁";
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            message = "需要管理员权限才能修改 hosts";
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public static bool TrySetNetworkAdaptersEnabled(bool enabled, out string message)
    {
        if (!AdminHelper.IsElevated)
        {
            message = "需要管理员权限才能启用/禁用网卡";
            return false;
        }

        try
        {
            if (!enabled)
            {
                var names = GetActiveAdapterNames();
                if (names.Count == 0)
                {
                    message = "没有找到活动网卡";
                    return false;
                }

                JsonStore.Save(StoragePaths.NetworkStateFile, names);

                var failed = new List<string>();
                foreach (var name in names)
                {
                    AdminHelper.RunCommand("netsh", $"interface set interface name=\"{name}\" admin=disable", out var code);
                    if (code != 0)
                    {
                        failed.Add(name);
                    }
                }

                message = failed.Count == 0
                    ? $"已禁用 {names.Count} 个网卡"
                    : $"部分网卡禁用失败：{string.Join("、", failed)}";
                return failed.Count == 0;
            }

            var saved = JsonStore.Load(StoragePaths.NetworkStateFile, static () => new List<string>());
            var targets = saved.Count > 0 ? saved : GetActiveAdapterNames();
            var errors = new List<string>();
            foreach (var name in targets)
            {
                AdminHelper.RunCommand("netsh", $"interface set interface name=\"{name}\" admin=enable", out var code);
                if (code != 0)
                {
                    errors.Add(name);
                }
            }

            try
            {
                if (File.Exists(StoragePaths.NetworkStateFile))
                {
                    File.Delete(StoragePaths.NetworkStateFile);
                }
            }
            catch
            {
            }

            message = errors.Count == 0
                ? $"已恢复 {targets.Count} 个网卡"
                : $"部分网卡恢复失败：{string.Join("、", errors)}";
            return errors.Count == 0;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    /// <summary>程序启动 / 退出时调用：清理可能残留的封锁，保证不会把用户永久锁在断网状态。</summary>
    public static void RevertAllQuietly()
    {
        TryRevertWebBlock(out _);

        if (!AdminHelper.IsElevated)
        {
            return;
        }

        try
        {
            if (File.Exists(StoragePaths.NetworkStateFile))
            {
                TrySetNetworkAdaptersEnabled(true, out _);
            }
        }
        catch
        {
        }
    }

    private static List<string> GetActiveAdapterNames() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                        n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> NormalizeDomains(IEnumerable<string> domains)
    {
        var result = new List<string>();
        foreach (var raw in domains)
        {
            var domain = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (domain.Length == 0)
            {
                continue;
            }

            domain = domain
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim('/');

            var slash = domain.IndexOf('/');
            if (slash >= 0)
            {
                domain = domain[..slash];
            }

            if (domain.Length > 0 && !result.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(domain);
            }
        }

        return result;
    }

    private static string RemoveBlock(string hostsText)
    {
        var pattern = Regex.Escape(BeginMarker) + ".*?" + Regex.Escape(EndMarker);
        var cleaned = Regex.Replace(hostsText, pattern, string.Empty, RegexOptions.Singleline);
        return Regex.Replace(cleaned, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
    }
}
