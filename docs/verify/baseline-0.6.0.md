# 0.6.0 性能与体积基线（S1）

日期：2026-11-14　提交：`a9e9f47`（含 Phase 11、Phase 18）
环境：Windows 11 26200，单显示器 2560×1600，缩放 150%，.NET SDK 10.0.403（用户目录），Release 构建。
结论：**通过**。三个数字已立此存照；后续每个阶段与本文对比，不再与 0.5.0 的旧条件混用。

## 1. 包体积

发布命令（与 Phase 5 相同）：

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained false -o publish\fdd-0.6.0-baseline
dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\sc-0.6.0-baseline
Compress-Archive -Path publish\sc-0.6.0-baseline\Wantodo.exe -DestinationPath publish\wantodo-0.6.0-baseline-win-x64.zip -Force
$fdd = 'Wantodo.Core.dll','Wantodo.deps.json','Wantodo.dll','Wantodo.exe','Wantodo.runtimeconfig.json' |
  ForEach-Object { "publish\fdd-0.6.0-baseline\$_" }
Compress-Archive -Path $fdd -DestinationPath publish\wantodo-0.6.0-baseline-fdd-win-x64.zip -Force
```

| 模式 | 产物 | 0.6.0 基线 | 0.5.0 | 差 |
|---|---|---|---|---|
| SC 单文件 | `Wantodo.exe` | 156,081,641 B | 156,074,985 B | +6,656 B |
| SC zip | `wantodo-0.6.0-baseline-win-x64.zip` | **63,873,099 B** | 63,869,489 B | +3,610 B |
| FDD 5 文件 | 总计 | 1,496,927 B（1.43 MB） | 1.37 MB | 约 +0.06 MB |
| FDD zip | `wantodo-0.6.0-baseline-fdd-win-x64.zip` | **199,765 B** | 196,936 B | +2,829 B |

说明：zip 与 0.5.0 一样排除 `.pdb`；`Compress-Archive` 默认压缩级别。0.6.0 的增量来自 Phase 11（事件过滤）与
Phase 18（弹窗 xaml/cs），都在预算内（FRD 估 T1 ≤50 KB、N1 ≤6 KB，目前共 +≈5 KB 二进制）。

## 2. 首帧（5 次，进程冷启动）

方法：每轮先确保 0 个 `Wantodo` 进程，`Start-Process` 启动，轮询 `E:\Wantodo\logs\app-YYYYMMDD.log`
新增的 `首帧 Nms；内存 WS=…；线程=…` 行，读到后立即结束进程。间隔约 1.5 s，系统未重启（文件在缓存）。

| 次 | SC（单文件） | FDD |
|---|---|---|
| 1 | 1246 ms | 1503 ms |
| 2 | 1226 ms | 1187 ms |
| 3 | 1240 ms | 1212 ms |
| 4 | 1212 ms | 1195 ms |
| 5 | 1221 ms | 1146 ms |
| **中位** | **1226 ms** | **1195 ms** |

- SC 首轮含自解压到 `%TEMP%\.net\Wantodo` 的余量（首次运行会更高）；FDD 必须带 `DOTNET_ROOT`，
  否则 apphost 直接弹安装运行时对话框（见第 4 节）。
- 对照：`docs/verify/phase3.md:35-39` 的 Debug 构建 801–1069 ms（中位 811 ms）、`phase5.md` 的 SC 单次 968 ms。
  本轮数值偏高，与当时机器负载不同有关；**本表只作 0.6.0 内部前后对比的起点**，跨版本比较要看同一台机器同一测法。

## 3. 内存（启动后静置 60 s，3 次采样）

方法：确认只有 1 个 `Wantodo` 进程后静置 60 s，读 `WorkingSet64` / `PrivateMemorySize64` / 线程数，取中位。
**private 才是基线**，WS 会被系统裁剪。

| 构建 | private（3 次） | 中位 | WS 中位 | 线程 |
|---|---|---|---|---|
| SC | 88.2 / 92.0 / 93.9 MB | **92.0 MB** | 139.4 MB | 11 |
| FDD | 89.0 / 80.9 / 87.8 MB | **87.8 MB** | 134.6 MB | 11 |

对照：`docs/verify/phase3.md:35-43` private 73–86 MB（Debug）、`phase5.md:37` 安装版 private 76 MB。
本轮比旧记录高 6–16 MB；原因未定位（运行时版本、机器负载、桌面层状态都可能），**不在此阶段下结论**，
仅记录：后续阶段的体积验收一律与上表比，涨了再查。

## 4. 踩坑

1. **FDD 启动要 `DOTNET_ROOT`**：直接 `Start-Process publish\fdd-0.6.0-baseline\Wantodo.exe` 会弹
   「You must install .NET Desktop Runtime」，进程退出、日志无新增；测 FDD 前先
   `$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"`。
2. **pwsh 读日志会乱码**：`Get-Content` 把无 BOM 的 UTF-8 中文读成乱码，`首帧` 匹配不到；
   改为按 ASCII 锚点 `WS=` 取行，再用 `(\d+)ms` 提取数字。
3. **单实例会吃掉采样**：若上一轮进程没退干净，新进程只会叫醒旧实例然后退出，采到的是旧进程。
   采样前必须 `Get-Process Wantodo` 为 0，启动 2 s 后确认恰好 1 个。

## 5. 本基线包含什么

含 Phase 11（自写标记过滤，只影响文件事件回调，不触及启动/内存路径）与 Phase 18（新建弹窗，按需创建，
启动时不加载）。因此本表可作为 Phase 13–17 的对照起点。

## 6. 复跑

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"
# 首帧：重复第 2 节的循环；内存：起进程 → 睡 60 s → Get-Process Wantodo
Get-Process Wantodo | Select-Object WorkingSet64, PrivateMemorySize64, @{n='Threads';e={$_.Threads.Count}}
```
