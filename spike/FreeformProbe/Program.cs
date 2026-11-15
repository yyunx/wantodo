using System.IO;
using System.Windows;
using System.Windows.Ink;
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
        SeedResources();

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

        // ⑧ P1.2：图形创建 / 文本创建 / 笔迹落盘 / 每表面撤销隔离
        P12Checks();

        Console.WriteLine(_failed == 0 ? "PROBE PASS" : $"PROBE FAIL ({_failed})");
        return _failed == 0 ? 0 : 1;
    }

    private static void P12Checks()
    {
        var inkDir = Path.Combine(Path.GetTempPath(), "wantodo-070", "probe-ink");
        if (Directory.Exists(inkDir))
        {
            Directory.Delete(inkDir, true);
        }

        var document = new CanvasDocument();
        var board = new FreeformBoard { Width = 800, Height = 600 };
        var saves = 0;
        board.Attach(document, () => saves++, null, inkDir);
        board.Measure(new Size(800, 600));
        board.Arrange(new Rect(0, 0, 800, 600));

        // 矩形：拖 60×50
        CreateShape(board, FreeformTool.Rect, new Point(100, 100), new Point(160, 150));
        var rect = ShapeWithKind(document, "rect");
        Check("矩形创建落模型", rect is not null, Describe(document));
        Check("矩形坐标来自拖动", rect is not null
            && Math.Abs(double.Parse(rect.Get("x1")!, System.Globalization.CultureInfo.InvariantCulture) - 100) < 0.01
            && Math.Abs(double.Parse(rect.Get("y2")!, System.Globalization.CultureInfo.InvariantCulture) - 150) < 0.01,
            rect is null ? "no rect" : $"x1={rect.Get("x1")} y2={rect.Get("y2")}");

        board.Undo();
        Check("撤销矩形后图元归零", document.Objects.Count == 0, $"objects={document.Objects.Count}");
        board.Redo();
        Check("重做矩形后图元回来", document.Objects.Count == 1, $"objects={document.Objects.Count}");

        // 线 / 箭头：形状字段必须区分
        CreateShape(board, FreeformTool.Line, new Point(300, 200), new Point(360, 240));
        CreateShape(board, FreeformTool.Arrow, new Point(400, 260), new Point(470, 320));
        Check("线创建落模型", ShapeWithKind(document, "line") is not null, Describe(document));
        Check("箭头创建落模型", ShapeWithKind(document, "arrow") is not null, Describe(document));

        // 文本：点一下建对象，但空文本不应留下
        board.Tool = FreeformTool.Text;
        board.PointerDown(new Point(600, 400), ModifierKeys.None);
        board.PointerUp();
        var text = document.Objects.FirstOrDefault(o => o.Kind == CanvasObject.KindText);
        Check("文本工具建出文本图元", text is not null, Describe(document));
        board.Tool = FreeformTool.Select; // 切工具 = 提交编辑，空文本应被删掉
        Check("空文本切工具后不留对象", document.Objects.All(o => o.Kind != CanvasObject.KindText), Describe(document));

        // 笔迹：走 OnStrokeCollected 的同一条路径，ISF 必须落盘
        var stroke = new Stroke(new StylusPointCollection
        {
            new StylusPoint(120, 320),
            new StylusPoint(180, 360),
            new StylusPoint(220, 380),
        })
        {
            DrawingAttributes = new DrawingAttributes { Color = System.Windows.Media.Colors.SteelBlue, Width = 3, Height = 3 },
        };
        board.CommitStroke(stroke);
        var ink = document.Objects.FirstOrDefault(o => o.Kind == CanvasObject.KindStroke);
        Check("笔迹落模型", ink is not null, Describe(document));
        Check("笔迹 ISF 落盘", ink is not null && File.Exists(Path.Combine(inkDir, ink.Get("path")!)),
            ink is null ? "no stroke" : $"path={ink.Get("path")}");
        Check("笔迹进 InkCanvas 层", board.InkStrokeCount == 1, $"strokes={board.InkStrokeCount}");
        board.Undo();
        Check("撤销笔迹后模型干净", document.Objects.All(o => o.Kind != CanvasObject.KindStroke), Describe(document));

        // V-13：每表面一条撤销栈，来回撤不串
        var shared = new CanvasDocument();
        var freeform = new FreeformBoard { Width = 800, Height = 600 };
        freeform.Attach(shared, null, null, inkDir);
        freeform.Measure(new Size(800, 600));
        freeform.Arrange(new Rect(0, 0, 800, 600));

        var structured = new CanvasBoard { Width = 800, Height = 600 };
        structured.Attach(shared, null, null);
        structured.Measure(new Size(800, 600));
        structured.Arrange(new Rect(0, 0, 800, 600));

        CreateShape(freeform, FreeformTool.Rect, new Point(10, 10), new Point(40, 40));
        CreateShape(freeform, FreeformTool.Line, new Point(50, 10), new Point(90, 40));
        CreateShape(freeform, FreeformTool.Arrow, new Point(10, 60), new Point(60, 90));
        structured.Add("note", 200, 200);
        structured.Add("note", 400, 200);
        Check("两面各建三步/两张", shared.Objects.Count == 3 && shared.Cards.Count == 2,
            $"objects={shared.Objects.Count} cards={shared.Cards.Count}");
        Check("两面栈深各自记账", freeform.UndoDepth == 3 && structured.UndoDepth == 2,
            $"freeform={freeform.UndoDepth} structured={structured.UndoDepth}");

        freeform.Undo();
        freeform.Undo();
        freeform.Undo();
        Check("自由面撤 3 次回到空", shared.Objects.Count == 0, $"objects={shared.Objects.Count}");
        Check("结构面不受自由面撤销影响", shared.Cards.Count == 2 && structured.UndoDepth == 2,
            $"cards={shared.Cards.Count} depth={structured.UndoDepth}");
        Check("结构面此刻仍可撤销", structured.CanUndo, $"canUndo={structured.CanUndo}");

        structured.Undo();
        Check("结构面撤 1 次剩 1 张", shared.Cards.Count == 1, $"cards={shared.Cards.Count}");
        Check("结构面撤销不清自由面重做栈", freeform.CanRedo, $"canRedo={freeform.CanRedo}");

        Console.WriteLine($"P1.2 saves={saves}");
    }

    /// <summary>
    /// 探针不启动真正的 App（那会带壳、抢单实例 Mutex），所以这里补一份最小的主题资源，
    /// 只为让 <see cref="CanvasBoard"/> 的 <c>SetResourceReference</c> 有东西可取。断言的都不是颜色。
    /// </summary>
    private static void SeedResources()
    {
        var resources = Application.Current.Resources;
        foreach (var key in new[] { "SurfaceCard", "TextPrimary", "TextSecondary", "TextMuted", "Accent", "BorderActive", "CardBackground", "Divider" })
        {
            resources[key] = System.Windows.Media.Brushes.DimGray;
        }

        resources["ThemeCardBorderBrush"] = System.Windows.Media.Brushes.Gray;
        resources["ThemeCardBorderThickness"] = new Thickness(1);
        resources["ThemeCardCornerRadius"] = new CornerRadius(8);
    }

    private static void CreateShape(FreeformBoard board, FreeformTool tool, Point from, Point to)
    {
        board.Tool = tool;
        board.PointerDown(from, ModifierKeys.None);
        board.PointerMove(to);
        board.PointerUp();
    }

    private static CanvasObject? ShapeWithKind(CanvasDocument document, string kind) =>
        document.Objects.FirstOrDefault(o => o.Kind == CanvasObject.KindShape && o.Get("shape") == kind);

    private static string Describe(CanvasDocument document) =>
        $"objects=[{string.Join(",", document.Objects.Select(o => o.Kind + ":" + (o.Get("shape") ?? "-")))}]";

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
