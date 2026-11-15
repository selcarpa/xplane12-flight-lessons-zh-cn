using System;
using System.IO;
using System.Runtime.InteropServices;

namespace XpLessonZhCn.Cli;

/// <summary>
/// 程序以 Windows GUI 子系统（WinExe）编译，默认没有控制台。
/// 走命令行模式时把输出接回调用方控制台，这样 --selftest / --check 等命令能正常打印。
/// Linux 上不需要处理（进程本身就有标准输出）。
/// </summary>
internal static class ConsoleBridge
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private static bool _initialized;

    public static void Enable()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (Exception)
        {
            // 某些宿主不允许改编码，忽略
        }

        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (!AttachConsole(AttachParentProcess))
            {
                return; // 没有可附加的控制台（例如双击运行），忽略
            }

            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);

            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stderr);
        }
        catch (Exception)
        {
            // 接不上控制台不影响功能
        }
    }
}
