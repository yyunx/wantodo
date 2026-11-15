# wantodo 0.7.0 计划书：双表面画布 · 定义先行（draft）

> 状态：**draft，等批注**。本轮不含业务代码改动，`src/`、`tests/`、`*.csproj` 一字未动。
> 基线：HEAD `3cb380f`（0.7.0-dev，画布 MVP 已落地；build 0 警告，测试 317 通过）。
> 依据：`docs/plan.md` Phase 0–24、`docs/verify/progress.md`、`docs/verify/block-d.md`、`src/` 实读。
> 铁律：术语必须有出处或「待核实 + 关键词」；每条定义必须有**反例**；本文不把「默认」当决策依据。

---

## 0 摘要与现状校正

**一句话新模型**：把「一个便签板」拆成**一个窗口、一块画板、两个表面**（结构化 / 自由），
其上叠**一个主题（Liquid Glass）× 一组色板（Palette）**，颜色只走令牌、几何只走主题，撤销按表面隔离。

**分期**：P0 架构与主题基座 → P1 表面功能与交互修正 → P2 打磨与性能。P0 不通过不写 P1 任何代码。

**需要你拍板的 3 件事**：① 画布数据是否合并进单个 `data.json`（§8 Q1）；② 表面切换器与工具栏的形态（§8 Q2/Q3）；③ 窗口级液态玻璃在没有 `AllowsTransparency` 的前提下先探针还是先降级（§8 Q8）。

### 0.1 现状校正（实测，提示词 §2 有 5 处与仓库不一致）

| # | 提示词 §2 的说法 | 仓库实况 | 证据 |
|---|---|---|---|
| 1 | 预设 = GitHub 深/浅、Dracula、Nord、Catppuccin Mocha + Custom | 已是 7 套：`light`(Tokyo Night Day)、`dark`(Tokyo Night)、`latte/frappe/macchiato/mocha`(Catppuccin)、`neon`；旧 id 走迁移表 | `src/Wantodo.Core/ThemePalette.cs:100-115`、`:152-185`、`:131-150` |
| 2 | 图片卡是带标题栏的方框 | 块 B 已去框（悬停浮层 + 双击打开），块 D 起卡片走 `ThemeCardBorderBrush/Thickness` | `docs/verify/block-d.md` §4；`CanvasBoard` 卡片渲染（**待逐行核对**） |
| 3 | 待办卡显示「（未选择栏）」无法落位 | 已能选桌面栏目 + 「＋ 新建栏目」；工作树未提交补丁再加「选择栏目…」占位 | `git diff -- src/Wantodo.App/Controls/CanvasBoard.cs`（+19/−2，未提交） |
| 4 | 计时有两个同名「开始」 | 弹窗已改「设置完成」，卡片为「开始 / 暂停 / 继续」，`PomodoroPlan` 已做分段时间轴 | `docs/verify/progress.md` 块 B 段 |
| 5 | 卡头拖拽被实现成改名 | 现拖拽 = 移动卡片；改名走独立入口 | `CanvasBoard` 拖动处理（**待逐行核对行号**） |
| 6 | 画布与便签板同一份 `data.json` | **不是**：便签板在 `data.json`，画布在 `board/canvas.json` | `src/Wantodo.Core/CanvasItem.cs:48` 注释；`DataPaths.CanvasFile` |

> 第 6 条是硬冲突：R0.4 要求「明令禁止两个文件」。见 §4.4 与 §8 Q1。

### 0.2 本轮不做的事（防止顺手重构）

不改 `CanvasBoard` 的现有交互、不合并 `ThemeEffects` 历史字段、不重写 `LayoutNode`、不动便签板（桌面 + 下拉面板）的布局算法、不引入任何第三方包。

---

## 1 概念废弃声明

### 1.1 被废弃的旧定义

旧模型（0.6.0 起，块 C/块 D 拼出来的）把三件事压进三个互不相干的字段：

| 旧字段 | 旧含义 | 承载了什么 |
|---|---|---|
| `Board.Theme`（string） | 配色预设 | 16 个颜色令牌 + 圆角 + 层级 |
| `Board.FormId`（string） | 形式感 | 描边宽度/圆角/阴影/卡片透明度 |
| `Board.ThemeEffects`（bool） | 形式感总开关 | 半开状态 |

### 1.2 错在哪（四条，逐条可验伪）

1. **范畴错误**：颜色是可替换的**令牌集合**，风格是**几何与材质规则**。合并在一个 `Theme` 字符串后，每加一种风格都要改 `ThemePalette` 的 `switch`，而配色与风格无法自由组合（例：「硬阴影 + 浅色 Catppuccin」表达不出来）。
2. **正交性缺失**：`FormId=brutal` 对任何配色都硬套 2px 黑边与 0 圆角，它没有材质定义，只有轮廓常量；`FormId=glass` 只是一句 `CardAlpha=0.72`，背景无任何采样——按 §3.4 的定义，这**不算**液态玻璃。
3. **没有「表面」概念**：画布只有一层，卡片自带 chrome；「图片不该有框」只能靠 `if (kind == image)` 在渲染里打补丁，而不是靠层职责隔离。
4. **数据层承载 UI 语义**：`theme` / `formId` / `panelMode` 都是「UI 怎么画」写进了持久化对象，于是每次改外观都要动迁移代码。

### 1.3 新模型

```
外观 = Theme（材质 × 形状 × 层级）  ×  Palette（颜色槽位集合）
数据 = 画板文档（一个文件）× ThemeId / PaletteId / Canvas（两棵集合）
```

- **Theme**（本期只 1 个：`liquidGlass`）拥有：窗口材质、卡片材质、描边、圆角、阴影、内高光、动效时长。
- **Palette** 拥有：`WindowBackground/SurfaceCard/OnSurfaceCard/BorderSubtle/BorderActive/Accent/AccentSoft/TextPrimary/TextSecondary/TextMuted/Divider/Glow`（12 个颜色槽位）+ 2 个数值（`CornerRadius`、`Elevation`，属于 Theme，不随 Palette 变）。
- **`FormId` / `ThemeEffects` 降级为只读历史别名**：读取时换算成 `themeId`，此后不再写回。

### 1.4 旧数据迁移（`data.json` Version 2 → 3）

| 旧字段 / 文件 | 新 | 规则 | 兜底 |
|---|---|---|---|
| `theme: "mocha"` / `"dracula"` / `"catppuccin"` | `paletteId: "mocha"` | 复用 `ThemePalette.MigrateThemeId` 现有映射表，`themeId` 一律写 `liquidGlass` | — |
| `theme` 为未知值 | `paletteId: "dark"` | 记 WARN `[迁移] 未知配色 X → dark` | 不崩 |
| `theme: "custom"` 且 `customBackground`、`customAccent` 都能解析 | `paletteId: "custom"` | 保留自定义色，`SurfaceCard` 等由 `ThemeDerivation` 推导 | 缺一则 `dark` |
| `theme: "System"` | `paletteId` = 当前系统明暗对应项 | 迁移时求一次，此后不再跟随系统 | — |
| `formId: "brutal"` 等 | 丢弃 | `themeId` 一律 `liquidGlass`；记 WARN `[迁移] 旧形式 X → 液态玻璃` | — |
| `themeEffects: false` | 丢弃 | 改由设置页「减少动效/效果」开关承担（仅关阴影与动画，不关材质） | — |
| `board/canvas.json` | `data.json.canvas` | 读入 → 合并 → 旧文件改名 `canvas.imported-<ts>.json` 保留 | 解析失败 → 空画布 + WARN |
| `panelMode: "Canvas"` | 保留 | 画布形态不变 | — |
| `canvas` 已存在且 `canvas.json` 也在 | 以 `data.json.canvas` 为准 | 记 WARN，不覆盖 | — |

迁移前先把原文件复制一份 `data.json.pre-070.bak`（只做一次，存在则跳过）。迁移函数是 Core 纯函数 + 单测，不在 `App` 里。

### 1.5 反例（用来检验你我理解一致）

- 保留 `theme: "dark"` 当主题 id，再并排加 `themeId` → **不算**完成迁移（两套真相）。
- 迁移时把 `canvas.json` 删掉而不是改名保留 → **不算**（不可回滚）。
- 新增 `Theme2` 字段让新代码读它、旧代码读 `Theme` → **不算**。

---

## 2 术语对照表（索引；完整定义在 `docs/design/terminology.md`）

| 术语 | 英文原名 | 类别 | 本项目对应物 |
|---|---|---|---|
| 云母材质 | Mica | 材质 | 窗口级底层材质（Win11） |
| 亚克力材质 | Acrylic | 材质 | 窗口级底层材质（Win10 降级档） |
| 液态玻璃 | Liquid Glass | 材质 | 本期唯一 Theme 的命名来源 |
| 背景采样模糊 | backdrop blur | 材质 | 窗口级材质的能力，卡片级不做 |
| 玻璃拟态 | glassmorphism | 风格 | 液态玻璃的近似风格，不是它本身 |
| 高光反射 | specular highlight | 材质 | 卡片 1px 内高光 + 顶部轻渐变 |
| 内高光细线 | 1px inner highlight | 风格 | `ThemeStrokeBrush` 在卡片顶边 |
| 新粗野主义 | Neubrutalism | 风格 | 待办主题（本期不做） |
| 便当网格 | Bento grid | 布局（非主题） | 待办布局，明确不是主题 |
| 陶土拟物 | Claymorphism | 风格 | 待办主题（本期不做） |
| 新拟物 | Neumorphism | 风格 | 待办主题（本期不做） |
| 动态排版 | Kinetic typography | 风格 | 待办主题（本期不做） |
| 高饱和色 | Hyperchromatic | 色板策略 | 待办主题（本期不做） |
| 半调网点 | halftone | 图案 | 待办主题的图案素材 |
| 颗粒噪点 | grain / noise | 图案 | 待办主题的图案素材 |
| 排线阴影 | hachure | 图案 | 待办主题的图案素材 |
| 点阵网格 | dot grid | 图案 | 自由表面的背景网格候选 |
| 设计令牌 | design token | 色彩 | `ThemeService` 写入的资源键 |
| 语义令牌 | semantic token | 色彩 | `SurfaceCard` 等 12 个槽位名 |
| 色阶 | color ramp | 色彩 | 候选 Palette 的取用方式 |
| 色调色板 | tonal palette | 色彩 | Material 系候选的取用方式 |
| 层级刻度 | elevation scale | 色彩/层级 | `ThemeElevation` + `ThemeShadowOffset` |
| 对比度 | contrast ratio | 色彩 | `ThemeDerivation.EnsureContrast` 的门限 |
| 多表面画布 | multi-surface canvas | 画布 | 本期的核心结构 |
| 结构化表面 | structured surface | 画布 | 卡片层 |
| 自由表面 | freeform surface | 画布 | 图元层 |
| 表面切换器 | surface switcher | 交互 | 顶栏第一行的分段控件 |
| 分段控件 | segmented control | 交互 | 切换器控件选型 |
| 工具栏 | toolbar | 交互 | 顶栏第二行 |
| 浮动工具条 | floating toolrail | 交互 | 备选形态（不采用） |
| 作用域撤销 | per-scope undo | 交互 | 每表面一条撤销栈 |
| 行内改名 | inline rename | 交互 | 卡片标题双击进入编辑 |
| 拖拽把手 | drag handle | 交互 | 卡头 / 面板顶部拖拽区 |
| 可供性 | affordance | 交互 | 可拖动、可缩放、可点 |
| 空状态 | empty state | 交互 | 表面无内容时的引导 |
| 状态机 | state machine | 交互 | 计时器的 Idle/Configured/Running/Paused/Done |
| 拖拽阈值 | drag threshold | 交互 | 4 DIP 起拖 |
| 命中测试 | hit testing | 交互 | `PointFromScreen` 系列 |
| 无铬 | chrome-less | 视觉 | 图片对象不画容器 |

> 索引 39 条（`terminology.md` 另有 10 条实现向条目：Mica Alt、系统背景材质、硬阴影、画布文档、画布视口、笔迹、图元、色板、焦点、视图状态持久化），覆盖材质、风格、色彩、画布与表面、交互五类；每条在 `terminology.md` 有定义 + 出处，反例与验收在 §5。

---

## 3 主题体系模型

### 3.1 正交维度

| 维度 | 包含 | 不包含 | 数据字段 | 改动影响面 |
|---|---|---|---|---|
| Theme | 窗口材质、卡片材质、描边、圆角、阴影、内高光、动效 | 任何具体色值 | `themeId` | 所有窗口 + 所有卡片 |
| Palette | 12 个颜色槽位 | 几何、材质、圆角 | `paletteId` | 只换色，形状不变 |
| 减少效果开关 | 阴影、动画时长 | 材质、颜色 | `reducedEffects` | 只影响动效与阴影 |

三者可独立组合：`liquidGlass × mocha × 减少效果=false` 是合法状态；`brutal × mocha` 在 0.7.0 不合法（`brutal` 不是 Theme 而是待办主题，见 §5 待办行）。

### 3.2 切换传播与漏点防线

传播链：设置页/颜色键 → `BoardViewModel` 改 `themeId`/`paletteId` → `JsonBoardStore.Save`（原子）→ `ThemeService.Apply` 写入 `Application.Resources` → 所有 `DynamicResource` 绑定自动刷新。**不重建窗口、不重建控件**。

三道防线（都要有验收）：

1. **只准 `DynamicResource`**：主题令牌禁止被 `{StaticResource}` 引用。新增单测扫描 `src/Wantodo.App/**/*.xaml`，命中即 fail。
2. **App 层零裸 hex**：`rg -n '#[0-9A-Fa-f]{6}\b' src/Wantodo.App` 只允许命中 `ThemeStyles.xaml` 里的图案定义；`ThemePalette.cs`（Core 数据源）不在约束内。
3. **键集合闭环**：`ThemeService` 写入的键 = 常量数组 `ThemeResourceKeys`（单一真相）；单测比对「XAML 引用的主题键 ⊆ 写入键」，缺键或写无用键都 fail。

> 为什么靠资源而不是重建：0.6.0 已验证 DynamicResource 热切换（`docs/decisions.md`，块 C），重建窗口会让便签丢失焦点、让贴桌面层级重新 `Attach`，代价高。

### 3.3 控件 × 令牌矩阵（覆盖 §6.3 全部条目）

图例：`面` = WindowBackground / SurfaceCard；`文` = TextPrimary / TextSecondary / TextMuted；`强` = Accent / AccentSoft；`边` = BorderSubtle / BorderActive；`形` = CornerRadius / StrokeWidth；`层` = Elevation / Shadow。

| 区域 / 控件 | 面 | 文 | 强 | 边 | 形 | 层 | 备注 |
|---|---|---|---|---|---|---|---|
| 主窗口背景 | WindowBackground + Mica | — | — | — | — | — | 材质失败退纯色 |
| 桌面便签栏 | SurfaceCard α0.72 | TextPrimary/Secondary | Accent（勾选） | BorderSubtle | 10 | 阴影 4 DIP | 块 A–D 已走令牌 |
| 下拉面板容器 | WindowBackground + Mica | — | — | — | — | — | 面板独立窗口 |
| 面板顶栏 / 工具行 | SurfaceCard α0.86 | TextPrimary | AccentSoft（选中工具） | BorderSubtle | 10 | 1 | 常驻，不随滚动 |
| 状态行（张数/缩放/撤销） | 透明 | TextMuted | AccentSoft | — | — | — | 字号最小的文本层 |
| 设置窗口 | WindowBackground + Mica | TextPrimary/Secondary | Accent（主按钮） | BorderSubtle | 10 | 1 | 已有下拉框/按钮模板 |
| 随心记窗口 | SurfaceCard | TextPrimary/Muted | Accent（光标） | BorderSubtle | 10 | 1 | 文本框走 AccentSoft 选区 |
| 画板·结构化表面 | WindowBackground + Mica | — | — | — | — | — | 无自身底色，透出材质 |
| 画板·自由表面 | WindowBackground + Mica | — | — | — | — | — | 同上，背景网格另算 |
| 记事卡 | SurfaceCard | TextPrimary | — | BorderSubtle | 10 | 阴影 | 卡内无边框常量 |
| 待办卡 | SurfaceCard | TextPrimary/Secondary | Accent（勾选/计数） | BorderSubtle + BorderActive | 10 | 阴影 | 栏目色不进卡片 |
| 计时卡 | SurfaceCard | TextPrimary / 剩余时间大字 | Accent（进度环） | BorderSubtle | 10 | 阴影 | 运行态强调色 |
| 链接卡 | SurfaceCard | TextPrimary（链接文本） | Accent（可点） | BorderSubtle | 10 | 阴影 | 打开走系统默认浏览器 |
| 图片对象 | 无容器 | — | BorderActive（选中描边） | 仅选中态 | 8 | 选中时阴影 | 无铬 |
| 笔迹 / 形状 / 箭头 | 无容器 | — | 令牌名（`accent` 等） | — | — | — | 颜色存令牌名，不存 hex |
| 文本对象（自由表面） | 无容器 | TextPrimary | — | 选中态 | — | — | 不写回随心记 |
| 按钮（全部） | SurfaceCard | TextPrimary | AccentSoft（悬停） | BorderSubtle | 8 | 0 | 已换模板 |
| 输入框 / 下拉框 | SurfaceCard | TextPrimary / TextMuted（占位） | Accent（光标/选区） | BorderSubtle / BorderActive（聚焦） | 8 | 0 | 已换模板 |
| 滚动条 | 透明轨道 + SurfaceCard 滑块 | — | AccentSoft（悬停） | — | 4 | — | 需自绘模板 |
| 右键菜单 | SurfaceCard α0.95 | TextPrimary | AccentSoft（高亮） | BorderSubtle | 8 | 阴影 | 与 ComboBox 弹层同源 |
| 空状态 | 透明 | TextMuted | AccentSoft（引导按钮） | — | — | — | 每表面一句文案 |
| 拖拽指示线 | — | — | Accent | — | — | — | 1 px 虚线 |
| 选中框 | — | — | BorderActive | 1 px | 8 | — | 与图片选中同一表达 |
| 工具栏本身 | SurfaceCard α0.86 | TextPrimary/Muted | AccentSoft | BorderSubtle | 10 | 1 | 三层结构见 §4.3 |

### 3.4 液态玻璃（Liquid Glass）材质定义与降级

**定义**：窗口级由 DWM 提供背景采样（Mica 或 Acrylic），其上叠三层内容（内容层 α0.72、控件层 α0.86、卡片层 α0.72 + 阴影），每层顶边 1 px 内高光、顶部轻渐变；颜色全部来自 Palette。

**层模型**：

| 层 | 内容 | 实现 | 本期 |
|---|---|---|---|
| L0 材质 | Mica（Win11）/ Acrylic（Win10 1803+）/ 纯色 | `Interop/` 新封装 | 做 |
| L1 内容层 | 面板与画板底 | `SurfaceCard` α0.72 | 做 |
| L2 控件层 | 按钮、输入框、工具选中态 | `SurfaceCard` α0.86 | 做 |
| L3 卡片层 | 卡片、图片选中框 | `SurfaceCard` + 阴影 | 做 |
| L4 折射 | 实时折射/动态变形 | 需要 GPU shader | **不做**（WPF 无此能力） |

**版本矩阵**（API 常量均已核实）：

| 系统 | API | 结果 |
|---|---|---|
| Win11 22621+ | `DWMWA_SYSTEMBACKDROP_TYPE`(38) = `DWMSBT_MAINWINDOW`(2) | Mica |
| Win11 22000–22620 | `DWMWA_MICA_EFFECT`(1029) = 1（旧属性） | Mica |
| Win10 1803–19045 | `SetWindowCompositionAttribute` + `ACCENT_ENABLE_ACRYLICBLURBEHIND`(4)，**未文档化** | Acrylic 或失败 |
| < 1803 / 高对比度 / 关透明 / 材质调用失败 | 无 | 纯色面 + 1 px 内高光 + WARN 日志 |

**必须先探针验证的三件事**（不验证不许写实现）：

1. 禁止 `AllowsTransparency` 时，WPF（`net10.0-windows`）如何让 Mica 透出：`WindowChrome.GlassFrameThickness=-1` + `DwmExtendFrameIntoClientArea` + `Window.Background=Transparent` 的组合在常驻窗口上是否真的出材质（还是黑底）。
2. 贴桌面层的窗口（owner = `DesktopHost.Find()`、`WS_EX_NOACTIVATE`）上 Mica 是否生效——DWM 可能因 owner 关系不画材质。
3. 收起态 12 DIP 条是否需要材质；若需要，切换时是否闪。

探针输出日志 + 截图，进 `docs/verify/` 后才能进 P0 实现。

**验收**：Win11 上设置页背景可见桌面透出（截图）；Win10 上纯色 + WARN；两者都不崩、不掉帧（首帧增量 ≤5%）。

### 3.5 色板候选与许可

取用原则：Palette 只取「语义槽位」（背景/表面/文本/强调/边界），不整包搬颜色名。**色值本身不受著作权保护**，许可义务主要在归属与 NOTICE。

| 色板 | 明暗 | 许可 | 取用槽位 | 本期 |
|---|---|---|---|---|
| Tokyo Night | 深 | Apache-2.0（`folke/tokyonight.nvim`，需保留 NOTICE） | 背景/表面/文本/强调 | 已用（`dark`） |
| Tokyo Night Day | 浅 | 同上 | 同上 | 已用（`light`） |
| Catppuccin（Latte/Frappé/Macchiato/Mocha） | 浅→深 | MIT | 背景/表面/文本/强调 | 已用（4 套） |
| 霓虹（本项目自定） | 深 | 本项目定义 | 全槽位 | 已用（`neon`） |
| GitHub Primer | 明/暗 | MIT | 背景/表面/边界/强调 | 候选（未启用） |
| Radix Colors | 明/暗 | MIT | 12 阶灰 + 强调 | 候选（未启用） |
| Kanagawa | 深 | MIT | 背景/表面/强调 | 候选（未启用） |
| Dracula | 深 | MIT | 背景/表面/强调 | 候选（已废弃，仅迁移用） |
| Rosé Pine | 明/暗 | MIT | 背景/表面/强调 | 候选（未启用） |
| Everforest | 明/暗 | MIT | 背景/表面/强调 | 候选（未启用） |
| Nord | 深 | 待核实（关键词：nordtheme LICENSE） | 背景/表面/强调 | 候选（未启用） |
| Gruvbox | 明/暗 | 待核实（关键词：morhetz/gruvbox LICENSE） | 背景/表面/强调 | 候选（未启用） |
| Solarized | 明/暗 | 待核实（关键词：altercation/solarized LICENSE） | 背景/表面/强调 | 候选（未启用） |
| One Dark | 深 | 待核实（关键词：atom one dark LICENSE） | 背景/表面/强调 | 候选（未启用） |
| Ayu | 明/暗 | 待核实（关键词：ayu-theme LICENSE） | 背景/表面/强调 | 候选（未启用） |
| Material 3 tonal | 明/暗 | 待核实（关键词：material-color-utilities LICENSE） | 色调色板 | 候选（未启用） |

### 3.6 颜色键

- 位置：设置页「颜色」分区（主入口）+ 画板状态行右侧的 7 个色点（快捷入口，与设置页同一 `paletteId`）。
- 分组：按明暗分两行——浅色组（light、latte）与深色组（dark、frappé、macchiato、mocha、neon）。
- 自定义色：`paletteId=custom` 只在旧数据带回 `customBackground`+`customAccent` 时出现；设置页可编辑，可删除（删除即回落 `dark`）。
- 反例：把 7 个色板做成 7 张缩略图塞进设置页顶部轮播 → 不算（入口重、不显示当前色）；颜色键点击后换 `themeId` → 不算（换色不是换主题）。

---

## 4 画布架构模型

### 4.1 双表面命名与职责边界

| 表面 | 正式名 | 承载 | **禁止承载** |
|---|---|---|---|
| A | 结构化表面（structured surface） | 记事卡、待办卡、计时卡、链接卡；卡片 = 数据记录的可视投影；可选中、可改名、可删（进回收） | 图片、笔迹、形状、箭头、无 id 装饰图元 |
| B | 自由表面（freeform surface） | 图片对象、笔迹（stroke）、形状（矩形/线/箭头）、自由文本对象 | 有语义状态的卡片（note/todo/timer/link）、条目勾选、栏目绑定 |

边界判定：**卡片进数据仓库、图元只进画布文档**。待办卡的「勾选」会写 `notes`，图片的「打开」不会写任何仓库——这条区别就是两表面的分界线。

### 4.2 表面切换器（surface switcher）

- 选型：**分段控件（segmented control）**，两段常驻：`结构化` / `自由`，位于顶栏第一行左侧；第一行右侧留 `＋` 扩展位。
- 理由：两段且互斥 → 分段控件比 tab 更准确（tab 暗示「多文档、可关闭、各自撤销」）；比图标分页更可读（文字 + 图标，40 px 高即可）。
- 备选：tab strip（若将来表面 > 4 个，升级为 tab + 溢出菜单）。
- 扩展策略：> 4 个表面时，分段控件退化为「图标 + 下拉溢出」，**不做横向滚动**（滚动条在 12 DIP 收起态下不可用）。
- 反例：切换后重建控件树、切回时选择丢失、两个 tab 各带一份撤销 → 都不算。

### 4.3 共享层 / 专属层

| 共享（两表面共用） | 专属 |
|---|---|
| 主题令牌与液体玻璃材质 | 结构化：插入卡（记事/待办/计时）、搜索、视图（网格/对齐/排序） |
| 缩放（25%–400% + 适应窗口） | 自由：选择、手形、文本、图片、矩形、线、箭头、画笔、橡皮 |
| 撤销/重做（各自栈，UI 同一提示） | — |
| 剪贴板（两表面行为不同，入口同一 `Ctrl+V`） | — |
| 搜索（本期只在结构化表面生效） | — |
| 键盘焦点、Esc 语义、空状态骨架 | — |
| 右键菜单骨架（条目不同） | — |

### 4.4 数据模型（推荐：单文件；见 §8 Q1）

```jsonc
{
  "version": 3,
  "themeId": "liquidGlass",
  "paletteId": "mocha",
  "reducedEffects": false,
  "canvas": {
    "activeSurfaceId": "structured",
    "cards": [
      { "surface": "structured", "id": "c1", "kind": "todo",
        "x": 40, "y": 60, "w": 260, "h": 200, "z": 3,
        "payload": { "columnId": "9f2c…" } }
    ],
    "objects": [
      { "surface": "freeform", "id": "o7", "kind": "image",
        "x": 520, "y": 80, "w": 320, "h": 200, "z": 1,
        "payload": { "path": "canvas-images/9f2c….png", "naturalW": "1600", "naturalH": "1000" } },
      { "surface": "freeform", "id": "o8", "kind": "shape",
        "payload": { "shape": "arrow", "x1": "80", "y1": "420", "x2": "300", "y2": "380",
                     "color": "accent", "strokeWidth": "2" } },
      { "surface": "freeform", "id": "o9", "kind": "stroke",
        "payload": { "path": "board/ink/o9.isf", "color": "accentSoft", "strokeWidth": "3" } }
    ],
    "viewports": {
      "structured": { "zoom": 1.0, "offsetX": 0, "offsetY": 0 },
      "freeform":   { "zoom": 0.75, "offsetX": -120, "offsetY": 40 }
    }
  }
}
```

规则：

- 两棵集合（`cards[]` / `objects[]`）各有自己的 schema；`surface` 是判别字段，本期取值恒定，为第三表面预留（恒定值不是冗余，是迁移位）。
- 二进制资产（图片、笔迹 ISF）**不进 `data.json`**，只存相对路径；读不到时渲染占位块 + WARN，不崩。
- 图元颜色存**令牌名**（`accent` / `textMuted`），不存 hex → 换 Palette 自动跟随。
- 一个 `data.json`、一套主题、一套持久化、一条保存路径（原子写）。
- 反例：`canvas.json` 与 `data.json` 各存一半内容 → 不算；`objects` 里塞 `kind: "todo"` → 不算。

### 4.5 状态与切换

- `activeSurfaceId` 持久化；每个表面自己的 `viewport` 持久化。
- 两个画布宿主（各自一个 `Canvas`/`Grid`）常驻 XAML，切换只改 `Visibility` 与 `IsHitTestVisible`；**不重建控件树、不丢选择、不清撤销栈**。
- 切换后焦点落到该表面的画布宿主；`Esc` 在两表面都是「退出当前操作」，不切换表面。
- 切换 20 次不丢卡片、不闪白（验收项 V-04）。

### 4.6 撤销作用域

- **每个表面一条撤销栈**，深度 50 步（内存内，不持久化）。
- `Ctrl+Z` / `Ctrl+Y` 只作用于当前表面；切到另一表面后按 `Ctrl+Z` 不动原表面数据。
- 状态行显示「撤销：N 步」，切表面时刷新为当前表面的 N。
- 跨表面操作（把卡片拖到自由表面、把图片拖进结构化表面）**本期不做**：拖过去显示禁止光标。
- 反例：两表面共用一条栈 → 不算；撤销后另一表面内容变了 → 不算。

### 4.7 图片归属

- 图片**只**存在于自由表面。
- 结构化表面的「插入图片」按钮 = 切到自由表面并在视口中心落位（一次点击完成两步），插入后焦点在自由表面。
- 下拉面板（Dock 形态）不出现图片对象。

### 4.8 渲染与性能

| 项 | 预算 | 手段 |
|---|---|---|
| 首帧增量 | ≤ 5%（基线 1098 ms → ≤ 1153 ms） | 两个画布宿主共用模板；材质只调一次 DWM |
| 内存增量 | ≤ 3 MB（基线 private 102.1 MB → ≤ 105.1 MB） | 图片按显示尺寸解码（`DecodePixelWidth`），不缓存原图 |
| 50 卡 + 500 笔迹 | ≥ 55 fps（每帧 ≤ 18 ms） | 笔迹层 > 500 条或 > 2000 顶点时，把稳定段合成到 `RenderTargetBitmap`（512×512 分块，最多 8 块），编辑中的笔迹留在 `InkCanvas` |
| 撤销 | 50 步 × 2 表面 | 只存操作闭包，不存快照 |
| 切换 | 20 次无卡顿 | 不重建控件；视口变换用 `MatrixTransform` |

笔迹实现建议：`InkCanvas` 只承载笔迹层（自带 `Strokes`、`EraseByPoint`、命中测试），几何图元自绘；两者 z 序由 `ZIndex` 统一。WPF 的 `StrokeCollection` 提供 `Save` 重载（待核实具体重载与 ISF 选项），`InkCanvas` 的层叠代价与静态化收益需在 P1 首个探针里量，不许先写结论。

---

## 5 逐项定义表（六列，R0–R7 + 待办主题）

### 5.1 R0 双表面画布（架构前提）

| 名称 | 定义 | 反例 | 边界 | 验收 | 出处 |
|---|---|---|---|---|---|
| 结构化表面（structured surface） | 由一组「有语义状态」的卡片构成的画布层；卡片是数据记录的可视投影，状态变化可回写 Core 仓库 | 自由摆放的图片/笔迹出现在这层；同一张卡同时出现在两层，都不算 | 本期承载 note/todo/timer/link 四类 | 30 次表面往返后卡片数与 id 不变；`data.json.canvas.cards` 与屏幕卡片一一对应；该层无 `kind=image` | 本项目 §4.1；同类概念：tldraw pages（待核实） |
| 自由表面（freeform surface） | 由无语义状态的图元（图片/笔迹/形状/文本）构成的画布层；图元只有几何与样式，不参与仓库 | 出现勾选框、栏目绑定、可勾选的条目，都不算 | 本期四类图元；不做图层树、不做组合 | 插入 10 个图元后切面往返 3 次，位置误差 ≤1 DIP；`canvas.objects` 条数一致 | 本项目 §4.1；同类：Figma canvas（待核实） |
| 表面切换器（surface switcher） | 顶栏第一行的互斥分段控件，切换时只改可见性 | 切换后重建控件、切回丢选择、两段各带撤销栈，都不算 | 两段常驻；>4 表面才升级为 tab + 溢出 | 切换 20 次不丢卡片、不闪白、不重建窗口；`activeSurfaceId` 落盘并在重启后恢复 | Apple HIG segmented controls（待核实路径）；Material segmented buttons（待核实路径） |
| 共享层（shared layer） | 两表面共用的一套能力：主题令牌、缩放、撤销 UI、剪贴板入口、焦点与空状态骨架 | 每表面各写一份缩放实现、各一套主题资源，不算 | 搜索本期只在结构化表面生效 | 两表面缩放同步率 100%（同一控件驱动）；主题切换后两表面同时变 | 本项目 §4.3 |
| 撤销作用域（per-surface undo scope） | 每表面一条独立撤销/重做栈，快捷键只作用于活动表面 | 共用一条栈、跨表面撤销，不算 | 深度 50；不持久化；跨表面操作不做 | 表面 A 做 3 步→切 B 做 2 步→回 A 连按 3 次 `Ctrl+Z`，A 回到初始且 B 不变 | 本项目 §4.6；同类：Adobe 分文档撤销（待核实） |
| 画布单文档（single canvas document） | 两棵集合同住一个 `data.json`，共用一次原子保存 | 两个文件、两套主题、两套持久化，不算 | 本期合并 `canvas.json`；二进制资产存路径 | `git grep` 无第二处 `canvas.json` 写入；杀进程后重启数据不丢（原子写用例） | `src/Wantodo.Core/JsonBoardStore.cs:108-136`；AGENTS.md 硬规则 4 |
| 图片归属规则（image ownership） | 图片只存在于自由表面；结构化表面的「插入图片」= 切面 + 落位 | 结构化表面出现图片卡，不算 | 下拉面板不出现图片 | 点「插入图片」后 `activeSurfaceId=freeform` 且新对象在视口中心 ±20 DIP | 本项目 §4.7 |

### 5.2 R1 图片

| 名称 | 定义 | 反例 | 边界 | 验收 | 出处 |
|---|---|---|---|---|---|
| 无铬图片对象（chrome-less image） | 容器不画标题栏、边框、底色，图像即边界；仅选中时 1 px 强调描边 + 手柄 | 常态 1 px 描边、标题行、内边距形成「框」，都不算 | 悬停可出半透明元信息浮层（2 s 后淡出） | 与直接贴图对比无外框；该对象 XAML 无边框常量；选中态才有描边 | 本项目定义（无外部规范） |
| 剪贴板位图粘贴（clipboard bitmap paste） | `Ctrl+V` 时读 `Clipboard.GetImage()`，编码为 PNG 落盘后生成图片对象 | 把 base64 塞进 `data.json`，不算 | 只接位图与文件两种形态 | 截图后 `Ctrl+V`，1 s 内出现对象；`data.json` 不增大且含 `path` | `System.Windows.Clipboard`（WPF，API 文档） |
| 文件与路径粘贴（file paste） | `Ctrl+V` 剪贴板含文件路径或文本路径时，按扩展名判定为图片 | 文本不是路径也当图片，不算 | 支持 png/jpg/jpeg/gif/webp/bmp | 资源管理器复制图片后粘贴，落盘一次、无重复拷贝 | WPF `DataFormats.FileDrop` |
| 图片资产与体积（image asset policy） | 原图存 `board/canvas-images/<sha256 前 16 位>.<ext>`，同图去重；显示按尺寸解码 | 生成缩略图文件、每次粘贴都复制一份，不算 | 单张 ≤20 MB（超出拒绝并提示）；总量 >200 MB 提示清理 | 粘贴同一图两次 → 目录只有 1 个文件；50 张图内存增量 ≤8 MB | 本项目 §8 Q4 |
| 圆角裁切与手柄（rounded clip and handles） | 选中态按 8 DIP 圆角裁切 + 四角手柄；未选中不裁切 | 未选中也裁切、手柄常驻，不算 | 只做等比缩放；不做自由变形 | 选中时四角手柄可见且拖动等比；未选中截图无手柄 | 本项目 §3.3 矩阵 |

### 5.3 R2 主题 / 色板

| 名称 | 定义 | 反例 | 边界 | 验收 | 出处 |
|---|---|---|---|---|---|
| 主题（Theme） | 材质 × 形状 × 层级的规则集合，决定「怎么画」而非「是什么颜色」 | 只换颜色的「主题」、只改圆角的「主题」，都不算 | 本期只实现 liquidGlass | 换 Palette 后描边/圆角/阴影数值不变（截图对比 + 令牌断言） | Apple Liquid Glass（apple.com/newsroom 2025-06-09）；Mica（MS Learn） |
| 色板（Palette） | 一套具名颜色槽位集合，可替换而不改变几何 | 带圆角/阴影参数的「配色方案」，不算 | 7 套沿用 + custom 条件出现 | 切 7 套色板，几何参数逐项相等；对比度全部达标（正文 ≥4.5:1） | WCAG 2.2 SC 1.4.3（w3.org/TR/WCAG22） |
| 颜色键（color key） | 画板状态行上直接可点的色点，一点即换 Palette | 点了换主题、点了要开新窗口，都不算 | 设置页为主入口，状态行为快捷入口 | 连点 7 次，每次 <300 ms 完成且不重建窗口 | 本项目 §3.6 |
| 令牌跟随（token propagation） | 已生成卡片跟随换色，靠资源订阅而非重建 | 需要重启、需要重建控件、部分卡片不变，都不算 | 不追 WPF 动画目标属性之外的漏点 | 切色后 `rg` 证明 App 层无裸 hex；单测（XAML 键 ⊆ 写入键）通过 | 本项目 §3.2；`src/Wantodo.App/Appearance/ThemeService.cs:32-84` |

### 5.4 R3 工具栏

| 名称 | 定义 | 反例 | 边界 | 验收 | 出处 |
|---|---|---|---|---|---|
| 三层工具栏（three-tier toolbar） | ① 切换器行（可扩展）② 当前表面工具组 ③ 状态行（张数/缩放/撤销） | 三层挤一行、状态信息藏进右键菜单，不算 | 固定顶栏，不做浮动 | 三行高度合计 ≤88 DIP；收起态 12 DIP 仍可见第一行 | Apple HIG toolbars（待核实路径） |
| 结构化工具组 | 插入卡（记事/待办/计时）、搜索、视图（网格/对齐/排序） | 把画笔塞进结构化表面，不算 | 本期 3 组 | 每按钮有 tooltip + 快捷键提示 | 本项目 §4.3 |
| 自由工具组 | 选择、手形、文本、图片、矩形、线、箭头、画笔、橡皮、缩放 25–400% + 适应 | 缺橡皮、缩放宽域不足，不算 | 本期 10 个工具 | 逐个工具可用且互斥高亮；缩放两端可达 25%/400% | 参照图 B（用户提供的笔记应用工具条） |
| 快捷键表 | `Ctrl+1/2` 切面、`V/H/T/R/A/B/E` 选工具、`空格+拖` 平移、`Ctrl+Z/Y`、`Delete`、`F2` 改名 | 与系统输入法冲突未测，不算完成 | 单一来源常量表 + 设置页只读展示 | 在中文输入法激活时逐个按，无一被吞（人工验收） | 本项目 §5.4；WPF `InputBindings` |
| 预留行（reserved row） | 第一行右侧的 `＋`：容纳更多表面入口（日历/参考文件/图片库），不允许空白占位 | 放一个「更多…」空白按钮且无行为，不算 | 本期只有 2 个真实入口 | 点 `＋` 出现 3 项（日历/参考文件/图片库，标「未实现」），点击不崩 | 本项目 §4.2 |

### 5.5 R4–R7 交互修正

| 名称 | 定义 | 反例 | 边界 | 验收 | 出处 |
|---|---|---|---|---|---|
| 待办卡 = 栏目投影（live view） | 待办卡是桌面栏目的**只读投影 + 勾选/加条目的写入口**，不持有独立列表 | 卡片内自建条目、与桌面栏目不同步，不算 | 新建栏目复用桌面新建流程 | 桌面加一条 → 画板卡 1 s 内出现；卡内勾选 → 桌面同步勾选 | 本项目 §8 Q5 |
| 新建栏目（new column） | 与桌面「新建栏目」同一对话框、同一校验 | 画板里另写一套命名规则，不算 | 本期只改入口位置 | 两处新建后 `columns[]` 结构一致、可互相改名 | `src/Wantodo.Core/Board.cs:60` |
| 拖动与改名分离（drag vs rename） | 卡头按下 + 移动 >4 DIP 起拖；双击标题进改名；`F2` 同上 | 单击即编辑、拖拽顺带改名，都不算 | 改名 `Enter` 提交、`Esc` 取消 | 单击 100 次无一次进编辑；拖 5 px 就移动；双击进编辑且光标在标题末 | WPF `SystemParameters.MinimumHorizontalDragDistance`（MS Learn） |
| 画布平移手势（pan gesture） | 手形工具或 `空格+拖` 平移视口；选择工具下拖卡片是移动 | 选择工具下拖空白也平移（与框选冲突），不算 | 本期不做惯性滚动 | 两种手势各自生效且不串 | 本项目 §4.3 |
| 计时按钮与状态机（timer state machine） | 弹窗按钮「设置完成」；卡片按钮随状态为「开始/暂停/继续」；`Idle → Configured → Running ⇄ Paused → Done` | 两个同名「开始」、配置完自动开跑，都不算 | 每组结束提示一次 | 全状态遍历按钮文案正确；配置完不自动跑；暂停恢复不丢剩余秒 | 本项目 §8 Q（状态机）；`src/Wantodo.Core/PomodoroPlan.cs` |
| 分段番茄钟（segmented pomodoro） | 数据结构 `Segments: [{ repeat, focus, break }]`；时间轴是纯函数 `TimeSpanAt(segments, elapsed)` | 把「前几组短、后几组长」写成 if 分支硬编码，不算 | 简洁版 = 单段；自定义版最多 4 段 | 单测覆盖跨段、暂停恢复、极端值（0/超大）；同一输入得到同一剩余秒 | 本项目 §8 Q（PomodoroPlan）；`PomodoroPlan` 现有实现 |
| 简洁版与自定义互转（simple/custom conversion） | 简洁版写回时把 Segments 压成单段，**二次确认**后覆盖；自定义版存在时简洁版显示「已自定义」只读 | 静默覆盖自定义配置，不算 | 本期不做自动换算 | 自定义 3 段 → 打开简洁版 → 显示「已自定义」；确认覆盖后 3 段变 1 段且可撤销（内存栈） | 本项目 §8 |

### 5.6 待办主题（每条一行定义，本期不实现）

| 名称 | 定义 | 反例 | 边界 | 验收（实现期） | 出处 |
|---|---|---|---|---|---|
| 新粗野主义（Neubrutalism） | 2 px 深色描边、0 圆角、硬阴影（无模糊、固定偏移）、高对比强调色 | 圆角 >0、阴影带模糊，不算 | 待办；本期不进 Palette 组合 | 截图与定义逐项对齐；对比度达标 | 待核实（关键词：neubrutalism） |
| 便当网格（Bento grid） | **布局**：不等大小单元格拼块，不是视觉主题 | 把便当网格当 Theme/ Palette 实现，不算 | 待办；本期只登记为布局候选 | 布局算法单测（无重叠、可换行） | 待核实（关键词：bento grid） |
| 陶土拟物（Claymorphism） | 大圆角 + 双层内阴影模拟软陶体积感 | 只是「圆角大一点」，不算 | 待办 | 内阴影两向、无硬边 | 待核实（关键词：claymorphism） |
| 新拟物（Neumorphism） | 同一底色 + 双向阴影（亮/暗）形成凸起与凹陷 | 换了底色再加深阴影，不算 | 待办 | 同一底色的凸/凹两态可辨 | 待核实（关键词：neumorphism） |
| 动态排版（Kinetic typography） | 文字按时间轴做位移动画（WPF 需逐帧动画） | 只是「字号动一动」，不算 | 待办；成本单列 | 60 fps 下动画不掉帧 | en.wikipedia.org/wiki/Kinetic_typography |
| 高饱和色（Hyperchromatic） | 色板策略：多个高饱和色并置、低灰阶 | 只是「颜色更艳」，不算 | 待办；作为 Palette 策略而非 Theme | 对比度与色盲可辨性测试 | 待核实（关键词：hyperchromatic design） |

---

## 6 分期与依赖

| 阶段 | 任务 | 依赖 | 退出条件 |
|---|---|---|---|
| P0 | 数据模型 v3 + 迁移函数 + 单测 | 无 | 旧 `data.json` + `canvas.json` 能打开、有备份、有 WARN |
| P0 | Theme/Palette 拆分（`themeId`/`paletteId`）+ `ThemeResourceKeys` 闭环 | 迁移 | 键集合单测通过 |
| P0 | 单文件画布 + 两个常驻画布宿主 + 切换器 | 迁移 | V-04/V-05 通过 |
| P0 | 液态玻璃探针 → 材质实现 → Win10 降级 | 探针 | Win11 截图 + Win10 WARN 日志 |
| P1 | 自由表面工具组 + 笔迹 + 形状/箭头/文本 | P0 | 工具逐个可用 + 50 卡 500 笔 ≥55 fps |
| P1 | 无铬图片 + 剪贴板两形态 + 资产策略 | P0 | V-06/V-07 通过 |
| P1 | R4 待办投影、R5 拖动/改名、R6 计时状态机、R7 分段番茄钟 | P0 | 各自验收项通过 |
| P2 | 颜色键 UI、右键菜单、滚动条模板、空状态、预留行 | P1 | 矩阵覆盖项全部有实现 |
| P2 | 笔迹静态化、性能验收、包体积核算 | P1 | 预算三项全部达标或写明超标数字 |

**本期不做**：6 个待办主题（含便当网格布局）、跨表面撤销、>2 个表面、图层树/组合、图片裁切与滤镜、自由表面的搜索、惯性滚动、自定义色板编辑器（保留旧 custom 的读取与删除）。

---

## 7 验收清单

| 编号 | 验收项 | 命令 / 操作 | 阈值 / 现象 | 证据 |
|---|---|---|---|---|
| V-01 | 构建与单测 | `dotnet build Wantodo.slnx -warnaserror`；`dotnet test --no-build` | 0 警告 0 错误；全绿（≥317） | 输出贴进 `docs/verify/` |
| V-02 | 迁移：旧文件 | 放一份 `theme=dracula` + `formId=brutal` + 独立 `canvas.json` 的数据 | 打开后：`paletteId=mocha`、`themeId=liquidGlass`、卡片在、`canvas.imported-*.json` 在、`data.json.pre-070.bak` 在 | 日志 + `data.json` 片段 |
| V-03 | 迁移：坏值 | `theme=SkyNet`、`canvas.json` 截半 | 不崩；`paletteId=dark`；画布为空 + WARN | 日志 |
| V-04 | 切换不重建 | 连续切面 20 次 | 不丢卡片、不闪白、窗口句柄不变 | 录屏或日志 |
| V-05 | 单文件 | 运行后 `git grep` 仓库与数据目录 | 只有 `data.json` 一处写入；无第二处 `canvas.json` 写入 | grep 输出 |
| V-06 | 无铬图片 | 插入图片后截图对比 | 无外框；选中才出手柄 | 截图 `docs/verify/0.7.0-image.png` |
| V-07 | 剪贴板两形态 | 截图后 `Ctrl+V`；资源管理器复制文件后 `Ctrl+V` | 两种情况都生成对象；`canvas-images` 无重复文件 | 截图 + 目录列表 |
| V-08 | 令牌无裸 hex | `rg -n '#[0-9A-Fa-f]{6}\b' src/Wantodo.App` | 仅 `ThemeStyles.xaml` 图案定义命中 | 输出 |
| V-09 | 静态资源滥用 | 单测 `ThemeTokenUsageTests` | XAML 无 `{StaticResource SurfaceCard}` 等主题键 | 测试输出 |
| V-10 | 对比度 | 7 套 Palette × 正文/次要/强调 | 正文 ≥4.5:1，次要 ≥3:1，强调 ≥3:1 | 测试输出（沿用 `ThemeContrastTests`） |
| V-11 | 材质 | Win11 上看设置页背景；Win10 上看降级 | Win11 有桌面透出；Win10 纯色 + WARN；都不崩 | 截图 + 日志 |
| V-12 | 性能 | 50 卡 + 500 笔迹拖动 | 首帧 ≤1153 ms；private ≤105.1 MB；≥55 fps | 数字贴进 `docs/verify/` |
| V-13 | 撤销作用域 | A 表面 3 步、B 表面 2 步、回 A 撤销 3 次 | A 复原、B 不变 | 录屏或单测 |
| V-14 | 待办投影 | 桌面加条目 / 卡内勾选 | 双向 1 s 内同步 | 录屏 |
| V-15 | 计时状态机 | 遍历 5 态 | 按钮文案与禁用态正确；配置完不自动跑 | 录屏 |
| V-16 | 番茄钟分段 | 简洁版与自定义互转 | 二次确认；无静默覆盖 | 录屏 + 单测 |
| V-17 | 包体积 | `dotnet publish -r win-x64 --self-contained false` 后量 FDD payload 与 zip | 与 §8 Q10 的裁决一致 | 数字 + 结论 |
| V-18 | 需人肉眼确认 | 列表 | 液态玻璃观感、图片无铬观感、三层工具栏是否拥挤、色点是否好点、预留行是否像"空白占位" | `PENDING-HUMAN` |

---

## 8 待确认清单（10 条，每条给推荐 + 理由 + 备选）

| # | 问题 | 推荐 | 理由 | 备选 |
|---|---|---|---|---|
| Q1 | 画布数据是否合并进单个 `data.json`（R0.4 要求禁双文件） | 合并：`Version 3` + `canvas` 节点 + 旧 `canvas.json` 导入后改名保留 | 双文件 = 两次原子写、两种崩溃窗口、两套「保存失败」提示；合并后一次写盘、一个 bak | 保留双文件（明写为对 R0.4 的降价，并在验收里改成「两文件同时原子写」） |
| Q2 | 表面切换器形态与位置 | 顶栏第一行左侧分段控件（2 段 + 右侧 `＋`） | 互斥、常驻、一眼可见；tab 暗示多文档/可关闭，与「同一文档两表面」语义不符 | tab strip；或图标分页（更省宽度、可读性差） |
| Q3 | 工具栏形态 | 固定三层顶栏 | 面板可拖到 12 DIP，浮动工具条在收起态无处安放；固定顶栏命中区稳定 | 浮动 toolrail（Figma 式），成本更高、需要避让内容 |
| Q4 | 图片存储形态 | 原图文件 + 相对路径 + 按显示尺寸解码；不生成缩略图文件 | `data.json` 不能被二进制撑大（原子写会变慢）；解码尺寸足够省内存 | 原图 + 单独缩略图（多一层文件生命周期，收益仅列表场景）；base64 内嵌（否决） |
| Q5 | 待办卡是栏目投影还是独立列表 | 栏目投影（live view） | 独立列表会造成「桌面一套、画板一套」的数据分裂；投影天然同步 | 独立列表（灵活性高，但必须补双向同步与冲突解决，本期不划算） |
| Q6 | 本期 Palette 范围 | 沿用现有 7 套 + 条件出现 custom；不新增 | 已在用、已过对比度测试；新增色板要重新过对比度与许可 | 追加 Primer/Radix 各 2 套（+许可说明 + 对比度测试成本） |
| Q7 | 颜色键入口 | 设置页为主 + 画板状态行色点为快捷入口 | 状态行本来就在，不占新空间；设置页承载分组与自定义 | 只放设置页（少一个入口，切换慢）；或右键菜单（发现性差） |
| Q8 | 窗口级液态玻璃路径 | 先写探针（三种 API + 无 `AllowsTransparency` 的 WPF 合成），验证通过再实现 | AGENTS.md 硬规则 7：Windows 行为不做假设；Mica 在贴桌面窗口上是否生效未知 | 直接退到「卡片级半透明」，但那按 §3.4 定义不算液态玻璃（需要在计划里改名） |
| Q9 | 撤销栈深度 | 每表面 50 步 | 50 步覆盖一次卡片的典型编辑序列；闭包内存约 50×2×~1 KB | 100 步（内存翻倍，收益小）；20 步（0.7.0 验收里已提 20，偏少） |
| Q10 | 包体积红线 | 保持 0.6.0 红线 229,376 B，并在 P2 还清已超的 18,420 B | 红线是对外承诺；超了先还债再加功能，避免「越做越大」成为惯例 | 把红线调到 278,252 B（本期净增 30 KB 预算），需在 `progress.md` 显式记录放宽理由 |

---

## 附：交付前自检

- [x] 术语表 ≥24 条（索引 39 + 实现向 10，共 49），每条有出处或「待核实 + 关键词」
- [x] R0–R7 每条都有：专业命题 / 定义 / 反例 / 边界 / 验收 / 出处
- [x] 每个待办主题都有一行定义（含「便当网格是布局不是主题」）
- [x] 「概念废弃声明」写清旧定义错在哪（4 条）、旧数据怎么迁（9 行迁移表）
- [x] 控件 × 令牌矩阵覆盖 §6.3 全部条目（23 行）
- [x] 双表面的数据模型、状态、撤销作用域三件事都写死
- [x] 待确认清单 10 条（≤10），每条带推荐值 + 理由 + 备选
- [x] 全文没有把「默认」当决策依据
