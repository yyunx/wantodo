# 0.7.0 执行进度

分支 `feat/0.7.0`（不 push）。每步一份 `pX.Y.md`，本文只放状态与下一步第一条命令。

| 步 | 内容 | 状态 | 文档 |
|---|---|---|---|
| P0.1 | 数据模型 v3 + 迁移 + 旧 canvas 导入 | DONE | [p0.1.md](p0.1.md) |
| P0.2 | Theme/Palette 拆分 + ThemeResourceKeys 闭环 | DONE | [p0.2.md](p0.2.md) |
| P0.3 | 单文件画布：`CanvasStore` 删除，画布并入 `data.json` | DONE | [p0.3.md](p0.3.md) |
| P0.4 | 双表面宿主 + 分段控件（只切 Visibility） | DONE | [p0.4.md](p0.4.md) |
| P0.5 | 液态玻璃探针 + 材质 + Win10 降级 | TODO | — |
| P1.1 | 自由表面工具组 + 缩放 + 平移 | TODO | — |
| P1.2 | 笔迹与图形 + 每表面撤销 | TODO | — |
| P1.3 | 无铬图片 + 剪贴板 + 资产策略 | TODO | — |
| P1.4 | 待办投影 / 拖动改名分离 / 计时状态机 | TODO | — |
| P2.1 | 颜色键 + 右键菜单 + 滚动条 + 空状态 | TODO | — |
| P2.2 | 笔迹静态化 + 性能 + 包体积 | TODO | — |
| P2.3 | release-0.7.0 + README + 两个包 + 总结 | TODO | — |

## 下一步第一条命令

```
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"; & "$env:DOTNET_ROOT\dotnet.exe" build Wantodo.slnx -warnaserror
```
