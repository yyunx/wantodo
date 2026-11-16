using System;
using System.IO;
using System.Linq;
using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

/// <summary>P2.2：孤儿资产清理把「没人引用」的文件挪进回收目录，引用中的一个都不能动。</summary>
public sealed class AssetCleanupTests : IDisposable
{
    private readonly string _root;

    public AssetCleanupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wantodo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 测试目录清不掉不影响断言
        }
    }

    private string Touch(string relative, int bytes = 8)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static CanvasObject Stroke(string id, string file) => new()
    {
        Id = id,
        Kind = CanvasObject.KindStroke,
        Payload = { ["path"] = file },
    };

    private static CanvasObject Image(string id, string file) => new()
    {
        Id = id,
        Kind = CanvasObject.KindImage,
        Payload = { ["path"] = "canvas-images/" + file },
    };

    [Fact]
    public void Run_MovesUnreferencedAndKeepsReferenced()
    {
        var inkDir = Path.Combine(_root, "ink");
        var imagesDir = Path.Combine(_root, "canvas-images");
        var trashDir = Path.Combine(_root, "trash");
        Touch("ink/o1.isf", 100);
        Touch("ink/o2.isf", 200);
        Touch("canvas-images/aaa.png", 300);
        Touch("canvas-images/bbb.png", 400);

        var document = new CanvasDocument();
        document.Objects.Add(Stroke("o1", "o1.isf"));
        document.Objects.Add(Image("c1", "aaa.png"));

        var result = AssetCleanup.Run(document, inkDir, imagesDir, trashDir);

        Assert.Equal(2, result.MovedFiles);
        Assert.Equal(600, result.MovedBytes);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(Path.Combine(inkDir, "o1.isf")));
        Assert.False(File.Exists(Path.Combine(inkDir, "o2.isf")));
        Assert.True(File.Exists(Path.Combine(imagesDir, "aaa.png")));
        Assert.False(File.Exists(Path.Combine(imagesDir, "bbb.png")));

        var trashed = Directory.EnumerateFiles(trashDir, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(new[] { "bbb.png", "o2.isf" }, trashed);
    }

    [Fact]
    public void Run_WithoutTrashDirectoryMovesNothing()
    {
        var inkDir = Path.Combine(_root, "ink");
        Touch("ink/orphan.isf", 10);

        var result = AssetCleanup.Run(new CanvasDocument(), inkDir, null, trashDir: null);

        Assert.Equal(0, result.MovedFiles);
        Assert.True(File.Exists(Path.Combine(inkDir, "orphan.isf")));
    }

    [Fact]
    public void Run_LeavesTemporaryFilesAlone()
    {
        var inkDir = Path.Combine(_root, "ink");
        Touch("ink/half.isf.tmp", 16);

        var result = AssetCleanup.Run(new CanvasDocument(), inkDir, null, Path.Combine(_root, "trash"));

        Assert.Equal(0, result.MovedFiles);
        Assert.True(File.Exists(Path.Combine(inkDir, "half.isf.tmp")));
    }

    [Fact]
    public void Run_MissingDirectoriesIsFine()
    {
        var result = AssetCleanup.Run(
            new CanvasDocument(),
            Path.Combine(_root, "nope"),
            null,
            Path.Combine(_root, "trash"));

        Assert.Equal(0, result.MovedFiles);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Run_IgnoresAbsolutePaths()
    {
        var inkDir = Path.Combine(_root, "ink");
        var absolute = Touch("elsewhere/shared.isf", 8);

        var document = new CanvasDocument();
        document.Objects.Add(Stroke("o9", absolute));

        var result = AssetCleanup.Run(document, inkDir, null, Path.Combine(_root, "trash"));

        Assert.Equal(0, result.MovedFiles);
        Assert.True(File.Exists(absolute));
    }

    [Fact]
    public void Run_KeepsImageWhosePathHasDirectoryPrefix()
    {
        var imagesDir = Path.Combine(_root, "canvas-images");
        Touch("canvas-images/keep.png", 32);

        var document = new CanvasDocument();
        document.Objects.Add(Image("c1", "keep.png"));

        var result = AssetCleanup.Run(document, null, imagesDir, Path.Combine(_root, "trash"));

        Assert.Equal(0, result.MovedFiles);
        Assert.Single(Directory.EnumerateFiles(imagesDir));
    }


}
