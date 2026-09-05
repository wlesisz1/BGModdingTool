using System;
using System.Text.RegularExpressions;
using Avalonia.Logging;
using BGModdingTool.Core.Services;

namespace BGModdingTool.App.Services;

/// <summary>
/// Routes Avalonia's own warnings/errors (binding failures, exceptions thrown
/// inside property setters or event handlers that Avalonia swallows) into the
/// app log, so "nothing happened / it crashed" reports leave a trace.
/// </summary>
public sealed partial class AvaloniaLogSink : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        => Write(level, area, source, messageTemplate);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        var i = 0;
        var message = Placeholder().Replace(messageTemplate,
            _ => i < propertyValues.Length ? propertyValues[i++]?.ToString() ?? "null" : "?");
        Write(level, area, source, message);
    }

    private static void Write(LogEventLevel level, string area, object? source, string message)
    {
        // "Selected.X at Selected: Value is null" is normal while nothing is selected — pure noise.
        if (area == LogArea.Binding && message.EndsWith("Value is null.", StringComparison.Ordinal)) return;
        var text = $"avalonia[{area}] {source?.GetType().Name}: {message}";
        if (level >= LogEventLevel.Error) AppLog.Error(text, null);
        else AppLog.Warn(text, null);
    }

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Placeholder();
}
