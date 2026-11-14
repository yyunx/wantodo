using System;
using System.Collections.Generic;
using System.IO;

namespace Wantodo.Core;

/// <summary>
/// 一次「自己写盘」留下的签名：路径、字节数、文件最后写入时间、记录时刻。
/// 用来把「保存触发的文件系统事件」与「外部编辑器改文件」区分开。
/// </summary>
public sealed record NoteWriteStamp(
    string Path,
    long Length,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset RecordedAt);

/// <summary>
/// 判断文件系统事件是不是自己刚写完造成的。纯逻辑，不碰 WPF，可单测。
/// 有意的取舍：路径、长度、mtime 三者同时匹配才算自己写的；只看长度会漏判外部
/// 「同长度改写」，只看 mtime 又会被文件系统时间精度误伤，所以用 1 秒的容差。
/// </summary>
public static class NoteWriteTracker
{
    /// <summary>签名有效期：超过这个时间的签名不再参与匹配，避免陈旧签名吞掉外部改动。</summary>
    public static TimeSpan MatchWindow { get; } = TimeSpan.FromSeconds(5);

    /// <summary>mtime 容差：NTFS 够精确，但 FAT/同步盘/杀软回写可能差几百毫秒。</summary>
    public static TimeSpan TimeTolerance { get; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>读一次文件的当前签名；文件不存在时 Length = -1。</summary>
    public static NoteWriteStamp Capture(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var info = new FileInfo(full);
        return info.Exists
            ? new NoteWriteStamp(full, info.Length, new DateTimeOffset(info.LastWriteTimeUtc), DateTimeOffset.UtcNow)
            : new NoteWriteStamp(full, -1, DateTimeOffset.MinValue, DateTimeOffset.UtcNow);
    }

    /// <summary>只看笔记文件（.md / .txt，且不是隐藏文件）；写盘用的 *.tmp 一律不算。</summary>
    public static bool IsNotePath(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (name.Length == 0 || name.StartsWith('.'))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 这次事件是否来自自己刚才的保存。任一项对不上（路径 / 长度 / mtime 超出容差）
    /// 或签名已过期，都按外部改动处理。
    /// </summary>
    public static bool IsSelfWrite(
        IReadOnlyList<NoteWriteStamp> stamps,
        string path,
        long length,
        DateTimeOffset lastWriteUtc,
        DateTimeOffset now)
    {
        if (length < 0 || !IsNotePath(path))
        {
            return false;
        }

        var full = System.IO.Path.GetFullPath(path);
        foreach (var stamp in stamps)
        {
            if (now - stamp.RecordedAt > MatchWindow)
            {
                continue;
            }

            if (stamp.Length != length)
            {
                continue;
            }

            if (!string.Equals(stamp.Path, full, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if ((stamp.LastWriteUtc - lastWriteUtc).Duration() > TimeTolerance)
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
