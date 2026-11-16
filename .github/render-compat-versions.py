#!/usr/bin/env python3
"""根据 compat/known-versions.json 生成 Release 说明里的「兼容版本」表格。

CI 的 release job 调用它并把输出追加到 Release 说明末尾；本地可以这样预览：

    python3 .github/render-compat-versions.py

新增兼容版本只需要改 compat/known-versions.json，Release 说明会自动跟着更新。
"""

from __future__ import annotations

import json
from pathlib import Path

MANIFEST = Path(__file__).resolve().parent.parent / "compat" / "known-versions.json"

PLATFORM_LABELS = {
    "windows": "Windows PE",
    "linux-wine": "Wine/Proton",
    "linux-native": "Linux 原生版（ELF）",
}


def platform_text(version: dict) -> str:
    platforms = version.get("platforms") or []
    names = [PLATFORM_LABELS.get(p, p) for p in platforms]
    # Windows PE 与 Wine/Proton 是同一个可执行文件，合成一条更清楚
    if "windows" in platforms and "linux-wine" in platforms:
        names = [n for n in names if n not in ("Windows PE", "Wine/Proton")]
        names.insert(0, "Windows PE（Wine/Proton 同款）")
    return " / ".join(names) or "—"


def main() -> int:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    versions = [
        v for v in manifest.get("versions", [])
        if str(v.get("status", "")).lower() == "verified"
    ]
    if not versions:
        print("（清单里还没有 verified 版本）")
        return 0

    print("| X-Plane 版本 | 平台 | 原版 sha256（前 12 位） | 补丁后 sha256（前 12 位） | 验证日期 |")
    print("|---|---|---|---|---|")
    for v in versions:
        print(
            "| {name} | {platforms} | `{sha}` | `{patched}` | {date} |".format(
                name=v.get("displayName") or v.get("id", "?"),
                platforms=platform_text(v),
                sha=(v.get("sha256") or "")[:12],
                patched=(v.get("patchedSha256") or "")[:12],
                date=v.get("verifiedOn") or "—",
            )
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
