#!/usr/bin/env bash
# steam-auth-check.sh —— 体检已部署的 DepotDownloader 认证文件
#
# 检查项：
#   · 认证文件是否存在、非空
#   · 是否能按 DepotDownloader 的格式解开（Deflate 解压）
#   · 文件 / 目录权限是否收紧（600 / 700）
#   · 账号名边车文件是否在（缺了就没法零交互下载）
#   · 是否已装进 DepotDownloader 的**原生 IsolatedStorage 位置**
#   · 文件修改时间（判断是否需要轮换）
#
# 用法：
#   scripts/steam-auth-check.sh [--path <认证文件>] [--strict]
#
# 退出码：0 正常；2 未找到认证文件；3 认证文件损坏；1 其他问题。
# 本脚本**不会输出认证文件内容**，只输出路径、大小、时间与结论。

set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/steam-auth-common.sh
. "$HERE/steam-auth-common.sh"

PATH_ARG=""
STRICT=0
while [ $# -gt 0 ]; do
  case "$1" in
    --path)   [ $# -ge 2 ] || steam_die "--path 缺少参数"; PATH_ARG="$2"; shift 2 ;;
    --strict) STRICT=1; shift ;;
    -h|--help) steam_usage "$0"; exit 0 ;;
    *) steam_die "未知参数：$1" ;;
  esac
done

AUTH=""
if [ -n "$PATH_ARG" ]; then
  [ -f "$PATH_ARG" ] || steam_die "指定的认证文件不存在：$PATH_ARG"
  AUTH="$PATH_ARG"
else
  AUTH="$(steam_auth_find || true)"
fi

if [ -z "$AUTH" ]; then
  steam_auth_missing_hint "查找位置（按优先级）：
  \$DEPOTDOWNLOADER_ACCOUNT_CONFIG
  \$STEAM_AUTH_FILE
  ${STEAM_AUTH_DIR:-$STEAM_AUTH_DIR_DEFAULT}/account.config
  /home/steam/.DepotDownloader/account.config
  $(steam_isolated_root)/ 下的任意 account.config（按文件名查找，不写死层数）"
  exit 2
fi

SIZE="$(steam_stat size "$AUTH")"
MODE="$(steam_stat mode "$AUTH")"
DIRMODE="$(steam_stat mode "$(dirname "$AUTH")")"
MTIME="$(date -r "$AUTH" '+%Y-%m-%d %H:%M:%S' 2>/dev/null || echo '未知')"
DAYS=$(( ( $(date +%s) - $(steam_stat mtime "$AUTH") ) / 86400 ))

printf '认证文件   : %s\n' "$AUTH"
printf '大小       : %s 字节\n' "$SIZE"
printf '权限       : %s（目录 %s）\n' "$MODE" "$DIRMODE"
printf '修改时间   : %s（%s 天前）\n' "$MTIME" "$DAYS"

RC=0
WARN=0
if [ "$SIZE" -eq 0 ]; then
  steam_warn "认证文件为空 → 认证已失效或初始化未完成"
  RC=3; WARN=$((WARN+1))
elif ! steam_auth_validate "$AUTH"; then
  steam_warn "认证文件无法按 DepotDownloader 的账号存储格式解开 → 已损坏"
  RC=3; WARN=$((WARN+1))
else
  printf '格式       : ✔ 可解析（DepotDownloader 账号存储）\n'
fi

# 账号名边车：缺了就没法在无人值守的情况下登录
USER_NAME="$(steam_auth_username "$AUTH" || true)"
if [ -n "$USER_NAME" ]; then
  printf '账号       : %s\n' "$USER_NAME"
else
  steam_warn "没找到账号名边车文件（${STEAM_AUTH_BASENAME}${STEAM_AUTH_USER_SUFFIX}），无人值守下载需要 --username 或 STEAM_USERNAME"
  WARN=$((WARN+1)); [ "$STRICT" -eq 1 ] && RC=1
fi

# 原生位置：DepotDownloader 只认 IsolatedStorage 里那份，文件有效但没就位 = 下载会失败
NATIVE="$(steam_auth_find_native || true)"
if [ -n "$NATIVE" ]; then
  printf '原生位置   : ✔ 已就位（%s）\n' "$NATIVE"
else
  steam_warn "还没装进 DepotDownloader 的原生 IsolatedStorage 位置 → 自动下载会反复要求验证
  修复：scripts/steam-auth-deploy.sh --install --auth-file <本文件>"
  WARN=$((WARN+1)); [ "$STRICT" -eq 1 ] && RC=1
fi

if [ "$MODE" != "600" ] || [ "$DIRMODE" != "700" ]; then
  steam_warn "权限过宽：期望文件 600 / 目录 700，可用 chmod 600 <文件> && chmod 700 <目录> 修正"
  WARN=$((WARN+1)); [ "$STRICT" -eq 1 ] && RC=1
fi

if [ "$DAYS" -gt 90 ]; then
  steam_warn "认证文件已存在 $DAYS 天，建议按 docs/ops/steam-auth.md 的「轮换」流程重新初始化"
  WARN=$((WARN+1)); [ "$STRICT" -eq 1 ] && RC=1
fi

if [ "$RC" -ne 0 ]; then
  printf '结论       : ✘ 认证不可用，请重新执行 scripts/steam-auth-init.sh 并重新部署\n'
elif [ "$WARN" -gt 0 ]; then
  printf '结论       : ⚠ 可用，但有 %s 项需要注意（见上方 warn；--strict 会把它们判为失败）\n' "$WARN"
else
  printf '结论       : ✔ 认证可用\n'
fi
exit "$RC"
