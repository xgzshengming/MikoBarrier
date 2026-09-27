using System.Windows;
using System.Windows.Input;
using MikoBarrier.Controls;

namespace MikoBarrier.Dialogs;

public partial class PasswordDialog : Window
{
    public PasswordDialog(string prompt)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            WindowFx.BringToForeground(this);
        };
        InputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Ok_Click(this, new RoutedEventArgs());
            }
        };
        SourceInitialized += (_, _) => WindowFx.ApplyRoundedCorners(this);
    }

    public string Password => InputBox.Password;

    /// <summary>用于校验密码是否正确；返回 false 时不会关闭窗口。</summary>
    public Func<string, bool>? Validator { get; set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Validator is not null && !Validator(Password))
        {
            ErrorText.Text = "密码不正确，请重试。";
            InputBox.SelectAll();
            return;
        }

        DialogResult = true;
    }

    private void Forgot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ForgotPasswordDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            InputBox.Clear();
            ErrorText.Text = string.Empty;
            InputBox.Focus();
            AppMessage.Info(this, "密码已重设，请输入新密码继续。");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}