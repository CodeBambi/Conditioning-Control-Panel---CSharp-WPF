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

## avalonia-port/achievements-wire: +238
- The Avalonia head creates the Core AchievementEngine (~/.config achievements.json), seeds
  CoreProgram.UnlockAchievementProvider and CoreProgression.TrackBubbleCountResultProvider like WPF, shows
  AchievementPopup per unlock on the UI thread, and saves on exit only when dirty. Free/patron counts and the
  bubble-count streak moved into the engine (WPF delegates). The Achievements tab reads live counts.
- Local-only (decisions log): no sync, no streak writes, no ResetProgress. Test apps use a sandbox path, so no
  test can touch a real achievements.json (proven: the real file is still absent after both desktop suites).
- Fixed on the way: a closed shell kept answering language changes (flaky SpiralHelpPopoverTests).
- Evidence: tab renders 3/50 from a sandbox fixture; sha256 unchanged after a 60 s idle session; tests
  fail-proofed (idle exit writes nothing, dirty exit persists, premium/free counts with literal numbers).
- Gaps: card grid, ItemUnlockedPopup (WardrobeCatalog is WPF-only), no user path reaches an unlock yet.

## avalonia-port/panic-hotkey: +873
- Panic key on Linux. PanicPolicy moved to Core (100% rename). X11/XWayland focus: XInput2 raw key presses on
  the root window (non-consuming, modifier-blind = WPF's low-level hook; layout changes re-resolve the keycode).
  WPF's press handling: lock card and Ctrl+K palette rungs first, then tray Stop everything + lock cards; a second
  press within 2 s exits. Devices rebind wired (an abandoned rebind cancels when the window loses focus).
- Wayland-native focus: GlobalShortcuts portal bound only while effects run (decision C, oracle-deep); fallback
  with a one-time notice when the portal is missing/refused/unanswered (proven live: KDE dialog unanswered -> fallback).
  D-Bus bug found and fixed (MessageWriter struct passed by value -> invalid body -> bus disconnect).
- Discovery: KWin routes Xwayland's XTest through libei, so synthetic presses can't prove anything on the live
  desktop; scripts/panic-check.sh proves the X path in a throwaway kwin --virtual + standalone Xwayland (fail-proven).
- Evidence: PanicKeyTests (5, fail-proofed), --panic-check, --portal-check, --tray-probe; two review rounds.
- Not run: an accepted portal bind + Activated, a physical key press on the live desktop, Windows.

## avalonia-port/quests-core: +456
- Core-extraction unit 4: Quest, QuestProgress and QuestDefinitionService moved to Core (App.Logger -> Log,
  AppVersion via CoreReleaseContent, mod-art resolution via CoreModArt). Reroll methods stay head-side as
  extension methods. One guard allowlist entry: the pack:// root that the saved cache format still requires.
- quests.json and quest_definitions_cache.json stay byte-identical (golden fixtures from the old code, shape +
  round-trip tests, fail-proofed); built-in quest fallback unchanged. Review: ACCEPT.

## avalonia-port/os-notifications: +182
- OS notifications on Linux via org.freedesktop.Notifications (own D-Bus connection; app name + per-user PNG
  icon; transient like a balloon; click restores the window). Wired to WPF's one-time "minimized to tray" hint.
  No notification server -> in-app toast if the window is visible, else drop + log once; Windows falls back.
- Evidence: OsNotificationsTests (fail-proofed); --notify-check live; dbus-monitor capture of the in-app Notify
  with the icon path. Review: ACCEPT + fixes. Other WPF balloon callers await their features on this head.

## avalonia-port/skilltree-service: +137
- Skill-tree ownership rules (HasSkill, CanPurchaseSkill, IsSecretSkillAvailable, sparkle tier, free rerolls,
  reroll bonus) moved from WPF SkillTreeService to Core SkillTreeRules (logic identical, reviewed); WPF delegates.
- Avalonia Enhancements reads owned state through it. Purchasing stays unavailable (auth not ported); no streak
  or skill writes on this head.
- Evidence: SkillTreeRulesTests (thresholds at the boundary), EnhancementsTabDrawsOwnedSkillAsOwned, fail-proofed;
  Keincheck with a sandbox profile owning 2 skills; 0 binding errors. Review: ACCEPT.

## avalonia-port/mods-core-prereqs: +247
- One app version: root Version.props (6.10.3) imported by WPF, Core and Avalonia; WPF stamp unchanged; Avalonia
  no longer reports 1.0.0 (blocked mod MinAppVersion checks), Core no longer 6.9.0. Decision logged.
- ModService unit 1 (oracle-deep plan): PersonaSwitchRules, CompanionContentResolver, ModCompanionContent,
  CountMediaFiles and the Hypnotube default links moved to Core with identical behaviour; seven CoreModsHooks and
  the CoreSession phrase-pool delegates added and seeded lazily in WPF (callers switch over in unit 3).
- Evidence: companion-content tests now run on Linux; new seam test fail-proofed; version before/after. Review: ACCEPT.

## avalonia-port/mods-golden-fixtures: +625
- ModService unit 2: golden fixtures pin the mod persistence formats BEFORE ModService moves to Core. Core tests
  round-trip every mod key in settings.json through SettingsService and a full mod.json; the WPF suite pins pack.json
  and a behaviour golden for Initialize(bambi) -> Activate(sissy) -> Activate(bambi), incl. pool order.
- The WPF suite now always runs in its own temp profile (never a developer's exported one). Each test fail-proofed.
- Not run: the behaviour golden against the real WPF build (Windows CI is its first real run). Review: ACCEPT + P2 fixed.

## avalonia-port/modservice-to-core: +59
- ModService unit 3: commit A swaps every head-only dependency for Core seams (one hook per call site, same order
  and try/catch; reviewed call site by call site). The pool backup reads the session through ONE snapshot delegate,
  so a StopSession between reads can't bring back #906. Commit B: pure git mv of ModService.cs and
  CirceNeutralPackPatch.cs (a dependency the plan missed) into CCP.Core.
- Evidence: the mod behaviour golden (#1799, green on Windows CI against the real WPF build) is reproduced by the
  moved Core ModService in a Linux harness; broken once (mismatch) and restored. Review: ACCEPT.

## avalonia-port/modservice-core-tests: +285
- ModService unit 4: the activation golden runs in CCP.Core.Tests on Linux (WPF copy removed; nothing seam-specific
  lost). New Core tests install a .ccpmod built in the test (backslash entries, traversal, reserved id, MinAppVersion
  above/below) and an uninstall that deletes only mods/<id>; LinuxSmoke gains an Initialize + ActivateMod check.
- Real Linux defect found and fixed: Windows-zipped .ccpmod entries with backslashes installed as flat files. The
  custom extraction runs off Windows only; WPF keeps .NET's ExtractToDirectory exactly. Each test fail-proofed.
  Same defect remains in ContentPackService, ReleaseContentService and the Avalonia ModCreatorWindow (follow-up).

## avalonia-port/mod-art-core-resolution: +199
- ModService unit 6: mod-art path resolution (event skin -> active mod, '..' and rooted paths rejected, .wav/.mp3
  twin) moved from WPF ModResourceResolver into CoreModArt; WPF calls it with caches and public API unchanged.
  Off Windows, lookups ignore case and find backslash-named files from .ccpmod extraction; Windows keeps File.Exists.
- Avalonia seeds CoreModArt in the desktop lifetime (effective once unit 5 creates ModService).
- Evidence: CoreModArtResolutionTests (case, backslash files, traversal incl. rooted skin paths, order), fail-proofed;
  test sandbox asserted. Review: ACCEPT. Follow-ups: cache the Linux probe when mods go live; case-variant folder clashes.

## avalonia-port/ccpmod-backslash-extraction: +54
- One Core helper CcpmodArchive.Extract for every .ccpmod/zip extraction of mod content (ModService, ContentPackService x2,
  ReleaseContentService, both ModCreatorWindow Load paths). Windows: passthrough to ZipFile.ExtractToDirectory with the
  same arguments (identical at every caller, reviewed). Elsewhere: Windows-zipped backslash entries land in folders;
  traversal still rejected; the target folder is created like the framework does. Test fail-proofed. Review: ACCEPT.

## avalonia-port/avalonia-seed-mods: +274
- ModService unit 5: the Avalonia head runs one ModService (desktop lifetime only, after the version seed),
  Initialize(settings.ActiveModId) like WPF, and seeds CoreMods through a new CoreMods.Attach that WPF now also uses
  (same 16 providers, reviewed). Mod-dependent text follows the active mod (live: "Welcome, Good Girl.").
- The Hypnotube known-title table moved to Core, so a legacy-link migration saves canonical titles on either head.
- Evidence: StartModsTests (golden round trip through the head path, fail-proofed); legacy-link migration test with and
  without the WPF provider; live sandbox launch. Review: ACCEPT + P2 fixed. No real profile touched.

## avalonia-port/dialogs-mod-manager-wired: +110
- ModService unit 7: the mod manager activates (+ saves ActiveModId), uninstalls (resets the active mod), installs a
  .ccpmod (picker -> InstallModAsync, WPF messages) and exports; refreshes on ModAvailabilityChanged; opened from the
  shell's Manage Mods. Share hidden until the catalogue client exists.
- Evidence: activate-persists test (fail-proofed); live sandbox: activate -> relaunch still active -> uninstall.
- Not run live: install/export through the native file picker. No live re-theme after a switch (next launch).

## avalonia-port/dialogs-mod-picker-wired: +148
- ModService unit 8: ModPickerDialog.ShowIfNeeded runs WPF's show rules in WPF's order and is called at shell startup on
  the upgrade path (never alongside the first-run wizard). This head has no content-pack service yet (oracle §5), so the
  picker never opens here, like WPF without ReleaseContent (decision logged; it waits for the pack service). The Avalonia
  ModPacks copy is deleted in favour of Core ModService.PackIdForMod (identical mapping).
- Evidence: ShowIfNeeded rule tests (fail-proofed); live sandbox upgrader profile: no picker, settings untouched.

## avalonia-port/engine-start-stop: +402
- Session runner U1: Core CoreEngine mirrors WPF StartEngine/StopEngine (flash always; subliminal, lock-card schedule and
  bouncing text per saved flag; all four skipped under audio-only; head stop hook closes overlays and lock cards). The
  shell Start/Stop drives it; card and wall toggles save while stopped and apply live while running. Tray Stop, panic
  and closing the shell stop the engine and keep saved flags. The session feature lock reads the new
  CoreSession.IsSessionRunning, so a plain Start never greys cards. Decision row replaces arm-from-toggle and tray rows.
- Evidence: CoreEngineTests (arming matrix incl. audio-only), tray and panic tests (fail-proofed); live sandbox: 0 overlays
  on relaunch, Start shows all three, Stop and tray Stop clear them in about 0.1 s with flags On; panic-check PASS, FAIL
  when broken. Review: FIX -> round 1 applied (audio-only, shell close, test cleanup).
- Not run live: a physical panic key on KWin; closing the shell with lock cards up. Stubbed: session stop dialog, Jump
  right in, audio-only audio bed, ramp and scheduler.

## avalonia-port/session-core-pure: +985
- Session runner U2: Core ports (not moves) WPF SessionEngine's pure parts: SessionClock, SessionXp, phase index,
  deferred starts, SessionSettingsSnapshot (WPF's 36 fields), PhrasePoolCustody (FoldUserPoolEdit verbatim). D1/D1a fix
  WPF mod-switch-mid-session pool leaks (D1a in Core ModService, so WPF too, only while a session runs). 50 Core tests
  (fail-proofed); --smoke checks the pool delegates are seeded. Review: FIX -> D1a (oracle-deep) -> ACCEPT. No user path yet.

## avalonia-port/session-runner-core: +425
- Session runner U3a: Core SessionRunner ports WPF SessionEngine Start/Stop/Tick for flash, subliminal (frames, whispers
  flag), bouncing text and lock cards: deferred starts (lock cards get the remaining window, #736), phase index, settings
  snapshot + pool custody restore, engine start/stop, WPF-format session log (XPEarned = banked XP, 0 until progression is
  ported). The session-start ledger is saved before the overrides apply, so a crash never persists session values.
  SubliminalDuration no longer leaks after a session (WPF bug, logged). No user path yet (U3b).
- Evidence: SessionRunnerTests (1-minute run, settings + log goldens, early stop, <30 s no file, deferred starts, ledger
  on disk before overrides), each fail-proofed. Review: ACCEPT, follow-ups applied.

## avalonia-port/secrets-linux: +348
- Auth unit 1: CoreSecrets provider for the Avalonia head: libsecret (*v_sync P/Invoke) on Linux, WPF's exact DPAPI files and
  entropy on Windows, memory-only + a one-time "not remembered" flag without a Secret Service (never plaintext). Seeded
  before SettingsService. Nothing reads secrets yet (login is a later unit).
- Evidence: scripts/secrets-roundtrip.sh runs a throwaway gnome-keyring in Docker: round trip + no-bus/no-file, both
  fail-proofed (and an empty test filter fails). DPAPI WPF-bytes test runs on Windows CI. The real KWallet was never touched.

## avalonia-port/session-runner-wire: OPEN perf finding
- Subliminal cards stay up 1.1-3.1 s instead of 0.23 s when flash and bouncing text run together. It is intermittent. It is not
  dispatcher priority: Render priority on the overlay RunOnce calls was measured and reverted. The suspect is the card's first
  frame arriving late (SubliminalOverlayWindow.Run starts its clock in RequestAnimationFrame). This fails "at least as fast";
  a perf branch follows. Evidence ~/ccp-port/evidence/avalonia-port/session-runner-wire/p2-*.txt.

## avalonia-port/session-runner-wire: +295
- Session runner U3b: the head owns one Core SessionRunner (App.Sessions), started from the Presets session rack with
  WPF's confirm. WPF clock labels on the Start and session buttons, WPF's stop-session confirms, the recap opens by itself
  (Completed / Ended Early), each flash shown is logged, the feature lock greys cards during a session. Tray Stop and
  panic end a session early (pause is U4). SessionClockLabel moved to Core unchanged. Decision: runs only the ported subset.
- Evidence: SessionRunWireTests (fail-proofed); live sandbox 2-minute session: overlays on time, bouncing text at 1:00,
  recap "Completed, 02:00, 5 images" == 5 flash windows and the log on disk, settings.json unchanged except TotalSessions
  and the ledger; early Stop -> Ended Early. Review: ACCEPT.

## avalonia-port/v2-client-core: +731
- Auth unit 2: the V2 HTTP client and its data types moved to Core (CCP.Core/Services/Account/V2AuthService.cs) and the
  XP-curve maths to Core (XpCurve); WPF calls into both (ProgressionService wrappers, ApplyUserDataToSettings stays in WPF
  until unit 6, MergedRecovery hook). No server contract change. Decision: the V2 client and OAuth live in Core.
- Evidence: XpCurveGoldenTests (113 values from the pre-move code), V2AuthServiceWireTests (fake handler: method, URL,
  headers, body transcribed from the WPF source; error paths; tokens never logged), each fail-proofed. Review: ACCEPT.

## avalonia-port/overlay-first-frame-perf: +55
- Root cause of subliminal cards lingering 1-3 s: overlays sat at _NET_WM_WINDOW_OPACITY 0; KWin sends a fully transparent
  XWayland window no frame callbacks, so the vsync'd swap waited on XWayland's 1 s fallback and stalled every overlay and
  the UI thread. X11Overlay.OpacityCardinal floors opacity at 1/(2^32-1) (invisible); a backstop closes each card by
  hold + 200 ms after Show().
- Live 150 s flash + subliminal + bouncing text: card p95 3.06 -> 0.23 s; bouncing-text worst frame gap 2097 -> 62 ms,
  69-152 fps; flash 6.37-6.45 s (WPF 6.45 s). 2 fail-proofed tests. Review: ACCEPT. One unexplained all-overlays vanish at
  131.7 s in one run (log overwritten; suspected stray panic key; not reproduced).

## avalonia-port/loopback-oauth-core: +563
- Auth unit 3: one Core LoopbackOAuth helper (WPF's exact localhost:4783x/callback/, CSRF state, 5-minute timeout, browser
  pages, /…/token exchange); WPF Patreon/Discord/SubscribeStar delegate to it. Windows keeps HttpListener; elsewhere it
  binds 127.0.0.1 and ::1 (managed HttpListener bound only ::1 here). Stray requests (favicon, wrong Host) don't end a
  sign-in; a busy port fails before the browser opens. Decision: OAuth redirect on Linux = loopback.
- Evidence: 19 LoopbackOAuthTests (IPv4/IPv6, bad state, error=, timeout, busy port, stray requests, port reuse, page
  SHA-256 pinned to the WPF originals), each fail-proofed. No real provider sign-in (not provable here). Review: ACCEPT.
## avalonia-port/providers-patreon-substar: +1000 (auth 4a; Patreon/SubscribeStar lifecycle in Core, WPF delegates, Avalonia seeds CoreAccount fail-closed; SecretStore memory-only under CCP_USERDATA_DIR;
review FIX->r1 applied; one earlier live run was unsandboxed: real settings.json not written (mtime before run), no keyring items, two empty mods dirs created and left)

## avalonia-port/session-pause-panic-ramps: +389
- Session runner U4: Core SessionRunner Pause/Resume (clock frozen, 100 XP per pause in the award, Resume restarts only
  features past their start minute), the flash ramp (never reaches settings.json), pink tint (±3 min random start, ramp,
  hidden while paused). Head: pause button with WPF's confirm, panic and tray pause a running session then stop the
  engine (replaces the U3b interim), stop confirm shows the pause penalty.
- The head had no log sink: now stderr + a rolling file under UserData/logs (WPF's package/version), and every engine-stop
  path logs its trigger.
- Evidence: 4 Core tests + SessionRunWireTests (fail-proofed); panic-check.sh session phase PASS, FAIL when broken; live
  sandbox: tray Stop froze the clock, Resume brought back only started features, recap XP +0 (banking not ported; log
  line shows "paused 2x, penalty -200"). Review: ACCEPT, file sink added.

## avalonia-port/art-debt: +372
- Look parity from the ponytail-debt ledger (7 cheap + 23 stale items): shell door medallions, mod-aware hero/side plates
  for five feature cards, Blink Trainer, Graded Intake (side plate bound to the hero like WPF) and the AI permissions hero
  (one helper, ModArt.BindFeaturePlates, painted on attach, decoded once at 800 px), vault backdrop, dashboard card icons,
  first-run pass cards, achievements images (embedded like WPF), EmiBook placement via Core, memory-folder link.
- Evidence: renders of every touched view (all viewed), live sandbox shots of doors and a feature card, 0 binding errors;
  startup-to-shell 2.39-2.44 s before vs 2.42-2.47 s after (noise). FeaturePlateArtTests (fail-proofed).
- Skipped with notes: Remote Control grid art, rail chips, intake pass card, exclusives rim (hue shift not in Core),
  programs sigil (ProgramArt naming not in Core); doors don't follow the mod yet. Review: FIX -> r1 applied.

## avalonia-port/provider-discord: +648
- Auth unit 4b: Discord's token lifecycle (exchange, refresh, validate/identity, auth-token heal, whitelist grant, 24 h
  cache, logout) moved to Core DiscordAccount; WPF DiscordService delegates; token files/entropy/JSON unchanged. The
  Avalonia head seeds it fail-closed beside Patreon/SubscribeStar, all three started in parallel like WPF.
- Evidence: 14 Core tests + seed tests (fake proxy), each fail-proofed; sandboxed live check: Settings > Account signed
  out, 0 binding errors. No real Discord account (not provable here). Review: ACCEPT, follow-up tests added.
## avalonia-port/quiz-core-pure: +997 (quiz unit 1: WPF QuizService's offline half -> Core QuizStore, same files/JSON; WPF delegates; AI quiz stubbed this wave (decision);
goldens from pre-move code, fail-proofed 5 ways; review ACCEPT; stale quiz ponytail notes are reworded in quiz unit 5)

## avalonia-port/login-ui: +801 (auth unit 5)
- OAuth browser flow (authorize URL, PKCE, state) moved to Core; WPF delegates with identical URLs. Avalonia LoginDialog
  wired (providers, register/invite, password, same-email confirm, name check), restore-session check (Core), logout
  (tokens + identity; a failed clear is reported), one-time "sign-in not remembered" notice. Review FIX -> r1 -> ACCEPT.
- Live sandbox: LoginDialog opens with all options, 0 binding errors. Real consent not provable here.

## avalonia-port/popquiz-scheduler-core: +594 (quiz unit 2)
- Core PopQuizScheduler (WPF's 60/rate ±30% tick, disabled -> nothing, no stacking, lock-card defer once then drop,
  busy-queue defer) + question bank/mod resolver behind a 7-member IPopQuizHost; WPF PopQuizService is the host
  (behaviour unchanged). 5 fake-clock tests, each fail-proofed. Review: ACCEPT. No Avalonia host yet (unit 3).

## avalonia-port/popquiz-avalonia-open: +186 (quiz unit 3)
- Pop quizzes are live on the Avalonia head: PopQuizHost opens the topmost PopQuizWindow with the mod-resolved pool,
  chimes via CoreAudio, a one-slot replay after the last lock card closes; a lock card also waits behind an open quiz
  (WPF #763). CoreEngine arms the scheduler on Start and Stop closes everything. Graded Intake "Test pop quiz" works.
- Evidence: PopQuizHostTests (fail-proofed 6 ways); live sandbox: quiz on Start, answering closes it, Stop closes an open
  one in <43 ms, a lock card defers it, Test opens one, broken arming shows none. XP not banked (no provider on this head).
  Review: FIX -> r1 applied. Open: one quiz closed by itself in one run (close reasons now logged).

## avalonia-port/main-20260928b: +90661 (main sync #2, merge only)
- One --no-ff merge of origin/release/6.11.3 (383 commits; decision logged: release/6.11.3 is where the user's work lands,
  GitHub main is unchanged). 6 content + 179 location/delete conflicts resolved toward the stack's layout (Assets/, Core
  delegation); Version.props 6.11.3. Compile fixes in the merge: PresetNaming, ModAudioPolicy, CompanionExperience,
  PersonalitySamples moved to Core. Review: ACCEPT.

## avalonia-port/main-20260928b-compat: +240
- Release behaviour carried into Core: bounce-wall overhang, the CoreAccount identity event, AiEffectControlGate (Avalonia
  grid shows the effective value), CCP Default neutral session words in PhrasePoolCustody. WPF tests read the moved Assets
  and language files through SourceRoots (a Linux test asserts each path exists). 12 parity rows added as missing, plus a
  blocked row for the content-pack script (phase 5). Review: FIX -> r1 applied.

## avalonia-port/nav-rail-hover-parity: +294 (user-reported: side menu looks off, does not open)
- Root cause: the rail's setup hung off OnAttachedToVisualTree, which never fires on a Window, so no hover hook or label
  fade ran live (the clipped "Da"). Setup moves to the constructor; hover reads IsPointerOver (hit-test aware like WPF).
  WPF's 56->236 px 190/150 ms open/close, linear label/pill fade, 44->50 tiles, 40->46 icons, staggered door names.
- Evidence: NavRailHoverTests (fail-proofed 4 ways); live sandbox open/closed screenshots; nav-check and click-through
  pass. Still missing: door glow/shimmer, hue glow, mod door art, watchdog, reduced motion. Review: ACCEPT.

## avalonia-port/lockcard-typed-parity: +221 (user-reported: lock card typing doesn't look/work right)
- Root causes: the success pulse threw (animation on the transform), so a correct repeat never counted; KWin left the
  card a clipped 300x200 box (non-resizable windows aren't maximised). LockCardText moved to Core (WPF matching: ellipsis,
  curly quotes, nbsp); input judged as it lands; the session phrase is unbound from its loc key; focus reclaim; the card
  covers its screen (primary card on the primary screen); 52 px glyph.
- Evidence: LockCardTypingTests (fail-proofed); live sandbox typed card: 3 repeats in 5.1 s, 0 errors. Voice solve
  follows (speech units). Review: FIX -> r1 applied.

## avalonia-port/speech-core-engine: +890 (voice unlock unit 1; user-reported: speak-to-unlock doesn't work)
- The Vosk engine moved into Core (SpeechEngine) behind a mic-source seam; WPF SpeechService derives from it with its NAudio
  mic (API unchanged). Same model, grammar and scoring as WPF (decisions D1-D4, oracle-deep).
- Evidence: SpeechEngineVoskTests with the real model and Vosk's test.wav replayed through the real session path: match,
  reject a wrong phrase (heard text, not a timeout), silence times out; CI fetches the model and requires it. Review: ACCEPT.

## avalonia-port/speech-linux-capture: +249 (voice unlock unit 2)
- PulseMicSource: parec capture (pw-record fallback), pactl source list without monitors, device choice like WPF; seeds
  CoreSpeech on the Avalonia head; Vosk referenced by the head. Recorder stderr/exit logged; the list is cached 5 s.
- Evidence: scripts/speech-capture-check.sh (null sink -> test WAV -> parec on the monitor, never a real mic): match exit
  0, wrong phrase exit 1, no stray recorder (evidence/speech-linux-capture/speech-check-*.txt). Review: ACCEPT.

## avalonia-port/firstrun-folder-picker: +235 (user-reported x2)
- First-run "Choose a content folder" did nothing visible: the click only queued the picker until the wizard closed (a
  WPF modal-on-modal workaround). It now opens the portal folder picker at once, owned by the wizard, through the shell's
  single PickAssetsFolder (same #1053 guard, settings write, images/videos folders); the button shows the chosen folder;
  the wizard can't close under an open picker. Decision logged (deliberate, user-requested).
- The top-right release button read "v6.9.1 IS OUT" on 6.11.3: it now names WPF's btn_v6_11_3_is_out; ReleaseButtonKeyTests
  keeps both shells on the same key (fails on the stale key).
- Evidence: FirstRunFolderPickerTests; live: portal FileChooser.OpenFile with the "First run" window as parent. Review: ACCEPT.

## avalonia-port/cloud-read-only: +475 (auth unit 6, part 1 — high risk)
- Core ProfileAdopt owns WPF's watermark, take-higher, season-forward, curve-epoch and login-adopt rules; EntitlementTierRule
  moved to Core (+ApplyRise). WPF ProfileSyncService / V2AuthServiceHead / SeasonRecapService / EntitlementTierSync /
  App.ValidateRestoredSessionAsync delegate to Core (restore check now Core, per the earlier decision row).
- Avalonia: restore-session, then a READ-ONLY profile load (GET /v2/user/profile); login applies WPF's full adopt. No write
  endpoint anywhere on this head (tested on the startup and login paths).
- Evidence: CloudReadOnlyTests (8 goldens incl. ceremony pending, hand-transcribed from WPF rules; 3 verified by the reviewer
  by hand), no-write assertions fail-proofed. Part 2 (leaderboard, profile card, heartbeat) is a separate branch. Review: ACCEPT.

## avalonia-port/lockcard-voice: +379 (voice unlock unit 3; user-reported)
- Speak-to-unlock on the Avalonia head: voice only when requested + CoreSpeech available + mic consent (WPF :338-342);
  WPF's listen loop on the Core engine (10 s listens, retry, typing after 6 unavailable, states/level/partial on the UI
  thread, stop on close/panic/fallback); DisableVoiceForAll. The Lock Card card's hint names the Linux model folder when no
  Vosk model is installed (none ships yet: ledger row linux-package-vosk-model).
- Evidence: LockCardVoiceTests (WAV-fed: spoken phrase completes, wrong phrase stays open, held mic falls back, no consent
  -> mic never opens, closing mid-listen releases the mic, the hint), each fail-proofed; panel-state frames; capture check.
  No live voice run (would risk the real mic). Review: ACCEPT, follow-ups applied.

## avalonia-port/linux-media-default: +112 (user request)
- On Linux the default media folder (no custom folder picked) is ~/ccp media, created with images/videos/audio/wallpapers
  on first use; both pickers open there. A profile with files already in UserData/assets keeps it (logged, nothing moved);
  sandboxed runs (CCP_USERDATA_DIR) and Windows keep UserData/assets. Decision logged (advisor: user).
- Evidence: LinuxMediaDefaultTests (7, fail-proofed); live run with a temp HOME created <tmp>/ccp media. Review: FIX -> r1.

## avalonia-port/firstrun-gate-dead-clicks: +364 (first-5-minutes fixes)
- 18+ gate ported from WPF: the wizard's consent checkbox (Enter disabled until ticked; closing unaccepted aborts the launch
  like WPF AbortUngatedLaunch) and WPF's app-level "Age Verification" dialog for installs already welcomed but never accepted
  (a crash mid-wizard or a copied WPF settings file). Decision logged for its OK wording.
- Dead clicks fixed: Library rows (Web App via the launcher with WPF's copy-link fallback, Exclusives, Media Log window,
  Phrases editor), dashboard mosaic/split tiles (left-click opens the module, right-click toggles), Vault, companion strip,
  Scheduler/Ramp and Catalogue pills; ramp link labels use WPF's keys.
- Evidence: FirstRunGateDeadClickTests (fail-proofed); live sandbox: gate shown, accept writes the setting, decline exits,
  dashboard and Library routes work, 0 binding errors. Review: FIX -> r1 applied.

## avalonia-port/cloud-read-only-2: +921 (auth unit 6 part 2, read-only)
- Core LeaderboardClient (GET /v3/leaderboard, GET /user/lookup, WPF's ranking) with the models and avatar hue moved into
  Core; WPF LeaderboardService goes through it. The Avalonia leaderboard's sample roster is gone: a real fetch on every
  visit/refresh/mode switch (one at a time, like WPF), your-rank badge, you-bar, honest "Failed to load" offline state.
- The heartbeat is a presence POST, so it waits for unit 7 (ledger row). The profile-card read is in Core; its Trainer Card
  UI is the next layer.
- Evidence: CloudLeaderboardTests (no-write, board, card fields, in-flight guard; fail-proofed); live sandbox: the real public
  board and the offline state. Review: ACCEPT, follow-ups applied.

## avalonia-port/avatar-tube-windowing: +518 (user-reported: tube didn't move on Detach, wasn't on the left when attached)
- WPF's windowing ported: one ToggleDetached for the tube menu and the hero Detach button (detached = topmost + drag from her
  visible parts; attached = docks on the shell's left edge with WPF's formula, TubeDockPlacement moved to Core); follows the
  shell's move/size/state (minimise hides, maximise detaches, restore re-attaches); Attach brings the shell back; mode and
  position saved in WPF DIPs, restored on the right screen after the real scaling arrives (1 s fallback). While attached,
  the tube's input region stops at the seam so it never steals the nav rail's clicks.
- Evidence: TubeDockMathTests, AvatarTubeWindowingTests (fail-proofed); live KWin/XWayland: docks at 767,746 / follows to
  967,946, detach topmost, restore after restart, input region ends at the shell edge. A real WM drag and a real click
  through the margin weren't possible (Keincheck can't drag; the screen was locked). Review: FIX -> r1 applied.

## avalonia-port/play-spiral-bleed: +218 (user-reported: Play's spiral kept showing on other pages)
- Root cause: Avalonia's IsVisible covers only the control itself, so Play's tier rims (drawn in the window's adorner layer),
  ember canvas and badges never saw their tab hide; the rims bled over other tabs and the loops kept burning CPU. New
  EffectiveVisibility.Watch parks/resumes them with their tab; the same fix for Vat glass, the takeover orb and the spiral glyph.
- Evidence: PlaySpiralBleedTests (fail-proofed); live Play -> Home clean x3 (before/after screenshots). Review: ACCEPT.

## avalonia-port/shell-resize-maximize: +251 (user-reported: maximized looks bad; can't resize by the sides)
- The frameless shell gets 5 px edges and 12 px corners (WPF's resize border) that hand the drag to the window manager
  with the right cursor, off while maximized. Min size clamps to the screen's work area (this screen is narrower than it).
- Viewbox: Fill like WPF while the window's shape is within ±15% of the 1585:901 canvas, otherwise Uniform, top-aligned
  (oracle-deep; decision logged). The portrait maximize that stretched everything 2.2x now keeps the art's shape.
- Evidence: ShellResizeMaximizeTests (15, fail-proofed); live maximize/restore screenshots. Not proven live: a real window-manager
  edge drag and the hover cursor. Review: ACCEPT + the oracle's policy.

## avalonia-port/trainer-card: +536 (auth unit 6 part 3, read-only)
- The Trainer Card shows real data: your card (settings, achievements, board rank with WPF's name fallback), My Profile,
  search (exact then partial), leaderboard row double-click; another trainer's card from the board row then GET /user/lookup
  (presence, staff/whitelist pills, achievement tiles). Unknown/offline shows the search plate (offline searches the last
  board). The sample card is gone; the card's text rules moved to Core TrainerCardText (WPF calls them). Board cached 60 s.
- Evidence: TrainerCardTests (fail-proofed); live sandbox: own card real values, a public trainer looked up without signing
  in, offline states, 0 binding errors. Missing: avatar picture, Patreon art, cosmetics, writes (unit 7). Review: ACCEPT.

## avalonia-port/session-runner-popquiz: +74 (quiz unit 4)
- Core SessionRunner drives pop quiz like WPF SessionEngine: starts with the session when the user toggle is on (else stops
  an engine-armed one), stops and closes on pause/panic, restarts on Resume, stops at the end, toggle and rate restored.
  The per-session PopQuiz* minute range is dead in WPF too (BuiltInPrograms.cs:430), so it isn't ported (decision via
  supervisor: WPF parity).
- Evidence: SessionRunnerTests (fail-proofed 4 ways); live 3-minute session: quiz at +46 s, tray panic closed it and paused,
  none while paused, back 43 s after Resume, closed at the end. Review: ACCEPT.

## avalonia-port/quiz-window-static: +303 (quiz unit 5)
- QuizWindow runs on Core QuizStore: categories, WPF's fallback question/profile path, scoring, quiz_history.json in WPF's
  shape, trend. The Graded Intake start button, its AI login gate and the past-quizzes list follow WPF's hidden state
  (pending removal in WPF; decision logged), so no user path reaches the quiz on either head. Fixed the score pulse /
  surrender shake animating the transform (it threw on the first question).
- Evidence: QuizWindowStaticTests (gate, full run with a golden history shape, trend, report), fail-proofed. Review: ACCEPT.

## avalonia-port/sync-body-core: +281 (auth unit 7a)
- Core SyncBody: WPF's sync body in WPF's order, one Known flag per field (unknown fields are omitted, never sent as null);
  WPF sets every flag, so its bytes are identical. The HMAC Signature/SignRequest and WPF's four-field heartbeat body are in
  Core too; Core's heartbeat now sends the four fields. Decision rows for the body builder, unknown fields and heartbeat.
- Evidence: SyncBodyGoldenTests (full, fresh-with-nulls, migration graft, omit-unknown, HMAC, heartbeat; goldens from the
  pre-switch code), fail-proofed. Nothing new is reachable on Avalonia yet (7c). Review: ACCEPT.

## avalonia-port/logout-clear-core: +141 (auth unit 7b)
- Core ProgressionClear.Apply = the settings half of WPF ClearProgressionData (same 28 fields, same order, XP watermark
  cleared, UnifiedId kept); WPF calls it, then saves and resets quests/achievements/skills as before.
- Evidence: ProgressionClearTests (every field), fail-proofed. Avalonia uses it with the push in 7c. Review: ACCEPT.

## avalonia-port/quiz-category-editor: +146 (quiz unit 6)
- The quiz category editor is ported: WPF's validation order and messages (incl. the built-in name clash), template copy,
  save (Id kept on edit) and delete through Core QuizStore; QuizWindow's Create/Edit save through it. Reachable only behind
  WPF's hidden quiz button, as in WPF.
- Evidence: QuizCategoryEditorTests (validation, template, create/edit/delete round trip vs the golden JSON keys),
  fail-proofed 4 ways. Review: ACCEPT.

## avalonia-port/avalonia-sync-push: +785 (auth unit 7c — HIGH RISK; server contract unconfirmed)
- Avalonia pushes /v2/user/sync with only unified_id/xp/level/descent_epoch/achievements (server ∪ local; omitted if the
  profile lacked them); gates: loaded this session + signed in + 30 s cooldown (429/409) + XP watermark. Triggers: after load,
  level-up, XP nudge, session stop, pre-logout, exit (2 s cap). WPF's response rules (level_reset + Descent refusal, adopt,
  watermark) moved to Core. Heartbeat (WPF's four fields) every 120 s after a load. Core ProgressionBank banks XP (no
  multipliers; passive sources skipped until an idle tracker exists). Logout: pre-sync -> heartbeat stop -> identity ->
  ProgressionClear.Apply -> reset; a different account clears progression first; replies after a reset are dropped.
- Evidence: SyncPushTests (13) + SessionRunner XP test, each fail-proofed; sandbox run with no account. Review: FIX -> r1 ->
  ACCEPT. Ledger row server-sync-contract BLOCKED: confirm the server treats absent keys as no change before merging.

## avalonia-port/release-content-core: +669 (content packs L1 — WPF's download path)
- ReleaseContentService moved to Core (pure git mv, then seam edits: Log, CoreSettings, CoreReleaseContent.AppVersion,
  CorePaths.UserData, new UiInvoke seam); WPF delegates with the identical dispatcher rule. Linux: free space measured on the
  mount holding content/; case-sensitive paths off Windows. PendingModChoice (settings half) in Core. CCP_CONTENT_BASE_URL
  honoured only for literal loopback/localhost without userinfo (dev fake server).
- Evidence: new Tests/CCP.Core.Content.Tests (20; loopback fake server that refuses other hosts: happy path, fallback cycle,
  ranged resume, range ignored, hash retry/failure, offline, space, target escape), fail-proofed 12 ways; added to CI.
  Nothing on the Avalonia head uses it yet (L2-L4). Review: ACCEPT.

## avalonia-port/content-pack-script: +94 (content packs L5)
- build-content-packs.ps1 reads pack sources from <repo>/Assets; zip entries and installer deletions use an install-relative
  path (no ../Assets entries). New content-packs workflow (path-filtered): -DeletionsOnly + git diff --exit-code on every
  relevant PR; the cross-commit zip-equality proof runs on demand.
- Evidence: locally with pwsh + a path shim, the deletion list is byte-identical (fail-proofed) and all seven zips and the
  manifest match the pre-move build. The Windows job hasn't run yet. Review: ACCEPT, CI cost follow-ups applied.

## avalonia-port/packs-mod-manager: +400 (content packs L2)
- The Avalonia head builds the Core pack service at desktop startup (WPF's providers and baseline rule; sandboxed runs never
  fetch from GitHub unless pointed at a loopback server) and the Mod Manager's pack rows download, show progress, end
  Installed and refresh the list, as in WPF. The fake content server is shared by both test projects.
- Evidence: ModManagerPackDownloadTests + the sandbox-fetch guard test (fail-proofed); live sandbox against a local fake
  server: not downloaded -> 42% -> "Media downloaded". Review: ACCEPT, follow-ups applied.

## avalonia-port/packs-mod-choice: +709 (content packs L3)
- The header mod combo switches mods live like WPF (activate, save, pending clear, audio-base fetch, palette repaint, selector
  rebuilt outside its own handler); the saved palette applies at startup; Mod Manager switches repaint on close. A chosen mod
  activates when its pack lands or on the next launch. The first-run mod step activates or records + downloads like WPF.
- Evidence: ModChoiceTests (5, fail-proofed); live sandbox against a local fake server: CCP Default -> Dronification re-themes
  with the chip named, a pending Bambi choice auto-activated after its download. Not repainted yet (noted): logo/feature art,
  achievements, skill tree, BambiCloud radio, per-mod presets. Review: FIX -> r1 applied.

## avalonia-port/packs-mod-picker: +227 (content packs L4)
- The mod picker dialog is ported with the live pack service (WPF's show rules, sequential downloads with per-card progress,
  PendingModChoice). The release/6.11.3 WPF removed the returning-user auto-open, so neither head opens it (decision row 31
  superseded); mods are offered via the first-run wizard and Mod Manager. No unprompted manifest fetch in sandboxed runs.
- Evidence: ModChoiceTests (dialog download -> activate; returning-user startup opens nothing and fetches nothing), fail-proofed.
  Review: FIX -> r1 applied.

## avalonia-port/publish-profiles: +265 (installer/updater U2)
- Publish profiles: win-x64 with WPF's settings (self-contained, single-file, native libs bundled, R2R, no trimming) and linux-x64
  (self-contained, loose). Assets/Models copied like WPF. WPF's single-instance mutex name with a named-pipe show/ack (sandboxed
  runs use suffixed names). CI publishes both and runs --smoke from the publish folder. Decision rows: exe name, upgrade in
  place, Linux channels (user brief), updater home, profiles, signal transport.
- Also fixed a test leak: ModChoiceTests left CoreMods pointing at its mod service, so shuffled test order made later tests
  see drone-mode active (10 consecutive green runs after the fix, no retries). Review: pending.

## avalonia-port/release-feed-core: +730 (installer/updater U3)
- Core ReleaseFeed holds the WPF updater's logic, moved verbatim: tag -> version, Setup asset pattern order + size,
  DidUpdateSucceed, update_skip/attempt/result markers (same names, 24 h / 7 day rules), the helper .cmd text (byte goldens
  with CRLF). WPF UpdateService forwards to it and keeps network, elevation, WebView2 cleanup and exit; its AppVersion stays
  until U8 (it seeds CoreReleaseContent).
- Evidence: ReleaseFeedTests (20; a v6.11.3-shaped fixture; helper goldens recorded from the pre-move code), fail-proofed.
  Review: ACCEPT.

## avalonia-port/avalonia-updater: +663 (installer/updater U4)
- The Avalonia head's updater on Core ReleaseFeed: WPF's pending-outcome check at startup, update check (offline skip,
  installed copies only on Windows, 24 h skip), the header pill and a one-time startup dialog after the first-run modals, the
  manual check. Windows downloads and runs the Core helper like WPF (compile-only here); Linux/macOS only notify (open the
  releases page). One Core LoopbackUrl rule guards CCP_CONTENT_BASE_URL and CCP_UPDATE_API_URL (fixed: "loopback" host).
- Evidence: AppUpdaterTests (Linux never downloads, pill, pending outcome, startup dialog + 24 h skip, loopback rule),
  fail-proofed; live sandbox against a loopback fake feed: pill "UPDATE TO v99.1.0", click opens the releases URL.
  Not run: the Windows download/install path. Review: FIX -> r1 applied.

## avalonia-port/linux-package: +201 (installer/updater U7)
- packaging/linux/build-tarball.sh: self-contained linux-x64 tarball with the Vosk small model (sha256-pinned), .desktop +
  128/256 px icons + appstream metainfo, LICENSE; app id and X11 WM_CLASS io.github.CodeBambi.ConditioningControlPanel. AUR PKGBUILD
  (-bin, /opt, MIT) and a Flatpak manifest (GNOME 50, bundled libVLC) consume it. CI builds + smokes the tarball (artifact only).
- Evidence: tarball smoke with the bundled model (fail-proofed by removing it); makepkg -si + --smoke in archlinux:latest;
  WM_CLASS read live. Flatpak never built here (row linux-package-flatpak blocked: first build + WPE question). Review: FIX -> r1.

## avalonia-port/linux-inapp-deps: +222 (user Linux brief, in-app)
- Distro-aware missing-package message (dlopen probe + /etc/os-release -> pacman/apt/dnf command, library list fallback) as a startup
  notice and in the web view panel, with Copy command; portal Registry.Register with the app id before the panic-key bind.
- Evidence: LinuxDependenciesTests (fail-proofed); dbus-monitor: KDE files the shortcut under our id once the .desktop is
  installed. Not live: a really missing library. Review: ACCEPT.

## avalonia-port/catalogue-client-core: +694 (catalogue U1)
- WPF CatalogueService logic moved verbatim to Core CatalogueClient (HTTP, token exchange + per-instance cache, SubmissionResult,
  status mapping incl. 429 Retry-After); WPF is a thin subclass; write gate stays WPF's token + unified id (decision logged).
- Evidence: CatalogueClientTests (15, fake handler, no network), fail-proofed 4 ways. Avalonia wiring is U3. Review: ACCEPT.

## avalonia-port/catalogue-lookup-core: +542 (catalogue U2)
- WPF CatalogueLookupService logic moved to Core CatalogueLookup (lookup by HT URL, download, validate, save with the collision
  suffix; the UI-thread opener is a per-head callback); WPF keeps its Dispatcher behaviour. Avalonia uses the Core CatalogueEntry.
- Evidence: CatalogueLookupTests + the SubmissionSucceeded once/never test, fake handler, no network, fail-proofed. Review: ACCEPT.

## avalonia-port/catalogue-submit-wire: +296 (catalogue U3)
- Deeper library 📤 -> CatalogueSubmitDialog -> Core CatalogueClient -> saved record -> WPF result toast; App.Catalogue built like
  WPF; CCP_CATALOGUE_BASE_URL loopback-only, sandbox without it sends nothing (WPF unaffected).
- Evidence: CatalogueSubmitWireTests (signed in/out) + base-URL theory, fail-proofed; live signed-out: 0 requests, 0 binding errors.
  Signed-in submit not live (no sandbox token). Review: ACCEPT.

## avalonia-port/catalogue-picker-pill: +549 (catalogue U4)
- WPF HT lookup chain on Core CatalogueLookup (toast -> picker or direct download -> opens in the player, UI-thread opener),
  triggered at WPF's point with the requested URL (WebHost has no live-URL hook, so no user path yet: row stub). /mine share-status
  polls at startup, Presets/Deeper tab open (90 s throttle) and Manage Mods; pills on the preset pane, session rack and Mod Manager.
- Evidence: CatalogueShellTests (fake handler, fail-proofed); live: Mod Manager pill "Rejected", 0 binding errors. Review: ACCEPT.

## avalonia-port/login-dialog-fixes: +53 (user live test 2026-09-29)
- Login: an error or late success after the dialog closed crashed the app (closed owner) or signed in silently; now logged and dropped.
- Shell: screen/scaling work-area fit waits 300 ms for the move to settle (it bounced the window between monitors mid-drag).
- Sandbox SecretStore no longer shows the "no secret store" notice. SIGTERM now shuts down through the lifetime (was ignored).
- Evidence: user Discord OAuth live in a sandbox (tokens received); SIGTERM live (exited in <6 s); SecretStoreTests; full gate. Review: ACCEPT.

## avalonia-port/companion-room-refresh: +232
- Companion hero card shows real state (WPF CompanionHeroRuntimeVm): name (from CoreMods), mod, flavour, level/XP, mute/show,
  AI and awareness pills, AI-access plate; mute/show/wake through the shell, Chat opens the tube input, Switch/Engine/Awareness
  scroll the room, the plate opens Patreon; the room re-reads the hero on tab return. Rows stay stub (Tutorial chip placeholder).
- Evidence: CompanionHeroSyncTests (fail-proven; order-independent after the StartModsTests reset); live mute saved, 0 binding errors.
  Review: FIX -> fixed.

## avalonia-port/studio-presets-wire: +367
- Presets New/Save-over/Load/Delete (WPF Presets.cs:2202-2381) with Core CoreEngine.Reconcile (#872: stops what the preset turned
  off, never starts); Studio rack right-click -> ToggleWallFeature; dashboard tile rings follow the saved flags; tile breathing
  pauses on hidden tabs (improvement over WPF, also fixed a headless double-click flake).
- Evidence: StudioPresetsWireTests (2), CoreEngineTests.Reconcile_*, fail-proven; live: presets saved/loaded/deleted, loading
  "Quiet" stopped Subliminal and cleared its ring, 0 binding errors. Rows stay stub. Review: ACCEPT.

## avalonia-port/quest-service-core: +294
- QuestService, QuestHardwareGate and QuestProgressRerolls moved into Core (git mv, 91/94/83% similar); head-only needs behind
  CoreQuests (provider verifying, streak shield, perfect-week bonus, program verifier, completion sound/haptics, camera/mic probes);
  timers post to the UI thread; WPF seeds the seam in App's static ctor and behaves the same. Avalonia tab wiring is next.
- Evidence: QuestServiceTests (roll, reroll, progress+completion; 4 fail-proofs); full gate; WPF tests compile-only here. Review: ACCEPT.

## avalonia-port/session-io: +508
- Sessions: per-row export, delete for custom sessions (Delete/Cancel confirm like WPF), export strip enabled on selection,
  .session.json/.preset.json drop import with WPF validation. SessionLock.Owned ported: 47 of 53 WPF markers greyed during a
  session incl. Haptics (row stays stub: ribbon pulse, program reason line, 6 markers). SessionSummaryPresentation moved to Core
  (git mv); the recap opens passively while a lock card/quiz/bubble count is up.
- Evidence: SessionIoTests (4, fail-proven); live: custom-row delete, lock during Morning Drift, 0 binding errors. Review: FIX -> fixed.

## avalonia-port/quests-tab-live: +486
- Avalonia builds and seeds the Core QuestService: definitions, provider-verifying hooks, completion chime through CoreAudio, real
  camera (/dev/video*) and Pulse mic probes (fail-open like WPF); SkillTree/Programs hooks unseeded. Quests tab and daily cards
  show the live board, weekly card, stats, streak and month calendar; rerolls spend the budget; completion banner, popup and
  sync push. Payout maths is one Core method (QuestService.ScaledQuestXp). Progress sources: flash images, XP awards.
- Evidence: QuestsTabLiveTests (fail-proven); live board and both rerolls. Rows stay stub (FX burst, stamps, art, punch card,
  streak fix, animations). Review: FIX -> fixed.

## avalonia-port/achievements-tab-live: +493
- Achievements tab card grid on Core (WPF order, hidden skipped, blurred/???/requirement, progress bars, Patreon chip,
  All/Unlocked/Locked); built once, one card redrawn per unlock (bursts combined), in-place refresh on show, cached 150 px badges.
  CheckLevelAchievements moved into Core AchievementEngine (WPF delegates); the head runs it on LevelUp plus a silent startup pass.
- Evidence: AchievementsTabGridTests + LevelUp->unlock test, fail-proven; live: session levelled 9->10, "Plastic Initiation" popup
  and card. Rows stay stub (rewards need WardrobeCatalog, tile animations, sound, passive toast). Review: FIX -> fixed.

## avalonia-port/header-xp-live: +270
- Header level chip, LVL label, XP readout and XP bar live from Core progression (CoreSettings + XpCurve); repaint on XP awards,
  level-ups, sign-in, profile load, logout and menu open. Bar fill slides 0.6 s and flashes on level-up (WPF FillXpBarTo /
  CelebrateLevelUp), off at MotionLevel Off. Profile menu Level/XP rail live.
- Evidence: HeaderLevelLiveTests (fail-proven, incl. sign-in/logout repaint and unsubscribe); live Lvl 10, 300/994 XP, 0 binding
  errors. Rows stay stub (odometer, glow dot, chip pop, burst, bank hold, rank title). Review: FIX -> fixed.

## avalonia-port/wardrobe-catalog-core: +370
- WardrobeCatalog moved to Core (git mv); WPF keeps its image half as WardrobeArt and seeds ProgressProvider; the achievement ->
  item map is Core AchievementRewards(). Avalonia achievement cards show reward art/silhouette and name, the Has-a-reward chip,
  the wardrobe counter and real item names; the item-unlocked toast fires 900 ms after a gated unlock (max 3) with real art.
- Evidence: Core WardrobeCatalogTests (3) + AchievementsTabGridTests, fail-proven; live band, silhouettes, counter 1/63, filter,
  0 binding errors. Toast not seen live. Rows stay stub (tile FX; toast inbox/no-activate). Review: ACCEPT. Follow-up: stale
  "WardrobeCatalog is WPF-only" notes in ProfileCustomizeDialog / WardrobeEditorDialog / ProfileWardrobe (profile unit).

## avalonia-port/lab-awareness-live: +701
- FIX (cross-cutting): Avalonia AccountSeed now seeds CoreEntitlement.HasPremiumProvider/HasLabProvider like WPF; paying users had
  no premium anywhere on Avalonia. AccountSeedTests fail-proven.
- Blink Trainer camera-free half: settings, demo loop (art moved to /Assets, WPF path unchanged), premium gate, Core
  BlinkTrainerState status row (WPF delegates), folder library, editors; consent manage/revoke; camera buttons disabled with a
  tooltip. Play card opens it. Rows stay stub (camera, session, calibration). Review: FIX -> fixed.

## avalonia-port/profile-wardrobe-live: +716
- Trainer Card wears the real decoration and charms (own card, board row, lookup), re-placed on resize; Customize opens from the
  hero, its wardrobe section and the Wardrobe editor use Core art; WardrobeStageGeometry moved to Core (git mv).
- Saving pushes the loadout like WPF PersistOwnCosmetics (SyncPush.PushCosmeticsAsync: cosmetics only on this push; empty = WPF's
  clear; a save skipped by the 30 s cooldown rides the next push instead of being dropped - deviation, logged).
- Evidence: ProfileWardrobeTests + ProfileCosmeticsSyncTests, fail-proven (6 breaks); live card/dialogs/editor screenshots. Rows
  stay stub (HoverPop, banners/presets/pins need CosmeticsCatalog, no WPF side-by-side). Review: FIX -> fixed.

## avalonia-port/hover-pop-behavior: +303
- WPF HoverPop ported (Controls/HoverPop.cs: 1.06 back-ease, 4-key wobble, leave ride-home, MotionLevel Off snap, rig on first
  hover) and attached on 16 of 17 WPF sites (Lockdown art absent here); FeatureCard drives it from ApplyHover like WPF.
- Evidence: HoverPopTests (Full/Off, real pointer enter/leave), fail-proven. Live hover not shown: Keincheck hit-test lands on the
  window template Panel in the content area (investigating separately). Rows wired: behavior-hover-pop, shell-profile-wardrobe,
  views-adorned-avatar. Review: ACCEPT.

## avalonia-port/content-hit-test: +82
- Question: Keincheck hit_test in tab content returned the window template Panel. Answer: not an app bug. Keincheck answers
  from the last drawn frame; the live shell was Minimized (first-run window active), so no new frames. Real input reaches the
  controls: ContentHitTestTests (Studio/Presets/Awareness/Quests, hit test + headless press on every visible button), fail-proven.

## avalonia-port/premium-gates-audit: +404
- Premium gates audited against WPF now that entitlement is seeded (#1913). Fixed: the six tab veils were always hidden (free
  users saw premium tabs unlocked) and the eight Play lockbands always shown (patrons saw them locked); unlock buttons were dead;
  the daily free feature and program premium check were never seeded; the Awareness master toggle had no gate. Veils repaint on
  tab switch, sign-in/out, tier change and day rollover. WPF EnforceEntitlementLapse flag half moved to Core EntitlementLapse
  (WPF delegates); startup clears in memory, events save (decision logged).
- Evidence: PremiumGatesTests, EntitlementLapseTests, lapse write test, all fail-proven; live Awareness veil for a free user.
  Rows stay stub (veil FX, Intake band, FREE TODAY stamps). Review: ACCEPT + P2s fixed.

## avalonia-port/exclusives-tab-live: +454
- ExclusiveFeature moved to Core (gates via CoreEntitlement + IntakePassAvailableProvider; WPF seeds the same sources). Vault from
  the real roster with live gates, art and FREE TODAY; cards route to their tabs (For You/Just Drop/Back Room inert with an honest
  tooltip); rail stars read Core; vault and stars repaint on tier/day change. Programs lock from entitlement; enrol disabled.
- Fixes: DailyFree never fetches the real override endpoint from a sandbox (since #1917). HoverPopTests leaked
  CoreSettings.ServiceProvider (#1915), which flaked ShellTray/Quests/Awareness tests in full runs; restored in finally.
- Evidence: ExclusivesVaultTests, rail stars, DailyFreeSandboxTests, all fail-proven; full Avalonia suite 5x green; live vault.
  Rows stay stub (motion, sheens, tier plates). Review: ACCEPT + notes fixed.

## avalonia-port/inbox-friends-core: +93
- Friends wire (FriendsApi), models and notice/landing/poll/sfx rules git-mv'd into CCP.Core/Services/Friends; identity, base URL
  and the merged-account reply hook are passed in (WPF passes the same BackRoomApi/MergedAccountRecovery values). FriendsService
  (DispatcherTimer + App.Settings) and all friends/inbox UI stay in WPF for now.
- Evidence: FriendsApiCoreTests (6, fake HTTP), fail-proven. No UI change. Review: ACCEPT.

## main sync #3: avalonia-port/main-20260929 + -compat (+231)
- Merge of origin/release/6.11.5 (185 commits, 6.11.4 + 6.11.5 "Locktober"); hand edits vs git's automatic merge are exactly the
  14 declared files (conflicts + compile fixes: FriendEvent/FriendReceipts/FriendsSentBook moved to Core, one logger swap, two
  WPF test paths, the update-button key). Every release line added to a file the stack had moved is present at its new path.
- Compat: ServerClock/SyncFailureBackoff to Core; signed requests use the server clock; SyncPush learns the clock, re-signs once on
  clock skew and backs off after failures (not on shutdown cancellations, as WPF); refused Patreon/Discord refresh tokens are not
  resent (as the release). 15 new ledger rows (12 missing, 3 wired). Evidence: new tests fail-proven; full gate green.

## avalonia-port/emidesk-live: +518
- Head EmiDeskService (WPF Toggle/Summon/Dismiss/EnsureWindow/mute prompt) owns whether EMI is out; dock chip, hover x and the
  settings switch route through it; she closes with the shell. Mute silences the tube (WPF Speech.cs:461). EmiState and the
  mute rule (EmiMuteRule) moved to Core, both heads use them; WPF behaviour identical, exit flush kept.
- Evidence: EmiDeskSummonTests (5, fail-proven); live summon/mute/dismiss. Rows stay stub (ring, codex, book, options, chord
  hotkey, face/voice, RefreshOutfit, StopPresentation). Review: FIX -> fixed, ACCEPT.

## avalonia-port/chaster-core: +53
- ChasterClient (App.Logger -> Serilog.Log), 17 pure Chaster rule files (incl. release 6.11.5 CirceLines/CircesMood/TabPriceEdit),
  StakeRules and LockSnapshot/LockLookup moved to CCP.Core with git mv; ChasterClientTests (27, fake handler) now run on Linux.
  ChasterService stays in WPF (App partial + System.Windows.Point); next slice splits CreateForApp and adds a Core point type.
- Evidence: 27 tests, fail-proven; full gate. No UI; ledger notes only. Review: ACCEPT.

## avalonia-port/spiral-descent-live: +373
- DescentCountdownService moved to Core (git mv; CoreSettings/CoreDispatch; WPF speech through a Speaker seam; phase marked before
  speaking like WPF; no ticks after exit). Core SyncPush arms/kills it from descent_countdown (absent = kill switch, as WPF).
  Avalonia shell: spark, T-minus tooltip, corner clock (gold at Terminal), presence per phase; at zero the Live fuse show opens
  unless the account already answered; witnessed flag saved.
- Evidence: DescentFuseLiveTests (5, fail-proven); live T-8m sandbox: gold corner, spark 1.25, zero opened the show, surfaces
  cleared. Rows stay stub (chrome dim, phase lines, rail chip, stage visual, ceremony/migration). Review: ACCEPT + P3s fixed.

## avalonia-port/chaster-service-core: +239
- ChasterService moved to Core (git mv; Core ScreenPoint replaces the WPF Point in NoteAt/BookedAt, converted at the WPF call
  sites; CreateForApp + DEBUG demo in head-side ChasterServiceApp; ChasterLadderApi in Core with injected identity/URL;
  FixedTimeEquals for the state check). Avalonia SecretChasterTokenStore reads/writes WPF's chaster_auth.dat shape via SecretStore.
- WPF source-scan tests (ChasterHooks, Circe, Natasha, ChasterTab*) read all product roots / moved languages and web assets.
- Evidence: ChasterServiceTests (63) on Linux + token-store tests, fail-proven; scan paths proven by script. No UI yet (no
  door: ChasterRailChip unported). Review: FIX -> fixed.

## avalonia-port/bubbles-live: +762
- Plain ambient Bubble Pop runs on this head: Core AmbientBubbles (WPF numbers: speed, wobble, pop, 40/3 cap, cadence, lucky 5%
  x20, 300 XP/day bucket; WPF BubbleService delegates the bucket), CoreEngine start/stop in WPF order. Avalonia BubbleOverlay per
  screen, click-through except on bubbles (X11 input region; Win32 toggles WS_EX_TRANSPARENT under the cursor), pop sound, XP,
  quests; idle loop parks when empty. Card toggles live, N/300 line; header XP tooltip filled on open.
- Evidence: AmbientBubblesTests (0 alloc/step at 40 bubbles), BubbleOverlayTests, fail-proven; live 3 overlays, pops paid XP;
  0.013 ms/step. Windows popping not run. Rows stay stub (trigger/v2 bubbles, lucky FX, Bubble Count). Review: ACCEPT + notes.

## avalonia-port/video-playback-core: +894
- Mandatory Video slice 1: Core MandatoryVideoScheduler (WPF interval x0.8-1.2, 60 s floor, shuffled local pick, 1.3 s pre-roll,
  re-arm on end/Esc, strict keys, secondary-monitor rule, volume, watch credit; WPF delegates). CoreEngine arms it. Avalonia
  MandatoryVideoOverlay: one LibVLC decoder feeding full-screen topmost windows (shared VlcFrameSink). Safety: a strict video
  falls open on the panic key/Esc when no global panic listener is live (as Lock Card #875). Empty library keeps re-arming
  (deviation, logged).
- Evidence: MandatoryVideoSchedulerTests + overlay tests, fail-proven; live first frame 20-45 ms, 21% of one core at 720p30,
  Stop clears in 0.3 s. Rows stay stub (blurred background, attention checks, grace pause, watchdogs, ducking). Review: FIX -> fixed.
## avalonia-port/chaster-tab-live: +1000
- Head builds the Core ChasterService (WPF options, SecretChasterTokenStore; a sandbox reaches only a loopback CCP_CHASTER_API_URL, fail closed otherwise; consent and site links obey it). Rail padlock (ChasterRailChip) opens a read-only tab: account chip, live lock clock, ends line, pills, lock pick, loopback PKCE link, facts, mood line.
- Evidence: ChasterTabLiveTests (fail-proven, real pointer click); live against a loopback fake, 0 binding errors. Rows missing->stub. Review: ACCEPT.

## avalonia-port/chaster-tab-2: +607
- Chaster slice 2: Unlink as WPF (confirm, revoke, SecretStore token cleared), the Run-the-tab switch with the one-time consent
  card, Pause; StartSettle at startup (sandbox loopback-only; outside a sandbox only when on + linked + not paused, as WPF) and the
  hooks this head can raise (quests, dailies board, level-ups; attach-once with a detach); rail chip hover peek and ring-coloured
  glow.
- Evidence: ChasterTab2Tests (4, fail-proven x7); live against a loopback fake (consent, pause, unlink + revoke, 0 binding errors).
  Rows stay stub (FX, peek spring/idle/pulse, program/escape hooks). Review: ACCEPT + P3 fixed.

## avalonia-port/video-attention: +821
- Mandatory Video slice 2: attention checks (Core rules; WPF VideoService/AttentionTargets/AchievementService delegate: target
  count/timing, verdict, +15 XP per catch, (replays+1)*50+200 pass, troll, third-replay mercy, stop/new clip cancels a pending
  replay), blurred fill when the clip letterboxes (64-px downscale, 0.6-0.8 ms/frame, 0 dropped), audio ducking, bouncing-text
  pause, no-videos dialog once per launch, Chaster attention notes (twice per fail, as WPF).
- Evidence: Core + Avalonia tests incl. attention trackers, fail-proven; live replays -> mercy, 3/3 catch, blurred bars, duck
  100->20->100%. Rows stay stub (grace pause, watchdogs, duration filter, bubble pause). Review: ACCEPT + P3s fixed.

## avalonia-port/bubble-count-live: +785
- Bubble Count is live: Core BubbleCountScheduler (WPF schedule 1-10/h +-20% min 60 s, 800 ms lead-in, XP 100 x clip length with a
  3-min cooldown, strict retry + mercy after 3, ambient-bubble pause; WPF delegates interval, XP scaling, target count), armed by
  CoreEngine; the game plays on the shared LibVLC across screens, the answer window pays 250 x clip length; panic closes all;
  strict falls open without a panic listener.
- Evidence: BubbleCountSchedulerTests + BubbleCountGameTests (committed 3 KB clip, runs on CI), fail-proven; live 3 screens,
  correct answer, strict retry. Rows: win-bubble-count-result wired; feat-bubble-count stub (Bambi Freeze lead-in); win-bubble-count
  stub (click raises the game over its bubbles). Review: FIX -> fixed.

## avalonia-port/video-grace-watchdog: +462
- Mandatory Video slice 3: Esc grace pause (WPF #735; 60 s card in the video window, auto-resume; the global panic key grace-pauses
  only when panic-overrides-all is off, else panic wins), lost/dead output replays the clip once then ends (WPF vout heal; deviations
  logged), overrun/max-length guards from the first frame, Test-button stuck/force-reset prompt, ambient bubbles held for the whole
  run, Chaster "video" note, Trainer -25 XP (CompanionPerks moved to Core).
- Evidence: scheduler + overlay + shell panic tests, fail-proven; live grace pause/resume/auto-resume, stuck prompt, panic stop, max
  length. Rows stay stub (UI-freeze watchdog, pack clips, min-duration filter, interaction queue). Review: FIX -> fixed.

## avalonia-port/bambi-freeze: +578
- Bambi Freeze lead-in (WPF BubbleCountService.cs:353) and subliminal whisper audio on Avalonia: Core SubliminalWhisper (90% reset
  roll, 4-8 s / 1-2 s gaps, 50+250 ms whisper-to-card, 500 ms unduck, (sub x master)^1.5 volume, clip lookup mod -> shared ->
  neutral), WPF SubliminalService/BackRoomVoice delegate; ContentLocator folds backslashes off Windows (fixes Windows-style
  paths on Linux).
- Evidence: Core + Avalonia tests (Windows-safe path asserts), fail-proven; live whisper at 40%, freeze card on 3 screens,
  game at +0.8 s, reset at +5.2 s. Rows stay stub (freeze haptic; subliminal StealsFocus, shared host, whisper stop). Review: FIX -> fixed.

## avalonia-port/haptics-core: +864
- HapticService + device manager, mixer, patterns, Buttplug (Intiface) and Lovense providers, ButtplugUrl, FunScript, ToyInput moved to
  Core (git mv; WPF delegates; three head seams). CoreHaptics seeded by both heads (shared auto-connect; exit stop first). Avalonia
  call sites as WPF: bubble pop, bounce, subliminal/freeze timing, video hit/background vibe/FunScript. Panic key and tray stop
  zero the toys first (WPF PanicStopEverySurface). Haptics page: real settings (save only on change), premium-gated enable/connect,
  providers, addresses (#1310), panic/test/status; wizard connect/test. core-guards now rejects using System.Windows in Core.
- Evidence: HapticsCoreTests, HapticsHeadTests (incl. panic zeroes the toy), fail-proven; mock toy only, no real hardware; live gates
  shown without premium. Rows stay stub (toy cards/routing, pattern editors, premium live connect); feat-intiface-url wired.
  Review: FIX -> fixed.

## avalonia-port/lockdown-core: +650
- LockdownService / LockdownStrictHold / LockdownPauseRule moved to Core (git mv; WPF delegates; EscapeKinds + CostsChaster in Core);
  time left now runs on a UTC clock (a DST fall-back or clock set back no longer lengthens a lockdown; affects WPF too, logged).
  Avalonia runs a real lockdown: premium gate, consent, Activate, clock, 5-tap secret phrase; Emergency Exit slab per the oracle-deep
  rule (notice with exact steps and real time left, tripwire, Chaster hold; never ends/restarts). WPF refusals: global panic ignored
  (window text input still works), Stop/tray Stop everything, Exit, user window close, pause, settings palette; minimize trips the
  escape counter; crash recovery at startup; preset/strict-toggle hold.
- Evidence: LockdownTests (7: phrase under StrictLock, global-only key ignore, recovery, preset hold, slab, UTC clock, expiry),
  fail-proven; live sandbox lockdown and unlock. Row stays stub (badge, theme, Possession, Dose, exit games, syskey hook). Reviews:
  FIX -> fixed; final safety check ACCEPT.

## avalonia-port/remote-control-core: +966
- Core RemoteRelay (WPF relay protocol: endpoints, headers, PIN/pairing URL, 5 s poll, backoff, idle/expiry; sandbox loopback-only)
  and RemoteCommands; WPF delegates constants/PIN/URL/labels. Avalonia Remote Control tab pairs: premium gate, login, double
  waiver, code/PIN/QR, status, tiers, Stop, command log; share-avatar pushes at once.
- Escape integrity (oracle-deep, logged): disable_panic always refused; under Lockdown only enable_strict_lock refused; the waiver
  says a controller can never turn the panic key off.
- Evidence: RemoteRelayTests (fake relay) + RemoteControlTabTests, fail-proven; live gate/login dialog/tiers, 0 binding errors. No real
  relay pairing tested. Rows stay stub (controller overlay, session/feature commands, toasts, 409 recovery). Reviews: FIX -> ACCEPT.

## avalonia-port/awareness-observer: +730
- Legacy Awareness Mode title observer (WindowAwarenessService + AppClusterMap) moved to Core (git mv; WPF builds it with its user32
  reader and no filter, unchanged). Linux reads _NET_ACTIVE_WINDOW/_NET_WM_NAME (XWayland windows only). Avalonia applies
  AwarenessPrivacyRules before anything reads the title, logs included (decision logged); entitlement re-checked every tick, lapse
  stops it; consent-gated start; the tube speaks the preset line only (no AI here, nothing leaves the machine). Fixed null
  AppClusterMap tables without an override file.
- Evidence: WindowAwarenessServiceTests (9) + tube reaction test, fail-proven; live test windows only. Rows stay stub (OCR/keyword
  Awareness tab, v2 observer, privacy view). Review: ACCEPT + notes fixed.

## avalonia-port/she-listening: +360
- Core VoiceInputRules (armed rule, modes to run with entitlement/consent/availability, wake-phrase parse + variants, sensitivity
  maths); WPF AutonomyService.Voice and MainWindow.SheListening delegate. She's Listening tab wired over real settings behind the voice
  gate (Start/Stop, consent, revoke, mantras, calibrate notice, device/models folders); Stop/revoke cut voice lock-card capture.
  The mic never opens on this head (no voice-command consumer yet); armed shows an honest "not on this build" status (decision logged).
- Evidence: VoiceInputRulesTests + Vosk WAV test + SheListeningTests (fakes/WAV only, never the real mic), fail-proven. Rows stay stub
  (blocker: AutonomyService.VoiceCommands). Review: ACCEPT.

## avalonia-port/autonomy-core: +928
- Takeover decisions moved to Core AutonomyScheduler (gate with takeover free day re-checked before every action, cooldown, busy retry,
  jitter x time-of-day, idle trigger, mood/intensity weighted pick, announce roll + delay, stop generation); WPF AutonomyService
  delegates (mantra check order kept: no speech probe without mic consent). Avalonia runs Takeover (switch/Start-Stop, Lockdown hold
  with WPF message only, resume opt-in, state hero) with Flash/Subliminal/LockCard/Video/Bubbles/BouncingText. Panic stops Takeover:
  confirmed as WPF's real behaviour (oracle-deep, logged).
- Evidence: AutonomySchedulerTests (fake clock: entitlement, stop generation, busy, weighted pick, idle) + TakeoverTests, fail-proven;
  live Start -> three actions -> Stop. Rows stay stub (other actions, banner/countdown/audio, voice commands). Review: ACCEPT + notes.

## avalonia-port/companion-ai-core: +603
- AiService, BambiSprite prompt assembly, PersonalityService (+ModPersonalityPicks, CompanionQuota, CompanionProxyContract, AiTextHygiene,
  CompanionLinkIndex) moved to Core (git mv; WPF seeds the Core seams in App's static ctor). Avalonia tube chat sends through Core
  AiService on WPF's brain-off path: moderation on input and output, sign-in + AI access gate, loopback-only CCP_AI_BASE_URL (sandbox
  fails closed), one ModerationCounter with cooldown, no chat text logged. WPF prompt tests seed CoreMods.Service again.
- Evidence: AiServiceProxyTests (5), TubeChatAiTests, BambiSpriteCoreModsTests, fail-proven; live signed out: 0 proxy requests.
  Rows stay stub (CompanionBrain default route, thinking animation, warning dialog, local providers). Review: FIX -> fixed.

## avalonia-port/companion-brain-core: +380
- CompanionBrain slice 1: CompanionBrain, PromptAssembler, MemoryStore, CompanionSessionStore, ChatSession, CompanionTurn,
  ConversationRecall, ExplicitMemory, CompanionMemoryMaintenance, ConversationDelivery, ConversationRelationship moved to Core (git mv;
  head-only deps are static seams WPF seeds before building the brain; no-settings reads keep WPF's false default). Avalonia App.Brain;
  tube chat routes through it while UseCompanionBrain is on (WPF ChatInput.cs:772); ask cards off in this slice (on since companion-brain-tube, only when a card can follow).
- Evidence: TubeChatBrainTests (loopback fake, protocol 2) + ConversationDeliveryCardTests, fail-proven; live signed out: 0 requests,
  no chat text logged or stored. Row feat-companion-brain stub. Plan: ~/ccp-port/briefs/companion-brain-plan.md. Review: FIX -> fixed.

## avalonia-port/voice-commands + voice-commands-safety: +974 / +162
- She's Listening gets its consumer: WPF VoiceCommands moved to Core (git mv; WPF delegates via VoiceCommandHost). Avalonia Vosk wake
  loop + push-to-talk over PulseMicSource only with consent + armed + entitled; Stop/revoke/lapse/shutdown close the mic and cancel any
  command in flight (no re-prompt can reopen it); a cancelled session never starts parec. Panic aborts capture, loop stays armed; spoken
  safe word works under Lockdown (oracle-deep). Help and "What you can say" list only runnable commands.
- Evidence: VoiceCommandsTests (Avalonia WAV-fed + Core), SpeechEngine cancelled-token test, fail-proven; never the real mic. Rows stub.
  Split at the 1000-line cap. Review: FIX -> fixed.

## avalonia-port/webcam-core: +494
- Webcam slice 1: WebcamCalibrationData moved to Core (git mv; same file and format); new Core WebcamPipeline holds the frame-free logic
  verbatim (BlinkDetector thresholds + 2 s hold + cooldown, GazeSideClassifier hysteresis + fallback, OneEuroFilter); WPF
  WebcamTrackingService delegates (-330 lines, same capture thread, no new allocations). Capture stack decided (oracle): OpenCvSharp4
  + OnnxRuntime on both heads.
- Evidence: WebcamPipelineTests (9, synthetic streams, fail-proven); no camera opened. Row shell-webcam stub. Review: ACCEPT.

## avalonia-port/test-flakes: +69
- Three order/timing flakes fixed at the root: {loc:Str} bindings no longer overwrite text set from code on a language switch (a real
  product bug; WPF semantics), HoverPop's test steps an injectable clock instead of sampling wall time, and tests flush their private
  settings service so a queued save cannot land in the next test.
- Evidence: full CCP.Avalonia.Tests 5 runs in a row green; each fix fail-proven (language switch keeps code text, untouched labels
  still re-localize).

## avalonia-port/webcam-capture + webcam-capture-safety: +921 / +168
- Detectors (BlazeFace/FaceMesh/Iris) moved to Core FaceModels.cs (WPF delegates). Avalonia WebcamTracker: IFrameSource seam, OpenCV V4L2
  source (OpenCvSharp4 4.13 + OnnxRuntime 1.20.1; Linux natives only on Linux), frames only in memory, consent at start and every frame,
  each session owns and always releases its camera, an abandoned loop blocks a new Start until it exits. Blink Trainer tracker toggle.
- Evidence: WebcamTrackerTests (fake source), fail-proven; ldd clean; tarball +54.1 MB; live with the device unavailable. Never a real camera.

## avalonia-port/companion-brain-tube: +692
- CompanionBrain slice 2: WPF thinking bubble (Core ThinkingPhrases) and double bounce; ask cards after a reply and unprompted
  (CompanionAskService + Ask* moved to Core behind 7 seams WPF seeds); a card is promised only when one can be built; busy = session,
  video, lock card, bubble count, pop quiz; the ask timer stops on exit on both heads.
- Evidence: TubeChatParityTests (loopback fake, protocol 2, fail-proven x9); live ask cards reached the tube. Rows stay stub. Review: FIX -> fixed.

## avalonia-port/companion-brain-signals: +222
- CompanionBrain slice 3: MemorySignalWriter moved to Core (head sources via SourcesHook/DeferredSourcesHook, WPF wiring unchanged);
  Core MandatoryVideoScheduler.VideoStarted; Avalonia seeds SignalMirrorFactory (profile, level-ups, feature use, video starts, chat turns).
- Evidence: MemorySignalSeedTests + VideoStarted test, fail-proven x3; live memory.json profile. Row stays stub (bark echo, mantra/drain/wipe).

## avalonia-port/inbox-flyout: +612
- InboxItem/StartupQueueCore moved to Core; new Core StartupInbox (WPF StartupPresenter delegates). Avalonia StartupLadder parks passive
  cards (programs intro, wardrobe toasts) through the Core quiet rule; shell badge (99+, hidden at 0) opens InboxFlyout.
- Evidence: InboxFlyoutTests (3, fail-proven); render-all. ctrl-inbox-flyout wired, shell-inbox stub.

## avalonia-port/chaos-hub-state: +272
- Chaos slice 1: ChaosMetaStore + ChaosRanks moved to Core (WPF ChaosMeta delegates). Avalonia slot picker shows real saves/ranks/locks,
  erase with WPF confirm, persists the chosen slot; hub balances/rank/stats read the active save; overlay uses the Core ranks.
- Evidence: ChaosMetaStoreTests (incl. legacy migration), ChaosHubStateTests, fail-proven. Rows stay stub (no opener, shelves, ChaosModeService, waves).

## avalonia-port/blink-trainer-session: +777
- Webcam slice 3 part 1: Blink Trainer session on Avalonia (click-through overlay per screen, swap per blink, looped video, auto-stop,
  haptic, quest credit), tab Start/Stop/countdown/preview, CoreWebcam seeded (Devices Start tracking/Privacy/Revoke). Merge with part 2.

## avalonia-port/blink-trainer-session-safety: +338
- Blink Trainer review fixes (revoke deletes calibration, close-to-tray stop, panic during start, cached preview pool, haptic off-thread) and
  the oracle-deep decision: every panic and the tray Stop everything also stop the camera (consent kept, notice shown). Fail-proven.

## avalonia-port/chaster-bill: +918
- Chaster exit bill (tray/Settings Exit; panic, 18+ refusal and ungated first run skip it), import confirm on both preset paths, account badge,
  booked "+time" flash on the rail padlock (Core BookedFlashPlan). Faked-write secret tests now read an empty store, never the keyring.
- Evidence: ChasterBillTests (7, fail-proven), headless frames. Rows wired x4, receipt stub. No Keincheck run.

## avalonia-port/mantra-service: +499
- MantraService moved to Core (WPF seeds XP/quest/Chaster seams); Avalonia MantraWindow on App.Mantra with synthesised tones and a LibVLC
  drone at WPF's volumes; exit cleanup; mantra favourite in memory signals.
- Evidence: MantraServiceTests, MantraWindowSessionTests, fail-proven. win-mantra stays stub (no opener, animations).

## avalonia-port/webhost-live-url: +240
- WebHost live URL (CurrentUrl, NavigationCompleted, always-navigate Navigate); site buttons/Reload navigate; the HT catalogue lookup fires on
  completion at WPF's point; CatalogueLookup obeys the sandbox/loopback rule. Fail-proven tests; completion not seen live (no web engine here).

## avalonia-port/emidesk-ring: +310
- Core EmiCodexChapters (WPF EmiCodex delegates); the Avalonia Codex reads the shipped chapters and keeps its bookmark; EmiDesk summon count
  and placement through EmiState. Fail-proven tests. Ring/Book/Options and the Codex opener stay stub.

## avalonia-port/launcher-core: +999
- Launcher slice 1: Core LauncherCards + rules (WPF builds tiles from them); Avalonia LauncherWindow with the openable cards, the CC Labs
  door and WPF's close rule. Fail-proven tests; Keincheck live. Rows missing -> stub.

## avalonia-port/takeover-actions: +480
- Takeover performs Pink Filter pulse (handed back on every stop path incl. app exit), Bubble Count, Comment (AI or WPF-style Giggle preset)
  and seeded Mind Wipe; unavailable actions are never picked. Wallpaper picker and voice hint. TakeoverTests (7), fail-proven.

## avalonia-port/graded-intake: +397
- IntakePassService in Core (each head passes its entitlement hooks; Avalonia hooks them in AccountSeed.Seed); the Graded Intake tab paints
  WPF's four pass gates and Begin Intake's checks. GradedIntakeGateTests (4), fail-proven. The intake run itself isn't ported yet.

## avalonia-port/friends-drawer: +1002
- Core FriendsService (IUiTimer seam; WPF FriendsServiceApp); Avalonia friends rail chip + drawer (presence, requests, block/remove with
  confirm, report, add by code), loopback-only in a sandbox, disposed on exit. FriendsDrawerTests (11), fail-proven. Rows stub.

## avalonia-port/chaos-shelves: +660
- Chaos slice 2: ChaosMeta/Upgrades/Boons/Lessons/Reveal in Core, ChaosBench; WPF run effects keyed by id; the Avalonia hub's shelves spend
  and save the active slot (one atomic save per buy); rendering never writes. Fail-proven tests. Rows stay stub.

## avalonia-port/webcam-gaze: +988
- Webcam slice 4 part 1: Core GazeEngine (WPF delegates); tracker gaze/face/head-pose feed with the WPF-profile calibration; Tracker Test
  and Quick Recal ported. Synthetic-frame tests, fail-proven. Merge with part 2.

## avalonia-port/sandbox-net-guard: +615
- Core SandboxNet: a sandbox sends every non-loopback HTTP/websocket request to http://127.0.0.1:0/ (refused, no DNS); ExternalOpener is the
  only link/file opener and refuses remote and UNC targets in a sandbox; SafeHyperlinkButton; scan tests block new bypasses. Fail-proven.

## avalonia-port/friends-drawer-fixes: +78
- Friends review fixes: dispose on exit, sandbox URL tests, presence marks the ask, remove/report tests, manual-tick timer test, ledger stubs.

## avalonia-port/intake-host: +574
- Graded Intake host slice 1: Core IntakeRun (latches, heartbeat, grading, caps, drafting, pass only on a parsed result; WPF delegates);
  Avalonia IntakeHostWindow + WebHost message bridge, not opened yet (page serving is slice 2). Fail-proven tests.

## avalonia-port/companion-brain-effects: +701
- CompanionBrain slice 4 part 1: AI command gate + 11 effect commands in Core (head Surface hooks); Avalonia executor and activities.

## avalonia-port/launcher-boot: +355
- Launcher slice 2: WPF's boot decision (Lockdown/18+/fresh install → panel), skip box, tray Back to CC Labs, second-instance handoff;
  the tube stays down during a hidden boot. Fail-proven tests; Keincheck live boot + handoffs.

## avalonia-port/web-assets-host: +282
- WebAssetServer: token-gated loopback HTTP over Resources/web (WebKitGTK has no custom scheme in Avalonia 12.1); Assets/web shipped with
  WPF's subset (+~158 MB). Hardened (constant-time token, per-port cookie, nosniff/no-referrer, symlink confinement). Fail-proven tests.

## avalonia-port/webcam-calibration: +993
- Webcam slice 5 part 1: Core WebcamCalibrationFit (WPF delegates); the Avalonia 16-point calibration window; Calibrate enabled. Merge with part 2.

## avalonia-port/webcam-calibration-fixes: +69
- A revoke during calibration stands; atomic calibration save (both heads); failed save keeps the old calibration. Row back to stub.

## avalonia-port/companion-brain-effects-safety: +319
- getbacktome follow-ups re-gated at fire time (3 per follow-up), every panic route on both heads cancels pending AI follow-ups and drops a late reply.

## avalonia-port/emidesk-ring-2: +822
- EmiSuggester + EmiDoors in Core; the Avalonia desk opens the ring with real openers (surface-less doors hidden); Options click-away via
  an X11 poll that keeps no key state; Book TAKE ME THERE. Fail-proven tests. Rows stay stub.

## avalonia-port/companion-awareness-brain: +229
- Awareness reactions take WPF's legacy AI-first route only with v2 off + consent + chat + AI; v2 on stays preset-only and sends nothing.
  Privacy tests (loopback), fail-proven. Arbiter chain stays WPF-only (decision logged).

## avalonia-port/chaster-ladder: +835
- Chaster tab ladder: clock, raffle card, top-ten scrap with opt-ins off by default (nothing posted until opted in); loopback-only in a sandbox.

## avalonia-port/intake-page: +655
- Graded Intake slice 2: Begin Intake opens the served intake page (origin-fenced, same-document); media served media-only and never from
  the profile; completion signals; pass only on a parsed result. Fail-proven tests; the page itself not run live (no web engine here).

## avalonia-port/calib-flake: +46
- Calibration window timing behind a TimeProvider; the test steps a manual clock (no sleeps). Full suite 3x green under load.

## avalonia-port/spoken-mantras: +577
- Spoken mantras: Core SpokenMantra + MantraVoiceService (WPF delegates); Takeover Spoken Mantra and the wake/PTT fallback; the mic opens only
  after her clip ends; panic/revoke close it. WAV + fake-mic tests, fail-proven.

## avalonia-port/ci-sweep: +126
- CI: Avalonia tests sharded (5 processes, completeness guard) to stay under the runner's memory; root-cause fixes for the Windows/Linux CI test failures on #1962–#1990. Native leak logged for follow-up.

## avalonia-port/her-room-chat: +716
- Her Room chat and memory diary live over the brain (Core CompanionRoomLogic + CompanionMemoryViewModel, WPF delegates); forget/wipe/switch-off
  really delete, incl. temp/backup siblings. Fail-proven tests.

## avalonia-port/flake-minepoll: +17
- Test-order flakes fixed at the source: a leaked auth token (MinePoll) and a shared awareness cooldown (AWindowChange). OpensPlays unconfirmed.

## avalonia-port/chaster-receipt: +652
- Chaster tab: own receipt panel with language re-render, calendar sheet, 2x2 preset keys (mood + limits refresh), 30 s hero tick; ticks only while visible.

## avalonia-port/chaos-waves: +448
- Chaos run engine (clock, end/Relapse, waves/acts, payout scalars) in Core as ChaosRunEngine; ChaosModels/ChaosRunEffects moved to Core; WPF RunTick delegates, behaviour unchanged.
- Avalonia ChaosHudWindow binds the Core ChaosRunState; runs still cannot start on Avalonia (no bubble field/draft/overlays); chaos-hud-window stays stub.

## avalonia-port/test-isolation: +214
- Assembly-wide per-test snapshot/restore of process-wide statics, CoreSettings and test-profile json (order leaks impossible by construction); optional seeded test order.
