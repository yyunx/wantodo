using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Wantodo.App.Interop;
using Wantodo.App.ViewModels;
using Wantodo.Core;

namespace Wantodo.App.Windows;

/// <summary>
/// 顶边下拉面板（形态 A）：与主窗口共用同一个 <see cref="BoardViewModel"/>，
/// 所以两边看到的是同一份数据，不存在互相覆盖。
/// 收起时只剩 12 DIP 高的一条把手；按住把手往下拖，超过记忆高度的 40% 展开。
/// </summary>
public partial class TopPanelWindow : Window
{
    private const double CollapsedHeight = 12;

    private readonly BoardViewModel _viewModel;
    private bool _dragging;
    private double _dragStartHeight;
    private int _dragStartY;

    public TopPanelWindow(BoardViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Width = Math.Min(760, SystemParameters.PrimaryScreenWidth);
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = 0;
        Height = CollapsedHeight;
        Opacity = 0;

        PreviewKeyDown += OnPreviewKeyDown;
    }

    public double FullHeight => Math.Max(Board.MinPanelHeight, Math.Min(Board.MaxPanelHeight, _viewModel.Model.PanelHeight));

    public bool IsExpanded => Height > CollapsedHeight + 2;

    public bool IsDragging => _dragging;

    /// <summary>列选择下拉是否打开：打开时点下拉项会被钩子误判成「点面板外」。</summary>
    public bool IsPickerOpen => ColumnPicker.IsDropDownOpen;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 收起时是一条 760×12 的窄条，靠 DWM 把左右两端磨圆，避免像一根硬邦邦的横杠。
        DesktopLayer.TryRoundCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    /// <summary>停留 120 ms 后出把手（只做透明度动画，不抢焦点）。</summary>
    public void ShowHandle()
    {
        if (!IsVisible)
        {
            Height = CollapsedHeight;
            Opacity = 0;
            BodyBorder.Visibility = Visibility.Collapsed;
            HandlePill.Visibility = Visibility.Visible;
            Show();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    public void Expand()
    {
        BodyBorder.Visibility = Visibility.Visible;
        HandlePill.Visibility = Visibility.Collapsed;
        AnimateHeight(FullHeight, 160, new CubicEase { EasingMode = EasingMode.EaseOut });
        _viewModel.SetPanelHeight(FullHeight);
    }

    public void Collapse()
    {
        if (!IsVisible)
        {
            return;
        }

        _dragging = false;
        HandlePill.Visibility = Visibility.Visible;
        AnimateHeight(CollapsedHeight, 140, new CubicEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            if (!IsExpanded)
            {
                Hide();
                BodyBorder.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>钩子给的屏幕坐标是物理像素；<see cref="PointToScreen"/> 也是物理像素，可以直接比。</summary>
    public bool ContainsScreenPoint(int x, int y)
    {
        if (!IsVisible)
        {
            return false;
        }

        try
        {
            Point topLeft = PointToScreen(new Point(0, 0));
            Point bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            return x >= topLeft.X && x <= bottomRight.X && y >= topLeft.Y && y <= bottomRight.Y;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>钩子给的屏幕坐标是物理像素；<see cref="PointFromScreen"/> 会换算成窗口内 DIP。</summary>
    public bool HitTestHandle(int screenX, int screenY)
    {
        if (!IsVisible)
        {
            return false;
        }

        try
        {
            Point local = PointFromScreen(new Point(screenX, screenY));
            return local.X >= 0 && local.X <= ActualWidth && local.Y >= 0 && local.Y <= CollapsedHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void BeginDrag(int screenY)
    {
        _dragging = true;
        _dragStartHeight = Height;
        _dragStartY = screenY;
        BeginAnimation(HeightProperty, null);
    }

    public void DragTo(int screenY)
    {
        if (!_dragging)
        {
            return;
        }

        double height = Math.Clamp(_dragStartHeight + (screenY - _dragStartY), CollapsedHeight, FullHeight);
        Height = height;

        bool showBody = height > CollapsedHeight + 2;
        BodyBorder.Visibility = showBody ? Visibility.Visible : Visibility.Collapsed;
        HandlePill.Visibility = Visibility.Visible;
    }

    public void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (Height >= FullHeight * 0.4)
        {
            Expand();
        }
        else
        {
            Collapse();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Collapse();
            e.Handled = true;
        }
    }

    private void OnQuickAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ColumnPicker.SelectedItem is not ColumnViewModel column)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(QuickBox.Text))
        {
            return;
        }

        column.DraftText = QuickBox.Text;
        column.AddDraft();
        QuickBox.Clear();
        e.Handled = true;
    }

    private void AnimateHeight(double target, int milliseconds, IEasingFunction easing, Action? completed = null)
    {
        BeginAnimation(HeightProperty, null);
        var animation = new DoubleAnimation(Height, target, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop,
        };

        animation.Completed += (_, _) =>
        {
            BeginAnimation(HeightProperty, null);
            Height = target;
            completed?.Invoke();
        };

        BeginAnimation(HeightProperty, animation);
    }
}
