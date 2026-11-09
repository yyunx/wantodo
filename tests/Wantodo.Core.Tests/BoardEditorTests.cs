using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

public class BoardEditorTests
{
    private static Board TwoColumns()
    {
        var board = new Board();
        board.Columns.Add(new NoteColumn { Title = "甲" });
        board.Columns.Add(new NoteColumn { Title = "乙" });
        return board;
    }

    [Fact]
    public void TryAddNote_AppendsTrimmedNote()
    {
        var column = new NoteColumn();

        var ok = BoardEditor.TryAddNote(column, "  写周报  ", out var note);

        Assert.True(ok);
        Assert.NotNull(note);
        Assert.Equal("写周报", note!.Text);
        Assert.Single(column.Notes);
        Assert.Same(note, column.Notes[0]);
        Assert.False(note.Completed);
        Assert.Null(note.CompletedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryAddNote_RejectsBlank(string? text)
    {
        var column = new NoteColumn();

        var ok = BoardEditor.TryAddNote(column, text, out var note);

        Assert.False(ok);
        Assert.Null(note);
        Assert.Empty(column.Notes);
    }

    [Fact]
    public void TryAddColumn_AddsUntilMaxThenRefuses()
    {
        var board = new Board();

        Assert.True(BoardEditor.TryAddColumn(board, "一", out var first));
        Assert.Equal("一", first!.Title);
        Assert.True(BoardEditor.TryAddColumn(board, "二", out _));
        Assert.True(BoardEditor.TryAddColumn(board, "三", out _));
        Assert.True(BoardEditor.TryAddColumn(board, "四", out _));

        Assert.False(BoardEditor.TryAddColumn(board, "五", out var extra));
        Assert.Null(extra);
        Assert.Equal(BoardEditor.MaxColumns, board.Columns.Count);
    }

    [Theory]
    [InlineData(null, "未命名栏")]
    [InlineData("   ", "未命名栏")]
    [InlineData("  想法  ", "想法")]
    public void TryAddColumn_NormalizesTitle(string? title, string expected)
    {
        var board = new Board();

        Assert.True(BoardEditor.TryAddColumn(board, title, out var column));
        Assert.Equal(expected, column!.Title);
    }

    [Fact]
    public void TryRemoveColumn_RemovesButKeepsLastOne()
    {
        var board = TwoColumns();
        var first = board.Columns[0];

        Assert.True(BoardEditor.TryRemoveColumn(board, first.Id));
        Assert.Single(board.Columns);

        var last = board.Columns[0];
        Assert.False(BoardEditor.TryRemoveColumn(board, last.Id));
        Assert.Single(board.Columns);
    }

    [Theory]
    [InlineData("  想法  ", "想法")]
    [InlineData("", "未命名栏")]
    public void RenameColumn_Normalizes(string title, string expected)
    {
        var column = new NoteColumn { Title = "旧" };

        BoardEditor.RenameColumn(column, title);

        Assert.Equal(expected, column.Title);
    }

    [Fact]
    public void SetCompleted_TracksCompletedAtBothWays()
    {
        var note = new Note { Text = "x" };

        BoardEditor.SetCompleted(note, true);
        Assert.True(note.Completed);
        Assert.NotNull(note.CompletedAt);

        var completedAt = note.CompletedAt;
        BoardEditor.SetCompleted(note, true);
        Assert.Equal(completedAt, note.CompletedAt); // 重复设置不刷新时间

        BoardEditor.SetCompleted(note, false);
        Assert.False(note.Completed);
        Assert.Null(note.CompletedAt);
    }

    [Fact]
    public void UpdateText_TrimsAndIgnoresEmpty()
    {
        var note = new Note { Text = "旧" };
        var originalUpdatedAt = note.UpdatedAt;

        BoardEditor.UpdateText(note, "   新内容 ");
        Assert.Equal("新内容", note.Text);
        Assert.True(note.UpdatedAt >= originalUpdatedAt);

        BoardEditor.UpdateText(note, "   ");
        Assert.Equal("新内容", note.Text);
    }

    [Fact]
    public void MoveNote_MovesAcrossColumnsAtIndex()
    {
        var board = TwoColumns();
        var source = board.Columns[0];
        var target = board.Columns[1];
        BoardEditor.TryAddNote(source, "a", out var a);
        BoardEditor.TryAddNote(target, "b", out _);
        BoardEditor.TryAddNote(target, "c", out _);

        Assert.True(BoardEditor.MoveNote(board, a!.Id, target.Id, 1));

        Assert.Empty(source.Notes);
        Assert.Equal(new[] { "b", "a", "c" }, target.Notes.Select(n => n.Text));
    }

    [Fact]
    public void MoveNote_ClampsIndex()
    {
        var board = TwoColumns();
        var source = board.Columns[0];
        var target = board.Columns[1];
        BoardEditor.TryAddNote(source, "a", out var a);

        Assert.True(BoardEditor.MoveNote(board, a!.Id, target.Id, 99));

        Assert.Single(target.Notes);
        Assert.Equal(0, target.Notes.IndexOf(a));
    }

    [Fact]
    public void MoveNote_WithinSameColumn_ForwardKeepsRequestedPosition()
    {
        var board = TwoColumns();
        var column = board.Columns[0];
        foreach (var text in new[] { "a", "b", "c", "d" })
        {
            BoardEditor.TryAddNote(column, text, out _);
        }

        var a = column.Notes[0];

        // 把 a 放到「第 3 位」（0 基 → 下标 2），期望结果 b c a d
        Assert.True(BoardEditor.MoveNote(board, a.Id, column.Id, 2));

        Assert.Equal(new[] { "b", "c", "a", "d" }, column.Notes.Select(n => n.Text));
    }

    [Fact]
    public void MoveNote_WithinSameColumn_Backward()
    {
        var board = TwoColumns();
        var column = board.Columns[0];
        foreach (var text in new[] { "a", "b", "c" })
        {
            BoardEditor.TryAddNote(column, text, out _);
        }

        var c = column.Notes[2];

        Assert.True(BoardEditor.MoveNote(board, c.Id, column.Id, 0));

        Assert.Equal(new[] { "c", "a", "b" }, column.Notes.Select(n => n.Text));
    }

    [Fact]
    public void MoveNote_UnknownColumnOrNoteFails()
    {
        var board = TwoColumns();
        BoardEditor.TryAddNote(board.Columns[0], "a", out var a);

        Assert.False(BoardEditor.MoveNote(board, a!.Id, "不存在的栏", 0));
        Assert.False(BoardEditor.MoveNote(board, "不存在的条目", board.Columns[1].Id, 0));
        Assert.Single(board.Columns[0].Notes);
    }

    [Fact]
    public void RemoveNote_RemovesOnlyTheRequestedOne()
    {
        var board = TwoColumns();
        var column = board.Columns[0];
        BoardEditor.TryAddNote(column, "a", out var a);
        BoardEditor.TryAddNote(column, "b", out var b);

        Assert.True(BoardEditor.RemoveNote(board, a!.Id));
        Assert.False(BoardEditor.RemoveNote(board, a.Id));
        Assert.Equal(new[] { "b" }, column.Notes.Select(n => n.Text));
        Assert.Same(b, column.Notes[0]);
    }

    [Fact]
    public void TryFindNote_LooksInEveryColumn()
    {
        var board = TwoColumns();
        BoardEditor.TryAddNote(board.Columns[1], "乙栏内容", out var note);

        Assert.True(BoardEditor.TryFindNote(board, note!.Id, out var column, out var found));
        Assert.Same(board.Columns[1], column);
        Assert.Same(note, found);
    }

    [Fact]
    public void TryAddColumn_WithIndex_InsertsAtRequestedPosition()
    {
        var board = TwoColumns();
        var second = board.Columns[1];

        Assert.True(BoardEditor.TryAddColumn(board, "插在中间", 1, out var inserted));

        Assert.Equal(3, board.Columns.Count);
        Assert.Same(inserted, board.Columns[1]);
        Assert.Same(second, board.Columns[2]);
    }

    [Fact]
    public void TryAddColumn_WithIndex_ClampsOutOfRange()
    {
        var board = TwoColumns();

        Assert.True(BoardEditor.TryAddColumn(board, "越界到末尾", 99, out var tail));
        Assert.Same(tail, board.Columns[^1]);

        Assert.True(BoardEditor.TryAddColumn(board, "负数到开头", -5, out var head));
        Assert.Same(head, board.Columns[0]);
    }

    [Fact]
    public void TryAddColumn_WithIndex_StillHonoursMaxColumns()
    {
        var board = TwoColumns();
        Assert.True(BoardEditor.TryAddColumn(board, "三", 1, out _));
        Assert.True(BoardEditor.TryAddColumn(board, "四", 1, out _));

        Assert.False(BoardEditor.TryAddColumn(board, "五", 0, out var extra));
        Assert.Null(extra);
        Assert.Equal(BoardEditor.MaxColumns, board.Columns.Count);
    }
}