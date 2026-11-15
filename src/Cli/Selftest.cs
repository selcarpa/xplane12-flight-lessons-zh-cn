using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XpLessonZhCn.Core;
using XpLessonZhCn.Platform;

namespace XpLessonZhCn.Cli;

/// <summary>
/// 不依赖游戏文件的自检：验证字符串解析、可用空间计算、补丁写入、结构校验、
/// 内置兼容清单与译文表的完整性。CI 与 --selftest 都跑这一套。
/// </summary>
public static class Selftest
{
    private static int _failures;

    public static bool Run()
    {
        _failures = 0;

        TestReadTextAt();
        TestLocateAndEnumerate();
        TestSafeSpan();
        TestPatchOne();
        TestEngineOrdering();
        TestVerifier();
        TestManifest();
        TestTranslations();
        TestPaths();

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

        // 头部填充：让 PE 头 / 文件尾校验有意义的区间（真实 exe 里这两处远离补丁区）
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

    private static void TestReadTextAt()
    {
        Console.WriteLine("read_text_at");
        byte[] blob = BuildBlob();
        var got = new PeImage(blob).ReadTextAt(PeImage.IndexOf(blob, Encoding.UTF8.GetBytes("Cirrus Intro"), 0, blob.Length));
        Check(got is not null && got.Value.Text == "Cirrus Intro", "能读出 NUL 结尾字符串");
        Check(new PeImage(blob).ReadTextAt(0) is null, "空串/零字节被跳过");
        Check(new PeImage(blob).ReadTextAt(blob.Length - 1) is null, "越界安全");
    }

    private static void TestLocateAndEnumerate()
    {
        Console.WriteLine("locate_region / enumerate_strings");
        byte[] blob = BuildBlob();
        var image = new PeImage(blob);
        image.LocateRegion();

        Check(image.RegionStart >= 0 && image.RegionEnd > image.RegionStart, $"区间 {image.RegionStart}..{image.RegionEnd}");

        var texts = new List<string>();
        foreach (var entry in image.EnumerateStrings()) texts.Add(entry.Text);

        Check(texts.Count >= 5, $"区间内解析出 {texts.Count} 条字符串");
        Check(texts.Count > 0 && texts[0] == "Cirrus Intro", "第一条是课程标题");
        Check(texts.Exists(t => t.StartsWith("Hey there", StringComparison.Ordinal)), "字幕锚点在区间内");

        int endAnchorOffset = PeImage.IndexOf(blob, PeImage.EndAnchor, 0, blob.Length);
        Check(endAnchorOffset == image.RegionEnd, "区间恰好在结束锚点处截止");
    }

    private static void TestSafeSpan()
    {
        Console.WriteLine("safe_span");
        byte[] blob = BuildBlob();
        int offset = PeImage.IndexOf(blob, Encoding.UTF8.GetBytes("Cirrus Intro"), 0, blob.Length);
        int? span = PeImage.SafeSpanOf(blob, offset, Encoding.UTF8.GetByteCount("Cirrus Intro"));
        Check(span is not null && span.Value >= Encoding.UTF8.GetByteCount("Cirrus Intro"), $"可用空间 {span} 字节（含对齐填充）");

        int prefixOffset = PeImage.IndexOf(blob, Encoding.UTF8.GetBytes("Flaps up!"), 0, blob.Length);
        Check(PeImage.SafeSpanOf(blob, prefixOffset, Encoding.UTF8.GetByteCount("Flaps up!")) is null, "前缀重叠被识别为非独立字符串");    }

    private static void TestPatchOne()
    {
        Console.WriteLine("patch_one");
        byte[] blob = BuildBlob();
        int offset = PeImage.IndexOf(blob, Encoding.UTF8.GetBytes("Cirrus Intro"), 0, blob.Length);
        byte[] english = Encoding.UTF8.GetBytes("Cirrus Intro");
        byte[] chinese = Encoding.UTF8.GetBytes("西锐入门");

        var (status, detail, _) = PatchEngine.PatchOne(blob, offset, english, chinese);
        Check(status == PatchStatus.Patched, $"写入成功（{detail}）");
        Check(new ReadOnlySpan<byte>(blob, offset, chinese.Length).SequenceEqual(chinese), "写入后内容正确");
        Check(blob[offset + chinese.Length] == 0, "结尾仍有 NUL");

        var again = PatchEngine.PatchOne(blob, offset, english, chinese);
        Check(again.Status is PatchStatus.Skipped, "重复写入被拒绝");

        int offset2 = PeImage.IndexOf(blob, Encoding.UTF8.GetBytes("Cirrus Landing"), 0, blob.Length);
        var tooLong = PatchEngine.PatchOne(blob, offset2, Encoding.UTF8.GetBytes("Cirrus Landing"), new byte[400]);
        Check(tooLong.Status == PatchStatus.TooLong, $"超长译文被拒绝（{tooLong.Detail}）");
    }

    private static void TestEngineOrdering()
    {
        Console.WriteLine("patch_engine（前缀重叠 + 顺序）");
        byte[] blob = BuildBlob();
        var image = new PeImage(blob);
        image.LocateRegion();

        var translations = new List<Translation>
        {
            new("Flaps up!", "收襟翼！"),
            new("Flaps up! 120 knots! 150 degree heading!", "收襟翼！120 节！航向 150！"),
            new("Cirrus Intro", "西锐入门"),
        };

        var result = PatchEngine.Apply(blob, image, translations);
        Check(result.Patched == 2, $"写入 {result.Patched} 条（预期 2）");
        Check(result.Skipped == 1, $"跳过 {result.Skipped} 条（预期 1，前缀重叠）");
        Check(result.TooLong == 0, "没有放不下的译文");
        Check(result.Spans.Count == 2, $"记录了 {result.Spans.Count} 个改动区间");

        var report = Verifier.Compare(BuildBlob(), blob, result.Spans);
        Check(report.Ok, $"结构校验通过（{report.Summary()}）");
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
        Check(manifest.Versions.Count >= 1, $"清单里有 {manifest.Versions.Count} 个版本");

        var current = manifest.Versions[0];
        Check(current.ExeSize == 91132488, $"当前版本 exe 大小 {current.ExeSize:N0}");
        Check(current.Sha256 == "2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936", "原版 sha256 正确");
        Check(current.PatchedSha256 == "36f5b21eb869e94e75a71de0aa7a4235976e16677424ad5421a45ac6b367131e", "补丁后 sha256 正确");
        Check(current.Entries == 513, $"词条数 {current.Entries}");
        Check(current.IsVerified, "状态为 verified");
        Check(manifest.FindByOriginal(current.Sha256) is not null, "能按原版哈希查到");
        Check(manifest.FindByPatched(current.PatchedSha256) is not null, "能按补丁后哈希查到");
        Check(manifest.FindByOriginal("deadbeef") is null, "未知哈希查不到（需要二次确认）");
    }

    private static void TestTranslations()
    {
        Console.WriteLine("translations");
        var pairs = Translations.Load(null);
        Check(pairs.Count == 513, $"内置译文 {pairs.Count} 条（预期 513）");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool allUnique = true;
        bool allNonEmpty = true;
        bool placeholderOk = true;

        foreach (var pair in pairs)
        {
            if (!seen.Add(pair.English)) allUnique = false;
            if (string.IsNullOrWhiteSpace(pair.English) || string.IsNullOrWhiteSpace(pair.Chinese)) allNonEmpty = false;
            if (pair.Chinese.Contains('\t')) allNonEmpty = false;

            if (CountPlaceholders(pair.English) != CountPlaceholders(pair.Chinese)) placeholderOk = false;
        }

        Check(allUnique, "没有重复词条");
        Check(allNonEmpty, "没有空词条/制表符");
        Check(placeholderOk, "[sim/xxx] 占位符数量一一对应");

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
        Check(titles >= 8, $"至少 8 条课程标题（实际 {titles}）");
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
