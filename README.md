# zh_cn_translate

给 **X-Plane 12.4+ 的「飞行教学」（Flight Lessons / Austin 飞行小课堂）** 做中文字幕与课程标题汉化的补丁工具。

这套课程把课程标题和口播字幕编译进了 `X-Plane.exe` 的只读数据段，运行时按地址直接引用，
不经过 `Resources/text/*.txt` 翻译表，所以往翻译表里补词条不会生效。本项目直接替换 exe 里
这些字符串的字节：写入 UTF-8 中文、尾部补 `0x00`，长度不超过原字符串的可用空间，
**不移动数据、不改代码段、文件大小不变**。

## 环境要求

- Python 3.10+（只用标准库，无第三方依赖）
- 已安装 X-Plane 12.4 或更高版本
- 打补丁前**退出游戏**；建议先备份（`apply` 会自动备份原版 exe）

## 快速开始

```bash
# 1. 打补丁（自动备份原版 exe，再原子替换）
python apply.py "D:/Games/X-Plane 12"

# 2. 校验：译文是否都在、改动是否都落在教学字符串范围内
python verify.py "D:/Games/X-Plane 12"

# 需要时还原
python restore.py "D:/Games/X-Plane 12"
```

其他命令：

```bash
python extract.py "D:/Games/X-Plane 12"    # 导出 exe 里的教学文本清单（翻译/排查用）
python apply.py   "D:/Games/X-Plane 12" --dry-run   # 预演，报告哪些译文放不下
python apply.py   "D:/Games/X-Plane 12" --check     # 看每条译文的现状
python restore.py "D:/Games/X-Plane 12" --list      # 列出备份
python selftest.py                                  # 不依赖游戏文件的自检
```

游戏路径参数可以省略，此时依次尝试：环境变量 `XP12_ROOT` → 当前目录向上查找 `X-Plane.exe` →
脚本所在目录向上查找。

## 目录

```
zh_cn_translate/
├─ common.py        定位游戏、定位教学文本区间、读写 C 字符串、原子替换 exe
├─ extract.py       导出教学文本清单
├─ apply.py         打补丁（--dry-run / --check）
├─ verify.py        校验补丁
├─ restore.py       用备份还原
├─ selftest.py      自检（不读游戏文件）
├─ translations/
│  └─ zh.tsv        译文表：英文<TAB>中文
└─ AGENT.md         维护者/AI 须知：设计约束、踩过的坑、校验基线
```

补丁运行时的产物放在**游戏目录**下的 `.zh_cn_translate/`：

```
<游戏目录>/.zh_cn_translate/
├─ data/    strings.tsv、strings.txt、patch_report.tsv
└─ backup/  X-Plane.exe.<sha>.bak（原版 exe，约 87 MB）
```

需要换位置时加 `--work-dir <目录>`。

## 翻译状态

目前覆盖 8 节课：课程卡片标题 8 条 + 口播字幕 505 句，共 **513 条词条**。

| 课程 | 内容 |
|---|---|
| Cirrus Intro / Basic Handling / Advanced Handling / Landing | 西锐 SR-22 四节课 |
| Evolution Intro / Emergencies / Cross-Country | 兰凯尔 Evolution 三节课 |
| Tesla Model 3 Test-Drive | 特斯拉试驾彩蛋 |

## 注意

- **Steam「验证游戏文件完整性」或游戏更新会覆盖 `X-Plane.exe`**，重新执行 `apply.py` 即可。
- Austin 的录音仍是英文；本工具汉化的是**字幕和课程标题**。
- 修改后的 exe 不再有数字签名（原版也未签名），个别杀软可能误报。
- 仅供个人汉化学习使用；X-Plane 及游戏内文本版权归 Laminar Research。

## 许可

代码以 MIT 许可发布，见 `LICENSE`。
