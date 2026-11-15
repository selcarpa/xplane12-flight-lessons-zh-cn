using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace XpLessonZhCn.Core;

public sealed record Translation(string English, string Chinese);

/// <summary>
/// 译文表：默认用编译进 exe 的 translations/zh.tsv；
/// 若 exe 同目录存在 translations/zh.tsv，则优先用它（方便玩家自己改词条）。
/// 格式：英文&lt;TAB&gt;中文，行首 # 为注释。
/// </summary>
public static class Translations
{
    public const string EmbeddedName = "translations.zh.tsv";

    public static IReadOnlyList<Translation> Load(string? externalPath, Action<string>? warn = null)
    {
        string? text = null;

        if (!string.IsNullOrEmpty(externalPath) && File.Exists(externalPath))
        {
            text = File.ReadAllText(externalPath, new UTF8Encoding(false));
            warn?.Invoke($"使用外部译文表：{externalPath}");
        }
        else
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedName);
            if (stream is null)
            {
                throw new PatchException($"程序内置译文表缺失（{EmbeddedName}），请重新下载。");
            }
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            text = reader.ReadToEnd();
        }

        return Parse(text ?? "", warn);
    }

    public static IReadOnlyList<Translation> Parse(string text, Action<string>? warn = null)
    {
        var pairs = new List<Translation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        int lineNo = 0;
        foreach (var rawLine in SplitLines(text))
        {
            lineNo++;
            string line = rawLine;
            if (line.Length > 0 && line[0] == '\uFEFF') line = line[1..];

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith('#')) continue;

            string[] parts = line.Split('\t');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                warn?.Invoke($"译文表第 {lineNo} 行格式不对，已跳过");
                continue;
            }

            string english = parts[0];
            string chinese = parts[1];

            if (!seen.Add(english))
            {
                warn?.Invoke($"译文表第 {lineNo} 行重复词条，已跳过：{Truncate(english, 60)}");
                continue;
            }

            pairs.Add(new Translation(english, chinese));
        }

        return pairs;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    public static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    /// <summary>英文原文 → 译文；找不到返回 null。</summary>
    public static Dictionary<string, string> ToMap(IReadOnlyList<Translation> pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs) map[pair.English] = pair.Chinese;
        return map;
    }
}
