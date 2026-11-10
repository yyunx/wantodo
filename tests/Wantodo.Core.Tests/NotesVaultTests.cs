using System;
using System.IO;
using System.Linq;
using Wantodo.Core;

namespace Wantodo.Core.Tests;

public sealed class NotesVaultTests : IDisposable
{
    private readonly string _root;
    private readonly NotesVault _vault;

    public NotesVaultTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wantodo-notes-tests", Guid.NewGuid().ToString("N"));
        _vault = new NotesVault(Path.Combine(_root, "notes"), Path.Combine(_root, "trash"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 测试清理失败不影响结论。
        }
    }

    [Fact]
    public void Scan_OnEmptyVault_ReturnsNothing()
    {
        Assert.Empty(_vault.Scan());
    }

    [Fact]
    public void Create_Txt_WritesTitleAsFirstLine_AndScanReadsItBack()
    {
        var result = _vault.Create(NoteFormat.Txt, "买牛奶");

        Assert.True(result.Success, result.Error);
        var doc = result.Document!;
        Assert.EndsWith(".txt", doc.FullPath);
        Assert.Equal("买牛奶", doc.Title);
        Assert.Equal(string.Empty, doc.Body);
        Assert.StartsWith("inbox/", doc.Id);

        var scanned = Assert.Single(_vault.Scan());
        Assert.Equal("买牛奶", scanned.Title);
        Assert.Equal(NoteFormat.Txt, scanned.Format);
        Assert.Equal(doc.Id, scanned.Id);
    }

    [Fact]
    public void Create_Md_WritesFrontMatter_AndRoundTripsTitleAndTags()
    {
        var result = _vault.Create(NoteFormat.Md, "论文精读");
        Assert.True(result.Success, result.Error);

        var doc = result.Document!;
        var withBody = _vault.Write(doc, "论文精读", "第一段\n第二段", new[] { "paper", "因果" })!;

        var text = File.ReadAllText(doc.FullPath);
        Assert.StartsWith("---\n", text);
        Assert.Contains("title: 论文精读", text);
        Assert.Contains("tags: [paper, 因果]", text);

        var reread = _vault.Read(doc.Id)!;
        Assert.Equal("论文精读", reread.Title);
        Assert.Equal("第一段\n第二段", reread.Body);
        Assert.Equal(new[] { "paper", "因果" }, reread.Tags);
        Assert.True(Math.Abs((reread.UpdatedAt - withBody.UpdatedAt).TotalSeconds) < 2);
    }

    [Fact]
    public void Create_SameTitleTwice_GetsNumericSuffix()
    {
        var first = _vault.Create(NoteFormat.Txt, "同名").Document!;
        var second = _vault.Create(NoteFormat.Txt, "同名").Document!;

        Assert.NotEqual(first.FullPath, second.FullPath);
        Assert.EndsWith("-2.txt", second.FullPath);
    }

    [Fact]
    public void Write_ChangesBodyAndTitle_OnDisk()
    {
        var doc = _vault.Create(NoteFormat.Txt, "旧标题").Document!;
        var updated = _vault.Write(doc, "新标题", "正文第二行")!;

        Assert.Equal("新标题", updated.Title);
        Assert.Equal("正文第二行", updated.Body);
        Assert.Contains("新标题", File.ReadAllText(doc.FullPath));

        var reread = _vault.Read(doc.Id)!;
        Assert.Equal("新标题", reread.Title);
        Assert.Equal("正文第二行", reread.Body);
    }

    [Fact]
    public void Scan_RecursesIntoYearMonthFolders_AndIgnoresOtherExtensions()
    {
        var nested = Path.Combine(_vault.Root, "2026", "11");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "20261110-1300-笔记.txt"), "深层标题\n正文");
        File.WriteAllText(Path.Combine(_vault.Root, "ignore.json"), "{}");
        File.WriteAllText(Path.Combine(_vault.Root, ".hidden.md"), "---\ntitle: x\n---");

        var all = _vault.Scan();

        var doc = Assert.Single(all);
        Assert.Equal("深层标题", doc.Title);
        Assert.Equal("2026/11/20261110-1300-笔记.txt", doc.Id);
    }

    [Fact]
    public void Scan_SeesExternalEdits_WithoutAnyIndex()
    {
        var path = Path.Combine(_vault.Root, "inbox", "20261110-1310-外部.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\ntitle: 外部改的\ncreated: 2026-11-10T13:10:00+08:00\n---\n\n外部正文\n");

        var doc = Assert.Single(_vault.Scan());

        Assert.Equal("外部改的", doc.Title);
        Assert.Equal("外部正文", doc.Body);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 13, 10, 0, TimeSpan.FromHours(8)), doc.CreatedAt);
    }

    [Fact]
    public void Trash_MovesFileOut_AndItDisappearsFromScan()
    {
        var doc = _vault.Create(NoteFormat.Txt, "要删的").Document!;

        Assert.True(_vault.Trash(doc));
        Assert.False(File.Exists(doc.FullPath));
        Assert.Empty(_vault.Scan());

        var trashed = Assert.Single(Directory.GetFiles(_vault.TrashRoot));
        Assert.Contains("要删的", trashed);
    }

    [Fact]
    public void Rename_KeepsTimestampPrefix_AndMovesFile()
    {
        var doc = _vault.Create(NoteFormat.Txt, "未命名").Document!;
        var renamed = _vault.Rename(doc, "买牛奶")!;

        Assert.NotEqual(doc.FullPath, renamed.FullPath);
        Assert.EndsWith("-买牛奶.txt", renamed.FullPath);
        Assert.Equal(doc.CreatedAt.Date, renamed.CreatedAt.Date);
        Assert.False(File.Exists(doc.FullPath));
        Assert.Equal(renamed.Id, _vault.Scan().Single().Id);
    }

    [Fact]
    public void Rename_Collision_GetsSuffix()
    {
        var first = _vault.Create(NoteFormat.Txt, "未命名").Document!;
        var second = _vault.Create(NoteFormat.Txt, "未命名").Document!;
        _vault.Rename(first, "同名");
        var renamed = _vault.Rename(second, "同名")!;

        Assert.EndsWith("-同名-2.txt", renamed.FullPath);
    }

    [Fact]
    public void Read_MissingId_ReturnsNull()
    {
        Assert.Null(_vault.Read("inbox/不存在.txt"));
    }

    [Fact]
    public void Write_Md_RefreshesUpdatedButKeepsCreated()
    {
        var doc = _vault.Create(NoteFormat.Md, "时间").Document!;
        System.Threading.Thread.Sleep(20);
        var updated = _vault.Write(doc, "时间", "变了")!;

        var text = File.ReadAllText(doc.FullPath);
        Assert.Contains("created: " + doc.CreatedAt.ToString("yyyy-MM-ddTHH:mm:sszzz"), text);
        Assert.Contains("updated: " + updated.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:sszzz"), text);
    }

    [Theory]
    [InlineData("买牛奶", "买牛奶")]
    [InlineData("AgentTrace 精读（多智能体）", "AgentTrace-精读多智能体")]
    [InlineData("a/b\\c:d", "abcd")]
    [InlineData("   ", "note")]
    [InlineData("2026-11-10 计划", "2026-11-10-计划")]
    public void Slug_ProducesFileNameSafeFragment(string title, string expected)
    {
        Assert.Equal(expected, NotesVault.Slug(title));
    }
}
