# Phase 23 验证：0.6.0 收口

日期：2026-11-14

## 1. 交付物

| 文件 | 改动 |
|---|---|
| `src/Wantodo.App/Wantodo.App.csproj` | 版本 0.5.1 → 0.6.0 |
| `publish\wantodo-0.6.0-win-x64.zip` | SC 包（仅 `Wantodo.exe`） |
| `publish\wantodo-0.6.0-fdd-win-x64.zip` | FDD 包（5 个文件，排除 pdb） |
| `docs/release-0.6.0.md` | 0.6.0 发布说明 |
| `README.md` | 下载表 0.5.1 → 0.6.0 |

## 2. 命令与原始字节

```
dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained false -o publish\fdd-0.6.0
dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\sc-0.6.0
```

| 项 | 原始字节 | 预算 | 结论 |
|---|---|---|---|
| FDD payload（5 文件） | 1,556,831 B | — | 0.6.0 基线 1,496,927 B，+59,904 |
| FDD zip | **223,415 B** | ≤229,376 B | 通过（+8,552 B vs 0.5.1 的 214,863） |
| SC exe | 156,140,521 B | — | — |
| SC zip | **63,893,046 B** | ≤63,900,000 B | 通过（余 6,954 B） |

设计文档的乐观预算 218,900 B 被超过 4,515 B，但仍在计划红线内。

## 3. 首帧与内存（同一方法：SC 包、冷启动、日志 `首帧` 行 / 60 s 静置采样）

| 项 | 0.6.0 实测 | 0.6.0 基线 | 判定 |
|---|---|---|---|
| 首帧 3 次 | 1316 / 1236 / 1231 ms，中位 **1236** | 1226 ms | +0.8%，≤+10% 通过 |
| private 60 s 3 次 | 100.4 / 100.0 / 99.2 MB，中位 **100.0** | 92.0 MB | **+8.0 MB，超出 +6 MB 预算 2 MB** |

- 内存超预算时面板为启用状态（`ShowTopPanel=true`），基线测量时面板关闭。怀疑面板窗口 + 两个低级钩子 + 前台监听贡献了增量，尚未逐项归因。
- 面板展开增量 private 未测（需要人把面板拉开时采样），记为 PENDING-HUMAN。
- WS 3 次 138.4 / 138.7 / 138.4 MB；线程 11/14/11。

## 4. 验收对照

- [x] FDD zip ≤229,376 B、SC zip ≤63,900,000 B，原始字节已记录。
- [x] 首帧中位 ≤基线 +10%。
- [ ] private ≤基线 +6 MB（实测 +8.0 MB，未达标）。
- [ ] 面板展开 private 增量 ≤6 MB（未测）。
- [x] `build -warnaserror` 0 警告、`test` 230 通过。

## 5. 需人肉眼确认

1. 用 `publish\sc-0.6.0\Wantodo.exe` 启动，打开设置确认版本区、主题列表有「樱花 / 赛博 / 美漫」。
2. 拉开面板并保持展开，任务管理器看 private 增量（记录数值即可）。
3. 关掉「主题效果」后逐窗看是否退回纯色。

## 6. 下一步

- 内存 +8 MB 的归因（面板关闭时复测一份基线）与面板增量测量。
- 之后进入 Phase 24（0.7.0 形态 B），或先处理本阶段欠账。
