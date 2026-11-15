#!/usr/bin/env python3
"""用备份还原原版 X-Plane.exe。

    python restore.py "D:/Games/X-Plane 12" --list
    python restore.py "D:/Games/X-Plane 12"
    python restore.py "D:/Games/X-Plane 12" --backup X-Plane.exe.2ca099b7c6b2.bak
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from common import backup_dir, replace_exe, resolve_game_root


def main() -> int:
    parser = argparse.ArgumentParser(description="还原 X-Plane.exe")
    parser.add_argument("path", nargs="?", help="X-Plane 12 安装目录")
    parser.add_argument("--work-dir", help="自定义工作目录")
    parser.add_argument("--backup", type=Path, help="指定备份文件")
    parser.add_argument("--list", action="store_true", help="只列出备份")
    args = parser.parse_args()

    root = resolve_game_root(args.path)
    backups = sorted(backup_dir(root, args.work_dir).glob("X-Plane.exe.*.bak"), key=lambda p: p.stat().st_mtime)

    if args.list or not backups:
        print(f"备份目录: {backup_dir(root, args.work_dir)}")
        for item in backups:
            print(f"  {item.name}  {item.stat().st_size:,} 字节")
        if not backups:
            print("  （空）——首次 apply 时会自动生成，或用 Steam 验证文件完整性")
        return 0

    exe = root / "X-Plane.exe"
    backup = args.backup or backups[-1]
    if not backup.is_absolute():
        backup = backup_dir(root, args.work_dir) / backup
    if not backup.is_file():
        raise SystemExit(f"备份不存在: {backup}")

    replace_exe(exe, backup.read_bytes())
    print(f"已从 {backup.name} 还原 {exe}")
    print(f"sha256: {hashlib.sha256(exe.read_bytes()).hexdigest()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
