#!/usr/bin/env python3
"""把译文原地写进 X-Plane.exe 的教学字符串。

只改只读数据段里的 C 字符串：写入 UTF-8 中文、尾部补 0x00，长度不超过原字符串可用空间
（原文 + 对齐填充）。不移动任何数据、不改代码段、文件大小不变。

    python apply.py "D:/Games/X-Plane 12"          # 备份后写入
    python apply.py "D:/Games/X-Plane 12" --dry-run
    python apply.py "D:/Games/X-Plane 12" --check
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from common import (
    TRANS_DIR,
    backup_dir,
    data_dir,
    find_region,
    load_translations,
    replace_exe,
    resolve_game_root,
    safe_span,
)


def patch_one(blob: bytearray, offset: int, english: bytes, chinese: bytes) -> tuple[str, str]:
    """返回 (状态, 说明)；状态为 patched / too-long / skipped。"""
    if bytes(blob[offset : offset + len(english)]) != english:
        return "skipped", "原文不匹配"
    avail = safe_span(blob, offset, len(english))
    if avail is None:
        return "skipped", "不是独立的字符串（可能是更长文本的前缀）"
    if len(chinese) > avail:
        return "too-long", f"需要 {len(chinese)} 字节，可用 {avail}"

    stop = offset + len(english)
    while stop < len(blob) and blob[stop] == 0:
        stop += 1
    blob[offset : offset + len(chinese)] = chinese
    blob[offset + len(chinese) : stop] = b"\x00" * (stop - offset - len(chinese))
    return "patched", f"{len(english)} -> {len(chinese)} 字节"


def occurrences(blob: bytes, needle: bytes, start: int, end: int) -> list[int]:
    """只在「字符串起点」处命中，避免把长子串里的同名片段也算进来。"""
    found, pos = [], start
    while (pos := blob.find(needle, pos, end)) >= 0:
        if pos == 0 or blob[pos - 1] == 0:
            found.append(pos)
        pos += 1
    return found


def cmd_check(exe: Path, pairs: list[tuple[str, str]]) -> int:
    original = exe.read_bytes()
    start, end = find_region(original)
    pending = applied = missing = 0
    for english, chinese in pairs:
        if original.find(english.encode(), start, end) >= 0:
            pending += 1
        elif original.find(chinese.encode(), start, end) >= 0:
            applied += 1
        else:
            missing += 1
            print(f"  缺失: {english[:70]}")
    print(f"词条总数 : {len(pairs)}")
    print(f"已应用   : {applied}")
    print(f"待应用   : {pending}（exe 里还是英文）")
    print(f"缺失     : {missing}")
    return 0 if missing == 0 else 2


def main() -> int:
    parser = argparse.ArgumentParser(description="给 X-Plane.exe 打教学汉化补丁")
    parser.add_argument("path", nargs="?", help="X-Plane 12 安装目录")
    parser.add_argument("--work-dir", help="自定义工作目录（默认 <游戏目录>/.zh_cn_translate）")
    parser.add_argument("--dry-run", action="store_true", help="只报告，不写盘")
    parser.add_argument("--check", action="store_true", help="只检查词条现状")
    args = parser.parse_args()

    root = resolve_game_root(args.path)
    exe = root / "X-Plane.exe"
    pairs = load_translations(TRANS_DIR)

    if args.check:
        return cmd_check(exe, pairs)

    original = exe.read_bytes()
    start, end = find_region(original)
    blob = bytearray(original)

    rows: list[str] = ["english\toffset\tstatus\tdetail"]
    counts = {"patched": 0, "too-long": 0, "skipped": 0, "not-found": 0}
    too_long: list[str] = []

    for english, chinese in pairs:
        en_b, zh_b = english.encode("utf-8"), chinese.encode("utf-8")
        if b"\t" in zh_b or b"\n" in zh_b:
            rows.append(f"{english}\t-\tskipped\t译文含制表符或换行")
            counts["skipped"] += 1
            continue
        offsets = occurrences(blob, en_b, start, end)
        if not offsets:
            rows.append(f"{english}\t-\tnot-found\t可能已打过补丁，或游戏已更新")
            counts["not-found"] += 1
            continue
        for offset in offsets:
            status, detail = patch_one(blob, offset, en_b, zh_b)
            rows.append(f"{english}\t{offset}\t{status}\t{detail}")
            counts[status] += 1
            if status == "too-long":
                too_long.append(f"{english[:60]} -> {detail}")

    print(f"译文词条 : {len(pairs)}")
    print(f"写入成功 : {counts['patched']}")
    print(f"放不下   : {counts['too-long']}")
    print(f"跳过     : {counts['skipped']}")
    print(f"未找到   : {counts['not-found']}（已打过补丁时为正常）")
    if too_long:
        print("\n这些译文需要改短：")
        for item in too_long[:20]:
            print(f"  {item}")

    report = data_dir(root, args.work_dir) / "patch_report.tsv"
    report.write_text("\n".join(rows) + "\n", encoding="utf-8", newline="\n")
    print(f"\n报告     : {report}")

    if args.dry_run:
        print("\n[dry-run] 未写盘")
        return 0 if not too_long else 1
    if counts["patched"] == 0:
        print("\n没有需要写入的内容。")
        return 0 if not too_long else 1

    backups = backup_dir(root, args.work_dir)
    digest = hashlib.sha256(original).hexdigest()[:12]
    backup = backups / f"X-Plane.exe.{digest}.bak"
    if backup.exists():
        print(f"原版备份已存在: {backup}")
    else:
        backup.write_bytes(original)
        print(f"已备份原版 exe: {backup}")

    replace_exe(exe, bytes(blob))
    print(f"已写入       : {exe}")
    print(f"新 sha256    : {hashlib.sha256(bytes(blob)).hexdigest()}")
    return 0 if not too_long else 1


if __name__ == "__main__":
    raise SystemExit(main())
