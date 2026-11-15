using System;
using System.IO;

namespace XpLessonZhCn.Platform;

/// <summary>工作目录（抽取结果 / 补丁报告 / 备份）的位置与可写性判断。</summary>
public static class PathPolicy
{
    public const string WorkDirName = ".zh_cn_translate";

    public static string WorkDir(string gameDir) => Path.Combine(gameDir, WorkDirName);

    public static string DataDir(string gameDir) => Path.Combine(WorkDir(gameDir), "data");

    public static string BackupDir(string gameDir) => Path.Combine(WorkDir(gameDir), "backup");

    public static string EnsureDir(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string EnsureDataDir(string gameDir)
    {
        string dir = DataDir(gameDir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string EnsureBackupDir(string gameDir)
    {
        string dir = BackupDir(gameDir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>配置目录：Windows 用 %APPDATA%，Linux 用 $XDG_CONFIG_HOME。</summary>
    public static string ConfigDir()
    {
        if (OperatingSystem.IsWindows())
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "xplane12-zh-cn");
        }

        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg, "xplane12-zh-cn");
    }

    /// <summary>游戏目录不可写时的回落位置。</summary>
    public static string FallbackWorkDir()
    {
        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "xplane12-zh-cn");
        }

        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".local", "share") : xdg, "xplane12-zh-cn");
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
