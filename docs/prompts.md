# 给 Codex 的提示词（可直接复制）

> 用法：一次只贴一段。先贴「0. 开场」，再贴「1. Phase 0 计划」，它给出计划你点头后贴
> 「2. Phase 0 实现」。之后每阶段套用「3. 阶段模板」。
> 关键纪律：**不要一次让它做整个应用**，那是幻觉和不可验证代码的主要来源。

---

## 0. 开场（每个新会话先贴这段）

```
你在 E:\Dev\Project\wantodo 工作，这是一个 Windows 桌面便签小组件项目。

先按顺序读完这些文件，不要写任何代码：
- AGENTS.md
- docs/spec.md
- docs/decisions.md
- docs/plan.md

读完只回三件事：
1) 用 5 行以内复述产品要做成什么、明确不做什么；
2) 指出 docs/ 里你认为互相矛盾、或技术上你判断会失败的地方（没有就说没有，不要编）；
3) 列出你打算先做哪个阶段，以及为什么先做它。

约束（长期有效）：
- 技术栈已锁定为 net10.0-windows + WPF，不要提议换框架。
- Windows 的桌面层行为不做任何假设，凡是「我记得是这样」的地方，必须在跑通的证据出现前
  标注为待验证。
- 一次只推进一个阶段，不要顺手把下一个阶段的活也写了。
```

---

## 1. Phase 0 计划（✅ 已完成 2026-11-09，留档）

```
执行 docs/plan.md 里的 Phase 0，但这一轮只输出计划，不改任何文件。

计划要具体到这种程度：
- .NET 10 SDK 的安装命令、验证命令，以及装完后的 dotnet --list-sdks 预期输出；
- spike 工程的目录与文件清单，每个文件负责什么；
- DesktopLayerService 里每个方法的签名（含 Win32 声明：函数名、DllImport 的库名、
  参数类型、返回值），以及每处 P/Invoke 的依据来源（MS Learn 链接或开源实现）；
- WorkerW 的两种可能拓扑（SHELLDLL_DefView 挂在 Progman 下 / 挂在某个 WorkerW 下）
  分别怎么枚举、怎么选目标句柄；
- Win+D 的候选补偿方案，以及你打算先试哪一个、怎么判断它失败了；
- TaskbarCreated 重挂的触发与重试策略；
- 每条验收项用什么命令或什么日志行来证明。

不要输出完整实现代码。计划里凡是有不确定的地方，明确写「待探针验证」。
```

---

## 2. Phase 0 实现（✅ 已完成 2026-11-09，留档）

```
按你上面的计划实现 Phase 0。范围严格限制在：
- 安装并使用 .NET 10 SDK（装到用户目录，不要管理员权限，不要改系统 PATH 之外的东西）
- spike/DesktopLayerProbe 工程
- src/Wantodo.App/Interop/DesktopLayerService.cs（先只放探针需要的最小实现）

禁止：
- 不要创建产品 UI、不要建数据模型、不要写持久化；
- 不要引入任何 NuGet 包；
- 不要用 AllowsTransparency；
- 不要为了让编译过去而注释掉报错的代码，编译错误要真修。

做完按这个格式回报：
1) 执行过的命令 + 关键输出（dotnet --list-sdks、dotnet build 的结尾行）；
2) 探针日志路径，以及日志里 WorkerW 拓扑的结论；
3) 每条验收项的状态：通过 / 未通过 / 需要我肉眼确认；
4) 写清「需要我肉眼确认」的项各自怎么测（按什么键、看什么现象）；
5) 更新 docs/verify/phase0.md，内容与上面一致。

如果 Win+D 那条没通过，停下来告诉我你试了哪几种补偿、结果如何，不要自行降级产品需求。
```

---

## 2.5 Phase 1 实现（✅ 已完成 2026-11-09，留档；当轮由会话内直接实现，未走 Codex）

```
Phase 0 已经通过，不要再重测桌面层，也不要重新论证技术栈。

先读这三个文件：
- docs/verify/phase0.md   ← 实测结论，尤其是「已通过」的那些项
- docs/plan.md            ← 只做 Phase 1 那一节
- AGENTS.md               ← 硬性规则

Phase 0 的实测结论（直接用，别再验证）：
- 挂载方式：SetWindowLongPtr(GWLP_HWNDPARENT, 宿主窗口)，owner 关系，不是 SetParent；
- 本机（Win11 26200）SHELLDLL_DefView 直接挂在 Progman 下，没有可用的 WorkerW，0x052C 是空操作，
  所以宿主按 Progman 优先、WorkerW 兜底来探测；
- Win+D 不会隐藏它，不需要任何 WM_SHOWWINDOW 抢回逻辑；
- Explorer 重启后监听 TaskbarCreated，重新取 Progman 句柄并重设 owner 即可，不用重建窗口。
- 探针源码在 spike/DesktopLayerProbe/，可以搬可以删，但搬完之后 spike 里不许再留一份重复实现。

本轮范围（只做这些）：
1) 解决方案骨架：
   - src/Wantodo.Core（net10.0，无 WPF 依赖，可单测）
   - src/Wantodo.App（net10.0-windows，WPF）
   - tests/Wantodo.Core.Tests（xUnit）
   建 Wantodo.sln，三个工程都挂进去。
2) Wantodo.Core：Board / Column / Note 模型 + JsonBoardStore
   - 原子写三分支：目标存在走 File.Replace(tmp, data, bak)；目标不存在走 File.Move(tmp, data)；
     File.Replace 抛 IOException 时降级 Copy+Move 并记 WARN；
   - 启动时解析失败回退 data.bak，再失败隔离成 data.corrupt-<ts>.json 后起空白库；
   - 测试必须覆盖：首次无文件、正常读写、损坏文件回退 bak、bak 也坏了、字段缺失兼容、
     首次保存（目标不存在）。
3) src/Wantodo.App/Interop/：把探针里的 FindDesktopHost / AttachToDesktopLayer / ShellRestartWatcher
   搬过来（整目录搬，命名空间改成 Wantodo.App.Interop）。
   不要搬：Topmost/Normal/HWND_BOTTOM 三个对照模式、WM_SHOWWINDOW 抢回、完整拓扑 dump。
4) 主窗口：
   - 无边框、无 AllowsTransparency（也不许 Window.Opacity<1）、深色底、圆角走 DWM 失败降级纯色；
   - 两栏布局，但渲染必须遍历 columns 集合（默认 seed「要做的事情」「突然想到的问题」两栏），
     不许在 XAML 里写死两个 ColumnDefinition；
   - 启动即挂桌面层，位置尺寸从 data.json 读，关闭时落盘。
5) 界面只做「显示」：能看、能拖窗口、能改大小；增删改条目留到 Phase 2。

禁止：
- 不要做条目增删改、勾选划线、拖拽排序（那是 Phase 2）；
- 不要做置顶/普通模式切换的 UI（Phase 3）、不要做托盘和开机启动（Phase 4）；
- 不要引入任何第三方 NuGet（xUnit/测试 SDK 除外）；
- 不要因为编译报错就注释掉代码，编译错误要真修。

完成后按这个格式回报：
1) dotnet build -warnaserror 和 dotnet test 的结尾几行；
2) 逐条对照 docs/plan.md 的 Phase 1 验收项：通过 / 未通过 / 需我肉眼确认；
3) 需肉眼确认的项给我具体操作步骤；
4) 你偏离计划的地方及原因；
5) docs/verify/phase1.md 已写好，一个 commit，message 以 phase-1: 开头。
```

---

## 2.6 Phase 2 实现（✅ 已完成 2026-11-09，留档）

```
Phase 1 已通过（证据 docs/verify/phase1.md），现在只做 Phase 2「交互」。

先读：docs/verify/phase1.md、docs/spec.md（交互细节那一节）、docs/plan.md 的 Phase 2、AGENTS.md。

Phase 1 的既成事实（直接用）：
- 解决方案 Wantodo.slnx；Core 无 WPF 依赖，已有 JsonBoardStore（原子写/bak 回退/损坏隔离）+ 12 个单测；
- 主窗口 MainWindow 已无边框 + WindowChrome(CaptionHeight=0, ResizeBorderThickness=6) + 自绘拖拽条；
- 栏数由 columns 集合 + UniformGrid.Columns 绑定决定，没有写死两栏；
- 窗口已挂桌面层；ShellRestartWatcher 监听 TaskbarCreated 重挂；
- 目前条目是只读的 TextBlock（Completed=True 时显示删除线），没有输入框、没有命令。

本轮范围（只做这些）：
1) 条目编辑：回车新增并保持焦点；双击进入编辑；Backspace 删空后删除；Esc 取消编辑；
2) 勾选划线：点击条目左侧圆点或按 Space 切换 Completed，UI 立刻变删除线；
3) 拖拽排序 + 跨栏拖动，带落点指示；拖拽期间不改变 Completed/CreatedAt；
4) 栏操作：加栏、删栏（删栏前确认「里面的 N 条会一起删」）、改栏标题（双击标题）；
5) 键盘流：↑/↓ 在条目间移动、Enter 编辑、Space 勾选、Ctrl+Enter 条目内换行；
6) 所有可变操作都改 Core 里的模型，UI 只做绑定；新增逻辑必须有单测。

禁止：
- 不要做托盘/开机启动/主题设置（Phase 4）、不要做安装包（Phase 5）；
- 不要引入 MVVM 框架或第三方 NuGet；
- 不要在 View 里写持久化逻辑，落盘仍然只经 JsonBoardStore；
- 不要用 async void（事件处理器除外，且内部 try/catch + 落日志）。

验收见 docs/plan.md 的 Phase 2；完成后写 docs/verify/phase2.md，message 以 phase-2: 开头。
```

---

## 2.7 Phase 2.5 布局优化（✅ 已完成 2026-11-09，留档）

> 这一轮也是会话内直接实现的，没走 Codex。要复现的要点：
> 1. `BoardEditor.TryAddColumn(board, title, index, out column)` 重载，越界夹到 `0..Count`
> 2. `BoardViewModel.ActiveColumn` + `SetActiveColumn`，加栏按「当前栏下标 + 1」插入
> 3. XAML：栏容器换 `ScrollViewer + ItemsControl + 横向 StackPanel`，卡片 `Width` 绑 `Window.ColumnWidth`
> 4. 代码：`MinColumnWidth=190`、`PreferredColumnWidth=280`、`ColumnGap=8`；
>    `UpdateColumnWidths()` 均分且保底 190；`GrowWindowForColumns()` 加栏先撑窗口
> 5. 验收见 `docs/verify/phase2.5.md`（4 栏 280 DIP / 窄窗 190 DIP + 横向滚动）

## 2.8 Phase 3 健壮性（✅ 已完成 2026-11-10，留档）

> 同样是会话内直接实现。要复现的要点：
> 1. `Wantodo.Core/PlacementGuard.cs`：`IsFullyInside` + `Clamp`，纯函数，先在单测里把边界定死
> 2. 保存路径：`RequestSave()`（400ms `DispatcherTimer` 合并）+ `FlushSave()`（同步落盘，异常恢复脏标记）
> 3. 退出路径：`OnClosing` 与 App 的 `OnSessionEnding` 都调 `FlushPendingSave()`
> 4. 系统事件：`SessionSwitch` / `DisplaySettingsChanged` / `PowerModeChanged` 里回调要切回 UI 线程；
>    `OnDpiChanged` 重算栏宽
> 5. 位置兜底只在「整窗不在虚拟桌面内」时介入，且目标是**主屏工作区**（否则藏到任务栏底下）
> 6. 可观测：启动日志打 `首帧 Nms；WS/private/线程`，退出日志打内存快照
> 7. 验收见 `docs/verify/phase3.md`（含 4 项「需手动确认」的说明）
## 2.9 Phase 4 托盘 / 设置 / 快捷键 / 主题（✅ 已完成 2026-11-10，留档）

范围（实际做完的）：

1. `Shell/TrayIcon.cs`：`NotifyIcon` + 右键菜单；左键单击切换显示/隐藏，双击开设置。
2. `Shell/Autostart.cs`：`HKCU\...\Run` 读写，写失败时把勾选回滚并提示。
3. `SettingsWindow`：层级 / 外观 / 色调强度 / 隐藏已完成 / 开机自启，改一个立即生效并落盘。
4. `QuickCaptureWindow` + `RegisterHotKey(Ctrl+Alt+Space)`：记到当前激活栏。
5. `Core/ThemePalette` + `App/Appearance/ThemeService`：调色纯函数 + DynamicResource 写画刷。
6. 图标（7 尺寸 DIB ICO）、`app.manifest`（PerMonitorV2）、版本 0.4.0、`AssemblyName=Wantodo`。

当轮踩到的坑（已写进 verify）：PNG-in-ICO 读不出来、`UseWindowsForms` 的 `CS0104`、
`ApplicationManifest` 文件缺失、`Close()` 触发 `Deactivated` 再 `Close()` 崩进程、
主题没在启动时套用、合成点击按缩放截图估坐标误差 13px。

---

## 3. 阶段模板（Phase 2 起每阶段套用）

```
本轮阶段：Phase <N>（见 docs/plan.md）。
先读 docs/verify/phase<N-1>.md，确认上一阶段是真的通过了，有疑点先问我。

这一轮只做 Phase <N> 的验收项，不做别的。开工前先给我 10 行以内的改动清单
（要建/改哪些文件、每个文件的职责），我确认后你再动手。

实现要求：
- 遵守 AGENTS.md 的全部硬性规则；
- 新逻辑优先写进 Wantodo.Core 并配单测，WPF 层只做绑定与视觉；
- 任何 Win32 调用只能出现在 src/Wantodo.App/Interop/；
- 每完成一个可验证的小块就先构建一次，不要攒到最后一次性编译。

完成后回报：
1) dotnet build -warnaserror 与 dotnet test 的结论（贴结尾几行）；
2) 逐条对照 Phase <N> 的验收清单：通过 / 未通过 / 需我肉眼确认；
3) 需肉眼确认项的测试步骤；
4) 你偏离计划的地方及原因；
5) docs/verify/phase<N>.md 已更新。
```

---

## 4. 纠偏提示词（它跑偏时用）

```
停。你现在做的超出了本轮范围：<具体指出，例如「你在写云同步」「你换了框架」「你加了 NuGet 包」>。

回到 docs/plan.md 的 Phase <N>，只做那一节的验收项。如果你认为这个限制不对，先把理由
和代价写出来问我，不要自己决定。
```

---

## 5. 代码审查提示词（每阶段收尾跑一次）

```
以「会长期挂在别人桌面上、且不能丢数据」的标准审查你刚写的代码，只挑能造成实际损害的问题，
按严重度排序，每条给文件名:行号 + 触发条件 + 修法：

- 数据丢失：非原子写、退出不落盘、异常路径吞掉写入失败；
- 窗口生命周期：句柄失效后用旧句柄、Explorer 重启后的悬挂 owner、退出时托盘图标残留；
- 线程：WPF 线程外更新 UI、async void、Dispatcher 误用；
- 资源：HwndSource/GDI 句柄泄漏、事件未退订（长时间挂机必现的那种）；
- 平台假设：写死了单显示器、假定 DPI=100%、假定 WorkerW 一定存在。

不要提风格问题，不要提「可以更优雅」。
```

---

## 6. 什么时候该停下来问人（写进给 Codex 的常规约束）

- 需要改系统设置（注册表 Run 项、文件关联、防火墙）
- 需要引入新的运行时或框架
- 验收项在真实机器上与预期不符，且试了两种方案仍不行
- 发现 docs/ 里的产品决策自相矛盾

---

## 附：人肉验收清单（你自己按这个测，别让 Codex 自证）

每个阶段结束后，用 2 分钟过一遍：

1. 打开浏览器，窗口是否被盖住；关掉浏览器，它是否还在原位。
2. Win+D，它是否还在；再按 Win+D 回来，它是否没乱跳。
3. 随便打 3 条内容 → 任务管理器结束进程 → 重开，3 条是否都在。
4. 拖到屏幕边缘外一半，重启后是否自动回到可见区。
5. 长时间（≥30 分钟）挂着，任务管理器里内存是否稳定不涨。
6. 注销重登，位置、栏数、内容是否原样。
