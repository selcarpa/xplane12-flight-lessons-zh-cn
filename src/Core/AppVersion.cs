using System;
using System.Reflection;

namespace XpLessonZhCn.Core;

/// <summary>程序版本号显示（优先用 InformationalVersion，这样 CI 从 tag 注入的 -snapshot 后缀也能显示）。</summary>
public static class AppVersion
{
    public static string Display { get; } = Compute();

    private static string Compute()
    {
        var assembly = typeof(AppVersion).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
