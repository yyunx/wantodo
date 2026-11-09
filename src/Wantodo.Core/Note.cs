namespace Wantodo.Core;

/// <summary>一条待办 / 一个突然想到的问题。完成状态只影响显示，不删除文本。</summary>
public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string Text { get; set; } = string.Empty;

    public bool Completed { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
