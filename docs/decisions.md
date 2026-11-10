# 决策记录（ADR）

## 环境实测（2026-11-09，本机）

| 项 | 实测结果 | 影响 |
|---|---|---|
| OS | Windows NT 10.0.26200（Win11 25H2 段） | WorkerW 拓扑必须实测，不能照抄 2021 年的博客 |
| .NET SDK | **不存在**。仅 `C:\Program Files (x86)\dotnet\shared` 下有 .NET 6.0.36 x86 运行时 | 走 WPF 必须先装 SDK（非管理员可装到用户目录） |
| Visual Studio | Community 2022 17.14（`D:\Apps\protal`），**无 .NET 桌面工作负载、无内置 SDK** | 不能指望 VS 提供编译器，用 CLI |
| Node | v20.15.0（`E:\Apps\Protable\node_js`） | Electron 路线开箱可用（但见 D1） |
| Rust | 无 | Tauri 路线要先装 rustup |
| Python | 3.12（`C:\Python312`） | PySide6 路线可行 |
| WebView2 运行时 | 157.0.4341.42 已装 | 备选路线可用 |
| Git | `E:\Downloads\迅雷下载\Git\cmd\git.exe` | 可用 |
| codex CLI | 0.159.3 | 可用 |

> 结论：三条路线**没有一条是开箱的**。既然都要装工具链，就选长期成本最低的那条。

---

## D1 — 技术栈：C# / .NET 10 / WPF（否决 Electron、Tauri、Qt）

**决策时间**：2026-11-09

**背景**：产品是「常驻桌面的便签小组件」，要求轻量、Win+D 后仍可见、普通窗口能盖住它。

**关键判断**：真正的技术难点只有一个 —— 把窗口挂到桌面层（WorkerW / Progman）。这个难点
与 UI 框架无关，**任何栈都必须写同一套 Win32 P/Invoke**。所以选栈时唯一该比较的是：
「挂接代码要写多少层胶水」+「常驻成本」。

| 方案 | 挂接胶水 | 常驻内存（量级） | 本机可开工性 | 判定 |
|---|---|---|---|---|
| WPF / .NET 10 | 直接 P/Invoke，无 IPC | 40–70 MB | 需装 SDK（脚本，非管理员） | **选中** |
| Electron | 主进程 + 渲染进程 + native 模块或 PowerShell | 150–300 MB | 开箱可用 | 否决 |
| Tauri | 需 Rust 侧 Win32 + 跨语言 IPC | 60–120 MB | 需装 rustup + MSVC | 备选 |
| Qt / PySide6 | 需 pywin32 ctypes | 80–150 MB + Python 打包 | 开箱可用 | 备选 |

**理由**：
1. WPF 的窗口就是 Win32 窗口，`HwndSource` 直接拿 HWND，挂接代码最短，没有跨进程/跨语言边界。
2. 不带 Chromium / WebView 进程，常驻开销最低，符合「像小组件一样一直在那儿」。
3. 代价是 UI 开发比 HTML 慢、且 Windows-only —— 这个产品的需求本身就是 Windows-only 桌面层，
   跨平台是伪需求。

**否决 Electron 的核心理由**：一个长期挂在桌面、几乎不交互的小组件，用多进程 Chromium 来跑，
是持续的内存与电量税，与产品定位矛盾。

**否决 Tauri 的核心理由**：它解决的是「体积」，但本产品的痛点不是体积，是桌面层级；而它反而
在最关键的地方多了一层 Rust↔前端 IPC。

**版本选择**：`net10.0-windows`。
- .NET 8 / .NET 9 均于 **2026-11-10 结束支持**（明天），不能作为新项目目标。
- .NET 10 是 LTS，支持到 **2028-11-14**。
- 来源：<https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core>

---

## D2 — 桌面层 = 把「桌面宿主窗口」设为 owner（Progman 优先，WorkerW 兜底）

> **2026-11-09 Phase 0 实测修正**：本机（Win11 26200）`SHELLDLL_DefView` 直接挂在 **Progman** 下，
> 23 个顶层 `WorkerW` 没有一个持有 DefView，`0x052C` 无效果。所以「WorkerW owner」在本机没有对象可挂，
> 实际生效的是 **Progman owner**，且它同时满足「普通窗口盖得住」和「Win+D 仍在」。
> 详见 `docs/verify/phase0.md`。

**决策**：「固定在桌面」实现为「把桌面宿主窗口设为窗口 owner」，宿主按 **Progman → WorkerW** 顺序探测：

| 模式 | 实现 | 语义 |
|---|---|---|
| `Desktop`（默认） | `SetWindowLongPtr(GWLP_HWNDPARENT, 宿主)`，宿主 = 持有 `SHELLDLL_DefView` 的窗口，没有就用 `Progman` | 在桌面图标之上、普通窗口之下；Win+D 后仍在 |
| `Normal` | 普通无边框窗口 | 会被任何窗口盖住 |
| `Topmost` | `SetWindowPos(HWND_TOPMOST)` | 永远最前，会干扰工作；本产品不作默认 |

**为什么不是 `SetParent`**：`SetParent` 会把窗口变成宿主的**子窗口**，坐标变相对父窗口、
透明/DWM 合成容易出问题、且窗口生命周期被父窗口牵连。设 **owner** 只影响 Z 序分组，几何与消息
仍是顶层窗口语义。这是 PermaNotes 明确踩过并改掉的点。**实测确认**：owner 方式下 `GetParent()=0`、
`exstyle=0`、DPI 缩放正确，不需要 `SetParent`。

**为什么不做「点击穿透」**：便签要能编辑，穿透了就点不到。改为「面板只占自己那块区域」。

**已知不确定性 → 已由 Phase 0 回答的问题**（原清单保留，答案见括号）：

1. `0x052C` 是否仍能催生 WorkerW → **不能，本机调用返回成功但拓扑不变**。产品代码不要依赖它成功。
2. `SHELLDLL_DefView` 挂 Progman 还是 WorkerW → **两种都要枚举**；本机在 Progman 下。
3. Win+D 后窗口是被隐藏还是被盖住 → **本机根本不触发隐藏**（`hideEvents=0`，窗口始终可见），
   因此**不需要**拦 `WM_WINDOWPOSCHANGING`，也不需要 `ShowWindow(SW_SHOWNOACTIVATE)` 抢回。
4. Explorer 重启后 owner 失效 → **必须**监听 `TaskbarCreated` 并重挂（实测新 Progman 换了句柄）；
   300/1500/4000ms 三次幂等重挂有效，不必重建窗口。

**退化策略与验收口径（已按实测更新）**：

| 落到哪一档 | 是否通过 Phase 0 | 说明 |
|---|---|---|
| Progman owner | **通过**（本机实际路径） | 桌面层、普通窗口可覆盖、Win+D 仍在，全部满足 |
| 持有 DefView 的 WorkerW owner | **通过**（旧版 Windows 的路径） | 同上；仅为跨版本兼容保留 |
| `SetParent` 到 Progman/WorkerW | **不通过** | 子窗口会改几何/DPI/合成语义，本机不需要，不要用 |
| `Topmost` | **不通过** | 违背「不抢镜」的核心设定（普通窗口盖不住它），只是「程序还能启动」的兜底 |

落到只可能用 `Topmost` 的机器时，Codex 必须停下、报告、交回产品决策，**不得自行判定 Phase 0 通过**。
此时可讨论的产品退路（需要用户拍板）：`Topmost` + 失焦自动降级；或改为「普通窗口 + 全局快捷键呼出」。


---

## D3 — 不使用 `AllowsTransparency="True"`

**决策**：窗口用 `WindowStyle=None` + 自定义拖拽/缩放，背景近似不透明；圆角与材质用 DWM
（`DwmSetWindowAttribute` 的 `DWMWA_WINDOW_CORNER_PREFERENCE` / `DWMWA_SYSTEMBACKDROP_TYPE`），
失败则优雅降级为纯色。

**理由**：WPF 的 `AllowsTransparency=True` 走分层窗口，**会关闭硬件加速**，文字发虚、常驻时
CPU 明显上升。一个全天挂着的便签不能付这个代价。调研里「半透明背景」的建议在常驻场景下是错的。

**「透明度」在这个产品里到底指什么（消除 spec 与 decisions 的表面冲突）**：

- **能做**：面板背景色的半透明感（不透明度可调）——但注意 WPF 里 `Window.Opacity < 1`
  **同样会触发分层窗口**，必须一并禁止。所以「背景半透明」只能靠**不透明底色 + 更浅/更深的色阶**
  来模拟，视觉上是「磨砂卡片」而不是「看穿桌面」。
- **能做**：DWM 材质（`DWMWA_SYSTEMBACKDROP_TYPE`：Mica / Acrylic）。模糊由 DWM 合成器完成，
  硬件加速，不触发分层窗口。**但它能否作用于桌面层窗口，是未验证的**——列为 Phase 0 的探针项，
  失败就降级为纯色，不算缺陷。
- **不做**：真正逐像素看穿桌面（窗口后面能看见壁纸内容）。这必须有 `WS_EX_LAYERED`，代价就是
  软件渲染，与「常驻低耗」直接冲突。

因此 spec 里「透明度原样回来」应理解为：**背景色/不透明度档位这个设置值被持久化**，而不是
「窗口是透明的」。P1 的条目改名为「外观（浅色/深色/磨砂/纯色 + 色调强度）」。


---

## D4 — 数据：单文件 JSON + 原子写

**决策**：`%APPDATA%\wantodo\data.json`（单文件，含所有栏与条目）。
写入：`data.json.tmp` → `FileStream.Flush(true)` → 替换。**替换分三支**（首次保存必须走第二支，
否则 `File.Replace` 会因目标不存在而抛 `FileNotFoundException`）：

| 情形 | 做法 |
|---|---|
| `data.json` 已存在 | `File.Replace(tmp, data.json, data.bak)` |
| `data.json` 不存在（首次） | `File.Move(tmp, data.json)` |
| `File.Replace` 抛 `IOException`（被占用/不支持） | `File.Copy(data.json, data.bak, true)` + `File.Move(tmp, data.json, true)`，记 WARN |

读取：解析失败则回退 `data.bak`，再失败则备份为 `data.corrupt-<时间戳>.json` 并起空白库。


**为什么不是 SQLite**：数据量是几百条，SQLite 引入原生依赖与迁移负担，收益为零。
**为什么要 bak + corrupt 兜底**：便签最不可接受的失败是「打开发现内容没了」。

---

## D5 — 栏数模型：`columns[]` 驱动，不硬编码两栏

**决策**：数据层就是 `columns: Column[]` + `items` 内联或按 `columnId` 关联，默认 seed 两栏。
UI 用 WPF `UniformGrid.Columns` 绑定配置值（1–4）。窗口变窄时按阈值降级为单栏 + 横向滚动，
而不是把内容挤爆。

---

## D6 — 序列化的三条硬约束（Phase 1 踩出来的）

**决策**：`data.json` 必须能被用户用记事本打开、改完再重启生效。为此：

1. **不许出现 `NaN`/`Infinity`**：System.Text.Json 会直接抛
   `ArgumentException: .NET number values such as positive and negative infinity cannot be written`。
   「没存过位置」用 `double?` 的 `null`，不用 `NaN`。
2. **派生属性必须 `[JsonIgnore]`**：`HasPosition => Left.HasValue && Top.HasValue` 这类计算属性
   会被序列化进文件（`"hasPosition": true`），用户手改时是噪音也是陷阱。
3. **中文不能被转义**：默认 encoder 写出 `\u8981\u505A`，必须用
   `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`；否则「手改 data.json」这条产品承诺等于没有。

## D7 — 圆角与内存：Phase 1 实测结论（更新 D3 / 遗留项）

- **`DWMWA_WINDOW_CORNER_PREFERENCE` 对 `WindowStyle="None"` 无效**：调用返回 `S_OK`，
  但截图里窗口仍是方角。所以「圆角走 DWM、失败降级纯色」这条只在有标题栏的窗口上成立。
  Phase 4 做外观时三选一：自绘圆角（内容层用 `Border CornerRadius` + 外层同色边距）、
  用 1px 描边弱化直角、或直接接受方角。**不要**为此打开 `AllowsTransparency`。
- **常驻内存：刚启动 113.1 MB 工作集 / 69.7 MB 私有；用了几分钟（含拖动缩放、Explorer 重启）
  后涨到 177.5 / 117.4。** 私有内存初始已在 80 MB 目标内，工作集略超；关键未知是「会不会随
  时间单调上涨」。Phase 3 要查：字体缓存、DWM 表面、`RenderCapability.Tier` 是否落到软件渲染、
  以及长时间挂机后的曲线。在这些都排除前，不要拿「WPF 天生如此」当结论。

---

## D8 — 以「已实现的数据结构」为准，回改 spec（Phase 2 收尾）

`docs/spec.md` 最初画的是 `schemaVersion` / `window.*` / `items[]` 平铺 + `order` 稀疏整数，
Phase 1 实际落盘的是 `version` / `surface` / `theme` / `tone` / `placement` / `columns[].notes[]`
内联。**决定以实现为准，改 spec**，理由：

- 条目内联后，跨栏拖动 = 两个 `List<T>` 的一次 Remove + Insert，不需要维护全局 `order`。
- `order` 稀疏整数是为「大量条目 + 频繁重排」设计的；这里一个栏里最多几十条，数组顺序足够。
- 已经跑过 12 个存储用例（含损坏回退、字段缺失补默认），换结构等于把验证过的代码重写一遍。

同一轮定下的两条交互规则：

- **`Shift+Enter` 换行，`Enter` 提交**。WPF `TextBox`（`AcceptsReturn=True`）只认不带 Ctrl 的
  Enter，`Ctrl+Enter` 既不上屏也不换行，所以最初写的 `Ctrl+Enter` 方案在真机上是坏的。
- **列表的 `ScrollViewer` 必须 `Focusable="False"`**。它默认可聚焦，点击时会把键盘焦点从条目行
  抢走，表现是「行看着被选中，按空格却没反应」。

---

## D9 — 布局：栏有最小宽度、新栏插在当前栏右边（Phase 2.5）

原来的 `UniformGrid` 等分在有 3–4 栏时会把每栏压到 150 px 以下，标题被截成「突然想到…」，
草稿提示被裁掉一半。用户的原话是「最后越来越窄」。决定：

- 每栏宽度由窗口算：`(可用宽度 - 栏间距) / 栏数`，**下限 190 DIP**。
- 加栏时先把窗口撑宽到「每栏 280 DIP」，上限是屏幕工作区；撑不下才保持 190 DIP 并让外层
  `ScrollViewer` 横向滚动。**宁可有滚动条，也不把字挤没**。
- 新栏插在「最近操作过的那一栏」右边，而不是永远追加到末尾。判断「当前栏」同时认键盘焦点和
  鼠标点进卡片——只认键盘焦点不够，用户经常只是点一下栏名就想在那栏旁边加。

为什么不是「自动重排成两行」：桌面小组件的高度是用户手动调的，自动换行会让窗口高度突然变成
两倍，比横向滚动更打断。多行留到以后做显式开关。

---

## D10 — 位置兜底：只在整个窗口跑出虚拟桌面时介入，目标是主屏工作区（Phase 3）

- **只在「整个窗口都不在虚拟桌面内」时动手**。用户自己把便签卡在屏幕边缘、只露一半，是允许的；
  每次启动都强行拉回会让窗口「自己跳走」。判断用 `PlacementGuard.IsFullyInside`。
- **拉回的目标是 `SystemParameters.WorkArea`（主屏工作区），不是虚拟桌面**。虚拟桌面包含任务栏，
  拉进去以后窗口会藏在任务栏底下，带栏宽最小 190 DIP 之后连栏尾输入框都点不到——实测踩过。
- **整窗可见，不留「一条边」**。第一版规则是「至少露 80×40 px」，结果 `5200,1400` 被拉到
  `1626.67,1026.67`，窗口几乎全在屏幕外，等于没救回来。
- 多屏场景下如果窗口本来就在副屏内，不动它（`IsFullyInside` 通过）。副屏被拔掉时它就不在虚拟桌面内了，
  才会被拉回主屏。

代价：用户如果把窗口拖成「大半在主屏外」再重启，位置会变；可以接受。

---
## D11 — Phase 4 的三个取舍：托盘用 WinForms、颜色算在 Core、自启直接写 Run

- **托盘用 `System.Windows.Forms.NotifyIcon`，不引第三方库**。托盘图标本质是 Shell 接口
  （`Shell_NotifyIcon`），WinForms 的封装是 Windows Desktop SDK 自带、最省事的一层；
  WPF 没有等价控件。代价是 `UseWindowsForms` 会把 WinForms/System.Drawing 塞进全局 using，
  与 WPF 的 `Application`/`TextBox`/`Point`/`KeyEventArgs` 撞名（`CS0104`），
  所以 csproj 里 `<Using Remove="System.Windows.Forms" />` + `<Using Remove="System.Drawing" />`，
  真要 WinForms 类型时用 `WinForms = System.Windows.Forms` 别名。这仍然不违反「第一版不引入第三方 NuGet」。
- **颜色计算放 Core 的 `ThemePalette`（纯函数，8 个单测），App 侧只做「把颜色写进 Resources」**。
  这样深浅色、色调混色、非法值夹取都能在没有 UI 的情况下测；`ThemeService` 只负责 `Freeze()` 和赋值。
  画刷在 XAML 里一律 `DynamicResource`，否则改主题不会立即重绘。
- **「透明度」按 D3 改成「色调强度」**。0 = 基色，1 = 背景往强调色混 18%（深色）/12%（浅色），
  文字与强调色不参与混色——这样不会出现「越调越看不清字」。
- **开机自启直接写 `HKCU\...\Run`，不用计划任务、不用服务**。单用户程序，Run 项能被用户在
  「任务管理器 → 启动」里看到和禁用，出问题也容易回滚；写失败（权限/被杀软拦）时把勾选回滚并提示。
- 托盘菜单与设置面板都**不放「栏数」**：加栏在主窗顶栏有「＋ 栏」，托盘里再放一份是重复入口。

---

## D12 — 分发形态：只对外发 self-contained 单文件；单实例用 Local 命名空间 Mutex + 注册窗口消息

- **分发用 self-contained 单文件（148.74 MB），不用 framework-dependent（1.37 MB）**。
  理由是可用性优先：FDD 版要求机器上有 .NET 10 Desktop Runtime，而本机（以及很多用户机器）
  的运行时装在用户目录，从资源管理器双击会直接弹 `You must install .NET to run this application`。
  WPF 不支持 trimming，也压不到几十 MB，所以「大一点但双击就能跑」是这笔交易里更划算的一边。
  代价：首次启动要先解压到 `%TEMP%\.net\Wantodo\...`，之后复用。
- **单实例用 `Mutex(@"Local\Wantodo.SingleInstance")` + `RegisterWindowMessage("Wantodo.Show")`**。
  用 `Local\` 而不是 `Global\`：只影响当前登录会话，注销后系统自动释放，不会出现「注销重进后
  新实例以为旧实例还在」。第二个实例不建窗口，`FindWindow(null,"wantodo")` + `PostMessage`
  通知第一个实例，然后自己 `Shutdown()`——不会出现第二个托盘图标，也不会两个进程同时写 `data.json`。
- **「叫到前面」要临时置顶**。桌面层窗口（owner=Progman）直接 `SetForegroundWindow` 会被系统拒绝，
  所以 `BringToFront()` 先 `SetWindowPos(HWND_TOPMOST)`、抢到前台后再 `HWND_NOTOPMOST` 撤掉，
  窗口仍然归 Progman 所有、落回桌面层。
- **安装只装当前用户**：`%LOCALAPPDATA%\Programs\wantodo` + 开始菜单 `.lnk`，不写 HKLM、
  不做控制面板卸载登记（用 `uninstall.ps1`）。好处是不需要管理员；代价是「应用」列表里看不到它。

---

## D13 — 窗口尺寸：AutoFit 是默认策略，用户一动就转 Manual（Phase 6）

- **`placement.sizeMode ∈ { AutoFit, Manual }`，默认 `AutoFit`**，缺字段/非法值一律回 `AutoFit`
  （`SizeModes.Normalize` 只认 `Manual`，其余返回常量 `AutoFit`——归一化的输出必须是枚举常量，
  不能是输入的回声，这一条是被单测抓出来的）。
- **AutoFit 的宽度公式放在 Core 纯函数里**（`LayoutMetrics.ComputePreferredWidth(n)=16+288n`），
  窗口侧只负责取工作区和发 `SetWindowPos`。好处是"多宽算对"这件事可以单测，不用开窗口。
- **删栏时左边缘不动、右边缘收窄**，不是计划里写的"右边缘对齐"。理由是左边缘是用户亲手放的位置，
  动它会让人感觉窗口在跳；右边缘收窄则符合"东西变少了"的直觉。
- **用户拖窗口 / 最大化 / 最小化 → 自动转 `Manual`**，此后加删栏都不再改宽度。用
  `_expectedWidth` 区分"程序自己设的宽度"和"用户拖的"：与预期差 <0.5 DIP 视为自己设的，
  避免程序把自己的动作误判成用户操作（自我触发）。
- **启动时收敛一次**。若上次宽度对不上恢复出的栏数（加完栏没关就被杀），启动即按当前栏数 AutoFit，
  否则一睁眼就是被挤扁的多栏。代价：老数据没有 `sizeMode` 字段，升级后首次启动会改一次宽度。
- **高度完全不参与 AutoFit**：高度由用户决定，程序不替他决定"多高合适"。

---
## 未决（需要产品决定，不阻塞下一步）

- [x] 完成项默认「划线保留」还是「隐藏」？→ 默认划线保留，设置面板可开「隐藏已完成的条目」（D11）
- [x] 栏数上限 4 还是无限？→ 4（`BoardEditor.MaxColumns`）
- [x] 是否需要全局快捷键「快速记一条」？→ 做，`Ctrl+Alt+Space`，记到当前激活栏
- [x] 是否需要开机自启（默认关）？→ 做，默认关，写 `HKCU\...\Run`
- [ ] 便签内容是否要支持 Markdown / 链接可点？
- [ ] 是否需要「跟随系统」主题（要订阅 `UserPreferenceChanged`）？

