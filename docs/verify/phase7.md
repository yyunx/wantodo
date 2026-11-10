# Phase 7 验收：数据根外置

日期：2026-11-10　目标：业务数据不落 C 盘、位置可配、可迁移、可回滚。

## 交付物

| 位置 | 内容 |
| --- | --- |
| `src/Wantodo.Core/DataPaths.cs` | 数据根下的目录布局（`board` / `config` / `notes` / `logs` / `trash` / `.lock` / `README.txt`）、`EnsureCreated()`、路径规范化、同目录判断 |
| `src/Wantodo.Core/DataRootResolver.cs` | 选根顺序：`portable.txt` → 注册表指针 → 程序目录（非系统盘）→ 非系统固定盘按空间降序 `<盘>:\Wantodo` → 兜底并告警 |
| `src/Wantodo.Core/DataMigrator.cs` | 迁移：`Inspect`（拒绝同目录/互相嵌套/目标非空/空间不足）→ 复制到 `.wantodo-incoming-<ts>` → 校验 → 换名 → 源目录写 `.migrated.json`；失败删暂存、源不动 |
| `src/Wantodo.Core/DataRetention.cs` | 日志留 14 天、`trash` 留 30 天 |
| `src/Wantodo.Core/RootLock.cs` | `.lock` 记录 pid/启动时间/机器/用户；活进程占用时拒绝第二次使用 |
| `src/Wantodo.App/Storage/WindowsDataRootEnvironment.cs` | 注册表 `HKCU\Software\wantodo\DataRoot`（失败降级 `%APPDATA%\wantodo\location.json`）、可写探测、固定盘枚举 |
| `src/Wantodo.App/Storage/DataRootService.cs` | 启动时解析根、建目录、写 README、清陈旧暂存、抢 `.lock`、导入 v0.4 旧数据、清理过期日志 |
| `src/Wantodo.App/SettingsWindow.xaml` | 「数据位置」分组：当前根、可用空间、打开目录 / 迁移到… / 清理旧数据 |
| `tests/Wantodo.Core.Tests/DataRootTests.cs` | 24 个用例（选盘 6、路径 5、迁移 7、保留策略 2、锁 3 + 1 个跳过 `.lock`） |

## 命令与结果

```
dotnet build Wantodo.slnx -warnaserror   → 0 警告 0 错误
dotnet test                              → 通过 108，失败 0（Phase 6 结束时 83）
```

## 实测（本机，C: 24.1 GB / D: 139.5 GB / E: 669.8 GB）

| # | 场景 | 结果 |
| --- | --- | --- |
| 1 | 开发版首次运行（exe 在 E:） | `数据根 E:\...\bin\Debug\net10.0-windows\data（来源 ProgramDirectory）`，目录七件套齐全，`board\data.json` 3134 字节 |
| 2 | v0.4 旧数据自动迁入 | `已把旧数据导入到 …\board\data.json`，旧文件改名 `data.migrated.json`，`status=Loaded columns=3` |
| 3 | 重启 | `来源 Pointer`，不再导入，`columns=3` |
| 4 | 程序放在 C 盘启动 | `数据根 E:\Wantodo（来源 OtherDrive）`，选的是空间最大的非系统盘 |
| 5 | 设置里迁移（UI，迁到空目录） | 成功：目标生成 `board`/`logs`/`README.txt`，点「重启」后新 pid 生效，新根补齐 `config`/`notes`/`trash`/`.lock`，`来源 Pointer`、`columns=3` |
| 6 | 迁移失败（目标选成当前根的子目录） | 弹窗 `迁移没有完成：目标目录不能放在当前目录里面，也不能包含当前目录`；源文件字节数不变、目标下无残留 |
| 7 | 迁移失败（日志正在增长，旧版） | 曾报 `校验失败：字节数 10484 ≠ 期望 10321`；修复后按「复制前长度」逐文件校验，`.log` 允许增长 |
| 8 | `.lock` 语义 | 迁移不复制 `.lock`（属「谁在用这个目录」的凭据），新根启动时重建 |
| 9 | 最终落点 | `E:\Wantodo`（非 C 盘、非开发目录），注册表 `DataRoot=E:\Wantodo`，3 栏数据完好 |

截图：`docs/verify/phase7-window.png`（新数据根下运行，三栏）、`docs/verify/phase7-settings.png`（设置面板「数据位置」）。

## 踩坑

1. **校验拿「迁移前扫描的总字节」比「迁移后文件」**：进程还在写日志，必然不等。改成逐文件记录复制前长度，`.log` 视为易变、只校验存在性，其余严格比字节。
2. **`.lock` 不能跟着搬**：搬过去就成了一条指着旧进程的陈旧锁。迁移时跳过，新根启动重建。
3. **「现在重启吗」只置了标志**：`RequestRestart()` 只设字段，主窗口不关就永远不触发 `OnExit`，用户会以为没反应。改成 Yes 分支 `Close()` + `Application.Current.Shutdown()`，并加 `_dataMigrated` 守卫防止退出时又往旧目录写。
4. **`FolderBrowserDialog` 是传统 SHBrowseForFolder**：UIA 抓不到，只能 `Alt+D` + 地址栏输入 + 坐标点「选择文件夹」（`Right-245, Bottom-38`）；点 `Right-90` 会命中「取消」。
5. **`DataRootService.Environment` 与 `System.Environment` 撞名**：改名 `Host`。

## 已知限制

- `config/settings.json` 暂未拆出：设置仍跟板数据一起放在 `board\data.json`，`DataPaths.SettingsFile` 已留位（拆开要动 schema，等 Phase 8/9 再做）。
- 迁移只搬文件、不搬空目录：`config`/`notes`/`trash` 在新根首次启动时由 `EnsureCreated()` 补建。
- 选中的目录非空时按约定拼一层 `Wantodo`，因此「想迁进某个已存在的空壳目录」需要它真的为空。

## 复跑

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
Get-Process Wantodo -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build Wantodo.slnx -warnaserror
dotnet test
# 看当前数据根
(Get-ItemProperty HKCU:\Software\wantodo).DataRoot
# 复位到默认（删指针，下次按程序目录/盘符规则重选）
Remove-ItemProperty HKCU:\Software\wantodo -Name DataRoot
```

## 待人工确认

1. 真实数据迁移到另一块盘后，重启看三栏内容是否完整。
2. 迁移中途拔盘 / 断电：下次启动应提示根不可用并回退（未实测）。
3. 程序装到 `%LOCALAPPDATA%\Programs\wantodo`（在 C 盘）启动时，是否落到 `E:\Wantodo`（本机已实测，其他机器看盘符）。
4. 卸载是否保留数据（`uninstall.ps1` 默认保留）。
5. `%APPDATA%\wantodo` 里剩下的 `app.migrated`/`data.migrated.json` 是否手动清掉。
