using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;
using MikoBarrier.Dialogs;

namespace MikoBarrier.Views;

public partial class RulesView : UserControl, IGuidedView
{
    private enum RulesPage
    {
        Hub,
        Black,
        White,
        Sites,
    }

    private bool _loaded;
    private RulesPage _page = RulesPage.Hub;

    public RulesView()
    {
        InitializeComponent();
        ShowRulesPage(RulesPage.Hub);
        Loaded += (_, _) =>
        {
            if (!_loaded)
            {
                _loaded = true;
                Refresh();
            }
        };
    }

    private Window? HostWindow => Window.GetWindow(this);

    public string CurrentGuideKey => _page switch
    {
        RulesPage.Black => HelpContent.RulesBlack,
        RulesPage.White => HelpContent.RulesWhite,
        RulesPage.Sites => HelpContent.RulesSites,
        _ => HelpContent.RulesHub,
    };

    public void ReplayCurrentGuide() =>
        GuideHooks.ShowOnFirstVisit(HostWindow, CurrentGuideKey, force: true);

    private void HubBlack_Click(object sender, RoutedEventArgs e) => ShowRulesPage(RulesPage.Black);

    private void HubWhite_Click(object sender, RoutedEventArgs e) => ShowRulesPage(RulesPage.White);

    private void HubSites_Click(object sender, RoutedEventArgs e) => ShowRulesPage(RulesPage.Sites);

    private void BackToRulesHub_Click(object sender, RoutedEventArgs e) => ShowRulesPage(RulesPage.Hub);

    private void ShowRulesPage(RulesPage page)
    {
        _page = page;
        var hubVisible = page == RulesPage.Hub;
        RulesHubPanel.Visibility = hubVisible ? Visibility.Visible : Visibility.Collapsed;
        RulesDetailPanel.Visibility = hubVisible ? Visibility.Collapsed : Visibility.Visible;

        BlackPagePanel.Visibility = page == RulesPage.Black ? Visibility.Visible : Visibility.Collapsed;
        WhitePagePanel.Visibility = page == RulesPage.White ? Visibility.Visible : Visibility.Collapsed;
        SitesPagePanel.Visibility = page == RulesPage.Sites ? Visibility.Visible : Visibility.Collapsed;

        if (page == RulesPage.Black)
        {
            MoveAddToolbar(BlackToolbarHost);
            RulesDetailTitleText.Text = MikoText.T("黑名单");
            RulesDetailHintText.Text = MikoText.T("自律进行中会拦截清单内的程序；中场休息除外。");
            Refresh();
            GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.RulesBlack);
        }
        else if (page == RulesPage.White)
        {
            MoveAddToolbar(WhiteToolbarHost);
            RulesDetailTitleText.Text = MikoText.T("白名单");
            RulesDetailHintText.Text = MikoText.T("白名单模式只允许名单内程序及其启动链路运行；只拦截有窗口的程序。");
            Refresh();
            GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.RulesWhite);
        }
        else if (page == RulesPage.Sites)
        {
            RulesDetailTitleText.Text = MikoText.T("要屏蔽的网站");
            RulesDetailHintText.Text = MikoText.T("每行一个域名，可带 https://；配合断网档位的温和档 / 狠人档生效。");
            Refresh();
            GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.RulesSites);
        }
    }

    private void MoveAddToolbar(ContentControl host)
    {
        if (ReferenceEquals(host.Content, AddProgramToolbar))
        {
            return;
        }

        if (ReferenceEquals(BlackToolbarHost.Content, AddProgramToolbar))
        {
            BlackToolbarHost.Content = null;
        }

        if (ReferenceEquals(WhiteToolbarHost.Content, AddProgramToolbar))
        {
            WhiteToolbarHost.Content = null;
        }

        host.Content = AddProgramToolbar;
    }

    public void Refresh()
    {
        var settings = App.State.Settings;

        var black = settings.Rules.Where(r => r.Kind == RuleKind.Blacklist).ToList();
        var white = settings.Rules.Where(r => r.Kind == RuleKind.Whitelist).ToList();

        BlackList.ItemsSource = black;
        WhiteList.ItemsSource = white;
        BlackTitle.Text = MikoText.T($"黑名单（{black.Count}）");
        WhiteTitle.Text = MikoText.T($"白名单（{white.Count}）");

        SitesBox.Text = string.Join(Environment.NewLine, settings.BlockedSites);
        SitesHint.Text = string.Empty;
    }

    private void AddRunningButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppPickerDialog();
        var owner = HostWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true || dialog.SelectedApps.Count == 0)
        {
            return;
        }

        var count = dialog.SelectedApps.Count;
        if (!RequirePassword(count > 1 ? $"批量新增 {count} 条规则" : "新增规则"))
        {
            return;
        }

        var kind = dialog.SelectedKind;
        var rules = dialog.SelectedApps
            .Select(app => new AppRule
            {
                Kind = kind,
                Target = RuleTarget.File,
                Path = app.FilePath,
                DisplayName = app.ProcessName,
                Note = app.WindowTitle,
            })
            .ToList();

        AddRules(rules);

        if (count == 1)
        {
            OfferParentFolder(dialog.SelectedApps[0].FilePath, kind);
        }
    }

    private void AddExeButton_Click(object sender, RoutedEventArgs e)
    {
        var fileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要加入名单的程序（可多选）",
            Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };

        if (fileDialog.ShowDialog(HostWindow) != true || fileDialog.FileNames.Length == 0)
        {
            return;
        }

        var count = fileDialog.FileNames.Length;
        if (!RequirePassword(count > 1 ? $"批量新增 {count} 条规则" : "新增规则"))
        {
            return;
        }

        var firstName = Path.GetFileNameWithoutExtension(fileDialog.FileNames[0]);
        var kind = ChooseKind(count == 1
            ? $"把「{firstName}」加入哪个名单？\n\n黑名单 = 自律时拦截\n白名单 = 自律时只允许名单内程序"
            : $"把选中的 {count} 个程序加入哪个名单？\n\n黑名单 = 自律时拦截\n白名单 = 自律时只允许名单内程序");
        if (kind is null)
        {
            return;
        }

        var rules = fileDialog.FileNames
            .Select(path => new AppRule
            {
                Kind = kind.Value,
                Target = RuleTarget.File,
                Path = path,
                DisplayName = Path.GetFileNameWithoutExtension(path),
            })
            .ToList();

        AddRules(rules);

        if (count == 1)
        {
            OfferParentFolder(fileDialog.FileNames[0], kind.Value);
        }
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要加入名单的文件夹（该目录下的程序都会被处理）",
        };

        if (folderDialog.ShowDialog(HostWindow) != true)
        {
            return;
        }

        if (!RequirePassword("新增规则")) { return; }

        var path = folderDialog.FolderName;
        var name = Path.GetFileName(path.TrimEnd('\\'));
        var kind = ChooseKind($"把文件夹「{name}」加入哪个名单？\n\n黑名单 = 目录内程序会被拦截\n白名单 = 只允许目录内程序");
        if (kind is null)
        {
            return;
        }

        AddRule(new AppRule
        {
            Kind = kind.Value,
            Target = RuleTarget.Folder,
            Path = path,
            DisplayName = $"{name}\\（文件夹）",
        });
    }

    /// <summary>
    /// 自检：把当前运行的进程按名单跑一遍，看看谁会真的被拦；
    /// 可以顺手点"立即拦截"验证规则到底有没有生效。
    /// </summary>
    private void DiagnoseButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        var plan = App.State.Engine.Plan ?? settings.LastPlan;
        var probePlan = new FocusPlan { BlockApplications = true, UseWhitelistMode = plan.UseWhitelistMode };

        var snapshot = ProcessPatrol.Snapshot();
        var decisions = ProcessPatrol.Sweep(settings, probePlan, snapshot);

        var lines = new List<string>
        {
            $"名单共 {settings.Rules.Count} 条（黑名单 {settings.Rules.Count(r => r.Kind == RuleKind.Blacklist)}、白名单 {settings.Rules.Count(r => r.Kind == RuleKind.Whitelist)}）",
            $"测试模式：{(plan.UseWhitelistMode ? "白名单" : "黑名单")}    按文件名匹配：{(settings.MatchByFileName ? "开" : "关")}",
            string.Empty,
        };

        if (decisions.Count == 0)
        {
            lines.Add("当前运行的进程里，没有匹配名单的程序。");
        }
        else
        {
            lines.Add($"当前会被拦截的程序（{decisions.Count} 个）：");
            lines.AddRange(decisions.Take(10).Select(d => $" {d.Name}   {d.FilePath}"));
            if (decisions.Count > 10)
            {
                lines.Add($" 还有 {decisions.Count - 10} 个");
            }
        }

        lines.Add(string.Empty);
        lines.Add($"另有 {ProcessPatrol.UnknownPathCount} 个进程读不到路径（游戏 / 反作弊常见，建议以管理员身份运行）。");

        AppMessage.Info(HostWindow, string.Join("\n", lines), "拦截规则自检");

        if (decisions.Count > 0 && AppMessage.Confirm(HostWindow,
                $"要立即拦截这 {decisions.Count} 个程序吗？（用来验证规则是否真的生效）",
                "拦截规则自检", okText: "立即拦截", cancelText: "不用"))
        {
            var handled = ProcessPatrol.Execute(decisions);
            AppMessage.Info(HostWindow,
                $"已处理 {handled.Count} / {decisions.Count} 个程序。\n如果数字对不上，说明权限不足（请到设置页以管理员身份重启）。",
                "拦截结果");
        }
    }

    private void SelectAllRules_Click(object sender, RoutedEventArgs e) => CurrentRuleList().SelectAll();

    private void ClearRulesSelection_Click(object sender, RoutedEventArgs e) => CurrentRuleList().UnselectAll();

    private ListBox CurrentRuleList() =>
        _page == RulesPage.White ? WhiteList : BlackList;

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {        var selected = BlackList.SelectedItems
            .Cast<AppRule>()
            .Concat(WhiteList.SelectedItems.Cast<AppRule>())
            .GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (selected.Count == 0)
        {
            AppMessage.Info(HostWindow, "请先在列表中选择规则（按住 Ctrl / Shift 可以多选）。");
            return;
        }

        if (!RequirePassword(selected.Count > 1 ? $"批量移除 {selected.Count} 条规则" : "移除规则"))
        {
            return;
        }

        var keys = selected.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var settings = App.State.Settings;
        settings.Rules.RemoveAll(r => keys.Contains(r.Key));
        App.State.Save();
        Refresh();
    }

    private void SaveSitesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePassword("保存网站名单")) { return; }

        var sites = SitesBox.Text
            .Split(new[] { '\r', '\n', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        App.State.Settings.BlockedSites = sites;
        App.State.Save();
        SitesHint.Text = MikoText.T($"已保存 {sites.Count} 个域名");
    }

    /// <summary>用自绘对话框问"加黑名单还是白名单"。</summary>
    private RuleKind? ChooseKind(string message)
    {
        var dialog = new ChoiceDialog("加入名单", message, "加入黑名单", "加入白名单", "取消");
        var owner = HostWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() switch
        {
            true => RuleKind.Blacklist,
            false => RuleKind.Whitelist,
            _ => null,
        };
    }

    /// <summary>
    /// 加入单个 exe 后，主动问一句"要不要把整个文件夹也加进来"。
    /// 因为大量程序是启动器拉起来的（游戏 / 更新器），只加启动器本体拦不住本体。
    /// </summary>
    private void OfferParentFolder(string filePath, RuleKind kind)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        var folderName = Path.GetFileName(folder.TrimEnd('\\'));
        var kindName = kind == RuleKind.Blacklist ? "黑名单" : "白名单";

        if (!AppMessage.Confirm(HostWindow,
                $"要不要把「{folderName}」整个文件夹也加入{kindName}？\n\n" +
                "很多程序是启动器拉起来的（比如游戏），只加启动器本体拦不住游戏本体。\n" +
                $"加入后会拦截该目录及其子目录下的所有程序：\n{folder}",
                "MikoBarrier", okText: "连文件夹一起加", cancelText: "只加这个程序"))
        {
            return;
        }

        AddRule(new AppRule
        {
            Kind = kind,
            Target = RuleTarget.Folder,
            Path = folder,
            DisplayName = folderName + "\\（文件夹）",
        });
    }

    /// <summary>改名单需要密码（防改动）。没设密码就直接放行。</summary>
    private bool RequirePassword(string action)
    {
        var settings = App.State.Settings;
        if (!settings.HasPassword)
        {
            return true;
        }

        var dialog = new PasswordDialog("修改名单需要密码（" + action + "）：")
        {
            Validator = password => PasswordService.VerifyPassword(settings, password),
        };

        var owner = HostWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// 一键白名单：把当前运行、能读到路径、且尚未被白名单覆盖的程序批量加入。
    /// 先算清楚要加多少条，再走密码门禁；取消密码不会修改任何配置。
    /// </summary>
    private void OneKeyWhitelistButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        var apps = ProcessCatalog.GetRunningApps();
        var (newRules, alreadyCovered, systemSkipped) = ProcessCatalog.BuildWhitelistBatch(settings, apps);

        if (newRules.Count == 0)
        {
            AppMessage.Info(HostWindow,
                $"没有需要新增的白名单。\n\n扫描到 {apps.Count} 个运行中的程序；" +
                $"{systemSkipped} 个系统组件自动放行，{alreadyCovered} 个已覆盖或重复。",
                "一键白名单");
            return;
        }

        // 专用预览窗口：底部按钮固定 + 列表可滚动 + 名称优先（路径按需展开），小屏也不会把按钮顶出去。
        var preview = new WhitelistPreviewDialog(newRules, apps.Count, alreadyCovered, systemSkipped);
        var owner = HostWindow;
        if (owner is not null)
        {
            preview.Owner = owner;
        }

        if (preview.ShowDialog() != true)
        {
            return;
        }

        if (!RequirePassword($"一键加入 {newRules.Count} 条白名单"))
        {
            return;
        }

        settings.Rules.AddRange(newRules);
        App.State.Save();
        Refresh();

        AppMessage.Info(HostWindow,
            $"已加入 {newRules.Count} 个程序到白名单。\n\n" +
            "白名单模式只拦截有窗口、且不在名单里的程序；系统组件与白名单程序的启动链路会自动放行。",
            "一键白名单");
    }

    /// <summary>
    /// 白名单体检：按白名单模式扫描当前运行的程序并导出报告，方便在“脏”电脑上确认不会误伤。
    /// 只读，不需要密码，也不修改任何配置（最多在内存里补齐内置保护默认项）。
    /// </summary>
    private void AuditWhitelistButton_Click(object sender, RoutedEventArgs e)
    {
        WhitelistAuditResult result;
        try
        {
            result = WhitelistAudit.Run(App.State.Settings);
        }
        catch (Exception ex)
        {
            AppMessage.Error(HostWindow, $"白名单体检失败：{ex.GetType().Name}: {ex.Message}", "白名单体检");
            return;
        }

        var reportPath = Path.Combine(StoragePaths.LogDir, "whitelist-audit.txt");
        var saved = true;
        try
        {
            Directory.CreateDirectory(StoragePaths.LogDir);
            File.WriteAllText(reportPath, result.Report, new UTF8Encoding(false));
        }
        catch
        {
            saved = false;
        }

        var warnings = result.HostWarnings.Count == 0
            ? $"必要宿主自检：运行中 {result.HostPassed}/{result.HostRunning} 个放行，{result.HostAbsent} 个当前未运行（正常）。"
            : "⚠ 发现风险：" + string.Join("；", result.HostWarnings);

        AppMessage.Info(HostWindow,
            $"进程总数：{result.Total}\n" +
            $"系统核心放行：{result.CoreProtected}    内置组件：{result.ComponentExempt}    系统目录：{result.SystemDirectory}\n" +
            $"用户白名单：{result.UserWhitelisted}    启动链路：{result.ChainAllowed}\n\n" +
            $"有窗口、会被拦截：{result.BlockedWindowed} 个\n" +
            $"无窗口、不会拦截：{result.BackgroundSafe} 个\n" +
            $"读不到路径：{result.UnknownPath} 个\n\n" +
            warnings + "\n\n" +
            (saved ? $"完整报告已写入：\n{reportPath}" : "报告写入失败（可检查 logs 目录权限）。"),
            "白名单体检");
    }

    private void AddRule(AppRule rule) => AddRules(new List<AppRule> { rule });

    /// <summary>批量加入规则（只保存一次）；重复的规则跳过。</summary>
    private int AddRules(List<AppRule> rules)
    {
        var settings = App.State.Settings;
        var added = 0;
        var skipped = 0;

        foreach (var rule in rules)
        {
            if (settings.Rules.Any(r => r.Key == rule.Key))
            {
                skipped++;
                continue;
            }

            settings.Rules.Add(rule);
            added++;
        }

        if (added > 0)
        {
            App.State.Save();
            Refresh();
        }

        if (skipped > 0)
        {
            AppMessage.Info(HostWindow,
                skipped == rules.Count
                    ? "选中的规则都已经存在了。"
                    : $"有 {skipped} 条规则已存在，已自动跳过。");
        }

        return added;
    }
}