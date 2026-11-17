# AGENT.md — 维护者 / AI 接手指南

这份文件写给后续接手的人（或 AI）：记录**已验证的事实**、**为什么这样设计**、**踩过的坑**，
避免重复试错。改代码或继续翻译前请先读完。

文档索引见 [docs/README.md](docs/README.md)；目录规划与研发路径见
[docs/maintainer/development.md](docs/maintainer/development.md)。

---

## 1. 已验证的事实（不要重复验证）

### 1.1 游戏与补丁对象

> 下面这些 sha256 的**机器可读副本**是 `compat/known-versions.json`（本节是人读版，改数据只改 JSON）。
> 清单里的 `steamBranch` 字段对应 Steam 分支：`public` / `previous_release`；
> `scripts/tools/golden-sha256.py` 按平台+分支查金标准，脚本里不再硬编码哈希。

Windows 版（`X-Plane.exe`，PE）——**当前最新验证：12.4.4-r2**：

| 项目 | 值 |
|---|---|
| 版本 | X-Plane **12.4.4-r2**，build **124419**，Intel 64-bit（内部版本串 `12.4.4-r2-e8642cec`） |
| Steam AppID | **2014780** |
| `X-Plane.exe` 大小 | **55,022,152** 字节（12.4.4 起体积比 12.4.3 的 91 MB 明显减小，教学文本内容未变） |
| 原版 sha256 | `5b18cc7d406bdf22be20135849b8705622cef6882edbb6e8d7c54e095b712563` |
| 打完补丁 sha256 | `ae57d6197d44d0bcfa4cc1017c1caccc762338871a483745a965e18591b78a3b` |
| 文本区间 | `42,825,408 .. 42,911,160`（同一对锚点，见 §4） |
| 区间内候选字符串 | **582** 条（区间边界随编译布局变化，与 12.4.3 的 621 条不可直接比较） |
| 译文条数 | **513**（8 课程标题 + 505 口播字幕，与 12.4.3 相同） |
| 补丁统计 | patched **513** / skipped **8**（前缀重叠）/ too-long **0** / not-found **0** |
| 改动量 | **786 段 / 45,095 字节**（占文件 0.0820%；与 12.4.3 改动字节数相同，段数差 1 是 64KB 分块计段导致） |
| 文件结构 | 大小、PE 头 `[:0x400]`、文件尾 `[-0x1000:]` 均不变 |

Windows 版 12.4.3-r2（仍保留在兼容清单里，继续支持）：

| 项目 | 值 |
|---|---|
| 版本 | X-Plane **12.4.3-r2**，build **124311**，Intel 64-bit |
| `X-Plane.exe` 大小 | **91,132,488** 字节 |
| 原版 sha256 | `2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936` |
| 打完补丁 sha256 | `deebc68ee5d3fe5adb33031a105d61d62c4554dd882b1bae88f7705fdc90ec5e` |
| 文本区间 | `41,552,520 .. 41,612,728` |
| 区间内候选字符串 | **621** 条 |
| 补丁统计 | patched **513** / skipped **8** / too-long **0** / not-found **0** |
| 改动量 | **785 段 / 45,095 字节**（占文件 0.0495%） |
| 文件结构 | 大小、PE 头 `[:0x400]`、文件尾 `[-0x1000:]` 均不变 |

Linux 原生版（`X-Plane-x86_64`，ELF）——**当前最新验证：12.4.4-r2**：

| 项目 | 值 |
|---|---|
| 版本 | X-Plane **12.4.4-r2**，build **124419**（`Compiled on Nov  9 2026 16:27:21`） |
| `X-Plane-x86_64` 大小 | **78,659,600** 字节 |
| 原版 sha256 | `565ac3942e952794d5625e04b2147543f2fca2f027942f5f205d142e52728fce` |
| 打完补丁 sha256 | `257cdd957852f9065c8a2c1bf4e769198615189d081ac79c3e63adb4dbf632da` |
| 文本区间 | `49,594,154 .. 49,647,601`（同一对锚点，字节搜索即可定位） |
| 区间内候选字符串 | **649** 条 |
| 译文条数 | **单一共用表 513 条**（两平台同一条译文；Linux 上 5 条句首带垃圾前缀由引擎兜底写入，9 条 not-found，见 §9） |
| 补丁统计 | patched **504** / skipped **7** / too-long **0** / not-found **9**（见 §9） |
| 改动量 | **776 段 / 44,820 字节**（占文件 0.0570%；连续改动区间 775 段，64KB 分块计段 +1） |
| 文件结构 | 大小、ELF 头 `[:0x400]`、文件尾 `[-0x1000:]` 均不变 |

Linux 原生版 12.4.3-r2（仍保留在兼容清单里，继续支持）：

| 项目 | 值 |
|---|---|
| 版本 | X-Plane **12.4.3-r2**，build **124311**（`Compiled on Jun 24 2026 15:40:13`） |
| `X-Plane-x86_64` 大小 | **76,056,352** 字节 |
| 原版 sha256 | `0fe6bc5c5d661633840bc5eba0de88d8def190f1ff40f6a7011d9252cc63eabb` |
| 打完补丁 sha256 | `b34e075c300ed61174fba79fe7ddab4d2bdb4756597715fd1f15726d1855a5c2` |
| 文本区间 | `47,827,431 .. 47,880,969` |
| 区间内候选字符串 | **614** 条 |
| 补丁统计 | patched **507** / skipped **7** / too-long **0** / not-found **6** |
| 改动量 | **779 段 / 44,871 字节** |
| 文件结构 | 大小、ELF 头 `[:0x400]`、文件尾 `[-0x1000:]` 均不变 |

> 🎯 **金标准**：任何实现（Windows / Linux）在可执行文件副本上打完补丁都必须得到对应版本的
> 补丁后 sha256：Windows 12.4.4 `ae57d619…`、Windows 12.4.3 `deebc68e…`、Linux 原生 12.4.4
> `257cdd95…`、Linux 原生 12.4.3 `b34e075c…`。C# 版已实测逐字节一致。

### 1.2 字体（决定「Wine 下中文会不会变方块」）

```
Resources/fonts/NotoSansCJK-SC-Regular.otf   16,412,332 字节   magic=OTTO
  cmap 覆盖：U+4E00–U+9FFF 20,945/20,992 ｜ U+3400–U+4DBF 6,582/6,592 ｜ U+3000–U+30FF 253/256
X-Plane.exe 里引用了该文件名（NotoSansCJK-SC-Regular ×1、Resources/fonts/ ×8）
```

**结论**：字幕渲染用**游戏自带字体**，与系统字体无关；Wine/Proton 下干净 prefix 也不需要装中文字体。
工具只做**检测 + 提示**（`WineFontProbe`），不装字体、不改 prefix。

### 1.3 Wine / Proton 与 Linux 原生版

- Wine/Proton 跑的就是同一个 PE `X-Plane.exe`，字节补丁逻辑 100% 复用，**没有第二套实现**。
- **Linux 原生版（X-Plane-x86_64，ELF）**：同一套字节逻辑直接复用（锚点、safe_span、长度只减不增
  全部一样），只是文件名/类型不同。**可执行文件类型由魔数自动检测**（`Core/ImageFormat.cs`：
  MZ → PE，`\x7FELF` → ELF），与文件扩展名无关。
- 直接在 Linux 文件系统上改 Steam 库里的文件，**不进 Wine prefix**。
- Linux 没有 NTFS「低完整性标签」问题，但**必须保住可执行位**（见 §4 第 6 条）。

### 1.4 教学文本为什么在可执行文件里

课程标题与口播字幕是**编译进只读数据段的 UTF-8 C 字符串**，运行时按地址引用，
不走 `Resources/text/*.txt` 翻译表。所有语言表里都没有这些字符串（Laminar 没提取）。
**注意：Windows 版与 Linux 原生版的口播字幕是同一套内容但不同编译版本**，文案有细微差异
（例如 Linux 版个别句子带 `E`/`A`/`?` 前缀垃圾字符、`mrakes`/`ip` 等笔误），
且 ELF 里字符串紧凑排列（字符串之间几乎没有填充空间，可用字节 = 英文字符串自身长度）。
个别 Linux 构建里字幕串前面直接跟着浮点/结构体数据（没有 NUL 分隔），英文原文不在「字符串起点」，
由 `PatchEngine.LenientOccurrences` 兜底识别（规则见 §4）——12.4.4 Linux 上这样救回了 5 条
（含每节课第一句 `Hey there, it's Austin Meyer…` 和字幕 `Let's do a touch-n-go.`），
12.4.3 Linux 上 4 条。同一词条可能既有「前缀长串命中」又有「数据前缀独立拷贝」，
`Apply` 两边都会处理；`Let's do a touch-n-go.` 的独立拷贝只有 22 字节可用，译文也相应收紧。

**统一译文表（设计决定）**：Windows 与 Linux 原生版**共用同一张** `translations/zh.csv`
（标准 CSV，方便用表格软件维护），列：`english,chinese,budget_bytes`：

- 每个词条**只有一条译文**，两个平台显示完全一致（不再维护"完整版/短版"两套）；
- `budget_bytes` 是该词条允许写入的最大 UTF-8 字节数：取当前验证版本里**最紧的发生位置**
  （Windows 12.4.4 与 Linux 原生 12.4.3/12.4.4 取最紧）。ELF 字符串紧凑排列，
  部分长句的上限就是英文字符串自身长度，译文必须在限制内表达清楚；
- 12.4.4 比旧版更紧的 7 条已收紧上限：`Evolution Cross-Country` 31→23、
  `OK so now you got a few of the X-Plane keys…` 128→127、`All right your airplane. I'm out.` 39→35
  （Windows 12.4.4），`I'm gonna show you a YouTube video…` 71→67、
  `We're going to fly new maneuvers…` 111→110、`Let's get going. Gimme one notch of flaps.` 47→42
  （Linux 12.4.4，含兜底写入的位置），`Let's do a touch-n-go.` 31→22（Linux 字幕独立拷贝，
  译文同时从 24 字节改短为 21 字节「来个连续起降吧」）；
- 自检会校验「每条中文 ≤ budget_bytes」，`--dry-run`/`--apply` 仍以实际二进制 safe_span 为准；
- 好处：单文件维护、双平台显示一致；当前各版本的金标准哈希见 §1.1。

---

## 2. 兼容清单与校验链（重要）

### 2.1 清单

[`compat/known-versions.json`](compat/known-versions.json) 记录「哪些可执行文件版本可信」
（Windows PE 12.4.4 / 12.4.3 与 Linux 原生 ELF 12.4.4 / 12.4.3 共四条；旧的保留）：

```json
{
  "id": "12.4.4-r2-124419",
  "displayName": "X-Plane 12.4.4-r2 (build 124419)",
  "status": "verified",
  "exeSize": 55022152,
  "sha256": "5b18cc7d406bdf22be20135849b8705622cef6882edbb6e8d7c54e095b712563",
  "patchedSha256": "ae57d6197d44d0bcfa4cc1017c1caccc762338871a483745a965e18591b78a3b",
  "regionStart": 42825408, "regionEnd": 42911160,
  "candidates": 582, "entries": 513,
  "patchStats": { "patched": 513, "skipped": 8, "tooLong": 0, "notFound": 0 },
  "platforms": ["windows", "linux-wine"],
  "verifiedOn": "2026-11-16"
}
```

Windows 12.4.3 条目字段含义相同（`exeSize` 91,132,488、`regionStart/End` 41,552,520/41,612,728、
`candidates` 621、`patchedSha256` `deebc68e…`）。Linux 原生版条目字段含义也相同：12.4.4
（`exeSize` 78,659,600、`regionStart/End` 49,594,154/49,647,601、`candidates` 649、
`patchedSha256` `257cdd95…`、`patchStats` { patched 504 / skipped 7 / tooLong 0 / notFound 9 }）、
12.4.3（`exeSize` 76,056,352、`regionStart/End` 47,827,431/47,880,969、`candidates` 614、
`patchStats` { patched 507 / skipped 7 / tooLong 0 / notFound 6 }），
`platforms: ["linux-native"]`。清单同时被编译进 exe；exe 同目录的
`compat/known-versions.json` 会覆盖内置版本。

### 2.2 校验链（`PatchService`）

```
1. 读 exe → sha256
2. sha256 == 某条 patchedSha256  → 已打过补丁，直接返回「已打补丁」
3. sha256 == 某条 sha256         → 已知识别版本（verified）
4. 都不是                        → 未验证版本 → 需要「二次确认」
5. 定位区间（锚点）失败           → 拒绝
6. 内存打补丁 → 结构校验（大小/PE头/尾/改动范围）失败 → 拒绝，不写盘
7. 已知版本：与清单 patchedSha256 不符 → 拒绝，不写盘（说明实现或译文漂移）
8. 备份原版 → 原子写入 → 重读文件核对 sha256 → 不符则用备份自动回滚
```

「二次确认」在 GUI 里是必须勾选的复选框；CLI 里必须显式加 `--force`。

### 2.3 新增兼容版本

1. 确认新版 exe 的锚点仍能定位（跑 `--check` 看「文本区间」与「候选字符串」是否合理）；
2. 跑 `--dry-run` 确认没有 `too-long`；
3. 在一份**副本**上 `--apply --force`，记下新 sha256 与统计；
4. 把新版本条目加进 `compat/known-versions.json`（旧的保留），`status` 写 `verified`；
5. 同步更新 `src/Cli/Selftest.cs` 里 `compat_manifest` 的硬编码断言（原版/补丁后哈希、大小、
   区间、候选数、统计），以及 README / AGENT 的版本表（**Release 说明里的兼容版本表由 CI 从
   清单自动生成，不用手改**；见 §10）；
6. 若新版本在某个词条上出现**更紧**的可用字节，按 §1.4 收紧 `translations/zh.csv` 的 `budget_bytes`；
7. 提交时附上验证数据（版本、大小、两个 sha256、统计）。

> 新版本 exe 体积可能大幅变化（12.4.4 Windows 55,022,152 字节，12.4.3 是 91,132,488 字节），
> 不能靠体积判断版本；以 `--check` 的锚点/区间与 `--dry-run` 的统计为准。

---

## 3. 代码结构

```
src/
├─ Program.cs            入口：无参 → GUI；带参 → CLI（Cli.CommandLine.TryRun 有返回码时直接退出）
├─ Core/                 平台无关，纯字节逻辑
│  ├─ LessonImage.cs     锚点定位、区间、字符串枚举、safe_span、只读字符串解析（PE/ELF 通用）
│  ├─ ImageFormat.cs     可执行文件类型检测（魔数：MZ→PE，\x7FELF→ELF）
│  ├─ PatchEngine.cs     occurrences + patch_one + 句首垃圾前缀兜底 + 统计（返回改动区间）
│  ├─ Verifier.cs        结构不变量 + 改动范围校验
│  ├─ CompatManifest.cs  兼容清单模型 / 内置资源加载
│  ├─ AppVersion.cs      版本号显示（优先 InformationalVersion，便于 CI 从 tag 注入）
│  └─ Translations.cs    CSV 解析（单表 english,chinese,budget_bytes；标准 CSV 引号/转义）
├─ Platform/             平台差异全在这里（Core 里不允许出现 OperatingSystem 判断）
│  ├─ GameLocator.cs     定位游戏：参数 → 环境变量 → 上次 → Steam VDF → 向上搜索
│  ├─ GameExecutable.cs  解析可执行文件：按平台候选顺序 + 魔数自动检测类型
│  ├─ ExeWriter.cs       原子替换（Windows 写父目录；Linux chmod 0755 + rename）
│  ├─ ProcessProbe.cs    游戏是否在运行（Windows 进程名 / Linux /proc，含 X-Plane-x86_64）
│  ├─ WineFontProbe.cs   自带字体完整性检测（检测+提示，不安装）
│  ├─ SfntCmap.cs        最小 sfnt/cmap 解析（判断字体有无某字形）
│  ├─ PathPolicy.cs      工作目录 = **程序目录**/.zh_cn_translate（便携，不写游戏/用户目录）
│  ├─ DirectoryOpener.cs 打开备份目录（按 PATH 解析绝对路径，不依赖当前工作目录）
│  ├─ RunLog.cs          运行日志 log.txt（GUI 与 CLI 都写）
│  └─ Elevation.cs       管理员/root 判断 + Windows 提权重启
├─ Service/PatchService.cs   Analyze / Apply / DryRun / Verify / Restore / ListBackups
├─ Cli/                  CommandLine、Selftest、ConsoleBridge、TeeTextWriter（CLI 输出同时写 log.txt）
└─ Gui/                  App、MainWindow、ConfirmWindow、UiFonts（Avalonia 11.3.x）
tests/SmokeTests/        核心自检 + GUI headless 启动（CI 跑这个）
```

`Cli` 与 `Gui` 共用同一个 `PatchService`，不允许在 GUI 里重复实现补丁逻辑。
译文表**只有一份**：`translations/zh.csv`（Windows 与 Linux 原生版共用，513 条；
`english,chinese,budget_bytes` 三列，`budget_bytes` 记录该条在目标二进制里的字节上限）。

### 3.1 运行时产物（全部在程序目录下，便携设计）

```
<程序目录>/.zh_cn_translate/
├─ backup/X-Plane.exe.<sha12>.bak   原版备份（还原/校验都用它）
├─ data/patch_report.tsv            逐条补丁结果
├─ log.txt                          运行日志（>4MB 时轮转为 log.1.txt）
└─ settings.json                    上次选择的游戏目录
```

**不要**把工作目录挪回游戏目录或 `%APPDATA%`：便携是刻意设计——用户删程序文件夹即可清理，
游戏目录保持干净；同时避免游戏目录只读/被 Steam 校验影响。`PathPolicy.WorkDir` 是唯一入口。

---

## 4. 关键设计约束（改代码前必读）

1. **锚点必须是不随翻译变化的字符串**：`oz_intro_widget_controller_text`（起）/
   `Could not load sound bank at:`（止）。早期用 `Cirrus Intro` 当锚点，打完补丁就找不到。
2. **只改数据段、只改字符串字节**：不新增节、不移动数据、不改代码、**长度只减不增**，
   写不满用 `0x00` 补齐，绝不越过下一条字符串起点。
3. **可用空间 = 到下一条字符串起点之间的字节数 − 1**（末尾保留 NUL）。`safe_span` 计算，
   放不下就报 `too-long`，**改短译文，不要做重定位**。
4. **只认「字符串起点」**：匹配位置前一字节必须是 `0x00`，否则会把长字符串里的同名片段算进来。
   > 唯一例外（兜底）：个别 Linux 构建里字幕串前直接跟着浮点/结构体数据（无 NUL 分隔）或 1~4 字节残字，
   > 英文原文不在字符串起点。`PatchEngine.LenientOccurrences` 只在英文**正好结束在 NUL**、且前缀是
   > 「非法 UTF-8/控制字节数据」或「≤4 字节且不以空格结尾的残字」时才承认它是独立字幕：
   > 从英文原文处写入、保留前缀字节不动。文本前缀（`That `、`…and then: Brakes off!`）一律不兜底，
   > 因为那是更长正文的片段。同一词条可能同时有两类命中（如 `Let's do a touch-n-go.`：既在课程卡片
   > 长串开头、又有一份句首带数据前缀的字幕拷贝），`Apply` 会先处理字符串起点命中、再处理兜底命中。
   > 12.4.4 Linux 的 `Hey there…` 等 5 条、12.4.3 Linux 的 4 条靠这条规则救回；
   > 改这里务必守住条件，放宽会把长句译残。
5. **前缀匹配要跳过**：`Flaps up!` 是 `Flaps up! 120 knots! 150 degree heading!` 的前缀，
   这类命中判为「不是独立的字符串」并跳过（8 条 skipped 就是这样来的），属正常现象。
6. **Linux 必须显式设可执行位**：`File.WriteAllBytes` 新建文件默认 644，替换后游戏起不来。
   `ExeWriter.ApplyExecutableMode` 会设 0755（exFAT 等不支持权限位的文件系统会静默忽略）。
7. **Windows 替换 exe 要「先写上一级目录再 rename」**：开发沙箱给工作目录打了
   `Mandatory Label\Low`，直接新建文件会继承该标签，游戏随后以低权限运行、**无法存档**。
   排查：`icacls "X-Plane.exe" | findstr Mandatory`，正常应无输出。
   **候选顺序**（见 `Platform/ExeWriter.cs`）：上一级目录 → **游戏目录本身** → 系统临时目录。
   早期版本漏了「游戏目录本身」，导致「上一级不可写但游戏目录可写」的环境直接报
   `Access to the path '...\X-Plane.exe.stage' is denied`（备份能写成、exe 替换失败）。
   上一级不可写时仍应能落在游戏目录（同卷、rename 原子）。
8. **某些目录带显式 `Everyone:(CI)(DENY)(DC)`（禁止删除子项）**：原子替换可能被拒，
   `ExeWriter.Replace` 会退化为 `File.Copy(overwrite)` 直接覆盖内容，覆盖后仍照常做哈希校验。
9. **权限不足要能提权**：`Platform/Elevation.cs` 判断 `Environment.IsPrivilegedProcess`，
   Windows 用 `ShellExecute(..., Verb="runas")` 以管理员身份重启并带上 `--path`；
   Linux 只给 `sudo` 文案，不自动提权。GUI 在提权前必须**先弹窗说明原因与后果**
   （`MainWindow.OfferElevationAsync`）；CLI 返回退出码 **4**。
   注意：提权后是新进程，原进程要 `Close()`；游戏目录要跟着传过去，否则用户还得重选。
10. **`patch_one` 的 `stop` 必须在写入前算**：中文比英文长时，写入后再扫 0 会把区间算短，
    导致结构校验误报「越界」。
11. **译文表格式**：标准 CSV，三列 `english,chinese,budget_bytes`（首行是表头）；
    行首 `#` 视为注释跳过。译文里不能有换行（制表符本身不特殊，但字段内含逗号/引号需按 CSV 规则转义）。

---

## 5. 开发中踩的坑

1. **cmap 子表偏移是相对 cmap 表起点的**，不是文件绝对偏移。漏加基址会导致 `SfntCmap.TryOpen`
   返回 null（字体检测报「无法解析 cmap」）。
2. **单文件 + 原生库自解压不可用**：`IncludeNativeLibrariesForSelfExtract=true` 要求启动时把
   原生库解压到 `%TEMP%\.net`，受限系统/杀软环境下直接启动失败（错误码 5）。
   → 改为 `IncludeNativeLibrariesForSelfExtract=false`，产出**便携目录**（exe + 3 个原生库）再打 zip。
3. **WinExe 没有控制台**：`--selftest` 等命令会「没有输出」。`ConsoleBridge` 用
   `AttachConsole(ATTACH_PARENT_PROCESS)` 接回父进程控制台，并设置 `Console.OutputEncoding = UTF8`。
4. **PowerShell 的 `>` 重定向对 WinExe 无效**（输出丢失）。测试时用
   `cmd /c "exe --args > out.txt"` 或 `Start-Process -RedirectStandardOutput`。
5. **Avalonia 主题要单独引包**：只引 `Avalonia` + `Avalonia.Desktop` 没有 `FluentTheme`，
   需要 `Avalonia.Themes.Fluent`（`App.Initialize()` 里 `Styles.Add(new FluentTheme())`）。
6. **`ShutdownMode` 在 `Avalonia.Controls` 命名空间**，不是 `Avalonia.Controls.ApplicationLifetimes`。
7. **UI 里访问 `FontManager.Current.SystemFonts` 可能抛异常**：`UiFonts` 全部包了 try/catch。
8. **没有开 trimming/裁剪**：Avalonia 反射用得多，裁剪有运行时风险；当前体积 39 MB 可接受，
   要瘦身需实测后再决定。
9. **GUI 在无桌面会话里不要用 `Start-Process` 冒烟测试**：进程树把脚本一起挂住。
   用 `tests/SmokeTests` 的 Avalonia headless 验证。
10. **在本仓库的 agent 沙箱里测「权限失败」要小心**：agent 启动的子进程受文件沙箱限制，
    只能写工作区，因此对 `%TEMP%`、`F:` 等位置的写失败**不代表**真实用户环境也没有权限；
    测 ACL 场景时把被测目录放在**工作区内**，并用 `icacls <dir> /deny "<user>:(WD)"`
    （不要用 `(OI)(CI)`，会连子目录一起禁掉）。
11. **不要做单文件 exe（已决定不提供该产物）**：Avalonia 依赖 `libSkiaSharp` / `av_libglesv2` /
    `libHarfBuzzSharp` 三个原生库，原生库不能从内存加载，`-p:IncludeNativeLibrariesForSelfExtract=true`
    时宿主会在 **`Main` 之前**把它们解压到 `%TEMP%\.net\<app>\<hash>`：
    - 解压目录**无法在代码里改**（实测 `Main` 里设 `DOTNET_BUNDLE_EXTRACT_BASE_DIR` 无效）；
    - `%TEMP%` 不可写时是**宿主层致命错误**，拿不到机会弹友好提示（`Failed to determine location
      for extracting embedded files`，错误码 5）。
    所以固定发布**便携目录版**（`IncludeNativeLibrariesForSelfExtract=false`，3 个原生库与 exe 同目录，
    再打 zip），不要重新引入单文件变体。
12. **进程的当前目录被删除后，别用相对路径启动进程**：便携程序可能从临时解压目录/测试夹具启动，
    启动后那个目录被清理掉，此时 `Directory.GetCurrentDirectory()` 会抛
    `FileNotFoundException: Unable to find the specified file.`。`Process.Start("xdg-open", …)`
    这种「相对程序名」在 Unix 上会先调 getcwd 解析程序位置，于是 GUI 点「打开备份目录」直接
    报 `打开目录失败：Unable to find the specified file.`（同时 `GameLocator` 的「当前目录向上
    搜索」候选也会在启动时抛异常）。
    → 统一走 `Platform/DirectoryOpener.cs`：按 PATH 解析出**绝对路径**再启动，并显式设置子进程
    `WorkingDirectory`；`GameLocator` 里读当前目录用 `CurrentDirectoryOrNull()` 降级，不要直接读
    `Environment.CurrentDirectory`。自检 `directory_opener / cwd_resilience` 会复现这个现场。
13. **Linux 进程检测别用宽泛 marker**：早期只要存在 `wineserver` / `wine64-preloader` / `proton`
    进程就当成「游戏在运行」，Proton 残留进程会让「打补丁」按钮一直灰（实测：切到 Linux 原生版后
    按钮变灰，其实游戏根本没开）。现在只认真正的 X-Plane 进程名（`X-Plane` / `X-Plane.exe` /
    `X-Plane-x86_64`，并跳过僵尸进程）；Wine/Proton 进程还要看它的 `cmdline`/`maps` 里是否真的
    加载了 X-Plane。分析时会把命中的进程写进日志（`检测到 X-Plane 12 正在运行：…`），
    自检 `process_probe` 锁定这个行为。

---

## 6. 构建与验证

```powershell
# Windows：构建 + 冒烟测试 + 打包 win-x64
.\scripts\build.ps1
```

```bash
# Linux：构建 + 冒烟测试 + 打包 linux-x64
./scripts/build.sh
```

> 本地脚本只产出当前平台的 x64 包；`win-arm64` / `linux-arm64` 由 CI 在 ARM64 runner 上原生构建。
> 本机也可以 `dotnet publish -r win-arm64`（或 `-r linux-arm64`）交叉发布，但 x64 机器上无法运行
> ARM64 产物，所以「产物能启动」的校验只能在 CI 的 ARM64 runner 上做。

### 本地端到端验证（需要游戏 exe 的副本，**不要动真游戏**）

> 备份与日志落在**程序目录**的 `.zh_cn_translate/`，不在副本目录；验证完删掉程序目录里的
> `.zh_cn_translate/backup` 即可，别把它提交进仓库。

```powershell
# 1) 复制一份 exe 到临时目录，目录里只需要 X-Plane.exe
# 2) 检查（顺带确认「写入权限」「日志」两行是否符合预期）
.\dist\win-x64\XPlane12-FlightLessons-zh-CN.exe --check --path "<副本目录>"
# 3) 预演
... --dry-run --path "<副本目录>"
# 4) 打补丁（必须得到金标准哈希）
... --apply --path "<副本目录>"
(Get-FileHash "<副本目录>\X-Plane.exe" -Algorithm SHA256).Hash.ToLower()
#   → 按 compat/known-versions.json 里对应版本的 patchedSha256 核对（金标准哈希见 §1.1）
# 5) 校验 / 还原
... --verify --path "<副本目录>"
... --restore --path "<副本目录>"
```

其他验证：

- `--selftest`：合成数据跑边界用例（超长拒绝、前缀跳过、重复写入拒绝、锚点失效、清单完整性）
- `dotnet run --project tests/SmokeTests`：自检 + GUI headless 启动（CI 双平台都跑）
- `--font-check`：真实 OTF 解析（应报「抽样 22/22 字形齐全」）
- 未验证版本：改副本 1 个字节 → `--apply` 应拒绝（exit 1），`--apply --force` 才执行
- 回归项「打开备份目录」：把 GUI 从临时目录启动 → 启动后删掉该目录 → 再点「打开备份目录」，
  应正常打开（旧实现会报 `Unable to find the specified file.`）

---

## 6.4 Codespaces 云端开发环境（`.devcontainer/`）

仓库带了一套 GitHub Codespaces 配置（预装 .NET 9 SDK + DepotDownloader），
可以在云端编译、跑测试，并取回 X-Plane 12 的**可执行文件**做端到端验证。

**完整说明在 [`.devcontainer/README.md`](.devcontainer/README.md)** —— 镜像内容、脚本用法、
最小包下载的预设表、Steam 认证步骤、能力边界、排障表。

维护者只需记住这四条：

1. **CI 仍然是发布的唯一权威**：Codespaces 只是 amd64 Linux 环境，跑不了 Windows / ARM64，
   也测不了提权、NTFS ACL、PE 原子替换、Wine/Proton 路径。
2. **只取可执行文件，不下完整游戏**：预设 depot `2014784`(linux) / `2014782`(windows)，
   用 `-filelist` 只取 `X-Plane-x86_64` / `X-Plane.exe`（≈ 79 MB / 55 MB）；
   公共 depot `2014781`（≈ 37 GB）**不提供**下载功能。
3. **账号走 `account.config` 持久化登录态**（`scripts/steam-auth-*.sh`，说明见
   [`docs/ops/steam-auth.md`](docs/ops/steam-auth.md)）：**本地人工登录一次**（`scripts/steam-auth-init.sh`，
   唯一允许输密码/验证码的地方）→ 把 DepotDownloader 自己生成的认证文件部署到目标机
   （`scripts/steam-auth-deploy.sh`，SSH/SCP 或加密 Secret）→ 目标机上**零交互**下载
   （`scripts/steam-download.sh`，只用登录态）。认证失效即**报错停止**，不重试、不改密码、不绕 Steam Guard。
   认证文件**绝不入库、绝不进公开 Artifact**。
4. 一切验证脚本都**先复制副本再操作**，绝不改下载下来的原件；
   打完的 sha256 仍要与 §1.1 的金标准逐字节一致才算数（§2.3 的新增版本流程同样适用）。

---

## 7. 游戏更新后怎么办

1. 跑 `--check`。若报「找不到教学文本锚点」→ 锚点失效：在 `Resources/text` 附近的字符串里
   重新挑一个**不会被翻译**的锚点，改 `Core/LessonImage.cs` 的常量。
2. 若区间正常但 `--dry-run` 报大量 `not-found`/`too-long` → 课程文案改写了：按新英文更新译文表。
3. 若只是版本变了但文案没变 → 走 §2.3 把新版本加进兼容清单（12.4.4 Windows 就是这样加的：
   只新增清单条目 + 更新自检/文档，译文一条未改）。
4. **Steam 校验/更新会把 exe 换回原版**，重新 `--apply` 即可；`backup/` 不会被覆盖。

---

## 8. 翻译约定

- 口吻：Austin 的第一人称、口语、简短，适合当字幕读。
- 术语：`collective` 总距、`bank` 坡度、`trim` 配平、`heading` 航向、`ft-lb` 英尺磅、
  `ITT` 涡轮进口温度、`PAPI`、`touch-n-go` 连续起降、`chandelle` 上升转弯。
- **保留 `[sim/xxx]` 占位符原样**（自检会校验数量一一对应）。
- 机型名保留英文：`Cirrus SR-22`、`Lancair Evolution`、`Tesla Model 3`。
- 长度不够时优先删修饰语，不要缩写术语。

---

## 9. 已知遗留

- **2 条「课程卡片描述」**（`Let's do a touch-n-go. …`）在 exe 里仍是英文：卡片描述显示走
  `Chinese.txt`（已中文），exe 里那份不参与显示，`--check` 会把它算成「待应用」，属正常。
  （12.4.4/12.4.3 Linux 里 `Let's do a touch-n-go.` 还有一份字幕拷贝，句首带数据前缀；可用字节只有 22，
  译文已收紧为 21 字节并纳入兜底写入，见 §4。）
- **skipped 行都是前缀重叠**（Windows 8 行 / Linux 7 行，如 `Flaps up!`、`Brakes off!`），不是漏译。
- **~22 条评分项标签**（`Throttle Control.` / `RPM Control` / … / `Pass/Fail`）未翻译，
  且**不在任何语言表里**；如果要译，需要新加译文并在清单里更新 patched sha256（会改变金标准）。
- **Linux 原生版差异**（共用表里这些词条在 Linux 上算「不适用 / not-found」，均属正常平台差异）：
  - **12.4.4 Linux 的 9 条 not-found**：
    - 4 个课程标题串在该 Linux 构建的二进制里**根本不存在**：`Cirrus Basic Handling` /
      `Evolution Intro` / `Evolution Emergencies` / `Tesla Model 3 Test-Drive`（12.4.4 Linux
      只保留 `Cirrus Intro` / `Cirrus Advanced Handling` / `Cirrus Landing` /
      `Evolution Cross-Country` 4 个标题串；游戏内标题显示情况待实机确认，工具无事可做）；
    - 4 条只是更长句的前缀 / 被长句先覆盖（`Brakes off!`、`Flaps up!`、`Flaps up! 120 knots…`、
      `A touch-n-go is a landing…`），最终字节正确（显示的是长句的中文）；
    - 1 条 Linux 版措辞不同（表里的 `ITT is a bit low. Add some power…`；游戏里实际串是
      `That ITT is a bit low…`，由表里另一条同义中文覆盖显示，不影响观感）。
  - **已由引擎宽松兜底救回（不再算 not-found）**：12.4.4 Linux 5 条字幕串前直接跟着二进制数据/残字
    （` AHey there, it's Austin Meyer…`、`ЄEOK let's learn how to get this Cirrus SR-22…`、
    `@\x83EWe're going to fly new maneuvers…`、`ALet's get going. Gimme one notch of flaps.`、
    `…95@Let's do a touch-n-go.`）；12.4.3 Linux 4 条（`We're going…`、`?I'm gonna show you…`、
    `ALet's get going…`、`Let's do a touch-n-go.`）。规则见 §4 第 4 条，从英文原文处写入、前缀字节不动。
  - **12.4.3 Linux 的 6 条 not-found**：Tesla 标题缺失、`Brakes off!`、`Flaps up!`、
    `Flaps up! 120 knots…`、`A touch-n-go…` 等前缀/覆盖情形、`ITT is a bit low…`（由 `That ITT…` 覆盖）。
- **12.4.4 兼容已完成（Windows PE + Linux 原生版）**：12.4.4 与 12.4.3 的 Windows / Linux 原生版
  共四条均已验证；各版本的金标准哈希、补丁统计与改动量见 §1.1。
  Linux 12.4.4 共救回 5 条、12.4.3 共 4 条句首带垃圾数据的字幕（含每节课第一句与
  `Let's do a touch-n-go.`）；后者只有 22 字节可用，译文随之收紧到 21 字节，Windows 两个版本的
  金标准也随译文变化重新验证（用原版 exe 副本实测）。12.4.4 Linux 缺 4 个课程标题串（见上条），需实机确认
  游戏里这些标题的显示来源；若实机仍显示英文，再另行调研（超出本工具「只改字符串字节」的能力范围）。
- **单一译文 + 字节上限**：每个词条只有一条译文（两平台一致）；`budget_bytes` 记录该条在
  目标二进制里的最大可用字节数（Windows 12.4.4 与 Linux 原生 12.4.3/12.4.4 取最紧位置，
  含 §4 兜底写入的位置）。改译文表时：中文 UTF-8 字节数必须 ≤ `budget_bytes`（自检会校验），改完务必跑 `--dry-run`
  并对副本做一次端到端验证（哈希要与清单一致）。不要再引入"完整版/短版"双译文机制。
- **ARM64 产物未经实机验证**：`win-arm64` / `linux-arm64` 只在 GitHub ARM64 runner 上跑
  冒烟测试与 `--selftest`（合成数据），**没有**在真实 Windows on ARM / ARM64 Linux 设备上运行过，
  也没有对真实游戏目录打过补丁（作者无 ARM 硬件）。Release 说明、README、包内 `使用说明.md`
  必须保留「未经实机测试」标注，不要写成已验证；等有 ARM 设备实测后再改。
- **GUI 界面中文依赖系统字体**：Windows 一般有；极简 Linux 桌面若缺中文字体，界面会显示方块
  （不影响游戏字幕）。可在 `UiFonts.Preferred` 里补充候选字体名。

---

## 10. 发布流程

发布由 GitHub Actions 完成（`.github/workflows/build.yml`），本地不需要上传任何东西：

```bash
# 1) 确认要发布的内容已经推到 main
git push origin main

# 2) 打 tag 并推送（tag 即版本号，CI 会用它编译进 exe）
git tag -a v0.1.0 -m "v0.1.0"
git push origin v0.1.0
```

触发后会发生什么：

1. `build` job 在四个 runner 上各跑一遍（**原生架构构建 + 冒烟自检**，不是交叉编译）：

   | runner | RID | 产物 |
   |---|---|---|
   | `windows-latest` | `win-x64` | `XPlane12-FlightLessons-zh-CN-win-x64.zip` |
   | `windows-11-arm` | `win-arm64` | `XPlane12-FlightLessons-zh-CN-win-arm64.zip`（⚠️ 未经实机测试） |
   | `ubuntu-latest` | `linux-x64` | `XPlane12-FlightLessons-zh-CN-linux-x64.tar.gz` |
   | `ubuntu-24.04-arm` | `linux-arm64` | `XPlane12-FlightLessons-zh-CN-linux-arm64.tar.gz`（⚠️ 未经实机测试） |

   每个 job：冒烟测试（核心自检 + GUI headless）→ 发布便携版 → 校验发布产物能启动（`--selftest`）→ 上传 artifact。
2. `release` job 等四个 job 都成功后，下载全部 artifact、合并成一份 `SHA256SUMS.txt`，创建 Release 并挂上
   4 个包（Windows 为 zip、Linux 为 tar.gz）与 `SHA256SUMS.txt`。Release 说明正文 =
   [`docs/release/release-notes.md`](docs/release/release-notes.md)（静态部分）+ `scripts/tools/render-compat-versions.py`
   读 `compat/known-versions.json` 生成的**兼容版本表**（版本、平台、原版/补丁后 sha256 前 12 位、
   验证日期），所以**新增兼容版本不需要手改 Release 说明**，改清单即可。
3. tag 名里带 `-` 的（如 `v0.1.0-snapshot`）**自动标记为预发布**，不会顶掉“Latest release”。

> **ARM64 runner 限制**：`windows-11-arm` / `ubuntu-24.04-arm` 这组标准 ARM64 runner
> **只对公共仓库免费可用**，私有仓库上会直接失败。仓库若转为私有：把这两个 `os` 换回
> `windows-latest` / `ubuntu-latest` 交叉构建 arm64（`dotnet publish -r win-arm64` /
> `-r linux-arm64` 均可行），同时跳过「校验发布产物能启动」两步——x64 runner 执行不了 ARM64
> 产物；或用付费的 arm64 larger runner。
>
> **ARM64 产物未实机验证**：ARM64 runner 上只跑冒烟测试与 `--selftest`（合成数据），没有在真实
> ARM 设备/真实游戏上验证（见 §9）。发布时不要把 ARM64 包写成「已验证」。

注意：

- **版本号来源**：`src/XpLessonZhCN.csproj` 的 `<Version>`（当前 `0.1.0`）是本地默认值。CI 从 tag 取版本号
  （去掉前缀 `v`），做正则校验后用 `-p:Version=…` 覆盖：完全不符合 `x.y.z` 形式的 tag 会退化成占位版本
  `0.0.0-ci`；带后缀的预发布 tag（如 `v0.1.0-snapshot`）按当前正则仍按 tag 原样传下去。
  所以 `--version` 只有在 tag 构建时才等于 tag 版本；本地 `scripts/build.sh` / `scripts/build.ps1`
  **不传版本号**，产物版本恒为 csproj 里的 `<Version>`。改版本只需改 tag 或同步改 csproj。
- **仓库设置**：Settings → Actions → General → Workflow permissions 需要允许
  **Read and write**，否则 `release` job 拿不到写权限（403）。workflow 里已声明 `contents: write`。
- 只在打 tag 时发布；推 main、PR、手动触发都只构建不发布。
- 本地预览发布产物：`./scripts/build.ps1`（Windows）、`./scripts/build.sh`（Linux），产物在 `dist/`，不会上传。

---

## 11. 禁止事项

- 不要改代码段、不要新增/移动节、不要做字符串重定位。
- 不要为了「让译文放得下」而覆盖相邻字符串或对齐填充之后的字节。
- 不要把游戏文件、备份 exe、机器相关的路径提交进仓库。
- 不要在 `Core/` 里写平台判断；平台差异只能进 `Platform/`。
- 不要跳过「写完重读校验」这一步。
