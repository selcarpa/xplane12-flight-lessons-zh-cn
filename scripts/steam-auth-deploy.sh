#!/usr/bin/env bash
# steam-auth-deploy.sh —— 把本地生成的认证文件安全地部署到目标机
#
# 三种模式：
#   1) --remote <user@host>   通过 SSH/SCP 部署到远程 Linux 主机（CI Runner / 自建机）
#   2) --install              在**本机**安装（把认证文件装进本机 DepotDownloader 的原生位置）
#   3) --secrets pack|unpack  GitHub Actions 用的加密传递（本地加密 → 存 Secret → Runner 解密）
#
# 安全约束：
#   · 认证文件视为高敏感凭据：绝不打印内容、绝不提交 Git、绝不放进公开 Artifact
#   · 远程目录 700、文件 600、属主为专用用户 steam
#   · 默认不覆盖已存在的认证文件（需显式 --overwrite）
#   · 传输失败一律返回非零退出码
#
# 用法：
#   scripts/steam-auth-deploy.sh --remote steam@runner.example.com [--port 22] [--remote-user steam] [--remote-dir DIR] [--overwrite]
#   scripts/steam-auth-deploy.sh --install [--auth-file FILE] [--overwrite]
#   scripts/steam-auth-deploy.sh --secrets pack [--in FILE] [--out FILE]
#   scripts/steam-auth-deploy.sh --secrets unpack [--in FILE] [--out FILE]

set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/steam-auth-common.sh
. "$HERE/steam-auth-common.sh"

MODE=""
SECRETS_SUB=""
REMOTE=""
PORT="22"
REMOTE_USER="steam"
REMOTE_DIR="/home/steam/.DepotDownloader"
AUTH_FILE=""
OUT_FILE=""
IN_FILE=""
OVERWRITE=0
CREATE_USER=0

while [ $# -gt 0 ]; do
  case "$1" in
    --remote)      [ $# -ge 2 ] || steam_die "--remote 缺少参数"; REMOTE="$2"; MODE="remote"; shift 2 ;;
    --install)     MODE="install"; shift ;;
    --secrets)     [ $# -ge 2 ] || steam_die "--secrets 缺少参数（pack | unpack）"; MODE="secrets"; SECRETS_SUB="$2"; shift 2 ;;
    --port)        [ $# -ge 2 ] || steam_die "--port 缺少参数"; PORT="$2"; shift 2 ;;
    --remote-user) [ $# -ge 2 ] || steam_die "--remote-user 缺少参数"; REMOTE_USER="$2"; shift 2 ;;
    --remote-dir)  [ $# -ge 2 ] || steam_die "--remote-dir 缺少参数"; REMOTE_DIR="$2"; shift 2 ;;
    --auth-file)   [ $# -ge 2 ] || steam_die "--auth-file 缺少参数"; AUTH_FILE="$2"; shift 2 ;;
    --in)          [ $# -ge 2 ] || steam_die "--in 缺少参数"; IN_FILE="$2"; shift 2 ;;
    --out)         [ $# -ge 2 ] || steam_die "--out 缺少参数"; OUT_FILE="$2"; shift 2 ;;
    --overwrite)   OVERWRITE=1; shift ;;
    --create-user) CREATE_USER=1; shift ;;
    -h|--help)     steam_usage "$0"; exit 0 ;;
    *) steam_die "未知参数：$1" ;;
  esac
done
[ -n "$MODE" ] || steam_die "请指定模式：--remote <user@host> | --install | --secrets pack|unpack"

SSH=(ssh -p "$PORT" -o BatchMode=yes -o StrictHostKeyChecking=accept-new)
SCP=(scp -P "$PORT" -o BatchMode=yes -o StrictHostKeyChecking=accept-new)

# ---- 阻止把凭据写进 Git 仓库里未忽略的路径 ----
guard_not_in_git() {
  local p="$1" dir top abs
  dir="$(dirname "$p")"
  [ -d "$dir" ] || return 0
  git -C "$dir" rev-parse --is-inside-work-tree >/dev/null 2>&1 || return 0
  top="$(git -C "$dir" rev-parse --show-toplevel 2>/dev/null)" || return 0
  # 用绝对路径判断，否则 git 会把相对路径当成相对 -C 目录
  case "$p" in /*) abs="$p" ;; *) abs="$top/$p" ;; esac
  if ! git -C "$top" check-ignore -q "$abs" 2>/dev/null; then
    steam_die "拒绝写入未被 .gitignore 忽略的仓库内路径：$p
请把该路径加入 .gitignore，或改用仓库外的路径。"
  fi
  # 已经被 Git 跟踪的文件，ignore 规则是无效的 —— 那更危险
  if git -C "$top" ls-files --error-unmatch "$abs" >/dev/null 2>&1; then
    steam_die "拒绝写入已被 Git 跟踪的路径：$p
请先把它从版本控制里移除（git rm --cached），或改用仓库外的路径。"
  fi
}

# =================================================================== 加密传递
if [ "$MODE" = "secrets" ]; then
  SUB="$SECRETS_SUB"
  case "$SUB" in
    pack)
      SRC="$AUTH_FILE"
      [ -n "$SRC" ] || SRC="$(steam_auth_find || true)"
      [ -n "$SRC" ] || steam_die "未找到认证文件（--auth-file 或先执行 steam-auth-init.sh）"
      [ -n "$OUT_FILE" ] && guard_not_in_git "$OUT_FILE"
      PASSPHRASE="${STEAM_AUTH_PASSPHRASE:-}"
      if [ -z "$PASSPHRASE" ]; then
        printf '加密口令（不回显）: ' >&2
        read -r -s PASSPHRASE || steam_die "无法读取口令（stdin 不是终端，请设置 STEAM_AUTH_PASSPHRASE）"
        echo >&2
        [ -n "$PASSPHRASE" ] || steam_die "口令不能为空"
      fi
      command -v openssl >/dev/null 2>&1 || steam_die "需要 openssl"
      if [ -n "$OUT_FILE" ]; then
        STEAM_AUTH_PASSPHRASE="$PASSPHRASE" openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -a -A \
          -pass env:STEAM_AUTH_PASSPHRASE -in "$SRC" -out "$OUT_FILE"
        chmod 600 "$OUT_FILE"
        steam_log "已加密 → $OUT_FILE（base64 文本，可安全存入 GitHub Secret）"
      else
        STEAM_AUTH_PASSPHRASE="$PASSPHRASE" openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -a -A \
          -pass env:STEAM_AUTH_PASSPHRASE -in "$SRC"
        echo
      fi
      ;;
    unpack)
      SRC="$IN_FILE"
      [ -n "$SRC" ] || steam_die "解密需要 --in <密文文件>"
      [ -f "$SRC" ] || steam_die "密文文件不存在：$SRC"
      DEST="${OUT_FILE:-${STEAM_AUTH_DIR:-$STEAM_AUTH_DIR_DEFAULT}/$STEAM_AUTH_BASENAME}"
      [ -e "$DEST" ] && [ "$OVERWRITE" -eq 0 ] && steam_die "目标已存在：$DEST（需要覆盖请加 --overwrite）"
      PASSPHRASE="${STEAM_AUTH_PASSPHRASE:-}"
      if [ -z "$PASSPHRASE" ]; then
        printf '解密口令（不回显）: ' >&2
        read -r -s PASSPHRASE || steam_die "无法读取口令（stdin 不是终端，请设置 STEAM_AUTH_PASSPHRASE）"
        echo >&2
        [ -n "$PASSPHRASE" ] || steam_die "口令不能为空"
      fi
      mkdir -p "$(dirname "$DEST")"; chmod 700 "$(dirname "$DEST")"
      STEAM_AUTH_PASSPHRASE="$PASSPHRASE" openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -a -A \
        -pass env:STEAM_AUTH_PASSPHRASE -in "$SRC" -out "$DEST" || steam_die "解密失败：口令不对或密文损坏"
      chmod 600 "$DEST"
      steam_auth_validate "$DEST" || steam_die "解密结果不是有效的 DepotDownloader 认证文件"
      steam_log "已解密 → $DEST（600）"
      # 顺手装进原生位置，下载脚本就能直接复用
      if INSTALLED="$(steam_auth_install_native "$DEST")"; then
        steam_log "已安装到原生位置：$INSTALLED"
      else
        steam_warn "没能装进 DepotDownloader 的原生 IsolatedStorage 位置，下载前需再执行一次 --install"
      fi
      ;;
    *) steam_die "--secrets 只支持 pack | unpack" ;;
  esac
  exit 0
fi

# =================================================================== 本机安装
if [ "$MODE" = "install" ]; then
  SRC="$AUTH_FILE"
  [ -n "$SRC" ] || SRC="$(steam_auth_find || true)"
  [ -n "$SRC" ] || steam_die "未找到认证文件（--auth-file 或先执行 steam-auth-init.sh）"
  [ -f "$SRC" ] || steam_die "认证文件不存在：$SRC"
  DEST="${STEAM_AUTH_DIR:-$STEAM_AUTH_DIR_DEFAULT}/$STEAM_AUTH_BASENAME"
  if [ -e "$DEST" ] && [ "$OVERWRITE" -eq 0 ] && [ "$SRC" != "$DEST" ]; then
    steam_die "目标已存在：$DEST（需要覆盖请加 --overwrite）"
  fi
  mkdir -p "$(dirname "$DEST")"; chmod 700 "$(dirname "$DEST")"
  if [ "$SRC" -ef "$DEST" ]; then
    # 源与目标本来就是同一个文件（init 已经把认证文件放在规范目录时就是这个情况）
    # → 不重复复制，只把权限收紧
    chmod 600 "$DEST"
    steam_log "源文件与目标位置相同，仅收紧权限"
  else
    install -m 600 "$SRC" "$DEST"
  fi
  # 账号名边车（找不到就不要写空文件，否则下游会以为「有边车但内容为空」）
  U="$(steam_auth_username "$SRC" || true)"
  [ -n "$U" ] || U="${STEAM_USERNAME:-}"
  if [ -n "$U" ]; then
    printf '%s\n' "$U" > "$DEST$STEAM_AUTH_USER_SUFFIX"; chmod 600 "$DEST$STEAM_AUTH_USER_SUFFIX"
  else
    steam_warn "源认证文件旁没有账号名边车（${STEAM_AUTH_BASENAME}${STEAM_AUTH_USER_SUFFIX}），下游下载需要 --username 或 STEAM_USERNAME"
  fi
  steam_auth_validate "$DEST" || steam_die "认证文件校验失败"
  INSTALLED="$(steam_auth_install_native "$DEST")"
  steam_log "已安装：$DEST"
  steam_log "原生位置：$INSTALLED"
  exit 0
fi

# =================================================================== 远程部署
[ -n "$REMOTE" ] || steam_die "--remote 需要 <user@host>"

SRC="$AUTH_FILE"
[ -n "$SRC" ] || SRC="$(steam_auth_find || true)"
[ -n "$SRC" ] || steam_die "本机未找到认证文件（--auth-file 或先执行 steam-auth-init.sh）"
[ -f "$SRC" ] || steam_die "认证文件不存在：$SRC"
steam_auth_validate "$SRC" || steam_die "认证文件校验失败（不是有效的 DepotDownloader 账号存储）"

USERNAME="$(steam_auth_username "$SRC" || true)"
[ -n "$USERNAME" ] || USERNAME="${STEAM_USERNAME:-}"
[ -n "$USERNAME" ] || steam_warn "没有找到账号名边车文件，远程下载任务需要额外传 --username/STEAM_USERNAME"

steam_log "源认证文件：$SRC"
steam_log "目标：$REMOTE（用户 $REMOTE_USER，目录 $REMOTE_DIR）"

# 1) 远端用户
RU_Q="$(printf '%q' "$REMOTE_USER")"
RD_Q="$(printf '%q' "$REMOTE_DIR")"
RF_Q="$(printf '%q' "$REMOTE_FILE")"
if ! "${SSH[@]}" "$REMOTE" "id -u $RU_Q" >/dev/null 2>&1; then
  if [ "$CREATE_USER" -eq 1 ]; then
    steam_log "远端创建专用用户 $REMOTE_USER"
    "${SSH[@]}" "$REMOTE" "sudo useradd -m -s /bin/bash $RU_Q" || steam_die "创建远端用户失败"
  else
    steam_die "远端不存在用户 $REMOTE_USER。
请在远端执行：sudo useradd -m -s /bin/bash $REMOTE_USER
或给本脚本加 --create-user。"
  fi
fi

# 2) 已存在则拒绝覆盖
REMOTE_FILE="$REMOTE_DIR/$STEAM_AUTH_BASENAME"
if "${SSH[@]}" "$REMOTE" "test -f $RF_Q" >/dev/null 2>&1; then
  if [ "$OVERWRITE" -eq 0 ]; then
    steam_die "远端已存在认证文件：$REMOTE_FILE
确需覆盖请加 --overwrite（轮换流程见 docs/ops/steam-auth.md）"
  fi
  steam_log "覆盖远端已存在的认证文件"
fi

# 3) 传输（先传临时文件，成功后再原子移动）
TMPREMOTE="$("${SSH[@]}" "$REMOTE" "mktemp -d /tmp/.steam-auth.XXXXXX")" || steam_die "无法在远端创建临时目录"
cleanup() { "${SSH[@]}" "$REMOTE" "rm -rf '$TMPREMOTE'" >/dev/null 2>&1 || true; }
trap cleanup EXIT

steam_log "传输中…"
"${SCP[@]}" -q "$SRC" "$REMOTE:$TMPREMOTE/$STEAM_AUTH_BASENAME" || steam_die "SCP 传输失败"
if [ -n "$USERNAME" ]; then
  printf '%s\n' "$USERNAME" | "${SSH[@]}" "$REMOTE" "cat > '$TMPREMOTE/$STEAM_AUTH_USER_SUFFIX'" || steam_die "传输账号名失败"
fi
"${SSH[@]}" "$REMOTE" "cat > '$TMPREMOTE/steam-auth-common.sh'" < "$HERE/steam-auth-common.sh" || steam_die "传输辅助脚本失败"

# 4) 远端落位：目录 700 / 文件 600 / 属主 steam
#    注意：临时目录保持 mktemp 的 700 不变（里面装的是凭据，不能放宽成 755）。
#    辅助脚本改为直接放进 REMOTE_DIR（属主 steam、目录 700），远端用户才读得到。
"${SSH[@]}" "$REMOTE" "set -e
sudo mkdir -p $RD_Q
sudo chown $RU_Q $RD_Q
sudo chmod 700 $RD_Q
sudo mv '$TMPREMOTE/$STEAM_AUTH_BASENAME' $RF_Q
sudo chown $RU_Q $RF_Q
sudo chmod 600 $RF_Q
if [ -f '$TMPREMOTE/$STEAM_AUTH_USER_SUFFIX' ]; then
  sudo mv '$TMPREMOTE/$STEAM_AUTH_USER_SUFFIX' $RF_Q'$STEAM_AUTH_USER_SUFFIX'
  sudo chown $RU_Q $RF_Q'$STEAM_AUTH_USER_SUFFIX'
  sudo chmod 600 $RF_Q'$STEAM_AUTH_USER_SUFFIX'
fi
sudo mv '$TMPREMOTE/steam-auth-common.sh' $RD_Q'/steam-auth-common.sh'
sudo chown $RU_Q $RD_Q'/steam-auth-common.sh'
sudo chmod 700 $RD_Q'/steam-auth-common.sh'
" || steam_die "远端落位失败"

# 5) 装进远端自己的 IsolatedStorage 身份目录（目录名与本机不同，必须在远端解析）
steam_log "在远端安装到原生 IsolatedStorage 位置…"
INSTALL_OUT="$("${SSH[@]}" "$REMOTE" "sudo -u $RU_Q -H bash $RD_Q'/steam-auth-common.sh' install-native --src $RF_Q" 2>&1)" \
  || steam_die "远端原生位置安装失败：
$INSTALL_OUT"
steam_log "远端原生安装完成"

cat >&2 <<EOF

✔ 部署完成

  远端认证文件 : $REMOTE_FILE（600，属主 $REMOTE_USER）
  远端原生位置 : ~/.local/share/IsolatedStorage/<随机>/<哈希>/Url.<md5>/AssemFiles/account.config
                  （层数与目录名由 .NET 决定，脚本按文件名定位，不写死）
  账号名       : ${USERNAME:-（未提供，下载时需 --username）}

  在远端验证：
    ssh $REMOTE "scripts/steam-auth-check.sh"

  远端下载（零交互）：
    ssh $REMOTE "scripts/steam-download.sh --app 2014780 --depot 2014784 --filelist scripts/files-linux.txt --output ~/downloads/linux"

EOF
