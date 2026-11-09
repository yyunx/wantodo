# Phase 5 验收证据 — 打包、单实例、安装脚本

- 日期：2026-11-10
- 环境：Windows 11 26200，单屏 2560×1600 @150%，.NET SDK 10.0.403（用户目录安装）
- 结论：**通过**（三条验收全部实测；「双击」用 `Start-Process` 等价替代，见待人工确认）

## 1. 本阶段交付物

| 文件 | 作用 |
|---|---|
| `install.ps1` | 复制到 `%LOCALAPPDATA%\Programs\wantodo`，建开始菜单快捷方式（可选桌面、可选自启），不需要管理员 |
| `uninstall.ps1` | 停进程、删快捷方式、清 `HKCU\...\Run`、删程序目录；**默认保留** `%APPDATA%\wantodo`，加 `-RemoveData` 才删 |
| `src/Wantodo.App/Wantodo.App.csproj` | 版本 0.4.0、`AssemblyName=Wantodo`、产品名/描述/公司、`IncludeSourceRevisionInInformationalVersion=false` |
| `src/Wantodo.App/App.xaml.cs` | 单实例 `Mutex(@"Local\Wantodo.SingleInstance")`；第二实例 `FindWindow(null,"wantodo")` + `PostMessage(ShowWantodoMessage)` 后自己退出 |
| `src/Wantodo.App/Interop/NativeMethods.cs` | 追加 `FindWindow` / `PostMessage` / `SetForegroundWindow` / `ShowWantodoMessage`（`RegisterWindowMessage("Wantodo.Show")`）/ `HWND_TOPMOST` / `HWND_NOTOPMOST` |
| `src/Wantodo.App/MainWindow.xaml.cs` | `WndProc` 处理 `ShowWantodoMessage` → `BringToFront()`：临时置顶抢前台，再撤掉置顶落回桌面层 |

## 2. 两种发布模式的体积

| 模式 | 产物 | 体积 | 说明 |
|---|---|---|---|
| framework-dependent | `publish/fdd/` 7 个文件，`Wantodo.exe` 524 KB | **1.37 MB** | 依赖机器上有 .NET 10 Desktop Runtime |
| self-contained 单文件 | `publish/sc/Wantodo.exe` | **148.74 MB** | 不依赖任何运行时；WPF 无法裁剪（trimming 不支持 WPF） |

发布命令与耗时：`dotnet publish -c Release -r win-x64` → FDD 约 7s；加 `--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` → 约 11–23s。

## 3. 启动耗时（看 `app.log` 的「首帧」）

| 场景 | 首帧 | 备注 |
|---|---|---|
| Debug 构建 | 922–1241 ms | 开发时 |
| FDD（带 `DOTNET_ROOT`） | 983 ms | 运行时在用户目录，必须带环境变量才起得来 |
| SC 单文件，干净临时目录 | 968 ms | 只有 `Wantodo.exe`，无运行时环境变量 |
| 安装后的 `%LOCALAPPDATA%` 版本 | 926 / 972 / 975 ms | 多次启动 |
| 外层用日志轮询观测 | 1653 ms | 含轮询间隔（100ms×n）与日志落盘，偏保守 |

内存（安装版）：`WS=132MB private=76MB 线程=14`，与 Debug 版一致。

## 4. 验收项与实测证据

| 验收项 | 结论 | 证据 |
|---|---|---|
| 干净目录双击能启动，不报缺运行时 | ✅（SC） | 把 `publish/sc/Wantodo.exe` 单独拷到 `%TEMP%\wantodo-sc-test\`，清空 `DOTNET_ROOT` 后启动：进程存活，日志 `首帧 968ms` |
| — 同一条件下 FDD 的表现 | ⚠️ 如实记录 | `publish/fdd` 拷到干净目录、不带运行时环境 → 弹窗 `You must install .NET to run this application.`，`App host version: 10.0.14`、`.NET location: Not found`。**所以对外分发只用 SC 版**；FDD 版只用来对照体积 |
| 报告两种发布模式的 exe 体积与启动耗时 | ✅ | 见上面第 2、3 节 |
| 重复双击不会开出两个窗口，只把已有窗口叫到前面 | ✅ | 连开两次：进程数始终 1；日志出现 `第二次启动：已把已有窗口叫到前面` |
| 安装脚本可用 | ✅ | `install.ps1` 输出：复制 152,307 KB 到 `%LOCALAPPDATA%\Programs\wantodo`、创建 `...\Start Menu\Programs\wantodo.lnk`；快捷方式 `TargetPath` 与 `WorkingDirectory` 都指向安装目录；从安装目录直接启动成功（`首帧 922ms`） |
| 卸载脚本可用且不误删数据 | ✅ | `uninstall.ps1` 后：程序目录不存在、快捷方式不存在、进程 0 个、`HKCU\...\Run` 无 `Wantodo` 值，而 `%APPDATA%\wantodo` 仍然存在（data.json 还在） |
| 重装可用 | ✅ | 卸载后再跑 `install.ps1`，安装目录版本 `0.4.0`，启动后单实例仍生效 |

截图：`phase5-installed.png`（安装版贴在桌面上的样子）、`phase5-desktop.png`（整屏）。

## 5. 踩到的坑

1. **`$PSScriptRoot` 在 `-File` 启动方式下为空**：参数默认值里直接 `Join-Path $PSScriptRoot ...` 报 `Cannot bind argument to parameter 'Path' because it is an empty string`。改成参数默认空串，进函数体后用 `$PSScriptRoot`，为空则 `Split-Path -Parent $MyInvocation.MyCommand.Path` 兜底。
2. **PowerShell 5.1 读无 BOM 的 UTF-8 会乱码**：两个脚本改成带 BOM 写出，注释里的中文才正常。
3. **`ProductVersion` 自动拼 git hash**（`0.4.0+77357d3...`）：加 `IncludeSourceRevisionInInformationalVersion=false`。
4. **148 MB 的单文件复制安装**：`install.ps1` 里必须先结束正在运行的进程，否则 `Copy-Item` 会撞上文件占用；脚本已加「先 `CloseMainWindow`、500ms 后还在就 `Kill`」。
5. **桌面层窗口不能直接 `SetForegroundWindow`**：第二次启动的 `BringToFront()` 先 `SetWindowPos(HWND_TOPMOST)` 抢前台，再 `HWND_NOTOPMOST` 撤掉，窗口仍归 Progman 所有，落回桌面层。

## 6. 已知限制

- SC 单文件 148 MB，首次启动会先解压到 `%TEMP%\.net\Wantodo\...`（`IncludeNativeLibrariesForSelfExtract`），第二次起复用，冷启动略慢。
- `install.ps1` 只装当前用户（`%LOCALAPPDATA%`）；想装到 `Program Files` 需要管理员和另一个脚本，本版不做。
- 快捷方式用的是 WScript.Shell 的 `.lnk`，没有做卸载登记（控制面板「应用」里看不到），要用 `uninstall.ps1` 卸。
- 代码没有签名，SmartScreen 首次运行会拦一下（「更多信息 → 仍要运行」）。

## 7. 复跑命令

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
cd E:\Dev\Project\wantodo

# 两种发布
dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained false -o publish\fdd
dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\sc

# 安装 / 卸载
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1          # 保留数据
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1 -RemoveData  # 连数据一起删
```

## 8. 待人工确认

1. **从资源管理器双击**安装目录里的 `Wantodo.exe`、以及开始菜单的 `wantodo`（我用 `Start-Process` 等价验证过，但没真的动鼠标双击）。
2. **首次运行 SmartScreen**：如果弹「Windows 已保护你的电脑」，点「更多信息 → 仍要运行」。
3. **注销重进 + 开机自启**：设置里打开自启 → 注销 → 重新登录，确认自动出现且仍贴在桌面层。
4. **把快捷方式固定到任务栏/开始菜单**：观感是否满意（图标只有 16/24/32… 多尺寸，没有做圆角遮罩适配）。
