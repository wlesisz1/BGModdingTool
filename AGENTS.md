# BGModdingTool

Mod manager for Infinity Engine games (BGEE/BG2EE/IWDEE/EET/BGT) orchestrating WeiDU.
Read `ARCHITECTURE.md` first — it records the safety-critical design decisions
(full-copy instances, content-addressed snapshots, WeiDU as the only installer).

- .NET 10, Avalonia 12 MVVM (CommunityToolkit.Mvvm) in `src/BGModdingTool.App`
  (targets `net10.0-windows` because the in-app audio preview uses NAudio's WinMM backend;
  Core stays `net10.0`). `src/BGModdingTool.Cli` is the headless seeder/superpack generator.
- Non-WeiDU asset packs (portraits, soundsets, music) are handled by `AssetLibrary`; on EE
  games they deploy to the user's Documents folder — the only sanctioned write outside an
  instance, always recorded in a deployment manifest so it can be undone.
- All domain logic lives in `src/BGModdingTool.Core` — keep it UI-free and testable.
- Build: `dotnet build BGModdingTool.slnx`. Tests: `dotnet test`.
  Avalonia XAML errors are reported as `Avalonia error AVLNxxxx` (not `: error`), and the
  XAML compiler only runs when C# actually recompiles — after a XAML failure the next
  incremental build looks green but the app crashes with "No precompiled XAML found";
  verify with `dotnet build --no-incremental` and by launching the exe.
- App exe: `src/BGModdingTool.App/bin/Debug/net10.0-windows/BGModdingTool.App.exe`.
- Diagnostics: `%LOCALAPPDATA%\BGModdingTool\logs\app-<date>.log` (every status + all unhandled
  exceptions via `AppLog`) and `install-<timestamp>-<result>.log` (auto-saved WeiDU output).
  Read these first when the user reports "it doesn't work".
- WeiDU exit codes: 0 = OK, 3 = "INSTALLED WITH WARNINGS" (component is in — success), anything
  else = failure. Only `SKIPPING:` lines mark not-applicable components; mods print their own
  "Skipping X" chatter. The orchestrator skips components already present in weidu.log (resume).
- NAudio must stay at 2.2.1 while NAudio.Vorbis is 1.5.0 (3.x changes WaveStream signatures →
  TypeLoadException that kills all playback, not just OGG).
- Never write code that modifies a user's original game directory; only instance
  directories under the tool's data root may be mutated.
- Builds are the USER's documents. The CLI generator (`superpack`) must never overwrite an
  existing build (it saves "<name> (generated <date>)" and prints a diff; `--overwrite` is
  explicit), and `BuildStore.Save` archives the previous version to `builds/history/`.
  Apply rule changes to existing builds as targeted edits, not regeneration.
- If WeiDU prints `FATAL ERROR:` (e.g. Out of memory), the run is unsafe to resume: dialog.tlk is
  only written on exit, so strings of that run's "installed" components are lost — restore the
  pre-install snapshot. Use the 64-bit WeiDU (the "+legacy" zip is 32-bit and OOMs on EET+ToF).
  Mods with external state (ToF/SCS `weidu_external/data`) should be reinstalled whole, not resumed.
- Engine hard limits: `ResourceLimits` (Core) measures SPLSTATE.IDS (256 states, ids ≥256 are dead),
  STATS 400+, PROJECTL, KITLIST, tlk size after every installed entry, prints `📊`/`!!! OVERFLOW`
  lines and records per-entry deltas in `%LOCALAPPDATA%\BGModdingTool\limits-ledger.json`
  (measured, not estimated). Known big consumers: SCS 5900 (+42), ToF 60200 (+26), ToF 80000 (+11),
  aTweaks Detectable-Spells item labels (+15, triggered by any "PnP creatures" component).
