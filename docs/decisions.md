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

## D2 — 桌面层 = WorkerW 作为 owner，不是 Topmost

**决策**：「固定在桌面」实现为「把 WorkerW 设为窗口 owner」，并保留三档可切换：

| 模式 | 实现 | 语义 |
|---|---|---|
| `Desktop`（默认） | `SetWindowLongPtr(GWLP_HWNDPARENT, workerW)` | 在桌面图标之上、普通窗口之下；Win+D 后仍在 |
| `Normal` | 普通无边框窗口 | 会被任何窗口盖住 |
| `Topmost` | `SetWindowPos(HWND_TOPMOST)` | 永远最前，会干扰工作 |

**为什么不是 `SetParent`**：`SetParent` 会把窗口变成 WorkerW 的**子窗口**，坐标变相对父窗口、
透明/DWM 合成容易出问题、且窗口生命周期被父窗口牵连。设 **owner** 只影响 Z 序分组，几何与消息
仍是顶层窗口语义。这是 PermaNotes 明确踩过并改掉的点。

**为什么不做「点击穿透」**：便签要能编辑，穿透了就点不到。改为「面板只占自己那块区域」。

**已知不确定性（必须在 Phase 0 用探针证明，不许假设）**：
1. Win11 25H2 上 `0x052C` 消息是否仍能催生出 WorkerW；WorkerW 数量（旧版两个、新版可能一个）；
2. 「显示桌面图标」开/关时，`SHELLDLL_DefView` 挂在 `Progman` 下还是某个 `WorkerW` 下
   —— 两种拓扑都要覆盖枚举；
3. Win+D 之后窗口是被隐藏还是只是被盖住 —— 决定要不要拦 `WM_WINDOWPOSCHANGING` /
   响应后 `ShowWindow(SW_SHOWNOACTIVATE)`；
4. Explorer 重启后 owner 句柄失效 —— 需要监听 `TaskbarCreated` 消息并重新挂接。

**退化策略**：WorkerW 完全不可用时，退到 `SetParent(Progman)`，再退到 `Topmost + WS_EX_TOOLWINDOW`，
并把降级原因写入日志和设置页。

---

## D3 — 不使用 `AllowsTransparency="True"`

**决策**：窗口用 `WindowStyle=None` + 自定义拖拽/缩放，背景近似不透明；圆角与材质用 DWM
（`DwmSetWindowAttribute` 的 `DWMWA_WINDOW_CORNER_PREFERENCE` / `DWMWA_SYSTEMBACKDROP_TYPE`），
失败则优雅降级为纯色。

**理由**：WPF 的 `AllowsTransparency=True` 走分层窗口，**会关闭硬件加速**，文字发虚、常驻时
CPU 明显上升。一个全天挂着的便签不能付这个代价。调研里「半透明背景」的建议在常驻场景下是错的。

---

## D4 — 数据：单文件 JSON + 原子写

**决策**：`%APPDATA%\wantodo\data.json`（单文件，含所有栏与条目）。
写入：`data.json.tmp` → `FileStream.Flush(true)` → `File.Replace(data.json, data.bak)`。
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
