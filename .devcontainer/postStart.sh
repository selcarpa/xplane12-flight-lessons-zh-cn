#!/usr/bin/env bash
# devcontainer postStartCommand：每次容器启动时打一条状态横幅（快速、失败不影响使用）
set -uo pipefail
cd "$(dirname "$0")/.." || exit 0

SDK="$(dotnet --version 2>/dev/null || echo '未安装 .NET')"
APPS="${STEAMAPPS_DIR:-$HOME/steamapps}"
GAME_DIR="$APPS/X-Plane 12"
AVAIL="$(df -h "$HOME" 2>/dev/null | awk 'NR==2 {print $4}')"

printf '\033[36m────────────────────────────────────────────\033[0m\n'
printf '\033[1mX-Plane 12 飞行教学汉化\033[0m · .NET %s\n' "$SDK"
printf '  可执行文件(linux)   : %s\n' "$GAME_DIR/X-Plane-x86_64"
printf '  可执行文件(windows) : %s\n' "$GAME_DIR/X-Plane.exe"
printf '  DepotDownloader: %s\n' \
  "$(command -v depotdownloader >/dev/null 2>&1 && echo ✔ || echo ✘)"
printf '  可用磁盘空间   : %s\n' "${AVAIL:-未知}"
# --strict：缺账号名边车、权限过宽、文件过旧都算「不能无人值守下载」
if bash scripts/steam-auth-check.sh --strict >/dev/null 2>&1; then
  printf '  Steam 认证     : ✔ 已就绪（可无人值守下载）\n'
else
  printf '  ✘ Steam 认证未就绪：无法无人值守下载。\n'
  printf '    需部署 account.config 与 account.config.username 两个文件（详见 docs/ops/steam-auth.md）。\n'
  printf '    部署：scripts/steam-auth-deploy.sh --install --auth-file .devcontainer/account.config\n'
fi
printf '  取回 X-Plane 12 的可执行文件只需 ≈79 MB（十进制）磁盘（不含整包）\n'
printf '\033[36m────────────────────────────────────────────\033[0m\n\n'
