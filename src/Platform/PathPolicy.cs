using System;
using System.IO;

namespace XpLessonZhCn.Platform;

/// <summary>
/// 程序自己的工作目录：固定在「程序所在目录」下的 <c>.zh_cn_translate/</c>。
///
/// 便携设计：备份、报告、日志、配置全部在这里，**不写游戏目录、不写用户目录**。
/// 卸载时删掉整个文件夹即可，不影响游戏（游戏侧只有被替换过的 X-Plane.exe 和原版备份）。
/// </summary>
public static class PathPolicy
{
    public const string WorkDirName = ".zh_cn_translate";

    /// <summary>工作目录：{程序目录}/.zh_cn_translate</summary>
    public static string WorkDir => Path.Combine(AppContext.BaseDirectory, WorkDirName);

    public static string DataDir => Path.Combine(WorkDir, "data");

    public static string BackupDir => Path.Combine(WorkDir, "backup");

    public static string LogFile => Path.Combine(WorkDir, "log.txt");

    public static string SettingsFile => Path.Combine(WorkDir, "settings.json");

    public static string ReportFile => Path.Combine(DataDir, "patch_report.tsv");

    public static string EnsureDir(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>尝试在目录里创建并删除一个探针文件，判断是否可写。</summary>
    public static bool IsWritable(string directory, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool IsWritable(string directory) => IsWritable(directory, out _);
}
