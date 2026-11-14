namespace Wantodo.Core;

/// <summary>整块便签板的数据。持久化对象就是它，字段变化要考虑旧文件兼容。</summary>
public sealed class Board
{
    public int Version { get; set; } = 2;

    /// <summary>窗口层级：Desktop（默认，贴桌面图标层）/ Normal / Topmost。</summary>
    public string Surface { get; set; } = "Desktop";

    /// <summary>外观：Dark / Light / System / Dracula / Nord / Catppuccin / Custom。</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>自定义主题的背景色（<c>#RRGGBB</c>，仅 <c>Theme=Custom</c> 使用；缺失按深色回退）。</summary>
    public string? CustomBackground { get; set; }

    /// <summary>自定义主题的强调色（<c>#RRGGBB</c>，仅 <c>Theme=Custom</c> 使用；其余令牌由它推导）。</summary>
    public string? CustomAccent { get; set; }

    /// <summary>色调强度 0.0–1.0。注意：这是背景色阶，不是窗口透明度（见 decisions.md D3）。</summary>
    public double Tone { get; set; }

    /// <summary>是否隐藏已完成的条目（数据仍保留，只是不显示）。</summary>
    public bool HideCompleted { get; set; }

    /// <summary>随心记新建文档的默认类型：<c>txt</c> 或 <c>md</c>（见 NotesVault）。</summary>
    public string DefaultNoteFormat { get; set; } = "txt";

    /// <summary>顶边下拉面板是否启用（Phase 17；默认关，关闭时不建窗口、不装钩子）。</summary>
    public bool ShowTopPanel { get; set; }

    /// <summary>下拉面板展开后的高度（DIP），夹取 240–720。</summary>
    public double PanelHeight { get; set; } = 320;

    public const double MinPanelHeight = 240;

    public const double MaxPanelHeight = 720;

    public WindowPlacement Placement { get; set; } = new();

    public List<NoteColumn> Columns { get; set; } = new();

    /// <summary>
    /// 停靠布局（Phase 9）。为 <c>null</c> 时按 <see cref="Columns"/> 的顺序生成均衡横向树，
    /// 旧数据（只有 columns[]）因此无需立刻改写就能打开。
    /// </summary>
    public LayoutNode? Layout { get; set; }

    /// <summary>确保 <see cref="Layout"/> 与 <see cref="Columns"/> 一致：缺失就重建，非法就回退到均衡布局。</summary>
    public string? NormalizeLayout()
    {
        var ids = Columns.Where(c => !string.IsNullOrEmpty(c.Id)).Select(c => c.Id).ToList();
        if (ids.Count != Columns.Count)
        {
            return "栏 id 有空值";
        }

        var error = Layout is null ? null : LayoutTree.Validate(Layout, ids);
        if (error is null)
        {
            Layout ??= LayoutTree.Balanced(ids);
            return null;
        }

        Layout = LayoutTree.Balanced(ids);
        return error;
    }

    /// <summary>面板高度来自手改文件或旧版本时回到合法区间。</summary>
    public void NormalizePanel()
    {
        if (double.IsNaN(PanelHeight) || double.IsInfinity(PanelHeight))
        {
            PanelHeight = MaxPanelHeight;
            return;
        }

        PanelHeight = Math.Clamp(PanelHeight, MinPanelHeight, MaxPanelHeight);
    }

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

        board.Layout = LayoutTree.Balanced(board.Columns.Select(c => c.Id).ToList());
        return board;
    }
}
