# wantodo 术语对照表（0.7.0）

> 用途：把口语需求翻成可审阅的专业术语。每条含**一句话定义**、**出处**、**本项目对应物**。
> 出处凡标「待核实 + 关键词」的，表示写这份文档时没有联网确认到规范页，实现前必须补查；不要在实现里先按猜测写。
> 定义里出现的「不算」以 `docs/plan-0.7.0.md` §5 的六列表为准（那里有反例与验收）。

## 1 材质（Material）

| 术语 | 英文原名 | 一句话定义 | 出处 | 本项目对应物 |
|---|---|---|---|---|
| 云母材质 | Mica | 由 DWM 绘制、只采样一次桌面壁纸的不透明动态材质，贴在长驻窗口背景，随系统主题变深浅 | [MS Learn：Mica material](https://learn.microsoft.com/en-us/windows/apps/design/style/mica) | 窗口级 L0 材质（Win11） |
| 云母变体 | Mica Alt | Mica 的壁纸着色更强版本，常用于带标签页标题栏的窗口 | 同上，`DWMSBT_TABBEDWINDOW` | 本期不用（无标签页标题栏） |
| 亚克力材质 | Acrylic | 对身后内容做实时模糊与着色的半透明材质（桌面 Acrylic 为最亮变体） | [MS Learn：Acrylic material](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic) | 窗口级 L0 材质（Win10 1803+ 档） |
| 系统背景材质 | system backdrop | DWM 按窗口类型在窗口背后绘制的材质类型枚举 | [MS Learn：DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type) | `DWMWA_SYSTEMBACKDROP_TYPE`(38) |
| 背景采样模糊 | backdrop blur | 对窗口后方像素取样后模糊、再把内容画在其上的效果 | 同上（Mica/Acrylic 的能力范围） | 只做窗口级；卡片级不做（WPF 无逐元素 backdrop-filter） |
| 液态玻璃 | Liquid Glass | Apple 2025 年公布的半透明材质：折射周围内容、随内容与明暗环境变色、有高光反射并可随交互变形 | [Apple Newsroom 2025-06-09](https://www.apple.com/newsroom/2025/06/apple-introduces-a-delightful-and-elegant-new-software-design/)；[HIG：Materials](https://developer.apple.com/design/human-interface-guidelines/materials) | 0.7.0 唯一 Theme 的命名与目标观感 |
| 玻璃拟态 | glassmorphism | 用半透明面 + 模糊 + 细高光边模拟玻璃的**风格**，不要求真实背景采样 | 待核实（关键词：glassmorphism backdrop-filter） | 液态玻璃的近似风格；**不等于**液态玻璃 |
| 高光反射 | specular highlight | 材质表面在光源方向出现的亮线/亮点，用来表达「这是玻璃」 | Apple Liquid Glass 描述（同上） | 卡片顶边 1 px 亮线 + 顶部轻渐变 |
| 内高光细线 | 1px inner highlight | 元素内侧顶部 1 px 亮边，制造厚度与边缘感 | 待核实（关键词：1px inner highlight hairline border） | `ThemeStrokeBrush` 用于卡片顶边 |
| 硬阴影 | hard shadow | 无模糊、固定偏移、纯色的投影（新粗野主义常用） | 待核实（关键词：hard shadow neubrutalism） | 待办主题 Neubrutalism 的投影形态 |

## 2 风格与图案（Style / Pattern）

| 术语 | 英文原名 | 一句话定义 | 出处 | 本项目对应物 |
|---|---|---|---|---|
| 新粗野主义 | Neubrutalism | 2 px 深色描边、零圆角、硬阴影、高对比强调色的界面风格 | 待核实（关键词：neubrutalism） | 待办主题（本期不做，块 D 参数近似） |
| 便当网格 | Bento grid | 用不等大小单元格拼块的**布局**方式，不是视觉主题 | 待核实（关键词：bento grid） | 待办布局候选；明确**不是** Theme |
| 陶土拟物 | Claymorphism | 大圆角 + 双向内阴影，模拟软陶的圆润体积感 | 待核实（关键词：claymorphism） | 待办主题（本期不做） |
| 新拟物 | Neumorphism | 与底色同色、用一明一暗两道阴影表达凸起/凹陷 | 待核实（关键词：neumorphism） | 待办主题（本期不做） |
| 动态排版 | Kinetic typography | 文字随时间做位移/变形/字重变化的排版动画 | [Wikipedia：Kinetic typography](https://en.wikipedia.org/wiki/Kinetic_typography) | 待办主题；WPF 需逐帧动画，成本单列 |
| 高饱和色 | Hyperchromatic | 以色相饱和度为表达手段、同时并置多个高饱和色的配色策略 | 待核实（关键词：hyperchromatic design） | 待办主题（作为 Palette 策略） |
| 半调网点 | halftone | 用规则网点密度表现明暗的印刷图案 | [Wikipedia：Halftone](https://en.wikipedia.org/wiki/Halftone) | 待办主题的图案素材 |
| 颗粒噪点 | grain / noise | 低频随机噪点纹理，用来削弱纯色面的「塑料感」 | [Wikipedia：Film grain](https://en.wikipedia.org/wiki/Film_grain) | 待办主题的图案素材 |
| 排线阴影 | hachure | 平行排线表示阴影或材质的图案 | [Wikipedia：Hatching](https://en.wikipedia.org/wiki/Hatching) | 待办主题的图案素材 |
| 点阵网格 | dot grid | 规则点阵背景，用作绘图画布的对齐参照 | 待核实（关键词：dot grid pattern） | 自由表面背景网格候选 |

## 3 色彩（Color）

| 术语 | 英文原名 | 一句话定义 | 出处 | 本项目对应物 |
|---|---|---|---|---|
| 设计令牌 | design token | 把颜色/字号/间距等决策抽成具名变量的做法，实现与设计共享同一份值 | W3C Design Tokens 格式草案（待核实版本号：tr.designtokens.org/format） | `ThemeService` 写入 `Application.Resources` 的键 |
| 语义令牌 | semantic token | 按**用途**而非色值命名的令牌（`SurfaceCard` 而非 `gray900`） | 同上 | 12 个颜色槽位 + 2 个数值 |
| 色阶 | color ramp | 同一色相从浅到深的连续档位集合 | 待核实（关键词：color ramp tone scale） | 候选 Palette 的取用方式 |
| 色调色板 | tonal palette | 由种子色算法生成的 13 档色阶集合（Material 3） | 待核实（m3.material.io，关键词：tonal palette） | 候选 Palette 的取用方式 |
| 层级刻度 | elevation scale | 用阴影与表面亮度表达的层级阶梯（0/1/2…） | 待核实（m2.material.io，关键词：elevation scale） | `ThemeElevation` + `ThemeShadowOffset` |
| 对比度 | contrast ratio | 两色相对亮度的比值，WCAG 要求正文 ≥4.5:1、大字/图形 ≥3:1 | [WCAG 2.2 SC 1.4.3](https://www.w3.org/TR/WCAG22/#contrast-minimum) | `ThemeDerivation.EnsureContrast` 的门限 |
| 色板 | palette | 一套可整体替换的颜色槽位取值集合 | 同上（语义令牌的组合） | 7 套 + 条件出现的 custom |

## 4 画布与表面（Canvas / Surface）

| 术语 | 英文原名 | 一句话定义 | 出处 | 本项目对应物 |
|---|---|---|---|---|
| 多表面画布 | multi-surface canvas | 同一文档内按语义划分多个绘制层，共享受控能力、各自持有内容与视图状态 | 待核实（关键词：tldraw pages / Figma pages） | 0.7.0 的核心结构 |
| 结构化表面 | structured surface | 承载**有语义状态**对象的画布层（对象可回写数据仓库） | 本项目定义（无外部规范） | 记事/待办/计时/链接卡 |
| 自由表面 | freeform surface | 承载**无语义状态**图元的画布层（只有几何与样式） | 本项目定义（无外部规范） | 图片/笔迹/形状/自由文本 |
| 画布视口 | viewport | 画布坐标到屏幕坐标的变换（缩放 + 平移），每表面独立 | 待核实（关键词：viewport culling canvas） | `viewports.structured/freeform` |
| 表面切换器 | surface switcher | 在多个表面间互斥切换的控件 | Apple HIG segmented controls（待核实页面路径）；Material segmented buttons（待核实路径） | 顶栏第一行分段控件 |
| 画布文档 | canvas document | 一次持久化写入所覆盖的完整画布数据 | 本项目定义；`src/Wantodo.Core/JsonBoardStore.cs:108` | 单个 `data.json` 的 `canvas` 节点 |
| 无铬 | chrome-less | 对象不画容器（无标题栏、无边框、无底色），内容自身构成边界 | 本项目定义（无外部规范） | 图片对象 |
| 笔迹 | stroke | 一串采样点构成的连续线条，是自由表面的基本图元 | WPF `StrokeCollection`（API 文档） | `objects[kind=stroke]`，ISF 落盘 |
| 图元 | primitive | 自由表面上不可再分的基本形状（图片/笔迹/矩形/线/箭头/文本） | 本项目定义 | `objects[]` 的 `kind` |

## 5 交互（Interaction）

| 术语 | 英文原名 | 一句话定义 | 出处 | 本项目对应物 |
|---|---|---|---|---|
| 分段控件 | segmented control | 一组互斥选项横向并排、只有一个高亮的控件，用于切换同级视图 | Apple HIG（待核实页面路径）；Material 3 segmented buttons（待核实路径） | 表面切换器、计时预设选择 |
| 工具栏 | toolbar | 常驻于窗口顶部/侧边、承载当前上下文命令的控件组 | Apple HIG toolbars（待核实页面路径） | 顶栏三层 |
| 浮动工具条 | floating toolrail | 浮在内容上方、可移动的窄工具条（Figma 式） | 待核实（关键词：floating toolrail） | 备选形态，本期不采用 |
| 作用域撤销 | per-scope undo | 每个文档/表面各自维护撤销栈，命令只作用于当前作用域 | Apple HIG undo and redo（待核实页面路径） | 每表面 50 步 |
| 行内改名 | inline rename | 在对象原位进入文字编辑，而不是弹对话框 | 待核实（关键词：inline rename pattern） | 双击卡片标题 / `F2` |
| 拖拽把手 | drag handle | 明确标示「此处可拖」的抓取区域 | [WCAG 2.2 SC 2.5.7 Dragging Movements](https://www.w3.org/TR/WCAG22/#dragging-movements) | 卡头、面板顶部条 |
| 可供性 | affordance | 物体外观暗示其可进行的操作 | Norman,《The Design of Everyday Things》（1988/2013） | 手柄、悬停浮层、光标形状 |
| 空状态 | empty state | 内容为空时展示的引导（文案 + 主操作），避免空白页 | 待核实（关键词：empty state pattern） | 每表面一句文案 + 一个引导按钮 |
| 状态机 | state machine | 由有限状态与迁移条件构成的模型，同一时刻只处于一个状态 | [Wikipedia：Finite-state machine](https://en.wikipedia.org/wiki/Finite-state_machine) | 计时器 Idle/Configured/Running/Paused/Done |
| 拖拽阈值 | drag threshold | 按下后移动超过该距离才判定为拖拽，避免误触 | WPF `SystemParameters.MinimumHorizontalDragDistance`（MS Learn） | 4 DIP 起拖 |
| 命中测试 | hit testing | 判断某个屏幕点落在哪个可视元素上的过程 | [MS Learn：WPF hit testing](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/hit-testing-in-the-visual-layer) | `PointFromScreen` / `HitTestHandle` |
| 焦点 | keyboard focus | 当前接收键盘输入的控件，同一窗口内唯一 | WPF `FocusManager`（API 文档） | 切面后落到画布宿主 |
| 视图状态持久化 | view-state persistence | 把缩放/平移等视图参数与内容一起保存，重启后复原 | 本项目定义 | `viewports` 节点 |

## 维护规则

1. 新增术语必须同时给**出处或「待核实 + 关键词」**，禁止只写名字。
2. 术语定义变化 → 同步 `docs/plan-0.7.0.md` §5 的六列表（反例与验收在那里）。
3. 标「待核实」的条目在对应阶段开工前必须补齐出处，并在 `docs/verify/` 留链接。
