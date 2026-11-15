using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace XpLessonZhCn.Platform;

public sealed class FontProbeResult
{
    public string FontPath { get; set; } = "";
    public bool FontExists { get; set; }
    public bool SizeOk { get; set; }
    public bool MagicOk { get; set; }
    public bool CmapOk { get; set; }
    public long FontSize { get; set; }
    public int SamplesCovered { get; set; }
    public int SamplesTotal { get; set; }
    public bool SystemCjkAvailable { get; set; }
    public string SystemDetail { get; set; } = "";
    public string Detail { get; set; } = "";

    /// <summary>游戏自带中文字体可用（这是字幕能否显示中文的关键）。</summary>
    public bool Ok => FontExists && SizeOk && MagicOk && CmapOk;
}

/// <summary>
/// Wine 场景的字体检测（只检测 + 提示，绝不安装字体、不修改 Proton prefix）。
///
/// 依据：X-Plane 12 自带 Resources/fonts/NotoSansCJK-SC-Regular.otf，
/// 并在 exe 里硬编码引用了该文件名，因此字幕渲染不依赖系统字体。
/// 这里只做兜底：字体文件缺失/损坏时，给出「Steam 验证文件完整性」的提示。
/// </summary>
public static class WineFontProbe
{
    public const string GameFontRelativePath = "Resources/fonts/NotoSansCJK-SC-Regular.otf";
    public const long MinFontSize = 15L * 1024 * 1024;

    private const string Samples = "这就是中国飞行教学起飞着陆襟翼航向配平节流阀";

    public static FontProbeResult Check(string gameDir)
    {
        var result = new FontProbeResult
        {
            FontPath = Path.Combine(gameDir, "Resources", "fonts", "NotoSansCJK-SC-Regular.otf"),
            SamplesTotal = Samples.Length,
        };

        var info = new FileInfo(result.FontPath);
        result.FontExists = info.Exists;

        if (!result.FontExists)
        {
            result.Detail = "游戏自带的中文字体文件不存在";
        }
        else
        {
            result.FontSize = info.Length;
            result.SizeOk = info.Length >= MinFontSize;

            try
            {
                using var stream = new FileStream(result.FontPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                var header = new byte[4];
                if (stream.Read(header, 0, 4) == 4)
                {
                    result.MagicOk = header[0] == (byte)'O' && header[1] == (byte)'T' &&
                                     header[2] == (byte)'T' && header[3] == (byte)'O';
                }

                if (result.MagicOk)
                {
                    stream.Position = 0;
                    var data = new byte[stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = stream.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }

                    var cmap = SfntCmap.TryOpen(data);
                    if (cmap is not null)
                    {
                        int covered = 0;
                        foreach (char ch in Samples)
                        {
                            if (cmap.HasCodePoint(ch)) covered++;
                        }
                        result.SamplesCovered = covered;
                        result.CmapOk = covered == Samples.Length;
                    }
                    else
                    {
                        result.Detail = "无法解析字体 cmap 表";
                    }
                }
                else
                {
                    result.Detail = "字体文件头不是 OTTO（文件可能损坏）";
                }
            }
            catch (Exception ex)
            {
                result.Detail = $"读取字体文件失败：{ex.Message}";
            }

            if (result.Ok)
            {
                result.Detail = $"自带中文字体正常（{result.FontSize / 1024 / 1024.0:F1} MB，抽样 {result.SamplesCovered}/{result.SamplesTotal} 字形齐全）";
            }
            else if (string.IsNullOrEmpty(result.Detail))
            {
                result.Detail = result.SizeOk ? "字体文件异常" : $"字体文件偏小（{result.FontSize:N0} 字节）";
            }
        }

        CheckSystemCjk(result);
        return result;
    }

    private static void CheckSystemCjk(FontProbeResult result)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                CheckWindowsFonts(result);
                return;
            }
            CheckLinuxFonts(result);
        }
        catch (Exception ex)
        {
            result.SystemDetail = $"检测系统字体失败：{ex.Message}";
        }
    }

    private static void CheckWindowsFonts(FontProbeResult result)
    {
        string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] candidates =
        {
            "msyh.ttc", "msyhbd.ttc", "simhei.ttf", "simsun.ttc", "msjh.ttc",
            "Deng.ttf", "NotoSansCJK-Regular.ttc", "SourceHanSansSC-Regular.otf",
        };

        var found = new List<string>();
        foreach (string name in candidates)
        {
            if (File.Exists(Path.Combine(fontsDir, name))) found.Add(name);
        }

        result.SystemCjkAvailable = found.Count > 0;
        result.SystemDetail = found.Count > 0
            ? $"系统已安装中文字体：{string.Join("、", found)}"
            : "系统未检测到常见中文字体";
    }

    private static void CheckLinuxFonts(FontProbeResult result)
    {
        try
        {
            var psi = new ProcessStartInfo("fc-list", ":lang=zh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is not null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    string first = output.Split('\n')[0];
                    int count = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                    result.SystemCjkAvailable = true;
                    result.SystemDetail = $"fontconfig 报告 {count} 个中文字体（例：{first.Trim()}）";
                    return;
                }

                result.SystemCjkAvailable = false;
                result.SystemDetail = "fontconfig 未找到中文字体（游戏自带字体仍可正常显示字幕）";
                return;
            }
        }
        catch (Exception)
        {
            // fc-list 不存在，继续用目录扫描兜底
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] dirs =
        {
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            Path.Combine(home, ".fonts"),
            Path.Combine(home, ".local", "share", "fonts"),
        };
        string[] markers = { "notosanscjk", "sourcehansans", "wqy", "droidsansfallback", "simhei", "simsun", "msyh" };

        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(file).ToLowerInvariant();
                    foreach (string marker in markers)
                    {
                        if (name.Contains(marker))
                        {
                            result.SystemCjkAvailable = true;
                            result.SystemDetail = $"在 {dir} 找到中文字体：{Path.GetFileName(file)}";
                            return;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 权限问题，继续
            }
        }

        result.SystemCjkAvailable = false;
        result.SystemDetail = "未找到中文字体文件（游戏自带字体仍可正常显示字幕）";
    }
}
