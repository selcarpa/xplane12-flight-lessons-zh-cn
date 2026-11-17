#!/usr/bin/env bash
# 在下载到的 X-Plane 12 **副本**上跑 --check / --dry-run / --apply，验证补丁引擎的行为。
# 真游戏目录绝不会被修改：脚本先 cp 一份副本再操作。
#
#   bash .devcontainer/xplane-patch-demo.sh            # --check + --dry-run
#   bash .devcontainer/xplane-patch-demo.sh --apply    # 额外 --apply / --verify，并打印 sha256
#   bash .devcontainer/xplane-patch-demo.sh --dir "<游戏目录>"
#   bash .devcontainer/xplane-patch-demo.sh --force    # 允许用 $HOME 之外的路径（副本目录）
set -euo pipefail
cd "$(dirname "$0")/.." || exit 1

APPS_ROOT="${STEAMAPPS_DIR:-$HOME/steamapps}"
GAME_DIR="$APPS_ROOT/X-Plane 12"
# 注意用 ${VAR-默认} 而不是 ${VAR:-默认}：显式设为空串时保留空串，交给下面的守卫拒绝
COPY_DIR="${XP12_COPY_DIR-$HOME/xplane12-copy}"
DO_APPLY=0
FORCE=0

while [ $# -gt 0 ]; do
  case "$1" in
    --apply) DO_APPLY=1; shift ;;
    --force) FORCE=1; shift ;;
    --dir)
      [ $# -ge 2 ] || { echo "缺少参数值：--dir <游戏目录>" >&2; exit 2; }
      GAME_DIR="$2"; shift 2 ;;
    -h|--help) awk 'NR==1 && /^#!/ {next} /^#/ {sub(/^# ?/,""); print; next} {exit}' "$0"; exit 0 ;;
    *) echo "未知参数：$1" >&2; exit 2 ;;
  esac
done

# ---- 路径守卫：副本目录会被 rm -rf，绝不允许指向家目录 / 根目录 / $HOME 之外 ----
guard_path() {
  # $1 = 路径，$2 = 说明
  case "$1" in
    "") echo "拒绝执行：$2 路径为空。" >&2; return 1 ;;
    "/") echo "拒绝执行：$2 不能是根目录 /。" >&2; return 1 ;;
  esac
  if [ "$1" = "$HOME" ]; then
    echo "拒绝执行：$2 不能是家目录本身（$HOME）。" >&2
    echo "  请改用 --dir 指定游戏目录，或修正环境变量 XP12_COPY_DIR。" >&2
    return 1
  fi
  case "$1" in
    "$HOME"/*) return 0 ;;
  esac
  if [ "$FORCE" -ne 1 ]; then
    echo "拒绝执行：$2 必须在 $HOME/ 之下：$1" >&2
    echo "  请改用 --dir 指定游戏目录，或修正环境变量 XP12_COPY_DIR；确需该路径请加 --force。" >&2
    return 1
  fi
  return 0
}

guard_path "$GAME_DIR" "游戏目录" || exit 2
guard_path "$COPY_DIR" "副本目录" || exit 2

TOOL="dist/linux-x64/XPlane12-FlightLessons-zh-CN"
if [ ! -x "$TOOL" ]; then
  echo "未找到已发布程序，先执行：dotnet publish src/XpLessonZhCN.csproj -c Release -r linux-x64 --self-contained true -o dist/linux-x64" >&2
  exit 1
fi

# 找到游戏可执行文件（Linux 原生 ELF 或 Windows PE）
EXE=""
for cand in X-Plane-x86_64 X-Plane.exe; do
  [ -f "$GAME_DIR/$cand" ] && { EXE="$GAME_DIR/$cand"; break; }
done
if [ -z "$EXE" ]; then
  echo "在 $GAME_DIR 里没找到 X-Plane-x86_64 / X-Plane.exe。" >&2
  echo "先执行：bash .devcontainer/download-exe.sh（认证见 docs/ops/steam-auth.md）" >&2
  exit 1
fi

echo "源文件  : $EXE"
echo "体积    : $(stat -c%s "$EXE" 2>/dev/null || stat -f%z "$EXE" 2>/dev/null || echo 0) 字节"
echo "sha256  : $(sha256sum "$EXE" | cut -d' ' -f1)"
rm -rf "$COPY_DIR"
mkdir -p "$COPY_DIR"
cp "$EXE" "$COPY_DIR/"
echo "已复制副本到：$COPY_DIR（真游戏不会被修改）"
echo

run() { echo "\$ $*"; "$@"; echo; }

run "$TOOL" --check    --path "$COPY_DIR"
run "$TOOL" --dry-run  --path "$COPY_DIR"

if [ "$DO_APPLY" -eq 1 ]; then
  run "$TOOL" --apply --path "$COPY_DIR"
  echo "打完后的 sha256（应与 AGENT.md §1.1 的金标准一致）："
  # 只哈希被改的那个可执行文件：目录里还可能有 .bak 备份，混在一起容易拿错行
  sha256sum "$COPY_DIR/$(basename "$EXE")" | sed 's/^/  /'
  run "$TOOL" --verify --path "$COPY_DIR"
fi
