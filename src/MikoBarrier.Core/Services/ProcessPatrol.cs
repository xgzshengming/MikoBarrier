using System.Diagnostics;
using System.Runtime.InteropServices;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

public enum PatrolActionKind
{
    None = 0,
    Kill = 1,
    Hide = 2,
}

public sealed record PatrolDecision(int Pid, string Name, string FilePath, PatrolActionKind Action, string Reason);

public sealed record PatrolFailure(PatrolDecision Decision, string Error);
public sealed record PatrolExecutionResult(IReadOnlyList<PatrolDecision> Handled, IReadOnlyList<PatrolFailure> Failures);

/// <summary>
/// 进程巡逻：按名单裁决当前运行的进程，然后结束它或者隐藏它的窗口。
///
/// 安全约定：
///  1. 系统核心进程永远放行（RuleEngine.SystemWhitelist）；
///  2. 白名单模式下，内置组件 / Windows 系统目录 / 用户白名单 / 启动链路祖先在白名单里的程序也放行；
///  3. 自律锁定自身进程永不处理；
///  4. **白名单模式只拦截有窗口的程序**，避免误杀后台服务 / 驱动 / 计划任务宿主；
///  5. 中场休息期间由调用方（Guard）跳过巡逻。
/// </summary>
public static class ProcessPatrol
{
    private static readonly string[] SelfProcessNames = { "MikoBarrier.App", "MikoBarrier.Guard" };

    private const int SwHide = 0;
    private const uint Th32CsSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ProcessId;
        public IntPtr Th32DefaultHeapId;
        public uint Th32ModuleId;
        public uint CntThreads;
        public uint Th32ParentProcessId;
        public int PcPriClassBase;
        public uint DwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string SzExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>上一次快照里"读不到路径"的进程数量（通常是需要管理员权限才能看到的进程）。</summary>
    public static int UnknownPathCount { get; private set; }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static IReadOnlyList<ProcessInfo> Snapshot()
    {
        UnknownPathCount = 0;
        var parentMap = ReadParentProcessMap();
        var list = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (SelfProcessNames.Any(s => s.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string path;
                try
                {
                    path = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    // 读不到路径（权限不够 / 反作弊保护的进程）：不跳过，
                    // 交给 RuleEngine 按进程名兜底匹配，否则游戏类程序永远拦不住。
                    path = string.Empty;
                }

                var title = string.Empty;
                try
                {
                    title = process.MainWindowTitle ?? string.Empty;
                }
                catch
                {
                }

                DateTime? startedUtc = null;
                try
                {
                    startedUtc = process.StartTime.ToUniversalTime();
                }
                catch
                {
                    // 受保护进程可能拒绝读取启动时间；不采集即可，不影响其他判定。
                }

                list.Add(new ProcessInfo(
                    process.Id,
                    process.ProcessName,
                    path,
                    title,
                    parentMap.TryGetValue(process.Id, out var parentPid) ? parentPid : 0,
                    startedUtc));
                if (path.Length == 0)
                {
                    UnknownPathCount++;
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return list;
    }

    /// <summary>用 Toolhelp 进程快照读取 PID -> 父 PID 映射；失败时返回空表（链路功能降级为不判定）。</summary>
    private static Dictionary<int, int> ReadParentProcessMap()
    {
        var map = new Dictionary<int, int>();
        var handle = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        if (handle == InvalidHandleValue)
        {
            return map;
        }

        try
        {
            var entry = new ProcessEntry32W { DwSize = (uint)Marshal.SizeOf<ProcessEntry32W>() };
            if (!Process32FirstW(handle, ref entry))
            {
                return map;
            }

            do
            {
                if (entry.Th32ProcessId != 0 && entry.Th32ParentProcessId != 0)
                {
                    map[(int)entry.Th32ProcessId] = (int)entry.Th32ParentProcessId;
                }

                entry.DwSize = (uint)Marshal.SizeOf<ProcessEntry32W>();
            }
            while (Process32NextW(handle, ref entry));
        }
        catch
        {
            // 枚举失败只影响启动链路判定，不影响基础拦截。
        }
        finally
        {
            CloseHandle(handle);
        }

        return map;
    }

    public static IReadOnlyList<PatrolDecision> Sweep(AppSettings settings, FocusPlan plan, IReadOnlyList<ProcessInfo>? snapshot = null)
    {
        var decisions = new List<PatrolDecision>();

        if (!plan.BlockApplications)
        {
            return decisions;
        }

        var list = snapshot ?? Snapshot();
        var byPid = new Dictionary<int, ProcessInfo>();
        foreach (var item in list)
        {
            byPid[item.Pid] = item;
        }

        foreach (var info in list)
        {
            var verdict = RuleEngine.EvaluateProcess(info, byPid, settings, plan);
            if (!verdict.ShouldBlock)
            {
                continue;
            }

            // 白名单模式：只拦真正有窗口的程序，保护后台服务、驱动、计划任务与 COM 宿主。
            if (plan.UseWhitelistMode && !info.HasWindow)
            {
                continue;
            }

            decisions.Add(new PatrolDecision(
                info.Pid,
                info.Name,
                info.FilePath,
                settings.KillBlockedProcesses ? PatrolActionKind.Kill : PatrolActionKind.Hide,
                verdict.Reason));
        }

        return decisions;
    }

    public static IReadOnlyList<PatrolDecision> Execute(IEnumerable<PatrolDecision> decisions, Action<string>? log = null) =>
        ExecuteDetailed(decisions, log).Handled;

    /// <summary>
    /// 执行拦截，并把"成功"和"失败"分开返回。
    /// 失败最常见的原因是没有管理员权限：提权程序（游戏启动器、反作弊）会被系统拒绝结束。
    /// </summary>
    public static PatrolExecutionResult ExecuteDetailed(IEnumerable<PatrolDecision> decisions, Action<string>? log = null)
    {
        var handled = new List<PatrolDecision>();
        var failures = new List<PatrolFailure>();

        foreach (var decision in decisions)
        {
            try
            {
                using var process = Process.GetProcessById(decision.Pid);
                if (process.HasExited)
                {
                    continue;
                }

                if (decision.Action == PatrolActionKind.Kill)
                {
                    process.Kill(entireProcessTree: true);
                    handled.Add(decision);
                    log?.Invoke($"已结束「{decision.Name}」(PID {decision.Pid})：{decision.Reason}  路径：{DescribePath(decision.FilePath)}");
                }
                else
                {
                    var handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero && ShowWindow(handle, SwHide))
                    {
                        handled.Add(decision);
                        log?.Invoke($"已隐藏「{decision.Name}」(PID {decision.Pid})：{decision.Reason}  路径：{DescribePath(decision.FilePath)}");
                    }
                    else
                    {
                        var error = "没找到可隐藏的窗口";
                        failures.Add(new PatrolFailure(decision, error));
                        log?.Invoke($"拦截「{decision.Name}」(PID {decision.Pid}) 失败：{error}  路径：{DescribePath(decision.FilePath)}");
                    }
                }
            }
            catch (ArgumentException)
            {
                // 进程已经自己退出了，忽略。
            }
            catch (Exception ex)
            {
                var hint = ex is System.ComponentModel.Win32Exception { NativeErrorCode: 5 }
                    ? "（Access Denied：需要以管理员身份运行）"
                    : string.Empty;

                failures.Add(new PatrolFailure(decision, ex.Message + hint));
                log?.Invoke($"拦截「{decision.Name}」(PID {decision.Pid}) 失败：{ex.Message}{hint}  路径：{DescribePath(decision.FilePath)}");
            }
        }

        return new PatrolExecutionResult(handled, failures);
    }

    private static string DescribePath(string filePath) =>
        string.IsNullOrEmpty(filePath) ? "读不到（受保护或权限不足）" : filePath;
}
