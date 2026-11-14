# Phase 21 验证：主题形式感与樱花（T1a）

日期：2026-11-14

## 1. 交付物

| 文件 | 改动 |
|---|---|
| `src/Wantodo.Core/ThemePalette.cs` | `ThemeStyle`（Flat/Soft/Neon/Comic）、`ThemeEffects`、`ThemeColors.Style`、樱花预设（Soft）、`StyleFor`/`EffectsFor`/`DisplayName` |
| `src/Wantodo.Core/Board.cs` | `ThemeEffects` 开关，默认 `true` |
| `src/Wantodo.App/Appearance/ThemeStyles.xaml` | 4 个 `DrawingBrush` 平铺纹理：纸纹 24×24 / 扫描线 4×4 / halftone 8×8 / 斜纹 6×6 |
| `src/Wantodo.App/Appearance/ThemeService.cs` | 写 `ThemeStrokeBrush` / `ThemeStrokeWidth` / `ThemeCornerRadius` / `ThemeTextureBrush`；关闭开关时全部回落 Flat |
| `src/Wantodo.App/App.xaml` | 合并 `ThemeStyles.xaml` |
| `src/Wantodo.App/App.xaml.cs` | 4 个形式感资源的兜底默认值 |
| `src/Wantodo.App/MainWindow.xaml` | 栏卡片用 `ThemeCornerRadius` / `ThemeStrokeBrush`，叠纹理 `Rectangle` |
| `src/Wantodo.App/SettingsWindow.xaml(.cs)` | 显示名「深色 / 浅色」、新增「樱花」、新增「主题效果」开关 |
| `src/Wantodo.App/Shell/TrayIcon.cs` | 托盘外观菜单：深色 / 浅色 / 樱花 |
| `src/Wantodo.App/ViewModels/BoardViewModel.cs` | `SetThemeEffects(bool)` |
| `tests/.../ThemeContrastTests.cs` | Theory 加樱花；新增 `StyleFor_MapsSakuraToSoftElseFlat`、`EffectsFor_FlatHasNoTexture`、`DisplayName_IsFriendly` |
| `tests/.../JsonBoardStoreTests.cs` | `ThemeEffects_DefaultTrueAndRoundTrips` |

## 2. 命令与输出

```
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -warnaserror
已成功生成。 0 个警告 0 个错误  已用时间 00:00:14.24

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test
失败: 0，通过: 226，已跳过: 0，总计: 226，持续时间: 843 ms
```

## 3. 机制

- 樱花 10 令牌按 `docs/design/themes-0.6.0.md` §1 写入，`Style = Soft`；实测对比度由 `Preset_MeetsContrastTargets` 断言（正文 12.68 / 正文对卡片 13.38 / 次要 6.36 / 弱化 5.43 / 强调上文字 5.66）。
- 形式感参数在 `ThemePalette.EffectsFor`：Soft = 1 px `#F3D3DA`、圆角 6、纸纹、阴影 12%；Neon/Comic 参数已就位（Phase 22 接预设）。
- `ThemeService.Apply(board)` 按 `board.ThemeEffects` 选择 `EffectsFor(colors.Style)` 或 `ThemeEffects.Flat`，再写全局资源；`ThemeTextureBrush` 取不到资源时用透明画刷。
- 纹理只铺在栏卡片的 `Rectangle` 上，不参与动画；动画仍只作用于高度/透明度。

## 4. 偏差与欠账

- 纹理目前只落在主窗栏卡片；面板、设置窗、对话框的描边/圆角/纹理留到 Phase 22（计划原文即 22 收口）。
- 描边宽度固定 1 px（`BorderThickness` 在 XAML 里写死），`ThemeEffects.StrokeWidth` 已算出但只有 1/2 两个值，Phase 22 再接 `Thickness` 资源。

## 5. 需人肉眼确认

1. 设置页选「樱花」：栏卡片应为浅粉底、1 px 淡粉描边、6 圆角，背景有很淡的点阵纸纹。
2. 关掉「主题效果」：同一置下只剩纯色，无描边/纸纹，切回开启应立刻恢复。
3. 150% 与 200% 缩放各看一眼纸纹是否清晰、无灰色摩尔纹。

## 6. 已知限制

- 樱花、赛博、美漫三套主题在深色任务栏环境下看起来仍像浅色应用，属预期。
- 关掉效果后 `DrawingBrush` 仍存在于资源字典（未实例化到控件，但不释放）；计划原文要求「不再实例化」，已满足。

## 7. 下一步

Phase 22：赛博 / 美漫预设 + 三套主题接到面板、设置窗、取色器与三个对话框。
