import io

p = 'src/Wantodo.App/MainWindow.xaml.cs'
s = io.open(p, encoding='utf-8-sig').read()
orig = s

old_dragover = """    private void OnNotesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(NoteDataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
"""
new_dragover = """    private void OnNotesDragOver(object sender, DragEventArgs e)
    {
        // 拖的是「栏」而不是条目：这里必须放行（Handled=false），让它冒泡到栏卡片。
        // 之前这里一律 Effects=None + Handled=true，结果整个列表区域都在拒绝拖栏，
        // 只有栏内边距那一条窄边能命中 —— 表现就是「只有最左边一条能停靠」。
        if (e.Data.GetDataPresent(PaneDataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = false;
            return;
        }

        if (!e.Data.GetDataPresent(NoteDataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
"""
assert old_dragover in s, 'dragover anchor'
s = s.replace(old_dragover, new_dragover)

start = s.index("    /// <summary>落点区域")
end = s.index("    private void ShowDropPreview")
new_calc = '''    /// <summary>
    /// 落点判定：把栏切成 30% / 70% 两条分界线。
    /// 左 30% = 停到左边、右 30% = 右边、上 30% = 上边、下 30% = 下边；
    /// 四个角同时命中两条带时取「离得更近的那条边」；正中间（30%~70% × 30%~70%）= 与这一栏互换。
    /// 坐标系：point 是相对「这一栏卡片」的坐标，width/height 也是这张卡片的实际尺寸。
    /// </summary>
    private static DockSide ComputeDropSide(Point point, double width, double height, out bool swap)
    {
        var nx = Math.Clamp(point.X / width, 0, 1);
        var ny = Math.Clamp(point.Y / height, 0, 1);

        var nearLeft = nx < 0.3;
        var nearRight = nx > 0.7;
        var nearTop = ny < 0.3;
        var nearBottom = ny > 0.7;

        if (!nearLeft && !nearRight && !nearTop && !nearBottom)
        {
            swap = true;
            return DockSide.Left;
        }

        swap = false;

        // 角落会同时命中两条带，取距离更近的那条边
        var dLeft = nx;
        var dRight = 1 - nx;
        var dTop = ny;
        var dBottom = 1 - ny;

        var best = double.MaxValue;
        var side = DockSide.Left;

        if (nearLeft && dLeft < best) { best = dLeft; side = DockSide.Left; }
        if (nearRight && dRight < best) { best = dRight; side = DockSide.Right; }
        if (nearTop && dTop < best) { best = dTop; side = DockSide.Top; }
        if (nearBottom && dBottom < best) { side = DockSide.Bottom; }

        return side;
    }

'''
s = s[:start] + new_calc + s[end:]

old_log = """            var point = e.GetPosition(element);
            var width = Math.Max(1, element.ActualWidth);
            var height = Math.Max(1, element.ActualHeight);
            var side = ComputeDropSide(point, width, height, out var swap);
            ShowDropPreview(element, side, swap);"""
new_log = """            var point = e.GetPosition(element);
            var width = Math.Max(1, element.ActualWidth);
            var height = Math.Max(1, element.ActualHeight);
            var side = ComputeDropSide(point, width, height, out var swap);
            if (side != _lastDropSide || swap != _lastDropSwap)
            {
                _lastDropSide = side;
                _lastDropSwap = swap;
                AppLog.Info($"落点判定 {side}{(swap ? "（互换）" : string.Empty)}：x={point.X:F0} y={point.Y:F0} 容器={width:F0}×{height:F0}");
            }

            ShowDropPreview(element, side, swap);"""
assert old_log in s, 'log anchor'
s = s.replace(old_log, new_log)

anchor = "    private bool _dragHoverLogged;"
assert anchor in s, 'field anchor'
s = s.replace(anchor, anchor + "\n    private DockSide _lastDropSide;\n    private bool _lastDropSwap = true;")

io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)
print('patched', len(orig), '->', len(s))
