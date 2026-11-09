using System;
using System.Collections.ObjectModel;
using Wantodo.Core;

namespace Wantodo.App.ViewModels;

/// <summary>一栏的视图模型：标题、条目集合、栏尾输入框。</summary>
public sealed class ColumnViewModel : ObservableObject
{
    private readonly BoardViewModel _board;

    private string _draftText = string.Empty;
    private bool _isEditingTitle;
    private string _titleDraft = string.Empty;
    private bool _isDropTarget;
    private int _noteCount;

    public ColumnViewModel(BoardViewModel board, NoteColumn model)
    {
        _board = board;
        Model = model;

        Notes = new ObservableCollection<NoteViewModel>(
            model.Notes.Select(n => new NoteViewModel(n, OnNoteChanged, OnNoteDeleteRequested)));
        _noteCount = Notes.Count;

        Notes.CollectionChanged += (_, _) => RefreshCount();
    }

    public NoteColumn Model { get; }

    public ObservableCollection<NoteViewModel> Notes { get; }

    public string Title => Model.Title;

    public int NoteCount
    {
        get => _noteCount;
        private set => SetProperty(ref _noteCount, value);
    }

    /// <summary>栏尾输入框的内容。回车提交后清空并保持焦点。</summary>
    public string DraftText
    {
        get => _draftText;
        set => SetProperty(ref _draftText, value);
    }

    public bool IsEditingTitle
    {
        get => _isEditingTitle;
        private set => SetProperty(ref _isEditingTitle, value);
    }

    public string TitleDraft
    {
        get => _titleDraft;
        set => SetProperty(ref _titleDraft, value);
    }

    /// <summary>拖拽经过本栏时高亮整栏，给一个「会放到这里」的落点反馈。</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }

    public bool AddDraft()
    {
        if (!BoardEditor.TryAddNote(Model, DraftText, out var note) || note is null)
        {
            DraftText = string.Empty;
            return false;
        }

        Notes.Add(new NoteViewModel(note, OnNoteChanged, OnNoteDeleteRequested));
        DraftText = string.Empty;
        _board.SaveNow();
        return true;
    }

    public void BeginTitleEdit()
    {
        TitleDraft = Model.Title;
        IsEditingTitle = true;
    }

    public void CommitTitleEdit()
    {
        if (!IsEditingTitle)
        {
            return;
        }

        IsEditingTitle = false;

        if (string.Equals(Model.Title, TitleDraft.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        BoardEditor.RenameColumn(Model, TitleDraft);
        OnPropertyChanged(nameof(Title));
        _board.SaveNow();
    }

    public void CancelTitleEdit()
    {
        IsEditingTitle = false;
        TitleDraft = Model.Title;
    }

    internal void RefreshCount() => NoteCount = Notes.Count;

    private void OnNoteChanged(NoteViewModel note) => _board.SaveNow();

    private void OnNoteDeleteRequested(NoteViewModel note) => _board.RemoveNote(note);
}
