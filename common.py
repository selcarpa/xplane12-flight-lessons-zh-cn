"""X-Plane 12「飞行教学」汉化工具的公共部分。

职责：
1. 定位 X-Plane 12 安装目录（支持命令行传入、环境变量、当前目录向上查找）；
2. 找到教学文本所在的只读数据区（课程标题 + 口播字幕）；
3. 读写这些 C 字符串，并安全地替换 exe。

教学文本为什么在 exe 里、为什么翻译表无效，见 README.md / AGENT.md。
"""
from __future__ import annotations

import os
from pathlib import Path

PROJECT_DIR = Path(__file__).resolve().parent
TRANS_DIR = PROJECT_DIR / "translations"

# 工作目录：放抽取结果、补丁报告和原版 exe 备份，默认在游戏目录下的 .zh_cn_translate
WORK_DIR_NAME = ".zh_cn_translate"

# 区间锚点用「不会被翻译的字符串」，打补丁前后都能定位：
#   START_ANCHOR：标题块之后第一个 UI 标识符
#   END_ANCHOR  ：课程文本结束后、引擎自己的错误串
START_ANCHOR = b"oz_intro_widget_controller_text"
END_ANCHOR = b"Could not load sound bank at:"
# 旧版锚点仅在找不到 START_ANCHOR 时兜底
TITLE_ANCHOR = b"Cirrus Intro"
CAPTION_ANCHOR = b"Hey there, it's Austin Meyer, the author of X-Plane."

SCAN_BACK = 4096
SCAN_FORWARD = 200_000
MIN_STRING_LEN = 2


def _looks_like_game(path: Path) -> bool:
    return (path / "X-Plane.exe").is_file()


def _search_upward(start: Path) -> Path | None:
    for candidate in [start, *start.parents]:
        if _looks_like_game(candidate):
            return candidate
    return None


def resolve_game_root(value: str | Path | None = None) -> Path:
    """定位游戏目录。

    顺序：命令行参数 > 环境变量 XP12_ROOT > 当前目录向上查找 > 脚本所在目录向上查找。
    """
    if value:
        root = Path(value).expanduser().resolve()
        if not _looks_like_game(root):
            raise SystemExit(f"{root} 下没有 X-Plane.exe，请检查路径。")
        return root

    env = os.environ.get("XP12_ROOT")
    if env:
        root = Path(env).expanduser().resolve()
        if not _looks_like_game(root):
            raise SystemExit(f"环境变量 XP12_ROOT={root} 下没有 X-Plane.exe。")
        return root

    found = _search_upward(Path.cwd()) or _search_upward(PROJECT_DIR)
    if found:
        return found

    raise SystemExit(
        "没找到 X-Plane 12 安装目录。\n"
        "用法: python apply.py \"D:/Games/X-Plane 12\"\n"
        "或者设置环境变量 XP12_ROOT。"
    )


def work_dir(root: Path, override: str | Path | None = None) -> Path:
    """工作目录：默认 <游戏目录>/.zh_cn_translate。"""
    path = Path(override).expanduser().resolve() if override else root / WORK_DIR_NAME
    path.mkdir(parents=True, exist_ok=True)
    return path


def data_dir(root: Path, override: str | Path | None = None) -> Path:
    path = work_dir(root, override) / "data"
    path.mkdir(parents=True, exist_ok=True)
    return path


def backup_dir(root: Path, override: str | Path | None = None) -> Path:
    path = work_dir(root, override) / "backup"
    path.mkdir(parents=True, exist_ok=True)
    return path


def read_text_at(blob: bytes, pos: int) -> tuple[str, int] | None:
    """读取 pos 处的 NUL 结尾 UTF-8 字符串，返回 (文本, 结束位置)；不像文本时返回 None。"""
    end = blob.find(b"\x00", pos)
    if end < 0:
        return None
    raw = blob[pos:end]
    if len(raw) < MIN_STRING_LEN or any(b < 0x20 or b == 0x7F for b in raw):
        return None
    try:
        return raw.decode("utf-8"), end + 1
    except UnicodeDecodeError:
        return None


def find_region(blob: bytes) -> tuple[int, int]:
    """返回教学文本所在区间 [start, end)。"""
    start_hit = blob.find(START_ANCHOR)
    if start_hit < 0:
        start_hit = blob.find(TITLE_ANCHOR)
        if start_hit < 0 and blob.find(CAPTION_ANCHOR) < 0:
            raise SystemExit(
                "找不到教学文本锚点，游戏可能已更新，需要更新 common.py 的 START_ANCHOR / END_ANCHOR。"
            )
    start = max(0, start_hit - SCAN_BACK)

    end_hit = blob.find(END_ANCHOR, start_hit)
    end = end_hit if end_hit > 0 else min(len(blob), start_hit + SCAN_FORWARD)
    return start, end


def iter_strings(blob: bytes, start: int, end: int):
    """顺序产出区间内所有 C 字符串：{offset, end, length, text}。"""
    pos = start
    while pos < end:
        got = read_text_at(blob, pos)
        if got is None:
            pos += 1
            continue
        text, after = got
        yield {"offset": pos, "end": after, "length": len(text.encode("utf-8")), "text": text}
        pos = after


def safe_span(blob: bytes, offset: int, length: int) -> int | None:
    """返回该字符串可用的内容字节数（不含结尾 NUL）；不是独立 C 字符串时返回 None。"""
    end = offset + length
    if end >= len(blob) or blob[end] != 0:
        return None
    pos = end
    while pos < len(blob) and blob[pos] == 0:
        pos += 1
    return pos - offset - 1


def load_translations(folder: Path = TRANS_DIR) -> list[tuple[str, str]]:
    """读取 translations/*.tsv（英文<TAB>中文），忽略空行与 # 注释。"""
    pairs: list[tuple[str, str]] = []
    seen: set[str] = set()
    for path in sorted(folder.glob("*.tsv")):
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            parts = line.split("\t")
            if len(parts) < 2 or not parts[0].strip() or not parts[1].strip():
                print(f"警告: {path.name}:{lineno} 格式不对，已跳过")
                continue
            english, chinese = parts[0], parts[1]
            if english in seen:
                print(f"警告: {path.name}:{lineno} 重复词条，已跳过: {english[:60]}")
                continue
            seen.add(english)
            pairs.append((english, chinese))
    return pairs


def replace_exe(target: Path, data: bytes) -> None:
    """原子替换 exe，并且不让新文件继承目录的完整性标签。

    如果目录带「低完整性级别」标签（开发沙箱常见），直接在目录里新建文件会让新 exe 也带
    该标签，游戏随后以低权限运行、写不了存档。所以先写到上一级目录，再用 os.replace 换回来。
    上一级不可写时退回系统临时目录（同卷才保证重命名可用）。
    """
    import tempfile

    candidates = [target.parent.parent / (target.name + ".stage")]
    try:
        candidates.append(Path(tempfile.gettempdir()) / (target.name + ".stage"))
    except Exception:
        pass

    last_error: Exception | None = None
    for stage in candidates:
        try:
            stage.write_bytes(data)
            try:
                os.replace(stage, target)
            except OSError:
                stage.unlink(missing_ok=True)
                raise
            return
        except OSError as exc:  # 换下一个候选位置
            last_error = exc
    raise SystemExit(f"写入 {target} 失败: {last_error}")
