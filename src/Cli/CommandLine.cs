using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XpLessonZhCn.Core;
using XpLessonZhCn.Platform;
using XpLessonZhCn.Service;

namespace XpLessonZhCn.Cli;

/// <summary>
/// 无界面命令行模式（CI / headless Linux / 排障用）。
/// 没有参数时返回 null，由 Program 打开 GUI。
/// </summary>
public static class CommandLine
{
    public const int ExitOk = 0;
    public const int ExitRecoverable = 1;
    public const int ExitVerifyFailed = 2;
    public const int ExitEnvironment = 3;

    /// <summary>GUI 启动时通过 --path 传入的游戏目录。</summary>
    public static string? StartupGameDir { get; private set; }

    public static int? TryRun(string[] args)
    {
        if (args.Length == 0) return null;

        ConsoleBridge.Enable();

        string? command = null;
        string? path = null;
        string? backup = null;
        bool force = false;
        bool yes = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    PrintHelp();
                    return ExitOk;
                case "--version":
                    PrintVersion();
                    return ExitOk;
                case "--selftest":
                    command = "selftest";
                    break;
                case "--check":
                    command = "check";
                    break;
                case "--dry-run":
                case "--report":
                    command = "dry-run";
                    break;
                case "--apply":
                    command = "apply";
                    break;
                case "--verify":
                    command = "verify";
                    break;
                case "--restore":
                    command = "restore";
                    break;
                case "--list-backups":
                    command = "list-backups";
                    break;
                case "--font-check":
                    command = "font-check";
                    break;
                case "--force":
                    force = true;
                    break;
                case "--yes":
                case "-y":
                    yes = true;
                    break;
                case "--path":
                    path = NextValue(args, ref i);
                    break;
                case "--backup":
                    backup = NextValue(args, ref i);
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"未知参数：{arg}（用 --help 查看用法）");
                        return ExitEnvironment;
                    }
                    if (command is null)
                    {
                        Console.Error.WriteLine($"未知命令：{arg}（用 --help 查看用法）");
                        return ExitEnvironment;
                    }
                    path ??= arg;
                    break;
            }
        }

        if (command is null)
        {
            // 只给了 --path 之类：打开 GUI 并预填目录
            StartupGameDir = path;
            return null;
        }

        if (command == "selftest")
        {
            return Selftest.Run() ? ExitOk : ExitVerifyFailed;
        }

        string? gameDir = ResolveGameDir(path);
        if (gameDir is null)
        {
            Console.Error.WriteLine("没有找到 X-Plane 12 目录。请用 --path 指定，例如：--path \"D:/Games/X-Plane 12\"");
            return ExitEnvironment;
        }

        var service = new PatchService(gameDir) { Log = Console.WriteLine };

        return command switch
        {
            "check" => RunCheck(service),
            "dry-run" => RunDryRun(service),
            "apply" => RunApply(service, force || yes),
            "verify" => RunVerify(service),
            "restore" => RunRestore(service, backup),
            "list-backups" => RunListBackups(service),
            "font-check" => RunFontCheck(service),
            _ => ExitEnvironment,
        };
    }

    private static string? NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"参数 {args[index]} 缺少取值");
            return null;
        }
        index++;
        return args[index];
    }

    private static string? ResolveGameDir(string? path)
    {
        return GameLocator.Find(path, Console.WriteLine);
    }

    private static int RunCheck(PatchService service)
    {
        var analysis = service.Analyze();
        if (analysis.FatalError is not null)
        {
            Console.Error.WriteLine(analysis.FatalError);
            return ExitEnvironment;
        }

        Console.WriteLine($"游戏目录 : {analysis.GameDir}");
        Console.WriteLine($"exe      : {analysis.ExePath}  ({analysis.ExeSize:N0} 字节)");
        Console.WriteLine($"sha256   : {analysis.Sha256}");
        Console.WriteLine($"版本     : {analysis.VersionText}");
        Console.WriteLine($"文本区间 : {analysis.RegionStart} .. {analysis.RegionEnd}（候选 {analysis.Candidates} 条）");
        Console.WriteLine($"词条总数 : {analysis.Entries}");
        Console.WriteLine($"已应用   : {analysis.Applied}");
        Console.WriteLine($"待应用   : {analysis.Pending}（exe 里还是英文）");
        Console.WriteLine($"缺失     : {analysis.Missing}");
        Console.WriteLine($"字体     : {(analysis.Font.Ok ? "自带中文字体正常" : "⚠ " + analysis.Font.Detail)}");
        if (!analysis.Font.Ok)
        {
            Console.WriteLine("           修复：Steam → 右键 X-Plane 12 → 属性 → 已安装文件 → 验证游戏文件完整性");
        }
        Console.WriteLine($"游戏运行 : {(analysis.GameRunning ? "是（" + analysis.GameRunningDetail + "）" : "否")}");
        Console.WriteLine($"备份     : {analysis.BackupCount} 份（{analysis.WorkDir}）");

        if (analysis.NeedsSecondaryConfirmation)
        {
            Console.WriteLine("注意     : 这个 exe 版本未在兼容清单中，打补丁需要二次确认（--force）。");
        }

        return analysis.Missing == 0 ? ExitOk : ExitRecoverable;
    }

    private static int RunDryRun(PatchService service)
    {
        var result = service.DryRun(out string reportPath);
        if (result is null) return ExitEnvironment;

        Console.WriteLine($"译文词条 : {result.Rows.Count - result.Skipped - result.TooLong - result.NotFound} 条命中");
        Console.WriteLine($"处理行数 : {result.Rows.Count}");
        Console.WriteLine($"写入成功 : {result.Patched}");
        Console.WriteLine($"放不下   : {result.TooLong}");
        Console.WriteLine($"跳过     : {result.Skipped}");
        Console.WriteLine($"未找到   : {result.NotFound}（已打过补丁时为正常）");
        Console.WriteLine($"报告     : {reportPath}");
        Console.WriteLine("[预演] 未写盘");
        return result.TooLong == 0 ? ExitOk : ExitRecoverable;
    }

    private static int RunApply(PatchService service, bool allowUnknown)
    {
        var outcome = service.Apply(allowUnknown);
        Console.WriteLine(outcome.Message);

        switch (outcome.Status)
        {
            case ApplyStatus.Applied:
                if (outcome.Engine is not null)
                {
                    Console.WriteLine($"写入成功 : {outcome.Engine.Patched} 条");
                    Console.WriteLine($"跳过     : {outcome.Engine.Skipped}");
                    Console.WriteLine($"放不下   : {outcome.Engine.TooLong}");
                    Console.WriteLine($"新 sha256: {outcome.PatchedSha256}");
                    service.StoreReport(outcome.Engine);
                }
                return ExitOk;

            case ApplyStatus.AlreadyPatched:
                return ExitOk;

            case ApplyStatus.NeedsSecondaryConfirmation:
                Console.Error.WriteLine("请确认后加 --force 重新执行。");
                return ExitRecoverable;

            case ApplyStatus.NothingToDo:
                if (outcome.Engine is not null) service.StoreReport(outcome.Engine);
                return ExitOk;

            default:
                return ExitRecoverable;
        }
    }

    private static int RunVerify(PatchService service)
    {
        var outcome = service.Verify();
        Console.WriteLine(outcome.Message);
        if (outcome.Report is not null)
        {
            Console.WriteLine($"对比备份 : {outcome.BackupName}");
            Console.WriteLine($"改动     : {outcome.Report.ChangedRanges} 段 / {outcome.Report.ChangedBytes:N0} 字节");
        }
        return outcome.Ok ? ExitOk : ExitVerifyFailed;
    }

    private static int RunRestore(PatchService service, string? backupName)
    {
        var outcome = service.Restore(backupName);
        Console.WriteLine(outcome.Message);
        if (outcome.Ok) Console.WriteLine($"sha256   : {outcome.RestoredSha256}");
        return outcome.Ok ? ExitOk : ExitRecoverable;
    }

    private static int RunListBackups(PatchService service)
    {
        var backups = service.ListBackups();
        Console.WriteLine($"备份目录 : {PathPolicy.BackupDir(service.GameDir)}");
        if (backups.Count == 0)
        {
            Console.WriteLine("  （空）——首次打补丁时会自动生成，或用 Steam 验证文件完整性");
            return ExitOk;
        }

        foreach (var backup in backups)
        {
            Console.WriteLine($"  {backup.Name}  {backup.Size:N0} 字节  {backup.Modified:yyyy-MM-dd HH:mm}");
        }
        return ExitOk;
    }

    private static int RunFontCheck(PatchService service)
    {
        var font = WineFontProbe.Check(service.GameDir);
        Console.WriteLine($"字体文件 : {font.FontPath}");
        Console.WriteLine($"状态     : {(font.Ok ? "正常" : "异常")}  {font.Detail}");
        if (font.FontExists)
        {
            Console.WriteLine($"大小     : {font.FontSize:N0} 字节");
            Console.WriteLine($"字形抽样 : {font.SamplesCovered}/{font.SamplesTotal}");
        }
        Console.WriteLine($"系统字体 : {(font.SystemCjkAvailable ? "有" : "无")}  {font.SystemDetail}");
        Console.WriteLine("说明     : 字幕使用游戏自带字体，系统字体只作参考，不影响补丁。");
        return font.Ok ? ExitOk : ExitRecoverable;
    }

    private static void PrintVersion()
    {
        var assembly = typeof(CommandLine).Assembly;
        string version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        Console.WriteLine($"X-Plane 12 飞行教学汉化 v{version}");
        Console.WriteLine("支持平台：Windows / Linux(Wine/Proton)");
    }

    private static void PrintHelp()
    {
        PrintVersion();
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  XPlane12-FlightLessons-zh-CN              打开图形界面");
        Console.WriteLine("  XPlane12-FlightLessons-zh-CN --path <目录> 打开图形界面并预填目录");
        Console.WriteLine();
        Console.WriteLine("无界面模式：");
        Console.WriteLine("  --selftest                 合成数据自检（不读游戏文件）");
        Console.WriteLine("  --check      [--path 目录] 查看词条现状");
        Console.WriteLine("  --dry-run    [--path 目录] 预演，不写盘");
        Console.WriteLine("  --apply      [--path 目录] [--force] 打补丁（自动备份 + 校验）");
        Console.WriteLine("  --verify     [--path 目录] 校验已打的补丁");
        Console.WriteLine("  --restore    [--path 目录] [--backup 文件名] 还原原版");
        Console.WriteLine("  --list-backups [--path 目录] 列出备份");
        Console.WriteLine("  --font-check [--path 目录] 检测游戏自带中文字体");
        Console.WriteLine("  --version / --help");
        Console.WriteLine();
        Console.WriteLine("退出码：0 成功 / 1 可恢复失败 / 2 校验失败 / 3 参数或环境错误");
    }
}
