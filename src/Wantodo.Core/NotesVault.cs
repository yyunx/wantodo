using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Wantodo.Core;

/// <summary>笔记的落盘格式。对应用户能看到的「新建文档类型」。</summary>
public enum NoteFormat
{
    Txt,
    Md,
}

/// <summary>
/// 一篇笔记。文件是唯一事实来源：这里的字段都是从磁盘解析出来的快照，
/// 修改必须走 <see cref="NotesVault.Write"/> 回写。
/// </summary>
public sealed record NoteDocument(
    string Id,
    string Title,
    string Body,
    NoteFormat Format,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Tags,
    string FullPath);

/// <summary>新建笔记的结果，失败时 <see cref="Document"/> 为 null。</summary>
public sealed record NoteCreateResult(NoteDocument? Document, string? Error)
{
    public bool Success => Document is not null;
}

/// <summary>
/// 笔记库：真实文件夹里的真实文件（默认 <c>&lt;root&gt;\notes</c>），
/// 外部编辑器可以随便改，这里不做数据库索引。
/// 命名 <c>yyyyMMdd-HHmm-&lt;slug&gt;.&lt;ext&gt;</c>，重名追加 <c>-2</c>。
/// </summary>
public sealed partial class NotesVault
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;
    private readonly string _trash;

    public NotesVault(string notesRoot, string trashRoot)
    {
        _root = Path.GetFullPath(notesRoot);
        _trash = Path.GetFullPath(trashRoot);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_trash);
    }

    public string Root => _root;

    public string TrashRoot => _trash;

    /// <summary>扫描所有笔记（递归）。按「最后修改」倒序，最新的在最前。</summary>
    public IReadOnlyList<NoteDocument> Scan()
    {
        if (!Directory.Exists(_root))
        {
            return Array.Empty<NoteDocument>();
        }

        var files = Directory
            .EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Where(IsNoteFile)
            .Select(TryRead)
            .Where(doc => doc is not null)
            .Select(doc => doc!)
            .OrderByDescending(doc => doc.UpdatedAt)
            .ToList();

        return files;
    }

    /// <summary>新建一篇空笔记。</summary>
    public NoteCreateResult Create(NoteFormat format, string? title = null)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var safeTitle = string.IsNullOrWhiteSpace(title) ? "未命名" : title.Trim();
            var inbox = Path.Combine(_root, "inbox");
            Directory.CreateDirectory(inbox);

            var path = AllocatePath(inbox, now, safeTitle, format);
            var doc = new NoteDocument(
                Id: ToId(path),
                Title: safeTitle,
                Body: string.Empty,
                Format: format,
                CreatedAt: now,
                UpdatedAt: now,
                Tags: Array.Empty<string>(),
                FullPath: path);

            WriteFile(doc);
            return new NoteCreateResult(doc, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new NoteCreateResult(null, ex.Message);
        }
    }

    /// <summary>把标题/正文写回磁盘；<c>updated</c> 跟着变。</summary>
    public NoteDocument? Write(NoteDocument document, string title, string body, IReadOnlyList<string>? tags = null)
    {
        var updated = document with
        {
            Title = title ?? string.Empty,
            Body = body ?? string.Empty,
            Tags = tags ?? document.Tags,
            UpdatedAt = DateTimeOffset.Now,
        };

        WriteFile(updated);
        return updated;
    }

    /// <summary>移到 <c>trash\</c>（带时间前缀，保留原文件名），失败返回 false。</summary>
    public bool Trash(NoteDocument document)
    {
        try
        {
            if (!File.Exists(document.FullPath))
            {
                return false;
            }

            Directory.CreateDirectory(_trash);
            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = Path.Combine(_trash, $"{stamp}-{Path.GetFileName(document.FullPath)}");
            var unique = target;
            var n = 2;
            while (File.Exists(unique))
            {
                unique = Path.Combine(_trash, $"{stamp}-{n++}-{Path.GetFileName(document.FullPath)}");
            }

            File.Move(document.FullPath, unique);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 把文件改成以新标题命名（保留原目录与时间戳）。仅用于「新建时还是占位标题」的场景：
    /// 已经存在过的文件不自动改名，避免外部链接/笔记引用失效。
    /// </summary>
    public NoteDocument? Rename(NoteDocument document, string newTitle)
    {
        try
        {
            if (!File.Exists(document.FullPath))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(document.FullPath) ?? _root;
            var stamp = Path.GetFileNameWithoutExtension(document.FullPath);
            // 原文件名形如 20261110-1314-未命名，取前 14 个字符（yyyyMMdd-HHmm-）作时间前缀。
            if (stamp.Length > 14 && stamp[8] == '-' && stamp[13] == '-')
            {
                stamp = stamp[..14];
            }
            else
            {
                stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            }

            var extension = document.Format == NoteFormat.Md ? ".md" : ".txt";
            var target = Path.Combine(directory, $"{stamp}{Slug(newTitle)}{extension}");
            var n = 2;
            while (File.Exists(target) && !string.Equals(target, document.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                target = Path.Combine(directory, $"{stamp}{Slug(newTitle)}-{n++}{extension}");
            }

            if (!string.Equals(target, document.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(document.FullPath, target);
            }

            var renamed = document with
            {
                Id = ToId(target),
                Title = newTitle,
                FullPath = target,
            };
            return renamed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>按 id 读一篇；不存在返回 null。</summary>
    public NoteDocument? Read(string id)
    {
        var path = Path.Combine(_root, id);
        return File.Exists(path) ? TryRead(path) : null;
    }

    /// <summary>文件名里的 id（相对 notes 根，用 <c>/</c> 分隔，方便当 key）。</summary>
    public string ToId(string fullPath)
    {
        var relative = Path.GetRelativePath(_root, fullPath);
        return relative.Replace('\\', '/');
    }

    private NoteDocument? TryRead(string path)
    {
        try
        {
            var format = FormatOf(path);
            var text = File.ReadAllText(path, Utf8NoBom);
            var info = new FileInfo(path);
            return format == NoteFormat.Md
                ? ParseMarkdown(path, text, info)
                : ParsePlainText(path, text, info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private NoteDocument ParsePlainText(string path, string text, FileInfo info)
    {
        var normalized = text.Replace("\r\n", "\n");
        var newline = normalized.IndexOf('\n');
        var title = newline < 0 ? normalized : normalized[..newline];
        var body = newline < 0 ? string.Empty : normalized[(newline + 1)..];
        return Build(path, title.Trim(), body.TrimEnd('\n'), NoteFormat.Txt, info, Array.Empty<string>(), null);
    }

    private NoteDocument ParseMarkdown(string path, string text, FileInfo info)
    {
        var normalized = text.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return ParsePlainText(path, text, info);
        }

        var end = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return ParsePlainText(path, text, info);
        }

        var front = normalized[4..end];
        var bodyStart = normalized.IndexOf('\n', end + 1);
        var body = bodyStart < 0 ? string.Empty : normalized[(bodyStart + 1)..];

        string? title = null;
        var tags = new List<string>();
        DateTimeOffset? created = null;
        foreach (var rawLine in front.Split('\n'))
        {
            var line = rawLine.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "title":
                    title = Unquote(value);
                    break;
                case "tags":
                    tags.AddRange(ParseTags(value));
                    break;
                case "created" when DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                    created = parsed;
                    break;
            }
        }

        var fallbackTitle = Path.GetFileNameWithoutExtension(path);
        return Build(
            path,
            string.IsNullOrWhiteSpace(title) ? fallbackTitle : title.Trim(),
            body.Trim('\n'),
            NoteFormat.Md,
            info,
            tags,
            created);
    }

    private NoteDocument Build(
        string path,
        string title,
        string body,
        NoteFormat format,
        FileInfo info,
        IReadOnlyList<string> tags,
        DateTimeOffset? created)
    {
        var createdTime = info.CreationTimeUtc;
        var updated = info.LastWriteTimeUtc;
        // 某些文件系统上 CreationTime 可能晚于 LastWriteTime（复制/解压），取较早者兜底。
        var createdOffset = new DateTimeOffset(createdTime.Kind == DateTimeKind.Utc ? createdTime : createdTime.ToUniversalTime());
        var updatedOffset = new DateTimeOffset(updated.Kind == DateTimeKind.Utc ? updated : updated.ToUniversalTime());
        if (createdOffset > updatedOffset)
        {
            createdOffset = updatedOffset;
        }

        return new NoteDocument(
            Id: ToId(path),
            Title: title,
            Body: body,
            Format: format,
            CreatedAt: created ?? createdOffset,
            UpdatedAt: updatedOffset,
            Tags: tags,
            FullPath: path);
    }

    private void WriteFile(NoteDocument document)
    {
        var directory = Path.GetDirectoryName(document.FullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = document.Format == NoteFormat.Md
            ? RenderMarkdown(document)
            : RenderPlainText(document);

        var temp = document.FullPath + ".tmp";
        File.WriteAllText(temp, content, Utf8NoBom);
        File.Move(temp, document.FullPath, overwrite: true);
    }

    private static string RenderPlainText(NoteDocument document)
    {
        var title = document.Title.Trim();
        var body = document.Body.Replace("\r\n", "\n").TrimEnd('\n');
        return body.Length == 0 ? title + "\n" : title + "\n" + body + "\n";
    }

    private static string RenderMarkdown(NoteDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("---\n");
        builder.Append("title: ").Append(NeedsQuote(document.Title) ? Quote(document.Title) : document.Title).Append('\n');
        builder.Append("created: ").Append(document.CreatedAt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("updated: ").Append(document.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)).Append('\n');
        if (document.Tags.Count > 0)
        {
            builder.Append("tags: [").Append(string.Join(", ", document.Tags)).Append("]\n");
        }

        builder.Append("---\n");
        var body = document.Body.Replace("\r\n", "\n").TrimEnd('\n');
        if (body.Length > 0)
        {
            builder.Append('\n').Append(body).Append('\n');
        }

        return builder.ToString();
    }

    private string AllocatePath(string directory, DateTimeOffset now, string title, NoteFormat format)
    {
        var stem = $"{now:yyyyMMdd-HHmm}-{Slug(title)}";
        var extension = format == NoteFormat.Md ? ".md" : ".txt";
        var candidate = Path.Combine(directory, stem + extension);
        var n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{stem}-{n++}{extension}");
        }

        return candidate;
    }

    /// <summary>把标题折成文件名片段：保留中英数字，空白与标点变 <c>-</c>。</summary>
    public static string Slug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "note";
        }

        var builder = new StringBuilder();
        foreach (var ch in title.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (ch is ' ' or '_' or '-' or '，' or '。' or ',' or '.')
            {
                builder.Append('-');
            }
        }

        var slug = CollapseDashes().Replace(builder.ToString(), "-").Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }

        return slug.Length == 0 ? "note" : slug;
    }

    private static bool IsNoteFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.'))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static NoteFormat FormatOf(string path)
        => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ? NoteFormat.Md : NoteFormat.Txt;

    private static IEnumerable<string> ParseTags(string value)
    {
        var inner = value.Trim();
        if (inner.StartsWith('[') && inner.EndsWith(']'))
        {
            inner = inner[1..^1];
        }

        return inner
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Unquote)
            .Where(tag => tag.Length > 0);
    }

    private static bool NeedsQuote(string value)
        => value.Contains(':') || value.Contains('#') || value.Contains('"') || value.StartsWith(' ') || value.EndsWith(' ');

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\\\"", "\"");
        }

        return trimmed;
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex CollapseDashes();
}
