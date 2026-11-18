# 0.7.0 执行进度

分支 `feat/0.7.0`（不 push）。每步一份 `pX.Y.md`，本文只放状态与下一步第一条命令。

| 步 | 内容 | 状态 | 文档 |
|---|---|---|---|
| P0.1 | 数据模型 v3 + 迁移 + 旧 canvas 导入 | DONE | [p0.1.md](p0.1.md) |
| P0.2 | Theme/Palette 拆分 + ThemeResourceKeys 闭环 | DONE | [p0.2.md](p0.2.md) |
| P0.3 | 单文件画布：`CanvasStore` 删除，画布并入 `data.json` | DONE | [p0.3.md](p0.3.md) |
| P0.4 | 双表面宿主 + 分段控件（只切 Visibility） | DONE | [p0.4.md](p0.4.md) |
| P0.5 | 液态玻璃探针 + 材质 + Win10 降级 | DONE（含 4 项 PENDING-HUMAN） | [p0.5.md](p0.5.md) · [探针](p0.5-probe.md) |
| P1.1 | 自由表面工具组 + 缩放 + 平移 | DONE（含 5 项 PENDING-HUMAN） | [p1.1.md](p1.1.md) · [探针日志](p1.1-probe.log) |
| P1.2 | 笔迹与图形 + 每表面撤销 | DONE（含 5 项 PENDING-HUMAN） | [p1.2.md](p1.2.md) · [探针日志](p1.2-probe.log) |
| P1.3 | 无铬图片 + 剪贴板 + 资产策略 | DONE（含 6 项 PENDING-HUMAN） | [p1.3.md](p1.3.md) · [探针日志](p1.3-probe.log) |
| P1.4 | 待办投影 / 拖动改名分离 / 计时状态机 | DONE（含 6 项 PENDING-HUMAN） | [p1.4.md](p1.4.md) · [探针日志](p1.4-probe.log) |
| P2.1 | 颜色键 + 右键菜单 + 滚动条 + 空状态 | DONE（含 5 项 PENDING-HUMAN） | [p2.1.md](p2.1.md) · [探针日志](p2.1-probe.log) |
| P2.2 | 笔迹静态化 + 性能 + 包体积 | DONE-WITH-DEBT（含 6 项 PENDING-HUMAN + 两条红线均超） | [p2.2.md](p2.2.md) · [探针日志](p2.2-probe.log) |
| P2.3 | release-0.7.0 + README + 两个包 + 总结 | DONE | [p2.3.md](p2.3.md) |

## 状态：12 步全部走完（2026-11-16）

`feat/0.7.0` 上 10 个 commit，未 push。收口数字：`build -warnaserror` 0 警告 0 错误、
`test --no-build` **437 通过 / 0 失败**、探针 **81 条 PASS**。

两条红线都超了，都没改（Q10）：

| 包 | 本次 | 红线 | 超出 |
|---|---|---|---|
| FDD zip | 285,331 B | 229,376 B | 55,955 B |
| SC zip | 63,957,693 B | 63,900,000 B | 57,693 B |

## 下一步

**没有本批的下一步了。** 剩余工作都是需要人（或需要退出真机实例）的：

1. 按 [p2.2.md](p2.2.md) §4 退出正在跑的实例，跑 portable 副本，把首帧 / private 抄进本文件（V-12 的三条数字目前全是 PENDING-HUMAN）；
2. 按 [p2.3.md](p2.3.md) §5 的表逐条肉眼验收并补截图；
3. 决定要不要为包体积开工（候选：拆 WPF 资源字典、删旧链接卡渲染路径、图片资产懒加载），这条要开新计划；
4. 是否 `git push` 由你决定，本批全程未推。

> 关于真实数据：P2.2 收口时核对过一次 `E:\Wantodo\board\data.json`（Length 4834 / mtime 2026-11-15 22:03:50，未变）。
> 2026-11-16 17:21:36 该文件被改写过一次（Length 5292，内容仍是 `"version": 2` / `theme: Sakura`）——
> 那是你自己在用的 0.6.0 实例保存的，不是本批：本批唯一一次启动 App 用的是 `publish\0.7.0-test` 的 portable 副本，
> 且被单实例 Mutex 直接挡下（exit=0，连日志目录都没建），没有任何写路径碰到 `E:\Wantodo\`。


## 未偿清单（收口交代）

- 主窗口未接材质（矩阵 #1，P2.1 写明理由）
- 面板顶栏圆角 10 / 自由表面背景网格 / 计时进度环 / 令牌名取色 / 空状态引导按钮 / 拖拽虚线（矩阵 ◐✗，#4/#9/#12/#15/#21/#22）
- **FDD zip 285,331 B 超红线 55,955 B；SC zip 63,957,693 B 超红线 57,693 B**（Q10：不许改红线，已写进 release notes；本批只做零成本减重 −2,739 B）
- V-12 的三条数字（首帧 ≤1153 ms / private ≤105.1 MB / 50 卡 500 笔 ≥55 fps）全部 PENDING-HUMAN：单实例 Mutex 被真机实例占用，portable 副本启动即退出；复测命令在 `p2.2.md` §4
- 笔迹静态化在 CPU 软件光栅代理下反而更慢（D36），真实合成器收益未验证
- 笔迹层是新增渲染层，首帧与内存未在真机复测
- 删除图元不删 `.isf`/图片 → 由启动时 `AssetCleanup` 挪进 `trash/assets-<ts>/`（D37），`trash/` 会缓慢长大
- Win10 Acrylic 降级只有代码路径与 WARN，无真机（A4）
- 图片资产总量 200 MB 提示只有逻辑层测试，未用真实大目录验（P1.3）
- 待办投影每秒重建行（D30），超过 50 条走截断，未做虚拟化
- 面板材质在真机上的拖拽/命中是否回归，只能人眼验（P2.1 PENDING-HUMAN 第 3 条）
- 所有截图类 PENDING-HUMAN（约 20 张）都还没补，清单在 `p2.3.md` §5

## 复跑命令

```
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
& "$env:DOTNET_ROOT\dotnet.exe" build Wantodo.slnx -warnaserror
& "$env:DOTNET_ROOT\dotnet.exe" test --no-build
& "$env:DOTNET_ROOT\dotnet.exe" spike\FreeformProbe\bin\Debug\net10.0-windows\FreeformProbe.dll
```

## 2026-11-18 装进安装目录并首次实机启动（hotfix）

- `src/Wantodo.App/Wantodo.App.csproj` 的 `<Version>` 由 `0.6.0` 升到 `0.7.0`（0.7.0 批次漏升，安装后仍显示 0.6.0.0）：commit `58946f2`。
- 用自包含单文件发布替换 `C:\Users\ty\AppData\Local\Programs\wantodo\Wantodo.exe`；旧 0.6.0 备份为同目录 `Wantodo-0.6.0-20261118-203442.exe.bak`。开始菜单 `wantodo.lnk` 指向的正是这个路径，目标文件换成 0.7.0 即等于「快捷方式已更新」。
- **第一次启动即崩**：`XamlParseException` → `ArgumentException: Item has already been added. Key: ScrollBar`。根因是 `App.xaml` 里 0.6.0 的旧隐式 `ScrollBar` 样式与 P2.1 新写的隐式样式重复；WPF 编译期不报，运行期加载 BAML 才炸。
- 修复：删旧样式，只留 P2.1 版；探针新增第 ⑬ 段 `XamlChecks()`，断言 `App.xaml` 无重复 `x:Key`、无重复隐式 `TargetType`。细节见 `hotfix-startup-crash.md`。
- 复验：build 0 警告 0 错误、test 437 通过、探针 PROBE PASS；安装后启动 pid 22104、主窗口可见、`首帧 1294ms / private 109MB`；`E:\Wantodo\board\data.json` 升到 `version 3`（`paletteId=light`、4 栏 11 条保留），生成 `data.json.pre-070.bak`，`canvas.json` 改名 `canvas.imported-20261118-203801.json`，孤儿图片 2 个挪进 `trash/assets-20261118-203801/`。
- 迁移前的数据整份备份在 `E:\Wantodo-backup-20261118-203252`（13 文件、351,428 B）。
- 崩溃残留进程 `pid 26908` 杀不掉（Access denied），但不持有单实例 Mutex，不影响使用；重启系统会消失。
- 由此修正一条旧结论：0.7.0 此前**从未真正启动过**（portable 副本被 Mutex 挡在写路径之前），所以 `p2.2.md` 里的首帧/内存数字是空白，本次 1294 ms / 109 MB 是第一次实测，仍不构成 V-12 的 50 卡 500 笔场景。