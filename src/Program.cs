using System;
using Avalonia;

namespace XpLessonZhCn;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
