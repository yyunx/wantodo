using System.Security.Cryptography;

namespace Wantodo.Core;

/// <summary>
/// 图片资产策略（plan §4.7 / Q4）：
/// 原图存 <c>board/canvas-images/&lt;sha256 前 16 位&gt;.&lt;ext&gt;</c>，同图去重；
/// 单张超过 20 MB 拒绝；总量超过 200 MB 只提示不阻止（不能因为一次粘贴就把已有图弄丢）。
/// 纯逻辑，便于单测；不依赖 WPF。
/// </summary>
public static class ImageAssets
{
    /// <summary>单张上限（Q4：超出拒绝并提示）。</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>总量提示线（Q4：>200 MB 提示清理）。</summary>
    public const long WarnTotalBytes = 200L * 1024 * 1024;

    /// <summary>剪贴板与文件对话框都只认这几种扩展名。</summary>
    public static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    public static bool IsSupported(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把一张图收进资产目录。同内容（sha256 前 16 位相同）只留一份；
    /// 返回的 <see cref="Result.RelativePath"/> 是相对数据根的路径（<c>canvas-images/xxx.png</c>），
    /// 写进 <c>payload.path</c>，不写绝对路径（数据根搬家不该让图片全丢）。
    /// </summary>
    public static Result Import(string sourcePath, string imagesDir, long maxBytes = MaxFileBytes)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return Result.Failed($"[图片] 源文件不存在：{sourcePath}");
        }

        if (!IsSupported(sourcePath))
        {
            return Result.Failed($"[图片] 不支持的扩展名：{Path.GetExtension(sourcePath)}");
        }

        long sourceBytes;
        try
        {
            sourceBytes = new FileInfo(sourcePath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failed($"[图片] 读不到源文件大小：{ex.Message}");
        }

        if (sourceBytes <= 0)
        {
            return Result.Failed("[图片] 源文件是空的");
        }

        if (sourceBytes > maxBytes)
        {
            return Result.Failed(
                $"[图片] 单张 {FormatBytes(sourceBytes)} 超过上限 {FormatBytes(maxBytes)}，未导入");
        }

        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        string hash = ShortHash(sourcePath);
        string fileName = hash + extension;
        string target = Path.Combine(imagesDir, fileName);
        bool deduplicated = File.Exists(target);

        try
        {
            Directory.CreateDirectory(imagesDir);
            if (!deduplicated)
            {
                CopyAtomically(sourcePath, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failed($"[图片] 拷贝失败：{ex.Message}");
        }

        long total = TotalBytes(imagesDir);
        string? warning = total > WarnTotalBytes
            ? $"[图片] 画布图片共 {FormatBytes(total)}，超过 {FormatBytes(WarnTotalBytes)}，建议清理不用的图"
            : null;

        return new Result(
            Imported: true,
            RelativePath: "canvas-images/" + fileName,
            AbsolutePath: target,
            Bytes: sourceBytes,
            Deduplicated: deduplicated,
            TotalBytes: total,
            Warning: warning,
            Error: null);
    }

    /// <summary>资产目录里已有文件的总字节数（递归算，容忍中途被删）。</summary>
    public static long TotalBytes(string imagesDir)
    {
        if (string.IsNullOrWhiteSpace(imagesDir) || !Directory.Exists(imagesDir))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(imagesDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 统计口径，不该因为一个文件读不到就整体失败
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return total;
        }

        return total;
    }

    /// <summary>sha256 十六进制小写前 16 位——文件名长度可控，碰撞概率对本地图片足够低。</summary>
    public static string ShortHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024.0 * 1024.0):0.#} MB"
        : $"{Math.Max(0, bytes) / 1024.0:0.#} KB";

    private static void CopyAtomically(string sourcePath, string target)
    {
        // 与数据落盘同一条规矩：先写 .tmp，flush 到盘，再换名，避免半张图。
        string temporary = target + ".tmp";
        using (var input = File.OpenRead(sourcePath))
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(output);
            output.Flush(true);
        }

        if (File.Exists(target))
        {
            File.Delete(temporary);
            return;
        }

        File.Move(temporary, target);
    }

    public sealed record Result(
        bool Imported,
        string? RelativePath,
        string? AbsolutePath,
        long Bytes,
        bool Deduplicated,
        long TotalBytes,
        string? Warning,
        string? Error)
    {
        public static Result Failed(string error) => new(false, null, null, 0, false, 0, null, error);
    }
}
