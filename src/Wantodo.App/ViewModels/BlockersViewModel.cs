using System.Collections.ObjectModel;
using Wantodo.Core;

namespace Wantodo.App.ViewModels;

/// <summary>「想做 ⇄ 卡在什么地方」的一行；两个格子都是自由文本。</summary>
public sealed class BlockerRowViewModel : ObservableObject
{
    private string _want;
    private string _blocker;

    public BlockerRowViewModel(BlockerRow row)
    {
        Model = row;
        _want = row.Want;
        _blocker = row.Blocker;
    }

    public BlockerRow Model { get; }

    public string Want
    {
        get => _want;
        set
        {
            if (SetProperty(ref _want, value))
            {
                Model.Want = value;
            }
        }
    }

    public string Blocker
    {
        get => _blocker;
        set
        {
            if (SetProperty(ref _blocker, value))
            {
                Model.Blocker = value;
            }
        }
    }

    public static BlockerRowViewModel Create(string want, string blocker = "")
    {
        var row = new BlockerRow { Want = want, Blocker = blocker };
        return new BlockerRowViewModel(row);
    }
}

/// <summary>卡住清单窗口的数据：一组行 + 存盘。</summary>
public sealed class BlockersViewModel : ObservableObject
{
    private readonly BlockersStore _store;
    private BlockersDocument _document;

    public BlockersViewModel(BlockersStore store)
    {
        _store = store;
        _document = store.Load();

        Rows = new ObservableCollection<BlockerRowViewModel>(_document.Rows.Select(r => new BlockerRowViewModel(r)));
    }

    public ObservableCollection<BlockerRowViewModel> Rows { get; }

    public string FilePath => _store.FilePath;

    /// <summary>按「想做」的文字查原因；主界面悬停提示用这个。</summary>
    public string? ReasonFor(string? want) => _document.Find(want ?? string.Empty)?.Blocker;

    public BlockerRowViewModel AddRow(string want, string blocker = "")
    {
        var vm = BlockerRowViewModel.Create(want, blocker);
        Rows.Add(vm);
        _document.Set(vm.Want, vm.Blocker);
        return vm;
    }

    public void RemoveRow(BlockerRowViewModel row)
    {
        Rows.Remove(row);
        _document.Remove(row.Want);
    }

    /// <summary>把界面上的行写回文档并保存（空「想做」的行直接丢弃）。</summary>
    public void Save()
    {
        _document = new BlockersDocument();
        foreach (var row in Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Want))
            {
                continue;
            }

            _document.Set(row.Want, row.Blocker);
        }

        _store.Save(_document);
    }
}
