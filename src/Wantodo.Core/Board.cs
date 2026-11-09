namespace Wantodo.Core;

/// <summary>整块便签板的数据。持久化对象就是它，字段变化要考虑旧文件兼容。</summary>
public sealed class Board
{
    public int Version { get; set; } = 1;

    /// <summary>窗口层级：Desktop（默认，贴桌面图标层）/ Normal / Topmost。</summary>
    public string Surface { get; set; } = "Desktop";

    /// <summary>外观：Dark / Light。</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>色调强度 0.0–1.0。注意：这是背景色阶，不是窗口透明度（见 decisions.md D3）。</summary>
    public double Tone { get; set; }

    /// <summary>是否隐藏已完成的条目（数据仍保留，只是不显示）。</summary>
    public bool HideCompleted { get; set; }

    public WindowPlacement Placement { get; set; } = new();

    public List<NoteColumn> Columns { get; set; } = new();

    /// <summary>首次启动（或数据彻底损坏）时的初始内容：两栏，带几条占位条目。</summary>
    public static Board CreateDefault()
    {
        var board = new Board();

        board.Columns.Add(new NoteColumn
        {
            Title = "要做的事情",
            Notes =
            {
                new Note { Text = "把这块便签拖到顺手的位置" },
                new Note { Text = "拖窗口边缘调整大小" },
            },
        });

        board.Columns.Add(new NoteColumn
        {
            Title = "突然想到的问题",
            Notes =
            {
                new Note { Text = "栏数以后要能自己加，先记一笔" },
            },
        });

        return board;
    }
}
