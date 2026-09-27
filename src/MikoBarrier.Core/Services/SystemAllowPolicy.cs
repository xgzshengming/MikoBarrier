using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>
/// 白名单模式下的「系统硬保护层」。
///
/// 目标：在白名单模式里，Windows 自带的系统目录、Defender 等关键目录中的程序始终放行，
/// 不依赖进程名列表是否齐全，也不依赖一键白名单有没有扫描到。
/// 这样在输入法多、杀软多、OEM 工具多的「脏」电脑上也不会因为漏加一条规则而把系统锁死。
///
/// 注意：
///   1. 只扩大白名单模式的放行范围；黑名单模式完全不受影响（用户仍可主动屏蔽 System32 里的小工具）。
///   2. 不包含 %ProgramFiles%\WindowsApps（商店应用目录），否则 Spotify / 游戏等商店应用会全部被放行。
///   3. 不包含用户可写的 Windows\Temp 等目录，避免「放行整个 Windows 目录」带来的绕过空间。
/// </summary>
public static class SystemAllowPolicy
{
    /// <summary>需要始终放行的系统目录（模板形式，运行时展开环境变量）。</summary>
    private static readonly string[] DirectoryTemplates =
    {
        // Windows 本体
        @"%SystemRoot%\System32",
        @"%SystemRoot%\SysWOW64",
        @"%SystemRoot%\WinSxS",
        @"%SystemRoot%\SystemApps",
        @"%SystemRoot%\ImmersiveControlPanel",
        @"%SystemRoot%\servicing",
        @"%SystemRoot%\assembly",
        @"%SystemRoot%\Microsoft.NET",
        @"%SystemRoot%\ShellExperiences",
        @"%SystemRoot%\Speech",
        @"%SystemRoot%\Fonts",

        // Windows Defender（不同系统版本可能装在 Program Files 或 ProgramData）
        @"%ProgramFiles%\Windows Defender",
        @"%ProgramFiles%\Windows Defender Advanced Threat Protection",
        @"%ProgramFiles(x86)%\Windows Defender",
        @"%ProgramData%\Microsoft\Windows Defender",
    };

    private static readonly string[] ExpandedDirectories = ExpandDirectories(DirectoryTemplates);

    /// <summary>展开后的系统保护目录（用于自检与界面展示，共 N 条）。</summary>
    public static IReadOnlyList<string> ProtectedDirectories => ExpandedDirectories;

    /// <summary>文件路径是否位于系统保护目录内（含子目录）。空路径 / 相对路径 / 非法路径一律返回 false。</summary>
    public static bool IsPathProtected(string? filePath)
    {
        var full = TryNormalizeFullPath(filePath);
        if (full is null)
        {
            return false;
        }

        foreach (var directory in ExpandedDirectories)
        {
            if (full.Equals(directory, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (full.Length > directory.Length &&
                full.StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
                (full[directory.Length] == Path.DirectorySeparatorChar ||
                 full[directory.Length] == Path.AltDirectorySeparatorChar))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>进程名是否在「白名单模式豁免组件」列表内（例如输入法、显卡驱动、杀软）。</summary>
    public static bool IsComponentExempt(string processName, AppSettings settings)
    {
        var name = RuleEngine.Normalize(processName);
        if (string.IsNullOrWhiteSpace(name) || settings.ComponentWhitelist is not { Count: > 0 })
        {
            return false;
        }

        foreach (var item in settings.ComponentWhitelist)
        {
            if (string.Equals(RuleEngine.Normalize(item), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>进程是否处于「系统核心 / 系统目录 / 内置组件」任一硬保护层内。</summary>
    public static bool IsProtectedProcess(string processName, string? filePath, AppSettings settings) =>
        RuleEngine.IsSystemCritical(processName, settings) ||
        IsComponentExempt(processName, settings) ||
        IsPathProtected(filePath);

    /// <summary>
    /// 把路径规整成可比较的完整路径：
    ///   - 去掉外层引号和 \\?\ 前缀；
    ///   - 必须是完整路径（避免把裸进程名解析成当前目录下的文件）；
    ///   - Path.GetFullPath 会消掉 .. 和 .，避免 C:\Windows\System32\..\Temp\x.exe 被误判为系统目录。
    /// </summary>
    private static string? TryNormalizeFullPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            var path = filePath.Trim().Trim('"');
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                path = path[4..];
            }

            if (!Path.IsPathFullyQualified(path))
            {
                return null;
            }

            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static string[] ExpandDirectories(IEnumerable<string> templates)
    {
        var list = new List<string>();
        foreach (var template in templates)
        {
            var expanded = Environment.ExpandEnvironmentVariables(template);
            if (expanded.Contains('%'))
            {
                // 环境变量不存在（例如 32 位系统上的 ProgramFiles(x86)），跳过即可。
                continue;
            }

            var full = TryNormalizeFullPath(expanded);
            if (full is null || full.Length == 0)
            {
                continue;
            }

            if (!list.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(full);
            }
        }

        return list.ToArray();
    }
}
