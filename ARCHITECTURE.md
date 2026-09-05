# BGModdingTool — Architecture

Modern mod manager for Infinity Engine games (BGEE, BG2EE, IWDEE, EET, classic BGT).
Goals: **data safety**, **reproducible configurations**, **reuse of community standards**.

## Core principles

1. **Never touch the original game install.** All modding happens inside a *game
   instance* — a full copy of the game directory managed by the tool.
   (Hardlink cloning was rejected: WeiDU may truncate/edit files in place, which
   through a hardlink would corrupt the source install.)
2. **WeiDU is the installer.** We never re-implement mod installation. The tool
   orchestrates `weidu.exe` (`--list-components-json`, `--force-install-list`,
   `--log`, `--language`) and treats `weidu.log` as the source of truth for
   what is installed.
3. **Reuse community metadata.** Project Infinity-style `mod.ini` metadata
   (name, author, links, `Before`/`After` ordering hints) is parsed when present.
   Community install-order lists (BWS/EET-style) are importable.
4. **Builds are the unit of configuration.** A *build* = ordered list of
   (tp2, language, component numbers) for a game type. Builds are JSON files —
   savable, shareable, re-playable from scratch on a fresh instance.
5. **Snapshots give versioning + rollback.** Before/after each install batch a
   snapshot of the instance is taken into a content-addressed store
   (SHA-256 blobs, deduplicated across snapshots; a size+mtime cache avoids
   re-hashing unchanged files). Restore materializes the manifest back into the
   instance directory.

## Solution layout

- `src/BGModdingTool.Core` — domain + services, no UI dependencies.
  - `Models/` — GameType, GameInstance, ModPackage/ModComponent/ModMetadata,
    BuildDefinition, WeiduLogEntry, Snapshot manifest types.
  - `Services/`
    - `GameDetector` — identify game type from a directory (chitin.key + engine lua/exe markers).
    - `InstanceService` — create/list/delete instances (full copy of source game).
    - `WeiduRunner` — process wrapper around weidu.exe; component listing (JSON), batch install, live log streaming.
    - `WeiduLogParser` — parse `weidu.log`.
    - `ModLibrary` — central mod storage (`%LibraryRoot%/mods/<id>`); extracts zip/7z/rar (SharpCompress), discovers `.tp2` files, reads `mod.ini`.
    - `ProjectInfinityIni` — PI metadata parser.
    - `SnapshotService` — content-addressed snapshot store + restore.
    - `InstallOrderService` — stable topological sort from PI Before/After
      metadata (cycles reported, not fatal) + sort by a community master
      order list (one mod per line; unlisted mods keep order, go last).
    - `ModDownloader` — downloads mod archives from direct URLs or GitHub
      repos (latest release asset, falling back to source zipball) and the
      official WeiDU Windows build (WeiDUorg/weidu) into `tools/`.
    - `BuildStore` / `JsonStore` — JSON persistence under the app data root.
    - `AppPaths` — all tool-owned paths (`%LOCALAPPDATA%/BGModdingTool` by default; configurable library root recommended on a large drive).
- `src/BGModdingTool.App` — Avalonia 11 MVVM UI (CommunityToolkit.Mvvm).
  Shell with sections: **Instances**, **Mod Library**, **Builds**, **Install** (log console).
- `tests/BGModdingTool.Core.Tests` — xUnit; parsers and services are covered with
  fixture files (no real game needed).

## Data layout (tool-owned)

```
<DataRoot>/
  config.json               # global settings (library root, weidu path, source games)
  builds/<name>.json        # build definitions
  instances/<id>/
    instance.json           # metadata (game type, source, created)
    game/                   # the playable modded copy
    snapshots/
      objects/ab/cdef...    # SHA-256 blobs (deduplicated)
      <snapshotId>.manifest.json
  mods/<modId>/             # extracted mod packages (shared across instances)
```

## Install flow (safety-critical path)

1. User picks instance + build (or ad-hoc component selection).
2. Tool copies required mod folders + correct `setup-*.exe`/weidu into the instance `game/`.
3. **Snapshot** (`pre-install`).
4. Run WeiDU non-interactively per mod: `setup-x.exe --no-exit-pause --log setup-x.debug --language N --force-install-list a b c`.
   Stream stdout to the UI; detect per-component success/failure from exit code + `weidu.log` diff.
5. On completion: **snapshot** (`post-install`), persist the realized build
   (actual `weidu.log`) next to the requested one.
6. On failure: offer rollback to `pre-install` snapshot (WeiDU stack uninstall is
   still available as the lighter option).
