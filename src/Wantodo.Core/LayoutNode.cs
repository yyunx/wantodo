using System.Text.Json.Serialization;

namespace Wantodo.Core;

/// <summary>停靠方向。Left/Right 生成左右切分，Top/Bottom 生成上下切分。</summary>
public enum DockSide
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>
/// 停靠布局的一棵树。整棵树是可序列化的纯数据（不留 WPF 引用），
/// 所以 Core 能直接把它当值来算、来测，界面只负责把它画成 Grid。
///
/// 用「一个类 + Type 判别字段」而不是继承体系：<c>System.Text.Json</c> 处理多态要写转换器，
/// 而这棵树将来可能被外部脚本读写，扁平结构更容易手改也更容易排错。
/// </summary>
public sealed class LayoutNode
{
    public const string LeafType = "leaf";
    public const string SplitType = "split";

    public const double MinRatio = 0.15;
    public const double MaxRatio = 0.85;

    /// <summary>"leaf" 或 "split"。</summary>
    public string Type { get; set; } = LeafType;

    /// <summary>叶节点指向的栏 id。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaneId { get; set; }

    /// <summary>分割方向："Horizontal"（左右）或 "Vertical"（上下）。</summary>
    public string Orientation { get; set; } = "Horizontal";

    /// <summary>第一路的占比，0.15–0.85。</summary>
    public double Ratio { get; set; } = 0.5;

    public LayoutNode? First { get; set; }

    public LayoutNode? Second { get; set; }

    [JsonIgnore]
    public bool IsLeaf => string.Equals(Type, LeafType, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsHorizontal => string.Equals(Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);

    public static LayoutNode Leaf(string paneId) => new() { Type = LeafType, PaneId = paneId };

    public static LayoutNode Split(string orientation, double ratio, LayoutNode first, LayoutNode second) => new()
    {
        Type = SplitType,
        PaneId = null,
        Orientation = orientation,
        Ratio = Math.Clamp(ratio, MinRatio, MaxRatio),
        First = first,
        Second = second,
    };

    public LayoutNode Clone() => new()
    {
        Type = Type,
        PaneId = PaneId,
        Orientation = Orientation,
        Ratio = Ratio,
        First = First?.Clone(),
        Second = Second?.Clone(),
    };
}

/// <summary>布局树的纯函数操作 + 校验 + 尺寸推算。全部无副作用，方便单测。</summary>
public static class LayoutTree
{
    /// <summary>相邻两路之间的分隔条宽度（DIP），与 XAML 里 GridSplitter 的 Width/Height 保持一致。</summary>
    public const double SplitterSize = 6;

    /// <summary>单个叶子的默认偏好尺寸。</summary>
    public const double PreferredPaneWidth = 280;

    public const double PreferredPaneHeight = 320;

    public const double MinPaneWidth = 190;

    public const double MinPaneHeight = 120;

    /// <summary>最大嵌套深度（根为 1）。4 栏的常见形态是 2–3 层。</summary>
    public const int MaxDepth = 3;

    /// <summary>把若干栏排成均衡的横向树：1 栏就是叶，2 栏 0.5/0.5，3 栏 0.667 → 0.5。</summary>
    public static LayoutNode? Balanced(IReadOnlyList<string> paneIds)
    {
        if (paneIds.Count == 0)
        {
            return null;
        }

        return Build(paneIds, 0, paneIds.Count);

        // 二分建树：4 栏得到 ((p0|p1)|(p2|p3))，深度正好 3 层，触不到 MaxDepth。
        // 若用「一个个往右挂」的写法，4 栏会变成 4 层嵌套，Validate 会直接判非法。
        static LayoutNode? Build(IReadOnlyList<string> ids, int from, int count)
        {
            if (count <= 0)
            {
                return null;
            }

            if (count == 1)
            {
                return LayoutNode.Leaf(ids[from]);
            }

            var half = count / 2;
            var first = Build(ids, from, half);
            var second = Build(ids, from + half, count - half);
            return LayoutNode.Split("Horizontal", (double)half / count, first!, second!);
        }
    }

    /// <summary>按深度优先顺序列出所有叶子指向的栏 id。</summary>
    public static IReadOnlyList<string> LeafIds(LayoutNode? root)
    {
        var ids = new List<string>();
        Walk(root, ids);
        return ids;

        static void Walk(LayoutNode? node, List<string> sink)
        {
            if (node is null)
            {
                return;
            }

            if (node.IsLeaf)
            {
                if (!string.IsNullOrEmpty(node.PaneId))
                {
                    sink.Add(node.PaneId);
                }

                return;
            }

            Walk(node.First, sink);
            Walk(node.Second, sink);
        }
    }

    /// <summary>在 <paramref name="targetPaneId"/> 的某一侧插入一个新栏。</summary>
    public static LayoutNode InsertBeside(LayoutNode root, string targetPaneId, string newPaneId, DockSide side)
    {
        ArgumentNullException.ThrowIfNull(root);

        var found = false;
        var result = Insert(root, targetPaneId, LayoutNode.Leaf(newPaneId), side, ref found);
        if (!found || result is null)
        {
            throw new InvalidOperationException($"布局里找不到栏 {targetPaneId}");
        }

        return result;

        static LayoutNode? Insert(LayoutNode? node, string target, LayoutNode inserted, DockSide side, ref bool found)
        {
            if (node is null)
            {
                return null;
            }

            if (node.IsLeaf)
            {
                if (!string.Equals(node.PaneId, target, StringComparison.Ordinal))
                {
                    return node;
                }

                found = true;
                var orientation = side is DockSide.Left or DockSide.Right ? "Horizontal" : "Vertical";
                var first = side is DockSide.Left or DockSide.Top ? inserted : node;
                var second = side is DockSide.Left or DockSide.Top ? node : inserted;
                return LayoutNode.Split(orientation, 0.5, first, second);
            }

            node.First = Insert(node.First, target, inserted, side, ref found);
            node.Second = Insert(node.Second, target, inserted, side, ref found);
            return node;
        }
    }

    /// <summary>把 <paramref name="paneId"/> 移到 <paramref name="targetPaneId"/> 的某一侧。</summary>
    public static LayoutNode MovePane(LayoutNode root, string paneId, string targetPaneId, DockSide side)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.Equals(paneId, targetPaneId, StringComparison.Ordinal))
        {
            return root;
        }

        if (!LeafIds(root).Contains(paneId, StringComparer.Ordinal))
        {
            return root;
        }

        var without = RemovePane(root, paneId) ?? throw new InvalidOperationException("移动后布局不该为空");
        return InsertBeside(without, targetPaneId, paneId, side);
    }

    /// <summary>删除一个栏对应的叶子；父 Split 用兄弟节点顶替自己（树坍缩）。</summary>
    public static LayoutNode? RemovePane(LayoutNode? root, string paneId)
    {
        if (root is null)
        {
            return null;
        }

        if (root.IsLeaf)
        {
            return string.Equals(root.PaneId, paneId, StringComparison.Ordinal) ? null : root;
        }

        root.First = RemovePane(root.First, paneId);
        root.Second = RemovePane(root.Second, paneId);

        if (root.First is null)
        {
            return root.Second;
        }

        if (root.Second is null)
        {
            return root.First;
        }

        root.Ratio = Math.Clamp(root.Ratio, LayoutNode.MinRatio, LayoutNode.MaxRatio);
        return root;
    }

    /// <summary>中心停靠：把两个栏的位置互换，树形不变。</summary>
    public static LayoutNode SwapPanes(LayoutNode root, string paneId, string otherPaneId)
    {
        ArgumentNullException.ThrowIfNull(root);

        Swap(root);
        return root;

        void Swap(LayoutNode? node)
        {
            if (node is null)
            {
                return;
            }

            if (node.IsLeaf)
            {
                if (string.Equals(node.PaneId, paneId, StringComparison.Ordinal))
                {
                    node.PaneId = otherPaneId;
                }
                else if (string.Equals(node.PaneId, otherPaneId, StringComparison.Ordinal))
                {
                    node.PaneId = paneId;
                }

                return;
            }

            Swap(node.First);
            Swap(node.Second);
        }
    }

    /// <summary>按路径找 Split。<paramref name="path"/> 由 '0'（第一路）/ '1'（第二路）组成，空串表示根。</summary>
    public static LayoutNode? FindSplit(LayoutNode? root, string path)
    {
        var node = root;
        foreach (var step in path)
        {
            if (node is null || node.IsLeaf)
            {
                return null;
            }

            node = step == '1' ? node.Second : node.First;
        }

        return node is null || node.IsLeaf ? null : node;
    }

    /// <summary>把某条路径上的 Split 比例改掉（夹到 0.15–0.85），返回是否命中。</summary>
    public static bool SetRatio(LayoutNode? root, string path, double ratio)
    {
        var split = FindSplit(root, path);
        if (split is null)
        {
            return false;
        }

        if (!double.IsFinite(ratio))
        {
            return false;
        }

        split.Ratio = Math.Clamp(ratio, LayoutNode.MinRatio, LayoutNode.MaxRatio);
        return true;
    }

    /// <summary>校验整棵树是否自洽；返回 null 表示合法。</summary>
    public static string? Validate(LayoutNode? root, IReadOnlyList<string> paneIds)
    {
        if (root is null)
        {
            return paneIds.Count == 0 ? null : "布局为空但存在栏";
        }

        if (paneIds.Count == 0)
        {
            return "没有栏但布局不为空";
        }

        var known = new HashSet<string>(paneIds, StringComparer.Ordinal);
        if (known.Count != paneIds.Count)
        {
            return "栏 id 有重复";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var error = Walk(root, 1, seen);
        if (error is not null)
        {
            return error;
        }

        if (seen.Count != known.Count)
        {
            return "布局里的栏与栏列表不一致";
        }

        foreach (var id in known)
        {
            if (!seen.Contains(id))
            {
                return $"布局缺少栏 {id}";
            }
        }

        return null;

        static string? Walk(LayoutNode? node, int depth, HashSet<string> seen)
        {
            if (node is null)
            {
                return "布局里有空节点";
            }

            if (depth > MaxDepth)
            {
                return $"布局嵌套超过 {MaxDepth} 层";
            }

            if (node.IsLeaf)
            {
                if (string.IsNullOrEmpty(node.PaneId))
                {
                    return "叶节点没有 paneId";
                }

                return seen.Add(node.PaneId) ? null : $"栏 {node.PaneId} 在布局里出现多次";
            }

            if (!double.IsFinite(node.Ratio) || node.Ratio < LayoutNode.MinRatio || node.Ratio > LayoutNode.MaxRatio)
            {
                return "ratio 越界";
            }

            if (!string.Equals(node.Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(node.Orientation, "Vertical", StringComparison.OrdinalIgnoreCase))
            {
                return "orientation 非法";
            }

            return Walk(node.First, depth + 1, seen) ?? Walk(node.Second, depth + 1, seen);
        }
    }

    /// <summary>按布局推算「每栏都是偏好尺寸」时窗口需要多大。</summary>
    public static (double Width, double Height) ComputePreferredSize(
        LayoutNode? root,
        Func<string, (double Width, double Height)>? paneSize = null,
        double splitter = SplitterSize)
    {
        if (root is null)
        {
            return (PreferredPaneWidth, PreferredPaneHeight);
        }

        paneSize ??= _ => (PreferredPaneWidth, PreferredPaneHeight);
        return Measure(root, paneSize, splitter);

        static (double Width, double Height) Measure(
            LayoutNode node,
            Func<string, (double Width, double Height)> size,
            double gap)
        {
            if (node.IsLeaf)
            {
                return node.PaneId is { Length: > 0 } id ? size(id) : (MinPaneWidth, MinPaneHeight);
            }

            var first = Measure(node.First!, size, gap);
            var second = Measure(node.Second!, size, gap);

            return node.IsHorizontal
                ? (first.Width + second.Width + gap, Math.Max(first.Height, second.Height))
                : (Math.Max(first.Width, second.Width), first.Height + second.Height + gap);
        }
    }

    /// <summary>按布局推算窗口的最小尺寸（每栏 190×120，且能被工作区夹住之前的下限）。</summary>
    public static (double Width, double Height) ComputeMinSize(LayoutNode? root, double splitter = SplitterSize)
        => root is null
            ? (MinPaneWidth, MinPaneHeight)
            : ComputePreferredSize(root, _ => (MinPaneWidth, MinPaneHeight), splitter);
}
