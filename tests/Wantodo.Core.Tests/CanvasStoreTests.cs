using System.Text.Json;
using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

public sealed class CanvasStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wantodo-canvas-" + Guid.NewGuid().ToString("N"));

    public CanvasStoreTests() => Directory.CreateDirectory(_dir);

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

    private CanvasStore Store() => new(Path.Combine(_dir, "canvas.json"));

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
    public void SaveThenLoad_RoundTripsItemsAndPayload()
    {
        var store = Store();
        var document = new CanvasDocument();
        var item = document.Add("link", 16, 24);
        item.Payload["url"] = "https://example.com";
        item.W = 300;
        item.H = 120;

        store.Save(document);
        var loaded = store.Load();

        var restored = Assert.Single(loaded.Items);
        Assert.Equal(item.Id, restored.Id);
        Assert.Equal("link", restored.Kind);
        Assert.Equal(16, restored.X);
        Assert.Equal(300, restored.W);
        Assert.Equal("https://example.com", restored.Get("url"));
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyCanvas()
    {
        var loaded = Store().Load();

        Assert.Equal(1, loaded.Version);
        Assert.Empty(loaded.Items);
    }

    [Fact]
    public void Load_CorruptFile_QuarantinesAndReturnsEmpty()
    {
        var store = Store();
        File.WriteAllText(store.FilePath, "{ not json");

        var loaded = store.Load();

        Assert.Empty(loaded.Items);
        Assert.True(Directory.EnumerateFiles(_dir, "canvas.json.corrupt-*").Any());
    }

    [Fact]
    public void Save_SecondTime_LeavesBackup()
    {
        var store = Store();
        var document = new CanvasDocument();
        document.Add("note", 1, 2);
        store.Save(document);
        document.Add("todo", 3, 4);
        store.Save(document);

        Assert.True(File.Exists(store.BackupPath));
    }

    [Fact]
    public void BoardAndCanvas_UseSeparateFiles()
    {
        var boardStore = new JsonBoardStore(Path.Combine(_dir, "data.json"));
        var canvasStore = Store();
        var board = Board.CreateDefault();
        var canvas = new CanvasDocument();
        canvas.Add("note", 10, 10);

        boardStore.Save(board);
        canvasStore.Save(canvas);
        boardStore.Save(board);

        Assert.True(File.Exists(boardStore.DataPath));
        Assert.True(File.Exists(canvasStore.FilePath));
        Assert.Single(canvasStore.Load().Items);
        Assert.Equal(Board.DockPanelMode, boardStore.Load().Board.PanelMode);
    }

    [Fact]
    public void PanelMode_DefaultsToDockAndUnknownFallsBack()
    {
        var board = new Board();
        Assert.Equal(Board.DockPanelMode, board.PanelMode);

        board.PanelMode = "Canvas";
        board.NormalizePanel();
        Assert.Equal(Board.CanvasPanelMode, board.PanelMode);

        board.PanelMode = "something-else";
        board.NormalizePanel();
        Assert.Equal(Board.DockPanelMode, board.PanelMode);
    }
}
