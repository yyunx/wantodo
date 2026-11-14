using System;
using System.Collections.ObjectModel;
using System.Linq;
using Wantodo.Core;

namespace Wantodo.App.ViewModels;

/// <summary>整块板的视图模型：持有 Board 模型和 JsonBoardStore，所有写入都在这里收口。</summary>
public sealed class BoardViewModel : ObservableObject
{
    private readonly JsonBoardStore _store;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer;
    private bool _dirty;

    public BoardViewModel(Board model, JsonBoardStore store)
    {
        Model = model;
        _store = store;

        Columns = new ObservableCollection<ColumnViewModel>(
            model.Columns.Select(column => new ColumnViewModel(this, column)));

        AddColumnCommand = new RelayCommand(AddColumn, () => CanAddColumn);

        _saveTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _saveTimer.Tick += (_, _) => FlushSave();
        _saveTimer.Stop();
    }

    public Board Model { get; }

    public ObservableCollection<ColumnViewModel> Columns { get; }

    public RelayCommand AddColumnCommand { get; }

    /// <summary>设置页的「顶边下拉面板」开关变化时通知桌面层服务。</summary>
    public event EventHandler? TopPanelVisibilityChanged;

    /// <summary>顶边下拉面板开关（默认关）。</summary>
    public bool ShowTopPanel
    {
        get => Model.ShowTopPanel;
        set
        {
            if (Model.ShowTopPanel == value)
            {
                return;
            }

            Model.ShowTopPanel = value;
            OnPropertyChanged();
            RequestSave();
            TopPanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>面板展开后记住高度（夹取 240–720）。</summary>
    public void SetPanelHeight(double height)
    {
        double clamped = Math.Clamp(height, Board.MinPanelHeight, Board.MaxPanelHeight);
        if (Math.Abs(Model.PanelHeight - clamped) < 0.5)
        {
            return;
        }

        Model.PanelHeight = clamped;
        RequestSave();
    }

    /// <summary>隐藏已完成的条目（数据保留，只是不显示）。</summary>
    public bool HideCompleted
    {
        get => Model.HideCompleted;
        set
        {
            if (Model.HideCompleted == value)
            {
                return;
            }

            Model.HideCompleted = value;
            foreach (var column in Columns)
            {
                column.RefreshHidden();
            }

            OnPropertyChanged();
            RequestSave();
        }
    }

    /// <summary>主题 / 色调变了：换掉全局画刷并落盘。</summary>
    public void ApplyAppearance()
    {
        Appearance.ThemeService.Apply(Model);
        RequestSave();
    }

    public void SetTheme(string theme)
    {
        Model.Theme = theme;
        ApplyAppearance();
    }

    public void SetTone(double tone)
    {
        Model.Tone = Math.Clamp(tone, 0, 1);
        ApplyAppearance();
    }

    /// <summary>
    /// 自定义主题：只存背景 + 强调两色，其余 8 个令牌由 <see cref="ThemeDerivation"/> 推导。
    /// 颜色非法时仍切到 <c>Custom</c>，空/坏值让 <see cref="ThemePalette.ForCustom"/> 回退深色，并记一条 WARN。
    /// </summary>
    public void SetCustomTheme(string? background, string? accent)
    {
        var normalizedBackground = ThemePalette.NormalizeHex(background);
        var normalizedAccent = ThemePalette.NormalizeHex(accent);

        if (normalizedBackground is null || normalizedAccent is null)
        {
            AppLog.Warn(
                $"自定义主题颜色非法：background={background ?? "null"} accent={accent ?? "null"}；" +
                "缺失或无法解析的一项会被忽略，主题退回深色底");
        }

        Model.Theme = ThemePalette.CustomTheme;
        Model.CustomBackground = normalizedBackground;
        Model.CustomAccent = normalizedAccent;
        ApplyAppearance();
    }

    /// <summary>随心记新建的默认类型：只认 <c>md</c>，其余按 <c>txt</c>。</summary>
    public void SetDefaultNoteFormat(string format)
    {
        Model.DefaultNoteFormat = string.Equals(format, "md", StringComparison.OrdinalIgnoreCase) ? "md" : "txt";
        OnPropertyChanged(nameof(DefaultNoteFormat));
        RequestSave();
    }

    public string DefaultNoteFormat => Model.DefaultNoteFormat;

    public bool CanAddColumn => Columns.Count < BoardEditor.MaxColumns;

    /// <summary>
    /// 最近操作过的栏。新栏插在它右边；没有的话（刚启动、还没点过任何栏）退化为追加到末尾。
    /// </summary>
    public ColumnViewModel? ActiveColumn { get; private set; }

    public void SetActiveColumn(ColumnViewModel? column)
    {
        if (!ReferenceEquals(ActiveColumn, column))
        {
            ActiveColumn = column;
        }
    }

    /// <summary>
    /// 把一栏停靠到另一栏的某一侧；布局树先算、校验通过才落数据，
    /// 否则保持原样并把原因抛回去（界面负责提示，不静默丢操作）。
    /// </summary>
    public bool MovePane(string paneId, string targetPaneId, DockSide side)
    {
        if (Model.Layout is null || string.Equals(paneId, targetPaneId, StringComparison.Ordinal))
        {
            return false;
        }

        var moved = LayoutTree.MovePane(Model.Layout, paneId, targetPaneId, side);
        if (LayoutTree.Validate(moved, Model.Columns.Select(c => c.Id).ToList()) is { } error)
        {
            AppLog.Warn($"停靠被拒绝：{error}");
            return false;
        }

        Model.Layout = moved;
        RequestSave();
        return true;
    }

    /// <summary>把两栏的位置互换（拖到中间时用）。</summary>
    public bool SwapPanes(string paneId, string otherPaneId)
    {
        if (Model.Layout is null || string.Equals(paneId, otherPaneId, StringComparison.Ordinal))
        {
            return false;
        }

        var swapped = LayoutTree.SwapPanes(Model.Layout, paneId, otherPaneId);
        if (LayoutTree.Validate(swapped, Model.Columns.Select(c => c.Id).ToList()) is { } error)
        {
            AppLog.Warn($"互换被拒绝：{error}");
            return false;
        }

        Model.Layout = swapped;
        RequestSave();
        return true;
    }

    /// <summary>
    /// 标记「有改动」，400ms 内合并成一次落盘（连续拖动、连敲几条不会每次都写盘）。
    /// 关窗、系统注销时会走 <see cref="FlushSave"/> 立刻写。
    /// 最坏情况：改完 400ms 内被强杀，丢最后一次改动——验收口径允许。
    /// </summary>
    public void RequestSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>立刻落盘，不管节流。关窗、注销、退出前必须调一次。</summary>
    public void FlushSave()
    {
        _saveTimer.Stop();

        if (!_dirty)
        {
            return;
        }

        _dirty = false;

        try
        {
            _store.Save(Model);
        }
        catch (Exception ex)
        {
            _dirty = true;
            AppLog.Error("保存失败", ex);
        }
    }

    public void AddColumn()
    {
        AddColumn(BoardEditor.DefaultColumnTitle);
    }

    /// <summary>按指定名字加一栏；空白名字回落到默认名（对话框里直接回车就是这条路）。</summary>
    public void AddColumn(string? title)
    {
        int index = ActiveColumn is null ? Columns.Count : Columns.IndexOf(ActiveColumn) + 1;
        string name = string.IsNullOrWhiteSpace(title) ? BoardEditor.DefaultColumnTitle : title.Trim();

        if (!BoardEditor.TryAddColumn(Model, name, index, out var column) || column is null)
        {
            AppLog.Warn($"加栏被拒绝：已经有 {Columns.Count} 栏（上限 {BoardEditor.MaxColumns}）");
            return;
        }

        var viewModel = new ColumnViewModel(this, column);
        Columns.Insert(Math.Clamp(index, 0, Columns.Count), viewModel);
        SetActiveColumn(viewModel);
        AddColumnCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanAddColumn));
        RequestSave();
        AppLog.Info($"加了第 {Columns.Count} 栏 id={column.Id}（插在下标 {index}）");
    }

    public bool RemoveColumn(ColumnViewModel column)
    {
        if (!BoardEditor.TryRemoveColumn(Model, column.Model.Id))
        {
            AppLog.Warn($"删栏被拒绝：{column.Title}（最后一栏不能删）");
            return false;
        }

        Columns.Remove(column);
        AddColumnCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanAddColumn));
        RequestSave();
        AppLog.Info($"删了栏 {column.Title}，剩 {Columns.Count} 栏");
        return true;
    }

    public void RemoveNote(NoteViewModel note)
    {
        var column = Columns.FirstOrDefault(c => c.Notes.Contains(note));
        if (column is null)
        {
            return;
        }

        BoardEditor.RemoveNote(Model, note.Model.Id);
        column.Notes.Remove(note);
        RequestSave();
    }

    /// <summary>
    /// 拖拽落位。<paramref name="index"/> 是移走之后在目标栏里的最终下标
    /// （语义与 <see cref="BoardEditor.MoveNote"/> 一致）。
    /// </summary>
    public void MoveNote(NoteViewModel note, ColumnViewModel target, int index)
    {
        var source = Columns.FirstOrDefault(c => c.Notes.Contains(note));
        if (source is null)
        {
            return;
        }

        if (!BoardEditor.MoveNote(Model, note.Model.Id, target.Model.Id, index))
        {
            return;
        }

        if (!ReferenceEquals(source, target))
        {
            source.Notes.Remove(note);
            target.Notes.Insert(Math.Clamp(index, 0, target.Notes.Count), note);
        }
        else
        {
            int from = source.Notes.IndexOf(note);
            int to = Math.Clamp(index, 0, source.Notes.Count - 1);
            if (from >= 0 && from != to)
            {
                source.Notes.Move(from, to);
            }
        }

        RequestSave();
    }

    /// <summary>关窗前把窗口位置写回模型并落盘。</summary>
    public void SavePlacement(double left, double top, double width, double height)
    {
        Model.Placement.Left = left;
        Model.Placement.Top = top;
        Model.Placement.Width = width;
        Model.Placement.Height = height;
        RequestSave();
    }
}
