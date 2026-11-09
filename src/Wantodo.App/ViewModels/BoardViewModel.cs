using System;
using System.Collections.ObjectModel;
using System.Linq;
using Wantodo.Core;

namespace Wantodo.App.ViewModels;

/// <summary>整块板的视图模型：持有 Board 模型和 JsonBoardStore，所有写入都在这里收口。</summary>
public sealed class BoardViewModel : ObservableObject
{
    private readonly JsonBoardStore _store;

    public BoardViewModel(Board model, JsonBoardStore store)
    {
        Model = model;
        _store = store;

        Columns = new ObservableCollection<ColumnViewModel>(
            model.Columns.Select(column => new ColumnViewModel(this, column)));

        AddColumnCommand = new RelayCommand(AddColumn, () => CanAddColumn);
    }

    public Board Model { get; }

    public ObservableCollection<ColumnViewModel> Columns { get; }

    public RelayCommand AddColumnCommand { get; }

    public bool CanAddColumn => Columns.Count < BoardEditor.MaxColumns;

    /// <summary>立即落盘。Phase 2 每条操作都存一次，节流留给 Phase 3。</summary>
    public void SaveNow()
    {
        try
        {
            _store.Save(Model);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存失败", ex);
        }
    }

    public void AddColumn()
    {
        if (!BoardEditor.TryAddColumn(Model, "新栏", out var column) || column is null)
        {
            AppLog.Warn($"加栏被拒绝：已经有 {Columns.Count} 栏（上限 {BoardEditor.MaxColumns}）");
            return;
        }

        Columns.Add(new ColumnViewModel(this, column));
        AddColumnCommand.RaiseCanExecuteChanged();
        SaveNow();
        AppLog.Info($"加了第 {Columns.Count} 栏 id={column.Id}");
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
        SaveNow();
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
        SaveNow();
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

        SaveNow();
    }

    /// <summary>关窗前把窗口位置写回模型并落盘。</summary>
    public void SavePlacement(double left, double top, double width, double height)
    {
        Model.Placement.Left = left;
        Model.Placement.Top = top;
        Model.Placement.Width = width;
        Model.Placement.Height = height;
        SaveNow();
    }
}
