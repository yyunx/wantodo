using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wantodo.Core;

namespace Wantodo.App.Dialogs;

/// <summary>
/// 新建笔记前先问名字：名字直接进文件名（<c>yyyyMMdd-HHmm-slug.ext</c>），
/// 留空则回落到「未命名」，之后首次改标题仍会同步文件名。
/// 与 <see cref="ColumnNameDialog"/> 同款：跟随主题、可被 UI Automation 驱动。
/// </summary>
public partial class NewNoteDialog : Window
{
    private readonly NoteFormat _format;
    private readonly DateTimeOffset _stamp;

    public NewNoteDialog(NoteFormat format)
    {
        InitializeComponent();

        _format = format;
        _stamp = DateTimeOffset.Now;
        Loaded += (_, _) => NameBox.Focus();
        UpdatePreview();
    }

    /// <summary>用户填的名字（可能是空，调用方回落到「未命名」）。</summary>
    public string NoteTitle => NameBox.Text?.Trim() ?? string.Empty;

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var title = string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名" : NameBox.Text.Trim();
        var extension = _format == NoteFormat.Md ? ".md" : ".txt";
        FilePreview.Text = $"文件名：{_stamp:yyyyMMdd-HHmm}-{NotesVault.Slug(title)}{extension}";
    }

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
}
