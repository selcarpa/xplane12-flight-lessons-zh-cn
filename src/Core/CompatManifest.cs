using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XpLessonZhCn.Core;

public sealed class PatchStats
{
    public int Patched { get; set; }
    public int Skipped { get; set; }
    public int TooLong { get; set; }

    /// <summary>英文原文不在二进制里的条数（Linux 原生版与 Windows 版文案有差异，可为非 0）。</summary>
    public int NotFound { get; set; }
}

/// <summary>兼容清单里的一条版本记录。</summary>
public sealed class KnownVersion
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "verified";
    public long ExeSize { get; set; }
    public string Sha256 { get; set; } = "";
    public string PatchedSha256 { get; set; } = "";
    public int RegionStart { get; set; }
    public int RegionEnd { get; set; }
    public int Candidates { get; set; }
    public int Entries { get; set; }
    public PatchStats? PatchStats { get; set; }
    public string[] Platforms { get; set; } = Array.Empty<string>();
    public string? VerifiedOn { get; set; }
    public string? Notes { get; set; }

    [JsonIgnore]
    public bool IsVerified => string.Equals(Status, "verified", StringComparison.OrdinalIgnoreCase);
}

public sealed class CompatManifest
{
    public const string EmbeddedName = "compat.known-versions.json";

    public int SchemaVersion { get; set; }
    public string? Note { get; set; }
    public string? UpdatedAt { get; set; }
    public List<KnownVersion> Versions { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读取兼容清单：优先 exe 同目录的 compat/known-versions.json，否则用内置的。</summary>
    public static CompatManifest Load(string? externalPath, Action<string>? warn = null)
    {
        string json;
        if (!string.IsNullOrEmpty(externalPath) && File.Exists(externalPath))
        {
            json = File.ReadAllText(externalPath);
            warn?.Invoke($"使用外部兼容清单：{externalPath}");
        }
        else
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedName)
                ?? throw new PatchException($"程序内置兼容清单缺失（{EmbeddedName}），请重新下载。");
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }

        var manifest = JsonSerializer.Deserialize<CompatManifest>(json, Options)
            ?? throw new PatchException("兼容清单解析失败（内容为空）。");
        return manifest;
    }

    /// <summary>按原版 exe 的 sha256 查找。</summary>
    public KnownVersion? FindByOriginal(string sha256)
    {
        foreach (var version in Versions)
        {
            if (string.Equals(version.Sha256, sha256, StringComparison.OrdinalIgnoreCase)) return version;
        }
        return null;
    }

    /// <summary>按打补丁后的 sha256 查找（用于判断「已经打过补丁」）。</summary>
    public KnownVersion? FindByPatched(string sha256)
    {
        foreach (var version in Versions)
        {
            if (!string.IsNullOrEmpty(version.PatchedSha256) &&
                string.Equals(version.PatchedSha256, sha256, StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
        }
        return null;
    }
}

public static class Hashes
{
    public static string Sha256File(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
