using System;
using System.Collections.Generic;
using System.IO;

namespace XpLessonZhCn.Platform;

/// <summary>
/// 原子替换 X-Plane.exe。
///
/// 候选写入位置（按顺序尝试）：
///   1. Windows：游戏目录的上一级（例如 steamapps\common）—— 避开某些沙箱给游戏目录打
///      「低完整性标签」（Mandatory Label\Low），否则新 exe 会被以低权限运行、无法存档；
///   2. 游戏目录本身 —— 同卷、rename 原子，且「能替换 exe」就意味着这里通常可写；
///   3. 系统临时目录（最后手段；跨卷时 rename 会退化为复制）。
///
/// Linux：同目录临时文件 + chmod 0755 + rename(2)。**必须显式设置可执行位**，
/// 否则新文件默认为 644，游戏起不来。
///
/// 全部失败时抛出 <see cref="ExeWriteException"/>，由上层判断是不是权限问题并引导用户提权。
/// </summary>
public static class ExeWriter
{
    public static void WriteAtomically(string targetPath, byte[] data, Action<string>? log = null)
    {
        string fullTarget = Path.GetFullPath(targetPath);
        string targetDir = Path.GetDirectoryName(fullTarget) ?? throw new ArgumentException("目标路径无效", nameof(targetPath));
        string fileName = Path.GetFileName(fullTarget);

        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            string? parent = Directory.GetParent(targetDir)?.FullName;
            if (!string.IsNullOrEmpty(parent)) candidates.Add(Path.Combine(parent, fileName + ".stage"));
        }

        candidates.Add(Path.Combine(targetDir, fileName + ".stage"));
        candidates.Add(Path.Combine(Path.GetTempPath(), fileName + ".stage"));

        var attempts = new List<string>();
        Exception? lastError = null;

        foreach (string stage in candidates)
        {
            try
            {
                File.WriteAllBytes(stage, data);
                ApplyExecutableMode(stage);
                Replace(stage, fullTarget);
                ApplyExecutableMode(fullTarget);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                lastError = ex;
                attempts.Add($"{stage} → {ex.Message}");
                log?.Invoke($"写入尝试失败（{stage}）：{ex.Message}");
                TryDelete(stage);
            }
        }

        throw new ExeWriteException(
            $"无法写入 {fullTarget}。\n尝试过的位置：\n  - " + string.Join("\n  - ", attempts),
            IsAccessDenied(lastError),
            lastError);
    }

    /// <summary>先尝试原子替换；某些目录的 DENY(DELETE_CHILD) 会让替换失败，退化为直接覆盖写入。</summary>
    private static void Replace(string stage, string target)
    {
        try
        {
            // Windows: MoveFileEx(REPLACE_EXISTING)；Unix: rename(2)。都是原子替换。
            File.Move(stage, target, overwrite: true);
        }
        catch (UnauthorizedAccessException)
        {
            // 目录上可能有显式 DENY(DC)/DENY(DELETE)，但文件本身可写 → 直接覆盖内容
            File.Copy(stage, target, overwrite: true);
            TryDelete(stage);
        }
        catch (IOException)
        {
            File.Copy(stage, target, overwrite: true);
            TryDelete(stage);
        }
    }

    /// <summary>Linux/macOS 上把文件设为 0755。</summary>
    public static void ApplyExecutableMode(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            const UnixFileMode mode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception)
        {
            // 某些文件系统（exFAT 等）不支持权限位，忽略即可。
        }
    }

    /// <summary>判断异常是否由「没有权限」引起。</summary>
    public static bool IsAccessDenied(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is UnauthorizedAccessException) return true;

            // Windows: 0x80070005 = ERROR_ACCESS_DENIED / EACCES
            if ((ex.HResult & 0xFFFF) == 5) return true;

            string message = ex.Message;
            if (message.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("EACCES", StringComparison.Ordinal))
            {
                return true;
            }

            ex = ex.InnerException;
        }
        return false;
    }


    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // 清理失败不影响主流程
        }
    }
}

/// <summary>写 exe 失败；<see cref="IsPermissionProblem"/> 为 true 时上层应引导用户提权。</summary>
public sealed class ExeWriteException : IOException
{
    public ExeWriteException(string message, bool isPermissionProblem, Exception? inner)
        : base(message, inner)
    {
        IsPermissionProblem = isPermissionProblem;
    }

    public bool IsPermissionProblem { get; }
}
