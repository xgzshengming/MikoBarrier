using System.Windows;
using System.Windows.Controls;

namespace MikoBarrier.Controls;

/// <summary>
/// 带“显示 / 隐藏”切换的密码框：内部同时维护 PasswordBox 与 TextBox，切换时互相同步。
/// 眼睛图标用 Path 绘制，不依赖 Segoe MDL2 等图标字体，避免出现缺字方块。
/// </summary>
public partial class PasswordRevealBox : UserControl
{
    private bool _syncing;

    public PasswordRevealBox()
    {
        InitializeComponent();
    }

    /// <summary>同 PasswordBox.Password，供现有业务代码直接读取 / 写入。</summary>
    public string Password
    {
        get => HiddenBox.Password;
        set
        {
            var text = value ?? string.Empty;
            HiddenBox.Password = text;
            VisibleBox.Text = text;
        }
    }

    /// <summary>保留 PasswordBox 的 `●` 掩码字符，供自检断言使用。</summary>
    public char PasswordChar
    {
        get => HiddenBox.PasswordChar;
        set => HiddenBox.PasswordChar = value;
    }

    public void Clear()
    {
        _syncing = true;
        try
        {
            HiddenBox.Clear();
            VisibleBox.Clear();
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>把键盘焦点交给当前可见的输入框。</summary>
    public new bool Focus() => VisibleBox.Visibility == Visibility.Visible
        ? VisibleBox.Focus()
        : HiddenBox.Focus();

    /// <summary>全选当前可见的输入内容。</summary>
    public void SelectAll()
    {
        if (VisibleBox.Visibility == Visibility.Visible)
        {
            VisibleBox.SelectAll();
        }
        else
        {
            HiddenBox.SelectAll();
        }
    }

    private void HiddenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            VisibleBox.Text = HiddenBox.Password;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void VisibleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            HiddenBox.Password = VisibleBox.Text;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        if (RevealButton.IsChecked == true)
        {
            VisibleBox.Text = HiddenBox.Password;
            HiddenBox.Visibility = Visibility.Collapsed;
            VisibleBox.Visibility = Visibility.Visible;
            VisibleBox.CaretIndex = VisibleBox.Text.Length;
            VisibleBox.Focus();
            RevealButton.ToolTip = "隐藏密码";
        }
        else
        {
            HiddenBox.Password = VisibleBox.Text;
            VisibleBox.Visibility = Visibility.Collapsed;
            HiddenBox.Visibility = Visibility.Visible;
            HiddenBox.Focus();
            HiddenBox.SelectAll();
            RevealButton.ToolTip = "显示密码";
        }
    }
}
