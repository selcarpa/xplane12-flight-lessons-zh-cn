using System;
using System.IO;
using System.Text;
using XpLessonZhCn.Platform;

namespace XpLessonZhCn.Cli;

/// <summary>
/// 把控制台输出同时写一份到 log.txt（命令行模式下用）。
/// 这样用户跑 `--check` / `--apply` 时的每行输出都会落到日志文件里。
/// </summary>
internal sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly StringBuilder _line = new();

    public TeeTextWriter(TextWriter inner) => _inner = inner;

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        _inner.Write(value);

        if (value == '\n')
        {
            FlushLine();
        }
        else if (value != '\r')
        {
            _line.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        foreach (char ch in value) Write(ch);
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write(Environment.NewLine);
    }

    public override void WriteLine()
    {
        Write(Environment.NewLine);
    }

    public override void Flush()
    {
        _inner.Flush();
        if (_line.Length > 0)
        {
            RunLog.Write(_line.ToString());
            _line.Clear();
        }
    }

    private void FlushLine()
    {
        if (_line.Length == 0) return;
        RunLog.Write(_line.ToString());
        _line.Clear();
    }
}
