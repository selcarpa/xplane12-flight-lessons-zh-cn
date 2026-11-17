#!/usr/bin/env bash
# 从 Steam 取回 X-Plane 12 的**可执行文件最小包**（不下载完整游戏），并与 AGENT.md §1.1 的
# 金标准 sha256 自动比对。
#
# 认证方式：**复用已部署的 DepotDownloader 登录态**（account.config），
# 全程零人工输入。详见 docs/ops/steam-auth.md。
#
#   bash .devcontainer/download-exe.sh                       # Linux 可执行文件（public 分支）
#   bash .devcontainer/download-exe.sh --os windows          # Windows 可执行文件
#   bash .devcontainer/download-exe.sh --branch previous_release   # 上一个稳定版（12.4.3）
#
# 预设（来自 Steam app_info，AppID 2014780）：
#   depot 2014784 = Linux   (oslist linux,   osarch 64)
#   depot 2014782 = Windows (oslist windows, osarch 64)
#   depot 2014781 = 公共 depot（≈ 37 GB，游戏主体）—— 本脚本不下载
#   可执行文件名取自 app_info 的 launch 项：X-Plane-x86_64 / X-Plane.exe
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"

TARGET_OS="linux"
BRANCH="public"
DEPLOYED="${STEAMAPPS_DIR:-$HOME/steamapps}/X-Plane 12"
EXTRA_ARGS=()

while [ $# -gt 0 ]; do
  case "$1" in
    --os)
      [ $# -ge 2 ] || { echo "缺少参数值：--os <linux|windows>" >&2; exit 2; }
      TARGET_OS="$2"; shift 2 ;;
    --branch)
      [ $# -ge 2 ] || { echo "缺少参数值：--branch <public|previous_release>" >&2; exit 2; }
      BRANCH="$2"; shift 2 ;;
    --dir)
      [ $# -ge 2 ] || { echo "缺少参数值：--dir <输出目录>" >&2; exit 2; }
      DEPLOYED="$2"; shift 2 ;;
    -h|--help) awk 'NR==1 && /^#!/ {next} /^#/ {sub(/^# ?/,""); print; next} {exit}' "$0"; exit 0 ;;
    *) EXTRA_ARGS+=("$1"); shift ;;
  esac
done

case "$TARGET_OS" in
  linux)
    DEPOT=2014784; FILE="X-Plane-x86_64"; FILELIST="$REPO/scripts/files-linux.txt"
    ;;
  windows)
    DEPOT=2014782; FILE="X-Plane.exe"; FILELIST="$REPO/scripts/files-windows.txt"
    ;;
  *) echo "未知平台：$TARGET_OS（支持 linux / windows）" >&2; exit 2 ;;
esac

# 金标准 sha256 从兼容清单查（唯一事实来源，不在本脚本里硬编码）
GOLDEN="$(python3 "$REPO/scripts/tools/golden-sha256.py" --os "$TARGET_OS" --branch "$BRANCH")" || {
  echo "兼容清单里找不到 os=$TARGET_OS branch=$BRANCH 的条目" >&2
  exit 2
}

# ---- 先确认认证就绪（没有就给出明确指引，而不是让下载脚本莫名失败）----
# --strict：缺账号名边车、权限过宽、文件过旧，都视为「不能无人值守下载」。
if ! bash "$REPO/scripts/steam-auth-check.sh" --strict >/dev/null 2>&1; then
  cat >&2 <<EOF
✘ Steam 认证未就绪：无法无人值守下载。
  需部署 account.config 与 account.config.username 两个文件（详见 docs/ops/steam-auth.md）。
  部署：scripts/steam-auth-deploy.sh --install --auth-file .devcontainer/account.config
EOF
  exit 3
fi

# ---- 下载（真正的逻辑在 scripts/steam-download.sh）----
bash "$REPO/scripts/steam-download.sh" \
  --app 2014780 --depot "$DEPOT" --os "$TARGET_OS" --branch "$BRANCH" \
  --filelist "$FILELIST" --output "$DEPLOYED" "${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}" || exit $?

# ---- 与 AGENT.md §1.1 的金标准比对 ----
EXE=""
[ -f "$DEPLOYED/$FILE" ] && EXE="$DEPLOYED/$FILE"
[ -z "$EXE" ] && EXE="$(find "$DEPLOYED" -maxdepth 3 -type f -name "$FILE" 2>/dev/null | sed -n '1p')"

if [ -n "$EXE" ] && [ -f "$EXE" ]; then
  SZ="$(stat -c%s "$EXE" 2>/dev/null || stat -f%z "$EXE" 2>/dev/null || echo 0)"
  SH="$(sha256sum "$EXE" | cut -d' ' -f1)"
  echo
  echo "文件    : $EXE"
  echo "体积    : $SZ 字节"
  echo "sha256  : $SH"
  echo
  if [ "$SH" = "$GOLDEN" ]; then
    echo "✅ 与 AGENT.md §1.1 记录的原版 sha256 一致（$TARGET_OS / $BRANCH）"
  else
    echo "⚠️ 与 AGENT.md §1.1 的原版 sha256 不同："
    echo "   本次    : $SH"
    echo "   记录    : $GOLDEN"
    echo "   → 通常意味着该分支对应的游戏版本与文档记录的不同（文档滞后），"
    echo "     或者 depot 文件与本地安装版存在差异。先跑 --check 看锚点是否还对得上。"
  fi
  echo
  echo "接着可以：bash .devcontainer/xplane-patch-demo.sh"
else
  echo "⚠️ 目录里没找到 $FILE" >&2
  find "$DEPLOYED" -maxdepth 3 -type f 2>/dev/null | sed -n '1,20p' >&2
  exit 1
fi
