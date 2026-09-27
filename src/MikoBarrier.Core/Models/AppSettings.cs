namespace MikoBarrier.Core.Models;

/// <summary>持久化到 config.json 的全部用户配置。</summary>
public sealed class AppSettings
{
    /// <summary>
    /// 系统核心进程白名单：无论黑名单还是白名单模式，这些进程永不被拦截，防止把 Windows 锁死。
    /// 这里只放“缺了会出问题”的系统组件；任务管理器、浏览器、游戏等一律不在其中（可以被拦截）。
    /// 新增的内置项会在 EnsureDefaults() 里增量合并进旧 config.json，用户追加的条目会保留。
    /// </summary>
    public static readonly string[] DefaultSystemWhitelist =
    {
        // Windows 核心
        "explorer", "dwm", "csrss", "winlogon", "wininit", "services", "lsass", "smss",
        "svchost", "fontdrvhost", "taskhostw", "conhost", "sihost", "runtimebroker",
        "userinit", "logonui", "consent", "wermgr", "werfault",
        // 开始菜单 / 任务栏 / 搜索 / 设置 / 锁屏等外壳组件
        "searchhost", "searchapp", "searchindexer", "searchprotocolhost", "searchfilterhost",
        "startmenuexperiencehost", "shellexperiencehost", "shellhost", "lockapp", "useroobebroker",
        "applicationframehost", "inputapp", "pickerhost", "openwith", "devicepairingwizard",
        "systemsettings",
        // 输入法与文本服务
        "ctfmon", "textinputhost", "tabtip", "chsime", "chsis", "conime", "imewdbld", "msctf",
        // 音频引擎
        "audiodg",
        // 打印 / COM 宿主 / WMI / 安装器 / 更新器 / 计划任务等非 exe 链路宿主
        "spoolsv", "printfilterpipelinesvc", "printisolationhost", "splwow64",
        "dllhost", "rundll32", "regsvr32", "wmiprvse", "msiexec", "trustedinstaller", "tiworker",
        "mousocoreworker", "usoclient", "usocoreworker", "wusa", "dism", "dismhost",
        "mmc", "msdtc", "scheduledsurrogate", "omadmclient", "musnotification",
        // Windows 安全中心 / Defender（不要和杀软对着干，否则会被当成病毒）
        "msmpeng", "nissrv", "mpcmdrun", "mpsigstub", "mpdefendercoreservice",
        "securityhealthservice", "securityhealthsystray", "securityhealthhost", "sechealthui",
        "smartscreen", "sense", "mssense", "msseces",
    };

    /// <summary>
    /// 白名单模式专用的“必需组件”豁免名单：第三方输入法、显卡 / 音频驱动、杀毒软件、触控板驱动等。
    /// 只在白名单模式自动放行；黑名单模式完全不受影响，用户仍可主动屏蔽（例如不想用某个输入法）。
    /// Windows 系统目录里的程序另有 SystemAllowPolicy 目录保护，不在这里重复。
    /// </summary>
    public static readonly string[] DefaultComponentWhitelist =
    {
        // 第三方输入法
        "sogoucloud", "sgtool", "sogouinput", "pinyinup", "sogoupy", "sogoupinyin",
        "qqpinyin", "qqpyinpututility", "baidupinyin", "baidusd",
        "wetype", "wetypeinput", "weixininput",
        "iflyinput", "iflyime", "iflyim",
        // 显卡 / 显示驱动控制组件
        "nvcontainer", "nvdisplay.container", "nvsphelper64", "nvsphelper", "nvtray", "nvidia web helper",
        "radeonsoftware", "amdrsserv", "atiesrxx", "atiedxx", "amdow", "radeonsettings",
        "igfxem", "igfxtray", "igfxpers", "hkcmd", "intelcpuservice", "igfxcuiservice", "intelcui",
        // 音频驱动控制面板
        "rtkauduservice64", "rtkngui64", "rtkaudapp", "rtkaudservice64", "realtekservice",
        "nahimicservice", "nahimic", "nahimic3", "nahimicvad",
        "wavesaudioservice", "wavesmaxxaudio",
        "dolbydax3", "dtsapo4service", "dtsapo3service",
        "sonicstudiopro", "sonicstudio3", "csaudio",
        // 触控板 / 外设驱动辅助
        "syntpenh", "syntphelper", "syntpcom", "etdctrl", "etdservice", "etdassistant",
        // 第三方杀毒 / 安全软件（避免拦截失败或触发自我保护）
        "hipsdaemon", "hipstray", "hipsmain",
        "360tray", "360safe", "zhudongfangyu", "360rp", "360sd",
        "qqpctray", "qqpcmgr", "qmbsrv", "qmdl", "qqpcrtp",
        "avp", "avpui", "ekrn", "egui",
        "avastui", "avastsvc", "aswidsagent", "avgui", "avgsvc",
        "bdagent", "vsserv", "mbamtray", "malwarebytes", "mbamservice",
        "mcshield", "mcuicnt", "mcafeeshield", "rtvscan", "savservice", "sophoshealth",
        "ravmond", "rstray", "rsmon", "kavtray", "kislive", "360rps",
    };

    // ---------- 界面 ----------
    /// <summary>兼容旧配置的基础明暗主题；实际外观以 ThemePreset 为准。</summary>
    public ThemeKind Theme { get; set; } = ThemeKind.Dark;

    /// <summary>主题预设 id（见 ThemeCatalog）；为空表示按 Theme 使用默认预设。</summary>
    public string ThemePreset { get; set; } = string.Empty;

    /// <summary>界面字体名称；为空表示使用系统默认字体。</summary>
    public string UiFontFamily { get; set; } = string.Empty;

    /// <summary>自定义窗口背景色（#RRGGBB）；为空表示使用所选预设的背景色。</summary>
    public string CustomBackgroundColor { get; set; } = string.Empty;

    /// <summary>全屏计时背景图 id（见 AmbienceCatalog）；为空表示不使用背景图。</summary>
    public string FullScreenBackgroundId { get; set; } = string.Empty;

    /// <summary>全屏计时主卡片是否收起：收起后只保留顶部迷你计时条，避免挡住背景图。</summary>
    public bool FullScreenPanelCollapsed { get; set; }

    public bool AutoStart { get; set; } = true;

    public bool MinimizeToTray { get; set; } = true;

    /// <summary>用户希望始终以管理员身份运行（下次启动会自动请求提权）。</summary>
    public bool StartElevated { get; set; }

    // ---------- 巫女模式 ----------
    /// <summary>是否已通过全屏计时角落的“裂缝”解锁巫女模式；解锁后永久保留。</summary>
    public bool MikoModeUnlocked { get; set; }

    /// <summary>是否启用巫女模式：界面文案切换为巫女口吻，并解锁「真·巫女」主题。</summary>
    public bool MikoModeEnabled { get; set; }

    // ---------- 密码 ----------
    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public int PasswordIterations { get; set; }

    /// <summary>忘记密码时的恢复码（只保存哈希）。</summary>
    public string RecoveryHash { get; set; } = string.Empty;

    public string RecoverySalt { get; set; } = string.Empty;

    public int RecoveryIterations { get; set; }

    /// <summary>忘记密码用的 3 个问答（只保存答案哈希）。</summary>
    public List<SecurityQuestion> SecurityQuestions { get; set; } = new();

    public bool HasSecurityQuestions => SecurityQuestions.Count >= 3;

    public bool HasPassword =>
        !string.IsNullOrWhiteSpace(PasswordHash) && !string.IsNullOrWhiteSpace(PasswordSalt);

    public bool HasRecoveryCode =>
        !string.IsNullOrWhiteSpace(RecoveryHash) && !string.IsNullOrWhiteSpace(RecoverySalt);

    // ---------- 密码 / 恢复码的每日提前退出限制 ----------
    /// <summary>密码提前退出计数所属的本地日期（yyyy-MM-dd，按天清零）。</summary>
    public string PasswordExitDate { get; set; } = string.Empty;

    /// <summary>当天成功用密码提前退出的次数：0→1→2→3；达到 3 次后当天冻结。</summary>
    public int PasswordExitCount { get; set; }

    /// <summary>当前密码冷却的截止 UTC 时间；为空或已过表示可以使用。</summary>
    public DateTime? PasswordExitCooldownUntilUtc { get; set; }

    /// <summary>恢复码“提前结束自律”额度所属的本地日期（yyyy-MM-dd）。</summary>
    public string RecoveryExitDate { get; set; } = string.Empty;

    /// <summary>当天是否已经用恢复码提前结束过自律（每天只允许一次）。</summary>
    public bool RecoveryExitUsed { get; set; }

    /// <summary>系统时间防回拨用的“最后一次观察到的 UTC 时间”（只增不减）。</summary>
    public DateTime LastSeenUtc { get; set; }

    // ---------- 名单与策略 ----------
    public List<AppRule> Rules { get; set; } = new();

    public List<string> BlockedSites { get; set; } = new();

    /// <summary>系统核心进程名：黑名单 / 白名单模式都永远放行。</summary>
    public List<string> SystemWhitelist { get; set; } = new();

    /// <summary>白名单模式专用的必需组件名单；黑名单模式不受影响，用户仍可主动屏蔽。</summary>
    public List<string> ComponentWhitelist { get; set; } = new();

    /// <summary>
    /// 黑名单 / 白名单里的"文件"规则是否同时按文件名匹配（默认开）。
    /// 开着更狠：同一个程序换了目录（启动器把本体放到别处）也能拦住；
    /// 关掉更精确：只认完整路径。
    /// </summary>
    public bool MatchByFileName { get; set; } = true;

    /// <summary>默认的执行方式：是否直接结束被拦截的进程（false = 只隐藏窗口并告警）。</summary>
    public bool KillBlockedProcesses { get; set; } = true;

    // ---------- 中断欠债 ----------
    /// <summary>上一次主动中断"欠下"的专注秒数（单次封顶 120 分钟），会自动加到下一次专注的第一轮。</summary>
    public int DebtSeconds { get; set; }

    /// <summary>提前退出欠债所属的本地月份（yyyy-MM，跨月清空）；旧配置为空时首次登记当前月并保留历史欠债。</summary>
    public string DebtMonth { get; set; } = string.Empty;

    // ---------- 防绕过：被 Kill 的递进惩罚欠债 ----------
    /// <summary>Kill 惩罚计数所属的本地月份（yyyy-MM，跨月清零）。</summary>
    public string KillPenaltyMonth { get; set; } = string.Empty;

    /// <summary>本月被确认 Kill 的次数（用于 5/10/15… 递进；跨月清零）。</summary>
    public int KillPenaltyCount { get; set; }

    /// <summary>本月尚未偿还的 Kill 惩罚欠债秒数（每月累计封顶 60 分钟，偿还后可再次补满）。</summary>
    public int KillPenaltyDebtSeconds { get; set; }

    /// <summary>本月是否已经达到过 60 分钟封顶；达到后任意新 Kill 都会把余额补满回 60。</summary>
    public bool KillPenaltyCapReached { get; set; }

    /// <summary>已入账的 Kill 事件 ID，保证重复扫描事件文件不会重复罚。</summary>
    public List<string> AppliedKillIncidentIds { get; set; } = new();

    /// <summary>Kill 事件去重用的上一条会话 ID / 时间（同一会话 15 秒内的双杀算一次）。</summary>
    public string KillPenaltyLastSessionId { get; set; } = string.Empty;

    public DateTime KillPenaltyLastUtc { get; set; }

    // ---------- 上一次使用的计划 ----------
    public FocusPlan LastPlan { get; set; } = new();

    /// <summary>旧版“整场自律只关联一个任务”的字段，仅为兼容旧 config.json 保留；新版请用 FocusPlan.RoundPlans 的每轮任务。</summary>
    public string LastTaskId { get; set; } = string.Empty;

    public void EnsureDefaults()
    {
        Rules ??= new List<AppRule>();
        BlockedSites ??= new List<string>();
        SystemWhitelist ??= new List<string>();
        ComponentWhitelist ??= new List<string>();
        SecurityQuestions ??= new List<SecurityQuestion>();
        AppliedKillIncidentIds ??= new List<string>();
        LastPlan ??= new FocusPlan();
        ThemePreset ??= string.Empty;
        UiFontFamily ??= string.Empty;
        CustomBackgroundColor ??= string.Empty;
        FullScreenBackgroundId ??= string.Empty;

        // 增量合并内置保护项：旧 config.json 缺哪条补哪条，用户自己追加的条目保留。
        // 注意：从配置里删掉内置条目，下次启动也会被补回来——这是防止把 Windows 锁死的安全底线。
        MergeDefaults(SystemWhitelist, DefaultSystemWhitelist);
        MergeDefaults(ComponentWhitelist, DefaultComponentWhitelist);
    }

    private static void MergeDefaults(List<string> target, IEnumerable<string> defaults)
    {
        foreach (var item in defaults)
        {
            if (!target.Any(existing =>
                    string.Equals(NormalizeName(existing), NormalizeName(item), StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(item);
            }
        }
    }

    private static string NormalizeName(string value)
    {
        var name = value.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }
}
