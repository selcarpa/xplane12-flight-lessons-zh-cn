#!/usr/bin/env bash
# steam-download.sh —— 复用已部署的认证文件，**零交互**地下载指定 App / Depot / 文件
#
# 设计要点：
#   · 只用持久化的登录态（account.config 里的 refresh token），
#     **不传 -password**，stdin 指向 /dev/null → 任何提示都拿不到输入，直接失败而不是挂起
#   · 认证失效（token 被吊销 / 需要重新验证）→ 明确报错并以专用退出码结束，
#     不重试登录、不改密码、不绕 Steam Guard
#   · 加超时兜底：DepotDownloader 在 2FA 提示处会反复重连，没有超时会一直转
#   · 下载结束后校验目标文件是否存在
#
# 用法：
#   scripts/steam-download.sh --app 2014780 --depot 2014784 \
#       --filelist scripts/files-linux.txt --output ./downloads/linux
#
# 选项：
#   --app ID          Steam App ID（X-Plane 12 = 2014780）
#   --depot ID        Depot ID（Linux = 2014784，Windows = 2014782）
#   --manifest ID     指定 Manifest ID（历史版本）；不给则用当前分支
#   --branch NAME     分支名（public / previous_release / public_beta），默认 public
#   --filelist FILE   文件过滤列表（DepotDownloader 的 -filelist 格式）
#   --output DIR      下载输出目录
#   --os OS           目标平台（linux / windows / macos），默认按本机推断
#   --username U      Steam 登录名（默认从认证文件旁的边车文件读）
#   --timeout SEC     超时秒数，默认 900
#   --dry-run         只打印将要执行的命令
#
# 退出码：
#   0 成功        1 参数/环境错误      2 下载失败
#   3 认证文件缺失 4 认证失效/需重新验证 5 目标文件未落地

set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/steam-auth-common.sh
. "$HERE/steam-auth-common.sh"

APP=""; DEPOT=""; MANIFEST=""; BRANCH="public"; FILELIST=""; OUTPUT=""
OS=""; USERNAME=""; TIMEOUT="${STEAM_DOWNLOAD_TIMEOUT:-900}"; DRY_RUN=0

while [ $# -gt 0 ]; do
  case "$1" in
    --app)      [ $# -ge 2 ] || steam_die "--app 缺少参数"; APP="$2"; shift 2 ;;
    --depot)    [ $# -ge 2 ] || steam_die "--depot 缺少参数"; DEPOT="$2"; shift 2 ;;
    --manifest) [ $# -ge 2 ] || steam_die "--manifest 缺少参数"; MANIFEST="$2"; shift 2 ;;
    --branch)   [ $# -ge 2 ] || steam_die "--branch 缺少参数"; BRANCH="$2"; shift 2 ;;
    --filelist) [ $# -ge 2 ] || steam_die "--filelist 缺少参数"; FILELIST="$2"; shift 2 ;;
    --output)   [ $# -ge 2 ] || steam_die "--output 缺少参数"; OUTPUT="$2"; shift 2 ;;
    --os)       [ $# -ge 2 ] || steam_die "--os 缺少参数"; OS="$2"; shift 2 ;;
    --username) [ $# -ge 2 ] || steam_die "--username 缺少参数"; USERNAME="$2"; shift 2 ;;
    --timeout)  [ $# -ge 2 ] || steam_die "--timeout 缺少参数"; TIMEOUT="$2"; shift 2 ;;
    --dry-run)  DRY_RUN=1; shift ;;
    -h|--help)  steam_usage "$0"; exit 0 ;;
    *) steam_die "未知参数：$1" ;;
  esac
done

[ -n "$APP" ]      || steam_die "缺少 --app"
[ -n "$DEPOT" ]    || steam_die "缺少 --depot"
[ -n "$OUTPUT" ]   || steam_die "缺少 --output"
[ -n "$FILELIST" ] || steam_die "缺少 --filelist"
[ -f "$FILELIST" ] || steam_die "文件列表不存在：$FILELIST"

DD="$(steam_find_depotdownloader || true)"
[ -n "$DD" ] || steam_die "未找到 DepotDownloader（用 DEPOTDOWNLOADER_BIN 指定路径）"

if [ -z "$OS" ]; then
  case "$(uname -s)" in
    Linux)  OS="linux" ;;
    Darwin) OS="macos" ;;
    *)      OS="windows" ;;
  esac
fi

# ---- 认证文件 ----
AUTH="$(steam_auth_find || true)"
if [ -z "$AUTH" ]; then
  steam_auth_missing_hint
  exit 3
fi
steam_auth_validate "$AUTH" || { steam_warn "认证文件无法解析（可能已损坏）"; exit 3; }

# ---- 账号名 ----
# 必须在改写 AUTH 之前找边车：边车紧挨着「规范目录」里的那份认证文件，
# 而下一步会把 AUTH 指向原生位置的路径，改写后就找不到边车了。
[ -n "$USERNAME" ] || USERNAME="$(steam_auth_username "$AUTH" || true)"
[ -n "$USERNAME" ] || USERNAME="${STEAM_USERNAME:-}"
[ -n "$USERNAME" ] || steam_die "缺少 Steam 登录名：用 --username 或 STEAM_USERNAME 指定（认证文件旁应有 ${STEAM_AUTH_BASENAME}${STEAM_AUTH_USER_SUFFIX}）"

# 若认证文件不在 DepotDownloader 的原生位置，先装进去
case "$AUTH" in
  "$(steam_isolated_root)"/*) ;;
  *) AUTH="$(steam_auth_install_native "$AUTH")" || steam_die "安装认证文件到原生位置失败" ;;
esac

mkdir -p "$OUTPUT"
OUTPUT="$(cd "$OUTPUT" && pwd)"

# 下载阶段**不**带 -remember-password：那个参数是「登录时把登录态写回 account.config」用的，
# 属于 steam-auth-init.sh 的职责。这里只复用已有登录态，多余的写入/交互都没有好处。
# -no-mobile 保留：避免 Steam Guard 走手机端确认而卡住无人值守流程。
ARGS=(-app "$APP" -depot "$DEPOT" -os "$OS" -osarch 64
      -dir "$OUTPUT" -filelist "$FILELIST"
      -username "$USERNAME" -no-mobile)
[ -n "$MANIFEST" ] && ARGS+=(-manifest "$MANIFEST")
[ -n "$BRANCH" ]   && ARGS+=(-branch "$BRANCH")

steam_log "App/Depot : $APP / $DEPOT（$OS，分支 $BRANCH${MANIFEST:+，manifest $MANIFEST}）"
steam_log "输出目录   : $OUTPUT"
steam_log "认证文件   : $AUTH"
steam_log "账号       : $USERNAME"

if [ "$DRY_RUN" -eq 1 ]; then
  printf '%s\n' "$DD" "${ARGS[@]}"
  exit 0
fi

# ---- 执行下载 ----
LOG="$(mktemp)"
trap 'rm -f "$LOG"' EXIT

# timeout 是必需品：DepotDownloader 在 2FA 提示处会反复重连，没有超时会一直转下去。
command -v timeout >/dev/null 2>&1 || steam_die "系统没有 timeout 命令，无法保证下载不会挂死（Debian/Ubuntu：apt install coreutils）"

set +e
timeout --signal=TERM "$TIMEOUT" "$DD" "${ARGS[@]}" </dev/null >"$LOG" 2>&1
RC=$?
set -e

# 过滤掉可能包含敏感信息的行后回显
steam_redact <"$LOG" | sed 's/^/  | /' >&2

# ---- 认证失效判定 ----
if grep -qiE "Access token was rejected|This account is protected by Steam Guard|Please enter the 2 factor auth code|Enter account password|InvalidPassword|InvalidPasswordSHA1|NoSteamID|Unauthorized|AccessDenied" "$LOG"; then
  cat >&2 <<EOF

[error] 认证失效 / 需要重新人工验证 Steam。

判定依据（已脱敏）：
$(grep -iE "Access token was rejected|Steam Guard|2 factor|account password|InvalidPassword|NoSteamID|Unauthorized|Access denied" "$LOG" | steam_redact | sed -n '1,5p' | sed 's/^/  | /')

处理办法（**不要**在自动化任务里改密码或绕过 Steam Guard）：
  1) 在本地有人的机器上重新执行：
       scripts/steam-auth-init.sh --username $USERNAME
  2) 重新部署认证文件：
       scripts/steam-auth-deploy.sh --remote <user@host> --overwrite
     （GitHub Actions：--secrets pack 覆盖 Secret，Runner 上 --secrets unpack --overwrite）
  3) 重新运行本脚本

EOF
  exit 4
fi

if [ "$RC" -eq 124 ] || [ "$RC" -eq 143 ]; then
  steam_die "下载超时（${TIMEOUT} 秒）。DepotDownloader 可能在认证提示处反复重连；请确认认证文件有效，或调大 --timeout。" 2
fi

if [ "$RC" -ne 0 ]; then
  steam_die "下载失败（退出码 $RC）。若与认证无关，请检查磁盘空间、depot/manifest 是否正确、以及 Steam 是否拒绝返回该历史版本。" 2
fi

# ---- 校验目标文件 ----
# 从 filelist 的每一行推断出期望的文件名：
#   普通路径   "bin/X-Plane-x86_64"        → X-Plane-x86_64
#   正则写法   "regex:(^|/)X-Plane-x86_64$" → X-Plane-x86_64
extract_expected_name() {
  local s="$1"
  s="${s#regex:}"                       # 去掉前缀
  s="$(printf '%s' "$s" | sed -E 's/\\([.\-_/])/\1/g')"  # 去掉转义
  s="${s#^}"
  s="${s%\$}"
  s="$(printf '%s' "$s" | sed -E 's/^\(\^?\|?\/\)?//; s/^\^?\///')"  # 去掉 ^ 或 (^|/) 之类的分组前缀
  s="$(printf '%s' "$s" | sed -E 's/[\^\$\(\)\|\?\*\+\{\}\[\]]//g')" # 去掉剩余正则元字符
  s="${s##*/}"
  printf '%s\n' "$s"
}

MISSING=0
while IFS= read -r line; do
  [ -z "$line" ] && continue
  case "$line" in \#*) continue ;; esac
  base="$(extract_expected_name "$line")"
  [ -z "$base" ] && continue
  # DepotDownloader 可能把文件放在输出目录下的子目录里，因此按名字递归找（限 3 层）
  hit="$(find "$OUTPUT" -maxdepth 3 -type f -name "$base" 2>/dev/null | sed -n '1p')"
  if [ -n "$hit" ]; then
    printf '  ✔ %s（%s 字节）\n' "$base" "$(steam_stat size "$hit")" >&2
  else
    printf '  ✘ %s 未落地\n' "$base" >&2
    MISSING=1
  fi
done < "$FILELIST"

if [ "$MISSING" -eq 1 ]; then
  steam_die "有目标文件没有下载到（见上方 ✘）。可能是 -filelist 的正则没命中，可改用 --file 精确指定文件名。" 5
fi

steam_log "下载完成，目标文件已全部落地：$OUTPUT"
