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

## 全局风险表

| 风险 | 触发条件 | 应对 |
|---|---|---|
| WorkerW 机制变动 | Windows 功能更新 | 探针化 + 三档降级 + 日志可诊断 |
| Win+D 隐藏窗口 | Shell 行为 | Phase 0 必须解决，不许带病往下 |
| 透明窗口软件渲染 | 用了 `AllowsTransparency` | 明令禁止（见 decisions.md D3） |
| 数据丢失 | 非原子写 / 退出不落盘 | 原子写 + bak + 强杀测试 |
| 多显示器/DPI | 换显示器、缩放不同 | 启动时可见性纠正 + PerMonitorV2 |
| Codex 幻觉 API | 让它「记得」Win32 用法 | 要求它先查文档并在 `docs/verify` 贴出处；编译不过就改，不许注释掉 |
