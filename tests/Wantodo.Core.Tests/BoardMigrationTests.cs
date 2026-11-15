using System.Text.Json;
using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

/// <summary>0.7.0 数据迁移：data.json v2 → v3 + 旧 canvas.json 并入。对应验收 V-02 / V-03。</summary>
public sealed class BoardMigrationTests : IDisposable
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wantodo-mig-" + Guid.NewGuid().ToString("N"));

    public BoardMigrationTests() => Directory.CreateDirectory(_dir);

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

    private string DataPath => Path.Combine(_dir, "data.json");

    private string CanvasPath => Path.Combine(_dir, "canvas.json");

    private JsonBoardStore Store() => new(DataPath);

    private void WriteOldBoard(string json) => File.WriteAllText(DataPath, json);

    private void WriteCanvas(string json) => File.WriteAllText(CanvasPath, json);

    [Fact]
    public void Upgrade_VersionTwoBoard_SetsVersionThreeAndEmptyCanvas()
    {
        var board = Board.CreateDefault();
        board.Version = 2;
        board.Canvas = null!;

        var diagnostics = new List<string>();
        BoardMigration.Upgrade(board, diagnostics);

        Assert.Equal(BoardMigration.CurrentVersion, board.Version);
        Assert.NotNull(board.Canvas);
        Assert.Equal(CanvasSurfaces.Structured, board.Canvas.ActiveSurfaceId);
        Assert.Empty(board.Canvas.Cards);
        Assert.Empty(board.Canvas.Objects);
        Assert.Contains(diagnostics, d => d.Contains("→ 3"));
    }

    [Fact]
    public void Load_OldBoardAndLegacyCanvas_MergesAndKeepsBackups()
    {
        WriteOldBoard("""
        {
          "version": 2,
          "theme": "dracula",
          "formId": "brutal",
          "columns": [ { "id": "col1", "title": "要做的事情", "notes": [ { "id": "n1", "text": "旧条目" } ] } ]
        }
        """);
        WriteCanvas("""
        {
          "version": 1,
          "items": [
            { "id": "c1", "kind": "todo", "x": 40, "y": 60, "w": 260, "h": 200,
              "payload": { "columnId": "col1" } },
            { "id": "c2", "kind": "image", "x": 520, "y": 80, "w": 320, "h": 200,
              "payload": { "path": "canvas-images/abc.png" } }
          ]
        }
        """);

        var store = Store();
        var result = store.Load();

        Assert.Equal(BoardLoadStatus.Loaded, result.Status);
        var board = result.Board;
        Assert.Equal(BoardMigration.CurrentVersion, board.Version);
        Assert.Single(board.Columns);
        Assert.Single(board.Canvas.Cards);
        Assert.Equal("todo", board.Canvas.Cards[0].Kind);
        Assert.Single(board.Canvas.Objects);
        Assert.Equal("image", board.Canvas.Objects[0].Kind);

        // 备份与旧文件改名都在
        Assert.True(File.Exists(Path.Combine(_dir, BoardMigration.PreUpgradeBackupName)));
        Assert.False(File.Exists(CanvasPath));
        Assert.Single(Directory.GetFiles(_dir, "canvas.imported-*.json"));

        // 合并结果已落盘：重新加载读到的是 data.json 里的 canvas
        var reloaded = Store().Load().Board;
        Assert.Equal(3, reloaded.Version);
        Assert.Single(reloaded.Canvas.Cards);
        Assert.Single(reloaded.Canvas.Objects);
    }

    [Fact]
    public void Load_CorruptLegacyCanvas_DoesNotCrashAndKeepsFile()
    {
        WriteOldBoard("""{ "version": 2, "columns": [] }""");
        WriteCanvas("""{ "version": 1, "items": [ { "id": "c1" """);

        var store = Store();
        var result = store.Load();

        Assert.Equal(BoardLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Board.Canvas.Cards);
        Assert.True(File.Exists(CanvasPath), "解析失败时不许动旧文件");
        Assert.Contains(store.Diagnostics, d => d.Contains("canvas.json 解析失败"));
    }

    [Fact]
    public void Load_LegacyCanvasWithoutBoard_ImportsIntoDefaultBoard()
    {
        WriteCanvas("""
        { "version": 1, "items": [ { "id": "c1", "kind": "note", "payload": { "noteId": "n1" } } ] }
        """);

        var store = Store();
        var result = store.Load();

        Assert.Equal(BoardLoadStatus.FirstRun, result.Status);
        Assert.Single(result.Board.Canvas.Cards);
        Assert.True(File.Exists(DataPath), "导入后必须立即落盘，避免下次再导入一遍");
    }

    [Fact]
    public void Import_WhenBoardAlreadyHasCanvas_DoesNotOverwrite()
    {
        var board = new Board();
        board.Canvas.Add("note", 10, 10);
        WriteCanvas("""{ "version": 1, "items": [ { "id": "c9", "kind": "note" } ] }""");

        var diagnostics = new List<string>();
        var result = CanvasLegacyImport.Import(board, CanvasPath, diagnostics);

        Assert.False(result.Imported);
        Assert.Single(board.Canvas.Cards);
        Assert.Equal("c1", board.Canvas.Cards[0].Id);
        Assert.True(File.Exists(CanvasPath));
        Assert.Contains(diagnostics, d => d.Contains("不覆盖"));
    }

    [Fact]
    public void Load_UnknownFields_AreIgnored()
    {
        WriteOldBoard("""
        { "version": 2, "theme": "SkyNet", "formId": "unknown-form", "columns": [], "futureField": 42 }
        """);

        var result = Store().Load();

        Assert.Equal(BoardLoadStatus.Loaded, result.Status);
        Assert.Equal(3, result.Board.Version);
        Assert.NotNull(result.Board.Canvas);
    }

    [Fact]
    public void CanvasDocument_Normalize_FillsViewportsAndDedupes()
    {
        var document = new CanvasDocument
        {
            ActiveSurfaceId = "weird",
            Cards = new List<CanvasItem>
            {
                new() { Id = "c1", Kind = "note" },
                new() { Id = "c1", Kind = "todo" },
                new() { Id = "", Kind = "note" },
            },
            Objects = new List<CanvasObject>
            {
                new() { Id = "c1", Kind = "image" },
            },
        };

        var removed = document.Normalize();

        Assert.Equal(1, removed); // 空 id 卡片被删
        Assert.Equal(CanvasSurfaces.Structured, document.ActiveSurfaceId);
        Assert.Equal(2, document.Cards.Count);
        Assert.NotEqual(document.Cards[0].Id, document.Cards[1].Id);
        Assert.NotEqual("c1", document.Objects[0].Id); // 与卡片撞 id → 重排
        Assert.True(document.Viewports.ContainsKey(CanvasSurfaces.Structured));
        Assert.True(document.Viewports.ContainsKey(CanvasSurfaces.Freeform));
        Assert.Equal(1.0, document.Viewports[CanvasSurfaces.Structured].Zoom);
    }

    [Fact]
    public void CanvasDocument_SerializesCardsAsCardsAndItemsAliasDoesNotDuplicate()
    {
        var document = new CanvasDocument();
        document.Add("note", 10, 20);

        var json = JsonSerializer.Serialize(document, CamelCase);

        Assert.Contains("\"cards\"", json);
        Assert.DoesNotContain("\"items\"", json);
        Assert.Contains("\"viewports\"", json);

        var round = JsonSerializer.Deserialize<CanvasDocument>(json, CamelCase)!;
        Assert.Single(round.Cards);
        Assert.Equal(2, round.Version);
    }
}
