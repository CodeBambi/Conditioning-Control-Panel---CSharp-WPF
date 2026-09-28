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

## avalonia-port/wire-core-standins-2: +593
- Wires the second batch of #1768 Core types: PresetsTabView lists Core presets and exports /
  drop-imports through PresetFileService; EnhancementsTabView draws SkillDefinition.All (read-only,
  SkillTreeService is head-only); SessionEditorWindow drops its EditorSession stand-in for
  TimelineSession and imports/exports via SessionFileService; QuizWindow builds its session with
  QuizSessionGenerator (fallback content; QuizService AI is head-only).
- Ledger: shell-preset-io, views-tab-presets, views-tab-enhancements, win-session-editor, win-quiz
  stay stub (notes updated: share/CRUD, purchasing, and no opener for the two windows).
- Evidence: Tests/CCP.Avalonia.Tests/CoreStandInTests.cs (+4 cases, each checked to fail when its
  surface is broken); Keincheck Presets rail and Enhancements tree under
  ~/ccp-port/evidence/wire-core-standins-2/.
- Not run: WPF tests (Windows-only); preset export/drop and chip clicks live (Keincheck click_at
  hit-tests fall through to the window backdrop Panel); SessionEditorWindow and QuizWindow have no
  user path.

## avalonia-port/audio-libvlc: +403
- Real one-shot audio: LibVLCSharp 3.10.1 (VideoLAN.LibVLC.Windows 3.0.24 on Windows only; Linux
  uses system libvlc). `Platform/LibVlcAudio.cs` seeds `CoreAudio` in App.axaml.cs only if LibVLC
  initialises. Duck/Unduck on Linux via `pactl` on other apps' sink-inputs (ref count + generation
  as WPF); Windows ducking left unseeded (WASAPI port later).
- Start latency measured 8-22 ms (dummy and PipeWire), so no warm pool.
- New `--audio-probe` (real output, pactl view, duck/unduck check); CI installs libvlc for `--smoke`.
- Ledger: shell-audio-playback-devices stays stub (notes updated). UI sound callers remain silent:
  the head does not link `Assets/sounds` yet (blocker, separate branch); no reachable UI calls Duck.
- Evidence: ~/ccp-port/evidence/audio-libvlc/.
- Review fixes: volume mapped linear->cubic (0.5 plays at ~-6 dB like WPF); exit force-unducks
  other apps; a vanished stream no longer aborts a duck sweep; streams named CCP in pactl; no
  duck at master volume 0. `CoreAudioTests` now cover ref count, generation, force-unduck and
  Shutdown with pactl stubbed. ModCreator's audio preview and AwarenessPresetDetailDialog's
  keyword preview now play user-picked files.

## avalonia-port/head-sounds: +139
- `CCP.Avalonia.csproj` links `Assets/sounds/**` as Content at WPF's logical path `Resources/sounds`
  with WPF's `$(ContentPackSoundsExclude)` set verbatim (flashes_audio audio and the three builtin-*
  mod payloads ship as content packs). Replaces the lone `assets/sounds/faucet_charge_drop.wav`
  link; `--smoke` and `--audio-probe` now read the clip from `Resources/sounds`.
- Output: 145 files, 10,997,611 bytes: the set WPF's Include/Exclude globs select from `Assets/sounds`.
  WPF's own output is larger (254 files) because its temporary `$(CirceNeutralInBoxOverride)` re-adds
  builtin-locked clips in-box; that is deliberately not mirrored (no pack downloader or bark path here).
  bin/Release/net10.0 677,483,550 -> 688,460,831 bytes.
- `SharedSoundsPackagingTests`: every file a PlayOneShot caller names exists under BaseDirectory, and
  no content-pack audio ships while the mod manifests do. Proven to fail (excluded `chaos/ui_click.mp3`;
  planted an mp3 under builtin-locked; removed builtin-locked/bark_rules.json).
- Ledger: nothing goes stub->wired. No sound caller is reachable: PopQuizWindow, QuizWindow,
  SessionCompleteWindow, ChaosOverlayWindow and BubbleCountWindow.ShowOnAllMonitors have no
  production opener, and AvatarTube's GigglePriority is only called from the render ctor, with playSound:false. Notes updated.
- Evidence: ~/ccp-port/evidence/head-sounds/ (live `--audio-probe` with pactl capture).
- Review fixes: the exclude list moved to repo-root `ContentPackSounds.props`, imported by both heads
  (WPF `Resources/sounds` output file list diffed before/after: identical, 254 files); sounds Content
  gets `ExcludeFromSingleFile`. Live UI-sound Keincheck run: not run (no reachable caller); the
  `--audio-probe` run stands in for playback only.

## avalonia-port/core-asset-import: +218
- Phase 2 units 2+3: AssetImportService, AssetExtensionRepair, AssetPresetService, SessionLogService and
  Models/SessionLog move to CCP.Core (renames). App.Logger -> Serilog Log.
- Seams: App.EnsureCustomAssetsDirectories became CorePaths.EnsureCustomAssetsDirectories(customPath)
  (same body; WPF startup and the import both call it, #391). SessionLogService no longer subscribes to
  App.Flash/App.Video; FlashService and VideoService (LibVLC + browser) call RecordImages/RecordVideo
  right after raising FlashDisplayed/VideoStarted, which is where the old handlers ran. Both no-op
  without an active session, as the unsubscribed handlers did.
- Evidence: Tests/CCP.Core.Tests/AssetImportAndSessionLogCoreTests.cs (custom dirs before fallback,
  JSON round-trip against Fixtures/session_log_premove.json generated from the pre-move model,
  RecordMedia append rules); each failed once when its behaviour was broken.
- Not run: WPF tests (Windows-only); no Avalonia wiring, so no Keincheck run.

## avalonia-port/wire-session-surfaces: +396
- Session editor opens from Presets Create New and row ✎ and saves through Core SessionManager
  (built-in -> new custom, custom -> overwrite; save failures logged and shown in an error dialog).
- Recent Sessions lists Core SessionLogService.LoadRecentLogs() instead of fabricated rows and reopens
  SessionCompleteWindow, which now takes Core SessionLog (Recap/MediaKind stand-ins deleted).
- Gap: no Avalonia session engine, so the end-of-session recap open (WPF Presets.cs:1788) stays unwired.
- Evidence: SessionSurfacesTests (4, each fail-proofed); CCP.Avalonia.Tests now runs in a temp
  CCP_USERDATA_DIR; Keincheck text dumps under evidence/avalonia-port/wire-session-surfaces/.
- Ledger: win-session-editor, win-session-log-history, win-session-complete stub -> wired;
  shell-session-io stays stub with an updated note.
- Not run: file-picker save live; WPF tests.
