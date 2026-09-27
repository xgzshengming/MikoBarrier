using System.Reflection;

namespace MikoBarrier;

/// <summary>
/// 应用版本信息：统一从程序集元数据读取，避免界面里再手写 v0.x 文案而产生割裂。
/// </summary>
public static class AppInfo
{
    /// <summary>程序集 InformationalVersion，例如 0.6.0-Miko。</summary>
    public static string InformationalVersion { get; } = ReadInformationalVersion();

    /// <summary>界面展示用版本号，例如 v0.6.0-Miko（按 InformationalVersion 原样展示）。</summary>
    public static string DisplayVersion { get; } = FormatDisplayVersion(InformationalVersion);

    private static string ReadInformationalVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Trim();
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string FormatDisplayVersion(string informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "v0.0.0";
        }

        // MSBuild 可能会追加 +commit：界面不展示这部分。
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            informational = informational[..plus];
        }

        // 按 InformationalVersion 原样展示，不再把 -Miko 折成括号。
        return informational.StartsWith('v') ? informational : "v" + informational;
    }
}
