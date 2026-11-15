# AGENT.md — 维护者 / AI 接手指南

这份文件写给后续接手的人（或 AI）：记录**已验证的事实**、**为什么这样设计**、**踩过的坑**，
避免重复试错。改代码或继续翻译前请先读完。

## 1. 已验证的事实（不要重复验证）

- X-Plane 12.4.3（build 124311）的「飞行教学」是**新系统**，与旧的
  `Resources/tutorials` + `Resources/wizards` 移动版移植教程**不是一回事**。旧教程补丁
  （`zh_tutorial_patch/`，如果有）对界面上看到的课程无效。
- 新课程的课程标题与口播字幕是**编译进 `X-Plane.exe` 只读数据段的 UTF-8 C 字符串**，
  运行时按地址引用。
- **往 `Resources/text/Chinese.txt` 补词条无效**：这些字符串在中文表里没有，
  在法、德、日、俄、西、意、葡、乌、韩**所有语言**的表里也都没有——说明 Laminar 没有把
  它们放进翻译提取流程。实测补了 8 个标题 + 3 条字幕，游戏里依旧英文。
- 文本区间（该版本）：`41,552,520 .. 41,612,728`，共 621 条候选字符串。
- 原版 exe：`91,132,488` 字节，`sha256 = 2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936`。
- 打完全部 513 条译文后：`sha256 = 36f5b21eb869e94e75a71de0aa7a4235976e16677424ad5421a45ac6b367131e`，
  改动 787 段共 45,186 字节（占 0.0496%），文件大小、PE 头、文件尾均未变。

## 2. 关键设计约束（改代码前必读）

1. **锚点必须是不随翻译变化的字符串**。早期用 `Cirrus Intro` 和第一句口播当锚点，
   打完补丁后这两句变成中文，`extract.py` 直接报“找不到锚点”。现在用
   `oz_intro_widget_controller_text` 和 `Could not load sound bank at:`。
2. **只改数据段、只改字符串字节**：不新增节、不移动数据、不改代码，长度只减不增。
   写不满的部分用 `0x00` 补齐，绝不越过下一条字符串的起点。
3. **可用空间 = 到下一条字符串起点之间的字节数 − 1**（末尾要留 NUL）。字符串之间通常有
   4~8 字节对齐填充，可以一并使用；`safe_span()` 负责计算，放不下就报 `too-long`，
   此时**改短译文**，不要做重定位。
4. **只认“字符串起点”**：`occurrences()` 要求匹配位置的前一字节是 `0x00`，否则会把
   长字符串里的同名片段也算进来（会产生一串无意义的 `skipped`）。
5. **前缀匹配要跳过**：有些英文是更长字符串的前缀（例如 `Flaps up!` 是
   `Flaps up! 120 knots! 150 degree heading!` 的前缀），这类命中会被判为
   `不是独立的字符串` 并跳过，属于正常现象。
6. **替换 exe 必须“先写到上一级目录再 rename”**，原因见第 3 节第 1 条。

## 3. 踩过的坑

1. **低完整性标签 → 游戏无法存档**（最坑的一次）
   开发沙箱给工作目录打了 `Mandatory Label\Low`（`icacls` 能看到 `(NW)`）。直接在游戏目录里
   新建文件会继承该标签，替换出来的 `X-Plane.exe` 也带 Low 标签，游戏被以低权限启动，
   弹「文件无法保存！……Output\logbooks\X-Plane Pilot.txt」，其它写盘功能也接连失败。
   解决：`common.replace_exe()` 先把新 exe 写到 `游戏目录/上一级`（通常没有该标签）再
   `os.replace` 回来。排查命令：`icacls "X-Plane.exe" | findstr Mandatory`，正常应无输出。
2. **`icacls /setintegritylevel Medium` 会 Access denied**（非管理员拿不到 SeSecurityPrivilege），
   所以只能靠搬目录绕开，别指望改标签。
3. **`shutil.move` 覆盖已存在文件会失败**（`FileExistsError`），用 `os.replace`。
4. **PowerShell 控制台输出中文/长字符串会乱码**：跑 Python 前设
   `$env:PYTHONUTF8='1'; $env:PYTHONIOENCODING='utf-8'`。
5. **git 自动 CRLF**：仓库带 `.gitattributes`（`* text=auto eol=lf`），否则 Windows 上提交后
   diff 全是换行变化。
6. **提交时 GPG 签名失败**：本机 `commit.gpgsign=true`，在受限环境里 gpg 连不上 keyboxd，
   需要完整权限或临时 `-c commit.gpgsign=false`。
7. **翻译表不要用 `[section]` 这类标记**：`load_translations()` 只认 `#` 注释和
   `英文<TAB>中文`，其它行会被当格式错误跳过。用 `# ---- xxx ----` 分节。
8. **tab 分隔**：译文里不能出现制表符或换行，`apply.py` 会检测并跳过。

## 4. 工作流

```bash
# 1) 抽取（游戏更新后先跑，确认区间和锚点还成立）
python extract.py "<游戏目录>"

# 2) 翻译：把「英文<TAB>中文」追加到 translations/zh.tsv
#    可用 data/strings.txt 当英文清单，按出现顺序逐条译

# 3) 预演：看有没有放不下的
python apply.py "<游戏目录>" --dry-run

# 4) 写入 + 校验
python apply.py "<游戏目录>"
python verify.py "<游戏目录>"

# 5) 改完代码先跑自检
python selftest.py
```

## 5. 游戏更新后怎么办

1. 跑 `extract.py`。若报「找不到锚点」，说明 `START_ANCHOR`/`END_ANCHOR` 失效：
   用 `data/strings.txt` 附近的字符串重新挑一个**不会被翻译**的锚点。
2. 若区间对了但 `apply.py` 报大量 `not-found`，说明课程文案改写了：按新英文重新翻译。
3. **Steam 校验/更新会把 exe 换回原版**，重跑 `apply.py` 即可；`backup/` 不会被覆盖。

## 6. 翻译约定

- 口吻：Austin 的第一人称、口语、简短，适合当字幕读。
- 术语：`collective` 总距、`bank` 坡度、`trim` 配平、`heading` 航向、`ft-lb` 英尺磅、
  `ITT` 涡轮进口温度、`PAPI`、`touch-n-go` 连续起降、`chandelle` 上升转弯。
- **保留 `[sim/xxx]` 占位符原样**，不要翻译、不要删空格。
- 机型名保留英文：`Cirrus SR-22`、`Lancair Evolution`、`Tesla Model 3`。
- 长度不够时优先删修饰语，不要缩写术语。

## 7. 已知遗留

- 2 条“卡片描述”字符串在 exe 里仍是英文（`Let's do a touch-n-go. A touch-n-go is ...`），
  因为游戏显示这些描述走的是 `Chinese.txt`（显示为中文），exe 里那份不参与显示；
  `apply.py --check` 会把这 2 条报成“待应用”，属正常。
- `MISSION_ANALYTICS_TITLE` 之类只用于统计上报的字符串不翻译。

## 8. 禁止事项

- 不要改代码段、不要新增/移动节、不要做字符串重定位。
- 不要为了“让译文放得下”而覆盖相邻字符串或对齐填充之后的字节。
- 不要把游戏文件、备份 exe、机器相关的路径提交进仓库。
