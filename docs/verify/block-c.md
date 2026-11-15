# v6 块 C — 主题体系重构（7 套色板 + 迁移 + 语义令牌）

日期：2026-11-15　块：C　依赖：块 B（已提交 `773c046`）

## 1. 交付物

| 变更 | 文件 | 要点 |
|---|---|---|
| 7 套色板 | `src/Wantodo.Core/ThemePalette.cs` | Tokyo Night Day / Tokyo Night、Catppuccin Latte・Frappé・Macchiato・Mocha、霓虹 |
| 旧 id 迁移 | 同上 + `src/Wantodo.Core/Board.cs` | `MigrateThemeId`，`NormalizePanel()` 在加载时改写成新 id |
| 语义令牌 | 同上 | 新增 `SurfaceCard`、`OnSurfaceCard`、`BorderSubtle`、`BorderActive`、`AccentSoft`、`Glow`、`CornerRadius`、`Elevation` |
| 资源发布 | `src/Wantodo.App/Appearance/ThemeService.cs`、`src/Wantodo.App/App.xaml` | 16 个画刷 + 2 个数值令牌；默认值换成 Tokyo Night 深色 |
| 卡片面走令牌 | `src/Wantodo.App/Controls/CanvasBoard.cs`、`MainWindow.xaml`、`NotesWindow.xaml`、`BlockersWindow.xaml` | 卡片底 = `SurfaceCard`，常态描边 = `BorderSubtle`，选中描边 = `BorderActive` |
| 设置/托盘 | `SettingsWindow.xaml(.cs)`、`Shell/TrayIcon.cs` | 7 个单选；删除「跟随系统」与「自定义…」入口（`Custom` 只留内部调试） |

## 2. 命令与输出

```
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -warnaserror
    0 个警告
    0 个错误
    （2.56s）

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test --no-build
已通过! - 失败: 0，通过: 297，已跳过: 0，总计: 297，持续时间: 525 ms
```

启动冒烟：`src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.exe`（PID 31436），日志出现
「顶边下拉面板已启用」「首帧 1348ms；WS=139MB private=111MB；线程=14」，无异常。

## 3. 最终色值与实测对比度

单位 hex；比值 = 正文对窗口底 / 正文对卡片 / 次要对窗口底 / 弱化对卡片 / 强调上文字。阈值 7 / 7 / 4.5 / 4.5 / 4.5。

| 主题 | surface | card | text | secondary | muted | accent | onAccent | 比值 |
|---|---|---|---|---|---|---|---|---|
| light | #E1E2E7 | #F7F8FA | #1F2335 | #4B5478 | #5A5F7B † | #2E7DE9 | #141414 | 12.02 / 14.64 / 5.72 / 5.89 / 4.58 |
| dark | #1A1B26 | #24283B | #C0CAF5 | #9AA5CE | #898FAC † | #7AA2F7 | #16161E | 10.59 / 9.02 / 7.04 / 4.57 / 7.14 |
| latte | #EFF1F5 | #FFFFFF | #4C4F69 | #686B80 | #6A6D7A † | #8839EF | #FFFFFF | 7.06 / 7.99 / 4.64 / 5.15 / 5.41 |
| frappe | #303446 | #292C3C | #C6D0F5 | #A5ADCE | #979EB5 † | #CA9EE6 | #232634 | 8.06 / 9.04 / 5.55 / 5.18 / 6.83 |
| macchiato | #24273A | #1E2030 | #CAD3F5 | #A5ADCB | #8A91A9 † | #C6A0F6 | #181926 | 9.92 / 10.85 / 6.62 / 5.14 / 8.09 |
| mocha | #1E1E2E | #181825 | #CDD6F4 | #A6ADC8 | #8489A0 † | #CBA6F7 | #11111B | 11.34 / 12.14 / 7.37 / 5.07 / 9.23 |
| neon | #0B0F14 | #11161E | #E6F1FF | #9AB0C9 | #758298 † | #FF2E88 | #0B0F14 | 16.83 / 15.89 / 8.63 / 4.67 / 5.49 |

† = 被 `EnsureContrast` 校正过的色值（原色板：light #848CB5、dark #565F89、latte #8C8FA1、frappe #838BA7、
macchiato #8087A2、mocha #7F849C、neon #5B6B85）。校正只动弱化色，观感按 `docs/design/themes-0.6.0.md` 的源色板方向走。

补充：
- `light` 的 accent 上文字被推成 `#141414`（原计划白色不达标，4.58:1），肉眼应是深字压蓝底。
- `neon` 的 `Glow = #22D3EE`，其余主题 `Glow = Transparent`；发光绘制留给块 D。
- 卡片面对比度断言用 `SurfaceCard`；`SurfaceCard == CardBackground`。

## 4. 迁移与兼容

`ThemePalette.MigrateThemeId` 映射：`github-dark` / `nord` / `system` / `custom` / 未知 → `dark`；
`github-light` / `sakura` / `comic` → `light`；`dracula` / `catppuccin` → `mocha`；`cyber` → `neon`。
`Board.NormalizePanel()` 在加载时写回新 id；单测 `MigrateThemeId_MapsLegacyIds` 覆盖 12 组，
`JsonBoardStoreTests` 验证旧文件（`theme:"Dark"`、`theme:"Custom"`）打开不崩、得到有效主题。

## 5. 写死色扫描

```
grep -rn "#[0-9A-Fa-f]{6,8}" src/Wantodo.App --include=*.xaml
```

结果只剩 `App.xaml` 的资源字典（16 个画刷 + `OverlaySoft` + `DangerButton`）、
`Appearance/ThemeStyles.xaml`（纹理字典，块 D 用）和 `ThemeColorPicker.xaml` 的色相条光谱
（取色器内部，不是主题色）。`MainWindow` 的选中底色、对话框背景、`IconButton` 悬停全部改成令牌。

## 6. 手动验收（需人肉眼确认）

1. 设置页「外观」只剩 7 个选项；逐个点过去，卡片面（主窗口栏、面板卡片）不再出现浅色主题下的暗蓝块。
2. 切到「浅色」与「Catppuccin Latte」：主窗口、面板、便签窗、设置窗的卡片都是浅底深字。
3. 重启程序：主题保持在最后选的那套（`E:\Wantodo\board\data.json` 的 `theme` 已写成小写 id）。
4. 用旧的 0.6.0 数据文件（`theme:"Dark"` 或 `"Sakura"`）启动：不崩、落到深色或浅色，而不是白屏。

失败长什么样：浅色主题下仍有暗色卡片面；设置页点某主题没反应；旧文件启动报错或主题为空。

## 7. 已知欠账

- 形式层的 `brutal/glass/neon/clay/neumorph` 还没实现，`ThemeEffects.Flat` 之外的效果未被引用（块 D）。
- `ThemeColorPicker`（自定义取色）与 `ThemeStyles.xaml` 纹理仍在仓库里但没有入口，等块 D 或后续清理。
- 色调滑杆只对 `dark` / `light` 生效；其余 5 套固定色板忽略。
- 浅色主题的 `onAccent` 被推到近黑（4.58:1），观感与服务端色板不同，属校正结果。
- `ThemeEffects`（旧「主题效果」开关）仍保留字段与设置项，块 D 会重做成 `formId`。

## 8. 下一步第一条命令

块 D：形式层落地（先 `brutal`，再 `glass`、`neon`、`clay`/`neumorph` 视时间），每加一个 form 给对照截图与首帧/内存数字。
