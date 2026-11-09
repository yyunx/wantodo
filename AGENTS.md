# AGENTS.md — wantodo

Windows 桌面常驻便签小组件。贴在桌面图标层，像手机主屏的小组件；默认两栏
（「要做的事情」「突然想到的问题」），栏数可增减。

## 技术栈（已锁定，不要自行更换或提议替换）

- 目标框架：`net10.0-windows`（.NET 10 LTS，支持到 2028-11-14；.NET 8/9 已于 2026-11-10 EOL）
- UI：WPF，C# 13，`<Nullable>enable</Nullable>`
- 第三方 NuGet：第一版不引入（xUnit 测试除外）
- 目标：Windows 10 1809+ / Windows 11；开发机 Windows 11 26200，**当前未安装 .NET SDK**，
  需先用 `dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"`
  装到用户目录（非管理员）
- 选型理由与否决记录见 `docs/decisions.md`，不要在对话里重新辩论框架

## 目录约定

```
src/Wantodo.Core/            模型、持久化、纯逻辑（无 WPF 依赖，必须可单测）
src/Wantodo.App/             WPF 宿主、窗口、视图
src/Wantodo.App/Interop/     所有 Win32 P/Invoke 与桌面层逻辑（唯一出口）
tests/Wantodo.Core.Tests/    xUnit
spike/                       一次性探针，验证完可删
docs/                        规格 spec.md / 计划 plan.md / 决策 decisions.md /
                             提示词 prompts.md / 验收 verify/phaseN.md
```

## 硬性规则

1. **P/Invoke 只在 `Interop/`**：视图与 ViewModel 不得直接 `DllImport`。
2. **每次交付必须绿**：`dotnet build -warnaserror` 与 `dotnet test` 全过，输出贴进
   `docs/verify/phaseN.md`。不许为了编译通过而注释掉报错代码。
3. **禁止 `async void`**（UI 事件处理器除外，且内部必须 try/catch 并落日志）。
4. **落盘必须原子**：写 `*.tmp` → `Flush(true)` → `File.Replace`；启动时容忍半截文件并回退
   `*.bak`；再失败则隔离为 `*.corrupt-<ts>.json` 后起空白库。退出前强制落盘一次。
5. **禁止 `AllowsTransparency="True"`**：WPF 分层窗口会关闭硬件加速，常驻小组件不能付这个
   代价。圆角/材质用 DWM（`DwmSetWindowAttribute`），不支持时降级为纯色。
6. **不改系统设置**：注册表 `Run` 项只允许通过设置页的显式开关写入，且开关必须能真正回滚。
7. **Windows 行为不做假设**：WorkerW 拓扑、Win+D、多显示器、DPI 一律先写探针、留日志，再说
   结论。凡是「我记得 API 是这样」的地方，标注待验证并给出你要怎么测。

## 每个阶段的完成定义

- [ ] 代码构建通过、单测通过
- [ ] `docs/verify/phaseN.md` 写明：执行了什么命令、输出要点、哪些项需要人肉眼确认、怎么测
- [ ] 一个 commit，message 以 `phase-N:` 开头
- [ ] 没有达到 `docs/plan.md` 里该阶段的全部验收项 → 不开下一阶段，把卡点写清楚交回
