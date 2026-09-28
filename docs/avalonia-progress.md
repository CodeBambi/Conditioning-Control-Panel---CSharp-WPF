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

## avalonia-port/wire-programs-roadmap-awareness: +507
- Programs intro opens on the first Programs visit (CoreSettings flags, Core built-in programs and
  CoreMods; ProgramService is never constructed). Enrol stays disabled: no run panel or session runner here.
- RoadmapStepPopup and the track/badge messages open from RoadmapService.StepCompleted on the shell's
  single roadmap instance (subscription covered by a Language test).
- Awareness preset grid ported; built-in presets linked from Assets/AwarenessPresets like WPF. Cards,
  "+ New Preset" and the advanced link open AwarenessPresetDetailDialog (seen live). Tour hook gated on the
  Awareness tour via new CoreTutorial.CurrentTourName; it never fires until this head has a tutorial runner.
  The Awareness nav button now shows its tab.
- Evidence: ProgramsRoadmapAwarenessOpenersTests (4) + LanguageSelectorTests roadmap assertion, each
  fail-proofed; evidence/avalonia-port/wire-programs-roadmap-awareness/. Two review rounds.
- Not run: WPF tests; Chaster confirm on activate (not ported; lock time is declined, the safe direction).

## avalonia-port/x11-override-redirect: +203
- X11Overlay.SetOverrideRedirect(window, bounds): XChangeWindowAttributes(CWOverrideRedirect) + XSync on the
  overlay's own connection, applied before Show() (Avalonia 12.1.2 creates the XID in the Window ctor and maps in
  Show; measured), THEN Position/Width/Height - a configure issued before override-redirect is redirected to the
  WM, which can replay a stale 537x358 after map (was 14/25 FAIL on non-primary screens, now 25/25 PASS).
  Returns false on non-XID windows, 32-bit processes, or an X error from the server.
- X11 backend pinned on Linux (UseX11 after UsePlatformDetect); "X11 or Wayland" comment corrected.
- --overlay-check: per screen, a transparent click-through override-redirect overlay read back from the X
  server (map_state, override_redirect, depth 32, empty input shape, geometry == Screens).
- Evidence: 25 consecutive PASS live on this box (XDG_SESSION_TYPE=wayland via XWayland, 3 screens at 1.79
  scaling; overlay-check-loop-25.txt, before-fix baseline 11/25) and in a nested kwin; exit 1 with
  override-redirect skipped (overlay-check-failproof-no-override-redirect.txt). Existing x11-overlay-probe.sh still passes.
- Ledger: shell-clickthrough-overlays notes updated; stays stub until a feature uses the mechanism.
- Follow-up: X11Overlay's process-wide XSetErrorHandler replaces Avalonia's own handler (pre-existing).
- Not run: in CI (needs a display); Windows (no Win32 shim yet).

## avalonia-port/layered-audio: +395
- LayeredAudio (head-side port of WPF LayeredAudioService on LibVLC): start/restart/stop rules, ignore-master
  path for audio-only sessions, live track/master volume, ducked by other CCP sounds, true infinite loop
  (restart on end-of-file), synchronous bounded shutdown on exit.
- Audio Layers window opens from Settings > Audio and Home, single instance, open failure shown like WPF.
- Volume sticks per stream even when PipeWire restores by app name (bounded re-apply after Playing).
- Evidence: LayeredAudioTests (fail-proofed); --layers-probe with exact per-stream values (15/15 PASS in a
  supervisor re-run); live Keincheck run with a temp profile; 0 binding errors. Two review rounds.
- Ledger: win-layered-audio verified; shell-home-audio, shell-audio-playback-devices, views-settings-audio updated.
- Gaps: output-device selection (this head has no device picker yet); audio-only session start (no session engine).

## avalonia-port/overlay-flashes: +660/-275
- Image flashes as click-through override-redirect overlays, one window per image, WPF placement
  (FlashPlacement moved to Core; the WPF FlashService now calls it, logic unchanged), WPF timing
  (1 s delay, 300 ms stagger, lifetime FlashDuration + 1 s, linear 1/32 fade at WPF's rate), images decoded
  at display size, compositor-side fade via _NET_WM_WINDOW_OPACITY, busy guard like WPF.
- Manual trigger: head-only "▶ Test" on the Flash card (decision logged); the session scheduler that fires
  flashes is the next unit.
- Evidence: FlashOverlayPlacementTests (fail-proofed); X readback of 5 overlays (override-redirect, depth 32,
  empty input shape), timeline 0.28-0.29 s stagger, 6.40 s lifetime; KWin opacity ramp. Two review rounds.
- Not run: a physical click through an overlay (no xdotool), a desktop screenshot over another app (session
  locked), non-compositing X11 WMs (no fade there), Windows (no Win32 shim yet).

## avalonia-port/session-scheduler-core: +154
- CoreFlash: WPF's ambient flash rhythm in Core (3600/FlashFrequency s ±30%, min 3 s; skip when busy or a
  display change is settling). WPF FlashService now calls CoreFlash.NextIntervalSeconds (bit-identical).
- Avalonia: the Flash card's Enable toggle arms/disarms the schedule; the Per Hour slider re-rolls like
  WPF RefreshSchedule. Differences (engine Start/Stop not ported, DND guard, launch arming, Stop leaving
  bursts to finish) recorded in the decisions log and the feat-flash row.
- Evidence: CoreFlashScheduleTests (11, fail-proofed); live with a temp profile at 180/h: 4 automatic bursts
  at 24.2/20.0/21.7 s gaps, none in 75 s after disabling; 0 binding errors. Review: ACCEPT.

## avalonia-port/win32-overlay-shim: +260
- Win32 click-through topmost overlays behind the same overlay entry points (dispatch by platform handle:
  XID -> X11, HWND -> Win32): WPF's style bits (LAYERED|TOOLWINDOW, TRANSPARENT+NOACTIVATE only while
  click-through), HWND_TOPMOST via SetWindowPos, opacity via SetLayeredWindowAttributes, bits kept across
  Avalonia style rebuilds via Win32Properties.AddWindowStylesCallback.
- --overlay-check gains a Windows readback (style bits, visibility, rect, and a CAPTUREBLT pixel read proving
  the overlay draws); new advisory CI step on the windows job (continue-on-error until its first green run).
- Evidence: OverlayBackendDispatchTests (fail-proofed); Linux --overlay-check still PASS. Two review rounds.
- Not run: anything on Windows locally; the CI step is the only proof. DPI risk on mixed-scale monitors noted.

## avalonia-port/video-miniplayer: +444
- LibVLC video on Avalonia 12 via video callbacks into a WriteableBitmap (LibVLCSharp.Avalonia 3.10.1 only
  supports Avalonia 11; decision logged). One shared LibVLC for audio and video; audio media opt out of video.
- MiniPlayerWindow plays real local video with WPF's controls (silent preview, loop, play/pause/Space, seek,
  ±5 s, Esc, drag, full teardown); slider seek bug fixed.
- --video-check <file>: opens the real window, asserts frames change, seek lands, pause freezes, close frees.
- Evidence: MiniPlayerVideoTests + --video-check (fail-proofed: skipped Play -> exit 1; no teardown -> crash);
  screenshot on the real desktop. Review: ACCEPT.
- Gaps: no production opener (Assets tree held back on content packs), animated GIFs show one frame.

## avalonia-port/overlay-text-families: +746
- Subliminals and bouncing text as click-through overlays on the X11/Win32 mechanism, driven by the existing
  Core schedules/engine, with WPF's look and timing (subliminal envelope from the first painted frame;
  bouncing text per-frame loop with WPF's dt clamp, only the screens near the logo repainted).
- Card toggles arm the features (same decision as flashes). Closing the shell now stops every overlay
  and schedule (was: a UI-less process kept overlays up).
- Evidence: TextOverlayTests (4, fail-proofed); live timelines and X content grabs; shell-close proof with a
  negative control; frame stats 145-155 fps after the per-screen repaint fix. Two review rounds.
- Gaps: audio/haptics/XP hooks, engine gating, video pause, topmost re-assert on Windows.

## avalonia-port/achievement-read-rules: +553
- oracle-deep achievements unit 1: Core AchievementRules (single copy of WPF's thresholds), Achievement and
  AchievementMeter moved to Core, AchievementProgress split (data + pure statics in Core; App-dependent
  streak methods as head-side extension methods, call sites unchanged). Persisted shape unchanged.
- Decisions logged: achievements.json path, Avalonia local-only until auth, streak read-only until skill-tree seams.
- Evidence: AchievementProgressShapeTests pins every persisted property (name, type, order, loads back, no
  converter, double.MaxValue default), fail-proofed. Review: ACCEPT after the test hardening.
- Not run: WPF tests (Windows CI). No Avalonia surface yet.

## avalonia-port/notifications: +254
- In-app corner toasts ported from WPF NotificationService (look, 5 s default, 180/220 ms fades, stacking,
  replay before the host exists, action button, dismiss) into the shell's existing NotificationHost.
- Seeds CoreProgram.NotifyProvider and CoreEntitlement.ShowDeniedHandler like WPF: tier refusals show a toast
  whose "See tiers" opens Settings > Account; the browser's offline block toast is wired too.
- Evidence: NotificationServiceTests (fail-proofed three ways); live: Play > Gaze Minigame Open shows the
  refusal toast, "See tiers" opens Account, x and expiry dismiss; 0 binding errors. Review: FIX -> fixed.
- Gaps: tray balloons (OS notifications, later unit), sticky toasts, Patreon reconnect branch.

## avalonia-port/achievement-store: +392
- Core AchievementStore: WPF's achievements.json load/save lifted verbatim (System.Text.Json indented, UTF-8
  BOM, .tmp -> File.Replace with .bak, .bak fallback, one save lock); WPF AchievementService delegates to it.
- Golden fixture written by the pre-change serializer; tests: byte-identical round trip, fixture covers exactly
  the persisted properties, truncated file recovers from .bak, parsed equality, time-zone behaviour pinned.
  Each fail-proofed. Tests (a)/(e) need TZ pinning and run on the Linux CI job. Review: ACCEPT.

## avalonia-port/tray-icon: +305
- Tray icon (Avalonia TrayIcon; SNI on Linux) with WPF's menu (Show, Wake, separator, Exit) plus Stop everything,
  the no-hotkey panic control: stops flash/subliminal/bouncing text, unticks their Enable flags, keeps the app.
- X hides to tray like WPF (always), hides/restores the avatar tube; with no tray host (no StatusNotifierWatcher
  on the session bus) X really exits so overlays can never outlive a reachable UI. Exit uses Shutdown().
- Decision logged: the icon is always visible on this head (it is the panic control).
- Evidence: ShellTrayTests (2, fail-proofed three ways); live D-Bus: SNI registered, menu layout, Stop everything
  3 overlays -> 0, hide/show/exit; no-watcher dbus-run-session -> X exits. Review: FIX -> fixed.
- Gaps: OS balloons (org.freedesktop.Notifications), Lockdown refusal/exit bill on Exit, off-screen repair.

## avalonia-port/achievement-engine-core: +368
- oracle-deep achievements unit 3: Core AchievementEngine owns progress, the store, TryUnlock (WPF order: unlock ->
  save -> log -> suppress gate -> event once), dirty/autosave/retry, Reset, SuppressPopups and exclusive gating via
  CoreEntitlement. WPF AchievementService delegates; timers, Track* counters, forwarding and UI marshalling stay.
- The current progress is resolved inside the store's save lock (a queued autosave can't resurrect a logged-out
  account's progress after Reset).
- Evidence: AchievementEngineTests (9, each fail-proofed, incl. the Reset race). Review: ACCEPT + P2 race fixed.
- Not run: WPF tests (Windows CI). No Avalonia surface yet (unit 5).
