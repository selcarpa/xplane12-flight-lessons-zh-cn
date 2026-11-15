using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace XpLessonZhCn.Core;

/// <summary>
/// 一条译文。Windows 与 Linux 原生版**共用同一条译文**（尽量保持两平台显示一致）。
/// <see cref="BudgetBytes"/> 是该词条在目标二进制里允许写入的最大 UTF-8 字节数
/// （取当前验证版本里最紧的发生位置：Linux 原生版优先；Linux 无独立出现时取 Windows 版），
/// 仅作维护记录与静态校验用；运行时仍以实际二进制的 safe_span 为准。
/// </summary>
public sealed record Translation(string English, string Chinese, int BudgetBytes);

/// <summary>
/// 译文表：Windows 与 Linux 原生版共用 translations/zh.csv（CSV 维护，方便表格软件编辑）。
/// 列：english（英文原文）, chinese（中文译文）, budget_bytes（允许写入的最大字节数）。
/// 默认用编译进 exe 的表；exe 同目录存在 translations/zh.csv 时优先用它（方便玩家自改）。
/// 格式：标准 CSV（全字段可加双引号，双引号转义为两个双引号）；第一行为表头；
/// 行首 # 的行按注释跳过。
/// </summary>
public static class Translations
{
    public const string EmbeddedName = "translations.zh.csv";

    public const string HeaderEnglish = "english";

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

        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        bool headerSkipped = false;
        foreach (var (lineNo, fields) in ParseCsv(text))
        {
            if (fields.Count == 0) continue;

            string english = fields[0];
            if (!headerSkipped && string.Equals(english.Trim(), HeaderEnglish, StringComparison.OrdinalIgnoreCase))
            {
                headerSkipped = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(english)) continue;
            if (english.TrimStart().StartsWith('#')) continue; // 兼容 # 注释行

            if (fields.Count < 3 || string.IsNullOrWhiteSpace(fields[1]) ||
                string.IsNullOrWhiteSpace(fields[2]))
            {
                warn?.Invoke($"译文表第 {lineNo} 行格式不对（需要 english,chinese,budget_bytes 三列），已跳过");
                continue;
            }

            if (fields.Count > 3)
            {
                warn?.Invoke($"译文表第 {lineNo} 行列数超过 3，多余的列已忽略");
            }

            string chinese = fields[1];
            if (!int.TryParse(fields[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int budget) ||
                budget <= 0)
            {
                warn?.Invoke($"译文表第 {lineNo} 行的上限不是正整数，已跳过");
                continue;
            }

            if (!seen.Add(english))
            {
                warn?.Invoke($"译文表第 {lineNo} 行重复词条，已跳过：{Truncate(english, 60)}");
                continue;
            }

            pairs.Add(new Translation(english, chinese, budget));
        }

        return pairs;
    }

    /// <summary>最小 CSV 解析：支持双引号包裹、逗号、换行、"" 转义；返回行号与字段。</summary>
    private static IEnumerable<(int LineNo, List<string> Fields)> ParseCsv(string text)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int line = 1;
        int startLine = 1;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (ch == '\n') line++;
                    field.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return (startLine, fields);
                    fields = new List<string>();
                    line++;
                    startLine = line;
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return (startLine, fields);
        }
    }

    public static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

}
