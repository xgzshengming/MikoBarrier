using System.Windows;
using System.Windows.Input;
using MikoBarrier.Controls;
using MikoBarrier.Core.Services;

namespace MikoBarrier.Dialogs;

/// <summary>恢复码输入框：支持“查看本机保存的恢复码”，并做一次验证。</summary>
public partial class RecoveryCodeDialog : Window
{
    public RecoveryCodeDialog(string prompt = "请输入恢复码：")
    {
        InitializeComponent();
        PromptText.Text = prompt;

        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height * 0.9);
        Loaded += (_, _) =>
        {
            CodeBox.Focus();
            WindowFx.BringToForeground(this);
        };
        CodeBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Ok_Click(this, new RoutedEventArgs());
            }
        };
        SourceInitialized += (_, _) => WindowFx.ApplyRoundedCorners(this);
    }

    public string Code => CodeBox.Text.Trim();

    /// <summary>返回 false 时不会关闭窗口。</summary>
    public Func<string, bool>? Validator { get; set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Validator is not null && !Validator(Code))
        {
            ErrorText.Text = "恢复码不正确，请重试。";
            CodeBox.SelectAll();
            return;
        }

        DialogResult = true;
    }

    private void RevealSaved_Click(object sender, RoutedEventArgs e)
    {
        if (!RecoveryCodeStore.TryLoad(out var saved))
        {
            ErrorText.Text = "本机没有保存恢复码，或保存副本无法解密（可能换过 Windows 用户 / 电脑）。";
            SavedCodeText.Visibility = Visibility.Collapsed;
            return;
        }

        CodeBox.Text = saved;
        SavedCodeText.Text = saved;
        SavedCodeText.Visibility = Visibility.Visible;
        SavedCodeText.SelectAll();
        ErrorText.Text = "已填入本机保存的恢复码；点「确定」继续。";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
