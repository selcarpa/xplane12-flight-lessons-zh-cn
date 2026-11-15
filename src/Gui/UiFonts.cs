using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace XpLessonZhCn.Gui;

/// <summary>挑选一个能显示中文的界面字体；都找不到时退回默认字体并在日志里提示。</summary>
internal static class UiFonts
{
    private static readonly string[] Preferred =
    {
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "Source Han Sans SC",
        "WenQuanYi Micro Hei",
        "WenQuanYi Zen Hei",
        "Droid Sans Fallback",
        "SimHei",
        "PingFang SC",
        "Segoe UI",
        "Inter",
    };

    private static FontFamily? _main;
    private static bool? _cjkAvailable;

    public static FontFamily Main => _main ??= Pick();

    /// <summary>系统里是否有可用中文字体（用于提示，游戏字幕用的是游戏自带字体，不受影响）。</summary>
    public static bool CjkAvailable
    {
        get
        {
            if (_cjkAvailable is null)
            {
                try
                {
                    var names = new HashSet<string>(
                        FontManager.Current.SystemFonts.Select(f => f.Name),
                        StringComparer.OrdinalIgnoreCase);
                    _cjkAvailable = Preferred.Take(9).Any(names.Contains);
                }
                catch (Exception)
                {
                    _cjkAvailable = true; // 检测失败就不吓唬用户
                }
            }
            return _cjkAvailable.Value;
        }
    }

    private static FontFamily Pick()
    {
        try
        {
            var installed = new HashSet<string>(
                FontManager.Current.SystemFonts.Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (string name in Preferred)
            {
                if (installed.Contains(name)) return new FontFamily(name);
            }
        }
        catch (Exception)
        {
            // 拿不到字体列表时用默认值
        }
        return FontFamily.Default;
    }
}
