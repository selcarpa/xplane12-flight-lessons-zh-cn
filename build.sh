#!/usr/bin/env bash
# 一键构建 Linux 便携包（需要 .NET 9 SDK）
#   ./build.sh            构建 + 冒烟测试 + 打包
#   ./build.sh --skip-tests
set -euo pipefail

cd "$(dirname "$0")"
SKIP_TESTS=0
[ "${1:-}" = "--skip-tests" ] && SKIP_TESTS=1

# 若系统没有 .NET SDK、但仓库内放了便携 SDK（.dotnet/，已在 .gitignore），就用它。
# 便携 SDK 只在本机缺 SDK 时才存在，正常开发机上不触发。
if [ -x "$PWD/.dotnet/dotnet" ]; then
  export DOTNET_ROOT="$PWD/.dotnet"
  export PATH="$PWD/.dotnet:$PATH"
  export NUGET_PACKAGES="${NUGET_PACKAGES:-$PWD/.nuget}"
  export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$PWD/.dotnet-cli-home}"
fi

echo '=== 1/4 构建 ==='
dotnet build src/XpLessonZhCN.csproj -c Release --nologo

if [ "$SKIP_TESTS" -eq 0 ]; then
  echo '=== 2/4 冒烟测试（核心逻辑 + GUI headless） ==='
  dotnet run --project tests/SmokeTests/SmokeTests.csproj -c Release
fi

echo '=== 3/4 发布（linux-x64 自包含） ==='
rm -rf dist/linux-x64
dotnet publish src/XpLessonZhCN.csproj -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=false \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -o dist/linux-x64 --nologo

echo '=== 4/4 打包 ==='
cd dist/linux-x64
chmod +x XPlane12-FlightLessons-zh-CN
cp ../../docs/使用说明.md 使用说明.md
cd ..
TARBALL=XPlane12-FlightLessons-zh-CN-linux-x64.tar.gz
rm -f "$TARBALL"
tar czf "$TARBALL" -C linux-x64 .
sha256sum "$TARBALL" > SHA256SUMS-linux-x64.txt

echo
echo "完成：dist/$TARBALL"
cat SHA256SUMS-linux-x64.txt
