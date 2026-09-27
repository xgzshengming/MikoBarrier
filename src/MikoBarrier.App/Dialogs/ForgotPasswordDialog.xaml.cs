using System.Windows;
using MikoBarrier.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

namespace MikoBarrier.Dialogs;

/// <summary>忘记密码：可用恢复码或 3 个安全问题重设密码。重设不会解除当天密码退出冷却 / 冻结。</summary>
public partial class ForgotPasswordDialog : Window
{
    public ForgotPasswordDialog()
    {
        InitializeComponent();

        var settings = App.State.Settings;
        var questions = settings.SecurityQuestions;

        Question1Text.Text = $"问题 1：{QuestionAt(questions, 0)}";
        Question2Text.Text = $"问题 2：{QuestionAt(questions, 1)}";
        Question3Text.Text = $"问题 3：{QuestionAt(questions, 2)}";

        SourceInitialized += (_, _) => WindowFx.ApplyRoundedCorners(this);
        Loaded += (_, _) =>
        {
            if (settings.HasRecoveryCode)
            {
                RecoveryCodeBox.Focus();
            }
            else
            {
                Answer1Box.Focus();
            }
        };
    }

    private static string QuestionAt(IReadOnlyList<SecurityQuestion> questions, int index) =>
        index < questions.Count ? questions[index].Question : "（未设置）";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;

        if (!settings.HasSecurityQuestions)
        {
            HintText.Text = "还没有设置安全问题，只能用恢复码重置。";
            return;
        }

        var answers = new[] { Answer1Box.Text, Answer2Box.Text, Answer3Box.Text };
        if (!PasswordService.VerifySecurityAnswers(settings, answers))
        {
            HintText.Text = "答案不正确（需要 3 个问题全部答对）。";
            return;
        }

        ApplyNewPassword(settings);
    }

    private void ResetWithRecovery_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.State.Settings;
        if (!settings.HasRecoveryCode)
        {
            HintText.Text = "还没有生成恢复码，无法用恢复码重置。";
            return;
        }

        if (!PasswordService.VerifyRecoveryCode(settings, RecoveryCodeBox.Text))
        {
            HintText.Text = "恢复码不正确，请重试。";
            RecoveryCodeBox.SelectAll();
            return;
        }

        ApplyNewPassword(settings);
    }

    private void FillSavedRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (!RecoveryCodeStore.TryLoad(out var code))
        {
            HintText.Text = "本机没有保存恢复码副本（可能换过 Windows 用户 / 电脑，或还未重新生成）。";
            return;
        }

        RecoveryCodeBox.Text = code;
        HintText.Text = "已填入本机保存的恢复码。";
    }

    private void ApplyNewPassword(AppSettings settings)
    {
        var check = PasswordService.Validate(NewPasswordBox.Password);
        if (!check.IsValid)
        {
            HintText.Text = "新密码不符合要求：" + check.ProblemsText;
            return;
        }

        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            HintText.Text = "两次输入的新密码不一致。";
            return;
        }

        PasswordService.SetPassword(settings, NewPasswordBox.Password);
        // 按产品规则：重设密码不重置当天密码退出计数 / 冷却，防止靠重设密码绕过冷却。
        App.State.Save();

        AppMessage.Info(this, "密码已重设成功。注意：当天密码退出冷却 / 冻结状态不会因此解除。");
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}