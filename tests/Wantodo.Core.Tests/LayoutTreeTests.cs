using Wantodo.Core;

namespace Wantodo.Core.Tests;

public sealed class LayoutTreeTests
{
    private static LayoutNode Sample() => LayoutNode.Split(
        "Horizontal",
        0.5,
        LayoutNode.Leaf("a"),
        LayoutNode.Leaf("b"));

    [Fact]
    public void Balanced_Empty_ReturnsNull()
    {
        Assert.Null(LayoutTree.Balanced(Array.Empty<string>()));
    }

    [Fact]
    public void Balanced_Single_IsLeaf()
    {
        var tree = LayoutTree.Balanced(new[] { "a" });

        Assert.NotNull(tree);
        Assert.True(tree!.IsLeaf);
        Assert.Equal("a", tree.PaneId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Balanced_KeepsOrderAndValidates(int count)
    {
        var ids = Enumerable.Range(0, count).Select(i => $"p{i}").ToList();
        var tree = LayoutTree.Balanced(ids);

        Assert.NotNull(tree);
        Assert.Equal(ids, LayoutTree.LeafIds(tree));
        Assert.Null(LayoutTree.Validate(tree, ids));
    }

    [Fact]
    public void Balanced_IsEvenlyWide()
    {
        var ids = new[] { "a", "b", "c", "d" };
        var tree = LayoutTree.Balanced(ids)!;
        var (width, _) = LayoutTree.ComputePreferredSize(tree, _ => (100, 100));

        // 4 栏各 100 + 3 条分隔条
        Assert.Equal(4 * 100 + 3 * LayoutTree.SplitterSize, width, 3);
    }

    [Theory]
    [InlineData(DockSide.Left)]
    [InlineData(DockSide.Right)]
    [InlineData(DockSide.Top)]
    [InlineData(DockSide.Bottom)]
    public void InsertBeside_PutsNewPaneOnRequestedSide(DockSide side)
    {
        // 用单叶作根：插入后根就是那个新 Split，方向/位置才好断言
        var tree = LayoutTree.InsertBeside(LayoutNode.Leaf("a"), "a", "c", side);
        var ids = LayoutTree.LeafIds(tree);

        Assert.Contains("c", ids);
        Assert.Equal(side is DockSide.Left or DockSide.Top ? new[] { "c", "a" } : new[] { "a", "c" }, ids);
        Assert.Null(LayoutTree.Validate(tree, new[] { "a", "c" }));

        var root = Assert.IsType<LayoutNode>(tree);
        switch (side)
        {
            case DockSide.Left:
                Assert.True(root.IsHorizontal);
                Assert.Equal("c", root.First!.PaneId);
                break;
            case DockSide.Right:
                Assert.True(root.IsHorizontal);
                Assert.Equal("c", root.Second!.PaneId);
                break;
            case DockSide.Top:
                Assert.False(root.IsHorizontal);
                Assert.Equal("c", root.First!.PaneId);
                break;
            case DockSide.Bottom:
                Assert.False(root.IsHorizontal);
                Assert.Equal("c", root.Second!.PaneId);
                break;
        }
    }

    [Fact]
    public void InsertBeside_UnknownTarget_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LayoutTree.InsertBeside(Sample(), "zzz", "c", DockSide.Right));
    }

    [Fact]
    public void MovePane_AcrossLevels_KeepsSingleOccurrence()
    {
        var tree = LayoutTree.InsertBeside(Sample(), "a", "c", DockSide.Right);
        var moved = LayoutTree.MovePane(tree, "c", "b", DockSide.Top);

        Assert.Equal(new[] { "a", "b", "c" }, LayoutTree.LeafIds(moved).Order().ToArray());
        Assert.Null(LayoutTree.Validate(moved, new[] { "a", "b", "c" }));
    }

    [Fact]
    public void MovePane_ToItself_IsNoOp()
    {
        var tree = Sample();
        var moved = LayoutTree.MovePane(tree, "a", "a", DockSide.Right);

        Assert.Equal(new[] { "a", "b" }, LayoutTree.LeafIds(moved));
    }

    [Fact]
    public void RemovePane_CollapsesEmptySplit()
    {
        var tree = LayoutTree.InsertBeside(Sample(), "a", "c", DockSide.Right);
        var shrunk = LayoutTree.RemovePane(tree, "c");

        Assert.NotNull(shrunk);
        Assert.Equal(new[] { "a", "b" }, LayoutTree.LeafIds(shrunk));
        Assert.Null(LayoutTree.Validate(shrunk, new[] { "a", "b" }));
    }

    [Fact]
    public void RemovePane_LastPane_ReturnsNull()
    {
        Assert.Null(LayoutTree.RemovePane(LayoutNode.Leaf("a"), "a"));
    }

    [Fact]
    public void SwapPanes_SwapsLeafIdsInPlace()
    {
        var tree = Sample();
        var swapped = LayoutTree.SwapPanes(tree, "a", "b");

        Assert.Equal(new[] { "b", "a" }, LayoutTree.LeafIds(swapped));
    }

    [Fact]
    public void SetRatio_ClampsToAllowedRange()
    {
        var tree = Sample();

        Assert.True(LayoutTree.SetRatio(tree, string.Empty, 0.01));
        Assert.Equal(LayoutNode.MinRatio, tree.Ratio, 3);

        Assert.True(LayoutTree.SetRatio(tree, string.Empty, 0.99));
        Assert.Equal(LayoutNode.MaxRatio, tree.Ratio, 3);
    }

    [Fact]
    public void SetRatio_BadPathOrNaN_False()
    {
        var tree = Sample();

        Assert.False(LayoutTree.SetRatio(tree, "0", 0.5));
        Assert.False(LayoutTree.SetRatio(tree, string.Empty, double.NaN));
    }

    [Fact]
    public void Validate_RejectsDuplicateLeaf()
    {
        var tree = LayoutNode.Split("Horizontal", 0.5, LayoutNode.Leaf("a"), LayoutNode.Leaf("a"));
        Assert.NotNull(LayoutTree.Validate(tree, new[] { "a" }));
    }

    [Fact]
    public void Validate_RejectsMissingPane()
    {
        Assert.NotNull(LayoutTree.Validate(Sample(), new[] { "a", "b", "c" }));
        Assert.NotNull(LayoutTree.Validate(Sample(), new[] { "a" }));
    }

    [Fact]
    public void Validate_RejectsBadRatio()
    {
        var tree = LayoutNode.Split("Horizontal", 0.5, LayoutNode.Leaf("a"), LayoutNode.Leaf("b"));
        tree.Ratio = 0.99;

        Assert.NotNull(LayoutTree.Validate(tree, new[] { "a", "b" }));
    }

    [Fact]
    public void ComputeMinSize_UsesMinPaneSize()
    {
        var (width, height) = LayoutTree.ComputeMinSize(Sample());

        Assert.Equal(2 * LayoutTree.MinPaneWidth + LayoutTree.SplitterSize, width, 3);
        Assert.Equal(LayoutTree.MinPaneHeight, height, 3);
    }

    [Fact]
    public void ComputePreferredSize_VerticalStacksHeights()
    {
        var tree = LayoutNode.Split("Vertical", 0.5, LayoutNode.Leaf("a"), LayoutNode.Leaf("b"));
        var (width, height) = LayoutTree.ComputePreferredSize(tree, _ => (100, 80));

        Assert.Equal(100, width, 3);
        Assert.Equal(160 + LayoutTree.SplitterSize, height, 3);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var tree = LayoutNode.Split(
            "Vertical",
            0.3,
            LayoutNode.Leaf("a"),
            LayoutNode.Split("Horizontal", 0.6, LayoutNode.Leaf("b"), LayoutNode.Leaf("c")));

        var json = System.Text.Json.JsonSerializer.Serialize(tree);
        var back = System.Text.Json.JsonSerializer.Deserialize<LayoutNode>(json);

        Assert.NotNull(back);
        Assert.Equal(new[] { "a", "b", "c" }, LayoutTree.LeafIds(back));
        Assert.Equal(0.3, back!.Ratio, 3);
        Assert.Equal(0.6, back.Second!.Ratio, 3);
    }

    [Fact]
    public void Store_Round_Trips_Vertical_Layout()
    {
        var root = LayoutNode.Split(
            "Vertical",
            0.42,
            LayoutNode.Leaf("a"),
            LayoutNode.Split("Horizontal", 0.5, LayoutNode.Leaf("b"), LayoutNode.Leaf("c")));

        var board = new Board
        {
            Layout = root,
            Columns =
            {
                new NoteColumn { Id = "a", Title = "A" },
                new NoteColumn { Id = "b", Title = "B" },
                new NoteColumn { Id = "c", Title = "C" },
            },
        };

        var path = Path.Combine(Path.GetTempPath(), "wantodo-layout-" + Guid.NewGuid().ToString("N"), "data.json");
        var store = new JsonBoardStore(path);
        store.Save(board);

        var loaded = store.Load().Board;
        Assert.NotNull(loaded.Layout);
        Assert.False(loaded.Layout!.IsHorizontal);
        Assert.Equal(0.42, loaded.Layout.Ratio, 3);
        Assert.Equal(new[] { "a", "b", "c" }, LayoutTree.LeafIds(loaded.Layout));
    }
}
