# X-Plane 12 飞行教学汉化

给 **X-Plane 12 的「飞行教学」（Flight Lessons，Austin 的飞行小课堂）** 汉化**课程标题**与**口播字幕**。

- 图形界面，**不需要安装 Python / .NET / 任何运行环境**，解压双击即用
- 支持 **Windows** 与 **Linux（Steam / Proton / Wine）**
- 自动备份原版、打完自动校验、失败自动回滚，**不改变游戏文件大小、不动代码段**

> Austin 的录音仍是英文；本工具汉化的是**字幕**和**课程标题**。

---

## 一、下载与使用

### Windows

1. 下载 `XPlane12-FlightLessons-zh-CN-win-x64.zip`，解压到任意目录
2. **完全退出 X-Plane 12**，双击 `XPlane12-FlightLessons-zh-CN.exe`
3. 程序会自动找到游戏目录；没找到就点「浏览…」选到游戏根目录（含 `X-Plane.exe` 的那一层）
4. 点「打补丁」→ 确认 → 看到「补丁完成并通过校验」即可启动游戏

### Linux（Steam / Proton）

```bash
tar xzf XPlane12-FlightLessons-zh-CN-linux-x64.tar.gz -C ~/apps/xpzhcn
chmod +x ~/apps/xpzhcn/XPlane12-FlightLessons-zh-CN
~/apps/xpzhcn/XPlane12-FlightLessons-zh-CN
```

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
| 自动备份 | 首次打补丁前把原版 exe 备份到 `<游戏目录>/.zh_cn_translate/backup/` |
| 只改字符串 | 只替换只读数据段里的 UTF-8 字符串，**长度只减不增**，不足补 `0x00` |
| 结构不变 | 文件大小、PE 头、文件尾都不变；改动范围校验通过才允许写盘 |
| 版本校验 | 打补丁前核对 exe 的 sha256 是否在 [兼容清单](compat/known-versions.json) 里 |
| 二次确认 | **版本未经验证**时，必须勾选确认才能继续；否则直接拒绝 |
| 写完再验 | 写入后重新读取文件核对 sha256，不一致立即用备份还原 |
| 游戏运行检测 | 检测到 X-Plane 正在运行会拒绝打补丁 |
| 字体检测 | 检查游戏自带中文字体是否完好；缺失时提示用 Steam 验证完整性（**只提示，不装字体**） |

---

## 三、兼容性

| 项目 | 当前支持 |
|---|---|
| X-Plane 12 | **12.4.3-r2 (build 124311)** —— 唯一验证版本 |
| 原版 exe sha256 | `2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936` |
| 打完补丁 sha256 | `36f5b21eb869e94e75a71de0aa7a4235976e16677424ad5421a45ac6b367131e` |
| exe 大小 | 91,132,488 字节（打补丁前后不变） |
| Windows | Windows 10 1809+ / 11（自包含，无需装 .NET） |
| Linux | 桌面发行版 + Steam/Proton，或 SteamOS 桌面模式 |
| Linux 原生版 X-Plane | **暂不支持**（原生版不是 `X-Plane.exe`），后续计划 |

兼容信息全部记录在 [`compat/known-versions.json`](compat/known-versions.json)。
游戏更新后如果要支持新版本，改这个清单即可（见 [AGENT.md](AGENT.md)）。

---

## 四、常见问题

**Q：打完补丁进游戏还是英文？**
1. 确认程序提示「补丁完成并通过校验」；
2. 完全退出游戏再进（X-Plane 会缓存，需要重启）；
3. Steam 校验/更新会覆盖 exe，重新打一次即可；
4. 字幕只在「飞行教学」里，不在普通自由飞行里。

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
`<游戏目录>/.zh_cn_translate/backup/` 里的 `.bak` 覆盖 `X-Plane.exe`。

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

程序会自动解析 `libraryfolders.vdf` 找所有 Steam 库。

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

退出码：`0` 成功 / `1` 可恢复失败 / `2` 校验失败 / `3` 参数或环境错误。

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

产物在 `dist/`。CI（GitHub Actions）会在 Windows 与 Linux 上分别构建并跑冒烟测试，见
[`.github/workflows/build.yml`](.github/workflows/build.yml)。

---

## 八、仓库结构

```
├─ src/                          主程序（C# / Avalonia，单一代码库出 Windows + Linux）
│  ├─ Core/                      字节级补丁逻辑（平台无关）
│  ├─ Platform/                  平台差异：定位、写入、进程、字体
│  ├─ Service/                   高层用例（分析 / 打补丁 / 校验 / 还原）——GUI 与 CLI 共用
│  ├─ Cli/                       命令行模式、自检
│  └─ Gui/                       Avalonia 图形界面
├─ translations/zh.tsv           译文表（构建时嵌入 exe，exe 旁同名文件可覆盖）
├─ compat/known-versions.json    兼容清单（sha256 → 版本信息）
├─ tests/SmokeTests/             冒烟测试（核心逻辑 + GUI headless）
├─ python-achieve/               重写前的 Python 实现（已归档，计划删除）
├─ PLAN.md                       重写方案与设计决策
└─ AGENT.md                      维护者须知：已验证事实、设计约束、踩过的坑
```

---

## 许可

代码以 MIT 许可发布，见 [LICENSE](LICENSE)。
X-Plane 及游戏内文本版权归 Laminar Research；本项目仅供个人汉化学习使用，不包含任何游戏文件。
