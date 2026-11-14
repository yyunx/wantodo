# Phase 14 验收记录 — 10 个主题令牌与五套开源预设（T1a）

日期：2026-11-14
结论：**自动项通过**（构建 0 警告、测试 186 通过）；两条肉眼项待用户在带修复的 Debug 构建上确认（第 6 节）。未勾的两条见 `docs/plan.md:583-584`。

## 1. 交付物

| 位置 | 内容 |
| --- | --- |
| `src/Wantodo.Core/ThemePalette.cs`（244 行） | `Rgb`；`ThemeColors` 10 字段；5 套预设 Base；`For` 两个重载；`ForCustom`；`ApplyTone`；`SupportsTone`；`IsLightColors`。预设构造时用 `EnsureContrast` 校正文字三档与 `OnAccent` |
| `src/Wantodo.Core/ThemeDerivation.cs`（127 行） | `RelativeLuminance`、`Contrast`、`EnsureContrast`（步长 0.04、上限 25 步）、`IsLightBackground(0.35)`、`DeriveFrom`、`PickOnAccent`；Phase 15 复用 |
| `src/Wantodo.App/Appearance/SystemTheme.cs` | 读一次 `HKCU\...\Themes\Personalize\AppsUseLightTheme`，失败按深色并记 WARN |
| `src/Wantodo.App/Appearance/ThemeService.cs` | `Apply(Board)` 拼 `SystemTheme`；`Apply(ThemeColors)` 写 10 个动态资源 |
| `src/Wantodo.App/App.xaml:11-20` | 画刷 7 → 10：新增 `OnAccent`、`Hover`、`Selected`；`IconButton`/`TitleBarButton` 的 hover 改 `{DynamicResource Hover}`（:36、:65） |
| `src/Wantodo.App/SettingsWindow.xaml` | 外观区 6 个 radio（跟随系统 + 5 套预设），`WrapPanel` 两行 |
| `src/Wantodo.App/SettingsWindow.xaml.cs` | `LoadCurrentValues` 按 `Tag` 选中（空值视为 `Dark`）；`OnThemeChanged` 走 `SetTheme` 并控制色调滑杆可用性 |
| `src/Wantodo.App/Shell/TrayIcon.cs` | 「外观」子菜单换成同一组 6 项，按 `Model.Theme` 置勾选 |
| `tests/Wantodo.Core.Tests/ThemeContrastTests.cs` | 19 例：5 套阈值、Hover/Selected 互异、大小写、固定色板忽略 Tone、System 跟随、ForCustom、非法回退、EnsureContrast、Contrast 边界 |
| `tests/Wantodo.Core.Tests/ThemePaletteTests.cs` | 两条旧断言改为 GitHub 色值（`#0D1117`、`#F6F8FA`） |

## 2. 命令与输出

```
dotnet build -warnaserror
  Wantodo.Core -> ...\bin\Debug\net10.0\Wantodo.Core.dll
  Wantodo.App  -> ...\bin\Debug\net10.0-windows\Wantodo.dll
  已成功生成。 0 个警告 0 个错误（3.08 秒）

dotnet test --no-build
  已通过! - 失败: 0，通过: 186，已跳过: 0，总计: 186，持续时间: 560 ms
```

（Phase 12 结束时是 167 例，本阶段新增 19 例。）

## 3. 五套预设与实测对比度

色板来源：Dracula 官方 README、Nord 官方文档、Catppuccin v1.8.0 `palette.json`、GitHub Primer 公开页面
（GitHub 深色为公开已知值，未逐 token 核对 base 常量；差异只会影响观感，不影响阈值）。

| 预设 | 背景 | 卡片 | 正文 | 次要 | 弱化 | 强调 | 强调上文字 | 正文 / 正文对卡片 / 次要 / 弱化对卡片 / 强调上文字 |
|---|---|---|---|---|---|---|---|---|
| GitHub 深色 `Dark` | `#0D1117` | `#161B22` | `#E6EDF3` | `#B1BAC4` | `#7D8590` | `#2F81F7` | `#0D1117` | 16.02 / 14.64 / 9.63 / 4.64 / 5.05 |
| GitHub 浅色 `Light` | `#FFFFFF` | `#F6F8FA` | `#1F2328` | `#4D5560` | `#59636E` | `#0969DA` | `#FFFFFF` | 15.80 / 14.84 / 7.54 / 5.74 / 5.19 |
| Dracula | `#282A36` | `#21222C` | `#F8F8F2` | `#D0D2E4` | `#8894BA` | `#BD93F9` | `#282A36` | 13.36 / 14.81 / 9.51 / 5.26 / 5.90 |
| Nord | `#2E3440` | `#3B4252` | `#ECEFF4` | `#D8DEE9` | `#9AB4CD` | `#88C0D0` | `#2E3440` | 10.84 / 8.73 / 9.25 / 4.69 / 6.24 |
| Catppuccin Mocha `Catppuccin` | `#1E1E2E` | `#313244` | `#CDD6F4` | `#BAC2DE` | `#A6ADC8` | `#89B4FA` | `#1E1E2E` | 11.34 / 8.69 / 9.26 / 5.65 / 7.79 |

两处弱化色被自动校正：Dracula `#6272A4` → `#8894BA`（4.0:1 → 5.26:1）、Nord `#81A1C1` → `#9AB4CD`（4.0:1 → 4.69:1）。
数值来自一次临时 `Console.WriteLine` 测试（跑完即删），不是手算。

## 4. 为什么能保证「换主题不出现白底白字」

1. 令牌化：`App.xaml` 只有 10 个 `SolidColorBrush`，视图全部 `DynamicResource`；`ThemeService` 换的是这 10 个键，窗口不重建（`App.xaml:6-9` 的既有约定）。
2. 预设不手写「看起来能读」的值：`Build` 里对 `TextPrimary`/`TextSecondary`/`TextMuted` 分别对**窗口底与卡片底**跑 `EnsureContrast`，`OnAccent` 对强调色跑一次；不达标就往黑/白推，而不是留给人眼赌。
3. `System` 只在启动时读一次系统偏好，结果映射到 GitHub 浅/深色；不订阅 `UserPreferenceChanged`，避免常驻进程多一根钩子。
4. 固定色板忽略色调滑杆（`SupportsTone`），设置页把滑杆置灰——否则 `ApplyTone` 会把 Dracula/Nord 的背景改成不认识的色。

## 5. 自动能验证的部分

| # | 做了什么 | 结果 |
| --- | --- | --- |
| 1 | `dotnet build -warnaserror` | 0 警告 0 错误（3.08s） |
| 2 | `dotnet test --no-build` | 186 通过 / 0 失败（560ms） |
| 3 | 5 套预设逐条断言 5 个阈值 | 全过（`ThemeContrastTests`） |
| 4 | 0.5.0 的 `Theme=Dark/Light` 读入 | 映射到 GitHub 深/浅色，不抛错 |
| 5 | 未知主题值（如 `Deep`） | 回退 GitHub 深色 |

## 6. 需人肉眼确认（未勾）

### 6.1 切换 5 套主题即时生效、不重建窗口

1. 用带本次改动的 Debug 构建启动（`$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"` 后双击 `src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.exe`）。
2. 打开设置页，依次点「GitHub 深色 → GitHub 浅色 → Dracula → Nord → Catppuccin Mocha」。
3. 期望：每点一次，主窗口、设置页、托盘菜单立即换色；**任务栏不闪、窗口不重新出现**；日志（`<数据根>\logs\`）没有新窗口创建记录。
4. 失败长什么样：点完要关掉设置页才变色；或主窗口闪一下/回到屏幕中央。

### 6.2 深色与浅色下悬停底色可见

1. 切到「GitHub 深色」，鼠标移到主窗口右上角任一图标按钮上，停 1 秒。
2. 切到「GitHub 浅色」，重复同一动作。
3. 期望：两次都能看出按钮底色变化（浅色下不再是白叠白）。
4. 失败长什么样：浅色下按钮 hover 完全看不出来（0.5.0 的缺陷仍在）。

截图：待用户确认时补，或改为口头确认后删掉本条截图要求（`docs/plan.md:584` 仍写着「截图」）。

## 7. 已知限制与偏差

- **偏差（设置页形态）**：计划写「5 项预设 + 跟随系统开关」，实际是 6 个 radio（跟随系统 + 5 套）。枚举与行为一致，少一个独立控件；未改 `docs/requirements/frd-0.6.0.md` 的交互步骤字数。
- 「跟随系统」不跟随实时变化：系统切深/浅后要重启或重选一次。`docs/decisions.md:373` 的未决项仍开着。
- GitHub 深色值取自公开配色，未逐 token 核对 Primer 的 base 常量；若日后发现偏差，以观感为准，不动阈值。
- 阈值测的是不透明前景/背景对；`Hover`/`Selected` 是带 alpha 的叠色，不参与比值断言。
- 樱花/赛博/美漫从 0.6.0 移入未排期（触发条件在 `docs/plan.md` 未排期区），不是被否掉。

## 8. 下一步

用户确认 6.1/6.2 后：勾 `docs/plan.md:580-582` 两条、补实测结论、提交 `phase-14: 补肉眼验收结果`；然后进 Phase 15（T1b 自定义取色）。
