using System.Windows;
using System.Windows.Input;
using Wantodo.App.Controls;
using Wantodo.Core;

namespace FreeformProbe;

/// <summary>
/// 自由表面手势探针（0.7.0 P1.1）：把 <see cref="FreeformBoard"/> 放到无窗口的 WPF 环境里，
/// 用 <c>PointerDown/Move/Up</c> 驱动，验证三件人眼不好反复验的事：
/// ① 缩放能真的到达 25% / 400%；② 平移不会顺手触发框选；③ 选择工具下拖空白只框选、不平移。
/// 退出码 0 = 全绿，1 = 有断言失败。
/// </summary>
internal static class Program
{
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var board = new FreeformBoard { Width = 800, Height = 600 };
        var document = new CanvasDocument();
        document.Objects.Add(MakeText("o1", 100, 100, 80, 40, "A"));
        document.Objects.Add(MakeText("o2", 400, 300, 80, 40, "B"));

        var saves = 0;
        board.Attach(document, () => saves++, null);
        board.Measure(new Size(800, 600));
        board.Arrange(new Rect(0, 0, 800, 600));

        // ① 缩放两端可达
        for (var i = 0; i < 20; i++)
        {
            board.ZoomBy(FreeformZoom.Step, new Point(400, 300));
        }

        Check("缩放放大到 400%", Math.Abs(board.Zoom - CanvasViewport.MaxZoom) < 0.0001, $"zoom={board.Zoom}");

        for (var i = 0; i < 30; i++)
        {
            board.ZoomBy(1 / FreeformZoom.Step, new Point(400, 300));
        }

        Check("缩放缩小到 25%", Math.Abs(board.Zoom - CanvasViewport.MinZoom) < 0.0001, $"zoom={board.Zoom}");

        board.ResetZoom();
        Check("重置缩放 = 100%", Math.Abs(board.Zoom - 1.0) < 0.0001, $"zoom={board.Zoom}");

        // 锚点不变：以 (100,100) 为中心放大 2 倍，(100,100) 处的画布内容仍在 (100,100)
        board.ResetZoom();
        board.ZoomBy(2.0, new Point(100, 100));
        var viewport = document.ViewportOf(CanvasSurfaces.Freeform);
        var screenX = (100 * viewport.Zoom) + viewport.OffsetX;
        var screenY = (100 * viewport.Zoom) + viewport.OffsetY;
        Check("锚点缩放不跑位", Math.Abs(screenX - 100) < 0.01 && Math.Abs(screenY - 100) < 0.01,
            $"screen=({screenX:0.##},{screenY:0.##}) zoom={viewport.Zoom:0.##} offset=({viewport.OffsetX:0.##},{viewport.OffsetY:0.##})");

        // ② 手形平移：只改 offset，不产生选择、不进入框选
        board.ResetZoom();
        board.Tool = FreeformTool.Pan;
        board.ClearSelection();
        var offsetBefore = viewport.OffsetX;
        board.PointerDown(new Point(10, 10), ModifierKeys.None);
        board.PointerMove(new Point(60, 80));
        board.PointerUp();
        Check("手形平移改视口", Math.Abs(viewport.OffsetX - (offsetBefore + 50)) < 0.01 && Math.Abs(viewport.OffsetY - 70) < 0.01,
            $"offset=({viewport.OffsetX:0.##},{viewport.OffsetY:0.##})");
        Check("手形平移不选中任何图元", board.Selected.Count == 0, $"selected={board.Selected.Count}");

        // ③ 空格 + 选择工具：应该平移，不是框选，也不移动图元
        board.ResetZoom();
        board.Tool = FreeformTool.Select;
        board.SpaceHeld = true;
        var x1 = document.Objects[0].X;
        var y1 = document.Objects[0].Y;
        board.PointerDown(new Point(120, 120), ModifierKeys.None); // 正好压在 o1 上
        board.PointerMove(new Point(170, 150));
        board.PointerUp();
        board.SpaceHeld = false;
        Check("空格+拖 平移视口", Math.Abs(viewport.OffsetX - 50) < 0.01 && Math.Abs(viewport.OffsetY - 30) < 0.01,
            $"offset=({viewport.OffsetX:0.##},{viewport.OffsetY:0.##})");
        Check("空格+拖 不移动图元", Math.Abs(document.Objects[0].X - x1) < 0.01 && Math.Abs(document.Objects[0].Y - y1) < 0.01,
            $"o1=({document.Objects[0].X:0.##},{document.Objects[0].Y:0.##})");

        // ④ 选择工具拖空白 = 框选，视口不动
        board.ResetZoom();
        board.ClearSelection();
        board.PointerDown(new Point(50, 50), ModifierKeys.None);
        board.PointerMove(new Point(300, 200));
        board.PointerUp();
        Check("拖空白框选命中 o1", board.Selected.Count == 1 && board.Selected.Contains("o1"),
            $"selected=[{string.Join(",", board.Selected)}]");
        Check("拖空白不平移", Math.Abs(viewport.OffsetX) < 0.01 && Math.Abs(viewport.OffsetY) < 0.01,
            $"offset=({viewport.OffsetX:0.##},{viewport.OffsetY:0.##})");

        // ⑤ 点选与拖动图元：位置变了，视口没变
        board.ResetZoom();
        board.PointerDown(new Point(120, 120), ModifierKeys.None);
        Check("点中 o1 即选中", board.Selected.Count == 1 && board.Selected.Contains("o1"), $"selected={board.Selected.Count}");
        board.PointerMove(new Point(160, 150));
        board.PointerUp();
        Check("拖动图元位移 40,30", Math.Abs(document.Objects[0].X - 140) < 0.01 && Math.Abs(document.Objects[0].Y - 130) < 0.01,
            $"o1=({document.Objects[0].X:0.##},{document.Objects[0].Y:0.##})");
        Check("拖动图元不动视口", Math.Abs(viewport.OffsetX) < 0.01, $"offsetX={viewport.OffsetX:0.##}");

        // ⑥ 橡皮：点中就删
        board.Tool = FreeformTool.Eraser;
        board.PointerDown(new Point(180, 170), ModifierKeys.None);
        board.PointerUp();
        Check("橡皮删除 o1", document.Objects.All(o => o.Id != "o1"), $"objects={document.Objects.Count}");

        // ⑦ 每次手势结束都落盘（回调次数 ≥ 手势次数的 1/2，至少不为 0）
        Check("手势后触发保存", saves > 0, $"saves={saves}");

        Console.WriteLine(_failed == 0 ? "PROBE PASS" : $"PROBE FAIL ({_failed})");
        return _failed == 0 ? 0 : 1;
    }

    private static CanvasObject MakeText(string id, double x, double y, double w, double h, string text)
    {
        var item = new CanvasObject
        {
            Id = id,
            Kind = CanvasObject.KindText,
            Surface = CanvasSurfaces.Freeform,
            X = x,
            Y = y,
            W = w,
            H = h,
        };
        item.Payload = new Dictionary<string, string> { ["text"] = text };
        return item;
    }

    private static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}  ({detail})");
        if (!ok)
        {
            _failed++;
        }
    }
}
