using System.Windows;
using System.Windows.Input;
using Wantodo.App.ViewModels;

namespace Wantodo.App;

/// <summary>
/// 全局快捷键唤出的「快速记一条」小窗：贴屏幕上方，回车入库后消失，
/// 不切到主窗、不动主窗位置。Esc 或点别处取消。
/// </summary>
public partial class QuickCaptureWindow : Window
{
    private readonly ColumnViewModel _target;
    private bool _closing;

    public QuickCaptureWindow(ColumnViewModel target)
    {
        _target = target;
        InitializeComponent();

        Hint.Text = $"回车记到「{_target.Title}」 · Esc 取消";
        Loaded += OnLoaded;
        Deactivated += (_, _) => CloseOnce();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 居中贴在屏幕上方 16% 处，像 Spotlight 一样，眼球不用移太远
        var work = SystemParameters.WorkArea;
        Left = work.Left + Math.Max(0, (work.Width - ActualWidth) / 2);
        Top = work.Top + work.Height * 0.16;

        Activate();
        Input.Focus();
        Keyboard.Focus(Input);
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                Commit();
                break;

            case Key.Escape:
                e.Handled = true;
                CloseOnce();
                break;
        }
    }

    private void Commit()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0)
        {
            CloseOnce();
            return;
        }

        _target.DraftText = text;
        var added = _target.AddDraft();
        AppLog.Info($"快速记一条（{(added ? "成功" : "失败")}）→ {_target.Title}：{text}");
        CloseOnce();
    }

    /// <summary>
    /// 关窗要幂等：回车提交后 Close() 会触发 Deactivated，Deactivated 里再 Close() 会抛
    /// "在窗口关闭期间……无法调用 Close"（实测崩过一次，见 docs/verify/phase4.md）。
    /// </summary>
    private void CloseOnce()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }
}
