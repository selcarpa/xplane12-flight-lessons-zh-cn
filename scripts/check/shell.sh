#!/usr/bin/env bash
# scripts/check/shell.sh —— 体检所有 shell 脚本的语法
#
# 用法：scripts/check/shell.sh
# 退出码：0 全部通过；1 有脚本语法错误
set -uo pipefail

cd "$(dirname "$0")/../.."          # 仓库根

fail=0
count=0

# 用 mktemp 而不是固定路径：/tmp 不可写时不至于把全部脚本误判为语法错误，
# 并发运行时也不会互相覆盖 stderr 缓冲。
tmp="$(mktemp "${TMPDIR:-/tmp}/shell-syntax.XXXXXX")" || {
  printf '[shell] ✘ 无法创建临时文件（mktemp 失败）\n' >&2
  exit 1
}
trap 'rm -f "$tmp"' EXIT

while IFS= read -r f; do
  count=$((count + 1))
  if ! bash -n "$f" 2>"$tmp"; then
    printf '[shell] ✘ %s\n' "$f" >&2
    sed 's/^/         /' "$tmp" >&2
    fail=1
  fi
done < <(find scripts .devcontainer -name '*.sh' -type f 2>/dev/null | sort)

# 构建脚本残留检查：批次 1 之后 build.sh / build.ps1 都应位于 scripts/ 下
for leftover in build.sh build.ps1; do
  if [ -f "$leftover" ]; then
    printf '[shell] ✘ 构建脚本残留：仓库根仍有 %s（应位于 scripts/）\n' "$leftover" >&2
    fail=1
  fi
done

if [ "$fail" -eq 0 ]; then
  printf '[shell] ✔ %d 个脚本语法全部通过\n' "$count"
  exit 0
fi
printf '[shell] 存在语法错误的脚本\n' >&2
exit 1
