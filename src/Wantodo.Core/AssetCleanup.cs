using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Wantodo.Core;

/// <summary>
/// 孤儿资产清理（plan §4.8 / P2.2）：画布只引用 <c>payload.path</c>，
/// 删除图元时为了「撤销能把笔画/图片找回来」当时没删文件（D23 / D27）。
/// 这里在启动时做一次对账：目录里存在、但没有任何图元引用的文件**挪进回收目录**，不直接删。
/// </summary>
public static class AssetCleanup
{
    /// <summary>清理结果：挪走了几个文件、多少字节、失败原因。</summary>
    public sealed record Result(int MovedFiles, long MovedBytes, IReadOnlyList<string> Errors);

    /// <summary>
    /// 把 <paramref name="inkDir"/> 与 <paramref name="imagesDir"/> 里没有被引用的文件挪到
    /// <paramref name="trashDir"/>/assets-&lt;时间戳&gt;/。目录不存在直接返回；单个文件失败只记错误，不抛。
    /// 没给回收目录时一个文件都不动（宁可留孤儿，不可误删）。
    /// </summary>
    public static Result Run(CanvasDocument? document, string? inkDir, string? imagesDir, string? trashDir)
    {
        var keepInk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keepImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document is not null)
        {
            foreach (var item in document.Objects)
            {
                var path = item.Get("path");
                if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                {
                    continue;
                }

                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (string.Equals(item.Kind, CanvasObject.KindStroke, StringComparison.OrdinalIgnoreCase))
                {
                    keepInk.Add(name);
                }
                else if (string.Equals(item.Kind, CanvasObject.KindImage, StringComparison.OrdinalIgnoreCase))
                {
                    keepImages.Add(name);
                }
            }
        }

        var target = string.IsNullOrWhiteSpace(trashDir)
            ? null
            : Path.Combine(trashDir, "assets-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        var errors = new List<string>();
        var (files, bytes) = Sweep(inkDir, keepInk, target, errors);
        var (moreFiles, moreBytes) = Sweep(imagesDir, keepImages, target, errors);
        return new Result(files + moreFiles, bytes + moreBytes, errors);
    }

    private static (int Files, long Bytes) Sweep(
        string? dir, HashSet<string> keep, string? trashTarget, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) || trashTarget is null)
        {
            return (0, 0);
        }

        var moved = 0;
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(file);
            // 写盘中间态（*.tmp）一律不碰：可能正被另一个保存覆盖。
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || keep.Contains(name))
            {
                continue;
            }

            try
            {
                var size = new FileInfo(file).Length;
                Directory.CreateDirectory(trashTarget);
                var unique = Path.Combine(trashTarget, name);
                var suffix = 1;
                while (File.Exists(unique))
                {
                    unique = Path.Combine(
                        trashTarget,
                        Path.GetFileNameWithoutExtension(name) + "-" + suffix++ + Path.GetExtension(name));
                }

                File.Move(file, unique);
                moved++;
                bytes += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"[资产清理] 挪不走 {name}：{ex.Message}");
            }
        }

        return (moved, bytes);
    }
}
