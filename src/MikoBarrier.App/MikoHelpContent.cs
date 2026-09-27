namespace MikoBarrier;

/// <summary>
/// 巫女模式专用的引导 / 帮助正文：逐条重写为二次元日式巫女口吻，保留全部功能信息。
/// 普通模式完全不受影响，仍使用 HelpContent 里的原版文案。
/// </summary>
public static class MikoHelpContent
{
    private const string ExitPolicyText =
        "提前收工的规矩：暗号每天只能用 3 次；第 1 次成功后冷却 1 小时，第 2 次成功后冷却 2 小时，" +
        "第 3 次当天直接冻结，次日 0 点重开额度。恢复码可以绕过冷却，但每天只有 1 次；" +
        "重设暗号不会解除当天的冷却或冻结。";

    public static string GetTopicTitle(string key) => key switch
    {
        HelpContent.Welcome => "欢迎来到巫女结界",
        HelpContent.FocusHub => "结界修行",
        HelpContent.FocusQuick => "先挑个时辰",
        HelpContent.FocusTask => "多轮修行",
        HelpContent.FocusPolicy => "巫女的手段",
        HelpContent.FocusNetwork => "断网结界",
        HelpContent.RulesHub => "名册管理",
        HelpContent.RulesBlack => "黑名单的用法",
        HelpContent.RulesWhite => "白名单的规矩",
        HelpContent.RulesSites => "要封的网站",
        HelpContent.Tasks => "功课清单",
        HelpContent.Stats => "修行战绩",
        HelpContent.StatsHistory => "旧账",
        HelpContent.StatsTaskAwards => "功课累计",
        HelpContent.SettingsHub => "结界设置",
        HelpContent.SettingsAppearance => "衣装与开门",
        HelpContent.SettingsPassword => "暗号与防拆",
        HelpContent.SettingsRecovery => "恢复码的说明",
        HelpContent.SettingsQuestions => "安全问答",
        HelpContent.SettingsForgot => "忘了暗号",
        _ => "巫女的使用说明",
    };

    public static IReadOnlyList<GuidePage> GetPages(string key) => key switch
    {
        HelpContent.Welcome => new[]
        {
            new GuidePage("欢迎来到巫女结界",
                "哦呀，是你把 MikoBarrier 打开的？\n\n" +
                "这里是我看着的结界，专门把你从偷懒、分心和乱七八糟的念头里拽回来。" +
                "结界一旦展开，名册和手段都会锁死，想提前收工只能乖乖报暗号。\n\n" +
                "先点「继续」，巫女把这里的规矩讲给你听。"),
            new GuidePage("MikoBarrier 是什么（巫女版）",
                "MikoBarrier 是强制自律的类番茄钟：用轮次修行、应用屏蔽、键盘封印、全屏计时和断网结界，" +
                "把注意力按回正事上。\n\n" +
                "放心，我不会把你锁死：关机、注销一律放行；修行中也能用暗号、恢复码或安全问题退场。" +
                "不过动手前先把正在写的东西保存好，强杀进程可不会帮你留文档。\n\n" +
                "还有，结界展开后改不了暗号，最好先把恢复码和安全问题备齐。真是的，等出事了才想起来就太晚了。"),
        },

        HelpContent.FocusHub => new[]
        {
            new GuidePage("结界修行",
                "「结界修行」里有四扇门：\n\n" +
                "立刻开始：挑个时辰，直接展开单轮结界。\n" +
                "多轮修行：安排轮数、每轮时辰、中场茶歇和功课。\n" +
                "巫女的手段：应用屏蔽、白名单、键盘封印、全屏计时都在这里。\n" +
                "断网结界：不断网、温柔档或铁腕档，自己选。\n\n" +
                "配置好了就点右边的「展开结界」。开始后名册和手段都会锁死，想提前收工得报暗号——别想着偷偷改。"),
        },

        HelpContent.FocusQuick => new[]
        {
            new GuidePage("先挑个时辰",
                "选一个时辰就能开始单轮修行：5 分钟、25 分钟、40 分钟、60 分钟，或者自己定 1 到 120 分钟。\n\n" +
                "结界展开后名册与手段就锁死了，想提前收工只能报暗号。" +
                "中场茶歇会暂时放开应用屏蔽，但断网结界不会松开。\n\n" +
                "挑好时间，点右边「展开结界」就行——别拖，拖久了时辰也不会变长。"),
        },

        HelpContent.FocusTask => new[]
        {
            new GuidePage("轮数与茶歇",
                "想把事情分成几段做，就用多轮修行。\n\n" +
                "轮数可以选 1 到 12 轮；只有多轮时才能设置中场茶歇（5 或 10 分钟）。\n\n" +
                "每一轮都能挑功课，可以多选、跨轮复用，也可以一轮同时绑好几项。" +
                "同一轮绑多项时，该轮时辰和回合会按功课数量均分；一项都不绑就只记进总战绩。"),
            new GuidePage("统一时辰与统一功课",
                "默认是每一轮单独安排。\n\n" +
                "勾「每一轮都用同样的时辰」，就能给所有轮次选同一个时辰：" +
                "5 / 25 / 40 / 60 分钟，或 1 到 120 分钟自定义。\n\n" +
                "勾「所有轮次共用同一份功课」，再点下面的统一任务按钮，挑一组功课应用到所有轮次；" +
                "取消勾选就回到逐轮安排。"),
            new GuidePage("挑本轮的功课",
                "按住 Ctrl 或 Shift 可以多选：同一轮能绑多项功课；一项都不选，就是本轮不绑。\n\n" +
                "选错了就点「本轮重选」，把这一轮清干净重新挑。"),
        },

        HelpContent.FocusPolicy => new[]
        {
            new GuidePage("巫女的手段",
                "应用屏蔽：照「名册管理」里的名单办事，修行中把名单上的家伙挡住。\n\n" +
                "白名单：只放行名册里的程序跟它们带起来的小弟；系统核心、内置组件和系统目录永远放行，" +
                "免得把你的电脑锁死。\n\n" +
                "键盘封印：修行中吞掉 Alt+Tab / Alt+Esc / Alt+F4 / Ctrl+Esc / Ctrl+Shift+Esc；" +
                "Win 键留给你，Win+V 剪贴板和 Win+空格 输入法都能用。Ctrl+Alt+Del 是系统安全键，谁也拦不住；" +
                "真要紧急离开，就用暗号结束修行。\n\n" +
                "全屏计时：结界展开时一直挂在屏幕上，不影响你用别的程序。中场茶歇会自动放开应用屏蔽，" +
                "回到修行再锁上。\n\n" +
                "全屏计时右下角的「风景」能换背景；主卡片点右上角「缩起来」就只留顶部计时条，让背景图看个够。"),
        },

        HelpContent.FocusNetwork => new[]
        {
            new GuidePage("断网结界",
                "不断网：什么都不改。\n\n" +
                "温柔档：只把「要封的网站」写进 hosts，需要管理员权限。\n\n" +
                "铁腕档：直接禁用网卡，整机断网，同样需要管理员权限。\n\n" +
                "断网结界在中场茶歇时也不会松开——选之前想清楚，别到时候哭着找网。"),
        },

        HelpContent.RulesHub => new[]
        {
            new GuidePage("名册管理",
                "「名册管理」分成三本账：\n\n" +
                "黑名单：修行中拦住清单上的程序。\n" +
                "白名单：白名单模式下，只放行清单里的程序跟它们带起来的小弟。\n" +
                "要封的网站：配合断网结界的温柔档 / 铁腕档使用。\n\n" +
                "放心，系统核心、内置组件和 Windows 系统目录永远放行，不会让你连桌面都回不去。"),
        },

        HelpContent.RulesBlack => new[]
        {
            new GuidePage("黑名单",
                "黑名单里的程序，在修行中会被挡住（中场茶歇除外）。\n\n" +
                "别把系统核心、输入法、显卡、音频、杀软这些塞进去——不是它们拦不住，是拦了容易把电脑弄瘸。" +
                "系统核心、内置组件和 Windows 系统目录本来就会放行。\n\n" +
                "拿不准会不会误伤，就先点「白名单体检（写报告）」导出报告，看看哪些有窗口的程序会被挡。"),
        },

        HelpContent.RulesWhite => new[]
        {
            new GuidePage("白名单的规矩",
                "白名单模式只放行名册里的程序，以及它们启动链路上的小弟；而且只挡有窗口的程序，" +
                "后台系统组件不会被乱杀。\n\n" +
                "系统核心、输入法 / 显卡 / 音频 / 杀软这些内置组件，还有 Windows 系统目录里的程序，永远放行，" +
                "不会把电脑锁死。\n\n" +
                "名单只在修行中生效，中场茶歇会暂停应用屏蔽。名单支持 Ctrl / Shift 多选和批量移除，" +
                "添加程序时也能多选。\n\n" +
                "「一键放行」会扫描当前运行的程序，批量加进白名单并自动去重；系统核心、内置组件和系统目录里的程序" +
                "本来就会放行，不会重复添加。\n\n" +
                "拿不准就先跑「白名单体检（写报告）」，别等被挡了才来问巫女。"),
        },

        HelpContent.RulesSites => new[]
        {
            new GuidePage("要封的网站",
                "一行写一个域名，可以带 https://。\n\n" +
                "网站名单只在断网结界选「温柔档」或「铁腕档」时生效；写 hosts 需要管理员权限。" +
                "乱写可没用，巫女会检查的。"),
        },

        HelpContent.Tasks => new[]
        {
            new GuidePage("功课清单",
                "修行时可以给每一轮挑功课，支持多选和跨轮复用；修行结束后，轮数和时辰会累计到对应功课上。\n\n" +
                "批量添加时一行写一项功课，预计回合默认 1；之后可以在「多轮修行」里给每轮挑功课。\n\n" +
                "功课名自己想清楚，巫女可不负责帮你写作业。"),
        },

        HelpContent.Stats => new[]
        {
            new GuidePage("修行战绩",
                "只有完整完成的一次修行才算进自律次数；被中断的记录会留档，但不计数。\n\n" +
                "旧账和功课累计点进去看。别拿中断的记录来糊弄我哦。"),
        },

        HelpContent.StatsHistory => new[]
        {
            new GuidePage("旧账",
                "这里留着最近 200 条修行记录，包括完整完成的，也包括半途而废的；" +
                "中断记录不计入次数——成绩单可不会说谎。"),
        },

        HelpContent.StatsTaskAwards => new[]
        {
            new GuidePage("功课累计",
                "绑了功课的修行结束后，完成的轮数和时辰会累计到对应功课上，方便你看每项功课到底投入了多少。" +
                "要是效果不好，自己反思，别怪结界。"),
        },

        HelpContent.SettingsHub => new[]
        {
            new GuidePage("结界设置",
                "「结界设置」分两处：\n\n" +
                "衣装与开门：主题、界面字体与背景色、界面模式、开机自启、最小化到托盘。\n" +
                "暗号与防拆：设置暗号、生成恢复码、安全问题，还有忘记暗号时的重设入口。\n\n" +
                "下面还能打开神社仓库、日志卷轴，或者申请以管理员身份重启。"),
        },

        HelpContent.SettingsAppearance => new[]
        {
            new GuidePage("衣装与开门",
                "衣装：十一套预设配色（浅色 / 深色都有），点卡片立刻换；巫女模式会自动套用「真·巫女」白底朱红主题，" +
                "也可以自己在主题对话框里换。界面字体默认等线，只列中文字体；窗口背景色也能单独调。" +
                "侧栏底部的「自定义主题」也能打开同一个地方。\n\n" +
                "自定义背景色时，面板、输入框和边框会自动推导，正文自动选深 / 浅色，强调色跟着所选预设。\n\n" +
                "界面模式：普通模式和巫女模式可以切换；切到巫女模式会自动换「真·巫女」主题。\n\n" +
                "开机自动启动：用计划任务，登录时静默提权，不弹 UAC。\n\n" +
                "最小化到系统托盘：关窗口也继续在后台计时。"),
        },

        HelpContent.SettingsPassword => new[]
        {
            new GuidePage("暗号与防拆",
                "暗号要求：至少 16 位，大小写字母、数字、特殊字符都要有；巫女可不会通融。\n\n" +
                "暗号用于提前收工、修改设置和卸载。结界展开后改不了暗号，所以开始前先记牢；" +
                "出了紧急状况别怪没人提醒。\n\n" + ExitPolicyText),
        },

        HelpContent.SettingsRecovery => new[]
        {
            new GuidePage("恢复码",
                "恢复码有两个用途：忘了暗号时重设暗号（不限次数），或者在暗号冷却 / 冻结时提前收工（每天 1 次）。\n\n" +
                "生成后用 DPAPI 加密保存在本机 data\\recovery-code.dat，可以在本机查看并填入。\n\n" +
                "听好：恢复码文件跟着当前 Windows 用户走；换电脑或换用户之后，得重新生成一个。"),
        },

        HelpContent.SettingsQuestions => new[]
        {
            new GuidePage("安全问题",
                "设置 3 个安全问题。忘了暗号时，3 个问题全答对就能重设暗号。\n\n" +
                "答案不看大小写、空格也不算；只保存哈希，不存原文——所以重新保存时，" +
                "3 个问题和 3 个答案都得再填一遍。"),
        },

        HelpContent.SettingsForgot => new[]
        {
            new GuidePage("忘了暗号",
                "设过恢复码或安全问题之后，可以在这里分别重置暗号。\n\n" +
                "重置暗号不会解除当天的暗号退出冷却 / 冻结，恢复码的每日额度也不会重置。\n\n" +
                "要是两样都还没设，按钮会提醒你先去设置；设其中一种就能当后路。" +
                "真是的，别等到忘了暗号才来求我。"),
        },

        _ => new[]
        {
            new GuidePage("使用说明",
                "点标题栏右上角的问号，可以重新演示当前页的引导，或查看文字版说明。"),
        },
    };
}
