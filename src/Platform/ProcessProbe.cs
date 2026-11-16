using System;
using System.Diagnostics;
using System.IO;

namespace XpLessonZhCn.Platform;

/// <summary>检测游戏是否正在运行（打补丁前必须为 false，否则可能写坏正在被占用的 exe）。</summary>
public static class ProcessProbe
{
    /// <summary>
    /// Linux 上「算 X-Plane 进程」的名字：X-Plane、X-Plane.exe（Wine/Proton）、X-Plane-x86_64（原生）。
    /// 注意不能用宽泛的「名字里含 X-Plane」——工具自己的 exe 叫 XPlane12-FlightLessons-zh-CN，
    /// 也不能把单独存在的 wineserver / proton 当成游戏（旧实现会因此把按钮一直置灰）。
    /// </summary>
    public static bool IsGameProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string name = Path.GetFileName(value.Trim());
        return name.Equals("X-Plane", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("X-Plane.", StringComparison.OrdinalIgnoreCase)   // X-Plane.exe
            || name.StartsWith("X-Plane-", StringComparison.OrdinalIgnoreCase); // X-Plane-x86_64
    }

    /// <summary>Wine / Proton 相关进程：它们单独存在不算游戏在运行，还要看它是否真的加载了 X-Plane。</summary>
    public static bool IsWineProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string name = Path.GetFileName(value.Trim());
        return name.Contains("wine", StringComparison.OrdinalIgnoreCase)
            || name.Contains("proton", StringComparison.OrdinalIgnoreCase);
    }

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

        int self = Environment.ProcessId;

        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            string pidText = Path.GetFileName(dir);
            if (pidText.Length == 0 || !char.IsDigit(pidText[0])) continue;
            if (!int.TryParse(pidText, out int pid) || pid == self) continue;

            // 僵尸进程不持有文件句柄，不能算「正在运行」（实测残留的 X-Plane.exe 僵尸会让按钮一直灰）
            if (ReadProcState(dir) == "Z") continue;

            string? comm = TryReadText(Path.Combine(dir, "comm"))?.Trim();

            if (IsGameProcessName(comm))
            {
                detail = $"检测到进程 {comm} (PID {pid})";
                return true;
            }

            string? exeName = TryGetLinkFileName(Path.Combine(dir, "exe"));
            if (IsGameProcessName(exeName))
            {
                detail = $"检测到进程 {exeName} (PID {pid})";
                return true;
            }

            // Wine/Proton：只有命令行（Proton 启动链）或内存映射里确实有 X-Plane 可执行文件才算。
            // 单独的 wineserver / wine64-preloader / proton 可能是别的游戏或残留进程，不作为依据。
            if (!IsWineProcessName(comm) && !IsWineProcessName(exeName)) continue;

            if (ContainsXPlane(Path.Combine(dir, "cmdline")) || ContainsXPlane(Path.Combine(dir, "maps")))
            {
                string name = string.IsNullOrEmpty(comm) ? exeName ?? "wine" : comm;
                detail = $"检测到 Wine/Proton 进程 {name} (PID {pid}) 正在加载 X-Plane";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 读 /proc/&lt;pid&gt;/stat 的进程状态（第 3 个字段）；comm 里可能带空格/括号，按最后一个 ')' 切。
    /// </summary>
    private static string? ReadProcState(string procDir)
    {
        string? stat = TryReadText(Path.Combine(procDir, "stat"));
        if (string.IsNullOrEmpty(stat)) return null;

        int close = stat.LastIndexOf(')');
        if (close < 0 || close + 2 >= stat.Length) return null;
        return stat.Substring(close + 2, 1);
    }

    private static bool ContainsXPlane(string procFile)
    {
        string? raw = TryReadText(procFile);
        if (string.IsNullOrEmpty(raw)) return false;

        // cmdline 用 \0 分隔；maps 是普通文本。统一按空格处理后做大小写不敏感子串匹配。
        string text = raw.Replace('\0', ' ');
        return text.Contains("X-Plane", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception)
        {
            // 进程可能已退出，或无权限读取
            return null;
        }
    }

    private static string? TryGetLinkFileName(string path)
    {
        try
        {
            var link = new FileInfo(path).LinkTarget;
            return string.IsNullOrEmpty(link) ? null : Path.GetFileName(link);
        }
        catch (Exception)
        {
            // LinkTarget 在无权限时抛异常
            return null;
        }
    }
}
