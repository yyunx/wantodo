using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

/// <summary>0.7.0 P0.3：画布只住 data.json，不再有第二份 canvas.json。对应验收 V-05。</summary>
public sealed class CanvasDocumentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wantodo-canvas-" + Guid.NewGuid().ToString("N"));

    public CanvasDocumentTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言
        }
    }

    private JsonBoardStore Store() => new(Path.Combine(_dir, "data.json"));

    [Fact]
    public void Normalize_ClampsSizeAndPosition()
    {
        var item = new CanvasItem { Id = "c1", Kind = "note", X = -4, Y = double.NaN, W = 10, H = 5000, ZIndex = 0 };

        item.Normalize();

        Assert.Equal(0, item.X);
        Assert.Equal(0, item.Y);
        Assert.Equal(CanvasItem.MinWidth, item.W);
        Assert.Equal(CanvasItem.MaxHeight, item.H);
        Assert.Equal(1, item.ZIndex);
    }

    [Fact]
    public void Normalize_RemovesEmptyIdsAndRenumbersDuplicates()
    {
        var document = new CanvasDocument
        {
            Items =
            {
                new CanvasItem { Id = "" },
                new CanvasItem { Id = "c1" },
                new CanvasItem { Id = "c1" },
            },
        };

        var removed = document.Normalize();

        Assert.Equal(1, removed);
        Assert.Equal(2, document.Items.Count);
        Assert.Equal(2, document.Items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Add_AssignsIncreasingZ()
    {
        var document = new CanvasDocument();
        var first = document.Add("note", 10, 20);
        var second = document.Add("timer", 30, 40);

        Assert.Equal("c1", first.Id);
        Assert.Equal("c2", second.Id);
        Assert.Equal(2, second.ZIndex);
        Assert.Equal(10, first.X);
        Assert.Equal(20, first.Y);
    }

    [Fact]
    public void Board_RoundTripsCardsObjectsAndViewports()
    {
        var store = Store();
        var board = Board.CreateDefault();
        var card = board.Canvas.Add("link", 16, 24);
        card.Payload["url"] = "https://example.com";
        card.W = 300;
        card.H = 120;
        var stroke = board.Canvas.AddObject("stroke", 40, 50, 120, 80);
        stroke.Payload["points"] = "0,0;10,10";
        board.Canvas.ActiveSurfaceId = CanvasSurfaces.Freeform;
        board.Canvas.Viewports[CanvasSurfaces.Freeform] = new CanvasViewport { Zoom = 1.75, OffsetX = -30, OffsetY = 12 };

        store.Save(board);
        var loaded = store.Load().Board;

        var restored = Assert.Single(loaded.Canvas.Items);
        Assert.Equal("link", restored.Kind);
        Assert.Equal(300, restored.W);
        Assert.Equal("https://example.com", restored.Get("url"));
        var restoredStroke = Assert.Single(loaded.Canvas.Objects);
        Assert.Equal("stroke", restoredStroke.Kind);
        Assert.Equal("0,0;10,10", restoredStroke.Get("points"));
        Assert.Equal(CanvasSurfaces.Freeform, loaded.Canvas.ActiveSurfaceId);
        Assert.Equal(1.75, loaded.Canvas.Viewports[CanvasSurfaces.Freeform].Zoom);
    }

    [Fact]
    public void Canvas_LivesInsideDataJson_NotASecondFile()
    {
        var store = Store();
        var board = Board.CreateDefault();
        board.Canvas.Add("note", 10, 10);

        store.Save(board);

        Assert.Empty(Directory.EnumerateFiles(_dir, "canvas.json*"));
        Assert.Contains("\"canvas\"", File.ReadAllText(store.DataPath));
        Assert.Single(store.Load().Board.Canvas.Items);
    }

    [Fact]
    public void DataPaths_NoLongerExposesCanvasFile()
    {
        // V-05：全仓不应再有第二个画布文件路径；属性名也一并删掉，防止有人再拿它写盘。
        Assert.Null(typeof(DataPaths).GetProperty("CanvasFile"));
        Assert.NotNull(typeof(DataPaths).GetProperty("BoardFile"));
        Assert.NotNull(typeof(DataPaths).GetProperty("CanvasImagesDir"));
    }

    [Fact]
    public void Save_SecondTime_LeavesBackup()
    {
        var store = Store();
        var board = Board.CreateDefault();
        board.Canvas.Add("note", 1, 2);
        store.Save(board);
        board.Canvas.Add("todo", 3, 4);
        store.Save(board);

        Assert.True(File.Exists(store.BackupPath));
        Assert.Equal(2, store.Load().Board.Canvas.Items.Count);
    }

    [Fact]
    public void Load_MissingFile_StartsWithEmptyCanvas()
    {
        var loaded = Store().Load();

        Assert.Equal(BoardLoadStatus.FirstRun, loaded.Status);
        Assert.Empty(loaded.Board.Canvas.Items);
        Assert.Equal(2, loaded.Board.Canvas.Version);
    }

    [Fact]
    public void PanelMode_DefaultsToCanvasAndUnknownFallsBackToDock()
    {
        var board = new Board();
        Assert.Equal(Board.CanvasPanelMode, board.PanelMode);

        board.PanelMode = "Dock";
        board.NormalizePanel();
        Assert.Equal(Board.DockPanelMode, board.PanelMode);

        board.PanelMode = "something-else";
        board.NormalizePanel();
        Assert.Equal(Board.DockPanelMode, board.PanelMode);
    }
}
