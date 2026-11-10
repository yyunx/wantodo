# 分阶段开发计划

## 工作方式（比计划本身更重要）

1. **一次只做一个阶段**。Codex 拿到的是一个阶段的完整任务，而不是「把 App 做出来」。
2. **每阶段先 Plan 后 Code**：让它先输出「改哪些文件、用什么 API、验收怎么测」，你点头再写代码。
3. **阶段门控**：验收项里有任何一条没通过，就不进入下一阶段，让它修。
4. **人是唯一的视觉验收者**：Win+D、拖拽手感、文字是否发虚，Codex 无法自查，必须你肉眼确认后回一句「通过」。
5. **要求它交证据**：每条验收对应一条命令或一个日志行，写进 `docs/verify/phaseN.md`。

## 术语：阶段 vs 任务

- **阶段（Phase）**是**门控单位**，也是验收单位：阶段内的验收项必须一次性全部通过。
- **任务**是**交付粒度**：同一阶段可以拆成多次 Codex 会话/多个 commit（因此写「1–2 个任务」）。
  拆任务只影响交付节奏，不放宽验收标准——阶段内任何一个任务完成后，都不算该阶段通过。
- 一阶段拆成两个任务时，第一个任务结束后要先能构建、能跑，不留半截编译状态。

## 阶段总览

| 阶段 | 主题 | 产出 | 风险 |
|---|---|---|---|
| 0 | 桌面层探针 | 能证明 WorkerW 在你机器上怎么工作 | **最高** |
| 1 | 骨架 + 两栏静态界面 | 能看、能存、能读 | 低 |
| 2 | 交互 | 能真正当便签用 | 中 |
| 3 | 健壮性（持久化/DPI/多显示器/Explorer 重启） | 敢长期挂着不丢数据 | 中高 |
| 4 | 托盘、设置、快捷键、主题 | 顺手 | 低 |
| 5 | 打包与实测 | 能装上、能报体积 | 低 |
| 6 | 窗口尺寸自适应闭环 | 删栏/加栏时窗口跟着收放 | 低（✅ 完成） |
| 7 | 数据根外置（不落 C 盘） | 所有用户数据可迁、可改、可备份 | 中高（✅ 完成） |
| 8 | 随心记（笔记库） | 随手记 + 外部可编辑的真实文件 | 中（✅ 完成） |
| 9 | 停靠式自由布局 | 栏可停靠到任意位置、可单独缩放 | **高** |
| 10 | 0.5.0 收口 | 回归矩阵 + 发布说明 | 低 |

---

## Phase 0 — 桌面层探针（✅ 2026-11-09 通过，证据见 `docs/verify/phase0.md`）

**目的**：在写任何产品 UI 之前，先证明「窗口能挂在桌面图标层，且 Win+D 后还在」在你机器上成立。
这一阶段写的是一次性探针，放在 `spike/DesktopLayerProbe/`，允许丑。

**关于 P/Invoke 的规则落位**：探针的互操作代码在 `spike/DesktopLayerProbe/Interop/`，
命名空间 `DesktopLayerProbe.Interop`；到 Phase 1 时整目录搬进 `src/Wantodo.App/Interop/`。
不许在 `spike/` 和 `src/` 各留一份长期并存。

**实测结论（这几条直接决定 Phase 1 怎么写）**

| 问题 | 答案 |
|---|---|
| 挂到哪一层？ | `SetWindowLongPtr(GWLP_HWNDPARENT, Progman)`，owner 关系，`GetParent()=0` |
| 本机有 WorkerW 壁纸层吗？ | 没有。`SHELLDLL_DefView` 直接在 Progman 下，23 个顶层 WorkerW 都不持有它，`0x052C` 无效果 |
| 要 `SetParent` 吗？ | 不要。owner 已满足全部语义，且不破坏几何/DPI/合成 |
| Win+D 会隐藏它吗？ | 不会，`hideEvents=0`，窗口始终可见 → **不需要**任何抢回补丁 |
| 普通窗口盖得住吗？ | 能盖住（用户确认）→ 满足「不抢镜」 |
| 硬件加速保住了吗？ | 保住了，`exstyle=0x0`（无 `WS_EX_LAYERED`/`TOPMOST`/`TOOLWINDOW`），文字清晰、拖动跟手 |
| Explorer 重启怎么办？ | 监听 `TaskbarCreated`，重新取 Progman 句柄后重设 owner；300/1500/4000ms 三次幂等重挂有效，不必重建窗口 |

**验收**

- [x] `dotnet --version` → `10.0.403`，SDK 在 `%LOCALAPPDATA%\Microsoft\dotnet`（非管理员安装）
- [x] 探针日志显示 `0x052C` 前后拓扑一致、`SHELLDLL_DefView` 的父窗口是 `Progman`
- [x] `Ctrl+Alt+1`（桌面层）→ 浏览器能盖住它
- [x] 桌面层模式按 **Win+D** 后仍可见（项目生死线，已过）
- [x] `Ctrl+Alt+3` 置顶 / `Ctrl+Alt+4` 普通窗口 两个对照组行为符合预期
- [x] 结束 `explorer.exe` → 自动重挂回桌面层（日志证明 owner 换到新 Progman）
- [x] 150% 缩放下 `860×340` DIP → `1290×510` px，PerMonitorV2 清单生效
- [ ] 第二块屏 / 混合 DPI：本机只有一块屏，无法测，留待补
- [ ] DWM 材质（Acrylic/Mica）在桌面层窗口上是否生效：未测，属 P1，不阻塞
- [x] `docs/verify/phase0.md` 记录了 owner 方案、Win+D 是否被拦截（未拦截）、重挂策略

**踩坑记录（给后续阶段省时间）**
- SDK 装在用户目录时，直接双击 `bin\Debug\net10.0-windows\*.exe` 会弹出同名错误框
  （apphost 只认 `Program Files\dotnet`）。必须 `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"`。
- `Ctrl+Alt+R` 在本机被别的程序占用，`RegisterHotKey` 返回 false；热键注册失败必须记日志，不能静默。

---

## Phase 1 — 骨架 + 两栏界面（✅ 2026-11-09 通过，证据见 `docs/verify/phase1.md`）

**目的**：从「探针能跑」变成「能看、能存、能读的小组件」。交互留到 Phase 2，这一阶段只读。

**做完的东西**

- 解决方案 `Wantodo.slnx`：`src/Wantodo.Core`（net10.0，无 WPF 依赖）、`src/Wantodo.App`
  （net10.0-windows/WPF）、`tests/Wantodo.Core.Tests`（xUnit）。
- Core：`Board` / `NoteColumn` / `Note` / `WindowPlacement` + `JsonBoardStore`（三分支原子写、
  bak 回退、损坏隔离、`Normalize()` 补默认）。
- App：无边框窗口 + `WindowChrome`（`CaptionHeight=0`、`ResizeBorderThickness=6`）+ 自绘拖拽条，
  `UniformGrid Columns="{Binding Columns.Count}"` 决定栏数，`ItemsControl` 绑定数据。
- Interop：只搬 `DesktopHost.Find`（Progman 优先、WorkerW 兜底）、`DesktopLayer.Attach`
  （`Desktop`/`Normal`/`Topmost` 三档，默认 Desktop）、`ShellRestartWatcher`（`TaskbarCreated`
  → 300/1500/4000ms 三次幂等重挂）、`DesktopLayer.TryRoundCorners`（DWM 圆角，失败降级）。

**实测结论**

| 问题 | 答案 |
|---|---|
| 产品窗口还能挂桌面层吗？ | 能，`owner=Progman`、`GetParent()=0`、`layered=False`、`exstyle` 无 LAYERED/TOPMOST |
| Win+D 后还在吗？ | 在，前后 `visible=True` 且 `rect` 不变，不需要抢回补丁 |
| Explorer 重启呢？ | `TaskbarCreated` → 重挂到新 Progman（`0x204C4 → 0x30710`），三次重试全成功 |
| 位置尺寸会记住吗？ | 会，用户拖动+放大后的 `600.67×360 @ 730,80` 被写回并原样恢复 |
| 两栏是数据驱动的吗？ | 是，手改 `data.json` 的栏标题后重启，UI 标题跟着变 |
| 常驻内存多少？ | 刚启动 113.1 MB 工作集 / 69.7 MB 私有；拖动缩放 + Explorer 重启后涨到 177.5 / 117.4，是否持续增长留到 Phase 3 观察 |

**验收**
- [x] `dotnet test` 通过 12/12：首次无文件、正常读写、损坏文件回退 bak、字段缺失兼容、未知字段忽略
- [x] 启动后看到两栏 + 预设条目，位置尺寸来自 `data.json`
- [x] 手动改 `data.json` 里某栏标题，重启后 UI 跟着变（证明是数据驱动，不是硬编码）
- [x] 全窗口无 `AllowsTransparency="True"`（grep 只有显式的 `="False"`）
- [x] `dotnet build -warnaserror` → 0 警告 0 错误；`DllImport` 只出现在 `Interop/`
- [x] `docs/verify/phase1.md` 记录了日志、截图、外部核对方式

**踩坑记录**
- `double.NaN` 不能序列化进 JSON（`ArgumentException`），「没存过位置」必须用 `double?`。
- 派生属性会跟着进 JSON（`"hasPosition": true`），要 `[JsonIgnore]`。
- 默认 encoder 把中文转成 `\uXXXX`，要 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`，否则用户没法手改文件。
- PowerShell 5.1 的 `Get-Content` 默认按 ANSI 读日志 → 看日志必须 `-Encoding utf8`（否则全是乱码，会误判成编码 bug）。
- 非 DPI-aware 进程用 `GetWindowRect` + `PrintWindow` 截图会错位（表现为「右边那栏被裁掉」），要先 `SetProcessDPIAware()` 或按 `GetDpiForWindow` 换算。
- `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE)` 对 `WindowStyle="None"` 的窗口**不改变形状**（返回成功但仍是方角）。

---

## Phase 2 — 交互（✅ 2026-11-09 通过，证据见 `docs/verify/phase2.md`）

做完的东西：

- `src/Wantodo.Core/BoardEditor.cs`：加栏/删栏/改名/加条目/改文字/勾选/删除/移动，全部纯函数可单测
- `src/Wantodo.App/ViewModels/`：`ObservableObject` / `RelayCommand` / `NoteViewModel` / `ColumnViewModel` / `BoardViewModel`（无第三方 MVVM 框架）
- 交互：回车提交并保持焦点、双击就地编辑、Esc 取消、勾选划线、跨栏拖动排序、加栏/删栏（含确认）/改栏名
- 键盘：`↑/↓` 移行焦点、`Space` 勾选、`Enter` 编辑、`Shift+Enter` 条目内换行、`Esc` 取消

验收结论：

- [x] 连续敲多条待办不离开键盘、不失焦、不丢字（实测 2 条 + 中文输入法文本）
- [x] 勾选后划线出现，重启后仍是划线态
- [x] 拖到另一栏后重启，条目还在新栏
- [x] 加第 3/4 栏、删第 3 栏后布局与数据一致；删有内容的栏时有确认框，选「否」不删
- [x] `dotnet build -warnaserror` 0 警告 0 错误、`dotnet test` 32 通过

踩坑记录：

1. `AcceptsReturn=True` 的 TextBox 会先吃掉 `Enter`，提交逻辑必须挂在 `PreviewKeyDown`。
2. `Loaded` 在元素折叠时也会触发，隐藏的编辑框会在启动时抢焦点 → 改用 `IsVisibleChanged`。
3. 本地值（`BorderBrush="Transparent"`）会压住样式触发器，落点高亮要写进 `<Style>`。
4. `ScrollViewer` 默认可聚焦，点击时会从行上抢走键盘焦点 → `Focusable="False"`。
5. WPF `TextBox` 只认不带 Ctrl 的 Enter，`Ctrl+Enter` 换行不成立 → 改成 `Shift+Enter`。

---

## Phase 2.5 — 布局优化（✅ 2026-11-09 通过，证据见 `docs/verify/phase2.5.md`）

做完的东西：

- 新栏插在「最近操作过的那一栏」右边（`BoardViewModel.ActiveColumn`，键盘焦点或鼠标点进卡片都算），没有当前栏才追加到末尾
- 每栏最小 190 DIP；加栏时先把窗口撑到「每栏 280 DIP」，撑到屏幕边界为止，撑不下才退到最小宽度 + 横向滚动
- 新栏默认标题统一为 `未命名栏`
- `BoardEditor.TryAddColumn(board, title, index, out column)` 重载 + 3 个单测

验收结论：

- [x] 4 栏时每栏 280 DIP，标题与条目都看得清，不再出现一条缝（截图 `phase2.5-four.png`）
- [x] 在第 1 栏「＋ 栏」时新栏插在第 1 栏右边（`data.json` 顺序验证）
- [x] 窗口缩到 780 px 时出现横向滚动条，栏宽保底 190 DIP，桌面层级不受影响（截图 `phase2.5-narrow.png`）

踩坑记录：

1. `DataTemplate` 里不能写死栏宽，否则所有栏共享同一个值 → 绑 `Window.ColumnWidth` 依赖属性。
2. 用「当前 `ColumnWidth`」当加栏目标，会在窗口本来就窄时只撑到最小宽度；固定目标 280 DIP 才对。
3. 「当前栏」不能只认键盘焦点（用户可能只点一下栏名）→ 补 `PreviewMouseLeftButtonDown`。

---

## Phase 3 — 健壮性（✅ 2026-11-10 通过，证据见 `docs/verify/phase3.md`）

做完的东西：

- `src/Wantodo.Core/PlacementGuard.cs`：`IsFullyInside` + `Clamp` 纯函数；窗口整个跑出虚拟桌面时拉回**主屏工作区**（不是虚拟桌面，否则会被任务栏盖住）
- 写盘：`RequestSave()` 400ms 合并 + `FlushSave()` 同步落盘；`OnClosing` / `OnSessionEnding` 都强制 flush
- 系统事件：`SessionSwitch`（解锁/连回）、`DisplaySettingsChanged`（重排栏宽 + 重挂桌面层）、`PowerModeChanged`（唤醒重挂）、`OnDpiChanged`（重算栏宽）
- 启动可观测：首帧耗时、WS/private 内存、线程数写进 `app.log`

**验收**
- [x] 编辑内容后立刻强杀（实测回车后 168ms）→ `data.json` 仍是合法 JSON，至多丢 0.4s 内的输入，不出现空库
- [x] 回车后等 900ms 再强杀 → 内容已落盘
- [x] 回车后立刻点 ✕ 正常关闭 → 内容已落盘（`FlushPendingSave` 生效）
- [x] `placement.left/top = 5200,1400` 启动 → 拉回到 `1106,658.67`（工作区内，不压任务栏）
- [x] Explorer 重启后自动重挂（`Progman 0x30710 → 0x5029E`），窗口句柄和位置不变
- [x] 启动首帧 801–1069ms；常驻 private 内存 73–86MB（WS 会被系统裁剪，不作为基线）
- [ ] 改分辨率/拔副屏、DPI 变化、锁屏解锁、休眠唤醒：代码路径已实现，**未在本次实机触发**，手动测法见 `docs/verify/phase3.md` 第 4 节

**踩坑记录**
- 「至少露出 80×40 px」的兜底规则不够：窗口会停在屏幕边缘只剩一条边，用户照样找不到；改成整窗拉回。
- 拉回的目标必须是**工作区**而不是虚拟桌面：虚拟桌面包含任务栏区域，窗口会藏到任务栏底下，连栏尾输入框都点不到。
- `SystemEvents` 的回调不在 UI 线程，改窗口属性要 `Dispatcher.BeginInvoke`。
- `App` 里 `(MainWindow as MainWindow)?.FlushPendingSave()` 会因 `MainWindow` 既像属性名又像类型名而歧义，写 `Windows[0] is MainWindow window`。
- 改了 Core 的规则后必须重新 `dotnet build` **App** 再启动；只跑 `dotnet test` 不会更新 App 的 exe，会对着旧二进制做验证（这次就踩了）。

---

## Phase 4 — 托盘 / 设置 / 快捷键 / 主题（✅ 2026-11-10 通过，证据见 `docs/verify/phase4.md`）

- 托盘：左键显示隐藏、右键菜单（层级模式、栏数、主题、开机自启、退出）
- 设置面板：栏数、主题、透明度、完成项处理方式、是否自启
- 全局快捷键「快速记一条」：小输入框贴屏幕边缘，回车入库后消失
- 开机自启走 `HKCU\...\Run`，默认关，开关要能真正回滚

**验收**
- [x] 隐藏后托盘左键能召回，且不夺焦点（前台窗口仍是托盘溢出窗口，不是 wantodo）
- [x] 切换层级模式立即生效并写入配置（`GW_OWNER` 从 `0x5029E` 变 `0x0` 再变回）
- [~] 自启开关打开 → 注销重进后自动出现；关掉 → 不再出现（注册表写入/删除已验证，登录链路待人工确认）
- [x] 快速记一条：`Ctrl+Alt+Space` 唤出 → 输入 → 回车 → 30 秒内主窗能看到这一条

**做完的东西**

- `Shell/TrayIcon.cs`：`NotifyIcon` + 右键菜单（显示/隐藏、设置、层级、外观、隐藏已完成、开机自启、退出），左键单击切换、双击开设置。
- `Shell/Autostart.cs`：`HKCU\...\Run` 读写，写入带引号的 exe 绝对路径，关闭时删值。
- `SettingsWindow`：层级 / 外观 / 色调强度 / 隐藏已完成 / 开机自启，改一个立即生效并落盘。
- `QuickCaptureWindow` + `RegisterHotKey(Ctrl+Alt+Space)`：「快速记一条」记到当前激活栏。
- `ThemePalette`（Core 纯函数）+ `ThemeService`（写 `Application.Resources`）：深/浅两套基色，色调强度只混背景，文字与强调色不动。
- `Assets/wantodo.ico`：7 个尺寸的 DIB ICO；`app.manifest` PerMonitorV2；版本 0.4.0。

**踩坑记录**

1. PNG 压缩的 ICO 不能给 `System.Drawing.Icon` 用（`请求的范围扩展超过了数组的结尾`）→ 手写多尺寸 DIB ICO。
2. `UseWindowsForms` 会把 WinForms / System.Drawing 塞进全局 using，`Application`/`TextBox`/`Point` 等立刻 `CS0104` → csproj 里 `<Using Remove="..."/>`。
3. `ApplicationManifest` 指向不存在的文件 → `CS2001`，补 `app.manifest`。
4. **真崩溃**：快速记一条回车后 `在窗口关闭期间……无法调用 Close`（`0xc000041d`），`Close()` 触发 `Deactivated` 再 `Close()` → `_closing` + `CloseOnce()`；顺手挂 `DispatcherUnhandledException` 兜底。
5. **真 bug**：主题只在设置面板里生效，重启回到深色 → 构造函数里补 `ThemeService.Apply(board)`。
6. 合成点击打偏 13px：勾选圆点在 `1135,222`，我按缩放截图估成 `1122` → 一律用 UIA 的 `BoundingRectangle` 定位。

**偏离与取舍**：托盘菜单与设置面板都不放「栏数」（加栏在主窗有「＋ 栏」）；「透明度」按 D3 改成「色调强度」；「跟随系统」留到后续。

---

## Phase 5 — 打包（✅ 2026-11-10 通过，证据见 `docs/verify/phase5.md`）

- `dotnet publish -c Release -r win-x64`：先出 framework-dependent，再出 self-contained 单文件，两者都测体积
- 程序图标、窗口图标、版本号、单实例互斥（`Mutex`，二次启动唤出已有窗口）
- 提供 `install.ps1` / `uninstall.ps1`（复制到 `%LOCALAPPDATA%\Programs\wantodo`，建快捷方式，不写系统级路径）

**验收**
- [x] 干净目录下双击能启动，不报缺运行时（SC 单文件在只有 exe 的临时目录里跑起来，首帧 968ms）
- [x] 报告两种发布模式的 exe 体积与启动耗时（FDD 1.37 MB / 983ms；SC 148.74 MB / 968ms）
- [x] 重复双击不会开出两个窗口，只会把已有窗口提到前台（进程数始终 1，日志「第二次启动：已把已有窗口叫到前面」）

**做完的东西**

- 单实例：`Mutex(@"Local\Wantodo.SingleInstance")`；第二实例 `FindWindow(null,"wantodo")` +
  `PostMessage(RegisterWindowMessage("Wantodo.Show"))` 后自己退出；第一实例在 `WndProc` 里
  `BringToFront()`（临时置顶抢前台 → 撤置顶落回桌面层）。
- `install.ps1`：复制到 `%LOCALAPPDATA%\Programs\wantodo`、建开始菜单快捷方式（`-DesktopShortcut` 加桌面、
  `-StartWithWindows` 写 Run），复制前先结束运行中的实例。
- `uninstall.ps1`：停进程、删快捷方式、清 Run 值、删程序目录；**默认保留** `%APPDATA%\wantodo`，
  `-RemoveData` 才删。
- csproj 补齐产品名/描述/公司，`IncludeSourceRevisionInInformationalVersion=false`（版本号不带 git hash）。

**踩坑记录**

1. `$PSScriptRoot` 在 `powershell -File` 下为空 → 参数默认值里不能直接 `Join-Path $PSScriptRoot`，改到函数体里算并兜底 `$MyInvocation.MyCommand.Path`。
2. PowerShell 5.1 读无 BOM 的 UTF-8 会乱码 → 脚本写成带 BOM。
3. `ProductVersion` 自动拼 git hash → `IncludeSourceRevisionInInformationalVersion=false`。
4. `install.ps1` 复制 148 MB 前必须先结束进程，否则文件被占用。
5. 桌面层窗口 `SetForegroundWindow` 会被系统拒绝 → 先临时置顶再撤。

**结论**：对外分发只用 **self-contained 单文件**；framework-dependent 版在本机（运行时装在用户目录）双击起不来，
只用于对照体积。

---

## Phase 6 — 窗口尺寸自适应闭环（AutoFit / Manual）（✅ 2026-11-10 通过，证据见 `docs/verify/phase6.md`）

**结论速览**

- `LayoutMetrics`（Core 纯函数）落地：`ComputePreferredWidth(n)=16+288n`（1..4 栏 → 304/592/880/1168）、
  `ComputeMinWidth(n)=16+198n`、`FitTo`、`ComputeMaxPanes`；`SizeModes` 只认 `AutoFit`/`Manual`。
- 加栏撑宽、删栏收窄都已实测（592→880→1168、1168→880→592）；启动时按当前栏数收敛一次。
- 用户拖窗口 / 最大化 → 自动转 `Manual`，此后加删栏不再改宽度；设置面板可切回「跟随栏数」并立即收敛。
- 单测 83 通过（新增 `LayoutMetricsTests` 12 例 + 存储 2 例）。

**踩坑**

1. `SizeModes.Normalize` 第一版把 `"autofit"` 原样返回 → 归一化输出必须是常量，不能是输入的回声。
2. 合成鼠标点击送不进 WPF（`WindowFromPoint` 命中我们的 hwnd，但按钮无反应）→ 改用 UI Automation
   （`InvokePattern` / `SelectionItemPattern` / `WindowPattern`），一次成功。以后交互验证优先 UIA。
3. 老数据没有 `sizeMode` 字段 → 升级后首次启动会按栏数收敛一次尺寸，属预期行为，要写进发布说明。
4. 启动时若上次宽度对不上恢复出的栏数（例如加完栏没关就被杀），必须先 `ApplyAutoFit("启动")` 收敛，
   否则一睁眼就是被挤扁的四栏。

**偏离**：计划里写「Remove 时右边缘对齐」，实现是「左边缘不动、右边缘收」，因为左边缘是用户放窗口的位置，
动它会让人感觉窗口在跳。高度完全不参与 AutoFit。

---

**目的**：把 Phase 2.5 的「加栏撑宽」补成双向闭环——删栏要收窄，且不能跟用户手动调的尺寸打架。

**要做的**

1. **尺寸策略持久化**：`placement.sizeMode ∈ { AutoFit, Manual }`，默认 `AutoFit`。
2. **`LayoutMetrics.ComputePreferredSpan(paneCount)`**（Core 纯函数）：
   `gap × (n + 1) + preferred × n`，`preferred = 280 DIP`、`gap = 8 DIP`。
3. **结构变更收敛**：
   - Add：所需宽度 > 当前 → 撑宽（优先向右，越界则左移，夹到工作区）。
   - Remove：`AutoFit` 且当前 > 所需 → **收窄到所需**（右边缘对齐，避免整窗跳动）。
4. **手动 → Manual**：用户拖窗口边框/最大化时切 Manual；用 `_lastAppliedSize` 区分「程序设置」与
   「用户拖拽」，避免自己触发自己。
5. 高度不参与 AutoFit（高度由用户决定），只在 < `MinHeight` 时纠正。

**验收**

- [x] 3 栏（880 DIP）删 1 栏 → 回到 592 DIP；再删到 1 栏 → 继续收窄（实测 1168→880→592）。
- [x] `Manual` 状态下删栏，窗口宽度不变（实测 Manual 下加栏 900→900 px）。
- [x] 重启后策略保持；`AutoFit` 下加栏仍按 280 DIP/栏撑宽。
- [x] 单测覆盖 1–4 栏的期望宽度、工作区夹取、仅剩余一栏时的最小值（`LayoutMetricsTests` 12 例）。

**风险**：低。**预估**：0.5 天。**依赖**：Phase 2.5 的 `ColumnWidth` 逻辑。

---

## Phase 7 — 数据根外置（不落 C 盘）　✅ 2026-11-10 通过

**目的**：用户要求「产生的任何数据都不要放到 C 盘」，且位置可配、默认在程序目录下。详见
`docs/design/data-root.md`。

**结论速览**：选根顺序、目录布局、v0.4 自动导入、UI 迁移（成功 + 拒绝 + 自动重启）、`.lock` 语义
全部落地；`dotnet test` 108 通过。实测证据与踩坑见 `docs/verify/phase7.md`。本机最终落点
`E:\Wantodo`，注册表 `HKCU\Software\wantodo\DataRoot`。

**要做的**

1. **`DataRootResolver`**（Core，注入 `IFileSystem`）：`portable.txt` → 注册表指针
   `HKCU\Software\wantodo\DataRoot` → 程序目录（非 C:）→ 非系统固定盘按剩余空间降序 `<盘>:\Wantodo`
   → 兜底程序目录并告警。
2. **目录布局**：`config/settings.json`、`board/data.json(+.bak/.corrupt-<ts>)`、`notes/inbox|yyyy\MM`、
   `logs/app-yyyyMMdd.log`（留 14 天）、`trash/`（留 30 天）、`.lock`。
3. **`JsonBoardStore` / `AppLog` 改路径**：从 `%APPDATA%\wantodo` 迁到数据根；v0.4 数据自动迁移。
4. **`DataMigrator`**：Preflight → Copying（`.incoming`）→ Verifying（文件数/字节/JSON 可解析）→
   Switching（写指针 + rename）→ 提示重启；失败 Rollback，旧目录写 `.migrated.json` 不删。
5. **设置面板「数据」分组**：当前路径、剩余空间、迁移到…、打开目录、清理旧数据。

**验收**

- [x] 首次启动在非 C 盘创建 `<盘>:\Wantodo`；`%APPDATA%\wantodo` 之后不再增长（只留迁移标记）。
- [x] 设置里改路径 → 板/笔记/日志/备份完整迁移，重启后新路径生效、内容零丢失。
- [x] 目标不可写/空间不足 → 拒绝并回滚，原数据完好。
- [x] 现有 v0.4 的 `data.json` 首次升级自动迁入且可打开。
- [x] 单测：候选盘选择、路径规范化、迁移计划（含空间不足、目标非空）。

**风险**：中高（跨盘 IO、权限、断电）。**预估**：1–1.5 天。

**实际偏离**：`config/settings.json` 暂未拆出（设置仍在 `board\data.json`，`DataPaths.SettingsFile` 留位）；迁移只搬文件不搬空目录，新根首次启动由 `EnsureCreated()` 补建；迁移校验改为「逐文件复制前长度」，日志文件允许增长（`docs/verify/phase7.md` 踩坑 1）。

---

## Phase 8 — 随心记（笔记库）　✅ 2026-11-10 通过

**目的**：像手机便签一样随手记，笔记是可被外部编辑器打开的真实文件；默认新建类型可配（当前要求 txt）。

**要做的**

1. **`NotesVault`（Core）**：`Scan()` / `Create(format, title?)` / `Read(id)` / `Write(id, body)` /
   `Trash(id)`；命名 `yyyyMMdd-HHmm-<slug>.<ext>`，重名追加 `-2`；md 写 YAML front matter
   （title/created/updated/tags）。
2. **`NotesIndex`**：内存索引 + `FileSystemWatcher`（500 ms 去抖）；文件是唯一事实来源，索引随时可重建。
3. **UI**：左侧列表（标题/时间/摘要），右侧标题 + 正文；工具栏：新建、类型徽标、打开文件夹、删除（进 `trash/`）。
4. **设置项**：`defaultNoteFormat ∈ {txt, md}`、笔记子目录、是否随主窗一起显示。
5. **联动**：便签条目右键「记一笔」→ 预填标题；笔记「转为待办」→ 生成板条目（P1，可后置）。

**验收**（2026-11-10 全部通过，证据见 `docs/verify/phase8.md`）

- [x] 新建按设置类型落盘，文件名合规（无 `\ / : * ? " < > |`）；默认 txt。
- [x] 外部用记事本改动 → 2.5 秒内界面刷新（实测），且不覆盖用户正在输入的内容（`_dirty`/`_saving` 守卫）。
- [x] 删除进 `trash/`；笔记库在数据根下，切换数据根随之迁移（`DataRoot\notes`）。
- [x] 单测：命名/slug、front matter 往返、扫描、外部改名，共 14 例（总 125 通过）。

**结论速览**

- 笔记是**普通文件**，不是数据库：`<数据根>\notes\` 下 `yyyyMMdd-HHmm-<标题>.<ext>`，md 带 YAML front matter，txt 第一行当标题。
- 编辑器 0.6 秒去抖自动保存；自己保存时不触发重扫（否则会把光标打回开头）。
- 外部改动 2.5 秒内出现在窗口里，选中项不变。
- 删除进 `trash\`，文件名带删除时间戳。

**与计划的偏离**

| 计划 | 实际 | 原因 |
| --- | --- | --- |
| 独立 `NotesIndex` 类 | 直接 `Scan()` + `FileSystemWatcher` | 文件数在千级以内，全量重扫 <10ms，加索引层没有收益 |
| 「是否随主窗一起显示」设置项 | 未做，随心记是独立窗口 | 独立窗口更省事，也符合「点开就像手机便签」 |
| 「记一笔 / 转为待办」联动 | 未做（P1） | 属于跨模块联动，留到用户实际提出需求时再做 |

**踩坑**

1. 新建即写占位文件（「未命名」）→ 用户起名后文件名不会变；需要一次 `Rename` 且只在占位时改。
2. txt 与 md 的标题存法不同（首行 vs front matter）；md 缺 front matter 要能回退成纯文本，否则外部新建的 `.md` 打不开。
3. 自己保存会触发自己的 `FileSystemWatcher`，重扫会把编辑器重置；必须用 `_saving`/`_dirty` 守卫把重扫推迟。
4. `FileSystemWatcher` 回调在后台线程，动 UI 集合必须 `Dispatcher.BeginInvoke`。
5. 列表行用 `DockPanel` 时所有子元素默认 Dock 到左边，标题和时间会重叠；改三列 `Grid`。

**风险**：中（文件监听与外部编辑冲突）。**预估**：1.5–2 天。**依赖**：Phase 7。

---

## Phase 9 — 停靠式自由布局 ✅ 2026-11-10 通过

**目的**：用户要「像 Obsidian 那样把栏放到窗口任意位置」，移动后整体窗口尺寸跟着变；之后可单独拖分隔条
调整每栏比例，**窗口外框不变**。详见 `docs/design/dock-layout.md`、`docs/verify/phase9.md`。

**要做的**

1. **schema v2**：`panes[]` + `layout` 二叉树（`SplitNode(orientation, ratio, first, second)` /
   `LeafNode(paneId)`）；ratio 0.15–0.85、深度 ≤ 3、pane 最小 190×120；叶子与 panes 一一对应。
2. **不可变树操作（Core）**：`InsertBeside / MovePane / RemovePane / SetRatio /
   ComputePreferredSize / ComputeMinSize`，每次 `Validate()`。
3. **渲染**：递归生成 `Grid`（star / 6 px `GridSplitter` / star）；结构变更重建，比例变更只改 `GridLength`。
4. **拖拽**：`wantodo/pane` 与 `wantodo/note` 两种 `DataObject` 分流；5 个停靠热区（上/下/左/右/中心替换）
   + 右键盘标题「移动到 → 上/下/左/右」预设。
5. **窗口适配**：结构变更走 Phase 6 的 AutoFit；分隔条拖动**不改**窗口尺寸。
6. **迁移**：v1 `columns[]` → 均衡横向树；旧版读到 v2 拒绝启动。

**验收**

- [x] 拖到右/下/上/左，分割方向正确；其余栏尺寸不跳变。（落点算法与 `MovePane` 已通，**鼠标手感待人工确认**）
- [x] 右键盘标题的预设与拖拽结果一致。（菜单「移到最左/最右/最上/最下/与右互换」，UIA 实测可点）
- [x] 拖分隔条只改 pane 比例，窗口外框尺寸不变（0.5 → 0.4019，窗口仍 1168 DIP）。
- [x] 删栏后树坍缩、窗口按 AutoFit 收回。
- [x] v1 数据自动迁移，栏与条目零丢失；重启恢复布局（含竖直树 `Vertical 0.4`）。
- [x] 单测：插入四方位、跨层移动、移除坍缩、比例夹取、尺寸推算、非法树拒绝、迁移（150 全绿）。

**结论速览**：数据层用「单类 + `type` 判别」的二叉树（不用多态继承，省掉 JSON 转换器）；渲染递归生成嵌套
`Grid` + `GridSplitter`，结构变更重建、比例变更只改 `GridLength`；`Columns` 继续当 panes 数组，不新开
`panes[]`（见 D16）。本轮修掉四个真 bug：`GridSplitter` 量出 0×0 点不到、AutoFit 不落盘、布局与栏列表不一致
时静默回退、计算属性污染 JSON。踩坑：`DoDragDrop` 收不到合成鼠标（只能人肉验）、XAML 结构改造不能用
PowerShell 行切片（改 Python）、调试版与安装版共用单实例互斥、桌面层窗口的 UIA 要用 `FromHandle`。

**风险**：**高**（嵌套 splitter、拖拽与条目拖拽冲突、比例漂移）。**预估**：2–3 天。**依赖**：Phase 8 之后（笔记面板会复用同一套 pane）。

---

## Phase 10 — 0.5.0 收口

**要做的**

1. 版本号 `0.5.0`；两种发布模式重新出包，记录体积与启动耗时。
2. 回归矩阵：桌面层挂载、Win+D、Explorer 重启、DPI 100%/150%、多屏、强杀落盘、自启、单实例、
   数据根迁移、笔记外部编辑、停靠布局。
3. 文档同步：`docs/usage.md`（新增随心记/数据位置/停靠操作）、`docs/spec.md`、`docs/decisions.md`。
4. 发布说明 `docs/release-0.5.0.md`：能做什么、数据在哪、已知限制。

**验收**

- [ ] `dotnet build -warnaserror` 0 警告 0 错误；`dotnet test` 全绿。
- [ ] 安装脚本装完、卸载干净、数据保留；重装后数据还在。
- [ ] 回归矩阵全绿，证据进 `docs/verify/phase10.md`。

**风险**：低。**预估**：0.5–1 天。

---

## 全局风险表

| 风险 | 触发条件 | 应对 |
|---|---|---|
| WorkerW 机制变动 | Windows 功能更新 | 探针化 + 三档降级 + 日志可诊断 |
| Win+D 隐藏窗口 | Shell 行为 | Phase 0 必须解决，不许带病往下 |
| 透明窗口软件渲染 | 用了 `AllowsTransparency` | 明令禁止（见 decisions.md D3） |
| 数据丢失 | 非原子写 / 退出不落盘 | 原子写 + bak + 强杀测试 |
| 多显示器/DPI | 换显示器、缩放不同 | 启动时可见性纠正 + PerMonitorV2 |
| Codex 幻觉 API | 让它「记得」Win32 用法 | 要求它先查文档并在 `docs/verify` 贴出处；编译不过就改，不许注释掉 |
| 数据根盘未就绪 | 数据放在移动硬盘，启动时没插 | 提示「重试 / 换位置 / 只读继续」，不静默新建空库 |
| 笔记被外部占用 | 用户同时在别的编辑器里开着同一文件 | 按 mtime 比对，冲突时提示而不是覆盖 |
| 停靠树被写坏 | 手改 JSON / 崩溃时半截写 | 启动 `Validate()`，失败回 `.bak`，再失败降级均衡布局并告警 |
