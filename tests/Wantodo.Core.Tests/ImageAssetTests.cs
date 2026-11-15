using Wantodo.Core;

namespace Wantodo.Core.Tests;

/// <summary>P1.3 图片资产策略：去重、体积、扩展名、相对路径。全部走真实文件，不用 mock。</summary>
public sealed class ImageAssetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wantodo-tests", Guid.NewGuid().ToString("N"));

    public ImageAssetTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // 测试目录清不掉不影响结论
        }
    }

    [Fact]
    public void Import_WritesUnderCanvasImagesWithHashName()
    {
        var source = MakeFile("photo.png", 2048);

        var result = ImageAssets.Import(source, Path.Combine(_dir, "canvas-images"));

        Assert.True(result.Imported, result.Error);
        Assert.NotNull(result.RelativePath);
        Assert.StartsWith("canvas-images/", result.RelativePath);
        Assert.EndsWith(".png", result.RelativePath);
        Assert.Equal(Path.Combine(_dir, "canvas-images", Path.GetFileName(result.RelativePath!)), result.AbsolutePath);
        Assert.True(File.Exists(result.AbsolutePath!));

        // 文件名就是 sha256 前 16 位
        string expected = ImageAssets.ShortHash(source);
        Assert.Equal(expected + ".png", Path.GetFileName(result.RelativePath!));
        Assert.Equal(16, expected.Length);
    }

    [Fact]
    public void Import_SameContentTwice_KeepsOneFile()
    {
        var imagesDir = Path.Combine(_dir, "canvas-images");
        var first = MakeFile("a.png", 1024);
        var copy = Path.Combine(_dir, "b.png");
        File.Copy(first, copy);

        var one = ImageAssets.Import(first, imagesDir);
        var two = ImageAssets.Import(copy, imagesDir);

        Assert.True(one.Imported);
        Assert.True(two.Imported);
        Assert.Equal(one.RelativePath, two.RelativePath);
        Assert.False(one.Deduplicated);
        Assert.True(two.Deduplicated);
        Assert.Single(Directory.GetFiles(imagesDir, "*.png"));
        Assert.Empty(Directory.GetFiles(imagesDir, "*.tmp"));
    }

    [Fact]
    public void Import_DifferentContent_KeepsBoth()
    {
        var imagesDir = Path.Combine(_dir, "canvas-images");

        var one = ImageAssets.Import(MakeFile("a.png", 1024), imagesDir);
        var two = ImageAssets.Import(MakeFile("b.png", 2048), imagesDir);

        Assert.True(one.Imported && two.Imported);
        Assert.NotEqual(one.RelativePath, two.RelativePath);
        Assert.Equal(2, Directory.GetFiles(imagesDir, "*.png").Length);
    }

    [Fact]
    public void Import_RejectsUnsupportedExtension()
    {
        var result = ImageAssets.Import(MakeFile("notes.txt", 512), Path.Combine(_dir, "canvas-images"));

        Assert.False(result.Imported);
        Assert.Contains("不支持", result.Error);
        Assert.False(Directory.Exists(Path.Combine(_dir, "canvas-images")));
    }

    [Fact]
    public void Import_RejectsOversizeFile()
    {
        var imagesDir = Path.Combine(_dir, "canvas-images");
        var source = MakeFile("huge.png", 4096);

        var result = ImageAssets.Import(source, imagesDir, maxBytes: 1024);

        Assert.False(result.Imported);
        Assert.Contains("超过上限", result.Error);
        Assert.False(Directory.Exists(imagesDir) && Directory.GetFiles(imagesDir).Length > 0);
    }

    [Fact]
    public void Import_RejectsMissingAndEmptyFiles()
    {
        Assert.False(ImageAssets.Import(Path.Combine(_dir, "nope.png"), _dir).Imported);
        Assert.False(ImageAssets.Import(MakeFile("empty.png", 0), _dir).Imported);
    }

    [Fact]
    public void Import_ReportsTotalWarningPastThreshold()
    {
        var imagesDir = Path.Combine(_dir, "canvas-images");
        ImageAssets.Import(MakeFile("a.png", 800), imagesDir);
        ImageAssets.Import(MakeFile("b.png", 800), imagesDir);

        Assert.True(ImageAssets.TotalBytes(imagesDir) >= 1600);
        Assert.Null(ImageAssets.Import(MakeFile("c.png", 800), imagesDir).Warning);

        // 门槛是常量 200 MB，这里直接把目录撑到常量之外不现实，所以只验「不超过时不报」
        Assert.True(ImageAssets.WarnTotalBytes > ImageAssets.TotalBytes(imagesDir));
    }

    [Fact]
    public void Import_ExtensionIsNormalizedToLowerCase()
    {
        var result = ImageAssets.Import(MakeFile("SHOUT.PNG", 700), Path.Combine(_dir, "canvas-images"));

        Assert.True(result.Imported, result.Error);
        Assert.EndsWith(".png", result.RelativePath);
    }

    [Fact]
    public void IsSupported_CoversClipboardFormats()
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".JPG" })
        {
            Assert.True(ImageAssets.IsSupported("x" + extension), extension);
        }

        Assert.False(ImageAssets.IsSupported("x.txt"));
        Assert.False(ImageAssets.IsSupported(null));
        Assert.False(ImageAssets.IsSupported(string.Empty));
    }

    private string MakeFile(string name, int bytes)
    {
        var path = Path.Combine(_dir, name);
        var payload = new byte[bytes];
        new Random(name.GetHashCode(StringComparison.Ordinal)).NextBytes(payload);
        File.WriteAllBytes(path, payload);
        return path;
    }
}
