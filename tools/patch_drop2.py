import io

# ---------- 1) 布局树深度上限 3 -> 4 ----------
p = 'src/Wantodo.Core/LayoutNode.cs'
s = io.open(p, encoding='utf-8-sig').read()
old = 'public const int MaxDepth = 3;'
assert old in s, 'maxdepth'
s = s.replace(old, 'public const int MaxDepth = 4;')
io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)

# ---------- 2) Drop 复用 DragOver 的判定结果 ----------
p = 'src/Wantodo.App/MainWindow.xaml.cs'
s = io.open(p, encoding='utf-8-sig').read()

# 字段：记住「最后一次预览」的目标与方向
anchor = "    private bool _lastDropSwap = true;"
assert anchor in s
s = s.replace(anchor, anchor + "\n    private string? _lastDropTargetId;")

# DragOver 里把方向缓存下来
old_preview = """            if (side != _lastDropSide || swap != _lastDropSwap)
            {
                _lastDropSide = side;
                _lastDropSwap = swap;
                AppLog.Info($"落点判定 {side}{(swap ? "（互换）" : string.Empty)}：x={point.X:F0} y={point.Y:F0} 容器={width:F0}×{height:F0}");
            }
"""
new_preview = """            _lastDropTargetId = target.Id;
            if (side != _lastDropSide || swap != _lastDropSwap)
            {
                _lastDropSide = side;
                _lastDropSwap = swap;
                AppLog.Info($"落点判定 {side}{(swap ? "（互换）" : string.Empty)}：x={point.X:F0} y={point.Y:F0} 容器={width:F0}×{height:F0}");
            }
"""
assert old_preview in s, 'preview anchor'
s = s.replace(old_preview, new_preview)

# OnPaneDrop：删掉旧的 28%/80px 独立判定，改用缓存（目标不一致才重算同一个函数）
start = s.index('        var point = e.GetPosition(element);\n        var width = Math.Max(1, element.ActualWidth);')
end = s.index('        RebuildLayout();\n        AppLog.Info($"停靠：栏 {sourceId}')
new_drop = '''        // 松手时不要再自己算一套：DragOver 已经算好了方向并画了预览，
        // 两处阈值不一致就会出现「预览是右侧、落地却互换」。目标相同就直接用缓存。
        DockSide side;
        bool swap;
        var fromCache = string.Equals(_lastDropTargetId, target.Id, StringComparison.Ordinal);
        if (fromCache)
        {
            side = _lastDropSide;
            swap = _lastDropSwap;
        }
        else
        {
            side = ComputeDropSide(e.GetPosition(element), Math.Max(1, element.ActualWidth), Math.Max(1, element.ActualHeight), out swap);
        }

        var ok = swap
            ? _viewModel.SwapPanes(sourceId, target.Id)
            : _viewModel.MovePane(sourceId, target.Id, side);

        if (!ok && !swap)
        {
            // 层级太深时插不进去：先换成互换，至少让这次拖动有可见结果，并记下来
            ok = _viewModel.SwapPanes(sourceId, target.Id);
            if (ok)
            {
                swap = true;
                AppLog.Info("停靠位置太深，改为与目标栏互换");
            }
        }

        AppLog.Info($"Drop 目标={target.Id} 预览方向={(swap ? "互换" : side.ToString())} 来源={(fromCache ? "DragOver 缓存" : "Drop 重算")}");

        if (!ok)
        {
            AppLog.Warn("停靠没成功：布局树拒绝了这次移动（可能是层级超过上限）");
            return;
        }

'''
s = s[:start] + new_drop + s[end:]

# OnWindowDrop：同样优先用缓存
old_win = """        e.Handled = true;
        var side = ComputeDropSide(e.GetPosition(pane), Math.Max(1, pane.ActualWidth), Math.Max(1, pane.ActualHeight), out var swap);"""
new_win = """        e.Handled = true;
        var fromCache = string.Equals(_lastDropTargetId, target.Id, StringComparison.Ordinal);
        var side = fromCache
            ? _lastDropSide
            : ComputeDropSide(e.GetPosition(pane), Math.Max(1, pane.ActualWidth), Math.Max(1, pane.ActualHeight), out _);
        var swap = fromCache && _lastDropSwap;"""
assert old_win in s, 'window drop anchor'
s = s.replace(old_win, new_win)

old_win_log = """        AppLog.Info($"停靠（窗口级）：{sourceId} → {(becameSwap ? "与 " + target.Id + " 互换" : target.Id + " 的 " + side)}");"""
new_win_log = """        AppLog.Info($"Drop（窗口级）目标={target.Id} 预览方向={(becameSwap ? "互换" : side.ToString())} 来源={(fromCache ? "DragOver 缓存" : "Drop 重算")}");"""
assert old_win_log in s, 'window log anchor'
s = s.replace(old_win_log, new_win_log)

io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)
print('ok')
