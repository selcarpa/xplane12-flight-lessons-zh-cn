using System;
using System.Collections.Generic;

namespace XpLessonZhCn.Core;

public sealed class VerifyReport
{
    public bool SizeSame { get; set; }
    public bool PeHeaderSame { get; set; }
    public bool TailSame { get; set; }
    public long ChangedBytes { get; set; }
    public int ChangedRanges { get; set; }
    public List<PatchSpan> OutsideSpans { get; } = new();

    public bool Ok => SizeSame && PeHeaderSame && TailSame && OutsideSpans.Count == 0;

    public string Summary()
    {
        var parts = new List<string>
        {
            SizeSame ? "大小不变" : "大小变化",
            PeHeaderSame ? "PE 头未改" : "PE 头被改动",
            TailSame ? "文件尾未改" : "文件尾被改动",
            $"{ChangedRanges} 段改动 / {ChangedBytes:N0} 字节",
        };
        if (OutsideSpans.Count > 0) parts.Add($"{OutsideSpans.Count} 段越界");
        return string.Join("，", parts);
    }
}

/// <summary>校验补丁没有破坏文件结构，且所有改动都落在声明的字符串范围内。</summary>
public static class Verifier
{
    private const int HeaderBytes = 0x400;
    private const int TailBytes = 0x1000;
    private const int Chunk = 1 << 16;

    public static VerifyReport Compare(byte[] original, byte[] patched, IReadOnlyList<PatchSpan> spans)
    {
        var report = new VerifyReport
        {
            SizeSame = original.Length == patched.Length,
        };

        if (!report.SizeSame)
        {
            report.PeHeaderSame = false;
            report.TailSame = false;
            return report;
        }

        int headerLen = Math.Min(HeaderBytes, original.Length);
        report.PeHeaderSame = AsSpan(original, 0, headerLen).SequenceEqual(AsSpan(patched, 0, headerLen));

        int tailLen = Math.Min(TailBytes, original.Length);
        report.TailSame = AsSpan(original, original.Length - tailLen, tailLen)
            .SequenceEqual(AsSpan(patched, patched.Length - tailLen, tailLen));

        var diffs = ChangedRanges(original, patched);
        report.ChangedRanges = diffs.Count;

        long total = 0;
        foreach (var (start, stop) in diffs)
        {
            total += stop - start;
            bool inside = false;
            foreach (var span in spans)
            {
                if (span.Start <= start && stop <= span.Stop)
                {
                    inside = true;
                    break;
                }
            }
            if (!inside) report.OutsideSpans.Add(new PatchSpan(start, stop));
        }
        report.ChangedBytes = total;

        return report;
    }

    public static List<PatchSpan> ChangedRanges(byte[] a, byte[] b)
    {
        var ranges = new List<PatchSpan>();
        int length = Math.Min(a.Length, b.Length);

        for (int basePos = 0; basePos < length; basePos += Chunk)
        {
            int chunkLen = Math.Min(Chunk, length - basePos);
            if (AsSpan(a, basePos, chunkLen).SequenceEqual(AsSpan(b, basePos, chunkLen))) continue;

            int rangeStart = -1;
            for (int i = 0; i < chunkLen; i++)
            {
                bool differ = a[basePos + i] != b[basePos + i];
                if (differ && rangeStart < 0)
                {
                    rangeStart = basePos + i;
                }
                else if (!differ && rangeStart >= 0)
                {
                    ranges.Add(new PatchSpan(rangeStart, basePos + i));
                    rangeStart = -1;
                }
            }
            if (rangeStart >= 0) ranges.Add(new PatchSpan(rangeStart, basePos + chunkLen));
        }

        return ranges;
    }

    private static ReadOnlySpan<byte> AsSpan(byte[] blob, int start, int length) => new(blob, start, length);
}
