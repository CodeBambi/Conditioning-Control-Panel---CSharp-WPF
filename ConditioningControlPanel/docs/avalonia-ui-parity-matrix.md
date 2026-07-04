# Avalonia UI Parity Matrix — RESET 2026-07-02 (WS0)

**Second full reset, by owner ruling (2026-07-02).** The port was built largely by hand;
no prior verification claim is trusted. The 2026-06-23 sweep marked every row `[x]` from
the `--smoke-test` harness, which only proves surfaces open without exceptions — it does
NOT prove behavior. All of those marks are void. The old matrix with its evidence text is
in git history (HEAD `733c5362` and earlier).

**This matrix is governed by `docs/skia-rebuild-goal.md` WS0.** A row earns `[x]` only
through a WS0 lot pass: (1) exercised end-to-end in the running app against the WPF
behavior contract, (2) adversarial rubric review of the code, (3) proportionate
optimality check. Record evidence in the row: what was exercised, on which head, vs
which WPF behavior. **A smoke-test visit alone never earns `[x]`** — that is exactly how
the void marks happened.

> WPF `main` keeps moving (6.2.x as of 2026-07). Verify against the CURRENT WPF head,
> not memory; grep `<Version>`/`AppVersion` fresh (see `port-audit`). Sync deltas are
> tracked in plan §19.3.

## Status

- `[ ]` unverified — **default. Do not trust; nothing is "done" until exercised.**
- `[x]` verified — WS0-era evidence in the row, per the rules above.
- 🚧 partial — works but with a noted gap (gap must have a task-board row).
- ❌ broken / stub.

## WS0 review lots (service-area passes; see goal doc for the three checks)

| # | Lot | Status | Evidence / notes |
|---|---|---|---|
| 1 | Data/settings persistence + paths | **passed 2026-07-02** | 7 defects found+fixed (`e9501ce8`,`a2d1b9a8`,`b694b543`): secret-store no-op regression, 2 missing migrations, exit-flush gaps, quests serializer, roadmap drift, Roaming-migration orphaning, corrupt-file quarantine. Core tests 108→119; smoke 45 tabs clean for this area; deferred rows on task board ("Discovered by WS0 lot 1"). ⚠ RE-OPENED by merge `5ce70de6` (P0): `ProfileSyncService` is entirely unported — server profile sync + prestige/season-reset policy missing (`PermanentIds` tree-reset, monotonic `lifetime_points_spent`, `OnSeasonReset`); see "Sync-from-main: merge 5ce70de6" backlog |
| 2 | Session engine + start/stop | **passed 2026-07-03** | 15 verified divergences fixed across 4 commits (`9cd64bce`,`70857d09`,`44fc1421`,`61f339fc`): preset settings apply/restore, session-log single-owner w/ real elapsed/XP, per-session + manual intensity ramps, scheduler auto start/stop, engine-only plain START (WPF parity), delayed feature starts + bubble bursts, pause stops stimuli, dead VM panic path (P0), lockdown gates, autonomy arm/stop, launch behaviors, conditioning-tracker corruption (P0), achievement snapshots. 19-agent adversarial verify; Core tests 119→159; smoke: baseline noise only + 1 known stale harness assertion (chip filed; message proves engine-only contract). NOT exercised: live side-by-side pause/panic/scheduler-window vs WPF. ⚠ RE-OPENED by merge `5ce70de6`: (a) P0 data-loss — the session ramp wrote ramped pink/spiral opacity into auto-saving `settings.Current` (`SessionService.cs:400,408`), froze at max on crash — **FIXED `b1336991`** (ported WPF #471/#476 overlay direct-drive + new `ReleaseOpacityRampHolds`; +2 regression tests, Core 164→166). NOTE: `IntensityRampService.cs:122,128` was a FALSE-POSITIVE — WPF `RampTimer_Tick` (StartStop.cs:466,474) also writes settings, so those writes are faithful parity, left as-is. (b) #462 interaction-race cluster still OPEN — see "Sync-from-main" backlog |
| 3 | Overlays/compositor + click-through input | **passed 2026-07-03** | 55 findings (3 confirmed/11 partial verdicts + 36 rubric + 5 critic) fixed across 3 commits (`0c43277f`,`d45eaf9f`,`22798e8e`): P0 capture-affinity dual-surface split (brain-drain excluded from capture again, subliminals stay IN); P0 SKImage dispose-vs-render race; real brain-drain blur + spiral GIF frame animation + 0.1 opacity factor; engine idle watchdog (no destructive auto-stop); physical-px coordinate contract (mixed-DPI hit-tests); hotplug reconcile; single-display honored; FlashClickable/gaze-pop gating; hold-to-defuse parity; ad-hoc overlay ownership protocol; live opacity pickup (lot-2 ramps visible); keyword-highlight live capture toggle; topmost watchdog + DesktopMode. Core tests 159→164; `--verify-spiral` PASS (was always-fail stale harness, rewritten); smoke 0 findings / 0 first-chance exceptions (spiral.gif avares errors gone). Click-SWALLOW gap documented as WS2 (hook never swallows; pops leak the click — WPF parity deferred by goal design). NOT exercised: live multi-monitor hotplug/mixed-DPI/OBS-capture manual checks (steps listed in ledger row); benchmark startup +~0.8s vs optimized baseline filed for WS3. ⚠ RE-OPENED by merge `5ce70de6`: `SubliminalSolidMode` #461 (route subliminals through the shared click-through host) + animated-.webp gate-broadening (`SkiaImageDecoder` already decodes webp format-agnostically — just broaden the extension gates). See "Sync-from-main" backlog |
| 4 | Video/audio | **passed 2026-07-03** | 78 findings (5-agent adversarial: 22 core video / 9 variants / 18 audio / 7 attention / 22 rubric), criticals re-verified first-hand, fixed across 4 commits (`1bef3b5b`,`32ad38f8`,`d6222069`,`a8d3df33`): legacy video path restored as the live default (compositor layers were shadowing it unproven — videos rendered nothing; CCP_UCE_VIDEO=1 is the WS1 dev opt-in) + compositor-end scheduler unblock; P0 ducker crash-persistence + WPF relative/refcounted duck semantics (rescan, retry, watchdog, WebView2 exempt); PrimaryPlaybackTimeMs ×1000 unit bug; shuffled anti-repeat queue + content-pack videos + DisabledAssetPaths; duck/flash/bubbles coordinated around videos; companion −25 XP fail penalty; ESC/panic + strict-key contract on both window paths; DualMonitorEnabled/FillAllMonitorsWithVideo gates + no-activate secondaries; VideoHardwareDecoding honored; position-based watch credit; VideoAboutToStart + 1.3s pre-announce; FloatingText physical-px/mixed-DPI + TOOLWINDOW\|NOACTIVATE + HWND_TOPMOST re-assert; reentrant message pump deleted; UI-thread LibVLC dispose deadlocks deferred; gaze attention-check returned to WPF pre-ship dormancy (tab removed); spike harness Debug-gated; wallpaper original restore (base+Linux). Core tests 164/164; smoke 2/3 clean (44 tabs; 1 intermittent unattributed brush crash — row filed). NOT exercised: live side-by-side playback/ducking/multi-monitor vs WPF; OBS capture check. Deferred rows on task board ("Discovered by WS0 lot 4"). ⚠ RE-OPENED by merge `5ce70de6`: session-summary vs video-teardown defer (#462, `ShowSessionSummaryWhenClear`). See "Sync-from-main" backlog |
| 5 | Speech/mic + gaze/calibration | **passed 2026-07-03** | 41 findings (4-agent adversarial: 10 speech / 14 webcam-gaze / 6 calibration-status / 11 privacy-rubric), criticals re-verified first-hand, fixed across 4 commits (`2da98c0e` wave A, `53c4b0ab` wave B, `09d13f55` wave C, +MVVMTK follow-up): P0 wake-calibrate opened mic with no MicConsentGiven gate + new Avalonia MicConsentDialog port (3-step informed consent); P1 one-click webcam GrantConsent bypass rerouted through the multi-gate dialog; ConsentVersion made one source of truth (new Core `WebcamConsent.cs`) + version-aware gates (stale consent re-prompts instead of a dead toggle); the 3 calibration windows were FAKE-SUCCESS shells (random-noise tracker dot, “Done” that persisted nothing) — now honest (“not available yet”, REAL quick-recal that samples OnGazeMove + persists RuntimeOffset, REAL tracker test following gaze); `IWebcamService` seam completed (7 events + HasCalibration/RuntimeOffset/EnumerateDevices); cold-start race fixed (bounded readiness wait); gaze-focus calibration prerequisite restored; debug-cursor DIP→physical-px projection; GazeDriftCorrection Dispose handler leaks; device combo wired; failure states + real startup progress surfaced (optimistic IsTracking + fake 500ms splash gone); speech device name-match (#441b), honest voice-ack + pause/resume/help dispatches, model ranking, ResetInitState, loudness constant. **Privacy guardrail CLEAN** (full grep sweep, per-hit verified: no frame/audio/mic data reaches disk or network on either head; calibration JSON = coefficients only; all 34 tracker log sites aggregate-only). Core tests 164/164; slnf + WPF 0 errors; smoke baseline (44 tabs, 5 findings = known stale harness + ChaosRun infos). Calibration triage resolved the goal's stale BLOCKED note (BLOCKED disproven, core landed `837aaa1d`; real gap = 16-point window pipeline, filed as its own row). NOT exercised: live webcam/gaze/calibration vs WPF with real hardware (requires camera + calibrated run to resolve W-14 OnGazeMove coordinate-space ambiguity); real mic recognition/wake accuracy. Deferred rows on task board ("Discovered by WS0 lot 5") |
| 6 | Chaos/game mode | **passed 2026-07-03** | 4-agent adversarial review (~71 findings: run-engine/economy 18, visuals 28, narrative/HUD/SFX 16, input/rubric 9). STRUCTURAL VERDICT: the Avalonia Chaos RUN ENGINE (`AvaloniaChaosService` in `AvaloniaHeadStubs.cs`) is a SIMPLIFIED STAND-IN, not a faithful port of WPF's ~3000-line `ChaosModeService`; its deep economy/spawn/scoring layer is RE-SCOPED as a dedicated 'Chaos run-engine faithful port' workstream (P1 row). Fixed in-lot across 3 commits (`b9fb74be`,`3f4cceb8`,`b828fea5`) the self-contained correctness/safety/lifecycle/visual bugs: rank enum order+thresholds+names, DifficultyMult, lifetime-boon UpgradeCosts, removed 7 non-WPF boons, killed Avalonia-only passive focus regen, armed crash sentinel, bubble-pop achievement + loop tip + skillMult, boon-bar ribbon create/populate/teardown, ~7 in-run SFX cues (gameplay was silent), 3 lesson ticks (were permanent item locks); overlay HiDPI physical-px fixes across 6 overlays + BubbleHost/DVD/Local() unit fixes + DualMonitor honoring; restored dropped entrance animations (ripple expand, announcer/unlock-card pops, vibe pulse, keyframes, dashed tether, banner throb); PopText handler-leak; RaiseTopmost managed-flip removed; GIF-cascade animated budget (AppHangB1 freeze guard) + off-thread flash decode + art decoded-bitmap cache (OOM guard) + decode-to-width; ChaosOverlayWindow/ToyButton timer leaks; dead never-started timer allocs; tunnel WS_EX_NOACTIVATE|TOOLWINDOW. Core tests 164/164; slnf + WPF sln 0 err; smoke ChaosRun exercised clean (score/sparks/xp advanced, run start+end, results overlay visible; 44 tabs, 17 first-chance = baseline). PARITY OK (verified faithful, left as-is): narrative director, ChaosBoonColors, RevealService (18 predicates), lesson defs, meta persistence, catalogues, HUD bindings, hub debug, story-card display, announcer, SFX resolution (mod-overridable), art resolve, intro, SkiaFx/backdrop draw math, ex-styles correct on passive+interactive overlays, capture-affinity CLEAN, threading clean, compositor NON-BREAKAGE confirmed. Deferred rows on task board ('Discovered by WS0 lot 6'). NOT exercised: live full Chaos run vs WPF (economy/spawn feel, HiDPI overlay placement, audible SFX) with real hardware. ⚠ RE-OPENED by merge `5ce70de6`: `EffectPayload.Ambient` gate (a dashboard 'Trigger Bubble' video must not `ArmRandomSegment` a full mandatory video, #456/#458) — folded into the lot-6 run-engine faithful-port workstream; + animated-.webp in ChaosFlashOverlay/GifCascade extension gates. See "Sync-from-main" backlog |
| 7 | Progression/quests/economy | **passed 2026-07-04** | 5-agent adversarial review (~42 findings: A progression+achievements, Q quests+leaderboard, S skilltree+prestige+recap, PS profilesync, R rubric). STRUCTURAL VERDICT: XP logic lives in `AvaloniaProgressionService` (not Core); `SeasonRecapService` was TODO stubs; `ProfileSyncService` entirely ABSENT. Fixed in-lot across 5 commits (`997aed61` interface seams, `750d2615` wave A, `bc1df7bb` wave B, `b1236e8a` wave C, `eaa33975` wave D): **P0 data-corruption** achievements.json now single lock-guarded atomic writer + tmp-recovery (was naive WriteAllText + racing background writer); **P0** Season Recap ported to Core disk persistence (was dead stubs — re-view permanently broken + rollover lost); XP/economy parity (skill points +5→+1/level per WPF `PointsPerLevel=1`, companion+quest fed base not skill-multiplied XP, `SeasonPeakLevel` captured, attention-fail negative penalty now applies); prestige accrual (`TrackSkillPointsSpent`/`ReconcileLifetimePointsSpent` + purchase wiring + `OnSeasonReset` prune-to-`PermanentIds`); quest fixes (daily streak once/day not 3×, dirty-only per-tick save was synchronous every flash/bubble/minute, Oopsie streak-fix persists before XP debit, skill-tree XP multipliers folded in, rollover reconcile); rubric (per-second reflection/`dynamic`→typed cached, settings.json write debounced →≥60s from per-second, `QuestsTabViewModel` IDisposable 5-event leak, cross-thread binding mutation →DispatcherTimer, 2 dead methods deleted). Gates: slnf + WPF sln 0 err (seam guardrail; DIM interface additions don't touch WPF impls), Core tests 166→170 (+4), smoke baseline-clean (44 tabs, 17 first-chance = baseline, 5 findings = known stale-harness blocker + ChaosRun infos; quests tab + QuestCompletePopup exercised). PARITY OK (verified faithful): XP curve + session multiplier + all achievement predicates + 26 track hooks + `+1 skill point/100 bubbles`, quest pool 20+20 (6.1.7 sync) + reroll math + cadences + leaderboard FETCH, skill-tree defs/costs/tiers/prereqs (incl. 5 Tier-6 + 11 PermanentIds) + purchase flow, recap card VM/window, Chaos Sparks (self-contained, no progression intersection). DEFER (2 workstreams + rows on task board 'Discovered by WS0 lot 7'): **server-progression-sync** (`ProfileSyncService` ~2800 LOC entirely absent — periodic sync, cloud backup, leaderboard SUBMIT, HMAC, season-reset wiring; prestige-critical primitives landed in-lot), **Ditzy Data PRO analytics UI** (~832 LOC charts/panels), + idle/login XP gates, attention-XP revival, level-up network side-effects. NOT exercised: live side-by-side progression/quests vs WPF (economy feel, real season rollover, leaderboard submit) with a signed-in account |
| 8 | Browser/integrations | **passed 2026-07-04** | 5-agent adversarial review (~40 findings: B browser, AC account/auth/premium, C companion, D discord, R rubric/security). SECURITY CLEAN: tokens `[JsonIgnore]`→`SecureAuthTokenStore`/`AvaloniaTokenStorage`→`ISecretStore` (DPAPI/Keychain/AES-GCM), never logged, TLS, CSRF constant-time compare — no plaintext/token-log P0. Fixed in-lot across 4 commits (`ac9f8d9e` account/auth, `8213b1d2` companion, `de2aaa05` browser, + seams): **account** AC-1/AC-2 (#455) unified `AuthLogoutHelper` clears `AuthToken` + tears down every `IAuthProvider` (was leaving token+premium+OAuth live), AC-3 (#465) linked-Patreon +14d grace restored, AC-5 SubStar logout, D7 first-login, + rubric R1 (drop `cmd.exe` URL interpolation + scheme guard) R3 (log redaction) R6 (OAuth CTS cancel) R7 (`IEnumerable<IAuthProvider>` — was resolving only the last-registered) R8/R10 (dead code) R12 (null-token guard); **companion** C1 (#463) `IAvatarWindowService.IsSpeaking/IsSpeakingAudio` seam + `ShowAvatarLine` 16×750ms busy-retry (was clearing her queue mid-speech), C2 wired 13 orphaned reaction handlers (video/flash/subliminal/mindwipe/bubble/companion/level-up), C5/C6 handler parity; **browser** B1 (P1 login-break) OAuth now shell-launches the system browser via new `IBrowserHost.OpenExternalAsync` (was rendering in an invisible embedded WebView2), B2 (P1 #449/#439) dashboard Chromium autoplay+anti-MPO flags restored (black-video), B3 (P1) ad/tracker/popup blocking ported, + B4 URL-sanitize B5 mute B6 pop-out-live-URL B7 process-recovery B8 fullscreen-exit B12 hardening. Gates: slnf + WPF sln 0 err (seam guardrail; DIM additions to `IBrowserHost`/`IAvatarWindowService`/auth don't touch WPF impls), Core 170/170, smoke baseline-clean (44 tabs, 17 first-chance, 5 findings = known stale-harness blocker + ChaosRun infos; avatar portrait/emote + browser toolbar exercised). PARITY OK: OAuth CSRF/callback/device-code, Linux/macOS system-browser degradation, token encryption, Bark path, GigglePriority, quest/canned fallback. DEFER (workstreams + rows on task board 'Discovered by WS0 lot 8'): **Discord Rich Presence** (100% unported, dead toggle, NuGet sits unused — minimal Core `IDiscordPresence` seam recommended), **companion AI** (only stub `AvaloniaQuizAiService` registered → no AI-generated lines), + AC-4/AC-6, D8 webhooks, B9/B10/B11/B13, R2/R4/R5/R9/R11 P3s. NOT exercised: live signed-in OAuth/logout/premium-gate + real in-app browsing vs WPF |
| 9 | Tabs and dialogs | todo | |
| 10 | Theming/mods | todo | |
| 11 | Heads/DI/startup | todo | |

A Linux column/sweep section is added when WS4 starts (goal doc).

---

## Cross-cutting (check these first — they affect many screens)

- [ ] **Account login + premium gating** (OAuth providers, `HasPremiumAccess` gating)
- [ ] **START launches the mode** (session enters Running, effects start, stop returns to Idle)
- [ ] **Avatar reacts** (click → speech bubble)
- [ ] **Chaos run economy** end-to-end ("Down the Rabbit Hole": run lifecycle, boons, XP, narrative)
- [ ] **Overlays are pure passive click-through layers** (pink fill, spiral, subliminal, flash, brain-drain)
- [ ] **Multi-monitor (N screens)** incl. mixed landscape+portrait, per-monitor scale; single-display setting honored
- [ ] **Per-mod theme re-skin** across all 5 (CCP Default, Bambi Sleep, Sissy Hypno, Dronification, Circe's Lock)
- [ ] **Performance** — startup, working set, effect frame rates vs WPF and `benchmark-optimized.json`

## Main-sync deltas (ported from WPF 6.1.7; re-verify against current main)

- [ ] Chaos "Down the Rabbit Hole" main menu (logo, How-to-Play, menu music, fog/intro FX, options overlay)
- [ ] Quest pool refresh (20 free + 20 patron quests + art)
- [ ] Auth graceful browser-launch fallback (clipboard + dialog fallback)
- [ ] Subliminal double-flash fix (keep-alive windows, no Hide between flashes)
- [ ] Avatar focus-steal fix (`ShowActivated=false`, `SWP_NOACTIVATE`, no forced chat focus)
- [ ] Bubble pace (FIELD_PACE) / ChaosArt / ChaosTuning / Achievement autonomy quests / Lab tab deltas

## Tab views (`Views/Tabs`)

- [ ] Awareness
- [ ] Achievements
- [ ] Animations
- [ ] AppInfo
- [ ] Assets
- [ ] AvailableSubjects
- [ ] BambiTakeover
- [ ] BlinkTrainer
- [ ] CatalogueSubmissions
- [ ] CompanionHub
- [ ] Companion
- [ ] Deeper
- [ ] DeeperHub
- [ ] DeeperSubmissions
- [ ] Enhancements
- [ ] Haptics
- [ ] Lab
- [ ] Leaderboard
- [ ] LevelFeatures
- [ ] Lockdown
- [ ] Marquee
- [ ] Patreon
- [ ] PresetIO
- [ ] Presets
- [ ] Profile
- [ ] Quests
- [ ] RemoteControl
- [ ] Settings/Dashboard (feature cards, helper buttons, START/stop)
- [ ] SheListening (mapped 2026-07-02, `e140e0ff`)
- [ ] WebcamEngine (Webcam tab)
- [ ] Placeholder (should NOT appear for any real tab — flag if it does)

## Feature controls (`Features`)

- [ ] AppInfo
- [ ] AttentionCheck
- [ ] BouncingText
- [ ] BubbleCount
- [ ] BubblePop
- [ ] Flash
- [ ] IntensityRamp
- [ ] LockCard
- [ ] MindWipe
- [ ] PinkFilter
- [ ] Scheduler
- [ ] SchedulerRamp
- [ ] Spiral
- [ ] Subliminal
- [ ] System
- [ ] Video
- [ ] Visuals
- [ ] Webcam
- [ ] FeatureSettingsPopup (hosted in `SessionEditorWindow`)

## Dialogs (`Dialogs`)

- [ ] AssetSubmit
- [ ] AttentionCheckSettings
- [ ] AttentionTargetEditor
- [ ] AwarenessPresetDetail
- [ ] CataloguePicker
- [ ] CatalogueSubmit
- [ ] ChatShortcutCapture
- [ ] ColorEditor
- [ ] ColorPicker
- [ ] CompanionPhraseEditor
- [ ] CompanionPromptEditor
- [ ] ContentPolicyWarning
- [ ] DisplayName
- [ ] ExplicitContentAcknowledgement
- [ ] Input
- [ ] KnowledgeLinkEditor
- [ ] LocalAiSetupWizard
- [ ] LockCardColor
- [ ] Login
- [ ] ModManager
- [ ] OfflineUsername
- [ ] OpenAiCompatibleSamplerSettings
- [ ] RoadmapConfirm
- [ ] RoadmapDiary
- [ ] RoadmapStart
- [ ] RoadmapStep
- [ ] SessionEdit
- [ ] TextEditor
- [ ] UpdateNotification
- [ ] UpdateProgress
- [ ] UsernamePicker
- [ ] Warning
- [ ] WebcamConsent
- [ ] Welcome

## Windows (`Windows`)

- [ ] AnnouncementPopup
- [ ] AchievementPopup
- [ ] BubbleCount
- [ ] BubbleCountResult
- [ ] BugReport
- [ ] EasterEgg
- [ ] HelpVideo
- [ ] LockCard
- [ ] Mantra
- [ ] MiniPlayer
- [ ] ModCreator
- [ ] PinkRushPopup
- [ ] PopQuiz
- [ ] QuestCompletePopup
- [ ] Quiz
- [ ] QuizCategoryEditor
- [ ] QuizReportWindow
- [ ] SeasonRecap
- [ ] SessionComplete
- [ ] SessionEditorWindow
- [ ] SessionLogHistory
- [ ] Splash
- [ ] TutorialOverlay
- [ ] WebcamCalibration
- [ ] WebcamGazeTrackerWindow
- [ ] WebcamLoadingSplash
- [ ] WebcamQuickRecalWindow

## Deeper (`Views/Deeper`)

- [ ] DeeperEditor
- [ ] EnhancementPlayer
- [ ] GazePicker
- [ ] NewEnhancement
- [ ] UrlPrompt

## Chaos overlays (`Chaos`) & AvatarTube (`AvatarTube`)

- [ ] Chaos overlays render + animate smoothly and are click-through where they should be
- [ ] AvatarTube: speech
- [ ] AvatarTube: AI chat, emotes, drag/scale/attach, reactions, fullscreen detection
