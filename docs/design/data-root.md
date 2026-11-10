# 设计：数据根目录（Data Root）与「不落 C 盘」

> 对应 Phase 7。目标：**所有用户产生的数据（便签板、随心记笔记、日志、备份、回收站）都不放在系统盘**，
> 目录位置可在设置里改，首次运行自动选盘并完成迁移。

## 1. 约束与取舍

| 约束 | 做法 |
|---|---|
| 用户数据不落 C 盘 | 业务数据（board / notes / logs / trash）全部放在 **数据根**；C 盘只允许存在「指针」（几百字节或注册表项） |
| 默认在程序安装目录下 | 程序装在非 C 盘时，默认 `「<程序目录>\data」`；程序在 C 盘时，改选第一个可写的非系统固定盘 |
| 可配置 | 设置面板「数据位置」支持改根目录，触发迁移 |
| 便携 | 程序目录放一个空的 `portable.txt` → 强制使用 `「<程序目录>\data」`，忽略指针（U 盘场景） |
| 单实例 | 跨根写会互相覆盖，所以单实例 + 数据根 `.lock` 双重保护 |

**为什么用注册表存指针**：指针本身如果放 `%APPDATA%` 也落在 C 盘。注册表 `HKCU` 不产生用户数据文件，
是这块绕开 C 盘最干净的位置。降级：注册表不可写时，才用 `%APPDATA%\wantodo\location.json`（<200 B）。

## 2. 指针与解析顺序

```
HKCU\Software\wantodo
  DataRoot     REG_SZ   "D:\Wantodo"        ← 唯一指针
  SchemaVersion REG_DWORD 2
```

`DataRootResolver.Resolve()` 顺序：

1. 程序目录存在 `portable.txt` → `「<程序目录>\data」`（便携模式，最高优先级）。
2. 注册表 `DataRoot` 存在、目录存在、可写 → 直接使用。
3. 首次运行 / 指针失效：
   - 程序目录不在 C 盘 → `「<程序目录>\data」`。
   - 否则枚举 `DriveInfo.GetDrives()` 中 `DriveType.Fixed` 且 `!IsSystemDrive` 的盘，按可用空间降序，
     依次尝试 `「<盘>:\Wantodo」`：可创建、可写探针文件、空间 > 数据量 × 2 → 采用。
   - 都不满足 → 兜底 `「<程序目录>\data」` 并**在设置面板与日志里明确告警**「数据位于系统盘」。
4. 写入指针 + `SchemaVersion`。

> 只往根目录下写一个 `Wantodo\` 文件夹，不散落在盘符根目录里。

## 3. 目录结构（Schema v2）

```
D:\Wantodo\                         ← 数据根（设置里可改）
├─ config\
│  └─ settings.json                 ← 应用设置：surface / theme / tone / placement / sizeMode /
│                                      defaultNoteFormat / notesFolder / hotkey / autostart 等
├─ board\
│  ├─ data.json                     ← 便签板 + 布局树（原子写）
│  ├─ data.bak                      ← 上一版（File.Replace 自动生成）
│  └─ data.corrupt-<ts>.json        ← 双损坏时隔离出来的坏文件
├─ notes\                           ← 随心记 vault（文件即事实来源，可被外部编辑器改）
│  ├─ inbox\
│  │  └─ 20261110-2214-灵感.txt
│  └─ 2026\11\                      ← 归档（按年月，可选）
│     └─ 20261110-2130-周会纪要.md
├─ logs\
│  └─ app-20261110.log              ← 按天滚动，保留 14 天
├─ trash\                           ← 软删除（保留 30 天，启动时清理）
│  └─ 20261110-2215-<原文件名>
├─ .lock                            ← 进程 id + 启动时间，防两个实例写同一根
└─ README.txt                       ← 首次创建时写一段「这些文件是什么」，方便用户自己备份
```

**为什么板数据与设置分开**：设置频繁改（主题、层级），板数据体积大；分开后写盘互不牵连，也方便用户
「只备份 notes 和 board」。

**为什么 notes 用真实文件而不是塞进 JSON**：用户要的是「像手机便签」「能连到笔记」——文件可以被
Obsidian / VSCode / 记事本直接打开，外部改动只要重新扫描就能进索引，不会锁死在库里。

## 4. 文件命名与格式

- 新建笔记：`yyyyMMdd-HHmm-<slug>.<ext>`
  - `slug`：标题去掉 `\ / : * ? " < > |` 与首尾空白，连续空白折成 `-`，最长 40 字符；标题为空时用 `note`。
  - 重名：追加 `-2`、`-3`。
  - `ext`：由 `defaultNoteFormat` 决定（`txt` / `md`），**只影响新建**，已有文件按扩展名识别。
- `.md` 使用 YAML front matter：

```markdown
---
title: 灵感
created: 2026-11-10T22:14:00+08:00
updated: 2026-11-10T22:31:12+08:00
tags: []
---

正文…
```

- `.txt` 不写元数据，标题就是文件名（去掉时间前缀）。

## 5. 迁移流程（DataMigrator）

状态机：

```
Idle → Preflight → Copying → Verifying → Switching → Reloading → Done
                        └──────── Rollback ←──────┘
```

1. **Preflight**：目标根可写、非同一磁盘路径、目标目录为空或只有空目录、剩余空间 ≥ 源数据 × 2、
   当前没有别的实例持有 `.lock`。
2. **Copying**：`robocopy` 语义（保留时间戳），逐目录复制到 `「<新根>\.incoming」`。
3. **Verifying**：文件数/字节数比对 + `board/data.json`、`config/settings.json` 能 JSON 解析 +
   笔记文件可读。
4. **Switching**：写注册表指针 → 原子 rename `.incoming` 为正式目录 → 旧根写 `.migrated.json`（含迁移时间与新根）。
5. **Reloading**：提示「迁移完成，需要重启」→ 退出并由用户重开（或 `Process.Start` 自身后退出）。
6. **Rollback**：任一步失败 → 删 `.incoming` → 指针不动 → 原根数据完好，设置面板提示失败原因。

**旧目录处理**：不自动删除，只写 `.migrated.json` 并在设置里提供「清理旧数据」按钮（默认不删）。

## 6. 失败与边界

| 场景 | 行为 |
|---|---|
| 目标盘不可写（只读、BitLocker 锁、权限） | 跳过该候选，继续下一个；全失败则兜底并告警 |
| 目标空间不足 | 拒绝迁移，提示需要多少空间 |
| 迁移中途断电 | `.incoming` 是半成品；下次启动检测到即清理，指针未切换，源数据完好 |
| 用户手动删了数据根 | 启动时检测指针失效 → 重新走首次解析，提示「原数据目录不见了」 |
| 程序装在 `Program Files` | 目录不可写 → 自动改建到非 C 盘，不往 Program Files 里写 |
| 数据根在移动硬盘上，盘没插 | 启动时提示「数据盘未就绪」，提供「重试 / 换位置 / 以只读继续」 |
| 两个实例同时起 | `Mutex` 单实例 + `.lock` 校验，第二个直接退出 |

## 7. 与现有代码的衔接

- `JsonBoardStore.DefaultDataPath` 从 `%APPDATA%\wantodo\data.json` 改为 `「<DataRoot>\board\data.json」`。
- `AppLog` 日志路径改为 `「<DataRoot>\logs\app-yyyyMMdd.log」`，保留 14 天。
- 首次升级（v0.4 → v0.5）：检测 `%APPDATA%\wantodo\data.json` 存在且新根为空 → **自动迁移**并写日志，
  迁移完成后旧文件改名 `data.migrated.json`（不直接删）。
- 设置面板新增「数据」分组：当前路径、所在盘剩余空间、迁移到…、打开目录、清理旧数据。
- Core 侧新增纯函数：`DataRootPolicy.TryPick(candidates, probe)`、`PathLayout.Resolve(root)`、
  `MigrationPlan.Build(source, target)` —— 全部可单测，真实 IO 走注入的 `IFileSystem` 接口。

## 8. 测试要点

- 候选盘选择：有非系统盘 / 只有 C / 全部不可写 / portable 标记优先 / 指针优先。
- 迁移计划：文件清单、冲突、空间不足、目标非空、跨盘与同盘。
- 路径布局：根目录规范化、非法字符、超长路径（`longPathAware` 已开）。
- 日志轮转与 trash 清理（按日期，不删当天的）。
