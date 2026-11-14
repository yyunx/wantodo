using System;
using System.IO;
using Wantodo.Core;

namespace Wantodo.App.Notes;

/// <summary>列表里的一行。只放展示需要的字段，真实内容始终以磁盘文件为准。</summary>
public sealed class NoteListItem
{
    public NoteListItem(NoteDocument document)
    {
        Document = document;
    }

    public NoteDocument Document { get; }

    /// <summary>磁盘上的真实文件名（含时间前缀与扩展名），用来和资源管理器对账。</summary>
    public string FileName => Path.GetFileName(Document.FullPath);

    /// <summary>相对 notes 根的路径，如 <c>inbox\20261114-1402-买牛奶.md</c>。</summary>
    public string RelativePath => Document.Id.Replace('/', Path.DirectorySeparatorChar);

    public string Title => string.IsNullOrWhiteSpace(Document.Title) ? "未命名" : Document.Title;

    public string When
    {
        get
        {
            var local = Document.UpdatedAt.ToLocalTime();
            return local.Date == DateTimeOffset.Now.Date
                ? local.ToString("HH:mm")
                : local.ToString("MM-dd HH:mm");
        }
    }

    public string FormatBadge => Document.Format == NoteFormat.Md ? "MD" : "TXT";

    public string Snippet
    {
        get
        {
            var body = Document.Body.Replace("\r", " ").Replace("\n", " ").Trim();
            if (body.Length == 0)
            {
                return "（空白）";
            }

            return body.Length <= 40 ? body : body[..40] + "…";
        }
    }
}
