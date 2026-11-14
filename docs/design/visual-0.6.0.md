# 视觉与交互设计 0.6.0

> 上游：`docs/requirements/frd-0.6.0.md`（T1 / U1）、`docs/plan.md`（Phase 14–17）。
> 约束：动画只用 XAML `Storyboard` + 内置缓动；禁止图片序列与第三方动效库；
> `AllowsTransparency="True"` 永远不出现（`AGENTS.md` 规则 5）。
> 现状：令牌在 `src/Wantodo.App/App.xaml:10-16`（7 个 `DynamicResource`），
> 主题由 `src/Wantodo.App/Appearance/ThemeService.cs:26-32` 直接改 `Application.Resources`，
> `App.xaml:6-9` 已写明「换主题不重建窗口」——这条是既定设计，不重新讨论。
>
> 每条按四段写：**触发 → 动效 → 降级 → 资源与体积**。

## 1. 主题系统

### 1.1 令牌（7 → 10）

| 令牌 | 用途 | 0.5.0 |
|---|---|---|
| `WindowBackground` | 窗口底 | 有 |
| `CardBackground` | 卡片、面板、输入框底 | 有 |
| `TextPrimary` | 正文、标题 | 有 |
| `TextSecondary` | 次要说明 | 有 |
| `TextMuted` | 时间戳、占位符 | 有 |
| `Divider` | 分隔线 | 有 |
| `Accent` | 选中、强调、焦点框 | 有 |
| `OnAccent` | 新增：画在强调色块上的文字 | 无，硬编码白 |
| `Hover` | 新增：悬停底 | 0.5.0 硬编码 `#22FFFFFF`（`App.xaml:32`），本阶段删除 |
| `Selected` | 新增：选中行底 | 无 |

新增三项的直接原因：`App.xaml:32` 的 `#22FFFFFF` 叠在浅色主题的白色卡片上不可见——
**白底「白」悬停是 0.5.0 已存在的缺陷**，不是为 0.6.0 新造的需求。

### 1.2 预设（实测对比度，WCAG 相对亮度公式）

五套预设取自 GitHub 上流行的开源配色；数值是 `ThemePalette.For(...)` 输出的最终值
（文字三档已按阈值自动校正，所以个别弱化色比原色板略亮/略暗）。

| 预设 | 背景 | 卡片 | 正文 | 次要 | 弱化 | 强调 | 强调上文字 | 正文 / 正文对卡片 / 次要 / 弱化对卡片 / 强调上文字 |
|---|---|---|---|---|---|---|---|---|
| GitHub 深色（默认，`Dark`） | `#0D1117` | `#161B22` | `#E6EDF3` | `#B1BAC4` | `#7D8590` | `#2F81F7` | `#0D1117` | 16.02 / 14.64 / 9.63 / 4.64 / 5.05 |
| GitHub 浅色（`Light`） | `#FFFFFF` | `#F6F8FA` | `#1F2328` | `#4D5560` | `#59636E` | `#0969DA` | `#FFFFFF` | 15.80 / 14.84 / 7.54 / 5.74 / 5.19 |
| Dracula（`Dracula`） | `#282A36` | `#21222C` | `#F8F8F2` | `#D0D2E4` | `#8894BA` | `#BD93F9` | `#282A36` | 13.36 / 14.81 / 9.51 / 5.26 / 5.90 |
| Nord（`Nord`） | `#2E3440` | `#3B4252` | `#ECEFF4` | `#D8DEE9` | `#9AB4CD` | `#88C0D0` | `#2E3440` | 10.84 / 8.73 / 9.25 / 4.69 / 6.24 |
| Catppuccin Mocha（`Catppuccin`） | `#1E1E2E` | `#313244` | `#CDD6F4` | `#BAC2DE` | `#A6ADC8` | `#89B4FA` | `#1E1E2E` | 11.34 / 8.69 / 9.26 / 5.65 / 7.79 |

阈值写进单测，不达标构建失败：`TextPrimary/WindowBackground ≥ 7:1`、`TextSecondary ≥ 4.5:1`、
`TextMuted ≥ 4.5:1`、`OnAccent/Accent ≥ 4.5:1`、`TextPrimary/CardBackground ≥ 7:1`
（`tests/Wantodo.Core.Tests/ThemeContrastTests.cs`）。

弱化色被自动校正的代表：Dracula `#6272A4` → `#8894BA`（4.0:1 → 5.26:1）、Nord `#81A1C1` → `#9AB4CD`（4.0:1 → 4.69:1）。
樱花 / 赛博 / 美漫从 0.6.0 移入未排期（`docs/plan.md` 未排期区），触发条件写在那一节。

### 1.3 自定义取色

- 只让用户选两个颜色：**背景**、**强调**；其余 8 个令牌由 `src/Wantodo.Core/ThemeDerivation.cs` 推导。
- 组成（`src/Wantodo.App/Dialogs/ThemeColorPicker.xaml(.cs)`）：两个选择框（背景 / 强调）+ 色相条（0–360）+
  饱和度/亮度方块 + hex 输入 + 预览卡片（标题/次要/弱化/强调按钮）+ 5 组实时对比度读数 + 取消/应用。
  约 400 行，无第三方控件。
- 推导（纯函数，可单测，实现在 `src/Wantodo.Core/ThemeDerivation.cs`）：
  1. 背景在中灰区、黑白文字都到不了 7:1 时，先把背景沿更近的一侧压暗/提亮（保持色相，步长 5%，
     最多 40 步）到可读区；纯黑/纯白背景不触发。
  2. 背景相对亮度 `L > 0.35` 判浅底，否则深底。
  3. 卡片：浅底向白混 55%；深底向黑混 9%；若卡片上正文不足 7:1，把卡片往文字反方向推。
  4. 正文取反色族，再用 `EnsureContrast` 对窗口底、卡片底分别校验 `≥ 7:1`；推的方向取黑白中对比更高的一端。
  5. 次要 = 正文向背景混 30%，对背景/卡片校验 `≥ 4.5`，不足则继续往黑/白推。
  6. 弱化 = 正文向背景混 50%，同样校验 `≥ 4.5`。
  7. 强调色先保证与背景 `≥ 3:1`；`OnAccent` 在黑/白中取对比更高者，再校验 `≥ 4.5`。
  8. `Hover` = 卡片混 10% 正文色；`Selected` = 卡片混 22% 强调色。
- 背景被压暗/提亮的边界写进单测：`ForCustom_TwoHundredRandomPairs_KeepInvariants`（200 组随机色，
  正文对窗口底与卡片底都 ≥7:1，次要/弱化 ≥4.5，强调上文字 ≥4.5，单组 <5 ms）。
- **触发**：设置页「外观 → 自定义…」。**动效**：无（窗口即时打开），预览随取色实时重绘。
- **降级**：取色异常 → 保留当前主题并提示，不写盘。**资源**：约 +220 行；按需加载，关闭即释放。

### 1.4 切换时机与跟随系统

- 点预设或确认自定义 → 立即写 10 个 key，不重建窗口（`App.xaml:6-9`），同步完成 ≤16 ms。
- 「跟随系统」读一次 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`
  （`src/Wantodo.App/Appearance/SystemTheme.cs`），按结果选 GitHub 深/浅色；**不订阅** `UserPreferenceChanged`，
  系统主题变了要重启或重新选一次。订阅留到未排期（`docs/decisions.md:373` 的未决项仍开着）。
- 默认：新装 `Dark`；0.5.0 用户保留已存的 `Dark/Light`。

### 1.5 主题的资源账

| 项 | 说明 |
|---|---|
| 降级 | 订阅失败 → 开关置灰 + WARN；画刷缺失 → 回落深色 |
| 体积 | 代码 +≈620 行（含测试）；二进制 +≤50 KB；0 依赖、0 图片 |
| 内存 | +≤2 MB private（取色窗口按需） |
| 启动 | 0 影响：预设是常量，无 IO |

## 2. 顶边下拉面板（形态 A：镜像主界面）

### 2.1 触发与把手

```text
 光标移到屏幕顶边 2 DIP 内，停留 ≥120 ms
        ▁▁▁▁▁▁▁▁            ← 把手 120×4 DIP，圆角 2，Accent 40%
   ┌────────────────────────────────────┐
   │ 便签        （760 DIP 宽，居中）      │   ← 展开高度 = 记忆值 240–720 DIP
   │ 要做的事情    │ 突然想到的问题        │      CardBackground + Divider
   │ ☐ 写周报      │ 记一下…              │
   └────────────────────────────────────┘
```

- **触发**：主显示器顶边、高 2 DIP（150% 缩放为 3 物理像素）、宽 = 主屏宽；停留 120 ms 才出把手，
  避免鼠标划过去关闭按钮时误弹。热区窗口 `Opacity=0.01`（真透明不参与命中），
  `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`，不进 Alt+Tab。
- **动效**：把手淡入 120 ms（只动 `Opacity`）；拖动跟手，窗口高度 = 12 DIP + 拖拽距离
  （12–记忆高度），正文随高度显示/隐藏；松手过 40% 吸附展开 160 ms `CubicEase EaseOut`，
  否则 140 ms `CubicEase EaseIn` 收回 12 DIP 再 `Hide()`。
- **与设计的偏差**：原稿写「只动 `TranslateTransform` + `Clip`，不改 `Height`」。实现改为改 `Height`：
  面板要懒加载且常态不占屏幕，窗口一旦固定为 760×480 就会在桌面盖住一块透明命中区，
  点桌面图标会被吞。改 `Height` 的代价是拖动时面板内布局重排；DragMove 实测连续（见 `docs/verify/phase17.md`），
  若后续出现拖动卡顿再换「固定高度 + 裁剪」方案。
- **降级**：关闭开关 → 不建热区窗口、不装钩子、不启计时器，功能回到 0.5.0。
- **资源**：`Windows/HotZoneWindow.cs`（≈100 行）、`Windows/TopPanelWindow.xaml(.cs)`（≈330 行）、
  `Windows/TopEdgePanelService.cs`（≈190 行）、`Interop/LowLevelMouseHook.cs` 与 `Interop/ForegroundWatcher.cs`（≈250 行）；
  二进制 +≤60 KB；面板懒加载，常态 0 内存；展开 private +≤6 MB；空闲 CPU 0（无轮询）。

### 2.2 内容与同步

- 形态 A 复用同一个 `BoardViewModel` 实例，与主窗口读写同一份 `board/data.json`；
  两侧绑的是同一份内存模型，互相改动即时可见。
- 面板内容：两组列（`ItemsControl Columns → Notes`，每行 ☐/☑，完成态 TextMuted + 删除线）、
  底部 `ComboBox` 选目标列 + `TextBox` 回车快速添加。
- 写盘仍由 `BoardViewModel` 的 600 ms 去抖统一执行（`JsonBoardStore` 原子落盘），
  不存在两个窗口各写一份的问题，因此不需要复刻 Phase 11 的自写标记。
- 未做：外部程序直接改 `board/data.json` 时的自动重载（0.5.0 也不支持）；如需要，进未排期区单独排。

### 2.3 收回与失焦

- 收回：`Esc`、点面板外任意处（低级鼠标钩子，物理像素判定）、从把手向上拖回 <40%。
- 面板 `ShowActivated=false`：打开时不抢键盘焦点，不打断正在编辑的笔记；因此**不靠 `LostFocus` 判定**。
- 前台窗口盖满整块屏幕（视频/游戏/无边框全屏）时：隐藏热区并收起面板，鼠标点屏幕最上 2 px 不会被吞。

### 2.4 多显示器与 DPI

- 只响应**主显示器**顶边；热区 `Left=0, Top=0, Width=主屏宽, Height=2 DIP`，PerMonitorV2 换算物理像素。
- 显示器切换 / DPI 变化后的重新注册**未做**（本期只主屏）；要支持多屏时进未排期。
- 全屏检测用 `EVENT_SYSTEM_FOREGROUND`：比较前台窗口 `GetWindowRect` 与所在显示器 `rcMonitor`；
  排除 `Progman` / `WorkerW` / `Shell_TrayWnd` 与本进程窗口；检测失败只记 WARN，保持响应。

### 2.5 面板的资源账

| 项 | 说明 |
|---|---|
| 降级 | 全屏检测失败 → 保持响应并 WARN；钩子注册失败 → 点外不收回，其余功能不变 |
| 体积 | 见 2.1；无图片、无新依赖 |
| 内存 | 懒加载，首帧不加载面板 XAML |
| 上限 | `PanelHeight` 夹取 240–720 DIP，越界钳到边界 |
## 3. 待验证（先探针，后下结论）

| ID | 问题 | 怎么测 | 判定 |
|---|---|---|---|
| P1 | 2 DIP 热区窗口能否稳定收到 `MouseEnter`；Win11 26200 的贴边最大化 / 任务栏自动隐藏是否抢占顶边 | 探针 + 日志，各撞顶 50 次 | ≥49/50 且不干扰最大化 → 可行；否则改 `WH_MOUSE_LL` |
| P2 | 全屏检测（前台窗口矩形 ⊇ 显示器矩形）在独占与无边框全屏下是否成立 | 视频全屏、游戏窗口全屏各测 | 两种都识别；漏判则降级为「始终响应」并记录 |
| P3 | 150% / 200% DPI 下 2 DIP 热区的物理像素与把手位置 | 改缩放重启，截图测量 | 偏差 ≤1 px |
| P4 | `ShowActivated=false` 时点外部收回用什么判定可靠 | 对比 `LostFocus` / `Mouse.Capture` / 钩子 | 选定一种，误收回 0 次/100 次点击 |

## 4. 与需求的对应

| 需求 | 本文覆盖 | 阶段 |
|---|---|---|
| T1 主题令牌与预设 | 1.1–1.2、1.4、1.5 | Phase 14 |
| T1 自定义取色 | 1.3、1.5 | Phase 15 |
| U1 顶边热区可行性 | 2.1 的触发部分、3 的 P1–P4 | Phase 16 |
| U1 面板形态 A | 2.1–2.5 | Phase 17 |
| F1 问候语 / 页脚场景 | 不在本文（未排期，见 plan 未排期区） | — |

## 5. 资源汇总（相对 0.5.0）

| 项 | 新增行数 | 二进制 |
|---|---|---|
| 令牌 + 5 套预设 | +90 | ≤15 KB |
| 推导与校验（Core + 测试） | +270 | ≤20 KB |
| 取色器 | +220 | ≤15 KB |
| 下拉面板 + 热区 | +540 | ≤60 KB |
| 合计 | ≈+1120 行 | **≤110 KB** |

体积兜底：若超出，先砍「色相条 + S/V 方块」只留 hex 输入（省 ≈120 行 / ≈15 KB），预设不砍。
