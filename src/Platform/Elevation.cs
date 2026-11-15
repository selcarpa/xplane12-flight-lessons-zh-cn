using System;
using System.ComponentModel;
using System.Diagnostics;

namespace XpLessonZhCn.Platform;

/// <summary>
/// 权限相关：判断是否已有管理员/root 权限，以及「以管理员身份重新启动」。
/// 只处理进程权限，不做任何文件系统提权（不调用 takeown/icacls）。
/// </summary>
public static class Elevation
{
    private static bool? _elevated;

    /// <summary>当前进程是否已有管理员（Windows）或 root（Linux）权限。</summary>
    public static bool IsElevated
    {
        get
        {
            if (_elevated is null)
            {
                try
                {
                    _elevated = Environment.IsPrivilegedProcess;
                }
                catch (Exception)
                {
                    _elevated = false;
                }
            }
            return _elevated.Value;
        }
    }

    /// <summary>能否弹出系统授权窗口以提升权限（只有 Windows 支持自动提权重启）。</summary>
    public static bool CanRequestElevation => OperatingSystem.IsWindows();

    /// <summary>
    /// 以管理员身份重新启动本程序（Windows 会弹 UAC 授权窗口）。
    /// 返回 true 表示新实例已启动，调用方应退出当前实例。
    /// </summary>
    public static bool TryRestartAsAdministrator(string? gameDir, out string error)
    {
        error = "";

        if (!OperatingSystem.IsWindows())
        {
            error = "当前平台不支持自动提权重启。";
            return false;
        }

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            error = "无法确定本程序路径。";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas", // 触发 UAC
                WorkingDirectory = AppContext.BaseDirectory,
            };

            if (!string.IsNullOrWhiteSpace(gameDir))
            {
                startInfo.Arguments = $"--path \"{gameDir}\"";
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "已取消权限请求（UAC 弹窗里点了「否」）。";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>手动提权的提示文案（自动提权失败或平台不支持时给用户）。</summary>
    public static string ManualHint(string? gameDir, string? exePath = null)
    {
        string target = string.IsNullOrWhiteSpace(gameDir) ? "<游戏目录>" : gameDir!;

        if (OperatingSystem.IsWindows())
        {
            return "右键本程序 → 「以管理员身份运行」，然后重新点「打补丁」。";
        }

        string exe = string.IsNullOrWhiteSpace(exePath) ? "./XPlane12-FlightLessons-zh-CN" : exePath!;
        return $"在终端里用 sudo 重新运行：\n\n  sudo {exe} --path \"{target}\"";
    }
}
