# 修复：画笔只有下半截能画 / 画完图形后工具栏点不动

日期：2026-11-18　对应问题：m00001（用户报「画完一笔或图形后点工具栏没反应」「笔只有在下半部分才能画」）

## 一、已修：bug ②「笔只有下半部分能画」

### 根因（探针实测确认，不是猜的）

`InkCanvas` 默认 `ClipToBounds = true`，只在自己的**局部坐标** `[0,16000]²` 里绘制。
`FreeformBoard` 为了支持无限平面，给墨层加了 `RenderTransform = TranslateTransform(8000,8000) → _scale → _offset`，
但 **RenderTransform 只影响渲染结果，不改变裁剪所用的局部坐标** —— 于是画布坐标 `y < 0`
（视口偏移 `offsetY = 384.7`，屏幕上半截正好对应负 y）的笔迹被整段裁掉。

探针 `spike/FreeformProbe` 的 `InkOffscreenChecks()` 在修复前输出：

```
ink: w=16000 h=16000 left=-8000 top=-8000 strokes=2 clip=True hostClip=False
stroke bounds=(283.83,-186.17,63,23)      ← 上半截那一笔，坐标确实是负的
stroke bounds=(283.83,113.83,63,23)       ← 下半截对照
ink pixels: top band=0 bottom band=272     ← 负坐标笔迹一个像素都没渲染
```

用反射把 `ClipToBounds` 改成 `false` 再测一次：`top band=272 bottom band=272` → 锁定裁剪。

### 改动

| 文件 | 改动 |
| --- | --- |
| `src/Wantodo.App/Controls/FreeformBoard.cs` | 构造里加 `_ink.ClipToBounds = false;`（一行根因修复） |
| `src/Wantodo.App/Controls/FreeformBoard.cs` | `CommitStroke` 去掉 `Math.Max(0, bounds.X/Y)`；`EndCreate` / `CreateText` / `AddImage` 同样去掉坐标夹取——画布原点落在可视区中央，坐标本来就可以为负，夹到 0 会让「模型矩形」与真实笔迹错位，橡皮、框选、拖动全按错的矩形命中 |
| `src/Wantodo.App/Controls/FreeformBoard.cs` | 新增 `SyncStrokeBounds(item, strokes)`，在 `RebuildInk()` 里逐笔拿 ISF 的真实外框回写模型矩形，修复历史数据（旧版本存下的 `y = 0`），并打 `[画布] 笔迹矩形自愈 N 笔` |

### 验证

- `dotnet build -warnaserror`：0 警告 0 错误。
- 探针：`dotnet spike\FreeformProbe\bin\Debug\net10.0-windows\FreeformProbe.dll` → `PROBE PASS`，退出码 0。新增断言：
  - `墨层不再自裁剪（InkCanvas.ClipToBounds=false）`
  - `负画布坐标的笔迹也渲染出来（bug ② 上半截）`（top band > 0，修复前为 0）
  - `正画布坐标的笔迹渲染出来（对照）`
  - `负坐标笔迹的模型矩形不被夹到 0`
  - `橡皮能删掉上半截那一笔`（board(270,210) → 图元 2→1）
- 真实数据启动日志出现 `[画布] 笔迹矩形自愈 3 笔（模型矩形被旧版本夹到 0）`。

## 二、另修：便携副本无法启动（导致「修改了却没有任何改变」）

`App.xaml.cs` 的单实例互斥名原来是写死的 `Local\Wantodo.SingleInstance`，按登录会话全局互斥，
**与数据根无关**。所以带 `portable.txt` 的调试副本一启动就被安装版顶掉，`Shutdown()` 直接退出，
连日志都来不及写 —— 表现出来就是「改了代码重新打开，毫无变化」。

改为 `SingleInstanceName()`：安装版沿用原名，便携版按 `<exeDir>` 的 SHA256 前 8 字节派生名字，
两个数据根各跑各的。

## 三、把 bug ① 变成可定位（诊断 + 自愈）

现场（`E:\Wantodo\logs\app-20261118.log`）：`22:42:11.306 [自由表面] rebuild 图元=8` 之后到
`22:42:19.911 [面板] collapse from=878.0` 之间 **8.6 秒零日志**；期间工具栏显示正常但点不动，
只有点面板外能收起。`SetTool` 是无条件打日志的（`[画布] 工具 {id}`），所以那几下点击根本没进 WPF。

已加入的取证与自愈：

| 位置 | 内容 |
| --- | --- |
| `src/Wantodo.App/Interop/NativeMethods.cs` | `WindowFromPoint` / `DescribeWindowAt(x,y)` / `WindowAtBelongsToCurrentProcess(x,y)` / `RaiseToTopOfBand(hwnd)` |
| `src/Wantodo.App/Windows/TopEdgePanelService.cs` | 全局按下时打 `[指针] 全局按下 screen=(x,y) 面板内=… 捕获=… 窗口=<hwnd class pid>`；仍有捕获就 `Mouse.Capture(null)` 释放（上一轮手势泄漏）；**面板范围内按下但顶层窗口不是本进程时**，打 WARN 记下「被谁压住」并把面板提回最上层 |
| `src/Wantodo.App/Windows/TopPanelWindow.xaml.cs` | `EnsureAboveWindows()`；`AddHandler(PreviewMouseDownEvent, …, handledEventsToo: true)` 打 `[指针] 面板按下 local=(x,y) 源=… 捕获=…` |
| `src/Wantodo.App/Controls/FreeformBoard.cs` | `OnMouseLeftButtonUp` 用 `try/finally` 释放捕获并打 `[指针] 画板释放捕获`；`OnMouseLeftButtonDown` 打 `[指针] 画板捕获 … 手势=…`；`OnLostMouseCapture` 打 `[指针] 画板丢失捕获` |

实测的环境事实（`zorder.ps1` / `WindowFromPoint`，DPI-aware）：
面板窗口 `ex=0x00000100`，**不是置顶窗口**；`(900,300)`、`(1500,600)`、`(1200,700)` 处的顶层窗口是
别的进程的 `Chrome_RenderWidgetHostHWND` —— 任何普通窗口只要压在面板上，那一带的点击就永远到不了面板。

### 还需要人肉眼确认（bug ① 未定案）

1. 退出正在运行的安装版（托盘右键 → 退出），确认只剩一个 wantodo。
2. 运行 `%TEMP%\wantodo-diag\Wantodo.exe`（便携数据根：`%TEMP%\wantodo-diag\data`，日志同目录 `logs\app-20261118.log`）。
3. 顶边停留出把手 → 拖下来展开 → 选「矩形」画两个框 → 再去点工具栏任意按钮。
4. 把该日志行贴回来即可定案：
   - 有 `[指针] 全局按下 … 窗口=<某别的进程>` → 面板被压住（自愈已生效，会看到 WARN）。
   - 有 `[指针] 面板按下 … 源=ToggleButton` 但没有 `[画布] 工具` → 点击到了 WPF 但被吃掉。
   - 连 `[指针] 全局按下` 都没有 → 钩子没收到（输入被更上层拦截）。

## 四、测试

```
dotnet build src\Wantodo.App\Wantodo.App.csproj -c Release -warnaserror   → 0 警告 0 错误
dotnet test tests\Wantodo.Core.Tests\Wantodo.Core.Tests.csproj -c Release  → 通过 437，失败 0
dotnet spike\FreeformProbe\bin\Debug\net10.0-windows\FreeformProbe.dll     → PROBE PASS
```
