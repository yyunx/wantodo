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
| P2.1 | 颜色键 + 右键菜单 + 滚动条 + 空状态 | TODO | — |
| P2.2 | 笔迹静态化 + 性能 + 包体积 | TODO | — |
| P2.3 | release-0.7.0 + README + 两个包 + 总结 | TODO | — |

## 下一步

P2.1 打磨（控件 × 令牌矩阵 23 行逐行有实现或写明未做）：

1. 颜色键：设置页主入口 + 画板状态行 7 个色点（Q7）；
2. 右键菜单（卡片/画布）、滚动条模板、空状态、`＋` 预留行 3 项（日历/参考文件/图片库，标「未实现」，点击不崩）；
3. 面板 `TopPanelWindow` 启用材质（D16：`GlassSurface.Prepare/TryApply` + 内容层 α 两道），观感项留人眼。

先读 `docs/plan-0.7.0.md` §3.3（控件 × 令牌矩阵）与 §5.4（预留行）、`src/Wantodo.App/App.xaml` 现有样式、
`src/Wantodo.App/Windows/TopPanelWindow.xaml` 的三层工具栏。

未偿清单（收口时必须交代）：
- 面板 `TopPanelWindow` 未启用材质（D16，P2.1 做）
- 笔迹静态化与 500 笔阈值未做（P2.2 / V-12）
- 笔迹层是新增渲染层，P2.2 必须复测首帧与内存
- 删除图元不删 `.isf` 与图片资产，会有孤儿文件（D23 / D27，P2.2 清理）
- Win10 Acrylic 降级只有代码路径与 WARN，无真机（A4）
- 图片资产总量 200 MB 提示只有逻辑层测试，未用真实大目录验（P1.3）
- 待办投影每秒重建行（D30），超过 50 条走截断，未做虚拟化（P2.2 视性能再定）

## 下一步第一条命令

```
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"; & "$env:DOTNET_ROOT\dotnet.exe" build Wantodo.slnx -warnaserror
```
