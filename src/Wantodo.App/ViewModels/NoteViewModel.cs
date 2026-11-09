using System;
using Wantodo.Core;

namespace Wantodo.App.ViewModels;

/// <summary>一条便签的视图模型。数据改动只经 BoardEditor，改完通知外层去落盘。</summary>
public sealed class NoteViewModel : ObservableObject
{
    private readonly Note _model;
    private readonly Action<NoteViewModel> _onChanged;
    private readonly Action<NoteViewModel> _onDeleteRequested;

    private bool _isEditing;
    private string _editText = string.Empty;
    private bool _isDropBefore;

    public NoteViewModel(Note model, Action<NoteViewModel> onChanged, Action<NoteViewModel> onDeleteRequested)
    {
        _model = model;
        _onChanged = onChanged;
        _onDeleteRequested = onDeleteRequested;

        ToggleCommand = new RelayCommand(Toggle);
    }

    public Note Model => _model;

    public RelayCommand ToggleCommand { get; }

    public string Text => _model.Text;

    public bool IsCompleted => _model.Completed;

    /// <summary>拖拽时用来在目标行上方画一条落点线。</summary>
    public bool IsDropBefore
    {
        get => _isDropBefore;
        set => SetProperty(ref _isDropBefore, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    /// <summary>编辑框里的草稿。回车提交、Esc 取消；提交成空 = 删除这一条。</summary>
    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value);
    }

    public void Toggle()
    {
        BoardEditor.SetCompleted(_model, !_model.Completed);
        OnPropertyChanged(nameof(IsCompleted));
        _onChanged(this);
    }

    public void BeginEdit()
    {
        EditText = _model.Text;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;

        if (string.IsNullOrWhiteSpace(EditText))
        {
            _onDeleteRequested(this);
            return;
        }

        BoardEditor.UpdateText(_model, EditText);
        OnPropertyChanged(nameof(Text));
        _onChanged(this);
    }

    public void CancelEdit()
    {
        IsEditing = false;
        EditText = _model.Text;
    }

    public void RequestDelete() => _onDeleteRequested(this);
}
