using System;
using System.Collections.Generic;

namespace Wantodo.Core;

/// <summary>
/// 从卡片文本里挑出链接。只认 http/https 与 www. 开头，避免把「1.2.3」这类文字误判成域名。
/// 记事/待办的正文仍然只是一段文字，链接是在渲染时另外列出来的按钮。
/// </summary>
public static class TextLinks
{
    private static readonly char[] Trailing =
    {
        '.', ',', ';', ':', '!', '?', ')', ']', '}', '。', '，', '；', '：', '！', '？', '）', '】',
    };

    public static IReadOnlyList<string> Extract(string? text)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return found;
        }

        int i = 0;
        while (i < text.Length)
        {
            if (!TryFindScheme(text, i, out int start, out int prefixLength))
            {
                break;
            }

            int end = start + prefixLength;
            while (end < text.Length
                   && !char.IsWhiteSpace(text[end])
                   && text[end] != '"' && text[end] != '\'' && text[end] != '<' && text[end] != '>')
            {
                end++;
            }

            string candidate = text[start..end].TrimEnd(Trailing);
            if (candidate.Length > prefixLength)
            {
                string url = candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                    ? "https://" + candidate
                    : candidate;
                if (!found.Exists(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(url);
                }
            }

            i = Math.Max(end, start + prefixLength);
        }

        return found;
    }

    private static bool TryFindScheme(string text, int from, out int start, out int prefixLength)
    {
        start = -1;
        prefixLength = 0;

        foreach ((string scheme, int length) in new[] { ("https://", 8), ("http://", 7), ("www.", 4) })
        {
            int index = text.IndexOf(scheme, from, StringComparison.OrdinalIgnoreCase);
            while (index >= 0 && index > 0 && char.IsLetterOrDigit(text[index - 1]))
            {
                index = text.IndexOf(scheme, index + 1, StringComparison.OrdinalIgnoreCase);
            }

            if (index >= 0 && (start < 0 || index < start))
            {
                start = index;
                prefixLength = length;
            }
        }

        return start >= 0;
    }
}
