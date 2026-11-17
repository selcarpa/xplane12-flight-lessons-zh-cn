# Steam 认证与自动下载（DepotDownloader）

本项目需要从 Steam 取回 **X-Plane 12 的可执行文件**（补丁引擎改的是这个文件里的字符串），
用于在 CI / Codespaces 里做端到端验证。

Steam 的内容服务器要求登录态。本方案把这件事拆成两段：

```
┌─ 本地（有人） ────────────────┐        ┌─ 远程（无人） ─────────────┐
│ scripts/steam-auth-init.sh     │        │ scripts/steam-download.sh   │
│  · 人工输入密码 / Steam Guard  │  传输  │  · 只用持久化登录态          │
│  · -remember-password           │ ─────► │  · stdin=/dev/null（不等待） │
│  · 产出 account.config         │        │  · 认证失效→明确报错并停止   │
└───────────────────────────────┘        └────────────────────────────┘
```

**自动化任务里永远不会出现密码或验证码输入。**

---

## 1. 认证文件到底是什么（别想当然）

DepotDownloader 把「记住的登录态」存进 .NET 的 **IsolatedStorage**：

- 源码：`AccountSettingsStore.LoadFromFile("account.config")`
  → **文件名固定是 `account.config`**
- 存放位置（.NET 10 实测，比想象中深，是 4 层目录）：
  ```
  $XDG_DATA_HOME/IsolatedStorage/<随机8.3>/<哈希8.3>/Url.<32位md5>/AssemFiles/account.config
  # 默认 XDG_DATA_HOME = ~/.local/share
  # 例：~/.local/share/IsolatedStorage/abcde.fgh/ijklm.nop/Url.0123456789abcdef0123456789abcdef/AssemFiles/account.config
  ```
  层级含义：随机目录 → 哈希目录 → 身份目录 → 域目录（`AssemFiles`）。
- 里面装的是 **refresh token**（以及 Steam Guard 数据），由 `Steam3Session` 在登录成功后写入。

⚠️ **目录层数由 .NET 版本决定**（不同 .NET 版本下 IsolatedStorage 的深度不同，
不要把层数写死）。脚本因此**内容驱动查找**：`find -type f -name account.config`，
不依赖固定层数。

⚠️ **身份目录名在不同机器 / 不同安装路径下是不同的**：DepotDownloader 3.4.0
**未强名签名**，身份层因此就是 `Url.<32 位 md5>`（不是程序集强名哈希）。
所以**不能**把本机的目录结构原样搬到远端，必须把文件装进**目标机自己的**身份目录 ——
这正是 `scripts/steam-auth-common.sh` 的 `install-native` 在做的事。

---

## 2. 前置条件

- **DepotDownloader ≥ 3.x**（<https://github.com/SteamRE/DepotDownloader/releases>；Codespaces 镜像已装 3.4.0）
- **专用 Steam 下载账号**：独立的下载账号，不要用日常主账号；且**已拥有 X-Plane 12**（AppID 2014780）
- **一台有人的 Linux 机器**：只在**初始化**时需要人（输密码 + 验证码）
- **目标机可 SSH**，或改用 GitHub Actions + Secrets 方案

---

## 3. 本地初始化认证（唯一允许人工输入的环节）

```bash
# 需要一台装了 DepotDownloader 的 Linux 机器
export PATH="$HOME/.local/bin:$PATH"

scripts/steam-auth-init.sh --username <你的专用 Steam 账号>
```

会发生什么：

1. 检查 DepotDownloader 是否可用；
2. 建立认证目录 `~/.local/share/DepotDownloader/`（700）；
3. 以 `-username … -remember-password -no-mobile` 启动 DepotDownloader ——
   **此时它会提示你输入 Steam 密码与 Steam Guard 验证码，这是正常的，只此一次**；
4. 登录成功后，DepotDownloader 把登录态写进 `account.config`；
5. 脚本把它复制到规范目录、设为 600，并记录账号名；
6. 输出认证文件位置与后续传递方式（**不输出任何凭据内容**）。

常用选项：

| 选项 | 说明 |
|---|---|
| `--username U` | Steam 登录名（也可用环境变量 `STEAM_USERNAME`） |
| `--app ID` / `--depot ID` | 默认 `2014780` / `2014784`（X-Plane 12 的 Linux depot） |
| `--mode manifest\|download` | `manifest`（默认）只拉 manifest，体积小；`download` 会顺带下整个 depot |
| `--auth-dir DIR` | 认证目录，默认 `~/.local/share/DepotDownloader` |
| `--no-prompt-check` | 只做环境检查，不登录 |

产物：

```
~/.local/share/DepotDownloader/
├── account.config            # 600，登录态本体
├── account.config.username   # 600，登录名（边车，便于远端免交互）
└── username                  # 600，同上
```

---

## 4. 部署到目标机

### 4.1 SSH / SCP（自建机、CI Runner）

```bash
# 目标机需要有一个专用用户 steam
ssh runner "sudo useradd -m -s /bin/bash steam"     # 没有的话先建

scripts/steam-auth-deploy.sh --remote steam@runner.example.com --remote-user steam
```

脚本做的事：

1. 本地找到 `account.config` 并校验格式；
2. 检查远端用户存在（不存在可加 `--create-user` 自动创建）；
3. **默认不覆盖**远端已有文件（要覆盖必须显式 `--overwrite`）；
4. SCP 传到远端临时目录，成功后再原子移动（传失败不会留下半个文件）；
5. 远端落位：目录 `700`、文件 `600`、属主 `steam`；
6. **在远端自己解析出身份目录**，把认证文件装进远端的 IsolatedStorage 原生位置
   （远端与本机的目录名不同，所以这一步必须在远端做）；
7. 全程**不打印认证文件内容**，失败返回非零。

### 4.2 GitHub Actions（托管 Runner）+ Secret

托管 Runner 不保留状态，因此用**加密 Secret** 传递：

```bash
# 本地：加密（口令来自 STEAM_AUTH_PASSPHRASE 环境变量，或交互式输入）
scripts/steam-auth-deploy.sh --secrets pack --out account.b64

# 把 account.b64 的内容整个存进仓库 Secret（例：STEAM_AUTH_ENC）
# 绝不能放进 Artifact、绝不能提交 Git
```

Runner 上：

```yaml
- name: 解密认证文件
  env:
    STEAM_AUTH_PASSPHRASE: ${{ secrets.STEAM_AUTH_PASSPHRASE }}
    STEAM_AUTH_ENC: ${{ secrets.STEAM_AUTH_ENC }}
  run: |
    printf '%s' "$STEAM_AUTH_ENC" | base64 -d > account.b64
    scripts/steam-auth-deploy.sh --secrets unpack --in account.b64 \
      --out ~/.local/share/DepotDownloader/account.config
    rm -f account.b64          # 任务结束前清理
```

> 用 `pull_request` 触发的 workflow 拿不到 Secret，属于预期行为：
> **下载任务只能放在受信任的分支/定时任务里**。

### 4.3 Codespaces / 容器

把认证文件上传进容器（例如拖到 `~/account.config`），然后：

```bash
scripts/steam-auth-deploy.sh --install --auth-file ~/account.config
```

`--install` 会把它装到本机（容器内）的原生 IsolatedStorage 位置，并设好权限。

---

## 5. 下载（零交互）

```bash
# Linux 可执行文件
scripts/steam-download.sh --app 2014780 --depot 2014784 \
    --filelist scripts/files-linux.txt --output ./downloads/linux

# Windows 可执行文件
scripts/steam-download.sh --app 2014780 --depot 2014782 --os windows \
    --filelist scripts/files-windows.txt --output ./downloads/windows
```

> ⚠️ **`--os` 必须显式写**：它指定下载哪个平台的 depot 内容（`linux` / `windows` / `macos`）。
> 不传时脚本按运行它的本机 `uname` 推断 —— 在 Linux 主机上下 Windows 包时若省略 `--os windows`，
> 会被当成 linux，去拉错的内容。

X-Plane 12 的参数对照：

| 平台 | App ID | Depot ID | 文件列表 |
|---|---|---|---|
| Windows | 2014780 | 2014782 | `scripts/files-windows.txt`（`X-Plane.exe`） |
| Linux | 2014780 | 2014784 | `scripts/files-linux.txt`（`X-Plane-x86_64`） |

> `-filelist` 的每一行要么是 `regex:` 前缀的正则，要么是字面路径；
> **DepotDownloader 3.4.0 不支持注释行**（`#` 开头的行会被当成字面路径），所以这两个文件里只有正则行。

历史版本：

```bash
scripts/steam-download.sh --app 2014780 --depot 2014784 --os linux \
    --manifest <manifest-id> --filelist scripts/files-linux.txt --output ./downloads/old
```

脚本行为要点：

- **不传 `-password`**，只用 `account.config` 里的 refresh token（`-remember-password`）；
- `stdin` 指向 `/dev/null` —— 任何提示都拿不到输入，会**立刻失败**而不是挂起；
- 加了**超时**（默认 900 秒，`--timeout` 可调）：DepotDownloader 在 2FA 提示处会反复重连，
  没有超时就会一直转圈；
- 下载后**校验目标文件是否落地**，没落地就报错退出；
- 日志输出前会做脱敏（`password/token/code` 之类一律打码）。

退出码：

| 码 | 含义 |
|---|---|
| 0 | 成功 |
| 1 | 参数 / 环境错误 |
| 2 | 下载失败（非认证原因） |
| 3 | **认证文件缺失** |
| 4 | **认证失效 / 需要重新人工验证** |
| 5 | 目标文件未落地 |

---

## 6. 认证失效时会发生什么

判定依据（DepotDownloader 的实际输出）：

```
Access token was rejected (Expired).
This account is protected by Steam Guard.
Please enter the 2 factor auth code from your authenticator app:
Enter account password for "xxx":
InvalidPassword / NoSteamID / Unauthorized / AccessDenied
```

命中任意一条，脚本会：

1. **立即终止**下载任务（退出码 4），不做任何重试；
2. 打印明确的「认证失效」提示与脱敏后的判定依据；
3. **不改密码**、**不绕过 Steam Guard**、**不反复尝试登录**；
4. 提示管理员回到本地重新执行 `steam-auth-init.sh` 并重新部署。

恢复流程：

```bash
# 本地
scripts/steam-auth-init.sh --username <账号>            # 重新生成认证文件
scripts/steam-auth-deploy.sh --remote steam@runner --overwrite   # 重新部署
# 或
scripts/steam-auth-deploy.sh --secrets pack --out account.b64  # 覆盖 Secret
```

---

## 7. 安全要求

1. **Steam 使用独立下载账号**，不要用日常主账号；权限只买 X-Plane 12。
2. `account.config` 视为**高敏感凭据**：它等价于一次 Steam 会话。
3. **禁止提交 Git**：`.gitignore` 已忽略 `account.config`、`account.config.username`、
   `*.b64`（所有 base64 认证包）；`steam-auth-deploy.sh` 还会主动拒绝写入**未被忽略的仓库内路径**。
4. **禁止上传到公开 Artifact**；GitHub Actions 只走加密 Secret。
5. **不向 Agent 暴露认证目录**：认证目录 700、文件 600，属主是专用用户 `steam`；
   下载 Job 与 Agent Job 分开跑（Agent 不需要 Steam 账号）。
6. **不在日志里泄露凭据**：脚本只打印路径/大小/时间，日志输出前统一脱敏。
7. 远程传输走 **SSH**；托管 Runner 走**加密 Secret**。
8. **轮换**：建议每 60–90 天或账号异常时重新初始化一次；
   `scripts/steam-auth-check.sh` 会在文件超过 90 天时提醒。

---

## 8. 体检已部署的认证

```bash
scripts/steam-auth-check.sh                 # 自动查找
scripts/steam-auth-check.sh --path <文件>    # 指定路径
scripts/steam-auth-check.sh --strict        # 权限/边车缺失也判失败
```

输出示例（**不含任何凭据内容**）：

```
认证文件   : /home/steam/.local/share/IsolatedStorage/abcde.fgh/ijklm.nop/Url.0123456789abcdef0123456789abcdef/AssemFiles/account.config
大小       : 4096 字节
权限       : 600（目录 700）
修改时间   : 2026-11-10 09:12:33（7 天前）
格式       : ✔ 可解析（DepotDownloader 账号存储）
账号       : xp12_downloader
结论       : ✔ 认证可用
```

退出码：0 正常；2 未找到；3 损坏。

---

## 9. 排障

| 现象 | 原因 / 处理 |
|---|---|
| `未找到 DepotDownloader` | 装一下，或 `DEPOTDOWNLOADER_BIN=/path/to/DepotDownloader` |
| init 时反复要密码 | 账号或密码不对；或该账号没有 X-Plane 12 |
| init 后找不到 account.config | 登录没成功（看登录输出）；脚本只在成功登录后才复制 |
| `找不到 IsolatedStorage 身份目录` | 先跑一次 `DepotDownloader -V`（会触发 .NET 建目录，不联网） |
| 远端下载报 `Access token was rejected` | 登录态过期/被吊销 → 本地重新 init 并 `--overwrite` 部署 |
| 远端下载反复要 2FA | 远端没有装到原生位置；检查 `~/.local/share/IsolatedStorage/**/account.config` 是否存在 |
| 目标文件没落地 | `-filelist` 的正则没命中 → 用 `--filelist` 换成精确路径，或看下载日志 |
| 密文解不开 | 口令不对，或密文被换行/截断 → 重新 `pack` |

---

## 10. 与 Codespaces 的关系

Codespaces 镜像已预装 DepotDownloader，用同一套机制：本地 `steam-auth-init.sh` 生成认证文件 →
拖进 codespace → `steam-auth-deploy.sh --install --auth-file ~/account.config` → `download-exe.sh`。
详见 [.devcontainer/README.md](../../.devcontainer/README.md)。

---

## 11. 验收对照

| 验收标准 | 实现位置 |
|---|---|
| 本地首次登录能生成认证文件 | `scripts/steam-auth-init.sh` |
| 部署到另一台 Linux 主机后可复用登录态 | `scripts/steam-auth-deploy.sh --remote` |
| 远端重启后认证文件仍在 | 文件落在远端磁盘 + 原生 IsolatedStorage 目录（700/600） |
| 无需每次输入密码与验证码 | `scripts/steam-download.sh`（只用 refresh token，stdin=/dev/null） |
| 能下载 Windows / Linux 指定文件 | `scripts/steam-download.sh` + `scripts/files-*.txt` |
| 认证失效时正确停止 | `scripts/steam-download.sh`（退出码 4 + 明确提示） |
| 仓库中不存在任何 Steam 凭据 | `.gitignore` + 部署脚本的写入门禁 |
