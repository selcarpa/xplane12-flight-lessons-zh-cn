using System;
using System.IO;
using System.Text;

namespace XpLessonZhCn.Platform;

/// <summary>
/// 运行日志：追加写入 <c>{程序目录}/.zh_cn_translate/log.txt</c>。
/// 出问题时让用户把这个文件发过来即可，不需要复现。
/// </summary>
public static class RunLog
{
    private const long MaxBytes = 4L * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string? FilePath => _path;

    /// <summary>初始化日志文件（程序启动时调用一次）。失败则静默降级为「不记日志」。</summary>
    public static void Init()
    {
        if (_path is not null) return;

        try
        {
            PathPolicy.EnsureDir(PathPolicy.WorkDir);
            string path = PathPolicy.LogFile;

            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                string previous = Path.Combine(PathPolicy.WorkDir, "log.1.txt");
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(path, previous);
            }

            _path = path;
            Write($"===== 会话开始 {DateTime.Now:yyyy-MM-dd HH:mm:ss} · 进程 {Environment.ProcessId} =====");
        }
        catch (Exception)
        {
            _path = null; // 程序目录只读时不记日志，不影响功能
        }
    }

    public static void Write(string message)
    {
        string? path = _path;
        if (path is null) return;

        lock (Gate)
        {
            try
            {
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}", new UTF8Encoding(false));
            }
            catch (Exception)
            {
                _path = null; // 写失败就停掉，避免每行都抛异常
            }
        }
    }
}
