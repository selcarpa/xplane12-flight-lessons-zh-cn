#!/usr/bin/env bash
# 一键构建便携包（需要 .NET 9 SDK）
#
#   scripts/build.sh                    构建 + 冒烟测试 + 打包（linux-x64）
#   scripts/build.sh --rid linux-arm64  指定运行时标识（RID）
#   scripts/build.sh --version 1.2.3    透传版本号（CI 打 tag 时使用）
#   scripts/build.sh --skip-tests       跳过冒烟测试
#
# publish 参数、产物文件名规则、随包文档路径统一来自 scripts/lib/build-common.props
# （唯一事实源），CI 只调用本脚本，不再内联复制参数。
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT"

RID="linux-x64"
VERSION=""
SKIP_TESTS=0

while [ $# -gt 0 ]; do
  case "$1" in
    --rid)      [ $# -ge 2 ] || { printf '[build] ✘ --rid 需要一个参数\n' >&2; exit 2; }; RID="$2"; shift 2 ;;
    --rid=*)    RID="${1#*=}"; shift ;;
    --version)  [ $# -ge 2 ] || { printf '[build] ✘ --version 需要一个参数\n' >&2; exit 2; }; VERSION="$2"; shift 2 ;;
    --version=*) VERSION="${1#*=}"; shift ;;
    --skip-tests) SKIP_TESTS=1; shift ;;
    -h|--help)  awk 'NR==1 && /^#!/ {next} /^#/ {sub(/^# ?/,""); print; next} {exit}' "$0"; exit 0 ;;
    *) printf '[build] ✘ 未知参数：%s\n' "$1" >&2; exit 2 ;;
  esac
done

# 共享配置（publish 属性 / 产物名 / 随包文档）——唯一事实源
# shellcheck source=lib/build-common.props
. "$SCRIPT_DIR/lib/build-common.props"

# 若系统没有 .NET SDK、但仓库内放了便携 SDK（.dotnet/，已在 .gitignore），就用它。
# 便携 SDK 只在本机缺 SDK 时才存在，正常开发机上不触发。
if [ -x "$PWD/.dotnet/dotnet" ]; then
  export DOTNET_ROOT="$PWD/.dotnet"
  export PATH="$PWD/.dotnet:$PATH"
  export NUGET_PACKAGES="${NUGET_PACKAGES:-$PWD/.nuget}"
  export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$PWD/.dotnet-cli-home}"
fi

# publish 参数集中在此一处（MSBuild 属性来自 build-common.props）
PUBLISH_ARGS=(-c Release -r "$RID" --self-contained true)
old_ifs="$IFS"
IFS=','
for prop in $PUBLISH_PROPS; do
  if [ -n "$prop" ]; then
    PUBLISH_ARGS+=("-p:$prop")
  fi
done
IFS="$old_ifs"
if [ -n "$VERSION" ]; then
  PUBLISH_ARGS+=("-p:Version=$VERSION")
fi

echo '=== 1/4 构建 ==='
dotnet build src/XpLessonZhCN.csproj -c Release --nologo

if [ "$SKIP_TESTS" -eq 0 ]; then
  echo '=== 2/4 冒烟测试（核心逻辑 + GUI headless） ==='
  dotnet run --project tests/SmokeTests/SmokeTests.csproj -c Release
fi

echo "=== 3/4 发布（$RID 自包含） ==="
rm -rf "dist/$RID"
dotnet publish src/XpLessonZhCN.csproj "${PUBLISH_ARGS[@]}" -o "dist/$RID" --nologo

echo '=== 4/4 打包 ==='
cd "dist/$RID"
chmod +x XPlane12-FlightLessons-zh-CN
cp "$ROOT/$USER_GUIDE" "$USER_GUIDE_NAME"
cd ..
TARBALL="$APP_BASENAME-$RID.tar.gz"
rm -f "$TARBALL"
tar czf "$TARBALL" -C "$RID" .

# SHA256SUMS：格式与 sha256sum 兼容（每行 "<hash>  <文件名>"），UTF-8 无 BOM
if command -v sha256sum >/dev/null 2>&1; then
  sha256sum "$TARBALL" > "SHA256SUMS-$RID.txt"
else
  shasum -a 256 "$TARBALL" > "SHA256SUMS-$RID.txt"
fi

echo
echo "完成：dist/$TARBALL"
cat "SHA256SUMS-$RID.txt"
