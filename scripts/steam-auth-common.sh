#!/usr/bin/env bash
# steam-auth-common.sh —— Steam 认证相关的共享函数（被其余脚本 source）
#
# ⚠️ 本文件不含任何凭据，只负责：
#    · 定位 DepotDownloader 可执行文件
#    · 定位 / 安装 / 校验 DepotDownloader 的认证文件（account.config）
#    · 处理认证目录与文件的权限
#
# 关于 account.config 的真实位置（重要，不要想当然）：
#   DepotDownloader 用 .NET 的 IsolatedStorage 保存「记住的登录态」，
#   源码里是 AccountSettingsStore.LoadFromFile("account.config")，
#   即**文件名**固定为 account.config，但**存放位置**由 .NET 决定，实测（.NET 10）形如：
#       $XDG_DATA_HOME/IsolatedStorage/<随机8.3>/<哈希8.3>/Url.<md5>/AssemFiles/account.config
#   （默认 XDG_DATA_HOME=~/.local/share）
#   目录名与层数由 .NET 的实现与「程序集身份」决定：不同机器、不同 .NET 版本、
#   不同安装路径都可能不一样（DepotDownloader 3.4.0 未强名签名，身份层是 Url.<md5>）。
#   → 因此这里**不假设层数**：一律用「按文件名找文件」的内容驱动方式定位，
#     部署时也装进「目标机自己」的那个目录，绝不把本机的目录结构原样搬过去。
#
# 本文件被 source 使用；也可直接执行以提供远程安装能力：
#   bash steam-auth-common.sh install-native --src <文件>

set -euo pipefail

# ---------------------------------------------------------------- 日志
steam_log()  { printf '[%s] %s\n' "$(date '+%H:%M:%S')" "$*" >&2; }
steam_warn() { printf '[warn] %s\n' "$*" >&2; }
# steam_die <消息> [退出码]（默认 1）
steam_die()  { local code="${2:-1}"; printf '[error] %s\n' "$1" >&2; exit "$code"; }

# ---------------------------------------------------------------- 帮助与脱敏
# 打印脚本开头那段连续注释块（不含 shebang），避免各脚本自己数行号而把代码当帮助打出来
steam_usage() {
  awk 'NR==1 && /^#!/ {next} /^#/ {sub(/^# ?/,""); print; next} {exit}' "$1"
}

# 日志脱敏：password / token / code 形式的键值一律打码
steam_redact() {
  sed -E 's/(password|token|code)[^ ]*[:=][^ ]*/\1=[REDACTED]/Ig'
}

# 「没找到认证文件」的标准提示（各脚本共用，避免各写一套）
steam_auth_missing_hint() {
  cat >&2 <<EOF
[error] 未找到认证文件（account.config）→ 无法在无人值守的情况下登录。

处理办法：
  1) 在本地（有人工登录的机器）执行：
       scripts/steam-auth-init.sh --username <你的 Steam 账号>
  2) 把认证文件部署到本机：
       scripts/steam-auth-deploy.sh --remote steam@本机 --remote-user \$(whoami)
     或用 GitHub Secrets：--secrets pack / --secrets unpack
EOF
  if [ -n "${1:-}" ]; then printf '%s\n' "$1" >&2; fi
}

# ---------------------------------------------------------------- 常量
# 认证文件在 .NET IsolatedStorage 里的文件名（由 DepotDownloader 源码决定）
STEAM_AUTH_BASENAME="account.config"
# 账号名边车文件（只存登录名，便于远端复用登录态时免交互）
STEAM_AUTH_USER_SUFFIX=".username"
# 规范存放目录（我们自己约定的、可直接传输的位置）
STEAM_AUTH_DIR_DEFAULT="${XDG_DATA_HOME:-$HOME/.local/share}/DepotDownloader"

# ---------------------------------------------------------------- stat 兼容层
# GNU stat 与 BSD stat 的参数不同，这里统一成 steam_stat <mtime|size|mode> <文件>
steam_stat() {
  local what="$1" f="$2"
  case "$what" in
    mtime) stat -c%Y "$f" 2>/dev/null || stat -f%m "$f" 2>/dev/null || echo 0 ;;
    size)  stat -c%s "$f" 2>/dev/null || stat -f%z "$f" 2>/dev/null || echo 0 ;;
    mode)  stat -c%a "$f" 2>/dev/null || stat -f%Lp "$f" 2>/dev/null || echo '?' ;;
    *) echo '?' ;;
  esac
}

# 从 stdin 读路径列表，输出「修改时间最新」的那个（不依赖 GNU find 的 -printf）
steam_latest_path() {
  while IFS= read -r p; do
    [ -n "$p" ] || continue
    [ -e "$p" ] || continue
    printf '%s\t%s\n' "$(steam_stat mtime "$p")" "$p"
  done | sort -rn | sed -n '1p' | cut -f2-
}

# ---------------------------------------------------------------- 找 DepotDownloader
steam_find_depotdownloader() {
  local c
  if [ -n "${DEPOTDOWNLOADER_BIN:-}" ] && [ -x "${DEPOTDOWNLOADER_BIN}" ]; then
    echo "${DEPOTDOWNLOADER_BIN}"; return 0
  fi
  for c in depotdownloader DepotDownloader; do
    if command -v "$c" >/dev/null 2>&1; then command -v "$c"; return 0; fi
  done
  for c in /opt/depotdownloader/DepotDownloader /opt/depotdownloader/depotdownloader \
           "$HOME/.local/bin/DepotDownloader" /usr/local/bin/DepotDownloader; do
    [ -x "$c" ] && { echo "$c"; return 0; }
  done
  return 1
}

# ---------------------------------------------------------------- IsolatedStorage 路径
# .NET 的 LocalApplicationData 在 Linux 上 = $XDG_DATA_HOME 或 ~/.local/share
steam_isolated_root() {
  local base="${XDG_DATA_HOME:-$HOME/.local/share}"
  printf '%s/IsolatedStorage\n' "$base"
}

# 原生 IsolatedStorage 里的 account.config（内容驱动，不假设层数），最新的排前面
steam_auth_native_candidates() {
  local root
  root="$(steam_isolated_root)"
  [ -d "$root" ] || return 0
  find "$root" -type f -name "$STEAM_AUTH_BASENAME" 2>/dev/null \
    | while IFS= read -r f; do
        printf '%s\t%s\n' "$(steam_stat mtime "$f")" "$f"
      done \
    | sort -rn | cut -f2-
}

# 只在原生位置里找（登录轮换时要用：必须是**刚生成**的那份，不能命中旧副本）
steam_auth_find_native() {
  local f
  f="$(steam_auth_native_candidates | sed -n '1p')"
  [ -n "$f" ] && [ -f "$f" ] || return 1
  printf '%s\n' "$f"
}

# 列出候选认证文件位置（按优先级）
steam_auth_candidates() {
  [ -n "${DEPOTDOWNLOADER_ACCOUNT_CONFIG:-}" ] && echo "${DEPOTDOWNLOADER_ACCOUNT_CONFIG}"
  [ -n "${STEAM_AUTH_FILE:-}" ] && echo "${STEAM_AUTH_FILE}"
  echo "${STEAM_AUTH_DIR:-$STEAM_AUTH_DIR_DEFAULT}/${STEAM_AUTH_BASENAME}"
  echo "/home/steam/.DepotDownloader/${STEAM_AUTH_BASENAME}"
  steam_auth_native_candidates
}

# 找到第一个存在的认证文件；找不到返回 1
steam_auth_find() {
  local c
  while IFS= read -r c; do
    [ -n "$c" ] && [ -f "$c" ] && { printf '%s\n' "$c"; return 0; }
  done < <(steam_auth_candidates)
  return 1
}

# 找出本机 DepotDownloader 使用的「身份目录」——也就是 account.config 所在的那个目录。
# 定位顺序（与 .NET 的实际布局解耦）：
#   1) 已存在 account.config → 它的所在目录就是答案（最可靠）
#   2) 只有 AssemFiles 目录（.NET 为程序集文件目录）→ 取最新的一个
#   3) 旧布局兜底：IsolatedStorage/<随机>/<哈希>
#   4) 都没有 → 跑一次 `depotdownloader -V` 让 .NET 建目录（该动作不联网），再回到 1-3
steam_auth_identity_dir() {
  local root f d dd
  root="$(steam_isolated_root)"

  f="$(steam_auth_find_native || true)"
  if [ -n "$f" ]; then
    dirname "$f"
    return 0
  fi

  d="$(find "$root" -type d -name AssemFiles 2>/dev/null | steam_latest_path || true)"
  if [ -n "$d" ]; then
    steam_warn "未找到原生 account.config，装到最新的 AssemFiles 目录：$d"
    printf '%s\n' "$d"
    return 0
  fi

  d="$(find "$root" -mindepth 2 -maxdepth 2 -type d 2>/dev/null | steam_latest_path || true)"
  if [ -n "$d" ]; then
    printf '%s\n' "$d"
    return 0
  fi

  dd="$(steam_find_depotdownloader || true)"
  if [ -n "$dd" ]; then
    # -V 会触发 .NET 建立 IsolatedStorage 目录（该动作不联网）
    "$dd" -V >/dev/null 2>&1 || true
    f="$(steam_auth_find_native || true)"
    if [ -n "$f" ]; then dirname "$f"; return 0; fi
    d="$(find "$root" -type d -name AssemFiles 2>/dev/null | steam_latest_path || true)"
    if [ -n "$d" ]; then
      steam_warn "未找到原生 account.config，装到最新的 AssemFiles 目录：$d"
      printf '%s\n' "$d"
      return 0
    fi
    d="$(find "$root" -mindepth 2 -maxdepth 2 -type d 2>/dev/null | steam_latest_path || true)"
    if [ -n "$d" ]; then printf '%s\n' "$d"; return 0; fi
  fi
  return 1
}

# 把认证文件安装进「本机」的原生 IsolatedStorage 位置
#   用法：steam_auth_install_native <源文件> [根目录]
#   打印安装后的路径
steam_auth_install_native() {
  local src="${1:?需要源文件}" iddir
  [ -f "$src" ] || steam_die "认证文件不存在：$src"
  if [ "$(id -u)" -eq 0 ]; then
    steam_warn "以 root 运行：认证文件将属于 root，DepotDownloader 以普通用户运行时读不到"
  fi

  iddir="$(steam_auth_identity_dir || true)"
  if [ -z "$iddir" ]; then
    steam_die "找不到 DepotDownloader 的 IsolatedStorage 身份目录（$(steam_isolated_root)）。
请先运行一次 DepotDownloader（例如 'DepotDownloader -V'）让它创建存储目录，再重试。"
  fi

  mkdir -p "$iddir"
  chmod 700 "$iddir" 2>/dev/null || true
  install -m 600 "$src" "$iddir/$STEAM_AUTH_BASENAME"
  printf '%s\n' "$iddir/$STEAM_AUTH_BASENAME"
}

# 校验：能否被当作 DepotDownloader 的账号存储解开（Deflate 解压成功且内容非空）
# 只返回 0/1，**不输出任何内容**
steam_auth_validate() {
  local p="${1:?}"
  [ -s "$p" ] || return 1
  if command -v python3 >/dev/null 2>&1; then
    python3 - "$p" >/dev/null 2>&1 <<'PY'
import sys, zlib
data = open(sys.argv[1], 'rb').read()
try:
    raw = zlib.decompressobj(-15).decompress(data)
except Exception:
    sys.exit(1)
# Deflate 解开后应至少是一个 Protobuf 消息（字段号 >= 1，最少 2 字节）
sys.exit(0 if len(raw) >= 2 else 1)
PY
    return $?
  fi
  # 没有 python3 时退化为「非空即可」
  [ -s "$p" ]
}

# 读取账号名（边车文件）
steam_auth_username() {
  local auth="${1:-}" u c
  for c in "${auth}${STEAM_AUTH_USER_SUFFIX}" \
           "${STEAM_AUTH_DIR:-$STEAM_AUTH_DIR_DEFAULT}/${STEAM_AUTH_BASENAME}${STEAM_AUTH_USER_SUFFIX}"; do
    if [ -f "$c" ]; then
      u="$(head -n 1 "$c" | tr -d '\r')"
      [ -n "$u" ] && { printf '%s\n' "$u"; return 0; }
    fi
  done
  return 1
}

# ---------------------------------------------------------------- 远程安装用的小 CLI
steam_common_cli() {
  local cmd="${1:-}"
  shift || true
  case "$cmd" in
    install-native)
      local src=""
      while [ $# -gt 0 ]; do
        case "$1" in
          --src) [ $# -ge 2 ] || steam_die "--src 缺少参数"; src="$2"; shift 2 ;;
          *) steam_die "未知参数：$1" ;;
        esac
      done
      [ -n "$src" ] || steam_die "缺少 --src"
      steam_auth_install_native "$src" >/dev/null
      echo "OK"
      ;;
    find)
      steam_auth_find || steam_die "未找到认证文件"
      ;;
    *)
      steam_die "用法：steam-auth-common.sh {install-native --src F | find}"
      ;;
  esac
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then
  steam_common_cli "$@"
fi
