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

## 未决（需要产品决定，不阻塞 Phase 0）

- [ ] 完成项默认「划线保留」还是「划线后移到底部/隐藏」？
- [ ] 栏数上限 4 还是无限？
- [ ] 是否需要全局快捷键「快速记一条」（不弹出主窗口）？
- [ ] 是否需要开机自启（默认关）？
- [ ] 便签内容是否要支持 Markdown / 链接可点？
