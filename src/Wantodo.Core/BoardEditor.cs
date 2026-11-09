namespace Wantodo.Core;

/// <summary>
/// 所有「改数据」的操作都放这里，UI 只调用、不自己拼 List。
/// 全是纯函数（只改传入的对象），方便单测，也方便以后做撤销/同步。
/// </summary>
public static class BoardEditor
{
    /// <summary>栏数上限。超过就挤了，见 decisions.md D5。</summary>
    public const int MaxColumns = 4;

    public const string DefaultColumnTitle = "未命名栏";

    public static NoteColumn? FindColumn(Board board, string columnId)
        => board.Columns.FirstOrDefault(c => c.Id == columnId);

    /// <summary>加一栏。到上限或标题为空都返回 false，不做「悄悄改名」这种惊喜。</summary>
    public static bool TryAddColumn(Board board, string? title, out NoteColumn? column)
        => TryAddColumn(board, title, board.Columns.Count, out column);

    /// <summary>
    /// 在指定下标处加一栏（越界自动夹到 0..Count）。
    /// 新栏插在当前操作的那一栏右边，而不是永远追加到末尾——见 decisions.md D9。
    /// </summary>
    public static bool TryAddColumn(Board board, string? title, int index, out NoteColumn? column)
    {
        column = null;

        if (board.Columns.Count >= MaxColumns)
        {
            return false;
        }

        column = new NoteColumn { Title = NormalizeTitle(title) };
        board.Columns.Insert(Math.Clamp(index, 0, board.Columns.Count), column);
        return true;
    }

    /// <summary>删一栏。最后一栏不许删（否则界面空白，用户会以为数据没了）。</summary>
    public static bool TryRemoveColumn(Board board, string columnId)
    {
        if (board.Columns.Count <= 1)
        {
            return false;
        }

        var column = FindColumn(board, columnId);
        if (column is null)
        {
            return false;
        }

        return board.Columns.Remove(column);
    }

    public static void RenameColumn(NoteColumn column, string? title)
        => column.Title = NormalizeTitle(title);

    /// <summary>在栏尾加一条。文本只有空白就当作取消，返回 false。</summary>
    public static bool TryAddNote(NoteColumn column, string? text, out Note? note)
    {
        note = null;

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        note = new Note { Text = trimmed };
        column.Notes.Add(note);
        return true;
    }

    public static bool TryFindNote(Board board, string noteId, out NoteColumn? column, out Note? note)
    {
        column = null;
        note = null;

        foreach (var candidate in board.Columns)
        {
            var found = candidate.Notes.FirstOrDefault(n => n.Id == noteId);
            if (found is not null)
            {
                column = candidate;
                note = found;
                return true;
            }
        }

        return false;
    }

    public static bool RemoveNote(Board board, string noteId)
    {
        if (!TryFindNote(board, noteId, out var column, out var note) || column is null || note is null)
        {
            return false;
        }

        return column.Notes.Remove(note);
    }

    /// <summary>
    /// 把一条便签移到另一栏的指定位置（同栏内排序也走这里）。
    /// <paramref name="index"/> 是**移走之后**它在目标栏里的最终下标，越界会被夹到合法范围。
    /// 调用方（拖拽）要自己把「拖到第 j 行上方」换算成最终下标：同栏且往后拖时 j 要减一。
    /// </summary>
    public static bool MoveNote(Board board, string noteId, string targetColumnId, int index)
    {
        if (!TryFindNote(board, noteId, out var sourceColumn, out var note) || sourceColumn is null || note is null)
        {
            return false;
        }

        var targetColumn = FindColumn(board, targetColumnId);
        if (targetColumn is null)
        {
            return false;
        }

        sourceColumn.Notes.Remove(note);

        index = Math.Clamp(index, 0, targetColumn.Notes.Count);
        targetColumn.Notes.Insert(index, note);
        note.UpdatedAt = DateTimeOffset.Now;
        return true;
    }

    public static void UpdateText(Note note, string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || string.Equals(note.Text, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        note.Text = trimmed;
        note.UpdatedAt = DateTimeOffset.Now;
    }

    public static void SetCompleted(Note note, bool completed)
    {
        if (note.Completed == completed)
        {
            return;
        }

        note.Completed = completed;
        note.CompletedAt = completed ? DateTimeOffset.Now : null;
        note.UpdatedAt = DateTimeOffset.Now;
    }

    private static string NormalizeTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        return trimmed.Length == 0 ? DefaultColumnTitle : trimmed;
    }
}
