using System;
using System.Collections.Generic;

namespace Wantodo.Core;

/// <summary>
/// 按表面分开的撤销栈（0.7.0 P1.2，计划书 §4.6）：两个表面各一条，深度默认 50，
/// 只在内存里，不持久化。压栈的是「回到这个状态」的快照字符串，撤销/重做由调用方
/// 决定快照内容（见 <see cref="CanvasSnapshot"/>）。
/// </summary>
/// <remarks>
/// 之所以不共用一条栈：结构化表面按一次 Ctrl+Z 不能动到自由表面的内容（验收 V-13）。
/// 之所以快照位用「当前状态传入、恢复状态取出」：快照格式属于调用方，栈本身只做索引。
/// </remarks>
public sealed class SurfaceUndo
{
    /// <summary>每个表面的默认深度（计划书 §4.6）。</summary>
    public const int DefaultLimit = 50;

    private readonly Dictionary<string, List<string>> _undo = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _redo = new(StringComparer.Ordinal);
    private readonly int _limit;

    public SurfaceUndo(int limit = DefaultLimit)
    {
        _limit = Math.Max(1, limit);
    }

    /// <summary>本实例的深度上限。</summary>
    public int Limit => _limit;

    /// <summary>压入一份「撤销后应该回到的状态」，并清空该表面的重做栈。</summary>
    public void Push(string surface, string snapshot)
    {
        var list = StackFor(_undo, surface);
        list.Add(snapshot);
        Trim(list);
        StackFor(_redo, surface).Clear();
    }

    /// <summary>能否撤销。</summary>
    public bool CanUndo(string surface) => Depth(surface) > 0;

    /// <summary>能否重做。</summary>
    public bool CanRedo(string surface) => StackFor(_redo, surface).Count > 0;

    /// <summary>该表面撤销栈里还有几份快照。</summary>
    public int Depth(string surface) => StackFor(_undo, surface).Count;

    /// <summary>清空某个表面的两条栈（例如整份数据被替换时）。</summary>
    public void Clear(string surface)
    {
        StackFor(_undo, surface).Clear();
        StackFor(_redo, surface).Clear();
    }

    /// <summary>
    /// 取出一份撤销快照：把 <paramref name="current"/> 放进重做栈，返回要恢复的状态。
    /// 没有可撤销的内容时返回 false，且不改动任何栈。
    /// </summary>
    public bool TryUndo(string surface, string current, out string restored)
    {
        var undo = StackFor(_undo, surface);
        if (undo.Count == 0)
        {
            restored = "";
            return false;
        }

        restored = Take(undo);
        var redo = StackFor(_redo, surface);
        redo.Add(current);
        Trim(redo);
        return true;
    }

    /// <summary>取出一份重做快照，语义与 <see cref="TryUndo"/> 对称。</summary>
    public bool TryRedo(string surface, string current, out string restored)
    {
        var redo = StackFor(_redo, surface);
        if (redo.Count == 0)
        {
            restored = "";
            return false;
        }

        restored = Take(redo);
        var undo = StackFor(_undo, surface);
        undo.Add(current);
        Trim(undo);
        return true;
    }

    private static List<string> StackFor(Dictionary<string, List<string>> map, string surface)
    {
        var key = string.IsNullOrWhiteSpace(surface) ? CanvasSurfaces.Structured : surface;
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<string>();
            map[key] = list;
        }

        return list;
    }

    private static string Take(List<string> list)
    {
        var value = list[^1];
        list.RemoveAt(list.Count - 1);
        return value;
    }

    /// <summary>只留最近的 <see cref="_limit"/> 份，旧的先丢（最久远的状态先被忘掉）。</summary>
    private void Trim(List<string> list)
    {
        if (list.Count > _limit)
        {
            list.RemoveRange(0, list.Count - _limit);
        }
    }
}
