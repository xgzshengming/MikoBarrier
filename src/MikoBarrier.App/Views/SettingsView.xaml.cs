using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;
using MikoBarrier.Dialogs;

namespace MikoBarrier.Views;

public partial class SettingsView : UserControl, IGuidedView
{
    private enum SettingsPage
    {
        Hub,
        Appearance,
        Security,
        Password,
        Recovery,
        Questions,
        Forgot,
        RecoveryReset,
        QuestionReset,
    }

    private bool _firstPasswordSetup;
    private bool _updatingMikoMode;
    private SettingsPage _page = SettingsPage.Hub;

    public SettingsView()
    {
        InitializeComponent();
        ShowSettingsPage(SettingsPage.Hub);
        Loaded += (_, _) =>
        {
            Refresh();
            if (_page == SettingsPage.Hub)
            {
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.SettingsHub);
            }
        };
    }

    private Window? HostWindow => Window.GetWindow(this);

    public string CurrentGuideKey => _page switch
    {
        SettingsPage.Appearance => HelpContent.SettingsAppearance,
        SettingsPage.Password => HelpContent.SettingsPassword,
        SettingsPage.Security => HelpContent.SettingsPassword,
        SettingsPage.Recovery => HelpContent.SettingsRecovery,
        SettingsPage.Questions => HelpContent.SettingsQuestions,
        SettingsPage.Forgot => HelpContent.SettingsForgot,
        SettingsPage.RecoveryReset => HelpContent.SettingsForgot,
        SettingsPage.QuestionReset => HelpContent.SettingsForgot,
        _ => HelpContent.SettingsHub,
    };

    public void ReplayCurrentGuide() =>
        GuideHooks.ShowOnFirstVisit(HostWindow, CurrentGuideKey, force: true);

    public void Refresh()
    {
        var settings = App.State.Settings;

        ThemeSummaryText.Text = MikoText.T(ThemeManager.Describe(settings));
        AutoStartCheck.IsChecked = settings.AutoStart || ElevatedSetup.AreTasksInstalled();
        TrayCheck.IsChecked = settings.MinimizeToTray;
        RefreshMikoModeUi(settings);

        var savedCopy = RecoveryCodeStore.Exists
            ? "本机已保存 DPAPI 加密副本"
            : "本机尚未保存副本";
        PasswordHint.Text = string.Empty;

        SecurityStatusText.Text = MikoText.T(settings.HasPassword
            ? $"已设置密码（PBKDF2-SHA512 存储）。\n恢复码：{(settings.HasRecoveryCode ? "已生成" : "未生成，建议生成一个")}；{savedCopy}。\n安全问题：{(settings.HasSecurityQuestions ? "已设置" : "未设置，建议设置")}。"
            : "尚未设置密码：无法开始自律（没有密码就无法提前结束，太危险）。");

        RecoveryStatusText.Text = MikoText.T(settings.HasRecoveryCode
            ? $"已生成恢复码。{savedCopy}。\n恢复码用于重设密码（不限次数），也可在密码冷却 / 冻结时提前结束自律（每天 1 次）。"
            : "尚未生成恢复码。\n恢复码用于重设密码，也可在密码冷却 / 冻结时提前结束自律（每天 1 次）。");

        RecoveryHint.Text = string.Empty;

        var questions = settings.SecurityQuestions;
        Question1Box.Text = questions.Count > 0 ? questions[0].Question : string.Empty;
        Question2Box.Text = questions.Count > 1 ? questions[1].Question : string.Empty;
        Question3Box.Text = questions.Count > 2 ? questions[2].Question : string.Empty;
        Answer1Box.Clear();
        Answer2Box.Clear();
        Answer3Box.Clear();

        SecurityHint.Text = MikoText.T(settings.HasSecurityQuestions
            ? "已设置 3 个问题。忘记密码时可在「忘记密码」页用安全问题重设。"
            : "尚未设置安全问题（建议设置，和恢复码互为备份）。");

        ForgotStatusText.Text = MikoText.T(settings.HasPassword
            ? BuildForgotStatus(settings)
            : "还没有设置密码。请先设置密码，再配置安全问题。");

        QuestionReset1Text.Text = $"问题 1：{QuestionAt(questions, 0)}";
        QuestionReset2Text.Text = $"问题 2：{QuestionAt(questions, 1)}";
        QuestionReset3Text.Text = $"问题 3：{QuestionAt(questions, 2)}";
        QuestionResetAnswer1Box.Clear();
        QuestionResetAnswer2Box.Clear();
        QuestionResetAnswer3Box.Clear();
        QuestionResetNewPasswordBox.Clear();
        QuestionResetConfirmPasswordBox.Clear();
        QuestionResetHint.Text = string.Empty;
        RecoveryResetNewPasswordBox.Clear();
        RecoveryResetConfirmPasswordBox.Clear();
        ResetCodeBox.Clear();
        RecoveryResetHint.Text = string.Empty;

        DataPathText.Text = $"数据目录：{StoragePaths.Root}";
        EnvText.Text = $"运行权限：{(AdminHelper.IsElevated ? "管理员" : "普通用户（断网档位不可用）")}    " +
                       $"版本：{AppInfo.DisplayVersion}    .NET：{Environment.Version}    机器：{Environment.MachineName}";
    }

    private void RefreshMikoModeUi(AppSettings settings)
    {
        _updatingMikoMode = true;
        try
        {
            MikoModeRadio.IsEnabled = settings.MikoModeUnlocked;
            NormalModeRadio.IsChecked = !settings.MikoModeEnabled;
            MikoModeRadio.IsChecked = settings.MikoModeEnabled;

            UseMikoThemeButton.Visibility = settings.MikoModeUnlocked ? Visibility.Visible : Visibility.Collapsed;

            var hint = settings.MikoModeUnlocked
                ? "巫女模式已解锁，可随时切回普通模式。"
                : "巫女模式未解锁：全屏计时的角落里，也许藏着什么。";
            MikoModeHint.Text = MikoText.T(hint);
        }
        finally
        {
            _updatingMikoMode = false;
        }
    }

    private void UiMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingMikoMode)
        {
            return;
        }

        var settings = App.State.Settings;
        var wantMiko = MikoModeRadio.IsChecked == true;

        if (wantMiko == settings.MikoModeEnabled)
        {
            return;
        }

        if (wantMiko && !settings.MikoModeUnlocked)
        {
            _updatingMikoMode = true;
            NormalModeRadio.IsChecked = true;
            _updatingMikoMode = false;
            AppMessage.Warn(HostWindow, "巫女模式还没有解锁：全屏计时里藏着一次机会。");
            return;
        }

        if (!MikoModeService.TrySetEnabled(settings, wantMiko))
        {
            return;
        }

        ThemeManager.Apply(settings);
        App.State.Save();
        MikoText.RefreshAllWindows();
        Refresh();
    }

    private void UseMikoTheme_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        MikoModeService.ApplyMikoTheme(settings);

        ThemeManager.Apply(settings);
        App.State.Save();
        MikoText.RefreshAllWindows();
        Refresh();
    }

    private static string BuildForgotStatus(AppSettings settings)
    {
        if (settings.HasRecoveryCode && settings.HasSecurityQuestions)
        {
            return "恢复码和安全问题都已设置，可以选择任意一种方式重置密码。";
        }

        if (settings.HasRecoveryCode)
        {
            return "当前可用：恢复码重置。安全问题尚未设置，可先到「设置安全问题」里补齐。";
        }

        if (settings.HasSecurityQuestions)
        {
            return "当前可用：安全问题重置。恢复码尚未生成，可先到「生成 / 查看恢复码」里补齐。";
        }

        return "恢复码和安全问题都还没有设置。请先选择一种方式设置好，避免忘记密码后无法自助重置。";
    }

    private static string QuestionAt(IReadOnlyList<SecurityQuestion> questions, int index) =>
        index < questions.Count ? questions[index].Question : "（未设置）";

    // ---------------------------------------------------------------- 页面导航

    private void HubAppearance_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Appearance);

    private void HubSecurity_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Security);

    private void BackToSettingsHub_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Hub);

    private void BackToSecurity_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Security);

    private void BackToForgot_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Forgot);

    private void OpenPasswordPage_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Password);

    private void OpenRecoveryPage_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Recovery);

    private void OpenQuestionsSetupPage_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Questions);

    private void OpenForgotPage_Click(object sender, RoutedEventArgs e) => ShowSettingsPage(SettingsPage.Forgot);

    private void ShowSettingsPage(SettingsPage page)
    {
        _page = page;

        // 首次密码流程只允许在「恢复码 -> 安全问题」的连续跳转里免密；
        // 一旦回到系统设置 / 密码主页，再进入安全问题就必须输入当前密码。
        if (page is SettingsPage.Hub or SettingsPage.Security)
        {
            _firstPasswordSetup = false;
        }

        if (page != SettingsPage.Recovery)
        {
            FirstSetupNavPanel.Visibility = Visibility.Collapsed;
        }

        var hubVisible = page == SettingsPage.Hub;
        SettingsHubPanel.Visibility = hubVisible ? Visibility.Visible : Visibility.Collapsed;
        AppearancePagePanel.Visibility = page == SettingsPage.Appearance ? Visibility.Visible : Visibility.Collapsed;
        SecurityPagePanel.Visibility = page == SettingsPage.Security ? Visibility.Visible : Visibility.Collapsed;
        PasswordPagePanel.Visibility = page == SettingsPage.Password ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPagePanel.Visibility = page == SettingsPage.Recovery ? Visibility.Visible : Visibility.Collapsed;
        QuestionsSetupPagePanel.Visibility = page == SettingsPage.Questions ? Visibility.Visible : Visibility.Collapsed;
        ForgotPagePanel.Visibility = page == SettingsPage.Forgot ? Visibility.Visible : Visibility.Collapsed;
        RecoveryResetPagePanel.Visibility = page == SettingsPage.RecoveryReset ? Visibility.Visible : Visibility.Collapsed;
        QuestionResetPagePanel.Visibility = page == SettingsPage.QuestionReset ? Visibility.Visible : Visibility.Collapsed;

        switch (page)
        {
            case SettingsPage.Appearance:
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.SettingsAppearance);
                break;
            case SettingsPage.Security:
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.SettingsPassword);
                break;
            case SettingsPage.Password:
                PasswordHint.Text = string.Empty;
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.SettingsPassword);
                break;
            case SettingsPage.Recovery:
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.SettingsRecovery);
                break;
            case SettingsPage.Questions:
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.SettingsQuestions);
                break;
            case SettingsPage.Forgot:
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.SettingsForgot);
                break;
            case SettingsPage.RecoveryReset:
            case SettingsPage.QuestionReset:
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.SettingsForgot);
                break;
        }
    }

    // ---------------------------------------------------------------- 外观与启动

    private void OpenThemeDialog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ThemeDialog();
        var owner = HostWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        Refresh();
    }

    private void AutoStartCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePassword("开机自启"))
        {
            Refresh();
            return;
        }

        var settings = App.State.Settings;
        settings.AutoStart = AutoStartCheck.IsChecked == true;

        var ok = settings.AutoStart
            ? ElevatedSetup.TryInstall(out var message)
            : ElevatedSetup.TryRemove(out message);

        App.State.Save();

        if (!ok)
        {
            AppMessage.Warn(HostWindow,
                message + "\n\n注意：勾选状态会保留（代表你的意愿），但计划任务还没创建成功。" +
                "请先点上一界面的「以管理员身份重启」，再回来勾一次。");
        }
    }

    private void TrayCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePassword("托盘设置"))
        {
            Refresh();
            return;
        }

        App.State.Settings.MinimizeToTray = TrayCheck.IsChecked == true;
        App.State.Save();
    }

    /// <summary>改设置需要密码（防改动）。</summary>
    private bool RequirePassword(string action)
    {
        var settings = App.State.Settings;
        if (!settings.HasPassword)
        {
            return true;
        }

        var dialog = new PasswordDialog("修改设置需要密码（" + action + "）：")
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

    // ---------------------------------------------------------------- 密码

    private void SavePassword_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        var firstPassword = !settings.HasPassword;

        if (settings.HasPassword && !PasswordService.VerifyPassword(settings, CurrentPasswordBox.Password))
        {
            PasswordHint.Text = "当前密码不正确。";
            return;
        }

        var check = PasswordService.Validate(NewPasswordBox.Password);
        if (!check.IsValid)
        {
            PasswordHint.Text = "新密码不符合要求：" + check.ProblemsText;
            return;
        }

        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            PasswordHint.Text = "两次输入的新密码不一致。";
            return;
        }

        PasswordService.SetPassword(settings, NewPasswordBox.Password);
        App.State.Save();

        CurrentPasswordBox.Clear();
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        PasswordHint.Text = "密码已更新。";

        if (firstPassword)
        {
            _firstPasswordSetup = true;
            FirstSetupNavPanel.Visibility = Visibility.Visible;
            Refresh();
            ShowSettingsPage(SettingsPage.Recovery);
            RecoveryHint.Text = "首次设置密码完成。建议先生成恢复码；也可以点「跳过，以后再说」。";
        }
        else
        {
            AppMessage.Info(HostWindow, "密码已更新。");
            ShowSettingsPage(SettingsPage.Security);
            Refresh();
        }
    }

    private void CheckStrength_Click(object sender, RoutedEventArgs e)
    {
        var check = PasswordService.Validate(NewPasswordBox.Password);
        PasswordHint.Text = check.IsValid
            ? "\u2713 新密码符合要求。"
            : "\u2717 还缺：" + check.ProblemsText;
    }

    // ---------------------------------------------------------------- 恢复码

    private void GenerateRecovery_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        if (!settings.HasPassword)
        {
            RecoveryHint.Text = "请先设置密码，再生成恢复码。";
            return;
        }

        var code = PasswordService.GenerateRecoveryCode();
        PasswordService.SetRecoveryCode(settings, code);
        var saved = RecoveryCodeStore.Save(code);
        App.State.Save();

        RecoveryHint.Text = saved
            ? "新的恢复码已生成，并已加密保存在本机。"
            : "新的恢复码已生成，但本机保存失败，请抄下来存好。";

        AppMessage.Info(HostWindow,
            saved
                ? $"新的恢复码（已自动加密保存在本机，可随时点「查看本机恢复码」）：\n\n    {code}\n\n忘记密码时可以用它重置密码；恢复码退出每天仅 1 次。"
                : $"新的恢复码（本机保存失败，请抄下来存好）：\n\n    {code}\n\n忘记密码时可以用它重置密码。");
        Refresh();
        RecoveryHint.Text = saved
            ? "新的恢复码已生成，并已加密保存在本机。"
            : "新的恢复码已生成，但本机保存失败，请抄下来存好。";
    }

    private void ViewRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (!RecoveryCodeStore.TryLoad(out var code))
        {
            RecoveryHint.Text = "本机没有可读取的恢复码副本。若已设置恢复码，请重新生成一次；生成后会自动加密保存。";
            return;
        }

        try
        {
            Clipboard.SetText(code);
        }
        catch
        {
        }

        AppMessage.Info(HostWindow,
            $"本机保存的恢复码（已尝试复制到剪贴板）：\n\n    {code}\n\n" +
            "恢复码可用于重设密码，也可在密码冷却 / 冻结时提前结束自律（每天 1 次）。");
        RecoveryHint.Text = "已显示本机保存的恢复码。";
    }

    private void FirstSetupNext_Click(object sender, RoutedEventArgs e)
    {
        _firstPasswordSetup = true;
        ShowSettingsPage(SettingsPage.Questions);
    }

    private void SkipFirstSetup_Click(object sender, RoutedEventArgs e)
    {
        _firstPasswordSetup = false;
        FirstSetupNavPanel.Visibility = Visibility.Collapsed;
        ShowSettingsPage(SettingsPage.Security);
        AppMessage.Info(HostWindow, "已跳过。之后可以在「密码与防改动」里随时设置恢复码和安全问题。");
    }

    // ---------------------------------------------------------------- 安全问题

    private void SaveSecurityQuestions_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;

        if (!_firstPasswordSetup && settings.HasPassword &&
            !PasswordService.VerifyPassword(settings, QuestionsCurrentPasswordBox.Password))
        {
            SecurityHint.Text = "当前密码不正确，请重试。";
            return;
        }

        var entries = new List<(string Question, string Answer)>
        {
            (Question1Box.Text, Answer1Box.Text),
            (Question2Box.Text, Answer2Box.Text),
            (Question3Box.Text, Answer3Box.Text),
        };

        if (entries.Any(x => string.IsNullOrWhiteSpace(x.Question) || string.IsNullOrWhiteSpace(x.Answer)))
        {
            SecurityHint.Text = "3 个问题和 3 个答案都必须填写。";
            return;
        }

        PasswordService.SetSecurityQuestions(settings, entries);
        App.State.Save();
        SecurityHint.Text = "安全问题已保存。";
        QuestionsCurrentPasswordBox.Clear();

        var wasFirstSetup = _firstPasswordSetup;
        _firstPasswordSetup = false;
        FirstSetupNavPanel.Visibility = Visibility.Collapsed;

        AppMessage.Info(HostWindow, "安全问题已保存。\n\n忘记密码时，在「忘记密码」页用 3 个问题即可重设密码。");
        ShowSettingsPage(wasFirstSetup ? SettingsPage.Security : SettingsPage.Security);
        Refresh();
    }

    // ---------------------------------------------------------------- 忘记密码：选择重置方式

    private void GoRecoveryReset_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        if (!settings.HasRecoveryCode)
        {
            if (AppMessage.Confirm(HostWindow,
                    "还没有生成恢复码，无法用恢复码重置密码。\n\n要现在去生成恢复码吗？",
                    "忘记密码", okText: "去生成", cancelText: "知道了"))
            {
                ShowSettingsPage(SettingsPage.Recovery);
            }

            return;
        }

        ShowSettingsPage(SettingsPage.RecoveryReset);
    }

    private void GoQuestionReset_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        if (!settings.HasSecurityQuestions)
        {
            if (AppMessage.Confirm(HostWindow,
                    "还没有设置安全问题，无法用安全问题重置密码。\n\n要现在去设置安全问题吗？",
                    "忘记密码", okText: "去设置", cancelText: "知道了"))
            {
                ShowSettingsPage(SettingsPage.Questions);
            }

            return;
        }

        ShowSettingsPage(SettingsPage.QuestionReset);
    }

    private void RecoveryReset_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        if (!settings.HasRecoveryCode)
        {
            RecoveryResetHint.Text = "还没有生成恢复码。";
            return;
        }

        if (!PasswordService.VerifyRecoveryCode(settings, ResetCodeBox.Text))
        {
            RecoveryResetHint.Text = "恢复码不正确，请重试。";
            ResetCodeBox.SelectAll();
            return;
        }

        if (!TryApplyNewPassword(
                settings,
                RecoveryResetNewPasswordBox.Password,
                RecoveryResetConfirmPasswordBox.Password,
                out var error))
        {
            RecoveryResetHint.Text = error;
            return;
        }

        App.State.Save();
        AppMessage.Info(HostWindow, "密码已重设成功。注意：当天密码退出冷却 / 冻结状态不会因此解除。");
        ShowSettingsPage(SettingsPage.Security);
        Refresh();
    }

    private void QuestionReset_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        if (!settings.HasSecurityQuestions)
        {
            QuestionResetHint.Text = "还没有设置安全问题，无法用安全问题重置。";
            return;
        }

        var answers = new[]
        {
            QuestionResetAnswer1Box.Text,
            QuestionResetAnswer2Box.Text,
            QuestionResetAnswer3Box.Text,
        };

        if (!PasswordService.VerifySecurityAnswers(settings, answers))
        {
            QuestionResetHint.Text = "答案不正确（需要 3 个问题全部答对）。";
            return;
        }

        if (!TryApplyNewPassword(
                settings,
                QuestionResetNewPasswordBox.Password,
                QuestionResetConfirmPasswordBox.Password,
                out var error))
        {
            QuestionResetHint.Text = error;
            return;
        }

        App.State.Save();
        AppMessage.Info(HostWindow, "密码已重设成功。注意：当天密码退出冷却 / 冻结状态不会因此解除。");
        ShowSettingsPage(SettingsPage.Security);
        Refresh();
    }

    private static bool TryApplyNewPassword(AppSettings settings, string password, string confirm, out string error)
    {
        error = string.Empty;
        var check = PasswordService.Validate(password);
        if (!check.IsValid)
        {
            error = "新密码不符合要求：" + check.ProblemsText;
            return false;
        }

        if (password != confirm)
        {
            error = "两次输入的新密码不一致。";
            return false;
        }

        PasswordService.SetPassword(settings, password);
        return true;
    }

    // ---------------------------------------------------------------- 数据目录

    private void RelaunchElevated_Click(object sender, RoutedEventArgs e)
    {
        if (AdminHelper.IsElevated)
        {
            AppMessage.Info(HostWindow, "当前已经是管理员权限。");
            return;
        }

        if (!AppMessage.Confirm(HostWindow, "将以管理员身份重新启动 MikoBarrier（会弹 UAC），继续吗？",
                okText: "以管理员重启", cancelText: "取消"))
        {
            return;
        }

        App.State.Settings.StartElevated = true;
        App.State.Save();

        if (AdminHelper.TryRelaunchElevated())
        {
            App.ForceExit = true;
            Application.Current.Shutdown();
        }
    }

    private const string RepoUrl = "https://github.com/xgzshengming/MikoBarrier";

    private void OpenRepo_Click(object sender, RoutedEventArgs e) => OpenExternal(RepoUrl);

    private void OpenIssues_Click(object sender, RoutedEventArgs e) => OpenExternal(RepoUrl + "/issues/new/choose");

    private void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppMessage.Warn(HostWindow, $"打开链接失败：{ex.Message}");
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(StoragePaths.DataDir);

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(StoragePaths.LogDir);

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppMessage.Warn(HostWindow, ex.Message);
        }
    }
}