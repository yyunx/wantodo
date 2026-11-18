# 0.7.0 首次实机启动崩溃（hotfix）

时间：2026-11-18 20:34–20:40
触发：安装 0.7.0 并第一次真正运行（此前 0.7.0 只在 `publish\0.7.0-test` 跑过，被单实例 Mutex 挡在任何写路径之前，等于**从未启动过**）。

## 1. 现象

双击新装的 `Wantodo.exe`（156,293,097 B，v0.7.0.0）后进程消失、无窗口、`E:\Wantodo\logs` 没有新日志、`board/data.json` 未被改写（仍是 `"version": 2`、无 `data.json.pre-070.bak`）——说明异常发生在日志系统与数据迁移之前。

事件日志（`Application`）：

```
Application Error: Wantodo.exe, version 0.7.0.0
Faulting module: KERNELBASE.dll, version 10.0.26100.9711
Exception code: 0xe0434352

.NET Runtime:
Exception Info: System.Windows.Markup.XamlParseException:
  Set property 'System.Windows.ResourceDictionary.DeferrableContent' threw an exception.
 ---> System.ArgumentException: Item has already been added.
      Key in dictionary: 'System.Windows.Controls.Primitives.ScrollBar'
      Key being added:     'System.Windows.Controls.Primitives.ScrollBar'
   at System.Windows.ResourceDictionary.SetKeys(...)
   at Wantodo.App.App.InitializeComponent()
   at Wantodo.App.App.Main()
```

## 2. 根因

`src/Wantodo.App/App.xaml` 里有**两个隐式 `ScrollBar` 样式**：

- 0.6.0 留下的细滚动条（`Width=9`、滑块 `OverlaySoft`、无悬停态），原第 146–201 行；
- P2.1 按 `plan §3.3` 新写的滚动条（`Width=10`、滑块 `SurfaceCard`、悬停/拖动 `AccentSoft`、`MinHeight=24`），第 394 行起。

隐式样式没有 `x:Key`，键就是 `TargetType`；同一个 `ResourceDictionary` 里重复即冲突。WPF 在**编译期不报错**，只在运行期加载 BAML 时抛 `ArgumentException`，于是 `build -warnaserror` 与 437 个单测全绿也照样启动即崩。

为什么之前没发现：0.7.0 的 portable 试运行被单实例 Mutex 直接顶掉，单文件包只做了体积核算，从来没有一次真正走到 `App.InitializeComponent()`。

## 3. 修复

删掉 0.6.0 那份旧 `ScrollBar` 样式（含上方注释），只保留 P2.1 版（新版的 `Track` 同样给全了 `PageUpCommand`/`PageDownCommand`，并补了横向 `IsDirectionReversed=False`，功能是旧版的超集）。

## 4. 回归护栏

`spike/FreeformProbe/Program.cs` 新增第 ⑬ 段 `XamlChecks()`：

- 用 `XDocument` 读 `src/Wantodo.App/App.xaml`（从 `AppContext.BaseDirectory` 向上找 `Wantodo.slnx` 定位仓库根）；
- 断言无重复 `x:Key`；
- 断言无重复隐式样式 `TargetType`（这条正是本次崩溃的形态）。

探针是每步都要跑的验证手段，等于把「运行期才炸的类别」提前到提交前。

## 5. 验证

| 项 | 结果 |
| --- | --- |
| `dotnet build Wantodo.slnx -warnaserror` | 0 警告 0 错误（6.94 s） |
| `dotnet test --no-build` | **437 通过 / 0 失败**（815 ms） |
| `FreeformProbe` | **PROBE PASS**（含新增 4 条 XAML 自检） |
| 安装包 | `publish\install-0.7.0\Wantodo.exe` 156,289,001 B、FileVersion 0.7.0.0，复制到 `C:\Users\ty\AppData\Local\Programs\wantodo\Wantodo.exe` |
| 启动 | pid 22104，主窗口 `HwndWrapper[…]` `rect=494,23,1398,508`、`vis=True`，托盘图标 + 全局快捷键 + 顶边面板均已注册 |
| 首帧 / 内存 | `首帧 1294ms；WS=143MB private=109MB；线程=14` |
| 数据迁移 | `board/data.json` 升到 `"version": 3`，`paletteId=light`（Sakura→light 映射）、`themeId=liquidGlass`，4 栏 11 条全部保留 |
| 备份 | `board/data.json.pre-070.bak` 6,060 B（迁移前原件）、`canvas.json` 改名 `canvas.imported-20261118-203801.json` |
| 孤儿资产清理 | `trash/assets-20261118-203801/` 收到 `c1.png`(245,430 B)、`c2.png`(68,571 B)，两条日志 1 行 `[资产清理] 孤儿文件 2 个（314001 B）已挪进回收目录` |

数据安全：迁移前已把 `E:\Wantodo\board` 全量备份到 `E:\Wantodo-backup-20261118-203252`（13 文件、351,428 B）。

## 6. 遗留

- 崩溃残留进程 `pid 26908` 杀不掉（`Stop-Process` → Access is denied；`Win32_Process.Terminate` → ReturnValue 2），但它不持有单实例 Mutex，新实例能正常启动。重启系统会自然消失。
- 安装目录保留两份旧 exe 备份：`Wantodo-0.6.0-20261118-203442.exe.bak`（156,181,993 B）。
- 本节顺带确认：0.7.0 的 FDD/SC 包体积核算（`p2.2.md`）与首次真实启动无关；两条红线仍超，结论不变。
