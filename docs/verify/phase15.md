# Phase 15 验收记录 — 自定义取色（T1b）

结论：**自动项通过**（构建 0 警告、测试 205 通过）；三条肉眼项待用户在带本次改动的 Debug 构建上确认（第 5 节）。

## 1. 交付物

| 文件 | 改动 |
| --- | --- |
| `src/Wantodo.Core/HsvColor.cs` | 新增：HSV ↔ RGB 互转（取色器与单测共用） |
| `src/Wantodo.Core/ThemeDerivation.cs` | 新增 `EnsureReadableBackground`、`PushAway`；`EnsureContrast` 的推色方向改为「黑白中对比更高的一端」 |
| `src/Wantodo.Core/ThemePalette.cs` | 新增 `NormalizeHex`、公开 `TryParseHex` |
| `src/Wantodo.Core/Board.cs` | 新增可空 `CustomBackground`、`CustomAccent` |
| `src/Wantodo.App/Appearance/ThemeService.cs` | `Apply(Board)` 把两个自定义色传给 `ThemePalette.For` |
| `src/Wantodo.App/ViewModels/BoardViewModel.cs` | 新增 `SetCustomTheme`（规整 hex、非法值记 WARN、`Theme=Custom`） |
| `src/Wantodo.App/Dialogs/ThemeColorPicker.xaml(.cs)` | 新增取色对话框（两个选择框 + 色相条 + S/V 方块 + hex + 预览 + 5 组对比度） |
| `src/Wantodo.App/SettingsWindow.xaml(.cs)` | 外观区加「自定义…」按钮与当前两色摘要；`LoadCurrentValues` 支持 Custom |
| `tests/Wantodo.Core.Tests/HsvColorTests.cs` | 新增 11 例 |
| `tests/Wantodo.Core.Tests/ThemeContrastTests.cs` | 新增 3 例（纯白组合、200 组随机不变量、色调只动背景） |
| `tests/Wantodo.Core.Tests/JsonBoardStoreTests.cs` | 新增 1 例（自定义色存读 + 旧文件缺字段读出 null） |
| `docs/...` | FRD T1 交互步骤、视觉文档 1.3 推导步骤、plan Phase 15 勾选与「主题形式感」未排期条目 |

## 2. 命令与输出

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -warnaserror
# 已成功生成。0 个警告 / 0 个错误（3.07s）

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test --no-build
# 已通过! - 失败: 0，通过: 205，已跳过: 0，总计: 205（540ms）
```

新增 19 例（186 → 205）：`HsvColorTests` 11 例、`ThemeContrastTests` 3 例、`JsonBoardStoreTests` 1 例，其余为既有用例。

## 3. 机制

1. 取色器只用 WPF 原生控件：两个色块（背景 / 强调）+ `Slider` 色相条 + 自绘渐变 `Border` 做 S/V 方块 +
   `TextBox` hex + 预览卡片 + 5 组比值。拖动/点击 S/V 方块、拖色相条、敲 hex 三条路径都能改色；
   选择框用 2px `Accent` 描边标出当前编辑项。
2. 「应用」把两色交给 `BoardViewModel.SetCustomTheme`：`ThemePalette.NormalizeHex` 规整为 `#RRGGBB`，
   非法或空值记 `AppLog.Warn` 并按深色回退；随后 `Theme=Custom` 落盘。
3. 推导在 `ThemeDerivation.DeriveFrom`：先 `EnsureReadableBackground`（中灰背景压暗/提亮到可读侧）→
   卡片按浅/深方向混色 → `PushAway` 保证卡片上正文 ≥7:1 → 次要/弱化各混 30%/50% 再校正 4.5 →
   强调 `≥3:1`、`OnAccent` 黑白取优 → `Hover`/`Selected` 按 10%/22% 叠色。

## 4. 自动能验证的部分

| # | 做了什么 | 结果 |
| --- | --- | --- |
| 1 | 纯白背景 + 纯白强调 | 正文推成深色，对窗口底、卡片底均 ≥7:1（`ForCustom_WhiteOnWhite_StillReadable`） |
| 2 | 200 组随机（背景, 强调） | 正文 ≥7 / 次要 ≥4.5 / 弱化 ≥4.5 / OnAccent ≥4.5 全部成立（`ForCustom_TwoHundredRandomPairs_KeepInvariants`） |
| 3 | 单组推导耗时 | 200 组平均 <5 ms（断言在用例内） |
| 4 | 0.5.0 文件无两个新字段 | 读出 `null`，不抛错；存读往返一致（`CustomColors_RoundTripAndMissingDefaultsToNull`） |
| 5 | HSV 往返 | 200 组随机色误差 ≤1；6 个主色相、白/黑、满饱和都在范围内 |

### 4.1 首轮失败与修复（留证据）

第一次跑随机组用例时失败：`背景 #BB5125 上正文 4.86`。原因是中灰背景上黑白文字都到不了 7:1，
`EnsureContrast` 只会推前景，永远达不到。修复分三步：

1. `DeriveFrom` 先调 `EnsureReadableBackground`：把背景沿更近的一侧（压暗到 L≤0.10 或提亮到 L≥0.30）
   移动，保持色相；纯白/纯黑背景不触发。
2. `EnsureContrast` 的推色方向从「按前景亮度」改为「黑白中对比更高的一端」——此前浅色正文在中灰底上
   会往白推，越推越差。
3. 新增 `PushAway`：卡片底让正文掉到 7:1 以下时，把卡片往文字反方向挪，而不是继续改文字。

修完后再跑：`通过: 205 / 失败: 0`。

## 5. 需人肉眼确认（未勾）

### 5.1 纯白背景 + 纯白强调仍可读、重启一致

1. 设置 → 外观 → 点「自定义…」。
2. 背景与强调都填 `#FFFFFF`，点「应用」。
3. 期望：主窗口变成浅色底、正文为深色，能正常阅读；把鼠标放到标题/正文上仍看得清。
4. 关掉应用再启动，期望外观不变（`Theme=Custom` 与两个颜色已落盘）。
5. 失败长什么样：白底上出现浅灰/白字；或重启后回到 GitHub 深色。

### 5.2 极端中间色不出现「灰底灰字」

1. 在取色器把背景填 `#BB5125`（中灰橙），强调随意。
2. 期望：预览与主窗口的正文仍是可读的深/浅色；对比度读数正文 ≥7。
3. 失败长什么样：主窗口背景被压得过暗/过亮到认不出原色，或正文仍发灰。

### 5.3 弹窗操作流畅、取消可回滚

1. 点「自定义…」，改几笔颜色后按 Esc。
2. 期望：主窗口外观不变；设置页仍显示原来的主题。
3. 再打开、改色、点「应用」，期望立即生效且设置页摘要更新为「当前：背景 … · 强调 …」。
4. 失败长什么样：Esc 后主窗口已经变色；或应用后要重启才生效。

## 6. 已知限制与偏差

- 中灰背景会被自动压暗/提亮（保持色相），所以「选了什么背景就得到什么背景」只在亮度可读区内成立；
  取色器预览会显示校正后的最终色，用户能立即看到差异。
- 自定义主题忽略色调滑杆（`SupportsTone` 对 Custom 返回 true，但 `ForCustom` 本身不再叠加色调）。
- hex 接受 `#RRGGBB`、`#AARRGGBB`、不带 `#`；不认三位缩写与颜色名。
- 「跟随系统」仍只读一次注册表，系统切深浅后要重启或重选。
- 对比度只断言不透明前景/背景；`Hover`/`Selected` 是叠色，不参与。

## 7. 下一步

用户确认 5.1–5.3 后：勾 `docs/plan.md` Phase 15 剩余项、补实测结论、提交 `phase-15: 补肉眼验收结果`；
然后进 Phase 16（顶边热区探针）。
