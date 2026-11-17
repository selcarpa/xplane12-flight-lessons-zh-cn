# PLAN — 跨平台 GUI 方案

> 状态：**已实施**（v0.1.0，2026-11-15）。本文件保留为设计决策记录；实施偏差见 §16。
> 目标：**一个代码库、Windows 与 Linux(Wine/Proton) 双平台**的图形化补丁工具，用户零安装、解压即用。

> ⚠️ **历史设计记录：文件树与 CLI 已与实现脱节。**
> §4 列出的 `IBinaryImage.cs` / `PeImage.cs` / `PatchReport.cs` / `IExeWriter.cs` /
> `Gui/ViewModels/` / `Gui/Assets/` / `Cli/Commands.cs` / `tests/fixtures/sample.bin`
> **在实现里并不存在**；§9 的 `--report` 参数也**没有实现**。
> **请以 [AGENT.md §3](../../AGENT.md)（代码结构）与
> [compat/known-versions.json](../../compat/known-versions.json)（已验证版本）为准**，
> 本文只用来追溯设计由来，不要照着它改代码。

---

## 1. 目标与非目标

### 目标
| # | 目标 | 验收标准 |
|---|---|---|
| G1 | Windows 用户零安装 | 解压后双击运行，不需要装任何运行时 |
| G2 | Linux(Wine) 用户零安装 | 单个可执行文件 + 两行命令，不需要装任何依赖 |
| G3 | 上手成本 ≤ 3 步 | 选目录（可自动）→ 打补丁 → 看到成功；不需要开终端、不需要读文档 |
| G4 | 不破坏游戏 | 自动备份、打完自校验、失败自动回滚、文件大小/PE 结构不变 |
| G5 | 跨平台行为一致 | Windows 与 Linux 产物在同一个 exe 副本上产出**逐字节相同**的结果 |

### 非目标（本版不做）
- ~~**Linux 原生版 X-Plane**（ELF 二进制）暂不支持~~ → **已支持**（2026-11-15 落地）：同一套字节逻辑
  直接复用，`Core/ImageFormat.cs` 按魔数自动检测 PE/ELF。**Windows 与 Linux 原生版共用同一张
  译文表** `translations/zh.csv`（513 条，CSV：`english,chinese,budget_bytes`，单一译文 + 每条
  字节上限），两平台显示一致；金标准见 `compat/known-versions.json`（当前 Windows 12.4.4 `ae57d619…`、Linux 12.4.4 `257cdd95…` 等）。
  补丁引擎本身没有任何格式分支。
- **macOS** 不做，但技术栈天然支持，留待后续。
- **Wine 字体自动安装**：只做检测与提示，绝不修改 Proton prefix 或系统字体（已确认决定）。
- 课程音频（Austin 的英文录音）不处理；只汉化字幕与课程标题。

---

## 2. 已确认的技术事实（本机实测，改动前请勿重复验证）

### 2.1 游戏与补丁对象

| 项目 | 实测值 |
|---|---|
| 版本 | X-Plane **12.4.3-r2**，build **124311**，Intel 64-bit |
| 安装位置（本机） | `F:\SteamLibrary\steamapps\common\X-Plane 12` |
| Steam AppID | **2014780**（`appmanifest_2014780.acf`） |
| `X-Plane.exe` 大小 | **91,132,488** 字节 |
| 原版 sha256 | `2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936` |
| 教学文本区间 | `41,552,520 .. 41,612,728` |
| 区间内候选字符串 | **621** 条 |
| 现有译文条数 | **513**（8 课程标题 + 505 口播字幕；Windows/Linux 共用同一张 CSV，单一译文） |
| 打完补丁后 sha256 | `deebc68ee5d3fe5adb33031a105d61d62c4554dd882b1bae88f7705fdc90ec5e` |
| 补丁统计 | patched 513 / skipped 8（前缀重叠）/ too-long 0 / not-found 0 |
| 文件结构 | 大小、PE 头 `[:0x400]`、文件尾 `[-0x1000:]` 均不变 |

> 🎯 上面两个哈希是**跨平台一致性的金标准**：任何实现（Win/Linux）在 exe 副本上打完补丁都必须得到 `deebc68e…`。

> 📌 2026-11-16 更新：又兼容了 **12.4.4-r2 (build 124419)** 的 **Windows PE 与 Linux 原生版**：
> Windows `X-Plane.exe` 55,022,152 字节、金标准 `ae57d619…`；Windows 12.4.3 `deebc68e…`；
> Linux `X-Plane-x86_64` 78,659,600 字节、金标准 `257cdd95…`；Linux 12.4.3 `b34e075c…`。
> 句首带垃圾数据的字幕由引擎兜底写入（含第一句与 `Let's do a touch-n-go.`），9 条 not-found
> 属平台差异；12.4.3 条目仍保留。最新实测数据以 [AGENT.md §1.1](../../AGENT.md) 与
> `compat/known-versions.json` 为准。

### 2.2 Wine / Proton 场景

| 事实 | 影响 |
|---|---|
| Wine/Proton 运行的就是同一个 PE `X-Plane.exe` | 字节补丁逻辑 100% 复用，**不需要第二套实现** |
| 补丁直接改 Linux 文件系统上的 Steam 库文件 | 完全不进 Wine prefix，不碰 `drive_c`/注册表 |
| Linux 无 NTFS「低完整性标签」问题 | 现有的「写父目录再 rename」hack 仅在 Windows 生效 |
| 游戏自带中文字体 | 见 2.3，**Wine 下中文渲染不依赖系统字体** |

### 2.3 字体（本轮新实测，决定 §7 的设计）

```
Resources/fonts/NotoSansCJK-SC-Regular.otf   16,412,332 字节   magic=OTTO
  cmap 覆盖：U+4E00–U+9FFF  20,945/20,992
             U+3400–U+4DBF   6,582/6,592
             U+3000–U+30FF     253/256
X-Plane.exe 内部字符串引用：NotoSansCJK-SC-Regular ×1、Resources/fonts/ ×8、.otf ×1
```

**结论**：游戏自己加载自带的 CJK 字体，**系统有没有中文字体不影响字幕显示**。
字体风险从「高」降为「低」，但需防两件事：字体文件被删/损坏、个别发行版的 freetype 兼容性。
→ §7 的模块只做**检测 + 提示**，非阻塞。

### 2.4 已知的关键约束（来自原 AGENT.md，仍然有效）
1. 锚点必须是**不会被翻译**的字符串：`oz_intro_widget_controller_text`（起）/ `Could not load sound bank at:`（止）。
2. 只改数据段、只改字符串字节；**不新增节、不移动数据、不改代码、长度只减不增**，写不满补零。
3. 可用空间 = 到下一条字符串起点之间的字节数（含对齐填充），末尾保留 NUL。
4. 只认「字符串起点」：匹配位置前一字节必须是 `0x00`。
5. 前缀匹配必须跳过（如 `Flaps up!` 是 `Flaps up! 120 knots! 150 degree heading!` 的前缀）。
6. Windows 替换 exe 必须「写父目录再 rename」（绕开低完整性标签）。
7. ~~译文表只认 `#` 注释与 `英文<TAB>中文`；译文内不能有制表符/换行。~~
   **已废弃**：译文表已改为**标准 CSV 三列** `english,chinese,budget_bytes`（首行表头，行首 `#` 为注释），
   见 AGENT.md §1.4。

---

## 3. 技术选型（决策）

### 3.1 选定：.NET 9 + Avalonia UI

| 维度 | 说明 |
|---|---|
| 单代码库 | 同一份 XAML/代码出 Windows + Linux 窗口 |
| 零安装 | 自包含发布（便携目录/压缩包），Windows/Linux 都不需要预装运行时 |
| 交叉编译 | `dotnet publish -r linux-x64` 在 Windows 上即可产出 Linux 产物 |
| 中文 | 内嵌字子集，双平台渲染一致，不依赖系统字体 |
| 体积 | 预计 Windows/Linux 各 **30–40 MB**（自包含，不裁剪；trim 实测后再定） |

### 3.2 备选与否决理由

| 方案 | 体积 | 否决/保留理由 |
|---|---|---|
| Rust + egui/eframe | ~12 MB | 体积最优，但 Linux 产物**无法在 Windows 上交叉构建**（需 Linux 链接器），且需手工内嵌 CJK 字体；保留为备选 |
| .NET 9 + 本地 Web UI | ~12 MB | 最稳（零 GUI 依赖、零字体问题），但界面是浏览器标签页；保留为备选 |
| C# WinForms / net48 | 300 KB | **仅 Windows**，Linux 要靠 Mono 跑 WinForms → 否决 |
| 只发 Windows exe，Linux 用户用 wine 跑本工具 | 300 KB | 改动最小，但要求用户有可用 wine 环境、路径映射混乱、将来原生版无解 → 否决（仅作为应急退路） |

### 3.3 平台兼容矩阵

| 平台 | 支持范围 |
|---|---|
| Windows | Windows 10 1809+ / 11（自包含，无 .NET 依赖） |
| Linux | 桌面发行版（Ubuntu 22.04+/Fedora/Arch）、**SteamOS 桌面模式**；需常见 X11/Wayland + fontconfig；纯服务器无显示环境走 CLI |
| 游戏 | X-Plane 12.4.x（按锚点定位；版本未知时提示「未验证版本」但不阻止） |

---

## 4. 架构总览

```
xplane12-flight-lessons-zh-cn/
├─ src/
│  ├─ XpLessonZhCN.csproj          net9.0，Avalonia，自包含便携发布，双 RID
│  ├─ Program.cs                   入口：无参→GUI；带参→CLI
│  ├─ Core/                        ★ 平台无关，纯字节逻辑
│  │  ├─ IBinaryImage.cs           「待补丁目标」抽象（当前仅 PE，预留 ELF）
│  │  ├─ PeImage.cs                PE 定位锚点、区间、字符串枚举、safe_span
│  │  ├─ PatchEngine.cs            occurrences + patch_one + 统计
│  │  ├─ Translations.cs           内嵌 TSV + 外部覆盖 + 格式校验
│  │  ├─ Verifier.cs               结构不变量 + 改动范围校验
│  │  └─ PatchReport.cs            逐条结果（TSV 导出）
│  ├─ Platform/
│  │  ├─ IExeWriter.cs             Win / Linux 两套写入策略
│  │  ├─ GameLocator.cs            Win: 注册表+VDF；Linux: XDG/Flatpak/Snap/Deck
│  │  ├─ ProcessProbe.cs           游戏是否在运行
│  │  ├─ WineFontProbe.cs          §7 字体检测
│  │  └─ PathPolicy.cs             工作目录/备份目录/权限回落
│  ├─ Gui/
│  │  ├─ MainWindow.axaml(.cs)     主窗口
│  │  ├─ ViewModels/               MVVM（CommunityToolkit 或手写 INotifyPropertyChanged）
│  │  └─ Assets/                   图标、内嵌字体
│  └─ Cli/Commands.cs              --selftest/--check/--apply/--verify/--restore…
├─ translations/zh.csv             ★ 唯一译文本体（CSV，构建时嵌入，exe 旁可覆盖）
├─ tests/
│  ├─ selftest 用例（合成缓冲区）
│  └─ fixtures/sample.bin          小体积 PE 结构样本（CI 用，不含 91MB 游戏文件）
└─ .github/workflows/build.yml     双平台构建矩阵
```

**分层原则**：`Core` 不出现任何 `OperatingSystem.IsWindows()`，平台差异全部收敛在 `Platform/*` 的接口实现里（依赖注入，GUI 与 CLI 共用）。

---

## 5. Core 规格（字节级契约，实现时以此为准）

### 5.1 区间定位
```
START_ANCHOR = "oz_intro_widget_controller_text"
END_ANCHOR   = "Could not load sound bank at:"
SCAN_BACK    = 4096
region_start = index(START_ANCHOR) - SCAN_BACK
region_end   = index(END_ANCHOR, 从 START_ANCHOR 之后找)
兜底：START 找不到 → 用 "Cirrus Intro"; 仍找不到且无字幕锚点 → 明确报「锚点失效，游戏可能已更新」
```

### 5.2 字符串枚举
- 从 `region_start` 起逐字节尝试：读到 `0x00` 为结尾，长度 ≥ 2，全部字节 ∈ [0x20, 0x7E] ∪ 合法 UTF-8 多字节，否则跳过该字节。
- 输出 `{offset, end, byteLength, text}`。

### 5.3 可用空间 `safe_span(offset, byteLength)`
```
要求 blob[offset+byteLength] == 0x00（否则不是独立字符串 → null）
向后跳过连续 0x00，可用字节数 = 首个非零位置 - offset - 1（末尾保留 NUL）
```

### 5.4 单条补丁 `patch_one`
| 状态 | 条件 |
|---|---|
| `skipped` | 该位置原文不匹配（已打过补丁）／不是独立字符串（前缀） |
| `too-long` | `len(zh_utf8) > safe_span` → **改短译文，不重定位** |
| `patched` | 写入中文，其后到 `stop` 全部填 `0x00` |

### 5.5 硬性不变量（`Verifier` 逐条断言）
1. 文件大小与原版**完全相等**
2. `[:0x400]`（PE 头）不变
3. `[-0x1000:]`（文件尾）不变
4. 所有差异字节 ⊆ 声明过的补丁 span 并集
5. 补丁后 sha256（对已知版本）= `deebc68e…`

> 任何一条不成立 → 视为失败，GUI 自动从备份回滚。

---

## 6. Platform 规格

### 6.1 `IExeWriter`（安全替换）
| 平台 | 策略 |
|---|---|
| Windows | ①写 `<父目录>\X-Plane.exe.stage` →②`File.Replace`/`os.replace` 回游戏目录；失败回落 `%TEMP%`（同卷才 rename）。原因见 2.4-6 |
| Linux | ①写同目录临时文件 →②`chmod 0755`（**必须**，否则丢可执行位）→③`rename(2)` 原子覆盖；跨卷/EXDEV 回落「备份到工作目录 + 直接覆盖写入」 |

统一行为：写前校验目标可写、磁盘剩余空间 ≥ exe 大小 ×2；写后重读校验大小与哈希。

### 6.2 `GameLocator`（自动找游戏）
**Windows 候选**：命令行参数 → `%XP12_ROOT%` → 注册表 Steam 安装路径 → 解析各 Steam 库的 `libraryfolders.vdf` → 各库下 `steamapps/common/X-Plane 12` → 从当前目录/程序目录向上搜索 → 记住上次路径（程序目录 `.zh_cn_translate/settings.json`）。

**Linux 候选**：`$XP12_ROOT` → `~/.steam/steam/steamapps/libraryfolders.vdf` → `~/.local/share/Steam/...` → Flatpak `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/...` → Snap `~/snap/steam/common/.local/share/Steam/...` → 各库里 `steamapps/common/X-Plane 12` → 记住上次路径（同 `.zh_cn_translate/settings.json`），**大小写兜底**：精确找不到时列目录做忽略大小写匹配。

判定「是游戏目录」的依据：目录下存在 X-Plane 可执行文件——`X-Plane.exe`（PE）或
`X-Plane-x86_64` / `X-Plane`（Linux 原生 ELF）。**可执行文件解析**（`Platform/GameExecutable.cs`）：
按平台候选顺序找存在的文件，读魔数（MZ/ELF）自动判定类型；Linux 上原生版优先。

### 6.3 `ProcessProbe`
Windows：`Process.GetProcessesByName("X-Plane")`。Linux：扫描 `/proc/*/comm`、`/proc/*/exe` 链接名匹配 `X-Plane`（覆盖 `X-Plane.exe` 与 `X-Plane-x86_64`）`|wineserver|wine64-preloader|proton`。检测到游戏在跑 → **禁止打补丁**（仍允许检查/预演）。

### 6.4 `PathPolicy`
- 工作目录：**程序目录**下的 `.zh_cn_translate/`（`backup/` + `data/` + `log.txt` + `settings.json`）
  → 实施时按用户要求从「游戏目录下」改到「程序目录下」（便携、不污染游戏目录）
- 备份命名：`<可执行文件名>.<sha256前12位>.bak`（如 `X-Plane.exe.2ca099b7c6b2.bak`、
  `X-Plane-x86_64.0fe6bc5c5d66.bak`）；备份列表按当前可执行文件名过滤，两种版本互不混用。

---

## 7. Wine 字体检测与提示（本轮新增）

### 设计依据
游戏自带并加载 `Resources/fonts/NotoSansCJK-SC-Regular.otf`（§2.3 实测），因此**正常情况下 Wine 下中文渲染没有问题**。
本模块的价值是**兜底告知**：字体文件被删/损坏时，让用户得到「字幕显示为方块」的确切原因和修复路径，而不是一脸茫然。

### 检测逻辑（只读，绝不修改任何文件）
| 级别 | 检查项 | 判定 |
|---|---|---|
| L1 必需 | `<游戏目录>/Resources/fonts/NotoSansCJK-SC-Regular.otf` 存在 | 缺失 → ⚠ |
| L2 完整性 | 文件 ≥ 15 MB、前 4 字节 = `OTTO`、能解析 `cmap` 且抽样字符（`这就是中国飞行教学起飞着陆襟翼航向`）全部有字形 | 任一失败 → ⚠「字体文件损坏」 |
| L3 参考信息 | Linux：`fc-list :lang=zh` 是否有输出；Windows：系统是否装中文字体 | 仅展示，**不告警**（游戏用自带字体） |

### 提示文案（只在 L1/L2 失败时出现，非阻塞，可继续打补丁）
> ⚠️ 游戏自带的思源黑体（NotoSansCJK-SC-Regular.otf）缺失或损坏。
> 补丁本身没问题，但中文可能显示为方块。
> 修复：Steam → 右键 X-Plane 12 → 属性 → 已安装文件 → **验证游戏文件完整性**。
> （本工具不会自动修改游戏文件以外的任何内容。）

### 明确不做
- ❌ 不往 Proton prefix 装字体、不改 `drive_c/windows/Fonts`
- ❌ 不执行 `sudo`/包管理器命令（提示文案里给出命令文本供用户自行复制）
- ❌ 不因字体告警阻止打补丁

---

## 8. GUI 规格

### 8.1 窗口布局（单窗口，可缩放，最小 780×560）
```
┌─ X-Plane 12 飞行教学汉化  v0.1.0 ────────────────────────────┐
│ 游戏目录 [ F:\SteamLibrary\steamapps\common\X-Plane 12 ] [浏览]│
│           ✓ 已识别 X-Plane 12.4.3 · 91,132,488 字节 · 未打补丁 │
│           ⚠ 字体检查通过（自带思源黑体）                        │
├──────────────────────────────────────────────────────────────┤
│ 译文 513 条 ｜ 已应用 0 ｜ 待应用 505 ｜ 不适用 8 ｜ 候选字符串 614 条 ｜ 备份 0 份 │
├──────────────────────────────────────────────────────────────┤
│ [ 检查状态 ] [ 预演 ] [ 打补丁 ] [ 还原原版 ] [ 备份… ]        │
│ ████████████████░░░░░░░░  62%   正在写入字幕 320/513           │
├──────────────────────────────────────────────────────────────┤
│ 日志                                                    [复制][导出]│
│ [20:11:03] 已识别游戏目录，读取 exe 91,132,488 字节            │
│ [20:11:04] 预演完成：513 条可写入，0 条放不下                   │
└──────────────────────────────────────────────────────────────┘
```

### 8.2 状态机
`未定位` → `已定位-未打补丁` ↹ `已打补丁` ；任意状态可进入 `忙碌`、`错误`、`需回滚`。
按钮可用性随状态变化；`打补丁` 在「游戏运行中」「已有补丁」「无可写权限」时禁用并给出原因 tooltip。

### 8.3 关键交互
| 场景 | 行为 |
|---|---|
| 首次启动 | 自动搜索游戏目录；找到唯一目标 → 直接定位；找到多个 → 下拉选择 |
| 拖拽文件夹进窗口 | 校验后设为游戏目录 |
| 打补丁 | 确认框（列出：备份位置、将写入条数、文件大小不变、当前版本）→ 打 → 自动 `verify` → 成功/回滚 提示 |
| 还原原版 | 列出可用备份（时间/大小/哈希），选一个还原，还原后自动校验 |
| 校验失败 | 自动回滚到打补丁前的备份，日志给出差异摘要，状态回 `已定位-未打补丁` |
| 权限不足 | 显示可复制的命令（Linux `sudo ...`，Windows「以管理员身份运行」），不弹异常栈 |
| 日志 | 带时间戳；可复制、可导出 `patch_report.tsv` |
| 启动参数 | 支持 `--path "..."` 直接定位 |

### 8.4 体验细节
- 界面语言：简体中文（目标用户）；字体内嵌 `Noto Sans SC` 子集（约 300–600 KB），双平台显示一致
- 高 DPI：manifest 声明 PerMonitorV2，Avalonia 自身已处理缩放
- 记住上次目录、窗口大小/位置
- 全键盘可达（Tab 顺序、回车触发主操作、Esc 关闭对话框）
- 不做联网、不做自动更新检查（可在 v1.1 加「检查新版本」按钮，手动触发）

---

## 9. CLI 规格（无界面模式，CI 与 headless Linux 用）

> ⚠️ **本节是设计稿，参数名与实现已有出入**：实现里游戏目录一律用 `--path <dir>` 传入
> （不是位置参数 `<dir>`），且 `--yes` 未实现（未验证版本用 `--force`）。
> 以 [AGENT.md §6](../../AGENT.md) 的实际用法为准。

```
xpzhcn                          # 无参数 → 打开 GUI
xpzhcn --selftest               # 合成数据自检（不读游戏文件）
xpzhcn --check      <dir>       # 词条现状统计
xpzhcn --dry-run    <dir>       # 预演：报告放不下的条目，不写盘
xpzhcn --apply      <dir> [--yes]
xpzhcn --verify     <dir>
xpzhcn --restore    <dir> [--backup <name>]
xpzhcn --list-backups <dir>
xpzhcn --font-check <dir>       # §7 字体检测
xpzhcn --report     <dir>       # 【已废弃，未实现】导出 patch_report.tsv
xpzhcn --version
```

> `--report` **没有实现**：逐条补丁结果由程序自动写到 `<程序目录>/.zh_cn_translate/data/patch_report.tsv`，
> 不需要命令行参数（见 AGENT.md §3.1）。

| 退出码 | 含义 |
|---|---|
| 0 | 成功 |
| 1 | 可恢复失败（有译文放不下 / 游戏运行中 / 权限不足） |
| 2 | 校验失败或已回滚 |
| 3 | 参数或环境错误（找不到游戏、锚点失效、版本不支持） |

---

## 10. 构建与发布

### 10.1 项目配置要点
```xml
<TargetFrameworks>net9.0</TargetFrameworks>
<RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>
<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
<InvariantGlobalization>false</InvariantGlobalization>   <!-- 需要中文区域/排序 -->
<DebugType>none</DebugType>
```
- `translations/zh.csv` 与内嵌字体、图标通过 `<EmbeddedResource>` 打包
- 运行时优先读 exe 同目录 `translations/zh.csv`（存在则覆盖内嵌版，便于玩家自己修词条）
- 裁剪（`PublishTrimmed`）**先不开**：Avalonia 裁剪有反射风险，稳住体积换正确性；实测没问题再评估

### 10.2 产物
| 平台 | 文件名 | 用户操作 |
|---|---|---|
| Windows | `XPlane12-FlightLessons-zh-CN-win-x64.exe` | 双击 |
| Linux | `XPlane12-FlightLessons-zh-CN-linux-x64.tar.gz`（内含 `xpzhcn` + `README.txt`） | `tar xzf … && ./xpzhcn` |

命名统一 ASCII，避免下载/杀软/命令行编码问题；界面标题用中文。
Linux 侧 `AppImage` 作为 v1.1 加分项（CI 加 `appimagetool`）。

### 10.3 CI（`.github/workflows/build.yml`）
```yaml
strategy.matrix.os: [windows-latest, ubuntu-latest]
steps:
  - checkout
  - actions/setup-dotnet@v4 (9.0.x)
  - dotnet test（Core 单测 + --selftest）
  - dotnet publish -r ${{ rid }} -c Release -o dist
  - （linux）tar czf …；chmod +x 校验
  - upload-artifact
on: tag v* → 自动创建 Release 并附两个产物 + SHA256SUMS
```

> CI 是**权威 Linux 产物来源**：保证 Linux 二进制在 Linux 上构建、并在 `ubuntu-latest` 上跑通 `--selftest` 与样本端到端测试。

---

## 11. 验证计划

| 层级 | 内容 | 通过标准 |
|---|---|---|
| V1 单元 | `safe_span`/`patch_one`/`occurrences`/锚点兜底 | 全部边界用例（超长、前缀、重复、锚点缺失）通过 |
| V2 自检 | `--selftest`（合成 4 KB 缓冲区，不读游戏文件） | 退出码 0，CI 双平台都跑 |
| V3 金标准 | exe **副本**上完整打补丁 | sha256 == `deebc68e…`，大小/头/尾不变 |
| V4 跨平台 | Windows 产物 与 Linux 产物 对同一副本各打一次 | 两个输出**逐字节相同**（再比对哈希） |
| V5 平台特性 | Linux `chmod 0755`、rename 原子性、跨卷回落；Windows 完整性标签绕过 | 手动 + 脚本各一次 |
| V6 字体探测 | 删掉/截断 OTF 的副本上跑 `--font-check` | L1/L2 正确告警，且不阻塞 `--apply` |
| V7 回滚 | 人为制造 verify 失败 | 自动回滚，游戏 exe 哈希恢复为原版 |

> V3/V4 需要一个 91 MB 的 exe 副本（本地临时目录，不入库）；CI 只跑 V1/V2/V6 的合成样本版本。

---

## 12. 分发与运维

| 事项 | 方案 |
|---|---|
| 杀软误报 | 不做自解压打包、公开源码与构建脚本、Release 附 `SHA256SUMS`；README 写明误报处理（加白/上报） |
| SmartScreen | 无代码签名 → 首次运行提示「未知发布者」；README 给出「更多信息 → 仍要运行」说明，并提示可用 SHA256 核对 |
| 游戏更新后 | 版本识别：已知哈希 → 显示版本号；未知 → 显示「未验证版本，将尝试按锚点定位」；锚点失效 → 明确报错并引导更新工具 |
| Steam 校验/更新覆盖 | 工具检测到 exe 哈希变化 → 界面提示「补丁已失效，重新打一次」，一键重打 |
| 备份占用 | 每个原版备份 ~87 MB；界面显示占用并支持清理旧备份（保留最新 1 份） |
| 仓库卫生 | 不提交 `.bak`、`data/`、任何游戏文件；`.gitignore` 沿用 |

---

## 13. 风险清单

| 风险 | 概率 | 影响 | 应对 |
|---|---|---|---|
| 沙箱阻断进程级 TLS，无法 `dotnet restore` | 高（当前环境已实测） | 本机无法出 Linux 产物 | 走 CI 构建；或申请一次联网构建 |
| Avalonia 自包含体积超预期（>60 MB） | 中 | 下载体验 | 先实测，必要时评估 trim / 换 Web UI 方案 |
| Linux 缺 X11 库导致 GUI 起不来 | 低 | 桌面发行版一般都有 | 启动时捕获异常并提示安装 `libx11-6 libice6 libsm6 fontconfig`；headless 用 CLI |
| Wine 下中文仍显示方块（即使自带字体） | 低 | 功能失效 | §7 检测 + 提示；README 给验证步骤 |
| 游戏更新导致锚点/文案变化 | 中（长期必然发生） | 补丁失效 | 明确报错 + 版本识别 + 文档化更新锚点流程 |
| Steam Deck 外置 exFAT 库权限/rename 差异 | 低 | 写入失败 | 回落覆盖写入 + 明确提示 |
| 双平台行为漂移 | 中 | 结果不一致 | V4 跨平台逐字节比对 + CI 双平台 selftest |

---

## 14. 里程碑

| # | 里程碑 | 内容 | 产出 |
|---|---|---|---|
| M0 | 基线固化 | 记录原版/打完两个哈希与统计（§2.1 已完成） | 已完成 |
| M1 | Core + CLI | 移植字节逻辑、`--selftest`、副本上跑出金标准哈希 | 可在终端完成全部功能 |
| M2 | Platform | 双平台定位/写入/进程/字体探测 | Linux 侧可用 CLI 自测 |
| M3 | GUI | Avalonia 主窗口 + 状态机 + 日志 + 回滚 | 可视化可用 |
| M4 | 构建发布 | csproj 发布配置、CI matrix、双平台产物 | Release 素材 |
| M5 | 文档 | README（小白 3 步）、AGENT.md（架构/踩坑） | 可交付 |

工作量估计：M1–M2 为主（约 600–800 行 C#），M3 约 500–700 行 XAML/C#，M4–M5 约 200 行配置与文档。

---

## 15. 待决策（默认值已写入方案，可直接推翻）

| # | 决策点 | 默认 | 备选 |
|---|---|---|---|
| D1 | GUI 技术栈 | .NET 9 + Avalonia | Rust+egui（12 MB，需 Linux 端构建）/ 本地 Web UI（12 MB，浏览器界面） |
| D2 | Linux 产物从哪来 | GitHub Actions 构建（沙箱无法联网） | 批准本机联网构建一次 / 你在 Linux 上跑 `build.sh` |
| D3 | Linux 交付形态 | `tar.gz` | AppImage（免解压）/ 两者都要 |

---

## 16. 实施结果与偏差（v0.1.0）

| 项 | 方案 | 实际 |
|---|---|---|
| 技术栈 | .NET 9 + Avalonia | ✅ .NET 9 + **Avalonia 11.3.23**（未用 12.x，避开破坏性变更） |
| 构建 | 优先本地，否则 Actions | ✅ 本地构建成功（放开权限后 NuGet 可用）；CI 仍作为双平台权威构建，见 `.github/workflows/build.yml` |
| Windows 交付 | 单文件 exe | ⚠ **改为便携目录 + zip**：单文件 + 原生库自解压要求启动时写 `%TEMP%\.net`，受限系统直接失败且易被误判；现为 `exe + 3 个原生 dll` 后打 zip（**单文件变体已移除，不再提供**） |
| Linux 交付 | tar.gz | ✅ tar.gz（含可执行位 0755） |
| 体积 | 30–40 MB | Windows 便携包 **≈53 MB**（exe 39 MB + libSkiaSharp/av_libglesv2/libHarfBuzzSharp 合计 16.7 MB） |
| 测试 | selftest + 金标准 | ✅ `tests/SmokeTests`（核心自检 + GUI headless 启动）；真 exe 副本端到端验证得到 `deebc68e…` |
| 字体 | 检测 + 提示 | ✅ 已实现；实测游戏自带字体 22/22 抽样字形齐全 |

新增的关键实现约定：**兼容清单** `compat/known-versions.json` + **未验证版本二次确认** +
**写入前后双向 sha256 校验**（详见 AGENT.md §2）。
