using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using XpLessonZhCn.Core;
using XpLessonZhCn.Platform;

namespace XpLessonZhCn.Service;

public sealed class Analysis
{
    public string GameDir { get; set; } = "";
    public string ExePath { get; set; } = "";
    public bool ExeExists { get; set; }
    public long ExeSize { get; set; }
    public string Sha256 { get; set; } = "";
    public KnownVersion? Known { get; set; }
    public KnownVersion? KnownPatched { get; set; }
    public bool AlreadyPatched { get; set; }
    public bool RegionOk { get; set; }
    public string RegionError { get; set; } = "";
    public int RegionStart { get; set; }
    public int RegionEnd { get; set; }
    public int Candidates { get; set; }
    public int Entries { get; set; }
    public int Pending { get; set; }
    public int Applied { get; set; }
    public int Missing { get; set; }
    public bool GameRunning { get; set; }
    public string GameRunningDetail { get; set; } = "";
    public FontProbeResult Font { get; set; } = new();
    public bool WorkDirWritable { get; set; }

    /// <summary>游戏目录（也就是 exe 所在目录）是否可写。</summary>
    public bool GameDirWritable { get; set; }

    /// <summary>Windows 下当前没有管理员权限且目录不可写 → 需要提权才能打补丁。</summary>
    public bool NeedsElevationGain { get; set; }
    public string WorkDir { get; set; } = "";
    public int BackupCount { get; set; }
    public bool NeedsSecondaryConfirmation { get; set; }
    public string? FatalError { get; set; }
    public List<Translation> TranslationList { get; set; } = new();

    public bool CanPatch => ExeExists && RegionOk && !AlreadyPatched && !GameRunning && FatalError is null;

    public string VersionText
    {
        get
        {
            if (Known is not null) return Known.DisplayName;
            if (KnownPatched is not null) return KnownPatched.DisplayName + "（已打补丁）";
            return "未验证版本";
        }
    }
}

public enum ApplyStatus
{
    Applied,
    AlreadyPatched,
    NeedsSecondaryConfirmation,
    RefusedGameRunning,
    /// <summary>没有写入权限，需要以管理员身份运行。</summary>
    NeedsPermission,
    Failed,
    NothingToDo,
}

public sealed class ApplyOutcome
{
    public ApplyStatus Status { get; set; }
    public string Message { get; set; } = "";
    public EngineResult? Engine { get; set; }
    public VerifyReport? PostVerify { get; set; }
    public string? BackupPath { get; set; }
    public string PatchedSha256 { get; set; } = "";
}

public sealed class VerifyOutcome
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public VerifyReport? Report { get; set; }
    public string? BackupName { get; set; }
}

public sealed class BackupInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShaPrefix { get; set; } = "";
    public long Size { get; set; }
    public DateTime Modified { get; set; }
}

public sealed class RestoreOutcome
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public string RestoredSha256 { get; set; } = "";
}

/// <summary>把 Core / Platform 串起来的高层服务，GUI 与 CLI 共用。</summary>
public sealed class PatchService
{
    public PatchService(string gameDir)
    {
        GameDir = Path.GetFullPath(gameDir);
        ExePath = Path.Combine(GameDir, GameLocator.ExeName);
    }

    public string GameDir { get; }
    public string ExePath { get; }
    public Action<string>? Log { get; set; }

    private string? _workDir;

    /// <summary>工作目录：优先游戏目录下的 .zh_cn_translate，不可写时回落到用户数据目录。</summary>
    public string WorkDir
    {
        get
        {
            if (_workDir is null)
            {
                string primary = PathPolicy.WorkDir(GameDir);
                _workDir = PathPolicy.IsWritable(primary) ? primary : PathPolicy.FallbackWorkDir();
            }
            return _workDir;
        }
    }

    private static string TranslationsPath => Path.Combine(AppContext.BaseDirectory, "translations", "zh.tsv");
    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "compat", "known-versions.json");

    public List<Translation> LoadTranslations() => new(Translations.Load(TranslationsPath, Log));

    public CompatManifest LoadManifest() => CompatManifest.Load(ManifestPath, Log);

    public Analysis Analyze()
    {
        var analysis = new Analysis
        {
            GameDir = GameDir,
            ExePath = ExePath,
            WorkDir = PathPolicy.WorkDir(GameDir),
        };

        if (!File.Exists(ExePath))
        {
            analysis.FatalError = $"游戏目录下没有 {GameLocator.ExeName}：{ExePath}";
            return analysis;
        }

        analysis.ExeExists = true;
        var info = new FileInfo(ExePath);
        analysis.ExeSize = info.Length;

        byte[] blob = File.ReadAllBytes(ExePath);
        analysis.Sha256 = Hashes.Sha256(blob);

        var manifest = LoadManifest();
        analysis.Known = manifest.FindByOriginal(analysis.Sha256);
        analysis.KnownPatched = manifest.FindByPatched(analysis.Sha256);
        analysis.AlreadyPatched = analysis.KnownPatched is not null;
        analysis.NeedsSecondaryConfirmation = !analysis.AlreadyPatched &&
                                             (analysis.Known is null || !analysis.Known.IsVerified);

        analysis.TranslationList = LoadTranslations();
        analysis.Entries = analysis.TranslationList.Count;

        var image = new PeImage(blob);
        try
        {
            image.LocateRegion();
            analysis.RegionOk = true;
            analysis.RegionStart = image.RegionStart;
            analysis.RegionEnd = image.RegionEnd;

            int candidates = 0;
            foreach (var _ in image.EnumerateStrings()) candidates++;
            analysis.Candidates = candidates;

            foreach (var pair in analysis.TranslationList)
            {
                byte[] english = Encoding.UTF8.GetBytes(pair.English);
                byte[] chinese = Encoding.UTF8.GetBytes(pair.Chinese);

                bool hasEnglish = PatchEngine.Occurrences(blob, english, image.RegionStart, image.RegionEnd).Count > 0;
                if (hasEnglish)
                {
                    analysis.Pending++;
                }
                else if (PeImage.IndexOf(blob, chinese, image.RegionStart, image.RegionEnd) >= 0)
                {
                    analysis.Applied++;
                }
                else
                {
                    analysis.Missing++;
                }
            }
        }
        catch (PatchException ex)
        {
            analysis.RegionOk = false;
            analysis.RegionError = ex.Message;
        }

        analysis.GameRunning = ProcessProbe.IsGameRunning(out string runningDetail);
        analysis.GameRunningDetail = runningDetail;

        analysis.Font = WineFontProbe.Check(GameDir);

        analysis.WorkDir = WorkDir;
        analysis.WorkDirWritable = PathPolicy.IsWritable(analysis.WorkDir, out string? workDirError);
        if (!analysis.WorkDirWritable)
        {
            Log?.Invoke($"工作目录不可写（{workDirError}）：无法生成备份与报告，打补丁会被拒绝。");
        }

        analysis.GameDirWritable = PathPolicy.IsWritable(GameDir, out string? gameDirError);
        if (!analysis.GameDirWritable)
        {
            Log?.Invoke($"游戏目录不可写（{gameDirError}）：需要以管理员身份运行才能替换 X-Plane.exe。");
        }

        analysis.NeedsElevationGain = !Elevation.IsElevated &&
                                      (!analysis.GameDirWritable || !analysis.WorkDirWritable);

        analysis.BackupCount = ListBackups().Count;
        return analysis;
    }

    public ApplyOutcome Apply(bool allowUnknownVersion, Action<int, int>? progress = null)
    {
        if (ProcessProbe.IsGameRunning(out string runningDetail))
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.RefusedGameRunning,
                Message = $"X-Plane 12 正在运行（{runningDetail}），请先退出游戏再打补丁。",
            };
        }

        // 先探权限，避免白忙一场（也能在弹窗里提前告诉用户需要提权）
        if (!PathPolicy.IsWritable(GameDir, out string? writeError))
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.NeedsPermission,
                Message = $"没有写入游戏目录的权限：\n{GameDir}\n（{writeError}）\n\n" +
                          "需要以管理员身份运行本工具，才能替换 X-Plane.exe。",
            };
        }

        if (!File.Exists(ExePath))
        {
            return new ApplyOutcome { Status = ApplyStatus.Failed, Message = $"找不到 {ExePath}" };
        }

        var manifest = LoadManifest();
        byte[] original = File.ReadAllBytes(ExePath);
        string originalSha = Hashes.Sha256(original);

        KnownVersion? known = manifest.FindByOriginal(originalSha);
        KnownVersion? knownPatched = manifest.FindByPatched(originalSha);

        if (knownPatched is not null)
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.AlreadyPatched,
                Message = $"当前 exe 已经是打完补丁的状态（{knownPatched.DisplayName}）。如需重新打，请先还原原版。",
                PatchedSha256 = originalSha,
            };
        }

        if (known is null && !allowUnknownVersion)
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.NeedsSecondaryConfirmation,
                Message = $"当前 exe 的 sha256（{originalSha}）不在兼容清单里，这个版本未经验证。" +
                          "继续打补丁可能导致游戏异常，需要二次确认后才能执行。",
            };
        }

        var image = new PeImage(original);
        try
        {
            image.LocateRegion();
        }
        catch (PatchException ex)
        {
            return new ApplyOutcome { Status = ApplyStatus.Failed, Message = ex.Message };
        }

        var translations = LoadTranslations();
        var blob = (byte[])original.Clone();
        var engine = PatchEngine.Apply(blob, image, translations);

        progress?.Invoke(1, 1);

        if (engine.TooLong > 0)
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.Failed,
                Message = $"有 {engine.TooLong} 条译文放不下（需要改短译文），已中止，未写盘。",
                Engine = engine,
            };
        }

        if (engine.Patched == 0)
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.NothingToDo,
                Message = known is null
                    ? "没有需要写入的内容（译文可能已全部应用）。"
                    : "没有需要写入的内容（英文原文都不在 exe 里了）。",
                Engine = engine,
            };
        }

        // 写入前：结构不变量校验
        var preVerify = Verifier.Compare(original, blob, engine.Spans);
        if (!preVerify.Ok)
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.Failed,
                Message = $"补丁结果未通过结构校验：{preVerify.Summary()}。已中止，未写盘。",
                Engine = engine,
                PostVerify = preVerify,
            };
        }

        // 写入前：已知识别版本必须与清单记录的补丁后哈希一致
        string patchedSha = Hashes.Sha256(blob);
        if (known is not null && !string.IsNullOrEmpty(known.PatchedSha256) &&
            !string.Equals(known.PatchedSha256, patchedSha, StringComparison.OrdinalIgnoreCase))
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.Failed,
                Message = $"补丁结果与兼容清单记录的哈希不一致（得到 {patchedSha}，预期 {known.PatchedSha256}）。" +
                          "已中止，未写盘，请联系维护者。",
                Engine = engine,
                PostVerify = preVerify,
            };
        }

        string backupDir = PathPolicy.EnsureDir(Path.Combine(WorkDir, "backup"));
        string backupPath = Path.Combine(backupDir, $"{GameLocator.ExeName}.{originalSha[..12]}.bak");
        if (!File.Exists(backupPath))
        {
            try
            {
                File.WriteAllBytes(backupPath, original);
                Log?.Invoke($"已备份原版 exe：{backupPath}");
            }
            catch (Exception ex)
            {
                bool permission = ExeWriter.IsAccessDenied(ex);
                return new ApplyOutcome
                {
                    Status = permission ? ApplyStatus.NeedsPermission : ApplyStatus.Failed,
                    Message = permission
                        ? $"没有权限写入备份文件：\n{backupPath}\n（{ex.Message}）\n\n" +
                          "需要以管理员身份运行本工具，才能备份并替换 X-Plane.exe。"
                        : $"备份原版 exe 失败，已中止（未改动游戏）：{ex.Message}",
                    Engine = engine,
                };
            }
        }
        else
        {
            Log?.Invoke($"原版备份已存在：{backupPath}");
        }

        try
        {
            ExeWriter.WriteAtomically(ExePath, blob, Log);
        }
        catch (ExeWriteException ex) when (ex.IsPermissionProblem)
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.NeedsPermission,
                Message = $"没有权限写入游戏目录：\n{Path.GetDirectoryName(ExePath)}\n\n{ex.Message}\n\n" +
                          "需要以管理员身份运行本工具，才能替换 X-Plane.exe。",
                Engine = engine,
            };
        }
        catch (Exception ex)
        {
            return new ApplyOutcome
            {
                Status = ApplyStatus.Failed,
                Message = $"写入 exe 失败：{ex.Message}",
                Engine = engine,
            };
        }

        // 写入后：重读文件核对哈希，不一致立即用备份恢复
        string writtenSha = Hashes.Sha256File(ExePath);
        if (!string.Equals(writtenSha, patchedSha, StringComparison.OrdinalIgnoreCase))
        {
            TryRestoreFrom(backupPath);
            return new ApplyOutcome
            {
                Status = ApplyStatus.Failed,
                Message = $"写入后校验不通过（得到 {writtenSha}，预期 {patchedSha}），已用备份自动还原。",
                Engine = engine,
            };
        }

        return new ApplyOutcome
        {
            Status = ApplyStatus.Applied,
            Message = known is not null
                ? $"补丁完成并通过校验（{known.DisplayName}，共写入 {engine.Patched} 条）。"
                : $"补丁完成并通过结构校验（未验证版本，共写入 {engine.Patched} 条）。",
            Engine = engine,
            PostVerify = preVerify,
            BackupPath = backupPath,
            PatchedSha256 = writtenSha,
        };
    }

    /// <summary>预演：只在内存里打补丁，不写盘；同时导出报告。</summary>
    public EngineResult? DryRun(out string reportPath)
    {
        reportPath = "";

        if (!File.Exists(ExePath))
        {
            Log?.Invoke($"找不到 {ExePath}");
            return null;
        }

        byte[] original = File.ReadAllBytes(ExePath);
        var image = new PeImage(original);
        try
        {
            image.LocateRegion();
        }
        catch (PatchException ex)
        {
            Log?.Invoke(ex.Message);
            return null;
        }

        var translations = LoadTranslations();
        var blob = (byte[])original.Clone();
        var engine = PatchEngine.Apply(blob, image, translations);
        reportPath = StoreReport(engine);
        return engine;
    }

    public VerifyOutcome Verify()
    {
        if (!File.Exists(ExePath)) return new VerifyOutcome { Ok = false, Message = $"找不到 {ExePath}" };

        byte[] current = File.ReadAllBytes(ExePath);
        string sha = Hashes.Sha256(current);

        var manifest = LoadManifest();
        var knownOriginal = manifest.FindByOriginal(sha);
        if (knownOriginal is not null)
        {
            return new VerifyOutcome { Ok = false, Message = "当前 exe 是原版，还没有打补丁。" };
        }

        var backups = ListBackups();
        if (backups.Count == 0)
        {
            return new VerifyOutcome { Ok = false, Message = "没有找到原版备份，无法比对，请重新打一次补丁（会自动备份）。" };
        }

        var backup = backups[0];
        byte[] original = File.ReadAllBytes(backup.Path);
        var translations = LoadTranslations();
        var spans = ComputeSpansFromTranslations(current, translations);

        var report = Verifier.Compare(original, current, spans);
        return new VerifyOutcome
        {
            Ok = report.Ok,
            Message = report.Ok
                ? $"校验通过：改动全部落在教学字符串范围内，文件结构未变（{report.Summary()}）。"
                : $"校验失败：{report.Summary()}",
            Report = report,
            BackupName = backup.Name,
        };
    }

    public List<BackupInfo> ListBackups()
    {
        var list = new List<BackupInfo>();
        string dir = Path.Combine(WorkDir, "backup");
        if (!Directory.Exists(dir)) return list;

        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, GameLocator.ExeName + ".*.bak"))
            {
                var info = new FileInfo(file);
                string name = info.Name;
                string prefix = name.Length > GameLocator.ExeName.Length + 1
                    ? name[(GameLocator.ExeName.Length + 1)..].Split('.')[0]
                    : "";
                list.Add(new BackupInfo
                {
                    Path = file,
                    Name = name,
                    ShaPrefix = prefix,
                    Size = info.Length,
                    Modified = info.LastWriteTime,
                });
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"读取备份列表失败：{ex.Message}");
        }

        list.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return list;
    }

    public RestoreOutcome Restore(string? backupName)
    {
        if (ProcessProbe.IsGameRunning(out string runningDetail))
        {
            return new RestoreOutcome { Ok = false, Message = $"X-Plane 12 正在运行（{runningDetail}），请先退出游戏。" };
        }

        var backups = ListBackups();
        if (backups.Count == 0)
        {
            return new RestoreOutcome { Ok = false, Message = "没有可用的原版备份。" };
        }

        BackupInfo? target = null;
        if (!string.IsNullOrWhiteSpace(backupName))
        {
            foreach (var backup in backups)
            {
                if (string.Equals(backup.Name, backupName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(backup.Path, backupName, StringComparison.OrdinalIgnoreCase))
                {
                    target = backup;
                    break;
                }
            }
            if (target is null) return new RestoreOutcome { Ok = false, Message = $"找不到备份：{backupName}" };
        }
        else
        {
            target = backups[0];
        }

        byte[] data = File.ReadAllBytes(target.Path);
        string sha = Hashes.Sha256(data);

        if (target.ShaPrefix.Length == 12 && !sha.StartsWith(target.ShaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreOutcome
            {
                Ok = false,
                Message = $"备份文件可能已损坏（文件名哈希 {target.ShaPrefix} 与实际 {sha[..12]} 不一致），已中止。",
            };
        }

        try
        {
            ExeWriter.WriteAtomically(ExePath, data, Log);
        }
        catch (ExeWriteException ex) when (ex.IsPermissionProblem)
        {
            return new RestoreOutcome
            {
                Ok = false,
                Message = $"没有权限写入游戏目录，无法还原。\n需要以管理员身份运行本工具。\n\n{ex.Message}",
            };
        }
        catch (Exception ex)
        {
            return new RestoreOutcome { Ok = false, Message = $"还原失败：{ex.Message}" };
        }

        string writtenSha = Hashes.Sha256File(ExePath);
        if (!string.Equals(writtenSha, sha, StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreOutcome { Ok = false, Message = "还原后校验不通过，请手动确认。" };
        }

        return new RestoreOutcome { Ok = true, Message = $"已还原为原版（{target.Name}）。", RestoredSha256 = writtenSha };
    }

    public string StoreReport(EngineResult engine, string? workDirOverride = null)
    {
        string dir = workDirOverride ?? WorkDir;
        try
        {
            PathPolicy.EnsureDir(Path.Combine(dir, "data"));
            string path = Path.Combine(dir, "data", "patch_report.tsv");

            var builder = new StringBuilder();
            builder.AppendLine("english\toffset\tstatus\tdetail");
            foreach (var row in engine.Rows)
            {
                string english = row.English.Replace("\t", " ").Replace("\n", " ");
                builder.Append(english).Append('\t')
                    .Append(row.Offset.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(StatusText(row.Status)).Append('\t')
                    .AppendLine(row.Detail);
            }
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
            Log?.Invoke($"报告已写入：{path}");
            return path;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"写报告失败（不影响补丁结果）：{ex.Message}");
            return "";
        }
    }

    public static string StatusText(PatchStatus status) => status switch
    {
        PatchStatus.Patched => "patched",
        PatchStatus.TooLong => "too-long",
        PatchStatus.Skipped => "skipped",
        _ => "not-found",
    };

    private static List<PatchSpan> ComputeSpansFromTranslations(byte[] blob, IReadOnlyList<Translation> translations)
    {
        var spans = new List<PatchSpan>();
        foreach (var pair in translations)
        {
            byte[] chinese = Encoding.UTF8.GetBytes(pair.Chinese);
            int pos = 0;
            while (true)
            {
                int hit = PeImage.IndexOf(blob, chinese, pos, blob.Length);
                if (hit < 0) break;

                if (hit == 0 || blob[hit - 1] == 0)
                {
                    int stop = hit + chinese.Length;
                    while (stop < blob.Length && blob[stop] == 0) stop++;
                    spans.Add(new PatchSpan(hit, stop));
                }
                pos = hit + 1;
            }
        }
        return spans;
    }

    private void TryRestoreFrom(string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath)) return;
            ExeWriter.WriteAtomically(ExePath, File.ReadAllBytes(backupPath), Log);
            Log?.Invoke($"已用备份还原：{backupPath}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"自动还原失败，请手动用备份还原：{ex.Message}");
        }
    }
}
