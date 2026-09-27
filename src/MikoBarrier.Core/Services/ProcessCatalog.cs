using System.Diagnostics;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>枚举当前运行的程序，用于"把正在运行的软件加入名单"。</summary>
public static class ProcessCatalog
{
    /// <summary>自身进程名：不放进候选列表（勾选自己没有意义，也容易出 bug）。</summary>
    private static readonly string[] SelfProcessNames = { "MikoBarrier.App", "MikoBarrier.Guard" };

    public static IReadOnlyList<RunningApp> GetRunningApps(bool requirePath = true)
    {
        var list = new List<RunningApp>();
        var selfPath = Environment.ProcessPath;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                if (SelfProcessNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // 受保护 / 更高权限的进程读不到路径，跳过即可。
                }

                if (requirePath && string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(path) &&
                    !string.IsNullOrWhiteSpace(selfPath) &&
                    string.Equals(path, selfPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var title = process.MainWindowTitle ?? string.Empty;

                list.Add(new RunningApp
                {
                    DisplayName = string.IsNullOrWhiteSpace(title) ? name : $"{name} — {Trim(title, 48)}",
                    ProcessName = name,
                    FilePath = path ?? string.Empty,
                    WindowTitle = title,
                });
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return list
            .GroupBy(a => string.IsNullOrEmpty(a.FilePath) ? a.ProcessName : a.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 一键白名单：从当前运行程序里挑出尚未被白名单覆盖的程序，返回去重后的新规则。
    /// 系统核心进程、内置必需组件、Windows 系统目录里的程序都会自动放行，不需要重复加入；
    /// MatchByFileName 开启时同名程序只保留一条。
    /// </summary>
    public static (List<AppRule> NewRules, int AlreadyCovered, int SystemSkipped) BuildWhitelistBatch(
        AppSettings settings,
        IReadOnlyList<RunningApp> apps)
    {
        var newRules = new List<AppRule>();
        var plannedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plannedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var alreadyCovered = 0;
        var systemSkipped = 0;

        foreach (var app in apps)
        {
            if (string.IsNullOrWhiteSpace(app.FilePath))
            {
                continue;
            }

            // 系统核心 / 内置组件（输入法、显卡、杀软…）/ 系统目录：白名单模式本来就会放行，不用加规则。
            if (RuleEngine.IsSystemCritical(app.ProcessName, settings) ||
                SystemAllowPolicy.IsComponentExempt(app.ProcessName, settings) ||
                SystemAllowPolicy.IsPathProtected(app.FilePath))
            {
                systemSkipped++;
                continue;
            }

            if (settings.Rules.Any(r =>
                    r.Kind == RuleKind.Whitelist &&
                    RuleEngine.Matches(r, app.ProcessName, app.FilePath, settings.MatchByFileName)))
            {
                alreadyCovered++;
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(app.ProcessName)
                ? Path.GetFileNameWithoutExtension(app.FilePath)
                : app.ProcessName;

            var rule = new AppRule
            {
                Kind = RuleKind.Whitelist,
                Target = RuleTarget.File,
                Path = app.FilePath,
                DisplayName = displayName,
                Note = app.WindowTitle,
            };

            var duplicate = !plannedKeys.Add(rule.Key) ||
                            (settings.MatchByFileName && !plannedNames.Add(RuleEngine.Normalize(displayName)));
            if (duplicate)
            {
                alreadyCovered++;
                continue;
            }

            newRules.Add(rule);
        }

        return (newRules, alreadyCovered, systemSkipped);
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
