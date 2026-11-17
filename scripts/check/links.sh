#!/usr/bin/env bash
# scripts/check/links.sh —— 体检 Markdown 内部链接
#
# 检查所有 *.md 里形如 ](path) 的相对链接指向的文件是否存在。
# 外部链接（http/https/mailto）与页内锚点（#...）跳过。
#
# 用法：scripts/check/links.sh [路径…]
#   不带参数  → 检查全仓所有 *.md
#   传目录    → 检查该目录下所有 *.md（递归）
#   传文件    → 只检查该文件
#   路径既不是目录也不是文件 → 报错退出 2
# 退出码：0 全部通过；1 有失效链接；2 用法错误
#
# 注意：不依赖 git（无 git 的环境下也能跑），只用 find 展开 *.md。
set -uo pipefail

cd "$(dirname "$0")/../.."          # 仓库根

TARGETS=()

collect_dir() {                    # 收集目录下的全部 *.md
  local dir="$1"
  while IFS= read -r f; do
    [ -n "$f" ] && TARGETS+=("$f")
  done < <(find "$dir" -name '*.md' -type f 2>/dev/null | sort)
}

if [ $# -eq 0 ]; then
  collect_dir .
else
  for arg in "$@"; do
    if [ -d "$arg" ]; then
      collect_dir "$arg"
    elif [ -f "$arg" ]; then
      TARGETS+=("$arg")
    else
      printf '[links] ✘ 参数既不是目录也不是文件：%s\n' "$arg" >&2
      exit 2
    fi
  done
fi

fail=0
checked=0

for f in "${TARGETS[@]}"; do
  [ -f "$f" ] || continue
  dir="$(dirname "$f")"
  # 取出 ](...) 形式链接的目标
  while IFS= read -r link; do
    [ -z "$link" ] && continue
    case "$link" in
      http://*|https://*|mailto:*|'#'*|'') continue ;;
    esac
    target="${link%%#*}"                 # 去掉锚点
    target="${target%% *}"                # 去掉可能的标题
    [ -z "$target" ] && continue
    checked=$((checked + 1))
    if [ ! -e "$dir/$target" ]; then
      printf '[links] %s → %s 不存在\n' "$f" "$link" >&2
      fail=1
    fi
  done < <(grep -oE '\]\([^)]+\)' "$f" 2>/dev/null | sed -E 's/^\]\(//; s/\)$//')
done

if [ "$fail" -eq 0 ]; then
  printf '[links] ✔ %d 条相对链接全部有效\n' "$checked"
  exit 0
fi
printf '[links] ✘ 存在失效链接\n' >&2
exit 1
