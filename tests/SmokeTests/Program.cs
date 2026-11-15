using System;
using Avalonia;
using Avalonia.Headless;
using XpLessonZhCn.Cli;
using XpLessonZhCn.Gui;

namespace SmokeTests;

/// <summary>
/// CI / 本地冒烟测试入口（不需要游戏文件）：
/// 1. 跑补丁引擎自检（--selftest 用的同一套）；
/// 2. 在 Avalonia headless 平台下构造并显示主窗口，确认 GUI 能正常初始化。
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("=== 1/2 核心逻辑自检 ===");
        bool selftestOk = Selftest.Run();
        if (!selftestOk)
        {
            Console.WriteLine("自检失败");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("=== 2/2 GUI headless 启动 ===");
        try
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .SetupWithoutStarting();

            var window = new MainWindow(args.Length > 0 ? args[0] : null);
            window.Show();

            Console.WriteLine($"  ok  主窗口构造并 Show() 成功（{window.Title}，{window.Width}×{window.Height}）");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  FAIL  GUI 启动失败：" + ex);
            return 1;
        }
    }
}
