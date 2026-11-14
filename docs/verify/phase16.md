# Phase 16 验证：顶边热区与面板可行性探针

日期：2026-11-14　机器：Win11 26200，单屏 2560×1600 @150%（dpi=144）
结论：**P1 选窗口热区；P2 覆盖判定可用，独占全屏未复现；P4 选低级鼠标钩子；P3 只验了 150%。**
产品代码零改动（`git status` 只有 `spike/TopEdgeProbe/`）。

## 一、探针

`spike/TopEdgeProbe/`（WinExe, net10.0-windows, PerMonitorV2）：

| 文件 | 作用 |
|---|---|
| `HotZoneWindow.cs` | 主屏顶边 2 DIP 实心条（`WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`），`MouseEnter` 计数 |
| `Interop/MouseEdgeHook.cs` | `WH_MOUSE_LL`，顶边物理行第一次进入计一次，`_inside` 去抖 |
| `Interop/ForegroundWatcher.cs` | `EVENT_SYSTEM_FOREGROUND` 采样前台窗口 rect / monitor / covers / dpi |
| `PanelWindow.cs` | 520×340、`ShowActivated=false`，三种收回方式（hook / capture / lostfocus） |
| `FullscreenTestWindow.cs` | 无边框铺满主屏的对照组，Esc 关闭 |
| `Interop/NativeMethods.cs` | 全部 P/Invoke（`GetWindowRect`、`MonitorFromWindow`、`GetDpiForWindow`、`SHAppBarMessage` 等） |

日志：`%TEMP%\wantodo-topedge-probe.log`。

## 二、命令与输出

```
PS> dotnet build spike\TopEdgeProbe\TopEdgeProbe.csproj -warnaserror
    0 个警告
    0 个错误
PS> dotnet build -warnaserror            # 产品解决方案
    0 个警告
    0 个错误
PS> dotnet test --no-build
已通过! - 失败: 0，通过: 205，总计: 205（665 ms）
```

## 三、P1 命中率（用户撞顶 50 次）

```
swipes=50  hotzoneHits=50  hookHits=46  edgeHint=0  taskbarAutoHideTriggers=0
```

- 2 DIP 窗口：**50/50**，零漏判。
- 低级鼠标钩子：**46/50**（92%），快速甩动时 4 次没收到移动事件。
- 贴边最大化提示：**0 次**；任务栏自动隐藏触发：**0 次**（当时也未开自动隐藏）。
- 选定：**窗口热区**。钩子只作为降级（例如全屏时窗口热区不可见）。

> 中途用户看到过一次「贴边提示」样式的弹窗，实为探针自己给热区条加的 `ToolTip`，
> 不是 Windows 的贴边布局。已删除；产品实现里不要给热区窗口加任何 ToolTip。

## 四、P2 全屏检测

判定：前台窗口 `GetWindowRect` 完全包住 `GetMonitorInfo` 的显示器矩形 →
`covers=True`；否则 `False`。物理像素，不混 DIP。

命中 `covers=True` 的样本：

```
21:17:32.497 foreground title="" rect=(0,0,2560,1600) monitor=(0,0,2560,1600) covers=True
21:17:39.370 foreground title="" rect=(0,0,2560,1600) monitor=(0,0,2560,1600) covers=True   # 无边框测试窗
21:18:07.288 foreground title="" rect=(0,0,2560,1600) monitor=(0,0,2560,1600) covers=True   # Edge 视频全屏
```

`covers=False` 的典型：

| 场景 | rect | 原因 |
|---|---|---|
| 记事本最大化 | (-11,-11,2571,1539) | 底部让出任务栏 72 px |
| Edge 最大化 | (-111,127,2346,1653) | 顶/左被 DPI 阴影与边框推出屏幕外，但下沿超出、上沿未到 0 |
| 任务栏 | (0,1528,2560,1600) | 只有 72 px 高 |
| Alt+Tab「任务切换」 | (0,0,2560,1528) | 下沿差 72 px |
| 被 cloaked 的窗口 | (0,0,0,0) | 拿不到有效矩形 |

- **无边框全屏**：能识别（两次）。
- **独占全屏**：本机没有可复现的独占全屏程序，**未验证**。
- 降级规则：检测不到就当「不是全屏」，热区照常响应——宁可多出一个把手，也不让用户撞顶没反应。
- 新发现：桌面（`Program Manager`）也会 `covers=True`，Phase 17 必须把 `Progman` / `WorkerW`
  与自身窗口排除，否则桌面状态下热区会被误判为全屏而失效。

## 五、P3 DPI

| 缩放 | 2 DIP 对应 | 实测 | 结论 |
|---|---|---|---|
| 150% | 3 px | `hotzone-window heightDip=2 dpi=144`、`hookEdgeRow=3px` | 偏差 0 |
| 100% / 200% | — | 未测 | **未验证**，需临时改缩放后复跑 |

把手位置在 Phase 17 用 DIP 布局，偏差预计 ≤1 px，但本轮没有测到。

## 六、P4 收回判定

修复坐标 bug 后：

```
mode=hook  opens=2  falseRetract=0  correctRetract=2
```

- `hook`：点面板外准确收回，误收回 0。
- `capture` / `lostfocus`：误收回 0，但 `ShowActivated=false` 的面板不会收到 `Deactivated`，
  `LostFocus` 也不可靠；`Mouse.Capture` 会截走别处的点击，不适合常驻面板。
- 选定：**低级鼠标钩子**判「点外」。

### 失败长什么样（本轮真踩到的）

1. **点面板右侧三分之一被误判为点外** → 面板收回。原因：`PointToScreen` + `ActualWidth` 是 DIP，
   钩子坐标是物理像素，150% 下混用。改用 `GetWindowRect` 物理像素后消失。
2. **钩子丢 4/50** → 快速甩动时顶边事件缺失；窗口热区不受影响。
3. **热区 ToolTip 被当成 Win11 贴边提示** → 观察污染；已删除。

## 七、已知限制

- 独占全屏未验证；漏判按「始终响应」降级。
- 100% / 200% DPI 未验证。
- `taskbarAutoHide=False`，任务栏自动隐藏与热区的相互影响未测。
- 探针只覆盖主显示器；多显示器顶边未测（Phase 17 也只做主屏）。

## 八、下一步

Phase 17：按「窗口热区 + 钩子判点外 + 覆盖检测」实现形态 A，桌面窗口类排除写进实现。
