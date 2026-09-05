using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

public sealed record InstallReport(
    List<WeiduInstallResult> Results,
    SnapshotManifest PreSnapshot,
    SnapshotManifest? PostSnapshot,
    bool Aborted)
{
    public bool AllSucceeded => !Aborted && Results.All(r => r.ExitOk && !r.Failed);
}

public enum OnEntryFailure { Abort, Continue, }

/// <summary>
/// The safety-critical install path:
/// pre-snapshot → deploy mod files → run WeiDU per entry → post-snapshot →
/// persist the realized weidu.log next to the instance.
/// </summary>
public sealed class InstallOrchestrator(
    ModLibrary library, SnapshotService snapshots, WeiduRunner weidu, string? limitsLedgerPath = null)
{
    public async Task<InstallReport> InstallBuildAsync(
        GameInstance instance,
        BuildDefinition build,
        OnEntryFailure onFailure = OnEntryFailure.Abort,
        Action<string>? onOutputLine = null,
        IProgress<string>? phase = null,
        CancellationToken ct = default,
        Func<string, CancellationToken, Task<string?>>? onInputNeeded = null)
    {
        var byId = library.List().ToDictionary(m => m.Id, m => m);

        phase?.Report("Snapshot before install…");
        var pre = await snapshots.TakeAsync(instance, $"pre-install {build.Name}", ct: ct);

        var results = new List<WeiduInstallResult>();
        var aborted = false;
        var limitsBefore = ResourceLimits.Measure(instance.GamePath);
        foreach (var u in limitsBefore.Where(u => u.Limit is not null || u.IsOver))
            onOutputLine?.Invoke($"📊 start: {u}" + (u.IsOver ? $" — already {u.OverLimitIds.Count} id(s) over the limit" : ""));

        foreach (var entry in build.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.ModId is not null && byId.TryGetValue(entry.ModId, out var pkg))
            {
                phase?.Report($"Copying mod files: {pkg.Metadata.Name ?? pkg.ModFolderName}");
                library.DeployToGame(pkg, instance.GamePath);
            }

            if (entry.Components.Count == 0)
            {
                onOutputLine?.Invoke($"!!! No components selected for '{entry.Tp2}' — skipping.");
                results.Add(new WeiduInstallResult(entry, -1, []));
                if (onFailure == OnEntryFailure.Abort) { aborted = true; break; }
                continue;
            }

            var tp2OnDisk = ResolveTp2(instance.GamePath, entry.Tp2);
            if (tp2OnDisk is null)
            {
                onOutputLine?.Invoke($"!!! tp2 file for '{entry.Tp2}' not found in the game directory — skipping.");
                results.Add(new WeiduInstallResult(entry, -1, []));
                if (onFailure == OnEntryFailure.Abort) { aborted = true; break; }
                continue;
            }

            // Resume support: components already in weidu.log are not touched
            // again (re-installing would make WeiDU uninstall everything above them).
            var already = AlreadyInstalled(instance.GamePath, entry.Tp2);
            var toInstall = entry.Components.Where(c => !already.Contains(c)).ToList();
            if (toInstall.Count == 0)
            {
                onOutputLine?.Invoke($"✔ {entry.Tp2}: all selected components are already installed — skipping.");
                results.Add(new WeiduInstallResult(entry, 0, []));
                continue;
            }
            if (toInstall.Count < entry.Components.Count)
                onOutputLine?.Invoke($"ℹ {entry.Tp2}: {entry.Components.Count - toInstall.Count} component(s) already installed; " +
                                     $"installing the rest: {string.Join(' ', toInstall)}");

            phase?.Report($"WeiDU: {entry.Tp2} (components: {string.Join(", ", toInstall)})");
            SnapshotService.MarkModified(instance);
            var entryOnDisk = new BuildEntry
            {
                ModId = entry.ModId, Tp2 = tp2OnDisk,
                LanguageIndex = entry.LanguageIndex, Components = toInstall,
                ExtraArgs = entry.ExtraArgs, StdinInput = entry.StdinInput,
            };
            var result = await weidu.InstallAsync(instance.GamePath, entryOnDisk, onOutputLine, ct, onInputNeeded);
            results.Add(result);

            var expected = toInstall.ToHashSet();
            var got = result.InstalledNow.Select(e => e.ComponentNumber).ToHashSet();
            var missing = expected.Except(got).Count();
            // Components the mod itself SKIPPED as not applicable to this game
            // (e.g. BGT/Tutu-only) are fine — only unexplained gaps are failures.
            if (result.FatalError is not null)
            {
                onOutputLine?.Invoke($"!!! WeiDU crashed while installing {entry.Tp2} ({result.FatalError}). " +
                    "dialog.tlk is written only when WeiDU exits, so strings of the components marked installed " +
                    "in this run may be missing. Do NOT resume — restore the pre-install snapshot and run again.");
                results[^1] = result with { Failed = true };
                if (onFailure == OnEntryFailure.Abort) { aborted = true; break; }
                continue;
            }
            // Everything asked for was declared not applicable (SKIPPING / "does not have a
            // component"): WeiDU then exits non-zero (e.g. 6) although nothing went wrong.
            // The exit code alone is unreliable (6 = "some components skipped" even when the rest
            // installed fine): judge by what weidu.log gained, what the mod skipped, and error lines.
            var accountedFor = missing <= result.SkippedCount && result.ErrorCount == 0;
            if (accountedFor && !result.ExitOk)
            {
                onOutputLine?.Invoke(got.Count == 0
                    ? $"ℹ {entry.Tp2}: nothing to install — all {expected.Count} remaining component(s) are not applicable to this game (skipped by the mod)."
                    : $"ℹ {entry.Tp2}: {got.Count} installed, {missing} skipped by the mod — WeiDU exit code {result.ExitCode} treated as success.");
                results[^1] = result = result with { ExitCode = 0 };
            }
            if (!accountedFor)
            {
                onOutputLine?.Invoke($"!!! Install failure for {entry.Tp2} (exit code {result.ExitCode}, " +
                    $"{got.Count}/{expected.Count} components installed, {result.SkippedCount} skipped by the mod).");
                results[^1] = result with { Failed = true };
                if (onFailure == OnEntryFailure.Abort) { aborted = true; break; }
                continue;
            }
            // Hard-limit accounting: what this entry consumed, and whether a table overflowed.
            var limitsAfter = ResourceLimits.Measure(instance.GamePath);
            foreach (var line in ResourceLimits.Describe(limitsBefore, limitsAfter, entry.Tp2))
                onOutputLine?.Invoke(line);
            var deltas = ResourceLimits.Deltas(limitsBefore, limitsAfter);
            if (deltas.Count > 0 && limitsLedgerPath is not null)
            {
                try { ResourceLimits.AppendLedger(limitsLedgerPath, new LimitLedgerEntry(entry.Tp2, toInstall, DateTimeOffset.UtcNow, deltas)); }
                catch (Exception ex) { onOutputLine?.Invoke($"⚠ could not update the limits ledger: {ex.Message}"); }
            }
            limitsBefore = limitsAfter;

            if (result.HadWarnings)
                onOutputLine?.Invoke($"⚠ {entry.Tp2}: installed with warnings (WeiDU exit code 3) — " +
                    "usually harmless (e.g. fixes that are no longer needed); review the WARNING lines above.");
            foreach (var problem in IdsSanityCheck(instance.GamePath))
                onOutputLine?.Invoke($"⚠ {entry.Tp2} left {problem} — mods installed after it may fail to compile scripts " +
                                     "(seen with Rogue Rebalancing's old Detectable Spells library).");
            if (missing > 0)
                onOutputLine?.Invoke($"ℹ {entry.Tp2}: {missing} component(s) skipped by the mod as not applicable " +
                    "to this game — treated as success.");
        }

        SnapshotManifest? post = null;
        if (!ct.IsCancellationRequested)
        {
            phase?.Report("Snapshot after install…");
            post = await snapshots.TakeAsync(instance,
                aborted ? $"post-install (aborted) {build.Name}" : $"post-install {build.Name}", ct: ct);
        }

        // Persist what actually got installed, replayable later.
        var realizedPath = Path.Combine(instance.RootPath, "realized-build.json");
        var realized = BuildStore.FromWeiduLog(
            $"{build.Name} (realized)", build.GameType,
            File.Exists(Path.Combine(instance.GamePath, "weidu.log"))
                ? File.ReadAllText(Path.Combine(instance.GamePath, "weidu.log")) : "");
        JsonStore.Save(realizedPath, realized);

        return new InstallReport(results, pre, post, aborted);
    }

    /// <summary>
    /// Detects a corrupted STATS.IDS/SPLSTATE.IDS in override: rows whose "name" is a
    /// number ("200 182") are never legitimate and break every later script compile.
    /// </summary>
    public static IEnumerable<string> IdsSanityCheck(string gameDir)
    {
        foreach (var ids in new[] { "STATS.IDS", "SPLSTATE.IDS" })
        {
            var path = Path.Combine(gameDir, "override", ids);
            if (!File.Exists(path)) continue;
            int numericNames = 0;
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _)) numericNames++;
            }
            if (numericNames > 0)
                yield return $"a corrupted {ids} ({numericNames} rows with a number instead of a name)";
        }
    }

    /// <summary>Component numbers of this mod that weidu.log already lists as installed.</summary>
    private static HashSet<int> AlreadyInstalled(string gameDir, string tp2LogName)
    {
        var log = Path.Combine(gameDir, "weidu.log");
        if (!File.Exists(log)) return [];
        var key = InstallOrderService.NormalizeKey(tp2LogName);
        return WeiduLogParser.ParseFile(log)
            .Where(e => InstallOrderService.NormalizeKey(e.Tp2) == key)
            .Select(e => e.ComponentNumber)
            .ToHashSet();
    }

    /// <summary>Finds the tp2 in the game dir matching a weidu.log-style tp2 name.</summary>
    private static string? ResolveTp2(string gameDir, string tp2LogName)
    {
        var rel = tp2LogName.Replace('\\', '/');
        var direct = Path.Combine(gameDir, rel);
        if (File.Exists(direct)) return rel;

        // Try common variants: FOO/SETUP-FOO.TP2, FOO/FOO.TP2, SETUP-FOO.TP2, FOO.TP2
        var baseName = Path.GetFileNameWithoutExtension(rel.Split('/')[^1]);
        var bare = baseName.StartsWith("setup-", StringComparison.OrdinalIgnoreCase) ? baseName[6..] : baseName;
        string[] candidates =
        [
            $"{bare}/setup-{bare}.tp2", $"{bare}/{bare}.tp2",
            $"setup-{bare}.tp2", $"{bare}.tp2",
        ];
        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(gameDir, c))) return c;
        }
        return null;
    }
}
