using System.Text;
using MikoBarrier.Core.Services;

namespace MikoBarrier;

/// <summary>一页引导内容。</summary>
public sealed record GuidePage(string Title, string Body);

/// <summary>
/// 新手引导与帮助中心的唯一文本来源：
/// 首启分步演示、点击功能后的子界面引导、「重新演示」和文字版说明都从这里取内容。
/// 以后改文案只改这一处。
/// </summary>
public static class HelpContent
{
    public const string Welcome = "welcome";
    public const string FocusHub = "focus.hub";
    public const string FocusQuick = "focus.quick";
    public const string FocusTask = "focus.task";
    public const string FocusPolicy = "focus.policy";
    public const string FocusNetwork = "focus.network";
    public const string RulesHub = "rules.hub";
    public const string RulesBlack = "rules.black";
    public const string RulesWhite = "rules.white";
    public const string RulesSites = "rules.sites";
    public const string Tasks = "tasks";
    public const string Stats = "stats";
    public const string StatsHistory = "stats.history";
    public const string StatsTaskAwards = "stats.tasks";
    public const string SettingsAppearance = "settings.appearance";
    public const string SettingsHub = "settings.hub";
    public const string SettingsPassword = "settings.password";
    public const string SettingsRecovery = "settings.recovery";
    public const string SettingsQuestions = "settings.questions";
    public const string SettingsForgot = "settings.forgot";

    private static readonly string[] TextHelpTopicOrder =
    {
        Welcome,
        FocusHub,
        FocusQuick,
        FocusTask,
        FocusPolicy,
        FocusNetwork,
        RulesHub,
        RulesBlack,
        RulesWhite,
        RulesSites,
        Tasks,
        Stats,
        StatsHistory,
        StatsTaskAwards,
        SettingsHub,
        SettingsAppearance,
        SettingsPassword,
        SettingsRecovery,
        SettingsQuestions,
        SettingsForgot,
    };

    /// <summary>全部文字版帮助 topic（自检遍历用）。</summary>
    public static IReadOnlyList<string> AllTopicKeys => TextHelpTopicOrder;

    public static IReadOnlyList<GuidePage> GetPages(string key) =>
        GetPages(key, MikoText.IsEnabled);

    /// <summary>指定是否要巫女口吻的版本；自检和帮助中心共用。</summary>
    public static IReadOnlyList<GuidePage> GetPages(string key, bool miko) =>
        miko ? MikoHelpContent.GetPages(key) : GetNormalPages(key);

    private static IReadOnlyList<GuidePage> GetNormalPages(string key) => key switch
    {
        Welcome => new[]
        {
            new GuidePage("欢迎使用 MikoBarrier",
                "欢迎使用 MikoBarrier。\n\n" +
                "这是一个帮助你强制自律的 Windows 工具：开始后名单与策略会锁定，只能凭密码提前结束。\n\n" +
                "点击「下一步」，用两步了解它的用途和注意事项。"),
            new GuidePage("MikoBarrier 是什么",
                "MikoBarrier 是一个强制自律的类番茄钟应用：用轮次专注、应用屏蔽、键盘封锁、全屏计时和断网档位，" +
                "把注意力拉回正在做的事情上。\n\n" +
                "它不会锁死你的电脑：关机 / 注销会无条件放行；开始自律后可以凭密码、恢复码或安全问题退出。" +
                "但请务必先保存正在编辑的工作，强制结束进程仍可能让未保存的内容丢失。\n\n" +
                "另外：自律开始后无法更改密码，请确保自己记得密码；建议设置恢复码和安全问题作为后路。"),
        },

        FocusHub => new[]
        {
            new GuidePage("自律结界",
                "「自律结界」由四个功能入口组成：\n\n" +
                "快速开始：选一个时长，直接开始单轮自律。\n" +
                "任务模式：设置轮数、每轮时长、中场休息与任务。\n" +
                "强制策略：应用屏蔽、白名单、键盘封锁与全屏计时。\n" +
                "断网档位：不断网、温和档或狠人档。\n\n" +
                "配置完成后，点右侧的「开始自律」开始。开始后名单与策略会锁定，只能凭密码提前结束。"),
        },

        FocusQuick => new[]
        {
            new GuidePage("快速开始",
                "选择一个时长即可开始单轮自律：5 分钟、25 分钟、40 分钟、60 分钟，或 1 - 120 分钟的自定义时长。\n\n" +
                "开始后名单与策略会锁定，只能凭密码提前结束。中场休息会放开应用屏蔽，但断网档位仍然生效。\n\n" +
                "选择时长后，点右侧的「开始自律」即可开始。"),
        },

        FocusTask => new[]
        {
            new GuidePage("任务模式：轮数与休息",
                "任务模式适合需要多轮专注的安排。\n\n" +
                "可以设置 1 - 12 轮；多轮模式才可设置中场休息（5 / 10 分钟）。\n\n" +
                "给每一轮选择要做的任务：可以多选、可以给多轮选同一个任务，也可以一轮同时关联多个任务。" +
                "同一轮关联多个任务时，该轮时长与回合按任务数均分；不选则只计入整体统计。"),
            new GuidePage("统一时长与统一任务",
                "默认是每轮单独设置时长和任务。\n\n" +
                "勾选「全部轮次都使用同一时长」，可以为所有轮次选择一个统一时长：" +
                "5 分钟、25 分钟、40 分钟、60 分钟，或 1 - 120 分钟的自定义时长。\n\n" +
                "勾选「全部轮次都使用同一组任务」，再点下面的统一任务按钮，选择一组任务应用到所有轮次；" +
                "取消勾选则回到每轮单独选择。"),
            new GuidePage("选择本轮任务",
                "按住 Ctrl 或 Shift 可以多选：同一轮可以关联多个任务；一个都不选 = 本轮不关联任务。\n\n" +
                "选错了可以点「清空本轮选择」，把本轮恢复为空白状态。"),
        },

        FocusPolicy => new[]
        {
            new GuidePage("强制策略",
                "屏蔽应用：按「名单管理」里的规则执行，自律进行中拦截名单内的程序。\n\n" +
                "白名单模式：只允许名单内程序及其启动链路运行；系统核心、内置组件和系统目录始终放行。\n\n" +
                "键盘封锁：自律中吞掉 Alt+Tab / Alt+Esc / Alt+F4 / Ctrl+Esc / Ctrl+Shift+Esc；" +
                "Win 键保留（Win+V 剪贴板、Win+空格 输入法都能用）。Ctrl+Alt+Del 是系统安全键，拦不住；" +
                "需要紧急离开请用密码结束自律。\n\n" +
                "全屏计时：显示全屏倒计时，不影响使用其他程序。中场休息时应用屏蔽自动放开，回到自律自动恢复。\n\n" +
                "全屏计时右下角「背景」可以随时切换背景图；全屏卡片可点右上角「收起」只留顶部迷你计时条，让背景图完整显示。"),
        },

        FocusNetwork => new[]
        {
            new GuidePage("断网档位",
                "不断网：不修改网络。\n\n" +
                "温和档：只屏蔽「要屏蔽的网站」名单（写入 hosts，需要管理员权限）。\n\n" +
                "狠人档：直接禁用网卡，整机断网（需要管理员权限）。\n\n" +
                "断网档位在中场休息期间同样生效。"),
        },

        RulesHub => new[]
        {
            new GuidePage("名单管理",
                "「名单管理」分为三块：\n\n" +
                "黑名单：自律进行中拦截清单内的程序。\n" +
                "白名单：白名单模式下只允许清单内程序及其启动链路运行。\n" +
                "要屏蔽的网站：配合断网档位的温和档 / 狠人档使用。\n\n" +
                "系统核心、内置组件和 Windows 系统目录始终放行，不会把电脑锁死。"),
        },

        RulesBlack => new[]
        {
            new GuidePage("黑名单",
                "黑名单里的程序会在自律进行中（中场休息除外）被拦截。\n\n" +
                "不要加入系统核心、输入法、显卡、音频、杀软组件；系统核心、内置组件和 Windows 系统目录始终放行，" +
                "不会把电脑锁死。\n\n" +
                "拿不准会不会误伤时，先点「白名单体检」导出报告看看哪些有窗口的程序会被拦截。"),
        },

        RulesWhite => new[]
        {
            new GuidePage("白名单模式",
                "白名单模式只允许名单内的程序及其启动链路的子程序运行；只拦截有窗口的程序，不会拦截后台系统组件。\n\n" +
                "系统核心、输入法 / 显卡 / 音频 / 杀软等内置组件和 Windows 系统目录始终放行，不会把电脑锁死。\n\n" +
                "名单只在自律进行中生效，中场休息会暂停应用屏蔽。名单支持 Ctrl / Shift 多选、批量移除；" +
                "添加程序时也可以多选。\n\n" +
                "一键白名单：扫描当前运行的程序批量加入白名单（自动去重）；系统核心、内置组件和系统目录里的程序" +
                "本来就会放行，不会重复添加。\n\n" +
                "如果拿不准会不会误伤，先点「白名单体检」导出报告看看哪些有窗口的程序会被拦截。"),
        },

        RulesSites => new[]
        {
            new GuidePage("要屏蔽的网站",
                "每行一个域名，可以带 https://。\n\n" +
                "网站名单只在断网档位选择「温和档」或「狠人档」时生效；写入 hosts 需要管理员权限。"),
        },

        Tasks => new[]
        {
            new GuidePage("任务清单",
                "自律时可以给每一轮选择任务（可多选、可跨轮复用），结束后进度与时长会按轮累计到这里。\n\n" +
                "批量添加时每行一个任务名，预计回合默认 1；之后可以在自律页给每轮选择任务。"),
        },

        Stats => new[]
        {
            new GuidePage("自律统计",
                "只有完整完成的自律才计入自律次数；被中断的记录会保留下来，但不计数。\n\n" +
                "历史记录和任务累计分别点入查看。"),
        },

        StatsHistory => new[]
        {
            new GuidePage("历史记录",
                "这里保留最近 200 条自律记录，包括完整完成和中断的记录；中断记录不计入自律次数。"),
        },

        StatsTaskAwards => new[]
        {
            new GuidePage("任务累计",
                "关联了任务的自律结束后，完成轮数和专注时长会累计到对应任务上，方便查看每个任务的投入。"),
        },

        SettingsHub => new[]
        {
            new GuidePage("系统设置",
                "「系统设置」分为两个子界面：\n\n" +
                "外观与启动：预设主题、界面字体与背景色、开机自动启动、最小化到托盘。\n" +
                "密码与防改动：设置密码、生成恢复码、安全问题，以及忘记密码时的重置入口。\n\n" +
                "下方还可以打开数据目录、日志目录，或申请以管理员身份重启。"),
        },

        SettingsAppearance => new[]
        {
            new GuidePage("外观与启动",
                "主题：十套预设配色（浅色 / 深色各有选择），点卡片立即生效；" +
                "还可以单独选择界面字体（默认等线，字体列表只保留中文字体）、窗口背景色。侧栏底部的「自定义主题」按钮也能打开同一个界面。\n\n" +
                "自定义背景色时，面板、输入框和边框会自动推导，正文自动选择深 / 浅色，强调色跟随所选预设。\n\n" +
                "开机自动启动：使用计划任务，登录时静默提权，不弹 UAC。\n\n" +
                "最小化到系统托盘：关闭窗口时留在后台继续计时。"),
        },

        SettingsPassword => new[]
        {
            new GuidePage("密码与防改动",
                "密码要求：至少 16 位，同时包含大写字母、小写字母、数字和特殊字符。\n\n" +
                "密码用于提前结束自律、修改设置与卸载。自律开始后无法更改密码，请开启自律前确保自己记住密码，以防紧急状况下无法退出。\n\n" +
                SessionExitPolicyText()),
        },

        SettingsRecovery => new[]
        {
            new GuidePage("恢复码",
                "恢复码有两个用途：忘记密码时重设密码（不限次数），或在密码冷却 / 冻结时提前结束自律（每天 1 次）。\n\n" +
                "生成后会用 DPAPI 加密保存在本机 data\\recovery-code.dat，可在本机查看并填入。\n\n" +
                "注意：恢复码文件放在 data 目录，跟着当前 Windows 用户走；换电脑或换用户后需要重新生成。"),
        },

        SettingsQuestions => new[]
        {
            new GuidePage("安全问题",
                "设置 3 个安全问题。忘记密码时，答对全部 3 个问题即可重设密码。\n\n" +
                "答案不区分大小写、忽略空格；答案只保存哈希，不会明文存储，所以重新保存时必须把 3 个答案都重填一遍。"),
        },

        SettingsForgot => new[]
        {
            new GuidePage("忘记密码",
                "已经设置恢复码或安全问题后，可以在这里分别用来重置密码。\n\n" +
                "重置密码不会解除当天的密码退出冷却 / 冻结，恢复码退出额度也不会被重置。\n\n" +
                "如果还没有设置对应方式，按钮会提示先去设置；两种方式只需要设置其中一种就可以作为后路。"),
        },

        _ => new[]
        {
            new GuidePage("使用说明", "点击右上角的帮助按钮，可以重新演示当前页面的使用方式，或查看文字版说明。"),
        },
    };

    public static string GetTopicTitle(string key) =>
        GetTopicTitle(key, MikoText.IsEnabled);

    /// <summary>指定是否要巫女口吻的标题；自检和帮助中心共用。</summary>
    public static string GetTopicTitle(string key, bool miko) =>
        miko ? MikoHelpContent.GetTopicTitle(key) : GetNormalTopicTitle(key);

    private static string GetNormalTopicTitle(string key) => key switch
    {
        Welcome => "欢迎使用 MikoBarrier",
        FocusHub => "自律结界",
        FocusQuick => "快速开始",
        FocusTask => "任务模式",
        FocusPolicy => "强制策略",
        FocusNetwork => "断网档位",
        RulesHub => "名单管理",
        RulesBlack => "黑名单",
        RulesWhite => "白名单模式",
        RulesSites => "要屏蔽的网站",
        Tasks => "任务清单",
        Stats => "自律统计",
        StatsHistory => "历史记录",
        StatsTaskAwards => "任务累计",
        SettingsHub => "系统设置",
        SettingsAppearance => "外观与启动",
        SettingsPassword => "密码与防改动",
        SettingsRecovery => "恢复码",
        SettingsQuestions => "安全问题",
        SettingsForgot => "忘记密码",
        _ => "使用说明",
    };

    public static string GetFullTextHelp()
    {
        var builder = new StringBuilder();
        foreach (var key in TextHelpTopicOrder)
        {
            AppendTopic(builder, key, includeTitle: true);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendTopic(StringBuilder builder, string key, bool includeTitle)
    {
        if (includeTitle)
        {
            builder.AppendLine($"◆ {GetTopicTitle(key)}");
        }

        foreach (var page in GetPages(key))
        {
            builder.AppendLine();
            builder.AppendLine($"  {page.Title}");
            builder.AppendLine(page.Body.Trim());
        }

        builder.AppendLine();
    }

    private static string SessionExitPolicyText() =>
        "提前结束规则：" + SessionExitPolicy.PasswordRuleSummary;
}
