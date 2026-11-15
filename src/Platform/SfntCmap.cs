using System;

namespace XpLessonZhCn.Platform;

/// <summary>
/// 最小 sfnt / cmap 解析器：只回答「这个字体文件里有没有某个码点的字形」。
/// 支持 TrueType/OpenType（含 CFF 的 OTTO）与 cmap format 4 / 12。
/// 不依赖任何第三方库。
/// </summary>
public sealed class SfntCmap
{
    private readonly byte[] _data;
    private readonly int _offset;
    private readonly int _format;

    private SfntCmap(byte[] data, int offset, int format)
    {
        _data = data;
        _offset = offset;
        _format = format;
    }

    public static SfntCmap? TryOpen(byte[] data)
    {
        if (data.Length < 12) return null;

        uint magic = U32(data, 0);
        if (magic != 0x4F54544Fu && magic != 0x00010000u && magic != 0x74727565u)
        {
            return null; // 不是 sfnt
        }

        int numTables = U16(data, 4);
        int cmap = -1;
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + i * 16;
            if (record + 16 > data.Length) return null;

            if (data[record] == (byte)'c' && data[record + 1] == (byte)'m' &&
                data[record + 2] == (byte)'a' && data[record + 3] == (byte)'p')
            {
                cmap = (int)U32(data, record + 8);
                break;
            }
        }

        if (cmap <= 0 || cmap + 4 > data.Length) return null;

        int subtableCount = U16(data, cmap + 2);
        int bestFormat4 = -1;

        for (int i = 0; i < subtableCount; i++)
        {
            int record = cmap + 4 + i * 8;
            if (record + 8 > data.Length) break;

            int platform = U16(data, record);
            int encoding = U16(data, record + 2);
            // 注意：encoding record 里的 offset 是相对 cmap 表起点的
            int sub = cmap + (int)U32(data, record + 4);
            if (sub <= 0 || sub + 2 > data.Length) continue;

            bool isUnicode = platform == 0 || (platform == 3 && (encoding == 1 || encoding == 10));
            if (!isUnicode) continue;

            int format = U16(data, sub);
            if (format == 12) return new SfntCmap(data, sub, 12);
            if (format == 4 && bestFormat4 < 0) bestFormat4 = sub;
        }

        return bestFormat4 > 0 ? new SfntCmap(data, bestFormat4, 4) : null;
    }

    public bool HasCodePoint(int codePoint)
    {
        if (codePoint < 0 || codePoint > 0x10FFFF) return false;
        return _format == 12 ? HasFormat12(codePoint) : HasFormat4(codePoint);
    }

    private bool HasFormat12(int codePoint)
    {
        // format(2) reserved(2) length(4) language(4) numGroups(4) groups[12]
        if (_offset + 16 > _data.Length) return false;

        long numGroups = U32(_data, _offset + 12);
        int lo = 0;
        int hi = (int)Math.Min(numGroups, int.MaxValue) - 1;

        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            int group = _offset + 16 + mid * 12;
            if (group + 12 > _data.Length) return false;

            long start = U32(_data, group);
            long end = U32(_data, group + 4);
            long glyph = U32(_data, group + 8);

            if (codePoint < start) hi = mid - 1;
            else if (codePoint > end) lo = mid + 1;
            else return glyph != 0;
        }
        return false;
    }

    private bool HasFormat4(int codePoint)
    {
        if (_offset + 14 > _data.Length) return false;

        int segCount = U16(_data, _offset + 6) / 2;
        if (segCount <= 0) return false;

        int endBase = _offset + 14;
        int startBase = endBase + segCount * 2 + 2; // 跳过 reservedPad
        int deltaBase = startBase + segCount * 2;
        int rangeBase = deltaBase + segCount * 2;

        if (rangeBase + segCount * 2 > _data.Length) return false;

        for (int i = 0; i < segCount; i++)
        {
            int end = U16(_data, endBase + i * 2);
            if (codePoint > end) continue;

            int start = U16(_data, startBase + i * 2);
            if (codePoint < start) return false;

            short delta = (short)U16(_data, deltaBase + i * 2);
            int rangeOffset = U16(_data, rangeBase + i * 2);

            if (rangeOffset == 0)
            {
                return ((codePoint + delta) & 0xFFFF) != 0;
            }

            int address = rangeBase + i * 2 + rangeOffset + (codePoint - start) * 2;
            if (address < 0 || address + 2 > _data.Length) return false;

            int glyphId = U16(_data, address);
            if (glyphId == 0) return false;

            return ((glyphId + delta) & 0xFFFF) != 0;
        }

        return false;
    }

    private static int U16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

    private static uint U32(byte[] data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
