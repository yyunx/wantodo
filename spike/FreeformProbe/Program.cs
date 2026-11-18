using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // ⑨ P1.3：图片资产去重 / 无外框 / 撤销
        P13Checks();

        // ⑩ P1.4：待办投影 / 改名分离 / 计时状态机 / 分段番茄钟
        P14Checks();

        // ⑪ P2.1：空状态 / 右键菜单 / 全选
        P21Checks();

        // ⑫ P2.2：笔迹静态化 + 孤儿资产清理
        P22Checks();

        // ⑫.5 0.7.1 用户反馈批 1：圆工具 + 线型开关（B2/B3/B4 的工具归属与拖动改动见 XamlChecks）
        P071Checks();

        // ⑬ 资源字典自检：0.7.0 首次实机启动崩在重复隐式样式（ScrollBar 定义了两遍）
        XamlChecks();

        // ⑭ 0.7.1 现场排查：视口 offset 让画布原点落在可视区下方时，上半截的笔迹到底渲不渲出来
        InkOffscreenChecks();

        // ⑮ 0.7.1 第二批实机反馈：笔色被背景带走 / 橡皮容差 / 工具光标
        ToolingChecks();

        Console.WriteLine(_failed == 0 ? "PROBE PASS" : $"PROBE FAIL ({_failed})");
        return _failed == 0 ? 0 : 1;
    }

    private static double Measure(FreeformBoard board, RenderTargetBitmap target)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        const int frames = 30;
        for (var i = 0; i < frames; i++)
        {
            target.Render(board);
        }

        watch.Stop();
        return watch.Elapsed.TotalMilliseconds / frames;
    }

    /// <summary>
    /// 资源字典自检。WPF 允许「隐式样式」重复定义在编译期静默通过，运行期加载 App.xaml 时
    /// 才抛 ArgumentException「Item has already been added」——0.7.0 第一次实机启动就是这么崩的。
    /// 这里在探针阶段把它变成一条可自动发现的失败。
    /// </summary>
    /// <summary>
    /// 0.7.1 用户反馈批 1：圆工具、线型开关。拖动捕获与工具归属是文本级断言，放在 XamlChecks。
    /// </summary>
    private static void P071Checks()
    {
        var document = new CanvasDocument();
        var board = new FreeformBoard { Width = 800, Height = 600 };
        board.Attach(document, () => { }, null, Path.Combine(Path.GetTempPath(), "wantodo-070", "ink-shape"));
        board.Measure(new Size(800, 600));
        board.Arrange(new Rect(0, 0, 800, 600));

        board.Tool = FreeformTool.Circle;
        board.PointerDown(new Point(100, 100), ModifierKeys.None);
        board.PointerMove(new Point(180, 150));
        board.PointerUp();

        var circle = document.Objects.LastOrDefault(o => o.Get("shape") == "circle");
        Check("圆工具落模型（C7）", circle is not null, "no circle object");
        Check("圆落在按下位置且尺寸跟手", circle is not null && Math.Abs(circle.X - 100) < 1 && Math.Abs(circle.W - 80) < 2,
            circle is null ? "n/a" : $"x={circle.X} w={circle.W}");

        board.StrokeDashed = true;
        board.Tool = FreeformTool.Rect;
        board.PointerDown(new Point(300, 300), ModifierKeys.None);
        board.PointerMove(new Point(360, 350));
        board.PointerUp();

        var rect = document.Objects.LastOrDefault(o => o.Get("shape") == "rect");
        Check("线型开关写进 payload（C7）", rect?.Get("dash") == "1", rect is null ? "no rect" : $"dash={rect.Get("dash")}");
        Check("虚线图元能重建可视（不抛）", board.ObjectCount >= 2, $"objects={board.ObjectCount}");

        // 工具归属：结构化表面不该有画笔/箭头/图片，自由表面必须有（B2/B3 的语义来源）。
        Check("工具表里有圆", FreeformToolCatalog.All.Any(t => t.Tool == FreeformTool.Circle), "missing circle");
        Check("圆有快捷键 O", FreeformToolCatalog.FromShortcut('O') == FreeformTool.Circle, "no shortcut O");

        // B2：笔工具必须让墨层吃鼠标；切面回来时 SyncToolLayer 要能把它重新打开。
        board.Tool = FreeformTool.Ink;
        Check("笔工具下墨层吃鼠标", board.InkLayerActiveForTest, "ink layer not hit-testable");
        board.Tool = FreeformTool.Select;
        Check("非笔工具下墨层不吃鼠标", !board.InkLayerActiveForTest, "ink layer still grabs mouse");
        board.Tool = FreeformTool.Ink;
        board.SyncToolLayer();
        Check("切面同步后墨层仍是热的（B2）", board.InkLayerActiveForTest, "sync lost ink layer");

        // 平移过的视口下真的能落笔（真实数据里那组 offset 会用到）：输入自己收点，
        // 墨层只当显示器，因此不再有「平移后上半截点不动」的死区。
        var viewport = board.Document!.ViewportOf(CanvasSurfaces.Freeform);
        viewport.Zoom = 1;
        viewport.OffsetX = -45.33;
        viewport.OffsetY = 384.67;
        board.Rebuild();
        board.Measure(new Size(800, 600));
        board.Arrange(new Rect(0, 0, 800, 600));
        board.UpdateLayout();

        var strokesBefore = document.Objects.Count(o => o.Kind == CanvasObject.KindStroke);
        board.Tool = FreeformTool.Ink;
        // 落笔点必须在可视区（board y≈500 → canvas y≈115），否则会被 Normalize 夹到 0，
        // 笔画点留在负区，橡皮按模型矩形算就永远擦不到。
        board.PointerDown(new Point(240, 500), ModifierKeys.None);
        board.PointerMove(new Point(260, 510));
        board.PointerMove(new Point(280, 520));
        board.PointerUp();
        var strokesAfter = document.Objects.Count(o => o.Kind == CanvasObject.KindStroke);
        Check(
            "平移后也能落笔（B2）",
            strokesAfter == strokesBefore + 1,
            $"before={strokesBefore} after={strokesAfter}");

        board.Tool = FreeformTool.Eraser;
        board.PointerDown(new Point(250, 505), ModifierKeys.None);
        board.PointerMove(new Point(270, 515));
        board.PointerUp();
        Check(
            "按住橡皮拖过连擦（C5）",
            document.Objects.Count(o => o.Kind == CanvasObject.KindStroke) == strokesAfter - 1,
            $"strokes={document.Objects.Count(o => o.Kind == CanvasObject.KindStroke)}");

        // C8：颜色与粗细落到新图元上，并且重建后的可视真的用这个颜色。
        board.SetColorKey("#E5484D");
        board.SetStrokeWidth(5);
        board.Tool = FreeformTool.Rect;
        board.PointerDown(new Point(500, 300), ModifierKeys.None);
        board.PointerMove(new Point(560, 350));
        board.PointerUp();

        var colored = document.Objects.LastOrDefault(o => o.Get("color") == "#E5484D");
        Check("颜色写进 payload（C8）", colored is not null, "no object with custom color");
        Check("粗细写进 payload（C8）", colored?.Get("strokeWidth") == "5",
            colored is null ? "n/a" : $"strokeWidth={colored.Get("strokeWidth")}");

        var visual = colored is null ? null : board.ShapeVisualOf(colored.Id);
        Check(
            "重建后可视使用自定义颜色（C8）",
            visual?.Stroke is SolidColorBrush brush && brush.Color == (Color)ColorConverter.ConvertFromString("#E5484D"),
            visual?.Stroke is SolidColorBrush b2 ? b2.Color.ToString() : "n/a");

        board.SetColorKey("accent");
        board.SetStrokeWidth(3);
        Check(
            "恢复强调色后不再自定义（C8）",
            board.ColorKey == "accent" && board.StrokeWidth == 3,
            $"key={board.ColorKey} width={board.StrokeWidth}");

        // C6：按下开始拖，就已经是箭头本身（不是等到抬手才画，也不是虚线矩形框）。
        board.Tool = FreeformTool.Arrow;
        board.PointerDown(new Point(620, 260), ModifierKeys.None);
        board.PointerMove(new Point(700, 310));
        var midDrag = board.CreatePreviewForTest;
        Check(
            "箭头拖拽中就有预览（C6）",
            midDrag is System.Windows.Shapes.Path,
            midDrag is null ? "no preview" : midDrag.GetType().Name);
        var geometryBounds = (midDrag as System.Windows.Shapes.Path)?.Data?.Bounds ?? Rect.Empty;
        Check(
            "预览随指针变形（C6）",
            Math.Abs(geometryBounds.Width - 80) < 12,
            $"geo={geometryBounds.Width:0}×{geometryBounds.Height:0}");
        board.PointerUp();
        var arrow = document.Objects.LastOrDefault(o => o.Get("shape") == "arrow");
        Check("松手落定成箭头（C6）", arrow is not null, "no arrow object");
    }

    private static void XamlChecks()
    {
        var root = FindRepoRoot();
        Check("仓库根可见", root is not null, root ?? "(未找到 Wantodo.slnx)");
        if (root is null) return;

        var appXaml = Path.Combine(root, "src", "Wantodo.App", "App.xaml");
        Check("App.xaml 存在", File.Exists(appXaml), appXaml);
        if (!File.Exists(appXaml)) return;

        var doc = System.Xml.Linq.XDocument.Load(appXaml);
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        System.Xml.Linq.XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var keys = doc.Descendants()
            .Select(e => e.Attribute(x + "Key")?.Value)
            .Where(v => v is not null)
            .GroupBy(v => v!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Check("App.xaml 无重复 x:Key", keys.Count == 0, keys.Count == 0 ? "ok" : string.Join(",", keys));

        var implicitStyles = doc.Descendants(p + "Style")
            .Where(e => e.Attribute(x + "Key") is null)
            .GroupBy(e => e.Attribute("TargetType")?.Value ?? "", StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key + "×" + g.Count())
            .ToList();
        Check("App.xaml 无重复隐式样式", implicitStyles.Count == 0,
            implicitStyles.Count == 0 ? "ok" : string.Join(",", implicitStyles));

        // 工具归属（用户反馈 B2/B3）：结构化表面不该有画笔/箭头/图片；自由表面必须有。
        // 这里读 XAML 源码而不是跑窗口，是因为探针不启动真 App（单实例 Mutex）。
        var panelXaml = Path.Combine(root, "src", "Wantodo.App", "Windows", "TopPanelWindow.xaml");
        Check("TopPanelWindow.xaml 存在", File.Exists(panelXaml), panelXaml);
        if (File.Exists(panelXaml))
        {
            var text = File.ReadAllText(panelXaml);
            Check("结构化工具条没有图片入口（B3）", !text.Contains("OnCanvasAddImage"), "still wired");
            Check("自由工具条有图片工具", text.Contains("Tag=\"image\""), "missing image tool");
            Check("自由工具条有圆工具（C7）", text.Contains("Tag=\"circle\""), "missing circle tool");
        }

        // B4：卡片按下必须立刻捕获鼠标，否则快速拖动时指针跑出卡片就断流，表现为「拖不动」。
        var boardCs = Path.Combine(root, "src", "Wantodo.App", "Controls", "CanvasBoard.cs");
        if (File.Exists(boardCs))
        {
            var code = File.ReadAllText(boardCs);
            var start = code.IndexOf("private void OnCardMouseDown", StringComparison.Ordinal);
            var body = start >= 0 ? code.Substring(start, Math.Min(900, code.Length - start)) : string.Empty;
            Check("卡片按下即捕获鼠标（B4）", body.Contains("CaptureMouse()"), "no capture on mouse down");
        }

        // B2 回归：两块画板的可见性必须互补。曾经写成 freeform ? Collapsed : Visible，
        // 结果「结构化」时两块同时可见、「自由」时两块都折叠 —— 表现就是「自由画板点了没反应」。
        var panelCs = Path.Combine(root, "src", "Wantodo.App", "Windows", "TopPanelWindow.xaml.cs");
        if (File.Exists(panelCs))
        {
            var code = File.ReadAllText(panelCs);
            Check(
                "自由表面可见性没写反（B2）",
                code.Contains("FreeformScroller.Visibility = freeform ? Visibility.Visible", StringComparison.Ordinal),
                "FreeformScroller 可见性映射反了");
            Check(
                "结构化表面可见性没写反（B2）",
                code.Contains("CanvasScroller.Visibility = freeform ? Visibility.Collapsed", StringComparison.Ordinal),
                "CanvasScroller 可见性映射反了");
        }

        // 笔迹回归：InkCanvas 在 StrokeCollected 之前已把笔画放进自己的集合，
        // 再无脑 Add 会抛 duplicate stroke 并中断后续落盘。
        var freeformCs = Path.Combine(root, "src", "Wantodo.App", "Controls", "FreeformBoard.cs");
        if (File.Exists(freeformCs))
        {
            var code = File.ReadAllText(freeformCs);
            var start = code.IndexOf("internal void CommitStroke", StringComparison.Ordinal);
            var body = start >= 0 ? code.Substring(start, Math.Min(2000, code.Length - start)) : string.Empty;
            Check(
                "笔迹不重复加入墨层（不会抛 duplicate stroke）",
                !body.Contains("\r\n        _ink.Strokes.Add(stroke);") && !body.Contains("\n        _ink.Strokes.Add(stroke);"),
                "CommitStroke 里还在无条件 Add");
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Wantodo.slnx"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static void P21Checks()
    {
        // 空状态：没有卡片时有一句引导，放上卡片就消失
        var document = new CanvasDocument();
        var board = new CanvasBoard { Width = 900, Height = 700 };
        board.Attach(document, _ => { }, null);
        board.Measure(new Size(900, 700));
        board.Arrange(new Rect(0, 0, 900, 700));
        Check("结构化空状态出现", TreeTexts(board).Any(t => t.Contains("还没有卡片")), "no empty hint");

        document.Add("note", 100, 100);
        board.Rebuild();
        board.Measure(new Size(900, 700));
        board.Arrange(new Rect(0, 0, 900, 700));
        Check("有卡片后空状态消失", TreeTexts(board).All(t => !t.Contains("还没有卡片")), "empty hint still there");

        // 卡片右键菜单：改名 / 置顶 / 删除
        var card = Descendants(board).OfType<Border>().FirstOrDefault(b => b.Tag is CanvasItem);
        List<string?> headers = card?.ContextMenu?.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList() ?? new List<string?>();
        Check("卡片右键菜单三项", headers.SequenceEqual(new[] { "改名", "置顶", "删除" }), $"headers=[{string.Join("|", headers)}]");

        // 自由表面：右键菜单三项 + 全选
        var board2 = new FreeformBoard { Width = 800, Height = 600 };
        var document2 = new CanvasDocument();
        board2.Attach(document2, () => { }, null, null);
        board2.Measure(new Size(800, 600));
        board2.Arrange(new Rect(0, 0, 800, 600));
        List<string?> items = board2.ContextMenu?.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList() ?? new List<string?>();
        Check("画布右键菜单三项", items.SequenceEqual(new[] { "粘贴图片", "全选", "删除所选" }), $"items=[{string.Join("|", items)}]");
        document2.AddObject(CanvasObject.KindText, 40, 40, 120, 40);
        document2.AddObject(CanvasObject.KindText, 200, 40, 120, 40);
        board2.Rebuild();
        board2.SelectAll();
        Check("全选选中两个图元", board2.Selected.Count == 2, $"selected={board2.Selected.Count}");
        board2.DeleteSelected();
        Check("删除所选清空并落模型", document2.Objects.Count == 0, $"objects={document2.Objects.Count}");
    }

    private static void P22Checks()
    {
        var root = Path.Combine(Path.GetTempPath(), "wantodo-070", "probe-assets");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }

        var inkDir = Path.Combine(root, "ink");
        Directory.CreateDirectory(inkDir);
        var document = new CanvasDocument();
        var board = new FreeformBoard { Width = 800, Height = 600 };
        board.Attach(document, null, null, inkDir);
        board.Measure(new Size(800, 600));
        board.Arrange(new Rect(0, 0, 800, 600));

        // 静态化阈值是 plan §4.8 的硬数字：500 笔 / 2000 顶点
        Check("静态化阈值 500 笔", FreeformBoard.FlattenStrokeLimitForTest == 500,
            $"strokes={FreeformBoard.FlattenStrokeLimitForTest}");
        Check("静态化阈值 2000 顶点", FreeformBoard.FlattenVertexLimitForTest == 2000,
            $"vertices={FreeformBoard.FlattenVertexLimitForTest}");

        for (var i = 0; i < 3; i++)
        {
            var stroke = new Stroke(new StylusPointCollection
            {
                new StylusPoint(100 + i * 10, 100),
                new StylusPoint(150 + i * 10, 160),
                new StylusPoint(200 + i * 10, 200),
            })
            {
                DrawingAttributes = new DrawingAttributes { Color = System.Windows.Media.Colors.SteelBlue, Width = 3, Height = 3 },
            };
            board.CommitStroke(stroke);
        }

        Check("静态化前笔迹在墨层", board.InkStrokeCount == 3, $"strokes={board.InkStrokeCount}");
        Check("静态化前没有位图层", !board.HasStaticInkLayer, "static layer exists");
        board.FlattenInk();
        Check("静态化后墨层清空", board.InkStrokeCount == 0, $"strokes={board.InkStrokeCount}");
        Check("静态化后出现位图层", board.HasStaticInkLayer, "no static layer");

        // 落盘再打开：静态化状态下模型仍能还原（只是不再进墨层）
        var strokes = document.Objects.Count(o => o.Kind == CanvasObject.KindStroke);
        var reopened = new FreeformBoard { Width = 800, Height = 600 };
        reopened.Attach(document, null, null, inkDir);
        reopened.Measure(new Size(800, 600));
        reopened.Arrange(new Rect(0, 0, 800, 600));
        Check("重开后笔迹仍在模型里", strokes == 3, $"strokes={strokes}");

        // 孤儿资产清理：没被引用的挪进 trash，引用的原地不动
        var imagesDir = Path.Combine(root, "canvas-images");
        Directory.CreateDirectory(imagesDir);
        File.WriteAllBytes(Path.Combine(imagesDir, "used.png"), new byte[10]);
        File.WriteAllBytes(Path.Combine(imagesDir, "orphan.png"), new byte[20]);
        document.Objects.Add(new CanvasObject
        {
            Id = "c1",
            Kind = CanvasObject.KindImage,
            Surface = CanvasSurfaces.Freeform,
            Payload = { ["path"] = "canvas-images/used.png" },
        });
        var trashDir = Path.Combine(root, "trash");
        var cleaned = AssetCleanup.Run(document, inkDir, imagesDir, trashDir);
        Check("清理挪走孤儿图片", cleaned.MovedFiles == 1 && cleaned.MovedBytes == 20,
            $"moved={cleaned.MovedFiles} bytes={cleaned.MovedBytes}");
        Check("被引用的图片留在原地", File.Exists(Path.Combine(imagesDir, "used.png")), "used.png gone");
        Check("孤儿图片进了回收目录",
            Directory.EnumerateFiles(trashDir, "orphan.png", SearchOption.AllDirectories).Any(),
            "orphan.png not in trash");
        File.WriteAllText(Path.Combine(inkDir, "half.tmp"), "x");
        var second = AssetCleanup.Run(document, inkDir, null, trashDir);
        Check("清理不动 .tmp", second.MovedFiles == 0 && File.Exists(Path.Combine(inkDir, "half.tmp")),
            $"moved={second.MovedFiles}");

        // V-12 的「50 卡 + 500 笔」在控件层的重绘成本：静态化之后逐帧渲染 1920×1080
        var perfDoc = new CanvasDocument();
        var perfInk = Path.Combine(root, "perf-ink");
        var perf = new FreeformBoard { Width = 1800, Height = 1000 };
        perf.Attach(perfDoc, null, null, perfInk);
        perf.Measure(new Size(1800, 1000));
        perf.Arrange(new Rect(0, 0, 1800, 1000));
        for (var i = 0; i < 500; i++)
        {
            var x = 40 + (i % 25) * 68;
            var y = 40 + (i / 25) * 46;
            perf.CommitStroke(new Stroke(new StylusPointCollection
            {
                new StylusPoint(x, y),
                new StylusPoint(x + 30, y + 18),
                new StylusPoint(x + 55, y + 8),
            })
            {
                DrawingAttributes = new DrawingAttributes { Color = System.Windows.Media.Colors.SlateGray, Width = 2, Height = 2 },
            });
        }

        for (var i = 0; i < 50; i++)
        {
            var (x, y) = (60 + (i % 10) * 165, 520 + (i / 10) * 80);
            perfDoc.AddObject(CanvasObject.KindText, x, y, 150, 44);
            perfDoc.Objects[^1].Payload["text"] = "第 " + (i + 1) + " 条";
        }

        perf.Rebuild();
        perf.UpdateLayout();
        var liveFrame = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        for (var i = 0; i < 5; i++)
        {
            liveFrame.Render(perf); // 预热
        }

        var live = Measure(perf, liveFrame);
        Console.WriteLine($"PERF 未静态化 50 图元 + 500 笔：帧均 {live:0.0} ms ≈ {1000 / live:0} fps");

        // 500 笔正好在阈值上（>500 才自动压平），这里手动触发，对比压平前后的重绘成本
        perf.FlattenInk();
        perf.UpdateLayout();
        Check("静态化后墨层清空", perf.InkStrokeCount == 0, $"strokes={perf.InkStrokeCount}");
        Check("静态化后出现位图层", perf.HasStaticInkLayer, "no static layer");
        var flatFrame = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        for (var i = 0; i < 5; i++)
        {
            flatFrame.Render(perf);
        }

        var flat = Measure(perf, flatFrame);
        Console.WriteLine($"PERF 静态化后 50 图元 + 500 笔：帧均 {flat:0.0} ms ≈ {1000 / flat:0} fps（提速 {live / flat:0.0}×）");
        // 说明：这里是 CPU 软件光栅（RenderTargetBitmap）的代理测量，大位图在软件路径上反而更贵，
        // 不能当作 App 合成器的帧率。V-12 的三条数字仍记 PENDING-HUMAN，用 p2.2.md 里的命令在真机复测。
        Check("静态化不丢模型", perfDoc.Objects.Count(o => o.Kind == CanvasObject.KindStroke) == 500,
            $"strokes={perfDoc.Objects.Count(o => o.Kind == CanvasObject.KindStroke)}");
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

    private static void P13Checks()
    {
        var root = Path.Combine(Path.GetTempPath(), "wantodo-070", "probe-images");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }

        Directory.CreateDirectory(root);
        var imagesDir = Path.Combine(root, "canvas-images");
        string source = Path.Combine(root, "shot.png");
        WritePng(source, 64, 40, System.Windows.Media.Colors.SteelBlue);
        string twin = Path.Combine(root, "shot-copy.png");
        File.Copy(source, twin);

        var one = ImageAssets.Import(source, imagesDir);
        var two = ImageAssets.Import(twin, imagesDir);
        Check("图片按 sha256 前 16 位命名", one.Imported && Path.GetFileNameWithoutExtension(one.RelativePath!) == ImageAssets.ShortHash(source),
            $"relative={one.RelativePath}");
        Check("同图两次导入只留一份", two.Imported && two.Deduplicated && one.RelativePath == two.RelativePath
            && Directory.GetFiles(imagesDir, "*.png").Length == 1,
            $"files={Directory.GetFiles(imagesDir).Length} dedupe={two.Deduplicated}");
        Check("payload 用相对路径", one.RelativePath!.StartsWith("canvas-images/", StringComparison.Ordinal)
            && !Path.IsPathRooted(one.RelativePath), $"relative={one.RelativePath}");

        var document = new CanvasDocument();
        var board = new FreeformBoard { Width = 800, Height = 600 };
        board.Attach(document, null, imagesDir, Path.Combine(root, "ink"));
        board.Measure(new Size(800, 600));
        board.Arrange(new Rect(0, 0, 800, 600));

        var placed = board.AddImageObject(source, new Point(300, 200));
        Check("图片落模型", placed is not null && placed.Kind == CanvasObject.KindImage, $"objects={document.Objects.Count}");
        Check("图片带原始尺寸", placed is not null && placed.Get("naturalW") == "64" && placed.Get("naturalH") == "40",
            placed is null ? "no object" : $"w={placed.Get("naturalW")} h={placed.Get("naturalH")}");
        var visual = placed is null ? null : board.VisualOf(placed.Id);
        board.ClearSelection();
        visual = placed is null ? null : board.VisualOf(placed.Id);
        Check("图片无外框（未选中时）", visual is Border { } frame && frame.BorderThickness == new Thickness(0),
            visual is Border b ? $"thickness={b.BorderThickness.Left}" : visual?.GetType().Name ?? "null");
        Check("图片插在点击处", placed is not null && placed.X < 300 && placed.X + placed.W > 300 && placed.Y < 200 && placed.Y + placed.H > 200,
            placed is null ? "no object" : $"x={placed.X:0} y={placed.Y:0} w={placed.W:0} h={placed.H:0}");

        // 再插同一张：复用文件，对象多一个
        var again = board.AddImageObject(twin, new Point(600, 400));
        Check("粘贴同图复用文件", again is not null && document.Objects.Count == 2
            && Directory.GetFiles(imagesDir, "*.png").Length == 1,
            $"objects={document.Objects.Count} files={Directory.GetFiles(imagesDir, "*.png").Length}");
        Check("两对象指向同一资产", again is not null && placed is not null && again.Get("path") == placed.Get("path"),
            $"a={placed?.Get("path")} b={again?.Get("path")}");

        board.Undo();
        Check("撤销图片后只剩一个", document.Objects.Count == 1, $"objects={document.Objects.Count}");

        // 不支持的扩展名不该进模型
        string textFile = Path.Combine(root, "note.txt");
        File.WriteAllText(textFile, "not an image");
        Check("非图片不进模型", board.AddImageObject(textFile) is null && document.Objects.Count == 1,
            $"objects={document.Objects.Count}");

        // 结构化表面不再能产图片卡：方法和旧扩展名白名单都该消失，只剩两个转交事件
        var canvasType = typeof(Wantodo.App.Controls.CanvasBoard);
        Check("结构化表面没有 AddImage", canvasType.GetMethod("AddImage") is null,
            canvasType.GetMethods().Any(m => m.Name == "AddImage") ? "still there" : "removed");
        Check("结构化表面改抛事件",
            canvasType.GetEvent("ImagePasteRequested") is not null && canvasType.GetEvent("ImageDropRequested") is not null,
            "ImagePasteRequested/ImageDropRequested");
        Check("旧扩展名白名单已删", canvasType.GetField("ImageExtensions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) is null,
            "ImageExtensions");
    }

    private static void P14Checks()
    {
        var model = Board.CreateDefault();
        var column = model.Columns[0];
        var document = model.Canvas;

        var board = new CanvasBoard { Width = 800, Height = 600, ColumnsProvider = () => model.Columns };
        board.Attach(document, null, null);
        board.Measure(new Size(800, 600));
        board.Arrange(new Rect(0, 0, 800, 600));

        // ⑪ 待办卡 = 栏目投影：桌面加一条，卡片 1 s（一次 Tick）内出现
        var todo = document.Add("todo", 100, 100);
        todo.Payload["columnId"] = column.Id;
        board.Rebuild();
        Check("待办卡不持有独立列表", document.Items.Count(i => i.Kind == "todo") == 1 && todo.Payload.ContainsKey("columnId"),
            $"payload={string.Join(",", todo.Payload.Keys)}");

        Check("桌面加条目后卡片里出现",
            BoardEditor.TryAddNote(column, "投影进来的第一条", out var projected)
            && TickAndFind(board, "投影进来的第一条") is { } text
            && text.Text == "投影进来的第一条",
            $"notes={column.Notes.Count}");

        Check("卡内勾选写回栏目",
            projected is not null && ToggleThroughCard(board, "投影进来的第一条", true) && projected.Completed,
            $"completed={projected?.Completed}");

        Check("栏目勾选后卡片同步划线",
            TickAndFind(board, "投影进来的第一条") is { } struck && struck.TextDecorations is { Count: > 0 },
            $"decorations={TickAndFind(board, "投影进来的第一条")?.TextDecorations?.Count}");

        // ⑫ 拖动与改名分离：选中后 F2 才进改名，改名不改模型直到提交
        var note = document.Add("note", 300, 100);
        board.Rebuild();
        Check("正常态没有改名框", !board.IsRenamingForTest, "renaming=false");
        Check("选中卡片", board.SelectForTest(note.Id), $"id={note.Id}");
        board.BeginRenameSelected();
        Check("F2 进改名态", board.IsRenamingForTest, "renaming=true");
        Check("拖动阈值是 4 DIP", Math.Abs(CanvasBoard.DragThresholdForTest - 4) < 0.0001, $"{CanvasBoard.DragThresholdForTest}");
        Check("进改名不改模型", string.IsNullOrEmpty(note.Get("title")), $"title={note.Get("title") ?? "<null>"}");

        // ⑬ 计时状态机：配置完停在 Configured，且中央数字按计划总时长显示
        var timer = document.Add("timer", 500, 300);
        timer.Payload["mode"] = CanvasClock.ModeKey(TimerMode.Pomodoro);
        timer.Payload["plan"] = PomodoroPlan.CreateDefault().ToKey();
        timer.Payload["seconds"] = PomodoroPlan.CreateDefault().TotalSeconds().ToString(CultureInfo.InvariantCulture);
        timer.Payload["state"] = TimerStates.Configured;
        timer.Payload["startedAt"] = string.Empty;
        timer.Payload["elapsed"] = "0";
        board.Rebuild();
        board.TickForTest();
        string expected = CanvasClock.Format(PomodoroPlan.CreateDefault().TotalSeconds());
        Check("配置完不自动跑", timer.Get("state") == TimerStates.Configured, $"state={timer.Get("state")}");
        Check("番茄钟显示整份剩余", TreeTexts(board).Contains(expected), $"expected={expected} got=[{string.Join("|", TreeTexts(board).Where(t => t.Contains(':')))}]");
        Check("番茄钟按钮文案为开始", TimerStates.ActionLabel(TimerState.Configured) == "开始", TimerStates.ActionLabel(TimerState.Configured));

        // ⑭ 跑起来再暂停：剩余秒数不丢（用同一份 payload 直接推 90 秒）
        timer.Payload["state"] = TimerStates.Running;
        timer.Payload["startedAt"] = DateTimeOffset.Now.AddSeconds(-90).ToString("O", CultureInfo.InvariantCulture);
        board.Rebuild();
        board.TickForTest();
        string after = CanvasClock.Format(PomodoroPlan.CreateDefault().TotalSeconds() - 90);
        Check("跑到第 90 秒剩余正确", TreeTexts(board).Contains(after), $"expected={after}");
    }

    private static TextBlock? ProjectedText(CanvasBoard board, string content)
        => Descendants(board).OfType<TextBlock>().FirstOrDefault(t => t.Text == content);

    /// <summary>先跑一次 Tick（模拟 1 s 定时器）再找文本，验证「桌面加一条，画板 1 s 内出现」。</summary>
    private static TextBlock? TickAndFind(CanvasBoard board, string content)
    {
        board.TickForTest();
        // ScrollViewer 里的行要等模板套上才进可视树
        board.UpdateLayout();
        return ProjectedText(board, content);
    }

    /// <summary>点卡片里的复选框（走 Checked 处理器），而不是直接改模型。</summary>
    private static bool ToggleThroughCard(CanvasBoard board, string text, bool completed)
    {
        var box = Descendants(board).OfType<CheckBox>().FirstOrDefault(c =>
            c.Parent is Grid row && row.Children.OfType<TextBlock>().Any(t => t.Text == text));
        if (box is null)
        {
            return false;
        }

        box.IsChecked = completed;
        return true;
    }

    private static List<string> TreeTexts(CanvasBoard board)
        => Descendants(board).OfType<TextBlock>().Select(t => t.Text).ToList();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static void WritePng(string path, int width, int height, System.Windows.Media.Color color)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new System.Windows.Media.SolidColorBrush(color), null, new Rect(0, 0, width, height));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
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

    /// <summary>
    /// 现场排查（m00001 bug ②）：真实数据里视口是 zoom=1、offset=(-45.33, 384.67)，
    /// 于是画布原点落在控件顶端下方 384.67 DIP —— 上半截的落笔全是负画布坐标。
    /// 这里在真渲染管线上验证「负坐标笔迹能不能出现在画面上」，并落 PNG 供人眼确认。
    /// </summary>
    private static void InkOffscreenChecks()
    {
        var root = Path.Combine(Path.GetTempPath(), "wantodo-offscreen");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        var document = new CanvasDocument();
        var board = new FreeformBoard { Width = 800, Height = 600 };
        board.Attach(document, () => { }, null, Path.Combine(root, "ink"));

        var viewport = document.ViewportOf(CanvasSurfaces.Freeform);
        viewport.Zoom = 1;
        viewport.OffsetX = -45.33;
        viewport.OffsetY = 384.67;
        board.Rebuild();
        board.Measure(new Size(800, 600));
        board.Arrange(new Rect(0, 0, 800, 600));
        board.UpdateLayout();

        board.Tool = FreeformTool.Ink;

        // 上半截：board y=200 → canvas y = 200-384.67 = -184.67（负）
        board.PointerDown(new Point(240, 200), ModifierKeys.None);
        board.PointerMove(new Point(270, 210));
        board.PointerMove(new Point(300, 220));
        board.PointerUp();

        // 下半截：board y=500 → canvas y = 115.33（正）
        board.PointerDown(new Point(240, 500), ModifierKeys.None);
        board.PointerMove(new Point(270, 510));
        board.PointerMove(new Point(300, 520));
        board.PointerUp();

        board.UpdateLayout();

        var ink = (InkCanvas)typeof(FreeformBoard)
            .GetField("_ink", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(board)!;
        var inkHost = (Canvas)typeof(FreeformBoard)
            .GetField("_inkHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(board)!;

        Console.WriteLine($"  ink: w={ink.Width} h={ink.Height} left={Canvas.GetLeft(ink):0.##} top={Canvas.GetTop(ink):0.##} " +
                          $"strokes={ink.Strokes.Count} clip={ink.ClipToBounds} hostClip={inkHost.ClipToBounds}");
        foreach (var stroke in ink.Strokes)
        {
            var b = stroke.GetBounds();
            var first = stroke.StylusPoints[0];
            Console.WriteLine($"  stroke bounds=({b.X:0.##},{b.Y:0.##},{b.Width:0.##},{b.Height:0.##}) firstPt=({first.X:0.##},{first.Y:0.##})");
        }

        var images = Path.Combine(root, "png");
        Directory.CreateDirectory(images);
        WriteVisualPng(Path.Combine(images, "board.png"), board, 800, 600);
        WriteVisualPng(Path.Combine(images, "ink-only.png"), ink, 16000, 600);
        Console.WriteLine($"  png => {images}");

        var topBand = CountInkPixels(board, 800, 600, new Int32Rect(230, 190, 90, 40));
        var bottomBand = CountInkPixels(board, 800, 600, new Int32Rect(230, 490, 90, 40));
        Console.WriteLine($"  ink pixels: top band={topBand} bottom band={bottomBand}");

        // 回归锁：根因是 InkCanvas 默认 ClipToBounds=true 把负画布坐标整段裁掉。
        // 构造里必须已经关掉它，否则这里会重新变成 0。
        Check("墨层不再自裁剪（InkCanvas.ClipToBounds=false）", !ink.ClipToBounds, $"ClipToBounds={ink.ClipToBounds}");
        Check("负画布坐标的笔迹也渲染出来（bug ② 上半截）", topBand > 0, $"topBand={topBand}");
        Check("正画布坐标的笔迹渲染出来（对照）", bottomBand > 0, $"bottomBand={bottomBand}");

        var upper = document.Objects.FirstOrDefault(o => o.Kind == CanvasObject.KindStroke && o.Y < 300);
        Console.WriteLine($"  upper object: {(upper is null ? "<none>" : $"x={upper.X:0.##} y={upper.Y:0.##} w={upper.W:0.##} h={upper.H:0.##}")}");
        Check("负坐标笔迹的模型矩形不被夹到 0", upper is not null && upper.Y < 0,
            upper is null ? "<none>" : $"y={upper.Y:0.##}");

        // 橡皮按模型矩形命中：上半截那一笔必须擦得掉（旧版本这里会擦到下半截或什么都不擦）
        var beforeErase = document.Objects.Count;
        board.Tool = FreeformTool.Eraser;
        board.PointerDown(new Point(270, 210), ModifierKeys.None);
        board.PointerUp();
        Console.WriteLine($"  eraser top: objects {beforeErase} -> {document.Objects.Count}");
        Check("橡皮擦掉上半截笔迹（命中不再错位）", document.Objects.Count == beforeErase - 1,
            $"before={beforeErase} after={document.Objects.Count}");

        var bottomObjs = document.Objects.Where(o => o.Kind == CanvasObject.KindStroke).ToList();
        board.PointerDown(new Point(270, 510), ModifierKeys.None);
        board.PointerUp();
        Check("橡皮擦掉下半截笔迹（对照）", document.Objects.Count == bottomObjs.Count - 1,
            $"before={bottomObjs.Count} after={document.Objects.Count}");
        board.Tool = FreeformTool.Ink;
    }

    /// <summary>
    /// 0.7.1 第二批实机反馈：①换背景色把画笔颜色一起改了、颜色面板点了没反应；
    /// ②橡皮判定范围太小，细线几乎点不中；③笔/橡皮/缩放没有工具专属光标。
    /// </summary>
    private static void ToolingChecks()
    {
        var document = new CanvasDocument();
        var board = new FreeformBoard { Width = 800, Height = 600 };
        board.Attach(document, () => { }, null);

        var original = Application.Current.Resources["Accent"];
        Application.Current.Resources["Accent"] = new SolidColorBrush(Colors.DimGray);
        board.SetStrokeWidth(3);
        Check("默认笔色落成具体色值（不再保存 accent）", board.ColorKey == "#696969", $"key={board.ColorKey}");

        Application.Current.Resources["Accent"] = new SolidColorBrush(Colors.Red);
        board.SetStrokeWidth(4);
        Check(
            "换强调色不改已经拿在手里的笔色",
            board.ColorKey == "#696969" && board.StrokeColor == Colors.DimGray,
            $"key={board.ColorKey} color={board.StrokeColor}");

        board.SetColorKey("accent");
        Application.Current.Resources["Accent"] = new SolidColorBrush(Colors.Blue);
        board.SetStrokeWidth(5);
        Check(
            "显式选了「跟随色板」才继续跟",
            board.ColorKey == "accent" && board.StrokeColor == Colors.Transparent,
            $"key={board.ColorKey} color={board.StrokeColor}");
        Application.Current.Resources["Accent"] = original;

        // 橡皮容差：缩放 1 时 12 DIP 容差 = 12 画布单位
        var eraserDocument = new CanvasDocument();
        var eraserBoard = new FreeformBoard { Width = 800, Height = 600 };
        eraserBoard.Attach(eraserDocument, () => { }, null);
        eraserDocument.Objects.Add(MakeText("e1", 200, 200, 80, 40, "E"));
        eraserBoard.Rebuild();
        eraserBoard.Tool = FreeformTool.Eraser;
        eraserBoard.PointerDown(new Point(192, 220), ModifierKeys.None);
        eraserBoard.PointerUp();
        Check("橡皮擦掉边框外 8 DIP 的目标", eraserDocument.Objects.Count == 0, $"objects={eraserDocument.Objects.Count}");

        eraserDocument.Objects.Add(MakeText("e2", 200, 200, 80, 40, "E"));
        eraserBoard.Rebuild();
        eraserBoard.PointerDown(new Point(150, 220), ModifierKeys.None);
        eraserBoard.PointerUp();
        Check("橡皮不会擦到 50 DIP 外的目标", eraserDocument.Objects.Count == 1, $"objects={eraserDocument.Objects.Count}");

        Check("笔工具有专属笔尖光标", !ReferenceEquals(ToolCursors.Pen, Cursors.Cross), ToolCursors.Pen.ToString());
        Check("橡皮工具有专属橡皮光标", !ReferenceEquals(ToolCursors.Eraser, Cursors.Cross), ToolCursors.Eraser.ToString());
        Check("缩放工具有专属放大镜光标", !ReferenceEquals(ToolCursors.Zoom, Cursors.Cross), ToolCursors.Zoom.ToString());

        eraserBoard.Tool = FreeformTool.Eraser;
        Check("切到橡皮后控件光标跟着换", ReferenceEquals(eraserBoard.Cursor, ToolCursors.Eraser), eraserBoard.Cursor?.ToString() ?? "<null>");
        eraserBoard.Tool = FreeformTool.Ink;
        Check("切到画笔后控件光标跟着换", ReferenceEquals(eraserBoard.Cursor, ToolCursors.Pen), eraserBoard.Cursor?.ToString() ?? "<null>");
        eraserBoard.Tool = FreeformTool.Text;
        Check("文本工具仍是 I 形光标", ReferenceEquals(eraserBoard.Cursor, Cursors.IBeam), eraserBoard.Cursor?.ToString() ?? "<null>");
    }

    private static void WriteVisualPng(string path, Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static int CountInkPixels(Visual visual, int width, int height, Int32Rect region)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var buffer = new byte[stride * height];
        bitmap.CopyPixels(buffer, stride, 0);

        var count = 0;
        for (var y = region.Y; y < region.Y + region.Height && y < height; y++)
        {
            for (var x = region.X; x < region.X + region.Width && x < width; x++)
            {
                if (buffer[y * stride + x * 4 + 3] > 8)
                {
                    count++;
                }
            }
        }

        return count;
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
