#!/usr/bin/env python3
"""从 compat/known-versions.json 查出某个平台/分支的原版 sha256（金标准）。

兼容清单是**唯一事实来源**：脚本不再硬编码 sha256，避免文档、清单、脚本三处漂移。

    python3 scripts/tools/golden-sha256.py --os linux   --branch public
    python3 scripts/tools/golden-sha256.py --os windows --branch previous_release
    python3 scripts/tools/golden-sha256.py --os linux   --branch public --field patched

--os 取值：linux（原生 ELF，platforms 含 linux-native）、windows（PE，platforms 含 windows）。
--branch 取值：public / previous_release（对应清单里的 steamBranch 字段）。
找不到时退出码 1（stdout 为空）。
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# 本脚本位于 scripts/tools/，仓库根是它的上上级
MANIFEST = Path(__file__).resolve().parents[2] / "compat" / "known-versions.json"

PLATFORM_TOKENS = {
    "linux": ("linux-native",),
    "windows": ("windows",),
}


def main() -> int:
    ap = argparse.ArgumentParser(description="查兼容清单里的金标准 sha256")
    ap.add_argument("--os", dest="os_name", required=True, choices=sorted(PLATFORM_TOKENS))
    ap.add_argument("--branch", required=True, help="public / previous_release")
    ap.add_argument("--field", default="sha256", help="sha256（默认）或 patchedSha256")
    ap.add_argument("--manifest", default=str(MANIFEST), help="兼容清单路径")
    args = ap.parse_args()

    tokens = PLATFORM_TOKENS[args.os_name]
    data = json.loads(Path(args.manifest).read_text(encoding="utf-8"))

    for version in data.get("versions", []):
        if version.get("steamBranch") != args.branch:
            continue
        platforms = version.get("platforms", [])
        if not any(t in platforms for t in tokens):
            continue
        value = version.get(args.field, "")
        if not value:
            continue
        print(value)
        return 0

    print(
        f"未找到匹配条目：os={args.os_name} branch={args.branch}（清单：{args.manifest}）",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
