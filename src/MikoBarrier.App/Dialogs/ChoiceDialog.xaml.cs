using System.Windows;
using MikoBarrier.Controls;

namespace MikoBarrier.Dialogs;

/// <summary>
/// 三选一对话框：ShowDialog() 返回 true = 第一个按钮，false = 第二个按钮，null = 取消。
/// </summary>
public partial class ChoiceDialog : Window
{
    public ChoiceDialog(string title, string message, string primaryText, string secondaryText, string cancelText)
    {
        InitializeComponent();

        Title = MikoText.T(title);
        TitleBarControl.Title = MikoText.T(title);
        MessageText.Text = MikoText.T(message);
        PrimaryButton.Content = MikoText.T(primaryText);
        SecondaryButton.Content = MikoText.T(secondaryText);
        CancelButton.Content = MikoText.T(cancelText);

        SourceInitialized += (_, _) => WindowFx.ApplyRoundedCorners(this);
        Loaded += (_, _) => PrimaryButton.Focus();
    }

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}