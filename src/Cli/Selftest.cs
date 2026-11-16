using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using XpLessonZhCn.Core;
using XpLessonZhCn.Platform;

namespace XpLessonZhCn.Cli;

/// <summary>
/// 不依赖游戏文件的自检：验证字符串解析、可用空间计算、补丁写入、结构校验、
/// 可执行文件类型检测（PE / ELF）、内置兼容清单与译文表的完整性。
/// CI 与 --selftest 都跑这一套。
/// </summary>
public static class Selftest
{
    private static int _failures;

    public static bool Run()
    {
        _failures = 0;

        TestImageFormat();
        TestReadTextAt();
        TestLocateAndEnumerate();
        TestSafeSpan();
        TestPatchOne();
        TestEngineOrdering();
        TestLenientMatching();
        TestLenientApply();
        TestElfBlob();
        TestVerifier();
        TestManifest();
        TestTranslations();
        TestProcessProbe();
        TestPaths();
        TestDirectoryOpener();

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("自检全部通过");
            return true;
        }

        Console.WriteLine($"自检失败：{_failures} 项");
        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            Console.WriteLine($"  ok  {message}");
        }
        else
        {
            Console.WriteLine($"  FAIL  {message}");
            _failures++;
        }
    }

    private static byte[] BuildBlob()
    {
        var parts = new List<byte>();

        // 头部填充：让 PE/ELF 头与文件尾校验有意义的区间（真实可执行文件里这两处远离补丁区）
        parts.AddRange(new byte[0x400]);

        // 标题块
        parts.AddRange(Encoding.UTF8.GetBytes("Cirrus Intro"));
        parts.AddRange(new byte[] { 0, 0, 0, 0 });
        parts.AddRange(Encoding.UTF8.GetBytes("Cirrus Landing"));
        parts.AddRange(new byte[] { 0, 0, 0, 0 });

        // UI 锚点
        parts.AddRange(Encoding.UTF8.GetBytes("oz_intro_widget_controller_text"));
        parts.AddRange(new byte[] { 0, 0, 0, 0 });

        // 字幕
        parts.AddRange(Encoding.UTF8.GetBytes("Hey there, it's Austin Meyer, the author of X-Plane."));
        parts.AddRange(new byte[] { 0, 0, 0, 0 });
        parts.AddRange(Encoding.UTF8.GetBytes("Today, we're in a realy nice plane."));
        parts.AddRange(new byte[] { 0 });

        // 前缀重叠用例：英文里只有长句，没有独立的 "Flaps up!" 字符串
        //（与真实 exe 一致：短句只是长句的前缀，SafeSpan 会判为「非独立字符串」）
        parts.AddRange(Encoding.UTF8.GetBytes("Flaps up! 120 knots! 150 degree heading!"));
        parts.AddRange(new byte[] { 0, 0 });

        // 区间结束锚点
        parts.AddRange(Encoding.UTF8.GetBytes("Could not load sound bank at: %s FMOD Error: %d"));
        parts.AddRange(new byte[] { 0 });

        // 文件尾填充
        parts.AddRange(new byte[0x1200]);

        return parts.ToArray();
    }

    /// <summary>给合成数据加上 ELF 魔数，模拟 Linux 原生版二进制。</summary>
    private static byte[] BuildElfBlob()
    {
        byte[] blob = BuildBlob();
        blob[0] = 0x7F;
        blob[1] = (byte)'E';
        blob[2] = (byte)'L';
        blob[3] = (byte)'F';
        return blob;
    }

    private static void TestImageFormat()
    {
        Console.WriteLine("image_format（自动检测可执行文件类型）");
        var mz = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };
        var elf = new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02 };
        Check(ImageFormat.Detect(mz) == ExecutableKind.Pe, "MZ 魔数识别为 Windows PE");
        Check(ImageFormat.Detect(elf) == ExecutableKind.Elf, "ELF 魔数识别为 Linux 原生 ELF");
        Check(ImageFormat.Detect(new byte[] { 0, 0, 0, 0 }) is null, "未知魔数返回 null");
        Check(ImageFormat.Detect(Array.Empty<byte>()) is null, "空数据返回 null");
        Check(ImageFormat.Detect(null!) is null, "null 安全");

        Check(GameExecutableResolver.IsGameDir(null) == false, "IsGameDir(null) 为 false");
    }

    private static void TestReadTextAt()
    {
        Console.WriteLine("read_text_at");
        byte[] blob = BuildBlob();
        var got = new LessonImage(blob).ReadTextAt(LessonImage.IndexOf(blob, Encoding.UTF8.GetBytes("Cirrus Intro"), 0, blob.Length));
        Check(got is not null && got.Value.Text == "Cirrus Intro", "能读出 NUL 结尾字符串");
        Check(new LessonImage(blob).ReadTextAt(0) is null, "空串/零字节被跳过");
        Check(new LessonImage(blob).ReadTextAt(blob.Length - 1) is null, "越界安全");
    }

    private static void TestLocateAndEnumerate()
    {
        Console.WriteLine("locate_region / enumerate_strings");
        byte[] blob = BuildBlob();
        var image = new LessonImage(blob);
        image.LocateRegion();

        Check(image.RegionStart >= 0 && image.RegionEnd > image.RegionStart, $"区间 {image.RegionStart}..{image.RegionEnd}");

        var texts = new List<string>();
        foreach (var entry in image.EnumerateStrings()) texts.Add(entry.Text);

        Check(texts.Count >= 5, $"区间内解析出 {texts.Count} 条字符串");
        Check(texts.Count > 0 && texts[0] == "Cirrus Intro", "第一条是课程标题");
        Check(texts.Exists(t => t.StartsWith("Hey there", StringComparison.Ordinal)), "字幕锚点在区间内");

        int endAnchorOffset = LessonImage.IndexOf(blob, LessonImage.EndAnchor, 0, blob.Length);
        Check(endAnchorOffset == image.RegionEnd, "区间恰好在结束锚点处截止");
    }

    private static void TestSafeSpan()
    {
        Console.WriteLine("safe_span");
        byte[] blob = BuildBlob();
        int offset = LessonImage.IndexOf(blob, Encoding.UTF8.GetBytes("Cirrus Intro"), 0, blob.Length);
        int? span = LessonImage.SafeSpanOf(blob, offset, Encoding.UTF8.GetByteCount("Cirrus Intro"));
        Check(span is not null && span.Value >= Encoding.UTF8.GetByteCount("Cirrus Intro"), $"可用空间 {span} 字节（含对齐填充）");

        int prefixOffset = LessonImage.IndexOf(blob, Encoding.UTF8.GetBytes("Flaps up!"), 0, blob.Length);
        Check(LessonImage.SafeSpanOf(blob, prefixOffset, Encoding.UTF8.GetByteCount("Flaps up!")) is null, "前缀重叠被识别为非独立字符串");
    }

    private static void TestPatchOne()
    {
        Console.WriteLine("patch_one");
        byte[] blob = BuildBlob();
        int offset = LessonImage.IndexOf(blob, Encoding.UTF8.GetBytes("Cirrus Intro"), 0, blob.Length);
        byte[] english = Encoding.UTF8.GetBytes("Cirrus Intro");
        byte[] chinese = Encoding.UTF8.GetBytes("西锐入门");

        var (status, detail, _) = PatchEngine.PatchOne(blob, offset, english, chinese);
        Check(status == PatchStatus.Patched, $"写入成功（{detail}）");
        Check(new ReadOnlySpan<byte>(blob, offset, chinese.Length).SequenceEqual(chinese), "写入后内容正确");
        Check(blob[offset + chinese.Length] == 0, "结尾仍有 NUL");

        var again = PatchEngine.PatchOne(blob, offset, english, chinese);
        Check(again.Status is PatchStatus.Skipped, "重复写入被拒绝");

        int offset2 = LessonImage.IndexOf(blob, Encoding.UTF8.GetBytes("Cirrus Landing"), 0, blob.Length);
        var tooLong = PatchEngine.PatchOne(blob, offset2, Encoding.UTF8.GetBytes("Cirrus Landing"), new byte[400]);
        Check(tooLong.Status == PatchStatus.TooLong, $"超长译文被拒绝（{tooLong.Detail}）");
    }

    private static void TestEngineOrdering()
    {
        Console.WriteLine("patch_engine（前缀重叠 + 顺序）");
        byte[] blob = BuildBlob();
        var image = new LessonImage(blob);
        image.LocateRegion();

        var translations = new List<Translation>
        {
            new("Flaps up!", "收襟翼！", 1000),
            new("Flaps up! 120 knots! 150 degree heading!", "收襟翼！120 节！航向 150！", 1000),
            new("Cirrus Intro", "西锐入门", 1000),
        };

        var result = PatchEngine.Apply(blob, image, translations);
        Check(result.Patched == 2, $"写入 {result.Patched} 条（预期 2）");
        Check(result.Skipped == 1, $"跳过 {result.Skipped} 条（预期 1，前缀重叠）");
        Check(result.TooLong == 0, "没有放不下的译文");
        Check(result.Spans.Count == 2, $"记录了 {result.Spans.Count} 个改动区间");

        var report = Verifier.Compare(BuildBlob(), blob, result.Spans);
        Check(report.Ok, $"结构校验通过（{report.Summary()}）");
    }

    /// <summary>
    /// 12.4.4 Linux 构建里个别字幕串前直接跟着二进制数据（没有 NUL 分隔），英文原文不在「字符串起点」。
    /// 宽松兜底必须能识别这种命中，同时不能把「长句片段」当成独立词条。
    /// </summary>
    private static void TestLenientMatching()
    {
        Console.WriteLine("lenient_prefix（句首垃圾数据 / 残字）");
        byte[] hey = Encoding.UTF8.GetBytes("Hey there, it's Austin Meyer.");
        byte[] flaps = Encoding.UTF8.GetBytes("Flaps up!");
        byte[] chineseHey = Encoding.UTF8.GetBytes("嗨，我是 Austin Meyer。");
        byte[] chineseFlaps = Encoding.UTF8.GetBytes("收襟翼！");

        var bytes = new List<byte>();
        bytes.Add(0x00);
        bytes.AddRange(new byte[] { 0x20, (byte)'A' });            // 数据残字 " A"
        bytes.AddRange(hey);
        bytes.Add(0x00);
        bytes.AddRange(new byte[] { 0x40, 0x83, (byte)'E' });      // 非法 UTF-8 残字
        bytes.AddRange(hey);
        bytes.Add(0x00);
        bytes.AddRange(Encoding.UTF8.GetBytes("OK we got 200 feet! ")); // 长句前缀，应忽略
        bytes.AddRange(flaps);
        bytes.Add(0x00);
        byte[] blob = bytes.ToArray();

        var matches = PatchEngine.LenientOccurrences(blob, hey, 0, blob.Length);
        Check(matches.Count == 2, $"识别出 {matches.Count} 条句首垃圾前缀命中（预期 2）");
        Check(matches.Count == 2 && matches[0].Offset == 3 && matches[0].PrefixLength == 2,
            "第一处命中：残字 \" A\" 之后，保留前缀不动");
        Check(matches.Count == 2 && matches[1].PrefixLength == 3,
            "第二处命中：非法 UTF-8 残字之后");

        var noMatch = PatchEngine.LenientOccurrences(blob, flaps, 0, blob.Length);
        Check(noMatch.Count == 0, "长句前缀里的短词条不被误判为独立字幕");

        int writeOffset = matches.Count > 0 ? matches[0].Offset : -1;
        var (status, _, _) = PatchEngine.PatchOneAt(
            blob, writeOffset, hey, chineseHey, matches.Count > 0 ? matches[0].Note : null);
        Check(status == PatchStatus.Patched, "能从英文原文处写入（跳过句首垃圾前缀）");
        Check(Encoding.UTF8.GetString(blob, 3, chineseHey.Length) == "嗨，我是 Austin Meyer。",
            "写入后该处是中文，且前缀字节保持不变");
        Check(blob[1] == 0x20 && blob[2] == (byte)'A', "句首垃圾前缀未被改动");
        Check(!blob.AsSpan().SequenceEqual(bytes.ToArray()), "内容确有改动");
    }

    /// <summary>
    /// 同一条字幕可能既有「字符串起点」命中（如课程卡片长串开头，属前缀重叠要跳过），
    /// 又有一份句首带数据前缀的独立拷贝（字幕本体，需要写）。Apply 必须两边都处理。
    /// 12.4.4 Linux 的 `Let's do a touch-n-go.` 就是这种情形。
    /// </summary>
    private static void TestLenientApply()
    {
        Console.WriteLine("lenient_apply（前缀长串 + 句首垃圾前缀独立拷贝）");
        const string longText = "Let's do a touch-n-go. A touch-n-go is a landing followed by full power.";
        const string shortText = "Let's do a touch-n-go.";
        byte[] anchorStart = Encoding.UTF8.GetBytes("oz_intro_widget_controller_text");
        byte[] anchorEnd = Encoding.UTF8.GetBytes("Could not load sound bank at:");

        var bytes = new List<byte>();
        bytes.AddRange(anchorStart);
        bytes.Add(0x00);
        int longStart = bytes.Count;
        bytes.AddRange(Encoding.UTF8.GetBytes(longText));
        bytes.Add(0x00);
        bytes.Add(0x00);
        bytes.AddRange(new byte[] { 0x40, 0x83, (byte)'E' });   // 数据尾巴（非法 UTF-8）
        int standaloneStart = bytes.Count;
        bytes.AddRange(Encoding.UTF8.GetBytes(shortText));
        bytes.Add(0x00);
        bytes.AddRange(anchorEnd);
        bytes.Add(0x00);
        byte[] blob = bytes.ToArray();

        var image = new LessonImage(blob);
        image.LocateRegion();
        var translations = new List<Translation>
        {
            new(shortText, "来个连续起降吧", 1000),
        };

        var result = PatchEngine.Apply(blob, image, translations);
        Check(result.Patched == 1, $"独立拷贝写入 {result.Patched} 条（预期 1）");
        Check(result.Skipped == 1, $"长串开头的同名前缀跳过 {result.Skipped} 条（预期 1）");
        Check(result.NotFound == 0 && result.TooLong == 0, "没有未找到 / 放不下");
        Check(Encoding.UTF8.GetString(blob, longStart, longText.Length) == longText,
            "长串开头的原文保持不变");
        byte[] expected = Encoding.UTF8.GetBytes("来个连续起降吧");
        Check(Encoding.UTF8.GetString(blob, standaloneStart, expected.Length) == "来个连续起降吧",
            "独立拷贝已写成中文");
        Check(result.Rows.Exists(r => r.Detail.Contains("垃圾前缀")), "报告里标注了跳过的垃圾前缀");
    }

    private static void TestElfBlob()
    {
        Console.WriteLine("elf_blob（同一套逻辑跑 ELF 魔数的数据）");
        byte[] elfBlob = BuildElfBlob();
        Check(ImageFormat.Detect(elfBlob) == ExecutableKind.Elf, "带 ELF 魔数的数据被识别为 ELF");

        var image = new LessonImage(elfBlob);
        image.LocateRegion();
        Check(image.RegionStart >= 0 && image.RegionEnd > image.RegionStart,
            $"ELF 数据同样能定位区间 {image.RegionStart}..{image.RegionEnd}");

        var translations = new List<Translation>
        {
            new("Cirrus Intro", "西锐入门", 1000),
            new("Today, we're in a realy nice plane.", "今天飞机不错。", 1000),
        };

        var original = (byte[])elfBlob.Clone();
        var result = PatchEngine.Apply(elfBlob, image, translations);
        Check(result.Patched == 2 && result.TooLong == 0, $"ELF 数据打补丁 {result.Patched} 条");

        var report = Verifier.Compare(original, elfBlob, result.Spans);
        Check(report.Ok, $"ELF 数据结构校验通过（{report.Summary()}）");
        Check(elfBlob[0] == 0x7F && elfBlob[1] == (byte)'E', "ELF 魔数保持原样");
    }

    private static void TestVerifier()
    {
        Console.WriteLine("verifier");
        byte[] original = BuildBlob();
        byte[] tampered = (byte[])original.Clone();
        tampered[0] ^= 0xFF;
        var bad = Verifier.Compare(original, tampered, new List<PatchSpan>());
        Check(!bad.Ok, "头部被改动能被发现");

        byte[] grown = new byte[original.Length + 4];
        Array.Copy(original, grown, original.Length);
        var sizeBad = Verifier.Compare(original, grown, new List<PatchSpan>());
        Check(!sizeBad.SizeSame, "文件大小变化能被发现");
    }

    private static void TestManifest()
    {
        Console.WriteLine("compat_manifest");
        var manifest = CompatManifest.Load(null);
        Check(manifest.Versions.Count >= 4, $"清单里有 {manifest.Versions.Count} 个版本（Windows / Linux 原生各 12.4.4 + 12.4.3）");

        // 最新 Windows 版本：12.4.4-r2 (build 124419)
        var windows1244 = manifest.FindByOriginal("5b18cc7d406bdf22be20135849b8705622cef6882edbb6e8d7c54e095b712563");
        Check(windows1244 is not null, "能按 Windows 12.4.4 原版哈希查到");
        if (windows1244 is not null)
        {
            Check(windows1244.ExeSize == 55022152, $"Windows 12.4.4 exe 大小 {windows1244.ExeSize:N0}");
            Check(windows1244.PatchedSha256 == "ae57d6197d44d0bcfa4cc1017c1caccc762338871a483745a965e18591b78a3b", "Windows 12.4.4 补丁后 sha256 正确");
            Check(windows1244.RegionStart == 42825408 && windows1244.RegionEnd == 42911160, $"Windows 12.4.4 文本区间 {windows1244.RegionStart}..{windows1244.RegionEnd}");
            Check(windows1244.Candidates == 582, $"Windows 12.4.4 区间内候选字符串 {windows1244.Candidates}");
            Check(windows1244.Entries == 513, $"Windows 12.4.4 词条数 {windows1244.Entries}");
            Check(windows1244.IsVerified, "Windows 12.4.4 状态为 verified");
            Check(windows1244.PatchStats is not null &&
                  windows1244.PatchStats.Patched == 513 &&
                  windows1244.PatchStats.Skipped == 8 &&
                  windows1244.PatchStats.TooLong == 0 &&
                  windows1244.PatchStats.NotFound == 0,
                "Windows 12.4.4 补丁统计 513/8/0/0");
        }

        // 旧版 Windows：12.4.3-r2 (build 124311)，仍保留在清单里继续支持
        var windows1243 = manifest.FindByOriginal("2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936");
        Check(windows1243 is not null, "能按 Windows 12.4.3 原版哈希查到");
        if (windows1243 is not null)
        {
            Check(windows1243.ExeSize == 91132488, $"Windows 12.4.3 exe 大小 {windows1243.ExeSize:N0}");
            Check(windows1243.PatchedSha256 == "deebc68ee5d3fe5adb33031a105d61d62c4554dd882b1bae88f7705fdc90ec5e", "Windows 12.4.3 补丁后 sha256 正确");
            Check(windows1243.Entries == 513, $"Windows 12.4.3 词条数 {windows1243.Entries}");
            Check(windows1243.IsVerified, "Windows 12.4.3 状态为 verified");
        }

        // 最新 Linux 原生版：12.4.4-r2 (build 124419)
        var linux1244 = manifest.FindByOriginal("565ac3942e952794d5625e04b2147543f2fca2f027942f5f205d142e52728fce");
        Check(linux1244 is not null, "能按 Linux 原生版 12.4.4 原版哈希查到");
        if (linux1244 is not null)
        {
            Check(linux1244.ExeSize == 78659600, $"Linux 12.4.4 exe 大小 {linux1244.ExeSize:N0}");
            Check(linux1244.PatchedSha256 == "257cdd957852f9065c8a2c1bf4e769198615189d081ac79c3e63adb4dbf632da", "Linux 12.4.4 补丁后 sha256 正确");
            Check(linux1244.RegionStart == 49594154 && linux1244.RegionEnd == 49647601, $"Linux 12.4.4 文本区间 {linux1244.RegionStart}..{linux1244.RegionEnd}");
            Check(linux1244.Candidates == 649, $"Linux 12.4.4 区间内候选字符串 {linux1244.Candidates}");
            Check(linux1244.Entries == 513, $"Linux 12.4.4 词条数 {linux1244.Entries}（共用同一张表）");
            Check(linux1244.IsVerified, "Linux 12.4.4 状态为 verified");
            Check(linux1244.PatchStats is not null &&
                  linux1244.PatchStats.Patched == 504 &&
                  linux1244.PatchStats.Skipped == 7 &&
                  linux1244.PatchStats.TooLong == 0 &&
                  linux1244.PatchStats.NotFound == 9,
                "Linux 12.4.4 补丁统计 504/7/0/9");
        }

        // 旧版 Linux 原生版：12.4.3-r2 (build 124311)，仍保留在清单里继续支持
        var linux = manifest.FindByOriginal("0fe6bc5c5d661633840bc5eba0de88d8def190f1ff40f6a7011d9252cc63eabb");
        Check(linux is not null, "能按 Linux 原生版 12.4.3 原版哈希查到");
        if (linux is not null)
        {
            Check(linux.ExeSize == 76056352, $"Linux 12.4.3 exe 大小 {linux.ExeSize:N0}");
            Check(linux.PatchedSha256 == "b34e075c300ed61174fba79fe7ddab4d2bdb4756597715fd1f15726d1855a5c2", "Linux 12.4.3 补丁后 sha256 正确");
            Check(linux.Entries == 513, $"Linux 12.4.3 词条数 {linux.Entries}（共用同一张表）");
            Check(linux.IsVerified, "Linux 12.4.3 状态为 verified");
            Check(linux.PatchStats is not null &&
                  linux.PatchStats.Patched == 507 &&
                  linux.PatchStats.TooLong == 0 &&
                  linux.PatchStats.NotFound == 6,
                "Linux 12.4.3 补丁统计 507/7/0/6");
        }

        Check(manifest.FindByOriginal(windows1244?.Sha256 ?? "") is not null, "能按 Windows 12.4.4 原版哈希查到");
        Check(manifest.FindByPatched(windows1244?.PatchedSha256 ?? "") is not null, "能按 Windows 12.4.4 补丁后哈希查到");
        Check(manifest.FindByOriginal(windows1243?.Sha256 ?? "") is not null, "能按 Windows 12.4.3 原版哈希查到");
        Check(manifest.FindByPatched(windows1243?.PatchedSha256 ?? "") is not null, "能按 Windows 12.4.3 补丁后哈希查到");
        Check(manifest.FindByOriginal(linux1244?.Sha256 ?? "") is not null, "能按 Linux 12.4.4 原版哈希查到");
        Check(manifest.FindByPatched(linux1244?.PatchedSha256 ?? "") is not null, "能按 Linux 12.4.4 补丁后哈希查到");
        Check(manifest.FindByPatched(linux?.PatchedSha256 ?? "") is not null, "能按 Linux 12.4.3 补丁后哈希查到");
        Check(manifest.FindByOriginal("deadbeef") is null, "未知哈希查不到（需要二次确认）");
    }

    private static void TestTranslations()
    {
        Console.WriteLine("translations（CSV，单一译文 + 每条字节上限）");
        var pairs = Translations.Load(null);
        Check(pairs.Count == 513, $"共用译文 {pairs.Count} 条（预期 513）");

        bool allUnique = true;
        bool allNonEmpty = true;
        bool placeholderOk = true;
        bool allWithinBudget = true;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (!seen.Add(pair.English)) allUnique = false;
            if (string.IsNullOrWhiteSpace(pair.English) || string.IsNullOrWhiteSpace(pair.Chinese)) allNonEmpty = false;
            if (pair.Chinese.Contains('\t')) allNonEmpty = false;

            if (CountPlaceholders(pair.English) != CountPlaceholders(pair.Chinese)) placeholderOk = false;
            if (Encoding.UTF8.GetByteCount(pair.Chinese) > pair.BudgetBytes) allWithinBudget = false;
        }

        Check(allUnique, "没有重复词条");
        Check(allNonEmpty, "没有空词条/制表符");
        Check(placeholderOk, "[sim/xxx] 占位符数量一一对应");
        Check(allWithinBudget, "每条中文都不超过记录的上限（budget_bytes）");

        bool allHasBudget = true;
        foreach (var pair in pairs)
        {
            if (pair.BudgetBytes <= 0) allHasBudget = false;
        }
        Check(allHasBudget, "每条都有正的字节上限");

        int titles = 0;
        foreach (var pair in pairs)
        {
            if (pair.English.StartsWith("Cirrus ", StringComparison.Ordinal) ||
                pair.English.StartsWith("Evolution ", StringComparison.Ordinal) ||
                pair.English.StartsWith("Tesla ", StringComparison.Ordinal))
            {
                titles++;
            }
        }
        Check(titles == 8, $"共 {titles} 个课程标题（预期 8；Linux 原生版没有 Tesla 标题串，应用时算「不适用」）");
    }

    /// <summary>
    /// 进程判定只看 X-Plane 家族；单独的 wineserver / proton 与工具自身的进程名不能算游戏在运行
    /// （旧实现会因残留 Wine 进程把「打补丁」按钮一直置灰）。
    /// </summary>
    private static void TestProcessProbe()
    {
        Console.WriteLine("process_probe（不把残留 Wine 进程 / 工具自身当成游戏）");

        Check(ProcessProbe.IsGameProcessName("X-Plane"), "X-Plane 算游戏进程");
        Check(ProcessProbe.IsGameProcessName("X-Plane.exe"), "X-Plane.exe（Wine/Proton）算游戏进程");
        Check(ProcessProbe.IsGameProcessName("X-Plane-x86_64"), "X-Plane-x86_64（Linux 原生）算游戏进程");
        Check(!ProcessProbe.IsGameProcessName("XPlane12-FlightLessons-zh-CN"), "工具自己的进程名不算游戏");
        Check(!ProcessProbe.IsGameProcessName("wineserver"), "wineserver 本身不算游戏进程");
        Check(!ProcessProbe.IsGameProcessName("proton"), "proton 本身不算游戏进程");
        Check(!ProcessProbe.IsGameProcessName(""), "空进程名不算游戏");
        Check(!ProcessProbe.IsGameProcessName(null), "null 进程名不算游戏");

        Check(ProcessProbe.IsWineProcessName("wineserver"), "wineserver 被识别为 Wine 进程");
        Check(ProcessProbe.IsWineProcessName("wine64-preloader"), "wine64-preloader 被识别为 Wine 进程");
        Check(ProcessProbe.IsWineProcessName("proton"), "proton 被识别为 Wine/Proton 进程");
        Check(!ProcessProbe.IsWineProcessName("X-Plane-x86_64"), "原生 X-Plane 不是 Wine 进程");
    }

    private static void TestPaths()
    {
        Console.WriteLine("path_policy（产物固定在程序目录）");
        string workDir = XpLessonZhCn.Platform.PathPolicy.WorkDir;

        Check(workDir == Path.Combine(AppContext.BaseDirectory, ".zh_cn_translate"),
            $"工作目录 = 程序目录/.zh_cn_translate（{workDir}）");

        foreach (var (name, path) in new (string, string)[]
                 {
                     ("backup", XpLessonZhCn.Platform.PathPolicy.BackupDir),
                     ("data", XpLessonZhCn.Platform.PathPolicy.DataDir),
                     ("log", XpLessonZhCn.Platform.PathPolicy.LogFile),
                     ("settings", XpLessonZhCn.Platform.PathPolicy.SettingsFile),
                     ("report", XpLessonZhCn.Platform.PathPolicy.ReportFile),
                 })
        {
            Check(path.StartsWith(workDir, StringComparison.Ordinal), $"{name} 位于工作目录内");
        }
    }

    /// <summary>
    /// 「打开备份目录」与「自动查找游戏目录」都必须摆脱对当前工作目录的依赖：
    /// 便携程序可能从随后被删除的目录启动，此时取当前目录、或按相对程序名启动进程，
    /// 都会报 <c>Unable to find the specified file.</c>（getcwd 失败）。
    /// </summary>
    private static void TestDirectoryOpener()
    {
        Console.WriteLine("directory_opener / cwd_resilience（不依赖当前工作目录）");
        string backupDir = PathPolicy.BackupDir;

        Check(GameLocator.CurrentDirectoryOrNull() is not null, "正常情况下能读到当前工作目录");

        bool built = DirectoryOpener.TryBuildStartInfo(backupDir, out ProcessStartInfo? startInfo, out string? error);
        if (built)
        {
            Check(startInfo is not null, "能构建打开目录的启动信息");
            Check(startInfo!.WorkingDirectory == backupDir, "子进程工作目录 = 要打开的目录");
            if (!OperatingSystem.IsWindows())
            {
                Check(Path.IsPathRooted(startInfo.FileName), "启动器使用绝对路径（不按当前目录解析）");
            }
        }
        else
        {
            Check(!string.IsNullOrEmpty(error), "找不到启动器时返回明确错误而不是抛异常");
        }

        // Windows 用 ShellExecute + explorer.exe，不经过「按当前目录解析程序」的路径，问题只存在于 Unix
        if (OperatingSystem.IsWindows()) return;

        // 复现原问题现场：进程的当前目录被删除后，任何按相对程序名启动的 Process.Start
        // 都会抛 FileNotFoundException: Unable to find the specified file.
        string? saved = GameLocator.CurrentDirectoryOrNull();
        string ghost = Path.Combine(Path.GetTempPath(), "xp12-ghost-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ghost);
        try
        {
            Environment.CurrentDirectory = ghost;
            bool deleted;
            try
            {
                Directory.Delete(ghost, recursive: true);
                deleted = true;
            }
            catch (Exception)
            {
                deleted = false;
            }

            if (deleted)
            {
                Check(GetCurrentDirectoryFails(), "当前目录被删除后取当前目录会失败（复现原问题前提）");
                Check(GameLocator.CurrentDirectoryOrNull() is null, "自动查找游戏目录时能容忍当前目录被删除");
                bool builtWithoutCwd = DirectoryOpener.TryBuildStartInfo(backupDir, out ProcessStartInfo? psi, out _);
                Check(!builtWithoutCwd || (psi is not null && Path.IsPathRooted(psi.FileName)),
                    "当前目录失效时仍能构建打开目录的启动信息");
            }
            else
            {
                Console.WriteLine("  --  （当前平台不允许删除进程当前目录，跳过删除目录的复现）");
            }
        }
        finally
        {
            if (saved is not null)
            {
                try { Environment.CurrentDirectory = saved; } catch (Exception) { }
            }
        }
    }

    private static bool GetCurrentDirectoryFails()
    {
        try
        {
            _ = Directory.GetCurrentDirectory();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static int CountPlaceholders(string text)
    {
        int count = 0;
        int index = 0;
        while (true)
        {
            int start = text.IndexOf("[sim/", index, StringComparison.Ordinal);
            if (start < 0) break;
            int close = text.IndexOf(']', start);
            if (close < 0) break;
            count++;
            index = close + 1;
        }
        return count;
    }
}
