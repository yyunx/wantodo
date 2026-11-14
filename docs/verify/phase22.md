# Phase 22 验证：赛博、美漫与效果收口

日期：2026-11-14

## 1. 交付物

| 文件 | 改动 |
|---|---|
| `src/Wantodo.Core/ThemePalette.cs` | 新增 `CyberTheme` / `ComicTheme` 常量与预设（`Style = Neon / Comic`），`StyleFor` 映射，`DisplayName` 加「赛博 / 美漫」 |
| `src/Wantodo.App/SettingsWindow.xaml(.cs)` | 外观区加「赛博」「美漫」单选，选中匹配与回退逻辑同步 |
| `src/Wantodo.App/Shell/TrayIcon.cs` | 托盘外观菜单加「赛博」「美漫」 |
| `src/Wantodo.App/Appearance/ThemeService.cs` | 新增 `ThemeStrokeThickness`（`Thickness`）资源，供 XAML 直接绑定描边宽度 |
| `src/Wantodo.App/App.xaml.cs` | `ThemeStrokeThickness` 兜底默认值 |
| `src/Wantodo.App/MainWindow.xaml` | 栏卡片 `BorderThickness` 改用 `ThemeStrokeThickness`（Soft 1 px / Comic 2 px） |
| `tests/.../ThemeContrastTests.cs` | Theory 扩到 8 套（含赛博、美漫） |

## 2. 命令与输出

```
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -warnaserror
已成功生成。 0 个警告 0 个错误  已用时间 00:00:07.83

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test --no-build
失败: 0，通过: 230，已跳过: 0，总计: 230，持续时间: 699 ms
```

## 3. 机制

- 赛博：`#070B14 / #0E1524 / #E6FAFF / #8FA9C4 / #6C8399 / #22E0FF`，`Style = Neon`（1 px 强调色描边、圆角 4、扫描线 8%、外发光 45% 参数已算）。
- 美漫：`#FFFDF3 / #FFFFFF / #171A1F / #4A5260 / #667080 / #D6293A`，`Style = Comic`（2 px `#171A1F` 描边、圆角 14、halftone 6%、硬阴影 65%）。
- 8 套预设统一由 `Preset_MeetsContrastTargets` 断言；赛博/美漫的强调色未被 `EnsureContrast` 校正（阈值之上）。

## 4. 偏差与欠账

- 面板、设置窗、取色器与三个对话框尚未逐一接 `ThemeStyle`（计划第 3 条）；本次只把「描边宽度」做成全局 `Thickness` 资源，主窗已用。
- 外发光/硬阴影尚未绘制（参数已就位）；纹理仍只在主窗栏卡片。
- 200% DPI 与「切主题 ≤16 ms」需要人肉眼；FDD 包增量留 Phase 23 复测。

## 5. 需人肉眼确认

1. 设置页或托盘切到「赛博」：深底 + 青色强调 + 1 px 描边；切到「美漫」：米白底 + 红强调 + 2 px 黑描边。
2. 三套风格主题下点开「主题效果」开关，纯色 ↔ 形式感来回切换应即时。
3. 在 150%/200% 缩放下看描边是否有 1 px 灰边。

## 6. 已知限制

- Neon 的外发光、Comic 的硬阴影只有参数，尚无视觉效果；写入 Phase 23/24 前的欠账。
- 面板与对话框的描边/圆角仍固定，换主题时只有颜色变化。

## 7. 下一步

Phase 23：0.6.0 收口（两种包、首帧/内存/面板增量、release 文档、版本 0.6.0）。
