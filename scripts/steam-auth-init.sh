#!/usr/bin/env bash
# steam-auth-init.sh —— **本地人工初始化** DepotDownloader 的持久化认证
#
# 这里是整个流程中**唯一**允许人工输入 Steam 密码 / Steam Guard 验证码的地方。
# 初始化成功后，DepotDownloader 会把登录态（refresh token + Guard 数据）写进
# .NET IsolatedStorage 里的 account.config；之后的自动化任务只复用这个文件，
# 不再需要任何人工输入。
#
# 用法：
#   scripts/steam-auth-init.sh --username <Steam 登录名> [选项]
#
# 选项：
#   --username U     Steam 登录名（也可用环境变量 STEAM_USERNAME）
#   --app ID         App ID（默认 2014780 = X-Plane 12）
#   --depot ID       Depot ID（默认 2014784 = X-Plane 12 的 Linux depot）
#   --mode M         manifest = 只拉 manifest（默认）｜ check = 只做环境检查，不登录
#   --auth-dir DIR   认证文件存放目录（默认 $XDG_DATA_HOME/DepotDownloader）
#
# 退出码：0 成功；1 参数/环境错误；2 登录失败或认证文件未生成。

set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/steam-auth-common.sh
. "$HERE/steam-auth-common.sh"

USERNAME="${STEAM_USERNAME:-}"
APP="2014780"
DEPOT="2014784"
MODE="manifest"
AUTH_DIR="${STEAM_AUTH_DIR:-$STEAM_AUTH_DIR_DEFAULT}"

while [ $# -gt 0 ]; do
  case "$1" in
    --username) USERNAME="$2"; shift 2 ;;
    --app)      APP="$2"; shift 2 ;;
    --depot)    DEPOT="$2"; shift 2 ;;
    --mode)     MODE="$2"; shift 2 ;;
    --auth-dir) AUTH_DIR="$2"; shift 2 ;;
    --no-prompt-check) MODE="check"; shift ;;   # 旧名，保留兼容
    -h|--help)  steam_usage "$0"; exit 0 ;;
    *) steam_die "未知参数：$1" ;;
  esac
done

DD="$(steam_find_depotdownloader || true)"
[ -n "$DD" ] || steam_die "未找到 DepotDownloader。
请先安装：https://github.com/SteamRE/DepotDownloader/releases
或用 DEPOTDOWNLOADER_BIN 指定可执行文件路径。"
steam_log "使用 DepotDownloader：$DD"
"$DD" -V 2>&1 | sed -n '1p' | sed 's/^/  /' >&2 || true

case "$MODE" in
  manifest|check) ;;
  *) steam_die "--mode 只支持 manifest | check" ;;
esac

if [ "$MODE" = "check" ]; then
  steam_log "仅检查环境，不登录"
else
  [ -n "$USERNAME" ] || steam_die "缺少 --username（或环境变量 STEAM_USERNAME）"
fi

# ---- check 模式到此为止：只报告环境，不登录、不复制文件、不写边车 ----
if [ "$MODE" = "check" ]; then
  steam_log "仅检查环境，不登录、不复制文件、不写边车"
  # 注意：$(cmd || true) 的退出码恒为 0，不能用它做 if 判断 → 改判字符串
  NATIVE="$(steam_auth_find_native || true)"
  if [ -n "$NATIVE" ]; then
    steam_log "已有认证文件（原生位置）：$NATIVE"
  else
    steam_warn "尚未找到认证文件；执行 scripts/steam-auth-init.sh --username <账号> 生成"
  fi
  steam_log "认证目录（本次不会创建）：$AUTH_DIR"
  exit 0
fi

# ---- 认证目录 ----
mkdir -p "$AUTH_DIR"
chmod 700 "$AUTH_DIR"
steam_log "认证目录：$AUTH_DIR"

# ---- 登录（唯一允许人工输入的环节）----
steam_log "即将登录 Steam 账号：$USERNAME"
steam_log "→ 此时会提示输入 Steam 密码与 Steam Guard 验证码（正常现象，只此一次）"
steam_log "→ 用 -remember-password 让 DepotDownloader 把登录态存进 account.config"

DD_ARGS=(-app "$APP" -depot "$DEPOT" -username "$USERNAME" -remember-password -no-mobile -manifest-only)

set +e
"$DD" "${DD_ARGS[@]}"
RC=$?
set -e
if [ "$RC" -ne 0 ]; then
  steam_die "登录/初始化失败（退出码 $RC）。
· 账号必须拥有 X-Plane 12（AppID $APP）
· 密码错误会反复提示，请确认后重试
· Steam Guard 验证码需来自验证器 App 或邮箱"
fi

# ---- 定位生成的认证文件 ----
# 必须取**原生位置**里刚生成的那份：候选列表里 $AUTH_DIR/account.config 排在前面，
# 那是上一次的副本，直接用它会导致「重新初始化」拿到的还是旧登录态。
NATIVE="$(steam_auth_find_native || true)"
if [ -z "$NATIVE" ]; then
  # 兜底：本次登录开始之后才生成的文件
  NATIVE="$(steam_auth_native_candidates | sed -n '1p')"
fi
[ -n "$NATIVE" ] || steam_die "登录结束，但没有找到 account.config。
DepotDownloader 只会在成功登录后写入该文件；请检查上面的登录输出。"

steam_log "找到认证文件：$NATIVE"

# ---- 复制到规范目录，便于安全传输（install 保证权限是 600，没有 cp 的权限窗口）----
TARGET="$AUTH_DIR/$STEAM_AUTH_BASENAME"
install -m 600 "$NATIVE" "$TARGET"
# 账号名边车（便于远端复用登录态时免交互）
printf '%s\n' "$USERNAME" > "$TARGET$STEAM_AUTH_USER_SUFFIX"
chmod 600 "$TARGET$STEAM_AUTH_USER_SUFFIX"

if ! steam_auth_validate "$TARGET"; then
  steam_die "认证文件校验失败（无法按 DepotDownloader 的格式解开）"
fi

# ---- 输出：位置 + 后续传递方式（不输出任何内容）----
cat >&2 <<EOF

✔ 认证初始化完成

  认证文件   : $TARGET
  原生位置   : $NATIVE
  账号名     : $USERNAME
  文件权限   : 600（目录 700）

  接下来把它安全地传到目标主机（不要提交到 Git、不要放进公开 Artifact）：

    scripts/steam-auth-deploy.sh --remote steam@目标主机 --remote-user steam --auth-file "$TARGET"

  或用 GitHub Secrets（托管 Runner）：

    scripts/steam-auth-deploy.sh --secrets pack --out account.b64 --auth-file "$TARGET"
    # 把 account.b64 的内容存进 Secret（例：STEAM_AUTH_ENC）
    # Runner 上：scripts/steam-auth-deploy.sh --secrets unpack --in account.b64

  校验已部署的认证文件：

    scripts/steam-auth-check.sh

  如果认证目录不是默认位置，请让后续脚本也知道它：

    export STEAM_AUTH_DIR="$AUTH_DIR"

EOF
