#!/usr/bin/env python3
"""校验打完补丁的 X-Plane.exe。

检查：
1. 每条译文都能在 exe 里找到；
2. 文件大小、PE 头部、文件尾与原版一致；
3. 相对原版改动的每个字节都落在补丁声明过的字符串范围内。

    python verify.py "D:/Games/X-Plane 12"
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from common import TRANS_DIR, backup_dir, load_translations, resolve_game_root

CHUNK = 1 << 16


def changed_ranges(old: bytes, new: bytes) -> list[tuple[int, int]]:
    ranges: list[tuple[int, int]] = []
    for base in range(0, min(len(old), len(new)), CHUNK):
        a, b = old[base : base + CHUNK], new[base : base + CHUNK]
        if a == b:
            continue
        start = None
        for i in range(len(a)):
            if a[i] != b[i]:
                if start is None:
                    start = base + i
            elif start is not None:
                ranges.append((start, base + i))
                start = None
        if start is not None:
            ranges.append((start, base + len(a)))
    return ranges


def spans_from_translations(blob: bytes, pairs: list[tuple[str, str]]) -> list[tuple[int, int]]:
    """按译文反推被改写过的字节区间（写入的中文 + 后面被清零的填充）。"""
    spans: list[tuple[int, int]] = []
    for _english, chinese in pairs:
        needle = chinese.encode("utf-8")
        pos = 0
        while (pos := blob.find(needle, pos)) >= 0:
            if pos == 0 or blob[pos - 1] == 0:
                stop = pos + len(needle)
                while stop < len(blob) and blob[stop] == 0:
                    stop += 1
                spans.append((pos, stop))
            pos += 1
    return spans


def main() -> int:
    parser = argparse.ArgumentParser(description="校验汉化补丁")
    parser.add_argument("path", nargs="?", help="X-Plane 12 安装目录")
    parser.add_argument("--work-dir", help="自定义工作目录")
    parser.add_argument("--backup", type=Path, help="与原版对比用的备份（默认取最新）")
    args = parser.parse_args()

    root = resolve_game_root(args.path)
    exe = root / "X-Plane.exe"
    blob = exe.read_bytes()

    backups = sorted(backup_dir(root, args.work_dir).glob("X-Plane.exe.*.bak"), key=lambda p: p.stat().st_mtime)
    backup = args.backup or (backups[-1] if backups else None)

    problems: list[str] = []
    hits = 0
    for english, chinese in load_translations(TRANS_DIR):
        if chinese.encode("utf-8") in blob:
            hits += 1
        else:
            problems.append(f"译文不在 exe 里: {english[:60]}")

    print(f"exe        : {exe}")
    print(f"大小       : {len(blob):,} 字节")
    print(f"sha256     : {hashlib.sha256(blob).hexdigest()}")
    print(f"译文命中   : {hits} 条")

    if not backup:
        print("没有备份，跳过与原版的对比（首次 apply 时会自动生成）。")
        return 0 if not problems else 1

    old = backup.read_bytes()
    print(f"对比备份   : {backup.name}（{len(old):,} 字节）")
    if len(old) != len(blob):
        problems.append("文件大小与原版不同")
    if old[:0x400] != blob[:0x400]:
        problems.append("PE 头部被改动")
    if old[-0x1000:] != blob[-0x1000:]:
        problems.append("文件尾被改动")

    diffs = changed_ranges(old, blob) if len(old) == len(blob) else []
    spans = spans_from_translations(blob, load_translations(TRANS_DIR))
    outside = [d for d in diffs if not any(s <= d[0] and d[1] <= e for s, e in spans)]
    changed = sum(b - a for a, b in diffs)
    print(f"改动区间   : {len(diffs)} 段，共 {changed:,} 字节（占 {changed / len(blob):.4%}）")
    print(f"补丁范围   : {len(spans)} 条字符串")
    if outside:
        problems.append(f"{len(outside)} 段改动落在补丁范围之外: {outside[:3]}")

    if problems:
        print("\n校验失败:")
        for item in problems[:10]:
            print(f"  - {item}")
        return 1
    print("\n校验通过：改动全部落在教学字符串范围内，文件结构未变。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
