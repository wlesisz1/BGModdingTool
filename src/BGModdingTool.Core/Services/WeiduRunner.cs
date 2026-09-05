using System.Diagnostics;
using System.Text;
using BGModdingTool.Core.Models;

namespace BGModdingTool.Core.Services;

public sealed record WeiduInstallResult(
    BuildEntry Entry, int ExitCode, List<WeiduLogEntry> InstalledNow,
    /// <summary>Components WeiDU reported as "SKIPPING:" (not applicable to this game) — not failures.</summary>
    int SkippedCount = 0,
    /// <summary>The "FATAL ERROR: …" line when WeiDU itself crashed (e.g. Out of memory).</summary>
    string? FatalError = null,
    /// <summary>Set by the orchestrator when the entry did not deliver what the build asked for.</summary>
    bool Failed = false,
    /// <summary>Components WeiDU reported as "NOT INSTALLED DUE TO ERRORS".</summary>
    int ErrorCount = 0)
{
    /// <summary>WeiDU exit code 3 = "INSTALLED WITH WARNINGS" — the component is in, keep going.</summary>
    public const int ExitWithWarnings = 3;
    public bool ExitOk => ExitCode is 0 or ExitWithWarnings;
    public bool HadWarnings => ExitCode == ExitWithWarnings;
}

/// <summary>
/// Process wrapper around weidu.exe. All installs run non-interactively
/// (--force-install-list) inside an instance's game directory; stdout is
/// streamed to the caller line by line.
/// </summary>
public sealed class WeiduRunner(string weiduExePath)
{
    public string WeiduExePath { get; } = weiduExePath;

    /// <summary>
    /// Game language directory (e.g. "en_US") passed as --use-lang. Without it,
    /// WeiDU's first run on an EE game asks interactively which game language
    /// to use — an infinite prompt loop with our closed stdin.
    /// </summary>
    public string? GameLanguage { get; set; }

    private string UseLangArg => string.IsNullOrWhiteSpace(GameLanguage) ? "" : $" --use-lang {GameLanguage}";

    /// <summary>Lists a mod's languages via --list-languages (index + name).</summary>
    public async Task<List<(int Index, string Name)>> ListLanguagesAsync(string gameDir, string tp2, CancellationToken ct = default)
    {
        var output = await RunAsync(gameDir, $"--nogame --list-languages \"{tp2}\"", null, ct);
        var result = new List<(int, string)>();
        foreach (var line in output.Split('\n'))
        {
            // Accept both "0:English" and "Language 0: English" style lines.
            var m = System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^(?:Language\s+)?(\d+)\s*:\s*(.+)$");
            if (m.Success)
                result.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim()));
        }
        return result;
    }

    /// <summary>
    /// Picks the language index for the preferred language name (default
    /// "English"). Index 0 is NOT reliably English — many mods list Russian
    /// or French first — so callers must not assume it.
    /// </summary>
    public static int PickLanguageIndex(IReadOnlyList<(int Index, string Name)> languages, string preferred = "English")
    {
        if (languages.Count == 0) return 0;
        var match = languages.FirstOrDefault(l => l.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase));
        return match.Name is not null ? match.Index : languages[0].Index;
    }

    /// <summary>Convenience: lists languages and returns the preferred one's index (0 on failure).</summary>
    public async Task<int> FindLanguageIndexAsync(string workingDir, string tp2, string preferred = "English", CancellationToken ct = default)
    {
        try { return PickLanguageIndex(await ListLanguagesAsync(workingDir, tp2, ct), preferred); }
        catch { return 0; }
    }

    /// <summary>Lists a mod's components via --list-components.</summary>
    public async Task<List<ModComponent>> ListComponentsAsync(string gameDir, string tp2, int languageIndex, CancellationToken ct = default)
    {
        var output = await RunAsync(gameDir, $"--nogame --list-components \"{tp2}\" {languageIndex}", null, ct);
        var result = new List<ModComponent>();
        foreach (var line in output.Split('\n'))
        {
            // Format: ~TP2~ #lang #number // Component Label
            var t = line.Trim();
            if (!t.StartsWith('~')) continue;
            var parts = t.Split("//", 2);
            if (parts.Length < 2) continue;
            var head = parts[0];
            var hash = head.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (hash.Length >= 3 && int.TryParse(hash[^1], out var number))
                result.Add(new ModComponent(number, parts[1].Trim()));
        }
        return result;
    }

    /// <summary>
    /// Installs one build entry (one mod, selected components) into the game dir.
    /// Success is judged by the weidu.log diff, not just the exit code.
    /// When <paramref name="onInputNeeded"/> is given, an unexpected installer
    /// prompt (detected by prompt-looking output followed by silence) is routed
    /// to it; its result is typed into the installer's stdin. Returning null
    /// kills the installer.
    /// </summary>
    public async Task<WeiduInstallResult> InstallAsync(
        string gameDir, BuildEntry entry,
        Action<string>? onOutputLine = null, CancellationToken ct = default,
        Func<string, CancellationToken, Task<string?>>? onInputNeeded = null)
    {
        var logPath = Path.Combine(gameDir, "weidu.log");
        var before = WeiduLogParser.ParseFile(logPath);

        var components = string.Join(' ', entry.Components);
        var debugName = "setup-" + SanitizeDebugName(entry.Tp2) + ".debug";
        // The tp2 file MUST come first: WeiDU's list options (--force-install-list,
        // --args-list) greedily consume following non-option arguments, so a
        // trailing tp2 path would be misparsed as a component number.
        var extra = string.IsNullOrWhiteSpace(entry.ExtraArgs) ? "" : " " + entry.ExtraArgs.Trim();
        var args = $"\"{entry.Tp2}\" --no-exit-pause --noautoupdate --skip-at-view --log \"{debugName}\"{UseLangArg} " +
                   $"--language {entry.LanguageIndex} --force-install-list {components}{extra}";

        var exitCode = 0;
        var skipped = 0;
        string? fatal = null;
        var errors = 0;
        await RunAsync(gameDir, args, line =>
        {
            // Only WeiDU's own "SKIPPING: <component>" lines count; mods print
            // their own "Skipping X creation - already exists" chatter by the hundreds.
            if (line.TrimStart().StartsWith("SKIPPING:", StringComparison.Ordinal)) skipped++;
            // "WARNING: mod X does not have a component N": the build names a component this
            // version of the mod no longer has — nothing to install, not a failure.
            if (line.Contains("does not have a component", StringComparison.Ordinal)) skipped++;
            // WeiDU writes dialog.tlk only at exit: a crash here silently loses every
            // string added by the components that "succeeded" in this run.
            if (line.TrimStart().StartsWith("FATAL ERROR:", StringComparison.Ordinal)) fatal = line.Trim();
            if (line.TrimStart().StartsWith("NOT INSTALLED DUE TO ERRORS", StringComparison.Ordinal)) errors++;
            onOutputLine?.Invoke(line);
        }, ct, code => exitCode = code, entry.StdinInput, onInputNeeded);

        var after = WeiduLogParser.ParseFile(logPath);
        var beforeSet = before.Select(e => (e.Tp2, e.ComponentNumber)).ToHashSet();
        var installedNow = after.Where(e => !beforeSet.Contains((e.Tp2, e.ComponentNumber))).ToList();

        return new WeiduInstallResult(entry, exitCode, installedNow, skipped, fatal, ErrorCount: errors);
    }

    /// <summary>Uninstalls components of a mod via --force-uninstall-list (WeiDU handles the stack).</summary>
    public async Task<int> UninstallAsync(string gameDir, string tp2, IEnumerable<int> components,
        Action<string>? onOutputLine = null, CancellationToken ct = default)
    {
        var exitCode = 0;
        var args = $"\"{tp2}\" --no-exit-pause --noautoupdate{UseLangArg} --force-uninstall-list {string.Join(' ', components)}";
        await RunAsync(gameDir, args, line => onOutputLine?.Invoke(line), ct, code => exitCode = code);
        return exitCode;
    }

    private static readonly System.Text.RegularExpressions.Regex PromptPattern = new(
        @"please (choose|enter|select)|\[y\]|\(y/n\)|do you (want|wish)|would you like|" +
        @"enter the (full )?path|re-?install|\[r\]e|choice:|\?\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private async Task<string> RunAsync(string workingDir, string arguments,
        Action<string>? onLine, CancellationToken ct, Action<int>? onExit = null,
        string? stdinInput = null, Func<string, CancellationToken, Task<string?>>? onInputNeeded = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = WeiduExePath,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        var buffer = new StringBuilder();
        var tail = new System.Collections.Concurrent.ConcurrentQueue<string>();
        long lastOutputTicks = DateTime.UtcNow.Ticks;
        var promptSuspected = 0;

        void HandleLine(string data)
        {
            buffer.AppendLine(data);
            tail.Enqueue(data);
            while (tail.Count > 20) tail.TryDequeue(out _);
            Interlocked.Exchange(ref lastOutputTicks, DateTime.UtcNow.Ticks);
            if (data.Length > 0 && PromptPattern.IsMatch(data)) Interlocked.Exchange(ref promptSuspected, 1);
            onLine?.Invoke(data);
        }
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) HandleLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) HandleLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start weidu: {WeiduExePath}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Feed prepared answers for ACTION_READLN prompts.
        if (!string.IsNullOrEmpty(stdinInput))
        {
            var input = stdinInput.ReplaceLineEndings("\n");
            if (!input.EndsWith('\n')) input += "\n";
            await process.StandardInput.WriteAsync(input);
            await process.StandardInput.FlushAsync(CancellationToken.None);
        }

        try
        {
            if (onInputNeeded is null)
            {
                // No interactive channel: close stdin so an unexpected prompt
                // reads EOF instead of hanging.
                process.StandardInput.Close();
                await process.WaitForExitAsync(ct);
            }
            else
            {
                // Keep stdin open and watch for stalls: prompt-looking output
                // followed by silence means the installer waits for an answer.
                while (!process.HasExited)
                {
                    await Task.Delay(500, ct);
                    if (process.HasExited) break;
                    var idle = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastOutputTicks));
                    var threshold = Interlocked.CompareExchange(ref promptSuspected, 0, 0) == 1
                        ? TimeSpan.FromSeconds(2)
                        : TimeSpan.FromSeconds(25);
                    if (idle < threshold) continue;

                    Interlocked.Exchange(ref promptSuspected, 0);
                    // Ask the user, but stop asking the moment the installer exits on its own
                    // (a long silent step that ends is not a prompt).
                    using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var inputTask = onInputNeeded(string.Join("\n", tail), promptCts.Token);
                    var exitTask = process.WaitForExitAsync(promptCts.Token);
                    var first = await Task.WhenAny(inputTask, exitTask);
                    promptCts.Cancel();
                    if (first == exitTask || process.HasExited) break;
                    string? answer;
                    try { answer = await inputTask; }
                    catch (OperationCanceledException) { break; }
                    if (answer is null)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        break;
                    }
                    onLine?.Invoke("⌨ > " + answer);
                    await process.StandardInput.WriteLineAsync(answer);
                    await process.StandardInput.FlushAsync(CancellationToken.None);
                    Interlocked.Exchange(ref lastOutputTicks, DateTime.UtcNow.Ticks);
                }
                await process.WaitForExitAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        onExit?.Invoke(process.ExitCode);
        return buffer.ToString();
    }

    private static string SanitizeDebugName(string tp2)
    {
        var name = Path.GetFileNameWithoutExtension(tp2.Replace('\\', '/').Split('/')[^1]);
        if (name.StartsWith("setup-", StringComparison.OrdinalIgnoreCase)) name = name[6..];
        return name.ToLowerInvariant();
    }
}
