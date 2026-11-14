# 主题规格：樱花 / 赛博 / 美漫（0.6.0）

> 状态：设计定稿，实现为 Phase 21（樱花 + 形式感基础设施）与 Phase 22（赛博 + 美漫 + 收口）。
> 现有 5 套纯色预设（深色 / 浅色 / Dracula / Nord / Catppuccin）保持不变；
> 设置页显示名统一为「深色 / 浅色 / 樱花 / 赛博 / 美漫 / 跟随系统 / 自定义」，`Theme` 枚举值不变。

## 1. 三套主题的 10 令牌

| 令牌 | 樱花（Soft） | 赛博（Neon） | 美漫（Comic） |
|---|---|---|---|
| WindowBackground | `#FFF7F8` | `#070B14` | `#FFFDF3` |
| CardBackground | `#FFFFFF` | `#0E1524` | `#FFFFFF` |
| TextPrimary | `#3A2B31` | `#E6FAFF` | `#171A1F` |
| TextSecondary | `#6E5560` | `#8FA9C4` | `#4A5260` |
| TextMuted | `#7E6270` | `#6C8399` | `#667080` |
| Divider | `#F0DDE2` | `#1E3A47` | `#E3DFCF` |
| Accent | `#B23E55` | `#22E0FF` | `#D6293A` |
| OnAccent | `#FFFFFF` | `#04121A` | `#FFFFFF` |
| Hover | `#FBE9ED` | `#12202E` | `#FFF1D6` |
| Selected | `#F6D6DE` | `#16324A` | `#FFE3B0` |

### 实测对比度（WCAG 相对亮度公式，PowerShell 现算）

| 主题 | 正文/bg | 正文/card | 次要/bg | 弱化/card | OnAccent/Accent |
|---|---|---|---|---|---|
| 樱花 | 12.68 | 13.38 | 6.36 | 5.43 | 5.66 |
| 赛博 | 18.26 | 16.92 | 8.10 | 4.64 | 11.91 |
| 美漫 | 17.11 | 17.44 | 7.72 | 5.01 | 4.96 |

阈值：正文/bg ≥7、正文/card ≥7、次要 ≥4.5、弱化 ≥4.5、OnAccent/Accent ≥4.5。五项全部达标。
`EnsureContrast` 对这三套不触发校正（赛博 11.91×、美漫 4.96×、樱花 5.66× 都在阈值上），
所以「校正后是否还好看」只对自定义主题成立。

## 2. 形式感参数

| 参数 | Soft（樱花） | Neon（赛博） | Comic（美漫） |
|---|---|---|---|
| 描边 | 1 px `#F3D3DA` | 1 px `#22E0FF`（强调件） | 2 px `#171A1F` |
| 投影 | 2 px 柔光、12% | 外发光 6 px、强调色 45% | 硬阴影 2 px 偏移、纯黑 65% |
| 圆角 | 6 | 4 | 14 |
| 纹理 | 24×24 点阵纸纹 5% | 4×4 扫描线 8% | 8×8 halftone 6% |
| 纹理实现 | `DrawingBrush` | `DrawingBrush` | `DrawingBrush` |

- 关闭「主题效果」开关后：描边、投影、纹理全部不加载，只剩 10 令牌，观感回到 0.6.0 的纯色主题。
- 纹理层不参与任何动画（避免平铺 Brush 在动画层掉帧）；动画只作用在透明度/高度等标量属性上。

## 3. 纹理规格（全部矢量，无位图）

| 名称 | 平铺尺寸 | 内容 | 颜色/透明度 | XAML 估计 |
|---|---|---|---|---|
| 纸纹 | 24×24 DIP | 3 个 0.6 DIP 圆点 + 1 条 0.4 DIP 短线 | `#3A2B31`，5% | ~0.7 KB |
| 扫描线 | 4×4 DIP | 1 条 1 DIP 横线 | `#22E0FF`，8% | ~0.4 KB |
| halftone | 8×8 DIP | 1 个 1.2 DIP 圆点（45° 排布） | `#171A1F`，6% | ~0.6 KB |
| 斜纹（备用） | 6×6 DIP | 1 条 1 DIP 斜线 | 主题文字色，4% | ~0.4 KB |

合计 ≤2.4 KB XAML，不新增图片、字体或第三方控件。

## 4. 体积预算

| 项 | 估计 |
|---|---|
| 3 套令牌 + 参数（C#） | +1.2 KB |
| 4 个 `DrawingBrush`（XAML） | ≤2.4 KB |
| 样式模板（描边/阴影/圆角） | ≤1.5 KB |
| 位图 / 字体 / 图标 | 0 |
| **合计** | **≤5.1 KB** |

FDD zip 预算：0.5.1 的 214,863 B + ≤4 KB = **≤218,900 B**；0.6.0 红线 229,376 B（224 KB），余量约 10 KB。

## 5. 素材与许可证

- 只引用**十六进制色值**，不复制任何源码、图片或字体。
- 参考来源：
  - Dracula — `github.com/dracula/dracula-theme`，MIT
  - Nord — `github.com/nordtheme/nord`，MIT
  - Catppuccin — `github.com/catppuccin/catppuccin`，MIT
  - GitHub Primer — `github.com/primer/primitives`，MIT
  - Tokyo Night — `github.com/folke/tokyonight.nvim`，Apache-2.0（未采用，仅记录）
- 配色名「深色 / 浅色」为自描述名称，不暗示与任何官方主题的隶属关系。
- 三套风格主题（樱花/赛博/美漫）的样式为原创几何参数，不含任何角色插画、商标或受版权素材。
- 署名段落（写入本文与发布说明）：
  「配色参考上述 MIT 项目公开的十六进制色值；本项目不包含其源码、图片或字体。」

## 6. 实现落点

| 文件 | 改动 |
|---|---|
| `src/Wantodo.Core/ThemePalette.cs` | 3 套预设 + `ThemeStyle` 枚举与参数 |
| `src/Wantodo.App/Appearance/ThemeStyles.xaml` | 4 个 `DrawingBrush` + 描边/阴影样式 |
| `src/Wantodo.App/App.xaml` | 纹理与样式资源合并字典 |
| `src/Wantodo.App/SettingsWindow.xaml` | 显示名改「深色 / 浅色」+ 主题效果开关 |
| `src/Wantodo.App/Shell/TrayIcon.cs` | 显示名同步 |
| `tests/Wantodo.Core.Tests/ThemeContrastTests.cs` | Theory 扩到 8 套 |

## 7. 待验证

| 项 | 怎么验 |
|---|---|
| 平铺 Brush 在 150%/200% 下清晰 | 同一窗口截图，纹理边缘无灰度带 |
| 纹理叠动画不掉帧 | 展开面板时采样 `CompositionTarget.Rendering`，≥55 fps |
| 关闭效果后仍是完整主题 | 逐窗截图对照 `ThemeStyle=Flat` |
