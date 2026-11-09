namespace Wantodo.Core;

/// <summary>一栏。栏数由 Board.Columns 决定，不要在 UI 里写死两栏。</summary>
public sealed class NoteColumn
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string Title { get; set; } = "未命名栏";

    public List<Note> Notes { get; set; } = new();
}
