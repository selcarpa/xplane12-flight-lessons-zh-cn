using System;
using System.IO;

namespace XpLessonZhCn.Platform;

/// <summary>
/// 原子替换 X-Plane.exe。
///
/// Windows：先写到「游戏目录的上一级」再 rename 回来，避免新文件继承目录的
/// 「低完整性标签」（Mandatory Label\Low）——否则游戏会被以低权限启动、无法存档。
/// Linux  ：同目录临时文件 + chmod 0755 + rename(2)。**必须显式设置可执行位**，
/// 否则新文件默认为 644，游戏起不来。
/// </summary>
public static class ExeWriter
{
    public static void WriteAtomically(string targetPath, byte[] data, Action<string>? log = null)
    {
        string fullTarget = Path.GetFullPath(targetPath);
        string targetDir = Path.GetDirectoryName(fullTarget) ?? throw new ArgumentException("目标路径无效", nameof(targetPath));
        string fileName = Path.GetFileName(fullTarget);

        var candidates = new System.Collections.Generic.List<string>();

        if (!OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(targetDir, fileName + ".stage"));
        }
        else
        {
            string? parent = Directory.GetParent(targetDir)?.FullName;
            if (!string.IsNullOrEmpty(parent)) candidates.Add(Path.Combine(parent, fileName + ".stage"));
            candidates.Add(Path.Combine(Path.GetTempPath(), fileName + ".stage"));
        }

        Exception? lastError = null;
        foreach (string stage in candidates)
        {
            try
            {
                File.WriteAllBytes(stage, data);
                ApplyExecutableMode(stage);

                // .NET 在 Windows 上用 MoveFileEx(REPLACE_EXISTING)，在 Unix 上用 rename(2)：都是原子替换。
                File.Move(stage, fullTarget, overwrite: true);
                ApplyExecutableMode(fullTarget);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                lastError = ex;
                TryDelete(stage);
                log?.Invoke($"写入尝试失败（{stage}）：{ex.Message}");
            }
        }

        throw new IOException(
            $"无法写入 {fullTarget}：{lastError?.Message ?? "未知错误"}。请确认游戏没有在运行，且没有权限限制。",
            lastError);
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
