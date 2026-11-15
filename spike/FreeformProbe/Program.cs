using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
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

        Console.WriteLine(_failed == 0 ? "PROBE PASS" : $"PROBE FAIL ({_failed})");
        return _failed == 0 ? 0 : 1;
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

    private static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}  ({detail})");
        if (!ok)
        {
            _failed++;
        }
    }
}
