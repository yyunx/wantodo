# 0.7.0 开工基线（2026-11-15）

分支：`feat/0.7.0`，起点 `0ba7102 chore: 0.7.0 前清理工作树`（其上为 `3cb380f`）。

## 构建与测试

```
dotnet build Wantodo.slnx -warnaserror
已成功生成。 0 个警告 0 个错误   (14.34 s)

dotnet test --no-build
已通过! - 失败: 0，通过: 317，已跳过: 0，总计: 317  (602 ms)
```

## 性能与体积基线（沿用 0.6.0 验收数据）

| 指标 | 基线 |
|---|---|
| 首帧 | 1098 ms |
| private working set | 102.1 MB |
| FDD payload | 1,616,223 B |
| FDD zip | 247,796 B |
| 红线（0.6.0 起沿用） | 229,376 B（已超 18,420 B，P2 还债） |

## 数据快照（只读，A1）

- 源：`E:\Wantodo\board\`（`data.json`、`canvas.json`、`canvas-images/`、`trash/` 等 13 项）
- 副本：`%TEMP%\wantodo-070\sample\board\`
- `data.json` 基线：Length 4834 B，LastWriteTime 2026-11-15 22:03:50（收口时复核未被改动）

## portable 副本（今晚唯一允许运行 App 的地方）

```
dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained false -o publish\0.7.0-test
Wantodo.exe 536,064 B / Wantodo.dll 969,216 B / Wantodo.Core.dll 112,128 B
publish\0.7.0-test\portable.txt  (22 B)
```

数据根 = `publish\0.7.0-test\Wantodo\`（`DataRootResolver` 的 `PortableMarker` 分支）。
`publish/` 被 `.gitignore:11` 忽略，不进提交。
