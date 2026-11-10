using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Wantodo.Core;

/// <summary>
/// 「想做 ⇄ 卡在什么地方」两列对照表。
///
/// 存储用 Markdown 表格（`board\blockers.md`），理由：用户本来就用 Obsidian，
/// 这份文件可以直接被 Obsidian 打开、引用、写双链；出问题也能用记事本手改。
/// 表格只认两列（想做 / 卡在什么地方），多出来的列在读取时忽略，写回时丢弃。
/// </summary>
public sealed class BlockersDocument
{
    public const string FileName = "blockers.md";

    private static readonly Regex SeparatorRow = new(@"^\|[\s:\-|]+\|$", RegexOptions.Compiled);

    public List<BlockerRow> Rows { get; } = new();

    /// <summary>把「想做」的文字归一成查找键：去空白、忽略大小写。</summary>
    public static string Key(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant();

    public BlockerRow? Find(string want)
    {
        string key = Key(want);
        if (key.Length == 0)
        {
            return null;
        }

        return Rows.FirstOrDefault(r => Key(r.Want) == key);
    }

    /// <summary>记下「这一条卡在什么地方」；已存在就更新原因。空原因等于删掉这一行。</summary>
    public BlockerRow? Set(string want, string? blocker)
    {
        string text = (want ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        string reason = (blocker ?? string.Empty).Trim();
        var existing = Find(text);

        if (reason.Length == 0)
        {
            if (existing is not null)
            {
                Rows.Remove(existing);
            }

            return null;
        }

        if (existing is not null)
        {
            existing.Blocker = reason;
            existing.UpdatedAt = DateTimeOffset.Now;
            return existing;
        }

        var row = new BlockerRow
        {
            Want = text,
            Blocker = reason,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };
        Rows.Add(row);
        return row;
    }

    public bool Remove(string want)
    {
        var existing = Find(want);
        return existing is not null && Rows.Remove(existing);
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("# 想做 ⇄ 卡在什么地方\n\n");
        sb.Append("| 想做 | 卡在什么地方 |\n");
        sb.Append("| --- | --- |\n");

        foreach (var row in Rows)
        {
            sb.Append("| ").Append(Escape(row.Want)).Append(" | ").Append(Escape(row.Blocker)).Append(" |\n");
        }

        return sb.ToString();
    }

    /// <summary>解析 Markdown；找不到表头就当空表（文件被改坏也不抛）。</summary>
    public static BlockersDocument Parse(string? markdown)
    {
        var doc = new BlockersDocument();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return doc;
        }

        bool started = false;

        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || !line.StartsWith('|'))
            {
                continue;
            }

            if (!started)
            {
                if (line.Contains("想做", StringComparison.Ordinal))
                {
                    started = true;
                }

                continue;
            }

            if (SeparatorRow.IsMatch(line))
            {
                continue;
            }

            var cells = SplitCells(line);
            if (cells.Count < 2)
            {
                continue;
            }

            string want = Unescape(cells[0]);
            string blocker = Unescape(cells[1]);
            if (want.Length == 0)
            {
                continue;
            }

            doc.Rows.Add(new BlockerRow
            {
                Want = want,
                Blocker = blocker,
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
            });
        }

        return doc;
    }

    private static List<string> SplitCells(string line)
    {
        string body = line.Trim();
        if (body.StartsWith('|'))
        {
            body = body[1..];
        }

        if (body.EndsWith('|'))
        {
            body = body[..^1];
        }

        var cells = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\\' && i + 1 < body.Length && body[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }

            if (c == '|')
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        cells.Add(current.ToString());
        return cells;
    }

    private static string Escape(string? text) =>
        (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");

    private static string Unescape(string? text) => (text ?? string.Empty).Replace("\\|", "|").Trim();
}

/// <summary>一行对照：想做 X，卡在 Y。</summary>
public sealed class BlockerRow
{
    public string Want { get; set; } = string.Empty;

    public string Blocker { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>`board\blockers.md` 的读写；写盘沿用「临时文件 + 换名」。</summary>
public sealed class BlockersStore
{
    public BlockersStore(string rootDirectory)
    {
        DirectoryRoot = rootDirectory;
        FilePath = Path.Combine(rootDirectory, BlockersDocument.FileName);
    }

    public string DirectoryRoot { get; }

    public string FilePath { get; }

    public BlockersDocument Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new BlockersDocument();
            }

            return BlockersDocument.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch (IOException)
        {
            return new BlockersDocument();
        }
        catch (UnauthorizedAccessException)
        {
            return new BlockersDocument();
        }
    }

    public void Save(BlockersDocument document)
    {
        Directory.CreateDirectory(DirectoryRoot);

        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, document.ToMarkdown(), new UTF8Encoding(false));
        File.Move(temp, FilePath, overwrite: true);
    }
}
