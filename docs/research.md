# 竞品与来源速查

> 这部分是第一轮调研的摘要。原始长文（含逐条论证与链接）在用户的调研文档里。
> 对 Codex 只有一个用途：**需要参考实现时，知道去哪读别人的代码**。

## 参考项目

| 项目 | 栈 | 值得看什么 |
|---|---|---|
| [PermaNotes](https://github.com/SuganthVPM/PermaNotes) | WPF / .NET 8 | **最重要**。Progman/WorkerW 集成、用 owner 而不是 `SetParent`、放弃 `WindowChrome` 改用自定义八方向缩放、Explorer 重启后重挂、多显示器、JSON 延迟保存 + 原子写 |
| [Stikly](https://github.com/wilkcraft/stikly) | Electron + C#/PowerShell | 三档层级模式的 UI 表达；Electron 调 `SetParent`/`SetWindowPos` 的边界 |
| [Task Nest](https://github.com/GovinduSasanka/task-nest) | Electron 纯 HTML/CSS | 透明无边框待办小组件的视觉与交互密度 |
| [WinNotes](https://github.com/OttomanDeveloper/notewidget-windows) | Flutter + Win32 | 无边框、隐藏任务栏按钮、开机启动的工程细节 |
| [JXT Sticky Notes](https://github.com/jxtsidhu/JXT-Sticky-Notes) | Python + PySide6 | 划线格式、托盘、自动保存 |

## 关键结论（已并入 docs/decisions.md）

1. 「始终可见」有三种不同语义，不能混为一谈：置顶 / 桌面层（WorkerW）/ 普通无边框窗口。
2. WorkerW 属于未公开 Shell 机制，PermaNotes 自己在 README 里列了兼容性风险 —— 所以必须
   探针化 + 三档降级。
3. 「两栏待办板」不是这些项目的默认模型，这是本产品的差异化点，没有现成 UI 可抄。
4. 纯 UI 难度低；**全部风险集中在窗口层级与数据安全**。

## 对本调研的三处修正

| 原结论 | 修正 | 依据 |
|---|---|---|
| WPF / .NET 8 为首选 | 改 .NET 10 | .NET 8/9 于 2026-11-10 结束支持（<https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core>） |
| 用 `AllowsTransparency=True` 做半透明 | **禁止**，改用 DWM + 近似不透明背景 | WPF 分层窗口关闭硬件加速，常驻功耗与字体清晰度不可接受 |
| WorkerW 用 `SetParent` 或 owner 均可 | 优先 owner（`GWLP_HWNDPARENT`），`SetParent` 仅作降级 | 子窗口会改变几何/合成/生命周期语义；PermaNotes 因此改用 owner |
