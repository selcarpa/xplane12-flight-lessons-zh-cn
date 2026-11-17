# 开发指南 —— 目录规划 · 研发路径 · 脚手架

> 本文是**给维护者（和接手的 AI）**看的：仓库该长什么样、一个新游戏版本从进来到发布要走哪几步、
> 以及每一步该敲哪个命令。
> 面向使用者的文档在 [`使用说明.md`](../user/使用说明.md)，发布说明在 [`release-notes.md`](../release/release-notes.md)，
> Steam 认证运维在 [`steam-auth.md`](../ops/steam-auth.md)，已验证事实与设计约束在 [`AGENT.md`](../../AGENT.md)。
> 文档总索引在 [`docs/README.md`](../README.md)。

**当前状态**：批次 1（目录搬迁）✅ 已落地；批次 2（统一入口 `dev.sh`）、批次 3（单元测试）⬜ 待做。
详见 [§5 落地批次](#5-落地批次)。

---

## 1. 诊断（批次 1 之前的问题，现已基本解决）

当时结构能跑，但有三处「同一类东西散在两个地方」：

1. **脚本有两个家** —— `build.sh` / `build.ps1` 在仓库根，`scripts/` 里另有一套 steam 脚本；
   不知道新脚本该放哪，CI / 文档 / devcontainer 三处各引一次。
2. **文档按「文件」而非「读者」分类** —— 用户手册、发布说明、运维说明混在 `docs/`；
   新人不知道该读哪份，发布说明混在用户文档里还容易被误改。
3. **开发者文档与用户文档平级** —— `AGENT.md`、`PLAN.md` 躺在根目录与 `README.md` 同级，门面被淹没。

另有两处欠账：没有单元测试（`Core/` 的字节级逻辑无回归护栏）、没有统一任务入口
（CI 敲 `dotnet`、本地各写各的，「本地过了」≠「CI 会过」）。

> 1–2 已由批次 1 解决（见 §5）；3 只解决了 `PLAN.md`（`AGENT.md` 按 §6 决定留在根目录）；
> 欠账中的统一入口属批次 2，单元测试属批次 3。

---

## 2. 目录结构（批次 1 已落地）

```
仓库根
├── README.md                 用户门面：一句话是什么、怎么用、去哪下载
├── AGENT.md                  维护者/AI 接手入口（保留根目录：被 CI、devcontainer、文档广泛引用）
├── LICENSE                   MIT
├── .gitignore / .gitattributes
│
├── src/                      产品代码（分层不变：平台无关 ↔ 平台相关）
│   ├── Program.cs            入口：无参 → GUI；带参 → CLI
│   ├── XpLessonZhCN.csproj   项目文件（net9.0 / Avalonia / 自包含便携发布）
│   ├── Core/                 补丁引擎、译文解析、校验、兼容清单（无 IO、无平台 API）
│   ├── Platform/             平台差异：PE/ELF、路径策略、提权、字体探测、进程
│   ├── Service/              用例编排（分析 / 打补丁 / 校验 / 还原），GUI 与 CLI 共用
│   ├── Cli/                  无界面入口（CI、headless Linux、脚本调用）
│   └── Gui/                  Avalonia 界面
│
├── tests/
│   └── SmokeTests/           端到端冒烟：核心自检 + GUI headless
│                             （⬜ UnitTests 计划中，见 §5 批次 3）
│
├── translations/zh.csv       数据：译文表（每行含字节上限）
├── compat/known-versions.json 数据：已验证的游戏版本清单
│
├── scripts/                  ★ 唯一的脚本入口
│   ├── build.sh / build.ps1  构建 + 冒烟 + 发布 + 打包 ✅（批次 1 迁入）
│   ├── check/                体检类 ✅ links.sh / shell.sh（⬜ translations.sh 待做）
│   ├── tools/                render-compat-versions.py、golden-sha256.py ✅（批次 1 迁入）
│   ├── files-linux.txt / files-windows.txt  下载用的文件过滤列表 ✅
│   ├── steam-*.sh            Steam 认证与下载 ✅（批次 1 迁入）
│   └── dev.sh                统一任务入口 ⬜（批次 2）
│
├── docs/                     ★ 按「读者」分类 ✅（批次 1）
│   ├── README.md             文档索引 ✅
│   ├── user/使用说明.md       随包发布（CI 拷进 dist）✅
│   ├── maintainer/
│   │   ├── development.md    本文：目录规划 / 研发路径 / 脚手架 ✅
│   │   └── plan.md           历史方案与决策 ✅（原 PLAN.md）
│   ├── ops/steam-auth.md     Steam 认证与自动下载运维 ✅
│   └── release/release-notes.md 发布说明模板（CI 拼装 Release 正文）✅
│
├── .github/workflows/build.yml  CI：四平台构建 + 冒烟 + 发布
└── .devcontainer/            Codespaces 云端环境（自成一体，不参与产品构建）
```

**不变的部分**（不要动）：

- `src/` 的五层划分与命名 —— 已经足够清晰，Core/Platform 的边界是本项目最值钱的设计决定（AGENT.md §4）。
- `translations/`、`compat/` —— 是**数据**，不放脚本、不放文档。
- `AGENT.md` 留在根目录 —— 它是「接手入口」，链接点多，移动收益低、风险高。

---

## 3. 研发路径

一条主线：**新游戏版本进来 → 自测通过 → CI 通过 → 发布 → 文档同步**。
每一步都有固定命令、固定产出、固定关卡。

### 3.1 阶段与关卡

| 阶段 | 目的 | 命令 | 状态 | 产出 | 关卡（不过不进下一步） |
|---|---|---|---|---|---|
| **S0 取样** | 拿到目标版本的可执行文件 | `scripts/steam-download.sh --app 2014780 --depot 2014784 --filelist scripts/files-linux.txt --output ./downloads/linux` | ✅ 可用（参数见 [ops/steam-auth.md](../ops/steam-auth.md) §5） | `~/steamapps/X-Plane 12/X-Plane-x86_64` | sha256 与 `AGENT.md` §1.1 一致，或**明确判定为新版本**并登记 |
| **S1 锚点分析** | 定位补丁区间与候选串 | `XPlane12-FlightLessons-zh-CN --selftest` + Core 扫描 | ✅ 可用 | 锚点常量、`compat/known-versions.json` 条目 | `--selftest` 通过；not-found 条数不高于同版基线 |
| **S2 译文** | 写/修译文 | 编辑 `translations/zh.csv` | ✅ 可用（体检脚本 ⬜ 批次 2） | 译文行（含字节预算） | 中文 UTF-8 字节数 ≤ `budget_bytes`（自检会校验）；`scripts/check/translations.sh` 落地前靠 `--dry-run` 兜底 |
| **S3 自测** | 在**副本**上验证补丁 | `.devcontainer/xplane-patch-demo.sh --apply` | ✅ 可用 | 打完的 exe + sha256 | sha256 与 AGENT.md §1.1「打完补丁 sha256」一致；原版文件未被改 |
| **S4 提交** | 本地全量检查 | `scripts/dev.sh check` | ⬜ 批次 2（当前手动跑 `scripts/build.sh` + `scripts/check/*.sh`） | — | 单测 + 冒烟 + 译文体检 + 链接检查 + 脚本语法 全绿 |
| **S5 CI** | 四平台构建 | push / PR → `build.yml` | ✅ 可用 | 4 个平台产物 | 四个矩阵 job 的冒烟 + `--selftest` 全过 |
| **S6 发布** | 出包 | 打 tag `v*` → `gh-release` | ✅ 可用 | Release（zip/tar.gz/SHA256SUMS） | Release 正文含自动生成的兼容版本表 |
| **S7 文档同步** | 让文档追上代码 | 改 `README.md` / `docs/user/使用说明.md` / `docs/release/release-notes.md` | ✅ 可用（`scripts/check/links.sh`） | — | `scripts/check/links.sh` 通过；版本号与 compat 清单一致 |

### 3.2 三条常见路径的快捷方式

| 我要做的事 | 命令序列 | 状态 |
|---|---|---|
| **支持一个新的游戏版本** | S0 取样 → `scripts/scaffold/new-version.sh --depot D --manifest M` 生成 compat 条目骨架 → S1 锚点 → S2 译文 → S3 自测 → S4 提交 → S6 发布 | 取样/S1/S2/S3/S6 ✅；`scripts/scaffold/new-version.sh` ⬜ 批次 3 |
| **只修译文** | S2 → `scripts/check/translations.sh` → S3 → S4 | `scripts/check/translations.sh` ⬜ 批次 2（当前用 `--dry-run` + `--selftest`） |
| **只想在云端试** | Codespaces → `scripts/dev.sh setup` → 按 `.devcontainer/README.md` 部署认证 → `scripts/dev.sh steam:download` | ⬜ 批次 2（当前直接用 `scripts/steam-download.sh`，见 [ops/steam-auth.md](../ops/steam-auth.md)） |

### 3.3 铁律（与 AGENT.md §6.4 一致）

1. **CI 是发布的唯一权威** —— 本地绿了不代表能发，ARM64 / Windows 特有行为只有 CI 能验。
2. **一切验证先复制副本再操作** —— 下载下来的原件一律不改。
3. **不下完整游戏** —— 只取可执行文件最小包（≈ 79 MB / 55 MB），公共 depot `2014781`（≈ 37 GB）不提供。
4. **凭据不入库** —— `account.config` 只在本地/CI 临时存在，见 [`ops/steam-auth.md`](../ops/steam-auth.md)。

---

## 4. 脚手架

> 状态：`scripts/check/links.sh` ✅、`scripts/check/shell.sh` ✅ 已落地；
> `scripts/dev.sh`、`scripts/check/translations.sh`、`scripts/scaffold/new-version.sh` ⬜ 属批次 2/3。

脚手架的目标：**让「加一条译文」「加一个游戏版本」「发一次版」变成一条命令**，而不是一段临场发挥的 shell。

### 4.1 统一入口 `scripts/dev.sh`

> ⬜ **整个 `scripts/dev.sh` 尚未落地（批次 2）**，下面的命令是设计稿，现在跑不通。
> 当前请直接用：`scripts/build.sh`（构建+冒烟+打包）、`scripts/check/links.sh`、
> `scripts/check/shell.sh`、`scripts/steam-download.sh`（下载）、`scripts/steam-auth-*.sh`（认证）。

```bash
./scripts/dev.sh setup              # 还原依赖 + 编译（首次/换机器）
./scripts/dev.sh test               # 单元测试 + 冒烟测试
./scripts/dev.sh lint               # 译文体检 + 文档链接 + shell 语法 + JSON 合法性
./scripts/dev.sh check              # lint + test（提交前一条命令）
./scripts/dev.sh build              # 发布当前 RID 的自包含单文件
./scripts/dev.sh pack               # 打包 zip/tar.gz + SHA256SUMS
./scripts/dev.sh steam:init         # 本地人工登录，产出 account.config
./scripts/dev.sh steam:deploy       # 部署认证文件到目标机
./scripts/dev.sh steam:download     # 零交互下载可执行文件
./scripts/dev.sh version:add        # 新增兼容条目骨架（交互式）
./scripts/dev.sh release:notes      # 预览拼装出来的 Release 正文
```

设计约束：

- **纯 bash**，不引入额外依赖（CI 里只需要 bash + dotnet + python3）；
- 每个 task 是一个函数，task 之间可组合（`dev.sh check` = `lint` + `test`）；
- 失败立刻非零退出，**不吞错误**；
- 与 CI **共用同一批检查脚本**（CI 的 `lint` job 直接调 `dev.sh lint`），避免「本地一套、CI 一套」。

### 4.2 检查器 `scripts/check/`

| 脚本 | 检查什么 | 状态 | 失败示例 |
|---|---|---|---|
| `translations.sh` | `zh.csv` 的键唯一、英文非空、**字节预算不超限**（与 Core 的 `budget_bytes` 同源）、UTF-8 合法 | ⬜ 批次 2（当前靠 `--dry-run` / `--selftest` 兜底） | `line 128: 译文 27 字节 > 预算 22 字节` |
| `links.sh` | 所有 Markdown 内部链接指向存在的文件 | ✅ 已落地 | `docs/maintainer/development.md → ../steam-auth.md 不存在` |
| `shell.sh` | 全部 `*.sh` 通过 `bash -n` | ✅ 已落地 | `scripts/foo.sh: 第 42 行语法错误` |

> `translations.sh` 是「脚手架」的关键一环：字节预算目前只存在于 `translations/zh.csv` 与 Core 常量两处，
> 手算容易错，让脚本代替人算。

### 4.3 生成器 `scripts/scaffold/new-version.sh`

> ⬜ 尚未落地（批次 3）。设计目标：新增游戏版本时，自动产出

1. `compat/known-versions.json` 的**条目骨架**（版本号、平台、sha256 占位、notes 模板）；
2. 需要登记的 sha256 清单（打勾式），便于直接填；
3. 后续命令清单（取样 → 锚点 → 译文 → 自测），把研发路径 S0→S3 固化下来。

### 4.4 文档模板（批次 3，计划中）

> ⬜ `docs/templates/` **还不存在**，下面只是计划里的形态。

- `release-notes-entry.md`：发布说明片段模板；
- `new-version-notes.md`：兼容清单 notes 字段模板。

**当前 CI 的真实机制**（不是按条目模板拼装）：`build.yml` 先 `cat docs/release/release-notes.md`
得到静态正文，再用 `scripts/tools/render-compat-versions.py` 读 `compat/known-versions.json`
**追加一整张兼容版本表**。所以现在新增兼容版本只需要改清单，不必手改发布说明；
若要引入按条目模板拼装，列入批次 3。

---

## 5. 落地批次

按「风险从低到高」分三批，每批独立可交付、可回滚。

### 批次 1：搬迁 ✅ 已完成（纯移动 + 改引用，不改逻辑）

| 动作 | 同步修改的地方 | 状态 |
|---|---|---|
| `build.sh` / `build.ps1` → `scripts/` | 脚本内 `cd` 改为仓库根（`dirname/..`）、`docs/user/使用说明.md` 路径；`AGENT.md`、`README.md`、`.devcontainer/postCreate.sh`、`.devcontainer/README.md` 的调用处 | ✅ |
| `docs/` 按读者分子目录 | `build.yml` 三处路径（Windows 打包 / Linux 打包 / Release 正文）、两个 build 脚本的拷贝路径、README 仓库树与使用说明链接 | ✅ |
| `PLAN.md` → `docs/maintainer/plan.md` | README 仓库树、AGENT.md 内链 | ✅ |
| `.github/render-compat-versions.py` → `scripts/tools/` | 脚本内 `parents[2]` 定位 `compat/known-versions.json`；`build.yml` 一处调用 | ✅ |
| 新增 `docs/README.md` 索引、`scripts/check/links.sh`、`scripts/check/shell.sh` | — | ✅ |

**验证**：Linux 侧 `scripts/build.sh` 实跑通过（构建 → 发布 → 打包 → SHA256，产物含 `使用说明.md`）；
`scripts/check/links.sh` 相对链接全部有效；`scripts/check/shell.sh` 全部脚本语法通过；
`python3 scripts/tools/render-compat-versions.py` 输出正常。
**Windows 侧未实机验证（由 CI 覆盖）**：本批次同时改了 `scripts/build.ps1` 与 workflow 里两处
Windows 路径，本机只跑了 Linux 脚本，Windows 打包与产物启动由 CI 的 `windows-latest` job 验证。

### 批次 2：统一入口（新增，不破坏现有流程）⬜ 待做

- 新增 `scripts/dev.sh`（`setup/test/lint/check/build/pack/steam:*/version:add/release:notes`）；
- 新增 `scripts/check/translations.sh`（译文字节预算体检）；
- `build.yml` 增加 `lint` job（调 `dev.sh lint`），让本地与 CI 同一套检查；
- `scripts/lib/` 归置共享 shell 库（`steam-auth-common.sh`）。

### 批次 3：可选增强 ⬜ 待做

- `tests/UnitTests`（引入 xunit，CI 增加约 20 秒）—— 给 `Core/` 的字节级逻辑加回归护栏；
- `scripts/scaffold/new-version.sh` —— 把「新增版本」固化成一两条命令（见 §4.3）；
- `docs/templates/` 文档模板 —— 当前 CI 是「静态正文 + 整张兼容版本表」，
  若要改成按条目模板拼装，模板放这里（见 §4.4）。

---

## 6. 已定的决策

| 决策 | 结论 | 理由 |
|---|---|---|
| 根目录的 `build.sh` 留不留兼容壳？ | **不留，直接删除**，全部改指 `scripts/build.sh` | 根目录只留门面文件；`scripts/check/shell.sh` 会持续确认根目录没有残留构建脚本 |
| `AGENT.md` 是否移进 `docs/`？ | **不动** | 它是接手指南，链接点分散（CI / devcontainer `openFiles` / compat 清单的 notes 都在指它），移动收益远小于风险 |
| 单元测试什么时候做？ | **放到批次 3** | 先让结构与入口清晰，再加测试工程，避免一次改动同时动「布局」和「依赖」 |
