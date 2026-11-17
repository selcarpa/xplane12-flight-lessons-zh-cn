# Codespaces 云端开发环境（`.devcontainer/`）

给本仓库（.NET 9 + Avalonia 11.3 的 X-Plane 12 汉化工具）配的一套 GitHub Codespaces /
Dev Container 环境：**预装 .NET 9 SDK 与 DepotDownloader**，容器建好就能编译、跑测试，
并按需取回 X-Plane 12 的**可执行文件**做端到端验证。

> 这套环境**只用于日常开发与翻译迭代**。跨平台产物与 Release 发布**仍然由 CI 负责**
> （`.github/workflows/build.yml`），Codespaces 替代不了它。

## 快速开始

1. 仓库页面 → **Code** → **Codespaces** → **Create codespace on main**
2. 等容器建好（首次 8–20 分钟），Codespaces 创建日志末尾会打印：

```
  dotnet run --project tests/SmokeTests/SmokeTests.csproj   # 冒烟测试
  scripts/build.sh                                         # 构建 + 打包 linux-x64
  bash .devcontainer/download-exe.sh                        # 下载可执行文件（≈79 MB（十进制），不下整包）
  bash .devcontainer/xplane-patch-demo.sh                   # 在游戏副本上跑 --check / --dry-run
```

3. 要下载 Steam 上的文件才需要先准备认证（见下），否则容器本身不碰任何游戏文件。

## 镜像里有什么

| 组件 | 版本 / 来源 | 说明 |
|---|---|---|
| 基础镜像 | `mcr.microsoft.com/devcontainers/base:ubuntu-24.04` | 自带 `vscode` 用户、git、sudo |
| .NET SDK | **9.0**（devcontainer feature） | 与 CI 的 `setup-dotnet 9.0.x` 对齐 |
| DepotDownloader | **3.4.0**（GitHub Releases，linux-x64） | 版本由 Dockerfile 的 `ARG DEPOTDOWNLOADER_VERSION` 控制；官方 features 里没有，只能手装 |

**不装 SteamCMD，也不提供「下载完整游戏」的功能**：游戏主体在公共 depot `2014781`（≈ 37 GB），
本环境只取补丁工具真正需要的那个可执行文件。

## Steam 认证

本环境**不保存明文密码**，而是复用 DepotDownloader 自己的「记住登录态」机制
（登录态存在 .NET IsolatedStorage 的 `account.config` 里）：

```bash
# ① 本地（有人工登录的机器）生成认证文件：只此一次会提示输入密码 + 验证码
#    会生成两个文件：account.config 与 account.config.username（账号名边车）
scripts/steam-auth-init.sh --username <你的专用 Steam 账号>

# ② 把【两个文件】传到目标机：Codespaces 里拖进文件树会落到工作区
#    /workspaces/<repo>/，请放到仓库内的 .devcontainer/ 下（已在 .gitignore 里）：
#      .devcontainer/account.config
#      .devcontainer/account.config.username
#    缺 .username 边车，下载时就得再传账号名，无法零交互
scripts/steam-auth-deploy.sh --install --auth-file .devcontainer/account.config

# ③ 体检（只看状态，不打印凭据）
scripts/steam-auth-check.sh
```

之后 `download-exe.sh` 即**零交互**下载。若认证不通过，各处都会给同一句提示：

```
✘ Steam 认证未就绪：无法无人值守下载。
  需部署 account.config 与 account.config.username 两个文件（详见 docs/ops/steam-auth.md）。
  部署：scripts/steam-auth-deploy.sh --install --auth-file .devcontainer/account.config
```

完整流程、远端部署、认证失效处理、轮换与安全要求见
**[docs/ops/steam-auth.md](../docs/ops/steam-auth.md)**。

> 认证文件等价于一次 Steam 会话：**绝不入库、绝不进公开 Artifact**。
> 该账号必须**已拥有 X-Plane 12**（AppID 2014780），建议单独用一个下载账号。

## 下载 X-Plane 12 的可执行文件（最小包）

补丁引擎改的是**游戏可执行文件里的字符串**，做端到端验证不需要整个游戏。预设写在脚本里
（数据取自 Steam `app_info`）：

| 平台 | depot | 可执行文件 | 完整 depot | 只取该文件 |
|---|---|---|---|---|
| Linux | `2014784` | `X-Plane-x86_64` | ≈ 122 MB | **≈ 79 MB（十进制）** |
| Windows | `2014782` | `X-Plane.exe` | ≈ 73 MB | **≈ 55 MB（十进制）** |
| （公共） | `2014781` | 游戏主体资源 | ≈ 37 GB | **不下载** |

> 「只取该文件」的体积来自兼容清单 `compat/known-versions.json` 的 `exeSize`
> （Linux 78 659 600 B ≈ 79 MB、Windows 55 022 152 B ≈ 55 MB，均为十进制 MB）。

```bash
bash .devcontainer/download-exe.sh                          # Linux，public 分支
bash .devcontainer/download-exe.sh --branch previous_release # 上一个稳定版（12.4.3）
bash .devcontainer/download-exe.sh --os windows             # Windows 版
bash .devcontainer/download-exe.sh --dir ~/steamapps/"X-Plane 12"
```

需要更细的控制（任意 depot / manifest / 超时）就直接用底层脚本：

```bash
scripts/steam-download.sh --app 2014780 --depot 2014784 \
    --filelist scripts/files-linux.txt --output ~/steamapps/"X-Plane 12" --timeout 1800
```

下载完会自动打印体积与 sha256，并与**兼容清单 `compat/known-versions.json`** 里的金标准比对
（清单是唯一事实来源，`AGENT.md` §1.1 是同一份数据的人读版）。
不一致通常是文档滞后于分支，先跑 `--check` 看锚点是否还对得上。

Steam 会拒绝为被发行商封禁的历史版本返回 manifest —— 那是发行商的选择，不是脚本问题。

## 在副本上验证补丁

```bash
bash .devcontainer/xplane-patch-demo.sh           # --check + --dry-run
bash .devcontainer/xplane-patch-demo.sh --apply   # 额外 --apply / --verify，并打印 sha256
```

脚本会先把可执行文件 `cp` 一份到 `$HOME/xplane12-copy/` 再操作，**绝不修改下载下来的原件**；
打完的 sha256 应与 `AGENT.md` §1.1 的「打完补丁 sha256」一致。
副本目录由 `XP12_COPY_DIR` 指定（默认 `$HOME/xplane12-copy`）；为防止误删，路径必须落在 `$HOME/` 之下，
用 `--dir` 指定仓库外的游戏目录或副本目录需额外加 `--force`。

## 常用命令

```bash
dotnet run --project tests/SmokeTests/SmokeTests.csproj   # 冒烟测试（核心逻辑 + GUI headless）
dotnet build src/XpLessonZhCN.csproj -c Release            # 编译
scripts/build.sh                                         # 编译 + 冒烟 + 发布 + 打包 + SHA256
```

## 能力边界

| ✅ 能做 | ❌ 不能做 |
|---|---|
| 改代码、改译文表、编译、跑冒烟测试（GUI headless）、跑 CLI 全链路 | **下载完整游戏**（刻意不提供：`depot 2014781` 约 37 GB） |
| 取回可执行文件，在**副本**上跑 `--check` / `--dry-run` / `--apply` / `--verify` | 跑真 GUI 窗口（容器无显示器）、跑 X-Plane 12 本体（需 GPU） |
| 按分支复现不同游戏版本，做兼容性验证 | 构建 win / ARM64 产物（由 CI 对应 runner 负责） |
| | 验证 Windows 特有行为：`Elevation` 提权、NTFS `Everyone:(CI)(DENY)` ACL、PE 原子替换；Wine / Proton 路径（只能验 Linux 原生 ELF） |

## 故障排查

| 现象 | 处理 |
|---|---|
| `未找到 depotdownloader` | 镜像构建时下载失败 → Codespaces 的 **Rebuild container** 重跑 Dockerfile |
| 想升级 DepotDownloader | 改 Dockerfile 的 `ARG DEPOTDOWNLOADER_VERSION` → 清构建缓存 → Rebuild |
| 提示「没有可用的 Steam 认证」/「未找到认证文件」 | 认证没部署 → 本地 `scripts/steam-auth-init.sh` 生成 `account.config` 与 `account.config.username`，两个文件都拖进仓库的 `.devcontainer/` 后执行 `scripts/steam-auth-deploy.sh --install --auth-file .devcontainer/account.config` |
| 提示「认证失效 / 需要重新人工验证」 | 登录态过期或被吊销 → 本地重新 init 再部署（**不要**在自动化里改密码） |
| `找不到 IsolatedStorage 身份目录` | 先跑一次 `depotdownloader -V` 让 .NET 建目录（不联网） |
| 每次都要验证码 / 下载报「没有该 app」 | 前者：认证文件没装进原生位置，检查 `~/.local/share/IsolatedStorage/**/account.config`；后者：账号必须**拥有** X-Plane 12 |
| `no manifest code returned for old manifest ids` | 该历史版本被发行商封禁，Steam 不再返回旧 manifest |
| 下载完目录里没有 exe | `-filelist` 的正则没命中 → 改用精确路径 |
| sha256 与金标准不一致 | 分支对应的游戏版本可能与文档记录不同 → 先跑 `--check` 看锚点 |
| NuGet 还原很慢 | 首次是冷缓存；volume 里已有包后 ~10 秒 |
| 容器重建后认证没了 | Steam 会话在 volume `xpzhcn-steam-session`，**容器重建仍在**；但 codespace 被**删除**时 volume 一起没，需重新部署 |

## 本目录的文件

| 文件 | 作用 |
|---|---|
| `Dockerfile` | 基于 `devcontainers/base`，装 DepotDownloader（版本由 ARG 控制） |
| `devcontainer.json` | 镜像构建 + .NET 9 feature + Codespaces 定制（保留 7 天 / 空闲 30 分钟回收 / 打开 `AGENT.md`）+ NuGet 与 Steam 会话 volume |
| `postCreate.sh` | **只在创建时跑一次**：还原依赖 → 编译 → 冒烟 → 发布 linux-x64 → 环境自检 |
| `postStart.sh` | 每次启动打一条状态横幅（SDK 版本、工具是否就位、认证是否就绪、磁盘余量），< 1 秒 |
| `download-exe.sh` | 下载可执行文件最小包并比对金标准 sha256（委托 `scripts/steam-download.sh`） |
| `xplane-patch-demo.sh` | 在文件**副本**上跑 `--check` / `--dry-run` / `--apply` / `--verify` |

认证与下载的通用脚本在仓库的 `scripts/`（与 devcontainer 无关，CI 也能用），
清单见 [docs/README.md](../docs/README.md)。
