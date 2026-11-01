using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HousingStudio.Session;

public enum LogKind
{
    Info,
    Done,
    Warn,
    Error,
}

public sealed record LogLine(DateTime At, LogKind Kind, string Text);

/// <summary>What the plugin did, newest last: shown in the window, copyable, and sent to Dalamud's log (/xllog).</summary>
public sealed class ActivityLog
{
    /// <summary>A whole errands run's worth (0.7.13 kept 500, and a long run lost its start before it could be copied).</summary>
    private const int Max = 5000;
    private readonly List<LogLine> lines = new();

    public IReadOnlyList<LogLine> Lines => lines;

    public void Add(LogKind kind, string text)
    {
        lines.Add(new LogLine(DateTime.Now, kind, text));
        // Trimmed in chunks (one line at a time moved the whole list on every line added).
        if (lines.Count > Max + 500) lines.RemoveRange(0, lines.Count - Max);
        switch (kind)
        {
            case LogKind.Error:
                Services.Log.Error(text);
                break;
            case LogKind.Warn:
                Services.Log.Warning(text);
                break;
            default:
                Services.Log.Information(text);
                break;
        }
    }

    public void Info(string text) => Add(LogKind.Info, text);
    public void Done(string text) => Add(LogKind.Done, text);
    public void Warn(string text) => Add(LogKind.Warn, text);
    public void Error(string text) => Add(LogKind.Error, text);

    public void Clear() => lines.Clear();

    public string AsText() =>
        lines.Aggregate(new StringBuilder(), (sb, l) => sb.Append(l.At.ToString("HH:mm:ss")).Append("  ").Append(l.Kind.ToString().ToUpperInvariant().PadRight(5)).Append(' ').AppendLine(l.Text)).ToString();
}
