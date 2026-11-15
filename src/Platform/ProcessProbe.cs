using System;
using System.Diagnostics;
using System.IO;

namespace XpLessonZhCn.Platform;

/// <summary>检测游戏是否正在运行（打补丁前必须为 false，否则可能写坏正在被占用的 exe）。</summary>
public static class ProcessProbe
{
    private static readonly string[] LinuxMarkers =
    {
        "X-Plane.exe",
        "wineserver",
        "wine64-preloader",
        "proton",
    };

    public static bool IsGameRunning(out string detail)
    {
        detail = "";
        try
        {
            if (OperatingSystem.IsWindows()) return CheckWindows(out detail);
            return CheckLinux(out detail);
        }
        catch (Exception ex)
        {
            detail = $"检测进程时出错（按未运行处理）：{ex.Message}";
            return false;
        }
    }

    private static bool CheckWindows(out string detail)
    {
        detail = "";
        foreach (string name in new[] { "X-Plane" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                detail = $"检测到进程 {process.ProcessName} (PID {process.Id})";
                return true;
            }
        }
        return false;
    }

    private static bool CheckLinux(out string detail)
    {
        detail = "";
        if (!Directory.Exists("/proc")) return false;

        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            string name = Path.GetFileName(dir);
            if (name.Length == 0 || !char.IsDigit(name[0])) continue;

            string? comm = null;
            try
            {
                comm = File.ReadAllText(Path.Combine(dir, "comm")).Trim();
            }
            catch (Exception)
            {
                // 进程可能已退出
            }

            if (!string.IsNullOrEmpty(comm) && ContainsMarker(comm))
            {
                detail = $"检测到进程 {comm} (PID {name})";
                return true;
            }

            try
            {
                var link = new FileInfo(Path.Combine(dir, "exe")).LinkTarget;
                if (!string.IsNullOrEmpty(link))
                {
                    string fileName = Path.GetFileName(link);
                    if (ContainsMarker(fileName))
                    {
                        detail = $"检测到进程 {fileName} (PID {name})";
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // LinkTarget 在无权限时抛异常，忽略
            }
        }

        return false;
    }

    private static bool ContainsMarker(string value)
    {
        foreach (string marker in LinuxMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
