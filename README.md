# X-Plane 12 飞行教学汉化

给 **X-Plane 12 的「飞行教学」（Flight Lessons，Austin 的飞行小课堂）** 汉化**课程标题**与**口播字幕**。

- 图形界面，**不需要安装任何运行环境**，解压双击即用
- 支持 **Windows** 与 **Linux（Steam / Proton / Wine，以及 Linux 原生版）**，x64 与 ARM64 都有包（ARM64 未经实机测试）
- **自动检测可执行文件类型**：Windows 版 `X-Plane.exe`（PE）或 Linux 原生版 `X-Plane-x86_64`（ELF），选哪个都能打
- 自动备份原版、打完自动校验、失败自动回滚，**不改变游戏文件大小、不动代码段**

> Austin 的录音仍是英文；本工具汉化的是**字幕**和**课程标题**。

> **只想打补丁？** 看 [使用说明](docs/使用说明.md) —— 3 步，一页看完。

---

## 一、下载与使用

### 先按 CPU 架构选包

| 你的系统 | 下载 |
|---|---|
| Windows x64（绝大多数 Intel / AMD 电脑） | `XPlane12-FlightLessons-zh-CN-win-x64.zip` |
| Windows ARM64（骁龙 X 等 ARM 笔记本） | `XPlane12-FlightLessons-zh-CN-win-arm64.zip` ⚠️ 未经实机测试 |
| Linux x64（常见发行版、Steam Deck） | `XPlane12-FlightLessons-zh-CN-linux-x64.tar.gz` |
| Linux ARM64（ARMv8-A / arm64-v8a） | `XPlane12-FlightLessons-zh-CN-linux-arm64.tar.gz` ⚠️ 未经实机测试 |

> 不确定就选 x64：绝大多数电脑都是 x64。ARM64 包只在 ARM 系统上使用；
> X-Plane 12 游戏本体仍是 x64（Windows on ARM 下由系统模拟运行）。

> ⚠️ **ARM64 包未经实机测试（实验性）**：作者没有 Windows on ARM / ARM64 Linux 硬件，
> `win-arm64` / `linux-arm64` 只通过 CI 的冒烟测试与 `--selftest`（合成数据），没有在真实
> ARM 设备上运行过，也没有对真实游戏目录打过补丁。在 ARM 设备上使用前请先备份原版，
> 建议先用「预演（不写盘）」/ `--dry-run` 确认；遇到问题欢迎反馈。

### Windows

1. 下载上表里对应你系统的 zip，解压到任意目录（例如 `D:\Tools\XP12汉化`）
2. **完全退出 X-Plane 12**，双击 `XPlane12-FlightLessons-zh-CN.exe`
3. 程序会自动找到游戏目录；没找到就点「浏览…」选到游戏根目录（含 `X-Plane.exe` 的那一层）
4. 点「打补丁」→ 确认 → 看到「补丁完成并通过校验」即可启动游戏

> 解压后 `exe` 与 3 个 `.dll` 要放在同一目录，不要把 exe 单独拷走。

### Linux（Steam / Proton 与 Linux 原生版）

```bash
# 按你的架构二选一：
tar xzf XPlane12-FlightLessons-zh-CN-linux-x64.tar.gz -C ~/apps/xpzhcn   # x64
# tar xzf XPlane12-FlightLessons-zh-CN-linux-arm64.tar.gz -C ~/apps/xpzhcn  # ARM64（ARMv8-A / arm64-v8a）

chmod +x ~/apps/xpzhcn/XPlane12-FlightLessons-zh-CN
~/apps/xpzhcn/XPlane12-FlightLessons-zh-CN
```

程序会自动检测游戏目录里的可执行文件类型：

- **Linux 原生版**：Steam 默认安装的 `X-Plane-x86_64`（ELF）→ 直接打补丁；
- **Steam / Proton / Wine**：游戏目录里的 `X-Plane.exe`（PE）→ 与 Windows 版完全相同的补丁逻辑；
- 两者都在时优先原生版（Linux 上）；备份按文件名分开存放，互不混用。

> Windows 与 Linux 原生版**共用同一份译文表**（`translations/zh.csv`，CSV 维护）：
> 每个词条只有一条译文，两个平台显示一致；每条还记录了允许写入的字节上限（`budget_bytes`），
> Linux 原生版字符串紧凑，超限的译文会被拒绝并提示。

游戏目录通常是 `~/.steam/steam/steamapps/common/X-Plane 12`（或你 Steam 库里对应的位置），
程序会自动在常见位置里找，也可以手动「浏览…」选择。

> 需要桌面环境（X11 / Wayland）。纯 SSH 无界面环境请用 [命令行模式](#六命令行模式高级)。
> **Steam Deck**：切到「桌面模式」运行即可。

### 三个按钮就够了

| 按钮 | 作用 |
|---|---|
| **检查状态** | 读 exe、算 sha256、显示版本 / 词条 / 字体情况，不改任何文件 |
| **打补丁** | 备份 → 写入中文 → 自动校验（失败自动回滚） |
| **还原原版** | 用备份恢复原版 exe（Steam「验证游戏文件完整性」也可以） |

「预演（不写盘）」可以提前看有没有放不下的译文；「打开备份目录」可以管理备份。

---

## 二、安全设计

| 机制 | 说明 |
|---|---|
| 自动备份 | 首次打补丁前把原版 exe 备份到 **程序目录**下的 `.zh_cn_translate\backup\` |
| 只改字符串 | 只替换只读数据段里的 UTF-8 字符串，**长度只减不增**，不足补 `0x00` |
| 结构不变 | 文件大小、PE 头、文件尾都不变；改动范围校验通过才允许写盘 |
| 版本校验 | 打补丁前核对 exe 的 sha256 是否在 [兼容清单](compat/known-versions.json) 里 |
| 二次确认 | **版本未经验证**时，必须勾选确认才能继续；否则直接拒绝 |
| 写完再验 | 写入后重新读取文件核对 sha256，不一致立即用备份还原 |
| 游戏运行检测 | 检测到 X-Plane 正在运行会拒绝打补丁 |
| 字体检测 | 检查游戏自带中文字体是否完好；缺失时提示用 Steam 验证完整性（**只提示，不装字体**） |

程序**只在自己身边**写文件，不往游戏目录、也不往用户目录塞东西：

```
<程序目录>/
├─ XPlane12-FlightLessons-zh-CN.exe    主程序
├─ *.dll                               运行库（保持同目录，别单独挪走 exe）
└─ .zh_cn_translate/                   自动生成
   ├─ backup/  原版 exe 备份（每份约 55–91 MB，取决于游戏版本）
   ├─ data/patch_report.tsv             逐条补丁结果
   ├─ log.txt                           运行日志（出问题时把这个发出来）
   └─ settings.json                      记住上次选的游戏目录
```

所以整个程序可以放心放在 U 盘或任意可写目录里，挪走就等于"清空配置"，但不影响游戏。

---

## 三、兼容性

| 项目 | 当前支持 |
|---|---|
| X-Plane 12 | **12.4.4-r2 (build 124419)** 与 **12.4.3-r2 (build 124311)**；Windows 版与 Linux 原生版均已支持 |
| Windows 12.4.4 `X-Plane.exe` 原版 sha256 | `5b18cc7d406bdf22be20135849b8705622cef6882edbb6e8d7c54e095b712563` |
| Windows 12.4.4 打完补丁 sha256 | `8b3e152150043b9f7e32ee520d895354e537fc5eb5c418aa39e0048b0ecfabb7` |
| Windows 12.4.4 exe 大小 | 55,022,152 字节（打补丁前后不变） |
| Windows 12.4.3 原版 sha256 | `2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936` |
| Windows 12.4.3 打完补丁 sha256 | `d021f2656852416686e9f9c6222db90111ef791eea7967c839ade03e2e7efdbb` |
| Windows 12.4.3 exe 大小 | 91,132,488 字节（打补丁前后不变） |
| Linux 12.4.4 `X-Plane-x86_64` 原版 sha256 | `565ac3942e952794d5625e04b2147543f2fca2f027942f5f205d142e52728fce` |
| Linux 12.4.4 打完补丁 sha256 | `8b29f772ce110ba9df92335de14f181be487faedb7ce8aa02acbfc11feca295f` |
| Linux 12.4.4 exe 大小 | 78,659,600 字节（打补丁前后不变） |
| Linux 12.4.3 原版 sha256 | `0fe6bc5c5d661633840bc5eba0de88d8def190f1ff40f6a7011d9252cc63eabb` |
| Linux 12.4.3 打完补丁 sha256 | `52218d9089e491f496bf96701989ab5b0a83eb76061086b21e0b9d786b88480e` |
| Linux 12.4.3 exe 大小 | 76,056,352 字节（打补丁前后不变） |
| Windows | Windows 10 1809+ / 11，x64 与 ARM64（自包含，无需装 .NET） |
| Linux | 桌面发行版 + Steam/Proton，或 SteamOS 桌面模式；**Linux 原生版直接支持**；提供 x64 与 ARM64 包 |
| ARM64（Windows on ARM / ARM64 Linux） | ⚠️ 提供 `win-arm64` / `linux-arm64` 包，但**未经实机测试**（作者无 ARM 硬件，见上文说明） |

兼容信息全部记录在 [`compat/known-versions.json`](compat/known-versions.json)。
每次 Release 的说明里会自动列出「兼容版本」表（版本、平台、原版/补丁后 sha256、验证日期）。
游戏更新后如果要支持新版本，改这个清单即可（见 [AGENT.md](AGENT.md)）。

---

## 四、常见问题

**Q：提示「没有写入游戏目录的权限」/ 日志里出现 `Access to the path ... is denied`？**
说明当前账户不能写游戏目录。程序会弹窗说明原因，并询问是否**以管理员身份重启**（Windows 会弹 UAC，
点「是」后程序带着当前游戏目录自动重开，再点一次「打补丁」即可）。
Linux 上程序不会自动提权，按弹窗里给出的 `sudo ...` 命令重新运行即可。
如果已经是管理员仍写不进去，多半是文件被占用或安全软件保护，先用 Steam 验证游戏文件完整性再试。

**Q：打完补丁进游戏还是英文？**
1. 确认程序提示「补丁完成并通过校验」；
2. 完全退出游戏再进（X-Plane 会缓存，需要重启）；
3. Steam 校验/更新会覆盖可执行文件，重新打一次即可；
4. 字幕只在「飞行教学」里，不在普通自由飞行里；
5. 确认打的是当前用的那个版本：Linux 原生版打 `X-Plane-x86_64`，Windows/Proton 打 `X-Plane.exe`（程序会自动识别）。

**Q：中文显示成方块 / 乱码？**
程序会检查游戏自带的 `Resources/fonts/NotoSansCJK-SC-Regular.otf`。如果提示字体缺失或损坏，
用 Steam → 右键 X-Plane 12 → 属性 → 已安装文件 → **验证游戏文件完整性** 修复。
（不需要给系统或 Wine 安装中文字体——游戏用的是自带字体。）

**Q：杀毒软件报毒 / Windows 提示"未知发布者"？**
本程序会修改游戏 exe，部分杀软对这类行为敏感。程序不含任何联网、自解压或混淆代码，
可核对发布页的 `SHA256SUMS.txt`。SmartScreen 提示时点「更多信息 → 仍要运行」。
不放心也可以按 [从源码构建](#七从源码构建) 自己编译。

**Q：提示「未验证版本，需要二次确认」？**
说明你的 X-Plane 版本不在兼容清单里（游戏更新过）。勾选确认后仍可尝试，但结果不保证；
建议等工具更新或反馈版本信息。

**Q：怎么彻底还原？**
程序里点「还原原版」；或者 Steam 验证游戏文件完整性；也可以直接用
**程序目录**下 `.zh_cn_translate\backup\` 里的 `.bak` 覆盖 `X-Plane.exe`。

**Q：课程卡片描述还是英文？**
课程卡片、评分项等少量界面文字由游戏自己的翻译表渲染，不在本工具改写范围内（见 AGENT.md 的已知遗留）。

---

## 五、Linux 上的路径参考

| 场景 | 游戏目录示例 |
|---|---|
| Steam（默认） | `~/.steam/steam/steamapps/common/X-Plane 12` |
| Steam（多库） | 见 `~/.steam/steam/steamapps/libraryfolders.vdf` |
| Flatpak | `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/X-Plane 12` |
| Snap | `~/snap/steam/common/.local/share/Steam/steamapps/common/X-Plane 12` |

程序会自动解析 `libraryfolders.vdf` 找所有 Steam 库；Linux 原生版对应的可执行文件是
`X-Plane-x86_64`（也可能叫 `X-Plane`），Windows/Proton 用的是 `X-Plane.exe`。

---

## 六、命令行模式（高级）

同一份可执行文件带参数运行即可，不需要图形界面：

```bash
XPlane12-FlightLessons-zh-CN --path "/path/to/X-Plane 12" --check
```

| 参数 | 说明 |
|---|---|
| `--check` | 查看版本、词条现状、字体状态 |
| `--dry-run` | 预演，报告放不下的译文（不写盘） |
| `--apply` | 打补丁（自动备份 + 校验）；未验证版本需加 `--force` |
| `--verify` | 校验已打的补丁 |
| `--restore` | 还原原版（`--backup <文件名>` 指定备份） |
| `--list-backups` | 列出备份 |
| `--font-check` | 检测游戏自带中文字体 |
| `--selftest` | 合成数据自检（不读游戏文件） |

退出码：`0` 成功 / `1` 可恢复失败 / `2` 校验失败 / `3` 参数或环境错误 / `4` 需要管理员权限。

---

## 七、从源码构建

需要 [.NET SDK 9](https://dotnet.microsoft.com/)（Windows / Linux 均可），不需要 Visual Studio。

```powershell
# Windows
.\build.ps1
```

```bash
# Linux
./build.sh
```

产物在 `dist/`。CI（GitHub Actions）会在四个原生 runner 上分别构建并跑冒烟测试
（`win-x64` / `win-arm64` / `linux-x64` / `linux-arm64`），见
[`.github/workflows/build.yml`](.github/workflows/build.yml)。

---

## 八、仓库结构

```
├─ src/                          主程序（C# / Avalonia，单一代码库出 Windows + Linux）
│  ├─ Core/                      字节级补丁逻辑（平台无关，PE/ELF 通用）
│  ├─ Platform/                  平台差异：定位、类型检测、写入、进程、字体
│  ├─ Service/                   高层用例（分析 / 打补丁 / 校验 / 还原）——GUI 与 CLI 共用
│  ├─ Cli/                       命令行模式、自检
│  └─ Gui/                       Avalonia 图形界面
├─ translations/zh.csv           译文表（Windows 与 Linux 原生版共用，构建时嵌入 exe；
│                                列：english, chinese, budget_bytes 字节上限）
├─ compat/known-versions.json    兼容清单（sha256 → 版本信息，Windows 与 Linux 各一条）
├─ tests/SmokeTests/             冒烟测试（核心逻辑 + GUI headless）
├─ PLAN.md                         方案与设计决策
└─ AGENT.md                      维护者须知：已验证事实、设计约束、踩过的坑
```

---

## 许可

代码以 MIT 许可发布，见 [LICENSE](LICENSE)。
X-Plane 及游戏内文本版权归 Laminar Research；本项目仅供个人汉化学习使用，不包含任何游戏文件。
