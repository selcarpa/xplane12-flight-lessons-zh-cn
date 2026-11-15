using System;
using System.Collections.Generic;
using System.Text;

namespace XpLessonZhCn.Core;

/// <summary>定位/解析失败，消息直接面向用户。</summary>
public sealed class PatchException : Exception
{
    public PatchException(string message) : base(message) { }
}

public sealed class StringEntry
{
    public int Offset { get; init; }
    public int End { get; init; }
    public int ByteLength { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>
/// 在 PE 里定位「飞行教学」文本区间，并读写其中的 C 字符串。
/// 规则与 Python 版 common.py / apply.py 逐条对齐（见 AGENT.md）。
/// </summary>
public sealed class PeImage
{
    public const int ScanBack = 4096;
    public const int ScanForward = 200_000;
    public const int MinStringLength = 2;

    public static readonly byte[] StartAnchor = Encoding.UTF8.GetBytes("oz_intro_widget_controller_text");
    public static readonly byte[] EndAnchor = Encoding.UTF8.GetBytes("Could not load sound bank at:");
    public static readonly byte[] TitleAnchor = Encoding.UTF8.GetBytes("Cirrus Intro");
    public static readonly byte[] CaptionAnchor = Encoding.UTF8.GetBytes("Hey there, it's Austin Meyer, the author of X-Plane.");

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public PeImage(byte[] blob) => Blob = blob;

    public byte[] Blob { get; }
    public int RegionStart { get; private set; }
    public int RegionEnd { get; private set; }

    /// <summary>在 [from, to) 内查找子串，返回绝对下标；找不到返回 -1。</summary>
    public static int IndexOf(byte[] haystack, byte[] needle, int from, int to)
    {
        if (needle.Length == 0 || haystack.Length == 0) return -1;
        int end = Math.Min(to, haystack.Length);
        if (from < 0 || from >= end) return -1;
        int len = end - from;
        if (len < needle.Length) return -1;

        int rel = new ReadOnlySpan<byte>(haystack, from, len).IndexOf(new ReadOnlySpan<byte>(needle));
        return rel < 0 ? -1 : from + rel;
    }

    /// <summary>定位教学文本区间；失败抛 <see cref="PatchException"/>。</summary>
    public void LocateRegion()
    {
        int startHit = IndexOf(Blob, StartAnchor, 0, Blob.Length);
        if (startHit < 0)
        {
            startHit = IndexOf(Blob, TitleAnchor, 0, Blob.Length);
            if (startHit < 0)
            {
                int captionHit = IndexOf(Blob, CaptionAnchor, 0, Blob.Length);
                if (captionHit < 0)
                {
                    throw new PatchException(
                        "找不到教学文本锚点：这个 exe 可能不是受支持的 X-Plane 12 版本，或已被修改过。");
                }
                startHit = captionHit;
            }
        }

        RegionStart = Math.Max(0, startHit - ScanBack);
        int endHit = IndexOf(Blob, EndAnchor, startHit, Blob.Length);
        RegionEnd = endHit > 0 ? endHit : Math.Min(Blob.Length, startHit + ScanForward);

        if (RegionEnd <= RegionStart)
        {
            throw new PatchException("教学文本区间无效（起始 >= 结束），拒绝继续。");
        }
    }

    /// <summary>读取 pos 处的 NUL 结尾 UTF-8 字符串；不像文本时返回 null。</summary>
    public (string Text, int After)? ReadTextAt(int pos)
    {
        if (pos < 0 || pos >= Blob.Length) return null;

        int end = Array.IndexOf(Blob, (byte)0, pos);
        if (end < 0) return null;

        int len = end - pos;
        if (len < MinStringLength) return null;

        for (int i = pos; i < end; i++)
        {
            byte b = Blob[i];
            if (b < 0x20 || b == 0x7F) return null;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(Blob, pos, len);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return (text, end + 1);
    }

    /// <summary>顺序枚举区间内所有 C 字符串。</summary>
    public IEnumerable<StringEntry> EnumerateStrings()
    {
        int pos = RegionStart;
        while (pos < RegionEnd)
        {
            var got = ReadTextAt(pos);
            if (got is null)
            {
                pos++;
                continue;
            }

            var (text, after) = got.Value;
            yield return new StringEntry
            {
                Offset = pos,
                End = after,
                ByteLength = after - 1 - pos,
                Text = text,
            };
            pos = after;
        }
    }

    /// <summary>
    /// 该字符串可用的内容字节数（不含结尾 NUL，含其后对齐填充）；
    /// 不是独立 C 字符串（是更长文本的前缀）时返回 null。
    /// </summary>
    public int? SafeSpan(int offset, int length) => SafeSpanOf(Blob, offset, length);

    /// <summary>无状态版本，供补丁引擎直接使用。</summary>
    public static int? SafeSpanOf(byte[] blob, int offset, int length)
    {
        long endLong = (long)offset + length;
        if (offset < 0 || endLong >= blob.Length) return null;

        int end = (int)endLong;
        if (blob[end] != 0) return null;

        int pos = end;
        while (pos < blob.Length && blob[pos] == 0) pos++;
        return pos - offset - 1;
    }
}
