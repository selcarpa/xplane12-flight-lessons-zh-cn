using System;

namespace XpLessonZhCn.Core;

/// <summary>可执行文件格式。同一套字节补丁逻辑同时支持 Windows PE 与 Linux 原生 ELF。</summary>
public enum ExecutableKind
{
    /// <summary>Windows 版 X-Plane.exe（MZ/PE）。</summary>
    Pe,

    /// <summary>Linux 原生版 X-Plane-x86_64 / X-Plane（ELF）。</summary>
    Elf,
}

/// <summary>
/// 按魔数自动检测可执行文件类型（不依赖文件扩展名）。
/// PE：0x4D 0x5A（MZ）；ELF：0x7F 'E' 'L' 'F'。
/// </summary>
public static class ImageFormat
{
    public static ExecutableKind? Detect(byte[] blob)
    {
        if (blob is null || blob.Length < 4) return null;

        if (blob[0] == 0x4D && blob[1] == 0x5A) return ExecutableKind.Pe;
        if (blob[0] == 0x7F && blob[1] == 0x45 && blob[2] == 0x4C && blob[3] == 0x46) return ExecutableKind.Elf;

        return null;
    }

    /// <summary>面向用户的可执行文件类型描述（带文件名）。</summary>
    public static string KindLabel(ExecutableKind kind, string fileName) => kind switch
    {
        ExecutableKind.Pe => $"Windows PE（{fileName}）",
        ExecutableKind.Elf => $"Linux 原生 ELF（{fileName}）",
        _ => $"未知格式（{fileName}）",
    };
}
