using System.Text.Json.Serialization;

namespace BGModdingTool.Core.Models;

/// <summary>A managed, modifiable copy of a game installation.</summary>
public sealed class GameInstance
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required GameType GameType { get; init; }
    /// <summary>Original game directory this instance was copied from (never modified).</summary>
    public required string SourcePath { get; init; }
    /// <summary>Root of the instance (contains game/, snapshots/, instance.json).</summary>
    public required string RootPath { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? Notes { get; set; }

    /// <summary>Snapshot the game folder was last brought to (taken or restored); null = unknown.</summary>
    public string? CurrentSnapshotId { get; set; }
    public string? CurrentSnapshotLabel { get; set; }
    public DateTimeOffset? CurrentSnapshotAt { get; set; }
    /// <summary>True once an install wrote to the folder after the current snapshot.</summary>
    public bool ModifiedSinceSnapshot { get; set; }
    /// <summary>Executable (file name in the game folder) used by "Launch game"; null = auto-pick.</summary>
    public string? LaunchExe { get; set; }

    [JsonIgnore]
    public string CurrentStateText => CurrentSnapshotId is null
        ? "State: unknown (no snapshot taken or restored yet)"
        : (ModifiedSinceSnapshot ? "State: modified after snapshot " : "State: snapshot ") +
          $"\"{CurrentSnapshotLabel}\" ({CurrentSnapshotAt?.ToLocalTime():g})";

    public string GamePath => Path.Combine(RootPath, "game");
    public string SnapshotsPath => Path.Combine(RootPath, "snapshots");
}
