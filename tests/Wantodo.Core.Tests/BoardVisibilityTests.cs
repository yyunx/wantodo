using Wantodo.Core;

namespace Wantodo.Core.Tests;

public sealed class BoardVisibilityTests
{
    [Fact]
    public void EmptyVisibilityLists_MeanAllColumns()
    {
        var board = new Board();
        board.Columns.Add(new NoteColumn { Id = "a", Title = "A" });
        board.Columns.Add(new NoteColumn { Id = "b", Title = "B" });

        Assert.Equal(new[] { "a", "b" }, board.VisibleColumnIds(panel: false).OrderBy(x => x));
        Assert.Equal(new[] { "a", "b" }, board.VisibleColumnIds(panel: true).OrderBy(x => x));
    }

    [Fact]
    public void PerSurfaceLists_AreIndependentAndIgnoreUnknownIds()
    {
        var board = new Board();
        board.Columns.Add(new NoteColumn { Id = "a", Title = "A" });
        board.Columns.Add(new NoteColumn { Id = "b", Title = "B" });
        board.DesktopVisibleColumnIds = new List<string> { "a" };
        board.PanelVisibleColumnIds = new List<string> { "b", "gone" };

        Assert.Equal(new[] { "a" }, board.VisibleColumnIds(panel: false));
        Assert.Equal(new[] { "b" }, board.VisibleColumnIds(panel: true));
    }
}
