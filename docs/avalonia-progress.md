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
