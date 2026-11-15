#!/usr/bin/env python3
"""不依赖游戏文件的自检：验证字符串解析、可用空间计算和补丁写入逻辑。

    python selftest.py
"""
from __future__ import annotations

from common import find_region, iter_strings, read_text_at, safe_span
from apply import patch_one


def build_blob() -> bytes:
    """拼一个结构类似 exe 数据段的小缓冲区。"""
    part_titles = b"\x00\x00\x00Cirrus Intro\x00\x00\x00\x00Cirrus Landing\x00\x00\x00\x00"
    part_ui = b"oz_intro_widget_controller_text\x00\x00\x00"
    part_captions = (
        b"Hey there, it's Austin Meyer, the author of X-Plane.\x00\x00\x00\x00"
        b"Today, we're in a realy nice plane.\x00"
        b"tail\x00"
    )
    part_engine = b"Could not load sound bank at: %s FMOD Error: %d\x00"
    return b"\x00\x00\x00\x00" + part_titles + part_ui + b"\x00\x00" + part_captions + part_engine + b"\x00\x00\x00"


def check(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)
    print(f"  ok  {message}")


def main() -> int:
    blob = bytearray(build_blob())

    print("read_text_at")
    got = read_text_at(bytes(blob), blob.find(b"Cirrus Intro"))
    check(got is not None and got[0] == "Cirrus Intro", "能读出 NUL 结尾字符串")
    check(read_text_at(bytes(blob), blob.find(b"\x00\x00\x00")) is None, "跳过空串")

    print("find_region / iter_strings")
    start, end = find_region(bytes(blob))
    texts = [row["text"] for row in iter_strings(bytes(blob), start, end)]
    check(len(texts) >= 3, f"区间内解析出 {len(texts)} 条字符串")
    check(texts[0] == "Cirrus Intro", "第一条是标题")
    check(any(text.startswith("Hey there") for text in texts), "字幕锚点也在区间内")

    print("safe_span")
    offset = blob.find(b"Cirrus Intro")
    span = safe_span(bytes(blob), offset, len(b"Cirrus Intro"))
    check(span is not None and span >= len("Cirrus Intro"), f"可用空间 {span} 字节（含对齐填充）")

    print("patch_one")
    chinese = "西锐入门".encode()
    status, detail = patch_one(blob, offset, b"Cirrus Intro", chinese)
    check(status == "patched", f"写入成功（{detail}）")
    check(bytes(blob[offset : offset + len(chinese)]) == chinese, "写入后内容正确")
    check(blob[offset + len(chinese)] == 0, "结尾仍有 NUL")

    status, detail = patch_one(blob, offset, b"Cirrus Intro", b"\xe4" * 999)
    check(status in ("skipped", "too-long"), f"重复写入被拒绝（{status}）")

    print("patch_one 放不下")
    offset2 = blob.find(b"Cirrus Landing")
    status, detail = patch_one(blob, offset2, b"Cirrus Landing", b"\xe4" * 400)
    check(status == "too-long", f"超长译文被拒绝（{detail}）")

    print("\n全部通过")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
