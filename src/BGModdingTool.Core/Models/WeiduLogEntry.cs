namespace BGModdingTool.Core.Models;

/// <summary>One installed component, as recorded in weidu.log.</summary>
public sealed record WeiduLogEntry(
    string Tp2,
    int LanguageIndex,
    int ComponentNumber,
    string? ComponentName,
    string? Version);
