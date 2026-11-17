# syntax=docker/dockerfile:1
#
# Codespaces / Dev Container 基础镜像
#   - devcontainers/base：通用 Linux 基础镜像（自带 vscode 用户、git、sudo 等）
#   - 额外安装 DepotDownloader（SteamRE）：用 -filelist 只取需要的可执行文件，
#     不下载完整游戏（官方 devcontainers/features 里没有 depotdownloader feature，只能手装）
#
# 构建上下文是仓库根目录（devcontainer.json 里的 "context": ".."）
FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04

# DepotDownloader 版本（截至 2026-11 最新为 3.4.0）
#   升级方法：改这里 → 删掉 Codespaces 的构建缓存 → 重建 codespace
#   版本列表：https://github.com/SteamRE/DepotDownloader/releases
ARG DEPOTDOWNLOADER_VERSION=3.4.0
ARG DEPOTDOWNLOADER_DIR=/opt/depotdownloader

# ca-certificates 用于 https 下载，curl 拉取安装包，unzip 解包，
# python3 用于校验 account.config（Deflate 解压 + Protobuf 结构，见 scripts/steam-auth-common.sh）
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates \
      curl \
      unzip \
      python3 \
 && rm -rf /var/lib/apt/lists/*

# DepotDownloader（linux-x64 发布版，自包含，不依赖镜像里的 .NET）
#   不硬编码解包后的文件名：不同版本可能是单文件，也可能是自包含目录，
#   所以解包后按「可执行文件」找，再统一软链到 /usr/local/bin/depotdownloader。
RUN mkdir -p "${DEPOTDOWNLOADER_DIR}" \
 && curl -fsSL \
      "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_${DEPOTDOWNLOADER_VERSION}/DepotDownloader-linux-x64.zip" \
      -o /tmp/depotdownloader.zip \
 && unzip -oq /tmp/depotdownloader.zip -d "${DEPOTDOWNLOADER_DIR}" \
 && rm -f /tmp/depotdownloader.zip \
 && chmod -R a+rX "${DEPOTDOWNLOADER_DIR}" \
 && BIN="$(find "${DEPOTDOWNLOADER_DIR}" -maxdepth 2 -type f -name 'DepotDownloader*' \
              ! -name '*.dll' ! -name '*.json' ! -name '*.pdb' | sed -n '1p')" \
 && if [ -z "${BIN}" ]; then echo "DepotDownloader 可执行文件未找到"; find "${DEPOTDOWNLOADER_DIR}" -maxdepth 2; exit 1; fi \
 && chmod a+x "${BIN}" \
 && ln -sf "${BIN}" /usr/local/bin/depotdownloader \
 && ln -sf "${BIN}" /usr/local/bin/DepotDownloader

ENV DEPOTDOWNLOADER_BIN=/usr/local/bin/depotdownloader \
    STEAMAPPS_DIR="/home/vscode/steamapps"

# 命名卷挂载点必须先在镜像里建好并把属主交给 vscode：
#   否则 Docker 以 root 创建挂载点，新卷归 root:root，
#   postCreate.sh 里以 vscode 身份写 NuGet 缓存会 Access denied。
# 镜像里已有该目录后，Docker 会把属主带进新卷。
RUN mkdir -p /home/vscode/.nuget/packages /home/vscode/.local/share \
  && chown -R vscode:vscode /home/vscode/.nuget /home/vscode/.local

# 运行时用非 root 用户（Steam 授权不接受 root；devcontainer.json 里也显式写了 remoteUser）
USER vscode

# 刻意不设 WORKDIR：Codespaces 的仓库挂载点是 /workspaces/<repo>-<随机串>，
# 不是固定路径，写死 WORKDIR 只会让人误判当前目录。

