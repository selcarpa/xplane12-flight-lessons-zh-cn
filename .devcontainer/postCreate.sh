#!/usr/bin/env bash
# devcontainer postCreateCommand：把仓库恢复到「能编译、能测试」的状态
set -euo pipefail
cd "$(dirname "$0")/.."

hr() { printf '\033[36m%s\033[0m\n' "──── $* ────"; }

hr "0/5 环境信息"
dotnet --info | sed -n '1,12p'
echo "DepotDownloader: $(command -v depotdownloader || echo '未安装')"

hr "1/5 还原 NuGet 依赖"
dotnet restore src/XpLessonZhCN.csproj
dotnet restore tests/SmokeTests/SmokeTests.csproj

hr "2/5 编译主项目"
dotnet build src/XpLessonZhCN.csproj -c Release --nologo

hr "3/5 冒烟测试（核心逻辑 + GUI headless）"
dotnet run --project tests/SmokeTests/SmokeTests.csproj -c Release

hr "4/5 发布 linux-x64 便携包"
# 无条件重新发布：dist/ 里可能留着上一轮的陈旧二进制，
# 而 xplane-patch-demo.sh 会拿它做验证，旧的会掩盖代码改动。
# publish 参数与产物命名统一由 scripts/build.sh 提供（唯一事实源），此处不再自带一份。
bash scripts/build.sh --rid linux-x64 --skip-tests

hr "5/5 DepotDownloader 自检"
if command -v depotdownloader >/dev/null 2>&1; then
  echo "DepotDownloader 可用：$(depotdownloader --version 2>&1 | head -1)"
else
  echo "⚠️ 未找到 depotdownloader（镜像构建时下载失败？）"
fi

# 认证状态只报「有没有」，绝不打印任何凭据内容。
# 用 --strict：缺账号名边车、权限过宽、文件过旧都算「不能无人值守下载」。
if bash scripts/steam-auth-check.sh --strict >/dev/null 2>&1; then
  echo "Steam 认证: ✔ 已就绪（可无人值守下载）"
else
  echo "✘ Steam 认证未就绪：无法无人值守下载。"
  echo "  需部署 account.config 与 account.config.username 两个文件（详见 docs/ops/steam-auth.md）。"
  echo "  部署：scripts/steam-auth-deploy.sh --install --auth-file .devcontainer/account.config"
fi

cat <<'EOF'

✔ 环境就绪。接下来可以：

  dotnet run --project tests/SmokeTests/SmokeTests.csproj   # 冒烟测试
  scripts/build.sh                                         # 构建 + 打包 linux-x64
  bash .devcontainer/download-exe.sh                        # 用已部署的认证下载可执行文件（≈79 MB（十进制），不下整包）
  bash .devcontainer/xplane-patch-demo.sh                   # 在副本上跑 --check / --dry-run

EOF
