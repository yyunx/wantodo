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
| P1.2 | 笔迹与图形 + 每表面撤销 | TODO | — |
| P1.3 | 无铬图片 + 剪贴板 + 资产策略 | TODO | — |
| P1.4 | 待办投影 / 拖动改名分离 / 计时状态机 | TODO | — |
| P2.1 | 颜色键 + 右键菜单 + 滚动条 + 空状态 | TODO | — |
| P2.2 | 笔迹静态化 + 性能 + 包体积 | TODO | — |
| P2.3 | release-0.7.0 + README + 两个包 + 总结 | TODO | — |

## 下一步

P1.2 笔迹（`InkCanvas`）+ 矩形/线/箭头/文本创建 + 每表面撤销 50 步（V-13：A 面 3 步 → B 面 2 步 → 回 A 撤 3 次，A 复原、B 不变）。
先读 `docs/plan-0.7.0.md` §4.6（撤销作用域）与 §5.3，再读 `src/Wantodo.App/Controls/CanvasBoard.cs` 里现有的撤销栈实现（`Ctrl+Z/Y` 已能用），把它抽成两个表面共用的一份。

## 下一步第一条命令

```
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"; & "$env:DOTNET_ROOT\dotnet.exe" build Wantodo.slnx -warnaserror
```
