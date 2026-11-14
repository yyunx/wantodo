# Phase 17 验收 — 顶边下拉面板（形态 A）

日期：2026-11-14　分支：main　HEAD：345b649（Phase 16）→ 本阶段提交见文末

## 1. 交付物

| 文件 | 作用 |
|---|---|
| `src/Wantodo.Core/Board.cs` | 新增 `ShowTopPanel`（默认 false）、`PanelHeight`（默认 320，夹 240–720）、`NormalizePanel()` |
| `src/Wantodo.Core/JsonBoardStore.cs` | 读取时夹取 `PanelHeight`，坏值不炸 |
| `src/Wantodo.App/ViewModels/BoardViewModel.cs` | `ShowTopPanel` 属性 + `TopPanelVisibilityChanged` 事件 + `SetPanelHeight` |
| `src/Wantodo.App/Windows/HotZoneWindow.cs` | 主屏顶边 2 DIP 命中条；`Opacity=0.01`、`WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE` |
| `src/Wantodo.App/Windows/TopPanelWindow.xaml(.cs)` | 下拉面板：12 DIP 把手、拖动改高度、40% 吸附、Esc/点外/上拖收回、勾选/快速添加 |
| `src/Wantodo.App/Windows/TopEdgePanelService.cs` | 开关启停、120 ms 停留、全屏让路、点外钩子 |
| `src/Wantodo.App/Interop/LowLevelMouseHook.cs` | `WH_MOUSE_LL`，只上报左键按下（屏幕物理像素） |
| `src/Wantodo.App/Interop/ForegroundWatcher.cs` | `EVENT_SYSTEM_FOREGROUND`，判断前台窗口是否盖满整块显示器 |
| `src/Wantodo.App/Interop/NativeMethods.cs` | 上述两个钩子与矩形/显示器 API 的 P/Invoke |
| `src/Wantodo.App/App.xaml.cs` | 主窗口 `Show()` 后创建服务并 `Sync()`，退出时释放 |
| `src/Wantodo.App/SettingsWindow.xaml(.cs)` | 「顶边下拉面板（主屏幕顶部，默认关）」勾选框 |
| `tests/Wantodo.Core.Tests/JsonBoardStoreTests.cs` | `TopPanel_RoundTripsAndClampsHeight` |

## 2. 命令与输出

```text
> dotnet build -warnaserror
    0 个警告
    0 个错误

> dotnet test --no-build
已通过! - 失败:     0，通过:   206，已跳过:     0，总计:   206，持续时间: 630 ms
```

启动（开关默认关，`E:\Wantodo\logs\*.log` 末三行）：

```text
2026-11-14 21:29:44.716 [INFO] 已注册全局快捷键 Ctrl+Alt+Space（快速记一条）
2026-11-14 21:29:44.908 [INFO] 托盘图标已创建
2026-11-14 21:29:44.957 [INFO] 首帧 1235ms；内存 WS=132MB private=75MB；线程=14
```

日志里**没有**「顶边下拉面板已启用」——默认关时不建热区、不装钩子。打开开关后应出现
「顶边下拉面板已启用（主屏顶边 2 DIP）」，关闭后出现「已关闭：热区与钩子都已释放」。

## 3. 实现要点

- 热区窗口常驻在主屏顶边：宽 = 主屏宽、高 2 DIP（150% 下 3 物理像素），`Opacity=0.01`
  才能参与命中（完全透明会被跳过），`ShowActivated=false` 不抢焦点。
- 停留 120 ms 才出把手；鼠标划过去不触发。
- 拖动手势：`Mouse.Capture` 后 `Height = 起点 + ΔY`，夹在 12–`PanelHeight`；正文随高度显示/隐藏。
- 松手：`Height >= FullHeight × 0.4` → 展开（160 ms `CubicEase EaseOut`），否则 140 ms 收回再 `Hide()`。
- 收回：`Esc`、低级鼠标钩子判定点面板外、向上拖回 40% 以下。
- 全屏让路：`ForegroundWatcher` 发现前台窗口矩形盖满所在显示器（排除桌面与本进程）时隐藏热区并收起面板；
  全屏解除后恢复。
- 面板与主窗口共享同一个 `BoardViewModel`，勾选/新增立即反映到桌面窗口，写盘仍走 600 ms 去抖 + 原子替换。

## 4. 与计划的偏差

1. **拖动实现**：计划写「改 `TranslateTransform` + `Clip`，不改 `Height`」。实际改 `Height`——
   面板懒加载且常态不占屏幕，若窗口固定为 760×480，就会在桌面盖住一块透明命中区，点桌面图标会被吞。
   代价是拖动时面板内布局重排；用户实测接受。若后续出现拖动卡顿，再评估「固定高度 + 裁剪」。
2. **同步方式**：计划写「复用 Phase 11 的自写标记」。实际两侧共享同一个 `BoardViewModel`，
   内存里就是同一份数据，不需要跨窗口文件同步；自写标记仍服务随笔窗口。
3. **热区文件**：计划里的 `Interop/TopEdgeHotZone.cs` 拆成 `Windows/HotZoneWindow.cs` +
   `Interop/LowLevelMouseHook.cs` + `Interop/ForegroundWatcher.cs`，分层更清楚，P/Invoke 仍只在 `Interop/`。

## 5. 需人肉眼确认

1. 设置 → 外观 → 勾「顶边下拉面板（主屏幕顶部，默认关）」。
   **期望**：勾上后无可见窗口；日志出现「顶边下拉面板已启用」。
2. 鼠标移到屏幕正上方中间，停留约 0.2 秒。**期望**：顶部中间浮出一条 120×4 的把手，
   鼠标划过顶边（不停留）不出现。
3. 按住把手往下拖。**期望**：面板跟着手指长高，内容（两组便签、勾选框、底部输入框）可见；
   拖不到 40% 就松手 → 面板收回；拖过 40% 松手 → 展开到上次高度。
4. 面板里点一条便签的 ☐。**期望**：变成 ☑ 并加删除线，桌面主窗口同一秒内同步；
   在桌面窗口取消勾选，面板也同步。来回 20 次，两边不打架。
5. 按 `Esc`；再拉出面板后点面板外任意处；再拉出后按住把手向上拖回。**期望**：三种方式都收回，
   且不会把点击漏给下面的窗口。
6. 播放一个全屏视频（或按 F11 全屏），把鼠标移到屏幕最上方。**期望**：不出把手，点击不被吞；
   退出全屏后恢复。最后关掉开关：日志出现「已关闭」，撞顶不再出把手。
   **失败长什么样**：全屏时顶边仍出把手/点击被吞 → 全屏判定漏了；关开关后热区还在 → `Stop()` 没拆干净。

## 6. 已知限制

- 只响应主显示器；显示器切换与 DPI 变化不重新注册（本期范围）。
- 全屏判定依赖前台窗口矩形；独占全屏（游戏）在 Phase 16 探针里未复现，按「检测失败保持响应」处理。
- 面板不跟随外部程序对 `board/data.json` 的修改（0.5.0 同样没有）。
- 面板不参与「新建先命名」弹窗：快速添加直接用输入的文字创建。

## 7. 下一步

- 用户肉眼确认第 5 节；通过后补勾 `docs/plan.md` Phase 17 剩余验收并单独提交 `phase-17: 补肉眼验收结果`。
- 之后按 docs/plan.md 未排期区的触发条件挑选下一项，或听用户指定（Phase 11–18 已全部完成）。

## 8. 交付后修复（0.5.1，2026-11-14）

用户实测发现三个问题，已修：

1. **拉不下来**：面板 `ShowActivated=false`，窗口未激活时 WPF 的 `CaptureMouse()` 不可靠；
   按下后鼠标一离开 12 DIP 把手带就收不到 `MouseMove`，高度卡住。
   改法：拖动改由全局低级鼠标钩子驱动（`LowLevelMouseHook` 新增 `MouseMoved` / `LeftButtonUp`；
   面板新增 `BeginDrag` / `DragTo` / `EndDrag` / `HitTestHandle`；服务在按下点落在把手带时开始拖）。
2. **点下拉选项把面板关掉**：`ComboBox` 弹出层是独立 HWND，点选项的坐标在面板矩形外，
   被钩子判成「点外面」。改法：面板暴露 `IsPickerOpen`，下拉打开时钩子不收起面板。
3. **顶部条太硬**：整窗背景由 `WindowBackground` 改为 `CardBackground`（展开后与卡片同色），
   收起时对窗口请求 DWM 圆角（`DesktopLayer.TryRoundCorners`），左右两端磨圆；
   把手改为 88×3、45% 不透明度。

复测：`build -warnaserror` 0 警告 0 错误；`test --no-build` 206 通过 / 0 失败。
第 5 节的肉眼项仍需用户在 0.5.1 上重新确认（拖动、选列、圆角观感）。
