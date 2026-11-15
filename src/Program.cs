using System;
using Avalonia;
using XpLessonZhCn.Platform;

namespace XpLessonZhCn;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 日志固定落在 {程序目录}/.zh_cn_translate/log.txt
        RunLog.Init();

        int? exitCode = Cli.CommandLine.TryRun(args);
        if (exitCode.HasValue) return exitCode.Value;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Gui.App>()
            .UsePlatformDetect()
            .LogToTrace();
}
