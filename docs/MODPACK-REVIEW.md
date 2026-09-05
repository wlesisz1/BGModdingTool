# Modpack documentation review (2026-09-02)

Scope: every entry of the generated builds **"Modpack: BG1+SoD (pre-EET)"** and
**"Modpack: EET Everything"**. Sources: each mod's local readme / `.ini` metadata /
`.tp2` predicates in the library, plus GitHub READMEs where local docs were thin.
Every change below is encoded in code (built-in order rules, install-order tiers,
generator component adjustments) so that the app's **Auto-sort** reproduces it.

Legend: **ORDER** = position change · **COMP** = component selection change ·
**NOTE** = no change, but read it.

## Structural changes

| Change | Source |
|---|---|
| **ORDER** EE Fixpack installed **before setup-EET** on BG2:EE **and** on the BG:EE instance (after DLC Merger). It was silently doing nothing after EET. | `setup-eefixpack.tp2:87/107/128` `REQUIRE_PREDICATE !GAME_IS ~eet~`; readme "before other mods with the sole exception of DLC Merger" |
| **ORDER** Rogue Rebalancing → SCS → **aTweaks** (RR was after aTweaks; aTweaks was before SCS). | aTweaks & RR readmes: "never install any component of Rogue Rebalancing after aTweaks"; SCS readme: aTweaks "designed to be installed after SCS" |
| **ORDER** Trials of the Luremaster after SCS. | TotLM readme: IWD spell import "skipped if SCS 'Include arcane spells from IWD:EE' detected … install this mod after" |
| **ORDER** EET Tweaks moved to the very end (after IWD_EET_integration and the NWN campaign, before EET_end). | EET Tweaks readme: "installed last or close to last … after any mods which add or alter CRE, BCS, DLG, ITM, SPL, EFF" |
| **ORDER** Imoen 4 Ever right after EET/Fixpack, before all quest/NPC mods. | I4E readme: "treated as a quest mod … before any other NPC mods that add interjections in SoA chapter 2&3"; ini `Type = Tweak_early` |
| **ORDER** The Planar Sphere Mod at the start of the quest block. | PSM readme: raw-overwrites AR0412/AR0419/AR0420 — "install … early before any other mod that modify these areas" |
| **ORDER** Region of Terror after IWD1_EET/IWD2_EET, before IWD_EET_End/Worldmap. | `rot.ini` `After = IWD1_EE, IWD2_EE`; GitHub: "after IWD-EET mods" |
| **ORDER** All Things Mazzy after Ninde, Pai'Na, Saradas Magic (crossmod), before tweaks. | ATM "List of Mods with Crossmod Content.txt": those "must be installed before AllThingsMazzy"; readme: "after major quest and NPC mods but before your tweaks" |
| **ORDER** Heroes, Thieves and Moneylenders after **all** NPC-adding mods. | HTaM readme: "install HTaM AFTER all mods that add new NPCs (otherwise their names will not appear on statues in Trademeet)" |
| **ORDER** Autumn's Twilight after Pai'Na. | AT readme: Pai'Na "must be installed BEFORE Autumn's Twilight" for crossmod |
| **COMP** Removed from the pack: **I Remember You** (tp2 references folder `irememberyou/` that does not match the package folder; `EXTEND_BOTTOM ar4900.bcs` is not EET-aware — install would fail; duplicates Aerie in BG:EE), **DNT** (unfinished v0.9 Clannad-crossover), G3 Anniversary, Dead Gardens (LCC conflict with Check the Bodies), HoW_EET (contained in IWD1_EET). | reviews part 3/1, LCC |

## Component changes (per mod)

| Mod | Change | Why |
|---|---|---|
| Tweaks Anthology | dropped **0** (Batch Installer) | interactive-only, like SCS/ToF "batch mode" — never for `--force-install-list` |
| Tweaks Anthology | dropped **6000–6350** (36 "NWN-style feats") | every one `REQUIRE MOD_IS_INSTALLED EEex` — EEex is not in the pack |
| Tweaks Anthology | dropped **2999, 3008, 3020, 3040, 3210, 3183, 3494–3497, 2090** | readme's own "Cheats" group / no-XP-cap / SoA twink items |
| Tweaks Anthology | dropped **2050, 2060, 2010, 2020** | "first option of each group" artifacts: +150 % XP, 89 000 BG1/SoD XP cap |
| Tweaks Anthology | dropped **2080, 2160, 2300, 2350, 2352, 2360, 2370, 2380, 2410** | auto-skip when Talents of Faerûn 60200/81100/40000/40100 is installed; 2370 also forbidden by SCS 4100 |
| EET Tweaks | dropped **1060, 2000, 2050, 2060, 2080** | Polish-only voices; 2.95 M XP cap (EET default is 8 M); +150 % XP; 2080 needs EEex |
| SCS | dropped **3550, 6020, 2080, 4030, 4100, 4240** | 3550 DEPRECATED (now an ini setting); 6020 IWDEE-only; the rest are blocked/skipped because Talents of Faerûn 2000/40600/41000/60200/60300/90100 is installed (`tp2:332, 623, 692, 945`) |
| aTweaks | dropped **150/152/153, 155, 156** (PnP Fiends family) | SCS readme: fiend components "at least partially incompatible with SCS Smarter Mages and Improved Fiends; use one or the other" — SCS 6510 kept |
| BG1 NPC Project | dropped **100** (Tutu/BGT-only), **90** (beta, conflicts with inn tweaks); **200** (PIDs) moved to the end of its list | `tp2:485 REQUIRE_PREDICATE (!GAME_IS ~bgee eet~)`; readme :151-153; readme :239 "must be installed after all other BG1NPC components" |
| Imoen 4 Ever | dropped **15** (bgee-only), **10** | `tp2:1588`; Reflections of Destiny `tp2:69 FORBID_COMPONENT imoen_forever 10` |
| Blackhearts | dropped **20005** | `REQUIRE GAME_IS bg2ee AND NOT eet` |
| Region of Terror | dropped **1, 2** | kit pack `!GAME_IS bg2ee eet`; biffing classic-only |
| Planar Sphere Mod | dropped **624, 625** | "sells some very overpowered items"; "not bugfixed" teleport add-on |
| All Things Mazzy | dropped **3** | epilogue override "will override … most mod epilogues" |
| Shards of Ice | dropped **2** | author: "you should be ashamed of yourself" (Summon Cow) |
| Test Your Mettle | **11 → 12** | "Reduce by 75% (recommended)" |
| Drizzt Saga (pre-EET) | dropped **2** | `DEPRECATED` "Raise the XP cap" |
| gorgon | dropped **4** | random loot on beggars/children — economy tweak |
| BP-BGT Worldmap | **[0 4]** (dropped 1, 3) | `REQUIRE_PREDICATE !GAME_IS … eet` / `ENGINE_IS tob` |
| NWNForBG | map component **10**, not 12 | real tp2 numbering (IWD1_EET readme is off by one) |

## Talents of Faerûn on EET — assessment (2026-09-02 follow-up)

Readme: "probably largely compatible with EET … only limited testing … (testing assumes EET_End
installed before ToF, contra the official EET install instructions)". Evidence beyond the readme:

- 14 code files carry explicit `GAME_IS ~eet~` branches; changelog entries beta 5/7/9/10/11 fix
  EET-specific bugs (ability scores on SoD/EET, kits on the BG part of EET, subraces on EET,
  Imoen UI glitches on EET, imported characters). EET is actively maintained, not just tolerated.
- `dw_talents.ini` `start_eet=bg` — correct for a full-trilogy start; set `soa`/`sod`/`tob` when
  starting EET elsewhere, otherwise wizards get too few spells at creation.
- `3p/original_game.ini` re-kits NPCs: Tiax → assassin of Cyric, Viconia → cleric of Shar (and
  drow subrace), Yeslick → Axe of Clangeddin, Aerie → air elementalist of Baervan (winged elf).
  On EET this is applied by creature name; check that the BG1-side Viconia/Tiax/Yeslick match the
  BG2-side ones, or disable the lines.
- **Rogue Rebalancing overlap** (found here): ToF's HLA system already contains RR's thief/bard
  HLAs, ToF 41000 rebalances the same kits, ToF 81100 replaces the proficiency tables RR 0 edits,
  and ToF requires proficiency-changing mods to come BEFORE it. Changes: RR components 0, 1, 2, 4,
  5, 6 dropped (content components 3, 7, 8, 9, 11, 12, 999 kept); RR now installs before ToF.
- UI mods: none in the pack (EET_gui excluded). If one is ever added it must precede ToF
  (built-in rule `eet_gui Before dw_talents`).
- Open decision: official EET order (EET_end last — current) vs the author's tested order
  (EET_end before ToF/SCS). SCS's readme says the same ("testing mostly assumes 'after'").
  Both DavidW mods leave journal/dialog files alone, so EET_end has nothing of theirs to
  post-process; the official order is kept by default, the tested order is a one-line change.

## Verified as correct (no change)

- **EET core** `--args-list sp "<BG:EE instance>"`: `bgee_dir.tph` uses `argv[1]` when `argv[0]` contains `p` (WeiDU `STRING_CONTAINS_REGEXP = 0` means *found*).
- **Ascension** `FORBID_COMPONENT stratagems 5900` = "install Ascension before SCS", satisfied.
- **Talents of Faerûn** before SCS and Tweaks (ToF readme); SCS then defers to ToF's kits/HLAs/NPC customisation.
- **NWNForBG** core+map early, **campaign (20)** after IWD_EET_integration, before EET_end (NWNForBG + IWD1_EET readmes).
- **IWD-EET family** order per `iwd*_eet.ini` Before/After.
- Language indices: all English (several mods have Russian/French/German/Spanish at index 0 — Godcall's English is at index 1, indented in the tp2).
- Cowled Menace before SCS; RoorenArt before Tweaks; Shards of Ice before Tweaks/SCS; Reflections of Destiny before SCS; BG1NPC before Framed.

## Notes for the player (no automatic action)

- **Reflections of Destiny** (v0.9.3 beta) components 100/200 rewrite major SoD plot points (Korlasz dungeon moved to BG1, Caelar backstory, Aun Argent removed). Kept, but it is opinionated.
- **All Things Mazzy "For the Evil"** Ust Natha/Viconia expansion is broken by SCS Improved Fiends (6510). SCS kept; drop ATM component 1 if that quest matters more.
- **BG1 Style Portraits vs RoorenArt**: ~45 NPC portraits overlap — the later mod (RoorenArt) wins. Swap them if you prefer BG1-style art.
- **RoorenArt component 20** writes portraits to `%USER_DIRECTORY%/portraits` (My Documents) — outside the instance directory.
- **BWQuest (Black Rose Pt.1)** audio is installed by an interactive-only `.bat`; run `BWQuest/audio-install.bat` in the instance afterwards for voiced dialogue.
- **Imoen 4 Ever** SoD reactions need Road to Discovery (not in the pack).
- **EE Fixpack** is alpha software; it is also on the EET BG:EE-side compatibility list.
