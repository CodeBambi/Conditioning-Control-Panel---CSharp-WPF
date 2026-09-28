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
