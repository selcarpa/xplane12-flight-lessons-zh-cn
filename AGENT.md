# AGENT.md — 维护者 / AI 接手指南

这份文件写给后续接手的人（或 AI）：记录**已验证的事实**、**为什么这样设计**、**踩过的坑**，
避免重复试错。改代码或继续翻译前请先读完。

---

## 1. 已验证的事实（不要重复验证）

### 1.1 游戏与补丁对象

| 项目 | 值 |
|---|---|
| 版本 | X-Plane **12.4.3-r2**，build **124311**，Intel 64-bit |
| Steam AppID | **2014780** |
| `X-Plane.exe` 大小 | **91,132,488** 字节 |
| 原版 sha256 | `2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936` |
| 打完补丁 sha256 | `36f5b21eb869e94e75a71de0aa7a4235976e16677424ad5421a45ac6b367131e` |
| 文本区间 | `41,552,520 .. 41,612,728`（起止锚点见 §4） |
| 区间内候选字符串 | **621** 条 |
| 译文条数 | **513**（8 课程标题 + 505 口播字幕） |
| 补丁统计 | patched **513** / skipped **8**（前缀重叠）/ too-long **0** / not-found **0** |
| 改动量 | **787 段 / 45,186 字节**（占文件 0.0496%） |
| 文件结构 | 大小、PE 头 `[:0x400]`、文件尾 `[-0x1000:]` 均不变 |

> 🎯 **金标准**：任何实现（Windows / Linux）在 exe 副本上打完补丁都必须得到
> `36f5b21e…`。C# 版已实测逐字节一致。

### 1.2 字体（决定「Wine 下中文会不会变方块」）

```
Resources/fonts/NotoSansCJK-SC-Regular.otf   16,412,332 字节   magic=OTTO
  cmap 覆盖：U+4E00–U+9FFF 20,945/20,992 ｜ U+3400–U+4DBF 6,582/6,592 ｜ U+3000–U+30FF 253/256
X-Plane.exe 里引用了该文件名（NotoSansCJK-SC-Regular ×1、Resources/fonts/ ×8）
```

**结论**：字幕渲染用**游戏自带字体**，与系统字体无关；Wine/Proton 下干净 prefix 也不需要装中文字体。
工具只做**检测 + 提示**（`WineFontProbe`），不装字体、不改 prefix。

### 1.3 Wine / Proton 场景

- Wine 跑的就是同一个 PE `X-Plane.exe`，字节补丁逻辑 100% 复用，**没有第二套实现**。
- 直接在 Linux 文件系统上改 Steam 库里的文件，**不进 Wine prefix**。
- Linux 没有 NTFS「低完整性标签」问题，但**必须保住可执行位**（见 §4.6）。

### 1.4 教学文本为什么在 exe 里

课程标题与口播字幕是**编译进只读数据段的 UTF-8 C 字符串**，运行时按地址引用，
不走 `Resources/text/*.txt` 翻译表。所有语言表里都没有这些字符串（Laminar 没提取）。

---

## 2. 兼容清单与校验链（重要）

### 2.1 清单

[`compat/known-versions.json`](compat/known-versions.json) 记录「哪些 exe 版本可信」：

```json
{
  "id": "12.4.3-r2-124311",
  "displayName": "X-Plane 12.4.3-r2 (build 124311)",
  "status": "verified",
  "exeSize": 91132488,
  "sha256": "<原版 sha256>",
  "patchedSha256": "<打完补丁的 sha256>",
  "regionStart": 41552520, "regionEnd": 41612728,
  "candidates": 621, "entries": 513,
  "patchStats": { "patched": 513, "skipped": 8, "tooLong": 0 },
  "platforms": ["windows", "linux-wine"],
  "verifiedOn": "2026-11-15"
}
```

**当前只收录 1 个版本**（12.4.3-r2）。清单同时被编译进 exe；exe 同目录的
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
5. 提交时附上验证数据（版本、大小、两个 sha256、统计）。

---

## 3. 代码结构

```
src/
├─ Core/                 平台无关，纯字节逻辑
│  ├─ PeImage.cs         锚点定位、区间、字符串枚举、safe_span、只读字符串解析
│  ├─ PatchEngine.cs     occurrences + patch_one + 统计（返回改动区间）
│  ├─ Verifier.cs        结构不变量 + 改动范围校验
│  ├─ CompatManifest.cs  兼容清单模型 / 内置资源加载
│  └─ Translations.cs    TSV 解析（内置 + 外部覆盖）
├─ Platform/             平台差异全在这里（Core 里不允许出现 OperatingSystem 判断）
│  ├─ GameLocator.cs     定位游戏：参数 → 环境变量 → 上次 → Steam VDF → 向上搜索
│  ├─ ExeWriter.cs       原子替换（Windows 写父目录；Linux chmod 0755 + rename）
│  ├─ ProcessProbe.cs    游戏是否在运行（Windows 进程名 / Linux /proc）
│  ├─ WineFontProbe.cs   自带字体完整性检测（检测+提示，不安装）
│  ├─ SfntCmap.cs        最小 sfnt/cmap 解析（判断字体有无某字形）
│  ├─ PathPolicy.cs      工作目录 = **程序目录**/.zh_cn_translate（便携，不写游戏/用户目录）
│  ├─ RunLog.cs          运行日志 log.txt（GUI 与 CLI 都写）
│  └─ Elevation.cs       管理员/root 判断 + Windows 提权重启
├─ Service/PatchService.cs   Analyze / Apply / DryRun / Verify / Restore / ListBackups
├─ Cli/                  CommandLine、Selftest、ConsoleBridge
└─ Gui/                  App、MainWindow、ConfirmWindow、UiFonts（Avalonia 11.3.x）
tests/SmokeTests/        核心自检 + GUI headless 启动（CI 跑这个）
```

`Cli` 与 `Gui` 共用同一个 `PatchService`，不允许在 GUI 里重复实现补丁逻辑。

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
8. **`patch_one` 的 `stop` 必须在写入前算**：中文比英文长时，写入后再扫 0 会把区间算短，
   导致结构校验误报「越界」。
9. **译文表格式**：只认 `#` 注释与 `英文<TAB>中文`；译文里不能有制表符/换行。

---

## 5. 开发中踩的坑

1. **cmap 子表偏移是相对 cmap 表起点的**，不是文件绝对偏移。漏加基址会导致 `SfntCmap.TryOpen`
   返回 null（字体检测报「无法解析 cmap」）。
2. **单文件 + 原生库自解压不可用**：`IncludeNativeLibrariesForSelfExtract=true` 要求启动时把
   原生库解压到 `%TEMP%\.net`，受限系统/杀软环境下直接启动失败（错误码 5）。
   → 改为 `IncludeNativeLibrariesForSelfExtract=false`，产出**便携目录**（exe + 4 个文件）再打 zip。
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

---

## 6. 构建与验证

```powershell
# Windows：构建 + 冒烟测试 + 打包 win-x64
.\build.ps1
```

```bash
# Linux：构建 + 冒烟测试 + 打包 linux-x64
./build.sh
```

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
#   → 36f5b21eb869e94e75a71de0aa7a4235976e16677424ad5421a45ac6b367131e
# 5) 校验 / 还原
... --verify --path "<副本目录>"
... --restore --path "<副本目录>"
```

其他验证：

- `--selftest`：合成数据跑边界用例（超长拒绝、前缀跳过、重复写入拒绝、锚点失效、清单完整性）
- `dotnet run --project tests/SmokeTests`：自检 + GUI headless 启动（CI 双平台都跑）
- `--font-check`：真实 OTF 解析（应报「抽样 22/22 字形齐全」）
- 未验证版本：改副本 1 个字节 → `--apply` 应拒绝（exit 1），`--apply --force` 才执行

---

## 7. 游戏更新后怎么办

1. 跑 `--check`。若报「找不到教学文本锚点」→ 锚点失效：在 `Resources/text` 附近的字符串里
   重新挑一个**不会被翻译**的锚点，改 `Core/PeImage.cs` 的常量。
2. 若区间正常但 `--dry-run` 报大量 `not-found`/`too-long` → 课程文案改写了：按新英文更新译文表。
3. 若只是版本变了但文案没变 → 走 §2.3 把新版本加进兼容清单。
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
- **8 条 skipped**：都是更长字符串的前缀（如 `Flaps up!`），不是漏译。
- **~22 条评分项标签**（`Throttle Control.` / `RPM Control` / … / `Pass/Fail`）未翻译，
  且**不在任何语言表里**；如果要译，需要新加译文并在清单里更新 patched sha256（会改变金标准）。
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

1. `build` job 在 Windows / Linux 各跑一遍：冒烟测试 → 发布便携版（Windows 为 zip、Linux 为 tar.gz）→ 校验产物能启动 → 上传 artifact。
2. `release` job 等两个平台都成功后，下载全部 artifact、合并成一份 `SHA256SUMS.txt`，创建 Release 并挂上
   `zip` / `tar.gz` / `SHA256SUMS.txt`。
3. tag 名里带 `-` 的（如 `v0.1.0-snapshot`）**自动标记为预发布**，不会顶掉“Latest release”。

注意：

- **版本号来源**：`src/XpLessonZhCN.csproj` 的 `<Version>` 是本地默认值；CI 用 tag 覆盖
  （`-p:Version=${tag#v}`），所以 `--version` 显示的一定是 tag 版本。改版本只需改 tag 或同步改 csproj。
- **仓库设置**：Settings → Actions → General → Workflow permissions 需要允许
  **Read and write**，否则 `release` job 拿不到写权限（403）。workflow 里已声明 `contents: write`。
- 只在打 tag 时发布；推 main、PR、手动触发都只构建不发布。
- 本地预览发布产物：`./build.ps1`（Windows）、`./build.sh`（Linux），产物在 `dist/`，不会上传。

---

## 11. 禁止事项

- 不要改代码段、不要新增/移动节、不要做字符串重定位。
- 不要为了「让译文放得下」而覆盖相邻字符串或对齐填充之后的字节。
- 不要把游戏文件、备份 exe、机器相关的路径提交进仓库。
- 不要在 `Core/` 里写平台判断；平台差异只能进 `Platform/`。
- 不要跳过「写完重读校验」这一步。
