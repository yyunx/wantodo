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

## Phase 2.5 — 布局优化（待做）

- 加栏不再永远追加到最右边：插到当前聚焦栏的右侧（没有聚焦时插到末尾）
- 栏有最小宽度，窗口放不下时横向滚动，而不是把每栏压到只剩几十像素
- 新增栏的默认标题统一（`未命名栏`）

**验收**
- [ ] 4 栏时每栏仍能看清标题与条目，不出现文字被裁成一条缝
- [ ] 在第 1 栏「＋栏」时新栏插在第 1 栏右边，而不是最末尾
- [ ] 窗口宽度不足时出现横向滚动条，滚动不影响桌面层级

---

## Phase 3 — 健壮性（1–2 个任务）

- 写盘节流 + 退出强制落盘；异常终止后重启内容完好（可用「任务管理器结束进程」验证）
- 多显示器 / 删除显示器后窗口不能跑到屏幕外（启动时做可见性纠正）
- DPI 变化（把窗口拖到不同缩放屏）后布局正确
- Explorer 重启、显示器休眠唤醒、锁屏解锁后的窗口层级保持
- 启动耗时与常驻内存记录（`dotnet-counters` 或任务管理器），写进 `docs/verify/phase3.md`

**验收**
- [ ] 编辑内容后在 1 秒内强杀进程 → 重启后内容还在（或至多丢最后 1 次编辑），不出现空库
- [ ] 拔掉副屏后重启，窗口自动回到主屏可见区
- [ ] 锁屏 10 分钟后解锁，窗口仍在桌面层
- [ ] 常驻内存（工作集）记录在案，目标 ≤ 80 MB

---

## Phase 4 — 托盘 / 设置 / 快捷键（1 个任务）

- 托盘：左键显示隐藏、右键菜单（层级模式、栏数、主题、开机自启、退出）
- 设置面板：栏数、主题、透明度、完成项处理方式、是否自启
- 全局快捷键「快速记一条」：小输入框贴屏幕边缘，回车入库后消失
- 开机自启走 `HKCU\...\Run`，默认关，开关要能真正回滚

**验收**
- [ ] 隐藏后托盘左键能召回，且不夺焦点
- [ ] 切换层级模式立即生效并写入配置
- [ ] 自启开关打开 → 注销重进后自动出现；关掉 → 不再出现
- [ ] 快速记一条：`Ctrl+Alt+Space` 唤出 → 输入 → 回车 → 30 秒内主窗能看到这一条

---

## Phase 5 — 打包（1 个任务）

- `dotnet publish -c Release -r win-x64`：先出 framework-dependent，再出 self-contained 单文件，两者都测体积
- 程序图标、窗口图标、版本号、单实例互斥（`Mutex`，二次启动唤出已有窗口）
- 提供 `install.ps1` / `uninstall.ps1`（复制到 `%LOCALAPPDATA%\Programs\wantodo`，建快捷方式，不写系统级路径）

**验收**
- [ ] 干净目录下双击能启动，不报缺运行时
- [ ] 报告两种发布模式的 exe 体积与启动耗时
- [ ] 重复双击不会开出两个窗口，只会把已有窗口提到前台

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
