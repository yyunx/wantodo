using System.Text.Json;
using Wantodo.Core;

namespace Wantodo.Core.Tests;

public sealed class JsonBoardStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dataPath;

    public JsonBoardStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wantodo-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
        _dataPath = Path.Combine(_dir, "data.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private JsonBoardStore NewStore() => new(_dataPath);

    private string BackupPath => Path.Combine(_dir, "data.bak");

    private string[] CorruptFiles() => Directory.GetFiles(_dir, "data.corrupt-*.json");

    [Fact]
    public void Load_NoFiles_ReturnsFirstRunWithSeedColumns()
    {
        var result = NewStore().Load();

        Assert.Equal(BoardLoadStatus.FirstRun, result.Status);
        Assert.Equal(2, result.Board.Columns.Count);
        Assert.Equal("要做的事情", result.Board.Columns[0].Title);
        Assert.Equal("突然想到的问题", result.Board.Columns[1].Title);
        Assert.False(File.Exists(_dataPath));
    }

    [Fact]
    public void Save_FirstTime_WritesDataWithoutBackupOrLeftoverTemp()
    {
        var store = NewStore();
        var board = Board.CreateDefault();
        board.Placement.Left = 120;
        board.Placement.Top = 80;

        store.Save(board);

        Assert.True(File.Exists(_dataPath));
        Assert.False(File.Exists(BackupPath));
        Assert.False(File.Exists(_dataPath + ".tmp"));
        Assert.Empty(store.Diagnostics);
    }

    [Fact]
    public void Save_SecondTime_KeepsPreviousContentInBackup()
    {
        var store = NewStore();
        var board = Board.CreateDefault();
        store.Save(board);

        board.Columns[0].Title = "改过的标题";
        store.Save(board);

        Assert.True(File.Exists(BackupPath));

        var backupJson = File.ReadAllText(BackupPath);
        Assert.Contains("要做的事情", backupJson);

        var currentJson = File.ReadAllText(_dataPath);
        Assert.Contains("改过的标题", currentJson);
        Assert.False(File.Exists(_dataPath + ".tmp"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = NewStore();
        var board = Board.CreateDefault();
        board.Surface = "Topmost";
        board.Theme = "Light";
        board.Tone = 0.4;
        board.Placement = new WindowPlacement { Left = 33.5, Top = 66.25, Width = 720, Height = 480 };
        board.Columns[1].Notes.Add(new Note { Text = "第三条", Completed = true });
        store.Save(board);

        var loaded = NewStore().Load();

        Assert.Equal(BoardLoadStatus.Loaded, loaded.Status);
        Assert.Equal("Topmost", loaded.Board.Surface);
        Assert.Equal("Light", loaded.Board.Theme);
        Assert.Equal(0.4, loaded.Board.Tone, 3);
        Assert.True(loaded.Board.Placement.HasPosition);
        Assert.Equal(33.5, loaded.Board.Placement.Left!.Value, 3);
        Assert.Equal(720, loaded.Board.Placement.Width, 3);
        Assert.Equal(2, loaded.Board.Columns.Count);
        Assert.Equal("第三条", loaded.Board.Columns[1].Notes[^1].Text);
        Assert.True(loaded.Board.Columns[1].Notes[^1].Completed);
    }

    [Fact]
    public void Load_PrimaryCorrupt_FallsBackToBackupAndQuarantinesIt()
    {
        var store = NewStore();
        var board = Board.CreateDefault();
        store.Save(board);
        board.Columns[0].Title = "第二版";
        store.Save(board);   // 此时 data.bak 里是"要做的事情"那一版

        File.WriteAllText(_dataPath, "{ 这不是 json ");

        var reloaded = NewStore().Load();

        Assert.Equal(BoardLoadStatus.RecoveredFromBackup, reloaded.Status);
        Assert.Equal("要做的事情", reloaded.Board.Columns[0].Title);
        Assert.Single(CorruptFiles());
        Assert.False(File.Exists(_dataPath));
        Assert.NotEmpty(reloaded.Message ?? string.Empty);
    }

    [Fact]
    public void Load_PrimaryMissingButBackupPresent_RecoversFromBackup()
    {
        var store = NewStore();
        store.Save(Board.CreateDefault());
        store.Save(Board.CreateDefault());   // 产生 data.bak
        File.Delete(_dataPath);

        var result = NewStore().Load();

        Assert.Equal(BoardLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal(2, result.Board.Columns.Count);
    }

    [Fact]
    public void Load_EmptyFile_TreatedAsCorruptAndRecovers()
    {
        var store = NewStore();
        store.Save(Board.CreateDefault());
        store.Save(Board.CreateDefault());
        File.WriteAllText(_dataPath, "   ");

        var result = NewStore().Load();

        Assert.Equal(BoardLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Single(CorruptFiles());
    }

    [Fact]
    public void Load_BothBroken_RecreatesSeedAndQuarantinesBoth()
    {
        var store = NewStore();
        store.Save(Board.CreateDefault());
        store.Save(Board.CreateDefault());
        File.WriteAllText(_dataPath, "<<<");
        File.WriteAllText(BackupPath, "<<<");

        var result = NewStore().Load();

        Assert.Equal(BoardLoadStatus.RecreatedAfterCorruption, result.Status);
        Assert.Equal(2, result.Board.Columns.Count);
        Assert.Equal(2, CorruptFiles().Length);
        Assert.False(File.Exists(_dataPath));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void Load_FieldsMissing_FillsDefaults()
    {
        File.WriteAllText(_dataPath, """{"columns":[{"title":"只有标题"}]}""");

        var result = NewStore().Load();

        Assert.Equal(BoardLoadStatus.Loaded, result.Status);
        var board = result.Board;
        Assert.Single(board.Columns);
        Assert.Equal("只有标题", board.Columns[0].Title);
        Assert.NotEmpty(board.Columns[0].Id);
        Assert.Empty(board.Columns[0].Notes);
        Assert.Equal(560, board.Placement.Width, 3);
        Assert.Equal(320, board.Placement.Height, 3);
        Assert.False(board.Placement.HasPosition);
    }

    [Fact]
    public void Load_UnknownFieldsIgnored_AndCamelCaseTolerated()
    {
        var json = JsonSerializer.Serialize(new { version = 1, columns = new[] { new { title = "X" } }, future = "字段" });
        File.WriteAllText(_dataPath, json);

        var result = NewStore().Load();

        Assert.Equal(BoardLoadStatus.Loaded, result.Status);
        Assert.Equal("X", result.Board.Columns[0].Title);
    }

    [Fact]
    public void Save_AfterRecovery_RestoresPrimaryFile()
    {
        var store = NewStore();
        store.Save(Board.CreateDefault());
        store.Save(Board.CreateDefault());
        File.WriteAllText(_dataPath, "坏掉");

        var recovered = NewStore().Load();
        Assert.Equal(BoardLoadStatus.RecoveredFromBackup, recovered.Status);

        NewStore().Save(recovered.Board);

        var reloaded = NewStore().Load();
        Assert.Equal(BoardLoadStatus.Loaded, reloaded.Status);
    }

    [Fact]
    public void DefaultDataPath_IsUnderAppDataWantodo()
    {
        var path = JsonBoardStore.DefaultDataPath();

        Assert.EndsWith(Path.Combine("wantodo", "data.json"), path);
        Assert.Contains("AppData", path);
    }
}
