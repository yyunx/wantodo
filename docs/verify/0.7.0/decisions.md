# 0.7.0 夜间执行：我替你做的决定

按提示词 A2 记录：凡是计划书未逐字规定、由执行者按推荐值自决的点。用户的 §1 裁决（Q1–Q10、A1–A8）不在此列。

| # | 决策点 | 选定 | 理由 | 改回成本 |
|---|---|---|---|---|
| D1 | `CanvasDocument` 版本号 | 新结构用 `2`，旧 `canvas.json` 视为 `1` | 旧文件已占用 1；用 `Version < 2` 判定旧结构，迁移路径无歧义 | 低：改常量 + 一处判断 |
| D2 | 图元（`Objects`）与卡片（`Cards`）的分界 | `kind=image` 进 `Objects`，其余（note/todo/timer/link）进 `Cards` | 图片属于自由表面素材，卡片属于结构化表面投影；P1.3 起笔迹/图形也进 `Objects` | 中：影响 P1.2/P1.3 数据形态 |
| D3 | 旧画布导入时机 | `Load()` 三分支都尝试，导入成功立即 `Save` | 避免每次启动重复导入；坏文件时也不会把旧画布丢掉 | 低 |
| D4 | 两边都有画布内容时 | 以 `data.json` 为准，旧文件改名保留，记 WARN | Q1 已裁决「以 data.json 为准」（迁移表第 9 行） | 低 |
| D5 | 升级前备份名 | `data.json.pre-070.bak`（只生成一次，存在跳过） | 计划书 V-02 明文要求该文件名 | 低 |
| D6 | `Items` 兼容别名 | 保留 `[JsonIgnore] Items => Cards`，P0.3 删 | 让 `CanvasBoard` 在 P0.1 不必同步大改，减少夹带 | 低：P0.3 一并清理 |
| D7 | App 层剩余裸 hex 的处理 | 允许三类例外并登记：`App.xaml` 的默认回退刷子、`ThemeColorPicker` 的 HSV 渐变、`ThemeStyles.xaml` 的装饰纹理 | 前两类是「主题应用前」的兜底与选择器自身的画布，第三类是几何纹理不是主题色；P2.1 若有余力再把纹理改成令牌 | 低：换成令牌需要新增 3 个资源键 |
| D8 | 迁移后是否立即落盘 | 是：`Load()` 里 `upgraded` 为真就 `Save` | V-02 要求「打开一次后 `data.json` 里有 paletteId」；不落盘则每次启动都重新迁移、诊断反复 | 低 |
| D9 | 旧 `formId` 的落点 | 全部折成 `liquidGlass`，`solid` 只保留常量不启用 | 计划书 §1.4 迁移表：formId 丢弃 + WARN；本期只交付一种材质 | 中：启用 solid 要补设置项与色板取值 |
| D10 | `CanvasStore` 的去留 | 整个类删除，`CanvasBoard` 改成接收 `Action<CanvasDocument>` 回调 | 单文件后独立读写器只会诱发「又在写 canvas.json」；回调让宿主自己决定何时合并保存（600 ms 去抖） | 低：类是自包含的，可从 git 历史取回 |
| D11 | 撤销时 `_document` 被整份替换 | 回调里把新实例写回 `Board.Canvas` 再请求保存 | 不做就地拷贝，避免「两份文档实例」；代价是 `Board.Canvas` 引用会变，凡缓存旧引用的地方都要重新取 | 低 |
| D12 | `DataPaths.CanvasFile` | 删除，并加单测断言该属性不存在 | 光删调用点不够，留着属性迟早被重新用上 | 低 |
| D13 | 自由表面宿主在 P0.4 的渲染范围 | 只渲染图片与文本两种图元，笔迹/图形先画边框占位 | 笔迹/图形属 P1.2；先留宿主与数据通路，避免 P0 就长出一个半成品绘图工具 | 低 |
| D14 | 分段控件里 `＋` 的行为 | 保留按钮（Q2 要求），点击只记日志 + tooltip；不新建表面 | 本期只有两个表面，凭空造第三个会让 `Cards`/`Objects` 的「表面」语义不清 | 低 |
| D15 | 表面切换是否重建控件 | 不重建，两个宿主常驻，只切 `Visibility` | V-04 要求「不闪白、句柄不变」；重建会让滚动位置与撤销栈一起丢 | 中：改回重建要处理状态迁移 |
| D16 | 材质在 P0.5 的作用范围 | 只给 `SettingsWindow`（Win11 上真实 Mica）；`TopPanelWindow` 本步不启用 | 面板贴桌面层且自己管拖拽/缩放/命中测试，材质透出后的观感（12 DIP 条、DWM 圆角、卡片对比度）必须人眼看过；验收 V-11 指定的就是设置页 | 低：面板加两行 `GlassSurface.Prepare/TryApply` 即可，P2.1 再做 |
| D17 | 让 Mica 透出的前置条件 | 必须 `WindowChrome.GlassFrameThickness=-1` + `Window.Background=null`，内容层自己带 α0.72/α0.86 底色 | 探针实测：只调 `DwmSetWindowAttribute` 时 mica 与 none 逐像素相同（hr=0 但不可见）；背景置空后可见，且控件照常可点 | 低：条件集中在 `GlassSurface` |
| D18 | 材质不可用时的画面 | 内容层退回不透明 `WindowBackground`/`CardBackground`，并记 WARN | 半透明内容叠在不透明窗口上会被误当成玻璃；探针也不支持 Win10 Acrylic（未文档化 API、无真机） | 低 |
