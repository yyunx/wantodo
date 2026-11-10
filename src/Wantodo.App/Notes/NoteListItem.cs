using System;
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
