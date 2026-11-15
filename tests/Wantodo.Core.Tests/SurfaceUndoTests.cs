using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

/// <summary>P1.2：每表面一条撤销栈 + 单表面快照（计划书 §4.6，验收 V-13）。</summary>
public sealed class SurfaceUndoTests
{
    private const string Structured = CanvasSurfaces.Structured;
    private const string Freeform = CanvasSurfaces.Freeform;

    [Fact]
    public void Push_ThenUndo_ReturnsPushedSnapshot()
    {
        var undo = new SurfaceUndo();
        undo.Push(Freeform, "s1");

        Assert.True(undo.CanUndo(Freeform));
        Assert.True(undo.TryUndo(Freeform, "current", out var restored));
        Assert.Equal("s1", restored);
        Assert.False(undo.CanUndo(Freeform));
        Assert.True(undo.CanRedo(Freeform));
    }

    [Fact]
    public void UndoRedo_RoundTripsThroughCurrent()
    {
        var undo = new SurfaceUndo();
        undo.Push(Freeform, "s1");
        undo.TryUndo(Freeform, "s2", out var restored);
        Assert.Equal("s1", restored);

        Assert.True(undo.TryRedo(Freeform, "s1", out var redone));
        Assert.Equal("s2", redone);
        Assert.True(undo.CanUndo(Freeform));
    }

    [Fact]
    public void EmptyStacks_ReturnFalse_AndDoNotChangeState()
    {
        var undo = new SurfaceUndo();
        Assert.False(undo.TryUndo(Freeform, "current", out var restored));
        Assert.Equal("", restored);
        Assert.False(undo.TryRedo(Freeform, "current", out _));
        Assert.False(undo.CanUndo(Freeform));
        Assert.Equal(0, undo.Depth(Freeform));
    }

    [Fact]
    public void Push_ClearsRedoOfSameSurface()
    {
        var undo = new SurfaceUndo();
        undo.Push(Freeform, "s1");
        undo.TryUndo(Freeform, "s2", out _);
        Assert.True(undo.CanRedo(Freeform));

        undo.Push(Freeform, "s3");
        Assert.False(undo.CanRedo(Freeform));
    }

    [Fact]
    public void Surfaces_DoNotShareStacks()
    {
        var undo = new SurfaceUndo();
        undo.Push(Freeform, "f1");
        undo.Push(Freeform, "f2");

        Assert.False(undo.CanUndo(Structured));
        Assert.False(undo.TryUndo(Structured, "x", out _));
        Assert.Equal(2, undo.Depth(Freeform));

        // 在另一个表面压栈不会清掉自由表面的重做
        undo.TryUndo(Freeform, "f3", out _);
        undo.Push(Structured, "s1");
        Assert.True(undo.CanRedo(Freeform));
    }

    [Fact]
    public void DeepUndo_KeepsAtMostFiftySnapshots()
    {
        var undo = new SurfaceUndo();
        for (var i = 0; i < 80; i++)
        {
            undo.Push(Freeform, $"s{i}");
        }

        Assert.Equal(SurfaceUndo.DefaultLimit, undo.Limit);
        Assert.Equal(50, undo.Depth(Freeform));

        // 最旧的 30 份已丢弃：从 s30 开始回退
        for (var i = 79; i >= 30; i--)
        {
            Assert.True(undo.TryUndo(Freeform, "current", out var restored));
            Assert.Equal($"s{i}", restored);
        }

        Assert.False(undo.CanUndo(Freeform));
    }

    [Fact]
    public void Clear_DropsBothStacksOfOneSurface()
    {
        var undo = new SurfaceUndo();
        undo.Push(Freeform, "f1");
        undo.TryUndo(Freeform, "f2", out _);
        undo.Push(Structured, "s1");

        undo.Clear(Freeform);
        Assert.False(undo.CanUndo(Freeform));
        Assert.False(undo.CanRedo(Freeform));
        Assert.True(undo.CanUndo(Structured));
    }

    [Fact]
    public void BlankSurfaceName_FallsBackToStructured()
    {
        var undo = new SurfaceUndo();
        undo.Push("", "s1");
        Assert.True(undo.CanUndo(Structured));
        Assert.Equal(1, undo.Depth(Structured));
    }

    [Fact]
    public void Capture_TakesOnlyTheRequestedSurface()
    {
        var document = BuildDocument();

        var freeform = CanvasSnapshot.Capture(document, Freeform);
        Assert.Contains("\"o1\"", freeform);
        Assert.DoesNotContain("\"c1\"", freeform);

        var structured = CanvasSnapshot.Capture(document, Structured);
        Assert.Contains("\"c1\"", structured);
        Assert.DoesNotContain("\"o1\"", structured);
        Assert.Equal(2, CanvasSnapshot.Count(freeform));
    }

    [Fact]
    public void Restore_OnlyReplacesOneSurface()
    {
        var document = BuildDocument();
        var snapshot = CanvasSnapshot.Capture(document, Freeform);

        // 两个表面各动一下
        document.Objects.Add(new CanvasObject { Id = "o9", Surface = Freeform, X = 10, Y = 10 });
        document.Cards.Add(new CanvasItem { Id = "c9", Surface = Structured, X = 10, Y = 10 });

        CanvasSnapshot.Restore(document, Freeform, snapshot);

        Assert.DoesNotContain(document.Objects, item => item.Id == "o9");
        Assert.Contains(document.Objects, item => item.Id == "o1");
        Assert.Equal(2, document.Objects.Count(item => item.Surface == Freeform));

        // 结构化表面原样保留（含新加的 c9）
        Assert.Contains(document.Cards, card => card.Id == "c1");
        Assert.Contains(document.Cards, card => card.Id == "c9");
    }

    [Fact]
    public void Restore_KeepsCardGeometry()
    {
        var document = BuildDocument();
        var snapshot = CanvasSnapshot.Capture(document, Structured);

        var card = document.Cards.Single(item => item.Id == "c1");
        card.X = 999;
        card.Y = 888;

        CanvasSnapshot.Restore(document, Structured, snapshot);

        var restored = document.Cards.Single(item => item.Id == "c1");
        Assert.Equal(40, restored.X);
        Assert.Equal(60, restored.Y);
    }

    [Fact]
    public void Restore_BadJson_IsIgnored()
    {
        var document = BuildDocument();
        var before = document.Objects.Count;

        CanvasSnapshot.Restore(document, Freeform, "{ not json");
        CanvasSnapshot.Restore(document, Freeform, "");

        Assert.Equal(before, document.Objects.Count);
    }

    [Fact]
    public void UndoSequence_DoesNotTouchOtherSurface()
    {
        // V-13 的 Core 版：A 面压 3 步、B 面压 2 步，回 A 撤 3 次，B 面的栈不动
        var undo = new SurfaceUndo();
        var freeformNow = "freeform-v3";

        undo.Push(Freeform, "freeform-v0");
        undo.Push(Freeform, "freeform-v1");
        undo.Push(Freeform, "freeform-v2");
        undo.Push(Structured, "structured-v0");
        undo.Push(Structured, "structured-v1");

        var steps = 0;
        while (undo.TryUndo(Freeform, freeformNow, out var restored))
        {
            freeformNow = restored;
            steps++;
        }

        Assert.Equal(3, steps);
        Assert.Equal("freeform-v0", freeformNow);
        Assert.Equal(2, undo.Depth(Structured));
    }

    private static CanvasDocument BuildDocument()
    {
        var document = new CanvasDocument();
        document.Cards.Add(new CanvasItem { Id = "c1", Surface = Structured, Kind = "note", X = 40, Y = 60, W = 200, H = 120 });
        document.Objects.Add(new CanvasObject { Id = "o1", Surface = Freeform, Kind = CanvasObject.KindText, X = 100, Y = 100 });
        document.Objects.Add(new CanvasObject { Id = "o2", Surface = Freeform, Kind = CanvasObject.KindImage, X = 300, Y = 100 });
        return document;
    }
}
