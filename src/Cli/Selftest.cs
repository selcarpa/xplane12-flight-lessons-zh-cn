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
        TestElfBlob();
        TestVerifier();
        TestManifest();
        TestTranslations();
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
        Check(manifest.Versions.Count >= 2, $"清单里有 {manifest.Versions.Count} 个版本（Windows + Linux 原生）");

        var windows = manifest.Versions[0];
        Check(windows.ExeSize == 91132488, $"Windows 版 exe 大小 {windows.ExeSize:N0}");
        Check(windows.Sha256 == "2ca099b7c6b218e78d02bdbe298be2aebf670b67c5adff827449b065e1107936", "Windows 版原版 sha256 正确");
        Check(windows.PatchedSha256 == "d021f2656852416686e9f9c6222db90111ef791eea7967c839ade03e2e7efdbb", "Windows 版补丁后 sha256 正确");
        Check(windows.Entries == 513, $"Windows 版词条数 {windows.Entries}");
        Check(windows.IsVerified, "Windows 版状态为 verified");

        var linux = manifest.FindByOriginal("0fe6bc5c5d661633840bc5eba0de88d8def190f1ff40f6a7011d9252cc63eabb");
        Check(linux is not null, "能按 Linux 原生版原版哈希查到");
        if (linux is not null)
        {
            Check(linux.ExeSize == 76056352, $"Linux 版 exe 大小 {linux.ExeSize:N0}");
            Check(linux.PatchedSha256 == "52218d9089e491f496bf96701989ab5b0a83eb76061086b21e0b9d786b88480e", "Linux 版补丁后 sha256 正确");
            Check(linux.Entries == 513, $"Linux 版词条数 {linux.Entries}（共用同一张表）");
            Check(linux.IsVerified, "Linux 版状态为 verified");
            Check(linux.PatchStats is not null && linux.PatchStats.TooLong == 0, "Linux 版没有放不下的译文");
        }

        Check(manifest.FindByOriginal(windows.Sha256) is not null, "能按 Windows 原版哈希查到");
        Check(manifest.FindByPatched(windows.PatchedSha256) is not null, "能按 Windows 补丁后哈希查到");
        Check(manifest.FindByPatched(linux?.PatchedSha256 ?? "") is not null, "能按 Linux 补丁后哈希查到");
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
