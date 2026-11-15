#!/usr/bin/env python3
"""导出 X-Plane.exe 里的教学文本清单，供翻译与核对。

    python extract.py "D:/Games/X-Plane 12"
    python extract.py --limit 20

产物（默认在 <游戏目录>/.zh_cn_translate/data/）：
    strings.tsv   offset / length / avail / text
    strings.txt   纯文本，一行一句
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from common import TRANS_DIR, data_dir, find_region, iter_strings, load_translations, resolve_game_root


def main() -> int:
    parser = argparse.ArgumentParser(description="导出飞行教学文本清单")
    parser.add_argument("path", nargs="?", help="X-Plane 12 安装目录")
    parser.add_argument("--work-dir", help="自定义工作目录（默认 <游戏目录>/.zh_cn_translate）")
    parser.add_argument("--limit", type=int, default=0, help="只打印前 N 条")
    args = parser.parse_args()

    root = resolve_game_root(args.path)
    exe = root / "X-Plane.exe"
    blob = exe.read_bytes()
    start, end = find_region(blob)

    rows = list(iter_strings(blob, start, end))
    for index, row in enumerate(rows):
        nxt = rows[index + 1]["offset"] if index + 1 < len(rows) else row["end"]
        row["avail"] = max(0, nxt - row["offset"] - 1)

    pairs = load_translations(TRANS_DIR)
    ready_en = sum(1 for en, _ in pairs if en.encode("utf-8") in blob)
    ready_zh = sum(1 for _, zh in pairs if zh.encode("utf-8") in blob)

    out_dir = data_dir(root, args.work_dir)
    tsv = out_dir / "strings.tsv"
    with tsv.open("w", encoding="utf-8", newline="\n") as fh:
        fh.write("offset\tlength\tavail\ttext\n")
        for row in rows:
            fh.write(f"{row['offset']}\t{row['length']}\t{row['avail']}\t{row['text']}\n")
    (out_dir / "strings.txt").write_text(
        "\n".join(r["text"] for r in rows) + "\n", encoding="utf-8", newline="\n"
    )

    print(f"游戏目录   : {root}")
    print(f"exe        : {exe}  ({len(blob):,} 字节, sha256 {hashlib.sha256(blob).hexdigest()[:12]})")
    print(f"文本区间   : {start} .. {end}")
    print(f"候选字符串 : {len(rows)} 条，英文共 {sum(r['length'] for r in rows):,} 字节")
    print(f"已译词条   : {len(pairs)} 条（英文原文命中 {ready_en}，已汉化 {ready_zh}）")
    print(f"输出       : {tsv}")
    if args.limit:
        print("\n前几条:")
        for row in rows[: args.limit]:
            print(f"  off={row['offset']} len={row['length']}  {row['text'][:70]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
