# v6 块 D — 形式层（palette × form）第一轮：机制 + 新粗野 + 玻璃/霓虹参数

日期：2026-11-15　块：D　依赖：块 C（已提交 `d0f4f2b`）

## 1. 交付物

| 变更 | 文件 | 要点 |
|---|---|---|
| 形式 id | `src/Wantodo.Core/ThemePalette.cs` | `FormFlat/FormBrutal/FormGlass/FormNeon/FormClay/FormNeumorph`、`FormId`、`ParseForm`（未知→flat） |
| 形式参数 | 同上 | `ThemeEffects` 增加 `CardAlpha`、`ShadowOffset`；`EffectsFor` 给 6 种形式各自的描边/圆角/阴影/透明度 |
| 持久化 | `src/Wantodo.Core/Board.cs` | `FormId`（默认 `flat`），`NormalizePanel()` 归一化未知值 |
| 资源发布 | `src/Wantodo.App/Appearance/ThemeService.cs` | 新增 `ThemeCardBorderBrush/Thickness/CornerRadius`、`ThemeShadowBrush/Offset`、`ThemeCardAlpha`；`SurfaceCard` 按形式带 alpha |
| 面板硬阴影 | `src/Wantodo.App/Controls/BoardLayoutHost.cs` | 每栏内容垫一块 `ThemeShadowBrush` 色块，`TranslateTransform(4,4)`；flat 透明不可见 |
| 卡片形式 | `src/Wantodo.App/Controls/CanvasBoard.cs` | 卡片圆角/常态描边走 `ThemeCard*` 资源；选中仍是 2 px `BorderActive`；图片卡保持无框 |
| 设置入口 | `SettingsWindow.xaml(.cs)` | 外观区加「形式」下拉（扁平/新粗野/液态玻璃/霓虹/黏土/新拟态）；旧「主题效果」勾选框改为隐藏的兼容字段 |
| 视图模型 | `src/Wantodo.App/ViewModels/BoardViewModel.cs` | `SetForm(string)`：归一化、写 `FormId`、同步 `ThemeEffects`、刷新外观并落盘 |

## 2. 命令与输出

```
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -warnaserror
    0 个警告
    0 个错误
    （2.49s）

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test --no-build
已通过! - 失败: 0，通过: 317，已跳过: 0，总计: 317，持续时间: 517 ms
```

新增测试：`tests/Wantodo.Core.Tests/ThemeFormTests.cs`（10 组 ParseForm Theory、6 组 FormId 往返、
brutal 参数、glass/flat 参数、Board 默认与归一化、旧文件无 formId）；`JsonBoardStoreTests` 的往返用例增加 `formId=brutal`。

## 3. 效果与数字（Debug 构建，SC 基线包同机）

| 形式 | 首帧（单次） | 60 s private | WS | 线程 |
|---|---|---|---|---|
| flat（默认） | 1098 ms | 102.1 MB | 140.9 MB | 11 |
| brutal | 1081 ms | 103.2 MB | 140.8 MB | 11 |

brutal 增量：private **+1.1 MB**（红线 <3 MB），首帧无退化。玻璃/霓虹/黏土/新拟态只改了参数，未单独测。
面板与卡片上没有新增图片资源（阴影是纯色块，0 字节）。

## 4. 各形式当前呈现

- `flat`：与块 C 完全一致（描边 0、圆角 10、无阴影）。
- `brutal`：2 px `#0F1014` 描边、圆角 0、硬阴影（偏移 4 DIP 的纯黑 65% 色块，不用 `DropShadowEffect`）。
  栏与画布卡片都生效；图片卡保持零边框。
- `glass`：`SurfaceCard` 带 0.72 alpha、1 px 半透明白描边、圆角 12、浅阴影。
  没有接 DWM Acrylic/Mica，也没有 Win10 降级分支——面板/卡片是"半透明色块"，不是真玻璃。**欠账**。
- `neon`：1 px 强调色描边、圆角 4、45% 阴影；发光字（双层错位）**未实现**，`Glow` 令牌只被发布、未被绘制。**欠账**。
- `clay` / `neumorph`：只给了圆角/阴影参数，没有内外高光层、没有双阴影，**视觉上接近 flat**。**欠账**。

## 5. 手动验收（需人肉眼确认）

1. 设置页「形式」选「新粗野」：主窗口各栏与卡片出现 2 px 深色描边、直角、右下硬阴影；阴影不是模糊的。
2. 在「新粗野」下切到「浅色」色板：描边仍是深色、文字仍可读，卡片面不再发暗。
3. 选「液态玻璃」：卡片面能透出画布底色（半透明），文字清晰；Windows 10 上不会出现黑框或透明窗口（无 `AllowsTransparency`）。
4. 重启程序：形式保持（`E:\Wantodo\board\data.json` 里 `formId` 为选中的小写 id）；旧文件没有 `formId` 时回到扁平。
5. 截图存 `docs/verify/block-d-brutal.png`、`block-d-glass.png` 后贴回（当前未截，标 PENDING-HUMAN）。

失败长什么样：切形式没反应；阴影是模糊的（说明走了 DropShadowEffect）；浅色主题下卡片仍是暗块；
重启回默认；设置页下拉空白。

## 6. 已知欠账

- 玻璃没有 DWM Acrylic/Mica 与 Win10 降级；`Interop/` 里已有圆角封装，可复用但没有做窗口级材质。
- 霓虹发光字（双层 `TextBlock` 错位 / Blur 复制层）未实现；`Glow` 令牌目前无消费者。
- 黏土/新拟态只有参数，没有高光层与双阴影。
- 画布卡片的硬阴影只做了"描边/圆角"，阴影块只加在栏（`BoardLayoutHost`）上，卡片在画布上仍是平面。
- `ThemeEffects` 旧字段仍作为兼容位（`ThemeEffects==false` 等价 flat），后续可删。
- 阴影偏移固定 4 DIP（`TranslateTransform` 不吃动态资源），玻璃的 2 DIP 偏移没有生效。

## 7. 下一步第一条命令

按 v6 §6：补 `glass` 的 DWM 分支（Win10 降级），再给 `neon` 做双层文字，最后视时间决定 clay/neumorph。

## 4.1 霓虹 wordmark（同日补）

`src/Wantodo.App/Windows/TopPanelWindow.xaml` 的「便签」标题改为三层：Glow 色偏移 (-1,0)、Accent 色偏移 (+1,0)、TextPrimary 正层；
后两层显隐由 `ThemeGlowVisible` 控制（`ThemeService` 只在 `form=neon` 时设 Visible）。
实测：neon 形式启动首帧 1078 ms、private 112 MB（单次，6 s 采样），无异常日志；正式 60 s 采样未做。
欠账：只给 wordmark 做了发光，计时数字/标题未接；`BlurEffect` 复制层未用（当前是零效果层方案）。
## 8. 包体积（2026-11-15，FDD Release 发布复测）

`dotnet publish -c Release -r win-x64 --self-contained false -o publish\fdd-0.7.0-dev` → 5 文件、payload **1,616,223 B**；
zip（排除 pdb）**247,796 B**。对照 0.6.0 基线 1,556,831 B / 199,765 B：payload **+59,392 B**，zip **+48,031 B**，
超过 v6 的 FDD 红线 229,376 B（超 18,420 B）。原因主要是 Phase 24 画布（CanvasBoard 约 1,350 行）与块 C 的 7 套色板/迁移表；
本块的形式层只加代码，没有二进制素材。**欠账：发布前需要一次减重**（候选：删无入口的 ThemeColorPicker、旧 BuildImageBody、
ThemeStyles 纹理若未使用则删）。