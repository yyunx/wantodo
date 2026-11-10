using System.Windows;
using System.Windows.Input;
using Wantodo.Core;

namespace Wantodo.App.Dialogs;

/// <summary>
/// 加栏时问一句名字。回车=确认（空的就用默认名），Esc=取消。
/// 单独做成窗口而不是 InputBox，是因为要跟随主题色、并且要能被 UI Automation 驱动（测试用）。
/// </summary>
public partial class ColumnNameDialog : Window
{
    public ColumnNameDialog(string defaultName)
    {
        InitializeComponent();

        NameBox.Text = defaultName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>用户填的名字（可能是空，调用方自己回落到默认名）。</summary>
    public string ColumnTitle => NameBox.Text?.Trim() ?? string.Empty;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnNameBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                DialogResult = true;
                e.Handled = true;
                break;

            case Key.Escape:
                DialogResult = false;
                e.Handled = true;
                break;
        }
    }

    /// <summary>给调用方一个整齐的默认值。</summary>
    public static string DefaultName => BoardEditor.DefaultColumnTitle;
}
