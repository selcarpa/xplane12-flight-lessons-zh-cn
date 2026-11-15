using System;
using System.Collections.Generic;

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
    /// <summary>只在「字符串起点」处命中，避免把长字符串里的同名片段算进来。</summary>
    public static List<int> Occurrences(byte[] blob, byte[] needle, int start, int end)
    {
        var found = new List<int>();
        int pos = start;
        while (true)
        {
            int hit = PeImage.IndexOf(blob, needle, pos, end);
            if (hit < 0) break;

            if (hit == 0 || blob[hit - 1] == 0) found.Add(hit);
            pos = hit + 1;
        }
        return found;
    }

    /// <summary>
    /// 写入单条译文；返回状态、说明，以及被改写区间的结束位置（Stop，仅 Patched 时有效）。
    /// Stop 在写入前就算好，避免「中文比英文长」时把区间算短。
    /// </summary>
    public static (PatchStatus Status, string Detail, int Stop) PatchOne(
        byte[] blob, int offset, byte[] english, byte[] chinese)
    {
        if (offset < 0 || offset + english.Length > blob.Length)
        {
            return (PatchStatus.Skipped, "位置越界", 0);
        }

        for (int i = 0; i < english.Length; i++)
        {
            if (blob[offset + i] != english[i]) return (PatchStatus.Skipped, "原文不匹配", 0);
        }

        var span = PeImage.SafeSpanOf(blob, offset, english.Length);
        if (span is null) return (PatchStatus.Skipped, "不是独立的字符串（可能是更长文本的前缀）", 0);

        int avail = span.Value;
        if (chinese.Length > avail)
        {
            return (PatchStatus.TooLong, $"需要 {chinese.Length} 字节，可用 {avail}", 0);
        }

        int stop = offset + english.Length;
        while (stop < blob.Length && blob[stop] == 0) stop++;

        Buffer.BlockCopy(chinese, 0, blob, offset, chinese.Length);

        int zeroFrom = offset + chinese.Length;
        if (zeroFrom < stop)
        {
            Array.Clear(blob, zeroFrom, stop - zeroFrom);
        }

        return (PatchStatus.Patched, $"{english.Length} -> {chinese.Length} 字节", stop);
    }

    /// <summary>按译文表顺序打补丁；blob 原地修改，返回逐条结果与改动区间。</summary>
    public static EngineResult Apply(byte[] blob, PeImage image, IReadOnlyList<Translation> translations)
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

            List<int> offsets = Occurrences(blob, english, image.RegionStart, image.RegionEnd);
            if (offsets.Count == 0)
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
        }

        return result;
    }
}
