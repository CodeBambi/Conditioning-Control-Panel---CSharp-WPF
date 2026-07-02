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
| 1 | Data/settings persistence + paths | **passed 2026-07-02** | 7 defects found+fixed (`e9501ce8`,`a2d1b9a8`,`b694b543`): secret-store no-op regression, 2 missing migrations, exit-flush gaps, quests serializer, roadmap drift, Roaming-migration orphaning, corrupt-file quarantine. Core tests 108→119; smoke 45 tabs clean for this area; deferred rows on task board ("Discovered by WS0 lot 1") |
| 2 | Session engine + start/stop | todo | |
| 3 | Overlays/compositor + click-through input | todo | |
| 4 | Video/audio | todo | |
| 5 | Speech/mic + gaze/calibration | todo | includes BLOCKED calibration port triage |
| 6 | Chaos/game mode | todo | |
| 7 | Progression/quests/economy | todo | |
| 8 | Browser/integrations | todo | |
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
