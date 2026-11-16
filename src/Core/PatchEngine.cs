using System;
using System.Collections.Generic;
using System.Text;

namespace XpLessonZhCn.Core;

public enum PatchStatus
{
    Patched,
    TooLong,
    Skipped,
    NotFound,
}

public sealed record PatchRow(string English, int Offset, PatchStatus Status, string Detail);

/// <summary>一次成功写入改动的字节区间 [Start, Stop)。</summary>
public sealed record PatchSpan(int Start, int Stop);

/// <summary>
/// 宽松匹配命中：英文串不在「字符串起点」（前一字节不是 NUL），但确定是独立字幕。
/// Linux 构建里个别字幕串前面直接跟着浮点/结构体数据（没有 NUL 分隔）或 1~4 字节残字；
/// 这些命中由 <see cref="PatchEngine.LenientOccurrences"/> 严格筛选，从英文原文处写入。
/// </summary>
public sealed record LenientMatch(int Offset, int PrefixLength)
{
    /// <summary>写进补丁报告的说明。</summary>
    public string Note => $"跳过句首 {PrefixLength} 字节垃圾前缀";
}

public sealed class EngineResult
{
    public List<PatchRow> Rows { get; } = new();
    public List<PatchSpan> Spans { get; } = new();
    public int Patched { get; set; }
    public int TooLong { get; set; }
    public int Skipped { get; set; }
    public int NotFound { get; set; }

    public bool HasProblems => TooLong > 0;
}

/// <summary>
/// 字节级补丁引擎。只改字符串内容，长度只减不增（不足用 0x00 补齐），
/// 不移动数据、不改代码段、不改文件大小。
/// </summary>
public static class PatchEngine
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>只在「字符串起点」处命中，避免把长字符串里的同名片段算进来。</summary>
    public static List<int> Occurrences(byte[] blob, byte[] needle, int start, int end)
    {
        var found = new List<int>();
        int pos = start;
        while (true)
        {
            int hit = LessonImage.IndexOf(blob, needle, pos, end);
            if (hit < 0) break;

            if (hit == 0 || blob[hit - 1] == 0) found.Add(hit);
            pos = hit + 1;
        }
        return found;
    }

    /// <summary>
    /// 宽松匹配：找出「不在字符串起点、但确定是独立字幕」的命中。只接受同时满足下列条件的命中，
    /// 避免误改长句片段：
    /// <list type="bullet">
    /// <item>英文原文正好结束在 NUL 上（即它确实是该 C 字符串的结尾）；</item>
    /// <item>命中不在字符串起点（前一字节不是 NUL）；</item>
    /// <item>从上一个 NUL 到英文起点之间的前缀是：二进制数据（含控制字节/非法 UTF-8），
    /// 或不超过 4 字节且不以空格结尾的残字（如 <c>" A"</c>、<c>"ЄE"</c>）。</item>
    /// </list>
    /// 这些情况下游戏显示的字符串从英文处开始（前缀是数据尾巴），从英文原文处写入、保留前缀字节不动；
    /// 文本前缀（如 <c>"That "</c>、<c>"... and then: Brakes off!"</c>）一律忽略，避免误改长句片段。
    /// </summary>
    public static List<LenientMatch> LenientOccurrences(byte[] blob, byte[] english, int start, int end)
    {
        var result = new List<LenientMatch>();
        int pos = start;
        while (true)
        {
            int hit = LessonImage.IndexOf(blob, english, pos, end);
            if (hit < 0) break;
            pos = hit + 1;

            int englishEnd = hit + english.Length;
            if (englishEnd >= blob.Length || blob[englishEnd] != 0) continue;
            if (hit > 0 && blob[hit - 1] == 0) continue;

            int stringStart = LastNullBefore(blob, start, hit) + 1;
            int prefixLength = hit - stringStart;
            if (prefixLength <= 0) continue;

            ReadOnlySpan<byte> prefix = new(blob, stringStart, prefixLength);
            if (!LooksLikeText(prefix))
            {
                // 前缀是二进制数据 → 字幕实际从英文起点开始
                result.Add(new LenientMatch(hit, prefixLength));
                continue;
            }
            if (prefixLength <= 4 && prefix[^1] != (byte)' ')
            {
                // 1~4 字节的残字（" A"、"ЄE" 等）→ 同上，从英文起点写
                result.Add(new LenientMatch(hit, prefixLength));
            }
            // 其余（文本前缀，如 "That "、"...and then: Brakes off!"）都当作更长正文的片段 → 忽略
        }
        return result;
    }

    /// <summary>
    /// 写入单条译文；返回状态、说明，以及被改写区间的结束位置（Stop，仅 Patched 时有效）。
    /// Stop 在写入前就算好，避免「中文比英文长」时把区间算短。
    /// </summary>
    public static (PatchStatus Status, string Detail, int Stop) PatchOne(
        byte[] blob, int offset, byte[] english, byte[] chinese)
    {
        return PatchOneAt(blob, offset, english, chinese, note: null);
    }

    /// <summary>同 <see cref="PatchOne"/>，可附加报告说明（用于句首垃圾前缀兜底命中）。</summary>
    public static (PatchStatus Status, string Detail, int Stop) PatchOneAt(
        byte[] blob, int offset, byte[] english, byte[] chinese, string? note)
    {
        if (offset < 0 || offset + english.Length > blob.Length)
        {
            return (PatchStatus.Skipped, "位置越界", 0);
        }

        for (int i = 0; i < english.Length; i++)
        {
            if (blob[offset + i] != english[i]) return (PatchStatus.Skipped, "原文不匹配", 0);
        }

        var span = LessonImage.SafeSpanOf(blob, offset, english.Length);
        if (span is null) return (PatchStatus.Skipped, "不是独立的字符串（可能是更长文本的前缀）", 0);

        int avail = span.Value;
        if (chinese.Length > avail)
        {
            return (PatchStatus.TooLong, $"需要 {chinese.Length} 字节，可用 {avail}", 0);
        }

        int originalEnd = offset + english.Length;
        int stop = originalEnd;
        while (stop < blob.Length && blob[stop] == 0) stop++;

        Buffer.BlockCopy(chinese, 0, blob, offset, chinese.Length);

        int zeroFrom = offset + chinese.Length;
        if (zeroFrom < stop)
        {
            Array.Clear(blob, zeroFrom, stop - zeroFrom);
        }

        string detail = note is null
            ? $"{english.Length} -> {chinese.Length} 字节"
            : $"{english.Length} -> {chinese.Length} 字节（{note}）";
        return (PatchStatus.Patched, detail, stop);
    }

    /// <summary>按译文表顺序打补丁；blob 原地修改，返回逐条结果与改动区间。
    /// Windows 与 Linux 原生版使用同一条译文；放不下时按 too-long 拒绝（不写盘），绝不溢出。</summary>
    public static EngineResult Apply(byte[] blob, LessonImage image, IReadOnlyList<Translation> translations)
    {
        var result = new EngineResult();

        foreach (var pair in translations)
        {
            byte[] english = System.Text.Encoding.UTF8.GetBytes(pair.English);
            byte[] chinese = System.Text.Encoding.UTF8.GetBytes(pair.Chinese);

            if (Array.IndexOf(chinese, (byte)'\t') >= 0)
            {
                result.Rows.Add(new PatchRow(pair.English, -1, PatchStatus.Skipped, "译文含制表符"));
                result.Skipped++;
                continue;
            }

            // 先找「字符串起点」命中，再找句首带垃圾前缀的兜底命中（两边都可能有：
            // 例如同一句既出现在课程卡片长串开头，也有一份独立拷贝跟在数据后面）。
            List<int> offsets = Occurrences(blob, english, image.RegionStart, image.RegionEnd);
            List<LenientMatch> lenient = LenientOccurrences(blob, english, image.RegionStart, image.RegionEnd);

            if (offsets.Count == 0 && lenient.Count == 0)
            {
                result.Rows.Add(new PatchRow(pair.English, -1, PatchStatus.NotFound, "可能已打过补丁，或游戏已更新"));
                result.NotFound++;
                continue;
            }

            foreach (int offset in offsets)
            {
                var (status, detail, stop) = PatchOne(blob, offset, english, chinese);
                result.Rows.Add(new PatchRow(pair.English, offset, status, detail));

                switch (status)
                {
                    case PatchStatus.Patched:
                        result.Patched++;
                        result.Spans.Add(new PatchSpan(offset, stop));
                        break;
                    case PatchStatus.TooLong:
                        result.TooLong++;
                        break;
                    default:
                        result.Skipped++;
                        break;
                }
            }

            foreach (var match in lenient)
            {
                var (status, detail, stop) = PatchOneAt(blob, match.Offset, english, chinese, match.Note);
                result.Rows.Add(new PatchRow(pair.English, match.Offset, status, detail));

                switch (status)
                {
                    case PatchStatus.Patched:
                        result.Patched++;
                        result.Spans.Add(new PatchSpan(match.Offset, stop));
                        break;
                    case PatchStatus.TooLong:
                        result.TooLong++;
                        break;
                    default:
                        result.Skipped++;
                        break;
                }
            }
        }

        return result;
    }

    /// <summary>返回 beforeIndex 之前、from 之后最后一个 0 的下标；没有则返回 from-1。</summary>
    private static int LastNullBefore(byte[] blob, int from, int beforeIndex)
    {
        if (beforeIndex <= from) return from - 1;
        return Array.LastIndexOf(blob, (byte)0, beforeIndex - 1, beforeIndex - from);
    }

    /// <summary>按 LessonImage.ReadTextAt 的同一套规则判断一段字节是否像 UTF-8 文本。</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (b < 0x20 || b == 0x7F) return false;
        }
        try
        {
            _ = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
