using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wantodo.Core;

namespace Wantodo.App.Dialogs;

/// <summary>
/// 自定义主题取色：色相条 + 饱和度/明度方块 + 十六进制输入 + 预览。
/// 只让用户挑背景与强调两色，其余令牌由 <see cref="ThemeDerivation"/> 推导；
/// 预览里直接显示推导后的对比度，不让人靠肉眼赌白底白字。
/// </summary>
public partial class ThemeColorPicker : Window
{
    private static readonly Rgb DefaultBackground = new(0x0D, 0x11, 0x17);
    private static readonly Rgb DefaultAccent = new(0x2F, 0x81, 0xF7);

    private Rgb _background;
    private Rgb _accent;
    private bool _editingAccent;
    private bool _syncing;
    private bool _dragging;

    public ThemeColorPicker(string? backgroundHex, string? accentHex)
    {
        InitializeComponent();

        _background = ThemePalette.TryParseHex(backgroundHex, out var background) ? background : DefaultBackground;
        _accent = ThemePalette.TryParseHex(accentHex, out var accent) ? accent : DefaultAccent;

        Loaded += (_, _) => UpdateAll();
        SvArea.SizeChanged += (_, _) => PositionThumb(HsvColor.FromRgb(CurrentColor));
    }

    public string BackgroundHex => _background.ToHex();

    public string AccentHex => _accent.ToHex();

    private Rgb CurrentColor => _editingAccent ? _accent : _background;

    private void SetCurrentColor(Rgb color)
    {
        if (_editingAccent)
        {
            _accent = color;
        }
        else
        {
            _background = color;
        }

        UpdateAll();
    }

    private void UpdateAll()
    {
        _syncing = true;
        try
        {
            BackgroundSwatch.Background = Brush(_background);
            AccentSwatch.Background = Brush(_accent);
            BackgroundChoice.BorderBrush = (Brush)FindResource(_editingAccent ? "Divider" : "Accent");
            AccentChoice.BorderBrush = (Brush)FindResource(_editingAccent ? "Accent" : "Divider");

            var hsv = HsvColor.FromRgb(CurrentColor);
            HueSlider.Value = hsv.Hue;
            HexBox.Text = CurrentColor.ToHex();
            HexHint.Text = _editingAccent ? "正在改强调色" : "正在改背景色";

            var hueColor = new HsvColor(hsv.Hue, 1, 1).ToRgb();
            SvHueBase.Background = new LinearGradientBrush(
                Colors.White, Color.FromRgb(hueColor.R, hueColor.G, hueColor.B), 0);
            PositionThumb(hsv);
            UpdatePreview();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void PositionThumb(HsvColor hsv)
    {
        double width = SvArea.ActualWidth;
        double height = SvArea.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double x = Math.Clamp(hsv.Saturation, 0, 1) * width;
        double y = (1 - Math.Clamp(hsv.Value, 0, 1)) * height;
        Canvas.SetLeft(SvThumb, x - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, y - SvThumb.Height / 2);
    }

    private void UpdatePreview()
    {
        var colors = ThemePalette.For(
            ThemePalette.CustomTheme, 0, systemIsLight: false, _background.ToHex(), _accent.ToHex());

        PreviewCard.Background = Brush(colors.CardBackground);
        PreviewCard.BorderBrush = Brush(colors.Divider);
        PreviewTitle.Foreground = Brush(colors.TextPrimary);
        PreviewSecondary.Foreground = Brush(colors.TextSecondary);
        PreviewMuted.Foreground = Brush(colors.TextMuted);
        PreviewAccentChip.Background = Brush(colors.Accent);
        PreviewAccentText.Foreground = Brush(colors.OnAccent);

        double primary = ThemeDerivation.Contrast(colors.WindowBackground, colors.TextPrimary);
        double primaryOnCard = ThemeDerivation.Contrast(colors.CardBackground, colors.TextPrimary);
        double secondary = ThemeDerivation.Contrast(colors.WindowBackground, colors.TextSecondary);
        double muted = ThemeDerivation.Contrast(colors.WindowBackground, colors.TextMuted);
        double onAccent = ThemeDerivation.Contrast(colors.Accent, colors.OnAccent);

        RatioText.Text =
            $"正文 {primary:0.0}:1 · 正文对卡片 {primaryOnCard:0.0}:1 · 次要 {secondary:0.0}:1 · " +
            $"弱化 {muted:0.0}:1 · 强调上文字 {onAccent:0.0}:1（阈值 7 / 7 / 4.5 / 4.5 / 4.5）";
    }

    private void ApplySvPoint(Point point)
    {
        double width = SvArea.ActualWidth;
        double height = SvArea.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double saturation = Math.Clamp(point.X / width, 0, 1);
        double value = 1 - Math.Clamp(point.Y / height, 0, 1);
        var hue = HsvColor.FromRgb(CurrentColor).Hue;
        SetCurrentColor(new HsvColor(hue, saturation, value).ToRgb());
    }

    private void OnBackgroundChoiceClick(object sender, MouseButtonEventArgs e)
    {
        _editingAccent = false;
        UpdateAll();
    }

    private void OnAccentChoiceClick(object sender, MouseButtonEventArgs e)
    {
        _editingAccent = true;
        UpdateAll();
    }

    private void OnHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing)
        {
            return;
        }

        var hsv = HsvColor.FromRgb(CurrentColor);
        SetCurrentColor(new HsvColor(HueSlider.Value, hsv.Saturation, hsv.Value).ToRgb());
    }

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        SvArea.CaptureMouse();
        ApplySvPoint(e.GetPosition(SvArea));
        e.Handled = true;
    }

    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            ApplySvPoint(e.GetPosition(SvArea));
        }
    }

    private void OnSvMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        SvArea.ReleaseMouseCapture();
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        if (ThemePalette.TryParseHex(HexBox.Text, out var color))
        {
            SetCurrentColor(color);
        }
        else
        {
            // 非法输入直接退回当前色，避免输入框里留着一个不生效的字符串
            HexBox.Text = CurrentColor.ToHex();
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private static SolidColorBrush Brush(Rgb color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 鼠标在按下瞬间已经松开，忽略
            }
        }
    }
}
