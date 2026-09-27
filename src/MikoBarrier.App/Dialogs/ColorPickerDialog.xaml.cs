using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MikoBarrier.Controls;

namespace MikoBarrier.Dialogs;

/// <summary>应用内自绘的取色器：常用色板 + 十六进制输入 + 文字对比度预览。</summary>
public partial class ColorPickerDialog : Window
{
    private static readonly string[] PaletteHex =
    {
        // 灰阶
        "FFFFFF", "F5F5F7", "E5E7EB", "D1D5DB", "9CA3AF", "6B7280", "4B5563", "374151", "1F2937", "111827",
        // 红
        "FEF2F2", "FEE2E2", "FECACA", "F87171", "EF4444", "DC2626", "B91C1C", "991B1B", "7F1D1D", "450A0A",
        // 橙 / 黄
        "FFFBEB", "FEF3C7", "FDE68A", "FBBF24", "F59E0B", "D97706", "B45309", "92400E", "78350F", "451A03",
        // 绿
        "ECFDF5", "D1FAE5", "A7F3D0", "34D399", "10B981", "059669", "047857", "065F46", "064E3B", "022C22",
        // 蓝
        "EFF6FF", "DBEAFE", "BFDBFE", "60A5FA", "3B82F6", "2563EB", "1D4ED8", "1E40AF", "1E3A8A", "172554",
        // 紫
        "F5F3FF", "EDE9FE", "DDD6FE", "A78BFA", "8B5CF6", "7C3AED", "6D28D9", "5B21B6", "4C1D95", "2E1065",
        // 粉
        "FDF2F8", "FCE7F3", "FBCFE8", "F472B6", "EC4899", "DB2777", "BE185D", "9D174D", "831843", "500724",
    };

    private bool _updating;

    public Color SelectedColor { get; private set; }

    public ColorPickerDialog() : this(Color.FromRgb(0x1F, 0x3A, 0x5F))
    {
    }

    public ColorPickerDialog(Color initial)
    {
        InitializeComponent();

        BuildPalette();
        SetColor(initial);

        SourceInitialized += (_, _) =>
        {
            WindowFx.ApplyRoundedCorners(this);
            ClampToWorkArea();
        };
        Loaded += (_, _) =>
        {
            HexBox.Focus();
            HexBox.SelectAll();
        };
    }

    private void BuildPalette()
    {
        foreach (var hex in PaletteHex)
        {
            if (!ThemeColorMath.TryParse(hex, out var color))
            {
                continue;
            }

            var button = new Button
            {
                Style = (Style)FindResource("ColorSwatchButton"),
                Background = new SolidColorBrush(color),
                ToolTip = "#" + hex,
                Tag = color,
            };
            button.Click += (_, _) => SetColor((Color)button.Tag);
            PalettePanel.Children.Add(button);
        }
    }

    private void SetColor(Color color)
    {
        SelectedColor = color;
        _updating = true;
        HexBox.Text = ThemeColorMath.ToHex(color);
        _updating = false;
        UpdatePreview();
    }

    private void HexBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updating || !ThemeColorMath.TryParse(HexBox.Text, out var color))
        {
            return;
        }

        SelectedColor = color;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var background = SelectedColor;
        PreviewBorder.Background = new SolidColorBrush(background);

        var inkDark = Color.FromRgb(0x10, 0x12, 0x16);
        var inkLight = Color.FromRgb(0xF7, 0xF8, 0xFA);
        var text = ThemeColorMath.ContrastRatio(background, inkDark) >=
                   ThemeColorMath.ContrastRatio(background, inkLight)
            ? inkDark
            : inkLight;

        PreviewTitle.Foreground = new SolidColorBrush(text);
        PreviewBody.Foreground = new SolidColorBrush(ThemeColorMath.Mix(text, background, 0.42));
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemeColorMath.TryParse(HexBox.Text, out var color))
        {
            AppMessage.Warn(this, "颜色格式不对，请输入 6 位十六进制色值，例如 #1F3A5F。");
            return;
        }

        SelectedColor = color;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>按窗口所在屏幕的工作区限制高度，避免小屏幕上确定键被顶出屏幕。</summary>
    private void ClampToWorkArea()
    {
        double workWidth;
        double workHeight;

        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var dpi = VisualTreeHelper.GetDpi(this);
            workWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
            workHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
        }
        catch
        {
            workWidth = SystemParameters.WorkArea.Width;
            workHeight = SystemParameters.WorkArea.Height;
        }

        MaxWidth = Math.Max(320, workWidth - 24);
        MaxHeight = Math.Max(240, workHeight - 40);
    }
}
