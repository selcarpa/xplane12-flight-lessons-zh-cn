using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XpLessonZhCn.Platform;

/// <summary>记住上次使用的游戏目录。</summary>
public sealed class Settings
{
    public string? LastGameDir { get; set; }

    private static string FilePath => Path.Combine(PathPolicy.ConfigDir(), "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (settings is not null) return settings;
            }
        }
        catch (Exception)
        {
            // 配置损坏时忽略，用默认值
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // 记不住也不影响功能
        }
    }
}

/// <summary>
/// 自动定位 X-Plane 12 安装目录。
/// Windows：显式路径 → %XP12_ROOT% → 上次路径 → 注册表/常见 Steam 路径 + libraryfolders.vdf → 向上搜索。
/// Linux  ：显式路径 → $XP12_ROOT → 上次路径 → ~/.steam / ~/.local/share/Steam / Flatpak / Snap + vdf → 向上搜索。
/// </summary>
public static class GameLocator
{
    public const string ExeName = "X-Plane.exe";
    public const string GameFolderName = "X-Plane 12";

    public static bool IsGameDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        if (File.Exists(Path.Combine(dir, ExeName))) return true;

        // ext4 等大小写敏感文件系统上的兜底
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (string.Equals(Path.GetFileName(file), ExeName, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception)
        {
            // 目录不可读
        }
        return false;
    }

    public static string? Find(string? explicitPath, Action<string>? log = null)
    {
        foreach (string candidate in Candidates(explicitPath, log))
        {
            if (IsGameDir(candidate))
            {
                log?.Invoke($"已定位游戏目录：{candidate}");
                return candidate;
            }
        }
        return null;
    }

    public static List<string> Candidates(string? explicitPath, Action<string>? log = null)
    {
        var list = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(path);
                if (!list.Contains(full, StringComparer.OrdinalIgnoreCase)) list.Add(full);
            }
            catch (Exception)
            {
                // 非法路径直接忽略
            }
        }

        Add(explicitPath);
        Add(Environment.GetEnvironmentVariable("XP12_ROOT"));
        Add(Settings.Load().LastGameDir);

        foreach (string library in SteamLibraryRoots(log))
        {
            Add(Path.Combine(library, "steamapps", "common", GameFolderName));
            Add(Path.Combine(library, GameFolderName));
        }

        Add(SearchUpward(Environment.CurrentDirectory));
        Add(SearchUpward(AppContext.BaseDirectory));
        return list;
    }

    private static string? SearchUpward(string? start)
    {
        if (string.IsNullOrWhiteSpace(start)) return null;

        var dir = new DirectoryInfo(start);
        for (int depth = 0; dir is not null && depth < 12; depth++, dir = dir.Parent)
        {
            if (IsGameDir(dir.FullName)) return dir.FullName;
        }
        return null;
    }

    /// <summary>返回所有 Steam 库根目录（含 steamapps 的那一层）。</summary>
    public static List<string> SteamLibraryRoots(Action<string>? log = null)
    {
        var roots = new List<string>();
        var steamDirs = new List<string>();

        void AddSteamDir(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(path);
                if (Directory.Exists(full) && !steamDirs.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    steamDirs.Add(full);
                }
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            AddSteamDir(QueryRegistrySteamPath(log));
            AddSteamDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            AddSteamDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
            AddSteamDir(Path.Combine(home, "Steam"));
        }
        else
        {
            AddSteamDir(Path.Combine(home, ".steam", "steam"));
            AddSteamDir(Path.Combine(home, ".steam", "root"));
            AddSteamDir(Path.Combine(home, ".local", "share", "Steam"));
            AddSteamDir(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
            AddSteamDir(Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"));
        }

        foreach (string steamDir in steamDirs)
        {
            AddRoot(steamDir);
            foreach (string vdf in new[]
                     {
                         Path.Combine(steamDir, "steamapps", "libraryfolders.vdf"),
                         Path.Combine(steamDir, "config", "libraryfolders.vdf"),
                     })
            {
                foreach (string path in ReadLibraryFolders(vdf))
                {
                    AddRoot(path);
                }
            }
        }

        void AddRoot(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (Directory.Exists(full) && !roots.Contains(full, StringComparer.OrdinalIgnoreCase)) roots.Add(full);
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        return roots;
    }

    /// <summary>解析 libraryfolders.vdf 里的库路径（新旧两种格式都支持）。</summary>
    public static IEnumerable<string> ReadLibraryFolders(string vdfPath)
    {
        string text;
        try
        {
            if (!File.Exists(vdfPath)) yield break;
            text = File.ReadAllText(vdfPath);
        }
        catch (Exception)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 新格式："path"		"D:\\SteamLibrary"
        foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
        {
            string value = Unescape(match.Groups[1].Value);
            if (seen.Add(value)) yield return value;
        }

        // 旧格式："1"		"D:\\SteamLibrary"
        foreach (Match match in Regex.Matches(text, "\"\\d+\"\\s*\"([^\"]+)\""))
        {
            string value = Unescape(match.Groups[1].Value);
            if (seen.Add(value)) yield return value;
        }
    }

    private static string Unescape(string value) => value.Replace("\\\\", "\\");

    private static string? QueryRegistrySteamPath(Action<string>? log)
    {
        try
        {
            var psi = new ProcessStartInfo("reg.exe", "query HKCU\\Software\\Valve\\Steam /v SteamPath")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            if (process.ExitCode != 0) return null;

            var match = Regex.Match(output, "SteamPath\\s+REG_SZ\\s+(.+)");
            if (!match.Success) return null;

            string path = match.Groups[1].Value.Trim();
            return path.Replace('/', '\\');
        }
        catch (Exception ex)
        {
            log?.Invoke($"读取 Steam 注册表路径失败（忽略）：{ex.Message}");
            return null;
        }
    }
}
