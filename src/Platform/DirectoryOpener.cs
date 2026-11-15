using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XpLessonZhCn.Platform;

/// <summary>
/// 在系统文件管理器里打开一个目录（GUI「打开备份目录」按钮用）。
///
/// 不能直接 <c>Process.Start("xdg-open", …)</c>：.NET 在 Unix 上会先解析相对程序名，
/// 解析过程会调用 <see cref="Directory.GetCurrentDirectory"/>。便携程序可能从
/// 「随后被删除的目录」启动（临时解压目录、自动化测试夹具），当前目录失效后这一步会抛
/// <c>FileNotFoundException: Unable to find the specified file.</c>，表现为「打开目录失败」。
/// 所以这里自己按 PATH 解析出**绝对路径**再启动，并显式设置子进程的工作目录，
/// 完全不依赖当前工作目录。
/// </summary>
public static class DirectoryOpener
{
    /// <summary>打开结果；失败时 <see cref="Error"/> 是给日志看的原因。</summary>
    public readonly record struct Result(bool Ok, string? Error)
    {
        public static Result Success => new(true, null);
        public static Result Fail(string error) => new(false, error);
    }

    /// <summary>Unix 上按优先级尝试的「打开目录」启动器。</summary>
    private static readonly (string Program, string[] ExtraArgs)[] UnixLaunchers =
    {
        ("xdg-open", Array.Empty<string>()),
        ("gio", new[] { "open" }),          // 没有 xdg-utils 的精简桌面
        ("kde-open5", Array.Empty<string>()),
        ("kde-open", Array.Empty<string>()),
        ("gnome-open", Array.Empty<string>()),
    };

    /// <summary>启动器多久没退出就认为「已经打开/已在运行」，不再等它。</summary>
    private const int LauncherWaitMilliseconds = 2000;

    /// <summary>确保目录存在后在文件管理器里打开它。不会抛异常。</summary>
    public static async Task<Result> OpenAsync(string directory)
    {
        // 兜底：任何意外都转成可展示的失败原因，别让「打开目录」把 GUI 带崩
        try
        {
            return await OpenCoreAsync(directory).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    private static async Task<Result> OpenCoreAsync(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            return Result.Fail($"无法创建目录：{ex.Message}");
        }

        if (OperatingSystem.IsWindows()) return OpenOnWindows(directory);

        var failures = new List<string>();
        foreach ((string program, string[] extraArgs) in UnixLaunchers)
        {
            string? executable = ResolveProgram(program);
            if (executable is null) continue;

            Result result = await RunLauncherAsync(executable, extraArgs, directory).ConfigureAwait(false);
            if (result.Ok) return Result.Success;
            failures.Add($"{program}: {result.Error}");
        }

        return Result.Fail(failures.Count == 0
            ? "系统里没有找到 xdg-open / gio 等文件管理器启动器（可能没有图形桌面）。"
            : string.Join("；", failures));
    }

    /// <summary>
    /// 构建「打开目录」的启动信息（自检用）。
    /// Unix 上返回的 <see cref="ProcessStartInfo.FileName"/> 一定是绝对路径，
    /// 且 <see cref="ProcessStartInfo.WorkingDirectory"/> 固定为要打开的目录——
    /// 这两点是「当前目录失效时仍能打开」的关键。
    /// </summary>
    internal static bool TryBuildStartInfo(string directory, out ProcessStartInfo? startInfo, out string? error)
    {
        startInfo = null;
        error = null;

        if (OperatingSystem.IsWindows())
        {
            startInfo = BuildWindowsStartInfo(directory);
            return true;
        }

        foreach ((string program, string[] extraArgs) in UnixLaunchers)
        {
            string? executable = ResolveProgram(program);
            if (executable is null) continue;

            startInfo = BuildUnixStartInfo(executable, extraArgs, directory);
            return true;
        }

        error = "系统里没有找到 xdg-open / gio 等文件管理器启动器（可能没有图形桌面）。";
        return false;
    }

    private static Result OpenOnWindows(string directory)
    {
        try
        {
            Process.Start(BuildWindowsStartInfo(directory));
            return Result.Success;
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    private static ProcessStartInfo BuildWindowsStartInfo(string directory)
    {
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!File.Exists(explorer)) explorer = "explorer.exe"; // 回落到系统目录搜索

        return new ProcessStartInfo(explorer, $"\"{directory}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = directory,
        };
    }

    private static ProcessStartInfo BuildUnixStartInfo(string executable, string[] extraArgs, string directory)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };
        foreach (string arg in extraArgs) startInfo.ArgumentList.Add(arg);
        startInfo.ArgumentList.Add(directory);
        return startInfo;
    }

    private static async Task<Result> RunLauncherAsync(string executable, string[] extraArgs, string directory)
    {
        try
        {
            using Process? process = Process.Start(BuildUnixStartInfo(executable, extraArgs, directory));
            if (process is null) return Result.Fail("进程没有启动");

            using var timeout = new CancellationTokenSource(LauncherWaitMilliseconds);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 有的启动器（如文件管理器本体）会一直挂着不退出；能启动就算打开成功
                return Result.Success;
            }

            return process.ExitCode == 0
                ? Result.Success
                : Result.Fail($"退出码 {process.ExitCode}");
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 在 PATH 里找可执行文件的**绝对路径**。相对的 PATH 条目直接跳过：
    /// 解析相对路径要依赖当前目录，而本类存在的意义就是摆脱这个依赖。
    /// </summary>
    private static string? ResolveProgram(string program)
    {
        if (Path.IsPathRooted(program)) return IsExecutableFile(program) ? program : null;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        foreach (string entry in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir = entry.Trim();
            if (!Path.IsPathRooted(dir)) continue;

            string candidate = Path.Combine(dir, program);
            if (IsExecutableFile(candidate)) return candidate;
        }

        return null;
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (OperatingSystem.IsWindows()) return true;

            UnixFileMode mode = File.GetUnixFileMode(path);
            const UnixFileMode anyExecute =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & anyExecute) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
