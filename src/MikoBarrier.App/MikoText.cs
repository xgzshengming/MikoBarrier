using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MikoBarrier.Controls;

namespace MikoBarrier;

/// <summary>
/// 巫女模式文本引擎：
/// - 静态 XAML 文本在窗口 Loaded 时被统一替换为巫女口吻，切回普通模式时按保存的原文还原；
/// - 代码动态设置的文本请使用 SetText / T，避免直接修改后无法随模式切换；
/// - 用户数据（任务名 / 路径 / 程序名）不会经过这里，翻译失败时原样保留。
/// </summary>
public static class MikoText
{
    private static readonly DependencyProperty OriginalTextProperty = DependencyProperty.RegisterAttached(
        "OriginalText", typeof(string), typeof(MikoText), new FrameworkPropertyMetadata(null));

    private static readonly DependencyProperty DisplayTextProperty = DependencyProperty.RegisterAttached(
        "DisplayText", typeof(string), typeof(MikoText), new FrameworkPropertyMetadata(null));

    private static readonly DependencyProperty OriginalContentProperty = DependencyProperty.RegisterAttached(
        "OriginalContent", typeof(string), typeof(MikoText), new FrameworkPropertyMetadata(null));

    private static readonly DependencyProperty DisplayContentProperty = DependencyProperty.RegisterAttached(
        "DisplayContent", typeof(string), typeof(MikoText), new FrameworkPropertyMetadata(null));

    private static readonly DependencyProperty OriginalTitleProperty = DependencyProperty.RegisterAttached(
        "OriginalTitle", typeof(string), typeof(MikoText), new FrameworkPropertyMetadata(null));

    private static readonly DependencyProperty DisplayTitleProperty = DependencyProperty.RegisterAttached(
        "DisplayTitle", typeof(string), typeof(MikoText), new FrameworkPropertyMetadata(null));

    private static bool _installed;

    /// <summary>当前是否启用巫女模式。读取 App.State，自检和单测中不依赖窗体。</summary>
    public static bool IsEnabled => App.State?.Settings.MikoModeEnabled == true;

    public static int MappingCount => MikoTextCatalog.Mappings.Count;

    public static bool HasMapping(string normal) =>
        MikoTextCatalog.Mappings.ContainsKey(normal);

    /// <summary>在所有窗口创建前调用一次，挂上 Window.Loaded 翻译钩子。</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        // 等模板和子控件都 Loaded 后再统一翻译，避免漏掉 Expand 状态里的静态文本。
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => ApplyTo(window)));
    }

    /// <summary>动态文本翻译：优先查表，查不到时做巫女口吻润色。</summary>
    public static string T(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        if (!IsEnabled)
        {
            return text;
        }

        return MikoTextCatalog.Mappings.TryGetValue(text, out var mapped)
            ? mapped
            : Flavorize(text);
    }

    /// <summary>只做精确查表翻译，不润色。供静态界面遍历使用，避免误伤用户数据。</summary>
    public static string TranslateStatic(string? text)
    {
        if (string.IsNullOrEmpty(text) || !IsEnabled)
        {
            return text ?? string.Empty;
        }

        return MikoTextCatalog.Mappings.TryGetValue(text, out var mapped) ? mapped : text;
    }

    /// <summary>设置动态 TextBlock 文本，同时记录原文，切换回普通模式时可精确还原。</summary>
    public static void SetText(TextBlock target, string normal)
    {
        target.SetValue(OriginalTextProperty, normal);
        var display = T(normal);
        target.SetValue(DisplayTextProperty, display);
        if (!string.Equals(target.Text, display, StringComparison.Ordinal))
        {
            target.Text = display;
        }
    }

    /// <summary>设置动态 ContentControl 文本（按钮等），同时记录原文。</summary>
    public static void SetContent(ContentControl target, string normal)
    {
        target.SetValue(OriginalContentProperty, normal);
        var display = T(normal);
        target.SetValue(DisplayContentProperty, display);
        if (target.Content is not string current || !string.Equals(current, display, StringComparison.Ordinal))
        {
            target.Content = display;
        }
    }

    /// <summary>遍历窗口 / 控件树，翻译静态文本并保存原文。</summary>
    public static void ApplyTo(DependencyObject? root)
    {
        if (root is null)
        {
            return;
        }

        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        Walk(root, visited);
    }

    /// <summary>切换模式后刷新所有已打开窗口的静态文本。</summary>
    public static void RefreshAllWindows()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        foreach (Window window in app.Windows)
        {
            ApplyTo(window);
        }
    }

    private static void Walk(DependencyObject node, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        ApplyElement(node);

        try
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is DependencyObject dependencyObject)
                {
                    Walk(dependencyObject, visited);
                }
            }
        }
        catch
        {
            // 某些 Freezable / 非逻辑节点不支持子级枚举，忽略。
        }

        if (node is Visual)
        {
            try
            {
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < count; i++)
                {
                    Walk(VisualTreeHelper.GetChild(node, i), visited);
                }
            }
            catch
            {
                // 模板尚未生成时 VisualTreeHelper 可能抛异常，忽略。
            }
        }
    }

    private static void ApplyElement(DependencyObject node)
    {
        if (node is TextBlock textBlock && !IsDataBound(textBlock, TextBlock.TextProperty))
        {
            ApplyString(
                textBlock,
                textBlock.Text,
                OriginalTextProperty,
                DisplayTextProperty,
                value => textBlock.Text = value);
        }

        if (node is ContentControl contentControl &&
            !IsDataBound(contentControl, ContentControl.ContentProperty) &&
            contentControl.Content is string content)
        {
            ApplyString(
                contentControl,
                content,
                OriginalContentProperty,
                DisplayContentProperty,
                value => contentControl.Content = value);
        }

        if (node is WindowTitleBar titleBar && !IsDataBound(titleBar, WindowTitleBar.TitleProperty))
        {
            ApplyString(
                titleBar,
                titleBar.Title,
                OriginalTitleProperty,
                DisplayTitleProperty,
                value => titleBar.Title = value);
        }

        if (node is Window window && !IsDataBound(window, Window.TitleProperty))
        {
            ApplyString(
                window,
                window.Title,
                OriginalTitleProperty,
                DisplayTitleProperty,
                value => window.Title = value);
        }
    }

    private static void ApplyString(
        DependencyObject owner,
        string? current,
        DependencyProperty originalProperty,
        DependencyProperty displayProperty,
        Action<string> assign)
    {
        current ??= string.Empty;
        var original = owner.GetValue(originalProperty) as string;
        var display = owner.GetValue(displayProperty) as string;

        if (original is null)
        {
            original = current;
        }
        else if (!string.Equals(current, display, StringComparison.Ordinal))
        {
            // 控件文本被代码直接改过：把新值当作原文，避免刷新时还原成旧内容。
            original = current;
        }

        var next = IsEnabled ? TranslateStatic(original) : original;
        owner.SetValue(originalProperty, original);
        owner.SetValue(displayProperty, next);

        if (!string.Equals(current, next, StringComparison.Ordinal))
        {
            assign(next);
        }
    }

    private static bool IsDataBound(DependencyObject target, DependencyProperty property) =>
        BindingOperations.IsDataBound(target, property);

    private static readonly (string From, string To)[] DynamicReplacements =
    {
        ("自律结界", "结界修行"),
        ("系统设置", "结界设定"),
        ("快速开始", "立刻开始"),
        ("任务模式", "多轮修行"),
        ("断网档位", "断网结界"),
        ("专注中", "结界展开中"),
        ("已完成", "已经搞定"),
        ("已中断", "已经中断"),
        ("中场休息", "中场茶歇"),
        ("自律统计", "修行战绩"),
        ("强制策略", "巫女的手段"),
        ("全屏计时", "全屏结界"),
        ("应用屏蔽", "干扰屏蔽"),
        ("强制自律", "强制结界"),
        ("开始后名单与策略会锁定", "结界展开后名册和手段就会锁死"),
        ("只能凭密码提前结束", "想提前收工只能报暗号"),
        ("使用帮助", "巫女的说明"),
        ("界面字体", "字型"),
        ("窗口背景色", "窗口底色"),
        ("重置密码", "换新暗号"),
        ("密码已重设", "暗号已经换好"),
        ("密码已", "暗号已经"),
        ("已保存", "已经记下"),
        ("已填", "已经填"),
        ("已生成", "已经生成"),
        ("专注", "修行"),
        ("自律", "结界"),
        ("密码", "暗号"),
        ("任务", "功课"),
        ("设置", "安排"),
        ("完成", "搞定"),
        ("保存", "记下"),
        ("删除", "抹掉"),
        ("如果", "要是"),
        ("是否", "要不要"),
        ("请先", "先"),
        ("请", "拜托"),
        ("需要", "得"),
        ("可以", "能"),
        ("无法", "没法"),
        ("尚未", "还没"),
        ("已获得", "已经拿到"),
        ("未获得", "还没拿到"),
        ("注意：", "听好："),
        ("建议", "巫女建议"),
        ("当前", "现在"),
    };

    /// <summary>对带变量的动态句子做保守替换，再加一点语气词；不改英文 / 路径。</summary>
    private static string Flavorize(string text)
    {
        if (!ContainsCjk(text))
        {
            return text;
        }

        var result = text;
        foreach (var (from, to) in DynamicReplacements)
        {
            result = result.Replace(from, to, StringComparison.Ordinal);
        }

        return AddTone(result);
    }

    private static string AddTone(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (text.EndsWith('。'))
        {
            var body = text[..^1];
            return EndsWithParticle(body) ? text : body + "哦。";
        }

        if (text.EndsWith('！') || text.EndsWith('？') || text.EndsWith('…') || text.EndsWith('.'))
        {
            return text;
        }

        if (text.Length <= 16 && !EndsWithParticle(text))
        {
            return text + "哦";
        }

        return text;
    }

    private static bool EndsWithParticle(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text[^1] is '哦' or '呢' or '呀' or '吧' or '啦' or '嘛' or '咯';
    }

    private static bool ContainsCjk(string text) =>
        text.Any(ch => ch >= '\u4e00' && ch <= '\u9fff');
}
