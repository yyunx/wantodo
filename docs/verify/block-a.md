# 块 A：画板高度拖拽可逆（2026-11-15）

## 1. 交付物

| 文件 | 改动 |
|---|---|
| `src/Wantodo.Core/PanelGeometry.cs` | `MinRatio` 0.4→0.2、`MaxRatio` 1.0→0.9；新增 `ClampHeight` / `HeightRatio`；`ExpandedSize`、`RatiosFromSize`、`Resize` 统一走它们 |
| `src/Wantodo.App/Windows/TopPanelWindow.xaml.cs` | 拖动高度改为「鼠标 Y 换算 DIP 减顶边」；`EndDrag` 持久化松手高度；`ClampCanvasHeight` 成为唯一写 `Height` 的入口；`[画板高度]` 日志 |
| `tests/Wantodo.Core.Tests/PanelGeometryTests.cs` | 新增可逆、下限 240、90% 上限、比例往返 4 例；修正 2 条旧断言 |
| `tests/Wantodo.Core.Tests/JsonBoardStoreTests.cs` | 非法比例夹取改为 0.2/0.9 |

## 2. 根因（三条假设逐条验证）

1. `PanelGeometry.Resize` 的高度下限是 `max(MinHeight 240, workArea.Height × MinRatio 0.4)`；1067 DIP 工作区上等于 426.8 ≈ 半屏，所以往上拖到一半就顶住。
2. `EndResize` 把 426.8 存成 `heightRatio=0.4`，`Expand()` 又只按这个比例展开；把手拖动超过 40% 后仍 `Expand()` 回 0.4，于是「后面一直是一半，改不回来」。
3. 旧 `DragTo` 用累计位移叠加，`EndDrag` 又从不写回拖动高度，150% 下偏差被放大。

## 3. 命令与输出

```
dotnet build -warnaserror
已成功生成。 0 个警告 0 个错误（6.88s）

dotnet test --no-build
已通过! - 失败: 0，通过: 265，已跳过: 0，总计: 265，持续时间: 534 ms
```

新增单测（`PanelGeometryTests`）：

- `Resize_BottomEdge_SequenceIsReversible`：1000 → 500 → 1000。
- `ClampHeight_BelowMinimum_UsesMinHeight`：100 / 0 / −50 → 240。
- `ClampHeight_TallerThanNinetyPercent_UsesWorkAreaCap`：5000 / +∞ → 0.9×工作区。
- `HeightRatio_RoundTripsThroughExpandedSize`：240/500/1000/1400 → 比例 → 展开高度误差 ≤0.5 DIP。
- `JsonBoardStoreTests.Load_ClampsIllegalPanelRatios`：`9`/`−3` → 0.9 / 0.2。

## 4. 日志（待人工拖动补录）

拖动开始/移动/结束由 `AppLog.Info` 写入 `%LOCALAPPDATA%\wantodo\logs\app-*.log`，格式：

```
[画板高度] begin Height=... ActualHeight=... Min=... Max=... dpi=... persisted=0 dragging=True
[画板高度] move#1 ...
[画板高度] end ... persisted=1 dragging=False
```

`persisted` 只在 `EndDrag` 里 +1，拖拽过程中必须一直是 0。

## 5. 需人肉眼确认

1. 撞顶拉出把手，往下拖到约半屏松手 → 面板停在半屏；再按住把手往下拖更多 → **能继续变高**，不会弹回半屏。
2. 拖底边往上→停在中途松开→再拖回去 → 高度可任意恢复；重启后保持。
3. 拖到接近顶部（12 DIP 内）松手 → 直接收起，不会存下半屏比例。

失败长什么样：把手拖动松手后高度弹回同一个值；重启后高度固定不变；日志里拖拽过程中出现 `persisted>0`。

## 6. 数据修复

`E:\Wantodo\board\data.json` 里被写坏的 `panelHeightRatio: 0.4` 已改回 `0.75`，备份 `data.json.bak-block-a`。

## 7. 已知限制

- 高度按屏幕绝对 Y 换算，仅在主屏单显示器场景有数据；多显示器/负坐标屏幕未验证。
- 收起态 12 DIP 仍直接赋 `Height`（不走 `ClampCanvasHeight`），这是收起动画的起点，不是拖拽路径。
