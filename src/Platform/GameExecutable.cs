using System;
using System.Collections.Generic;
using System.IO;
using XpLessonZhCn.Core;

namespace XpLessonZhCn.Platform;

/// <summary>
/// 可执行文件解析：自动检测游戏目录里存在的是哪个 X-Plane 可执行文件，
/// 并按魔数（MZ / ELF）识别其类型——Windows 版 X-Plane.exe 或 Linux 原生版 X-Plane-x86_64 / X-Plane。
/// </summary>
public sealed record GameExecutable(string FileName, ExecutableKind Kind);

public static class GameExecutableResolver
{
    /// <summary>Windows 上只认 X-Plane.exe（PE）。</summary>
    public static readonly string[] WindowsNames = { "X-Plane.exe" };

    /// <summary>
    /// Linux 上的候选顺序：原生版优先（X-Plane-x86_64 → X-Plane），
    /// 都没有才退回 X-Plane.exe（Steam/Proton、Wine 场景）。
    /// </summary>
    public static readonly string[] LinuxNames = { "X-Plane-x86_64", "X-Plane", "X-Plane.exe" };

    public static string[] CandidateNames =>
        OperatingSystem.IsWindows() ? WindowsNames : LinuxNames;

    /// <summary>目录里存在任意一个 X-Plane 可执行文件即视为游戏目录。</summary>
    public static bool IsGameDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;

        try
        {
            foreach (string name in CandidateNames)
            {
                if (File.Exists(Path.Combine(dir, name))) return true;

                // ext4 等大小写敏感文件系统上的兜底
                if (HasFileIgnoreCase(dir, name)) return true;
            }
        }
        catch (Exception)
        {
            // 目录不可读
        }
        return false;
    }

    /// <summary>
    /// 在游戏目录里解析可执行文件：按平台候选顺序找存在的文件，再读魔数判定类型。
    /// 返回 null 表示目录里没有任何 X-Plane 可执行文件。
    /// </summary>
    public static GameExecutable? Resolve(string gameDir)
    {
        foreach (string name in CandidateNames)
        {
            string path = Path.Combine(gameDir, name);
            if (!File.Exists(path) && !HasFileIgnoreCase(gameDir, name)) continue;

            string? actualPath = File.Exists(path) ? path : FindIgnoreCase(gameDir, name);
            if (actualPath is null) continue;

            try
            {
                using var stream = new FileStream(actualPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16);
                var header = new byte[4];
                int read = stream.Read(header, 0, header.Length);
                if (read < 4) continue;

                ExecutableKind? kind = ImageFormat.Detect(header);
                if (kind is null) continue;

                return new GameExecutable(Path.GetFileName(actualPath), kind.Value);
            }
            catch (Exception)
            {
                // 读不了（权限等）→ 跳过该候选
            }
        }

        return null;
    }

    private static bool HasFileIgnoreCase(string dir, string name)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception)
        {
            // 目录不可读
        }
        return false;
    }

    private static string? FindIgnoreCase(string dir, string name)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) return file;
            }
        }
        catch (Exception)
        {
            // 目录不可读
        }
        return null;
    }
}
