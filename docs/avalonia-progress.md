# Avalonia port: progress log

One entry per stack branch, oldest first. Line counts are added/removed against the
branch's predecessor. "Not run" lists anything that was compiled but not executed.

## avalonia-port/main-20260928 (#1762)
- Merge of WPF main 0ff4583 (upstream sync, exempt from the line cap). Conflict resolutions only.
- Not run: WPF tests (Windows-only).

## avalonia-port/main-20260928-compat (#1763): +20/-20
- Ported main's new Chaster/asset-folder code onto the Core split; fixed new WPF tests' paths.
- Evidence: 9 Linux test projects, LinuxSmoke, --smoke, core-guards green.
- Not run: WPF tests.

## avalonia-port/net10 (#1764): +43/-48
- Every project on net10.0; CI on the 10.0.x SDK only.
- Evidence: all builds; 9 Linux test projects green natively on .NET 10.0.12.
- Not run: WPF tests, self-contained WPF publish, installer.

## avalonia-port/keincheck-mcp (#1765): +17/-1
- Keincheck MCP server embedded in Debug builds.
- Evidence: live app answers MCP; list_windows returns the shell window.

## avalonia-port/parity-ledger
- Adds docs/avalonia-parity.md (362 rows from five scout passes over the WPF head), plus this
  progress log and docs/avalonia-decisions.md.
- Starting totals: 59 missing, 247 stub, 56 wired, 0 verified.
- Evidence: statuses read from source only; nothing verified yet. Reviewer spot-check: 20/20
  statuses correct, 2 uncovered theme files added, stub/wired rule tightened.

## avalonia-port/core-pure-moves-1: +2/-2
- Moves 6 portable WPF-head files into CCP.Core: PresetFileService, QuizSessionGenerator
  (App.Mods -> CoreMods, same fallback), TimelineSession, SkillTree, DescentBarkPolicy,
  DescentStageCopy. No ledger row changes status yet; this unblocks Avalonia wiring for
  shell-preset-io, win-quiz, win-session-editor, views-tab-enhancements and the Descent windows.
- Skipped (need head-only types first, next branch): SettingsPaletteIndex,
  EnhancementImportRules, MandatoryVideoEnhancementScanner, DescentCeremonyCopy, DescentFuseCopy.
- Evidence: Core/Avalonia/WPF-tests builds, core-guards, Core + 6 Avalonia test projects,
  LinuxSmoke, --smoke all green. Reviewer: ACCEPT (no P0-P2).
- Not run: WPF tests (Windows-only).

## avalonia-port/core-pure-moves-2: +165
- Moves the five files the previous branch skipped: SettingsPaletteIndex, EnhancementImportRules,
  MandatoryVideoEnhancementScanner (with EnhancementResolver and Diag), DescentCeremonyCopy,
  DescentFuseCopy (with DescentMigrationChoices, DescentCycleXp and DescentFusePhase).
- Two fail-safe Core hooks seeded by the WPF App static ctor: EnhancementResolver.LibraryMatchProvider
  (unset = no library match) and SettingsPaletteIndex.JustDropDoorAvailableProvider (unset/throwing = false).
- Evidence: CoreHeadProviderTests (unset/live/fail-closed, plus provider arguments); Core 62/62,
  6 Avalonia test projects, core-guards, LinuxSmoke, --smoke green. Reviewer: ACCEPT (P3 notes only).
- Follow-up (next branch): Avalonia still uses local stand-ins for these types
  (SettingsPaletteWindow, DescentFuse/CeremonyWindow, SpiralTabView, EnhancementPlayerWindow).
- Not run: WPF tests (Windows-only).

## avalonia-port/wire-core-standins-1: +332
- Deletes the Avalonia stand-ins for types #1768/#1769 moved to Core: DescentFuseWindow and
  SpiralTabView read DescentFuseCopy; DescentCeremonyWindow reads DescentCeremonyCopy and
  DescentMigrationChoices (only the Offer stand-in stays: DescentMigrationOffer is head-only);
  SettingsPaletteWindow lists SettingsPaletteIndex.Search and navigates (ShowTab, FocusSection,
  2s glow), and the rail's search pill opens it; EnhancementPlayerWindow calls EnhancementResolver.
- Ledger: win-settings-palette stub -> wired. shell-nav-rail, win-descent-fuse,
  win-descent-ceremony, views-tab-spiral, views-deeper-enhancement-player stay stub (notes updated).
- Evidence: Tests/CCP.Avalonia.Tests/CoreStandInTests.cs (5 cases); Keincheck palette open,
  "volume" query and Enter -> Settings > Audio, spiral fog text, under
  ~/ccp-port/evidence/wire-core-standins-1/.
- Not run: WPF tests (Windows-only); Descent windows and enhancement auto-load have no user path
  Keincheck can drive.
