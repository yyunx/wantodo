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
        int index = ActiveColumn is null ? Columns.Count : Columns.IndexOf(ActiveColumn) + 1;

        if (!BoardEditor.TryAddColumn(Model, BoardEditor.DefaultColumnTitle, index, out var column) || column is null)
        {
            AppLog.Warn($"加栏被拒绝：已经有 {Columns.Count} 栏（上限 {BoardEditor.MaxColumns}）");
            return;
        }

        var viewModel = new ColumnViewModel(this, column);
        Columns.Insert(Math.Clamp(index, 0, Columns.Count), viewModel);
        SetActiveColumn(viewModel);
        AddColumnCommand.RaiseCanExecuteChanged();
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
