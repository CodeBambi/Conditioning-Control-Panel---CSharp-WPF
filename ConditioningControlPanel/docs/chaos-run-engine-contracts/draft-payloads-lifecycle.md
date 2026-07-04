# WPF Chaos Engine — DRAFT + PAYLOADS + RUN-LIFECYCLE Behavior Contract

Extracted 2026-07-04 by archaeology agent for the chaos run-engine faithful port
(claim `bac65e4a`, plan `docs/chaos-run-engine-port-plan.md`). All references
`file:line` in `ConditioningControlPanel/Services/Chaos/` unless noted. Verbatim.

---

## 1. BOON DRAFT

### 1.1 When drafts occur
- Draft fires at every wave/loop boundary crossing: `RunTick` `CMS:1101` → `BeginWaveTransition(newWave)` when `newWave > WaveIndex` (`waveLen = RunDurationSec / WaveCount`, `CMS:1096-1098`).
- `BeginWaveTransition` (`CMS:1448`): `ChaosLessonHooks.OnLoopCompleted()` (1450), `AwardLoopTip()` (1451).
- `!BoonDraftEnabled` (`CMS:1454`): NO draft — inline advance `WaveIndex=newWave; ActIndex=1+(newWave-1)/5;` + `NotifyChaosWaveEscalated`, `FireActChangedIfCrossed()`, `AnnounceFinalLoopIfEntering()` (1456-1464).
- Drafts enabled (1466+): `_paused=true`, `_spawnTimer.Stop()`, `ChaosWaveTimerOverlay.Clear()`, `App.Bubbles.PopAllBubbles()` (silent wipe), `ChaosSfx.PlayWaveClear()` (1469), `Pulse(WAVE_CLEAR_COLOR, WAVE_CLEAR_PULSE)`, `NotifyChaosWaveCleared`, `_pendingWave = newWave` (1477).
- Scripted first run: drafts config-disabled but exactly ONE scripted draft mid-run via `TriggerScriptedDraft(options)` (`CMS:1489`, called by ChaosHappyPath); same choreography, `_pendingWave = WaveIndex` (no advance).

### 1.2 DraftChoices + draft4
- Default `ChaosRunConfig.DraftChoices = 3`; `draft4` upgrade `Apply = c => c.DraftChoices = 4` (`ChaosUpgrades.cs:86`, cost 200, Depth branch).
- `ChaosBoonPool.Draft` clamps `choices = Math.Clamp(choices, 2, 4)`.
- Invoked with `Config.DraftChoices` at `CMS:1461` and `1521` (reroll).

### 1.3 Duo/Trio RequiresAny/RequiresAll gating (`ChaosBoonPool.Draft`, ChaosModels.cs)
```csharp
static bool ReqMet(string id) => ChaosMeta.IsBoonActive(id) || ChaosMeta.IsUpgradeActive(id);
bool Draftable(ChaosBoon b) => (b.RequiresAny == null || b.RequiresAny.Any(ReqMet))
    && (b.RequiresAll == null || b.RequiresAll.All(ReqMet))
    && !(b.Unique && takenIds != null && takenIds.Contains(b.Id));
```
No rank gate on duo/trio (old Entranced wall removed). Duo/trio cards: `focus_here`←RequiresAny{pendulum_swing (UPGRADE id)}; `overload`←{e_stim}; `afterglow`←{vibe_popping}; `casting_couch`←{porn_dvd}; `tail_plug`←{rabbit_caller,the_pull,the_spanker}; `unleashed`←{collar}; `electrified_rabbits`←RequiresAll{the_spanker,e_stim}; `body_buzz`←RequiresAll{chain_reaction,e_stim}.

### 1.4 Sin-slot
```csharp
bool includeCurse = allowCurses && (guaranteeCurse || rng < sinChance) && curses.Count > 0;
int boonCount = includeCurse ? choices - 1 : choices;   // fill shuffled boons, add curses[0], top-up, Take(choices)
```
- `SinChance` seeded in `FromSettings`: `DefaultSinChance(RunsCompleted)` — `SIN_DEBUT_RUNS=2, SIN_FULL_RUNS=10, SIN_CHANCE_DEBUT=0.25, SIN_CHANCE_FULL=0.5`; 0.0 if runs<2; 0.5 if ≥10; else linear.
- Surrender capstone forces sin: `guaranteeCurse: MaxedBoons.Contains("surrender")` (`CMS:1467`, `1522`).
- `ChaosHappyPath.RigDraft(options, _state)` post-processes the first draft only (`CMS:1471`).

### 1.5 Unique-taken exclusion
`ChaosBoon.Unique` — once taken this run, never re-offered. `takenIds = TakenBoonIds()` (`CMS:1510`) = `ActiveBoons + ActiveCurses` ids; passed at `CMS:1467/1522`. Most cards Unique=true (gold_digger, welcome_shower, heavy_drop, gg_rabbits, size_queen, aftermath, focus_here, all duos, all sins). NOT unique: `defuse_chain`, `golden_touch`, `extra_shield`.

### 1.6 Reroll
`RerollDraft()` (`CMS:1519`): `RerollsLeft <= 0 → null`; else decrement + re-deal with same params. Wired via `ShowBoonDraft(..., rerollsLeft, onReroll: RerollDraft)` (`CMS:1473`). `RerollsLeft` from `taking_chances` boon (LevelValues {1,2,3}; also `ChanceDoubleOdds = 0.50 + 0.05*(clamp(v,1,3)-1)`).

### 1.7 OnBoonChosen (`CMS:1531`)
- `ChaosHappyPath.OnDraftResolved()` (1533).
- Card taken: `sinShielded = IsCurse && MaxedBoons.Contains("surrender") && !SurrenderShieldUsed` → `SurrenderShieldUsed=true` (1538-1539); first-times Whisper always + Yes if sin (1544-1545); `ChaosLessonHooks.OnDraftCardTaken(IsCurse)` (1547); `_state.ApplyBoon(boon, shieldDrawback: sinShielded)` (1548) — ApplyBoon: `ApplyShielded?.Invoke ?? Apply`; `BoonMult += RunMultBonus`; ActiveCurses/ActiveBoons add; RunPickTiles push; PushEvent. `extra_shield` → shield pulse (1549).
- Sin extras (1550-1568): `if (SinExtraMult > 0) BoonMult += SinExtraMult`; shielded → "candle took the sting"; Surrender maxed → `Shields += 1` + green pulse; `NotifyChaosCursePicked`; `ChaosSfx.Play("sin_accept", 0.6f)` (1569); announce; `ChaosNarrativeHooks.OnMoment("sin_accepted")`.
- Mantra branch: `NotifyChaosBoonPicked`, announce `◈ {Name}`.
- SKIP (null, 1590+): `Shields += 1`, "resisted → +1 resistance", green pulse, `NotifyChaosBoonSkipped`, announce "+1 RESISTANCE".
- Then `WaveIndex = _pendingWave; ActIndex = 1+(WaveIndex-1)/5; FireActChangedIfCrossed();` (1598-1600); `_overlay.ShowReadyGo(ResumeAfterDraft)` (1604).
- Auto-resume: `DraftAutoResumeSecDefault = 15` (`CMS:91`); untouched → auto-SKIP (+1 shield); 0 disables.

---

## 2. EFFECT PAYLOADS

### 2.1 Catalog (`EffectPayload.cs`; `Scale(min,max) = min + round((max-min)*clamp(Strength,0,100)/100)`)

| Kind | Class | Fire() |
|------|-------|--------|
| Flash | FlashPayload | `App.Flash.TriggerFlashOnce(amount=Scale(1,3), duration=(int)(Scale(900,2000)*GlobalDurationMult*DurationMult), size=Scale(68,143), suppressHaptic:false)` |
| Subliminal | SubliminalPayload | `App.Subliminal.FlashSubliminal()` |
| Overlay | OverlayPayload | `duration=(int)(Scale(1500,4500)*Global*Instance)`; braindrain → `ChaosFlashOverlay.Show(duration,o)`/`.Show()`; else `App.Overlay.ShowOverlayTimed(kind, duration, opacity=ScaleD(0.25,0.70))`; kind from `PickOverlayKind()` ∈ {pink_filter,spiral,braindrain} |
| Video | VideoPayload | `if (!Ambient) App.Video.ArmRandomSegment(SEGMENT_SEC=15); App.Video.TriggerVideo(silentIfEmpty:true)` |
| HtLink | HtLinkPayload | `HtLinkPool.PickRandom()` → `mw.NavigateToUrlInBrowser(url, autoPlayFullscreen:true)` |
| Audio | AudioPayload | `App.Subliminal.FlashSubliminal()` (whisper reuse; no dedicated pool; DisplayName "whisper") |
| BambiFreeze | BambiFreezePayload | `App.Subliminal.TriggerBambiFreeze()` (DisplayName "freeze") |
| BouncingText | BouncingTextPayload | `App.BouncingText.Start(...)` 1 affirmation; stop at `DURATION_SEC=8.0*Global` or `MAX_BOUNCES=12`; no-op if running. `OPACITY=0.85, TEXT_SIZE=120, SPEED=5` |
| GifCascade | GifCascadePayload | `ChaosGifCascadeOverlay.Show(spawnRatePerSec=1.67, durationSec=6.0*Global, gifSize=400, fallSpeed=3.6, opacity=0.9, startScale=0.45)` |

Factory `EffectPayloadFactory.Build(kind)`; Strength set by caller; weighted-pool layer deleted 2026-06-12. Audio/BambiFreeze arrive on `EffectBubbleSpec.Payload` from variants — engine never constructs directly (X1-18).

### 2.2 Dispatch paths
1. Benign treat pop — `spec.Payload.Fire()` direct (`CMS:1850`); NO scaling wrapper, NO OnPayloadFired hook.
2. Prism pop — `FireScaledPayload(spec.Payload)` (`CMS:1833`).
3. Detonation / Tease-touch / Brittle-shatter — `FirePayloadForDetonation(spec)` (`CMS:2205`) → FireScaledPayload.

`FireScaledPayload` (`CMS:2196`): `ChaosLessonHooks.OnPayloadFired(kind)`; `m = DetonationDurationMult ?? 1.0`; if ≠1 wrap `GlobalDurationMult *= m; try Fire(); finally /= m;`.

`FirePayloadForDetonation` (`CMS:2205`):
- Ambient remap (2207-2224): `if (Config.AmbientMode && IsIntrusivePayload(kind))` → coin `cascade = !HeavyEffectActive && rng < 0.5`; soft = GifCascade or BouncingText w/ same Strength; if cascade `_heavyUntilUtc = now + DURATION_SEC + 5`; stinger `fx_rain_start`/`fx_text`; FireScaledPayload(soft); return. `IsIntrusivePayload(k) => k == HtLink` ONLY (`CMS:2267`) — Video intentionally NOT remapped (2263-2266).
- Heavy gate (2229-2246): `heavy = Video || GifCascade`; if heavy && HeavyEffectActive → drop (log + "the deep is busy — {name} fizzles"). Else Video: `_chaosVideoCapUtc = now + VIDEO_HARD_CAP_SEC(15)`; `_heavyUntilUtc = now + 15+3`. GifCascade: `_heavyUntilUtc = now + DURATION_SEC+5`.
- Then `PlayPayloadStinger(StingerForVariant(spec.VariantId))` (2247): braindrain→fx_drain, bambifreeze→fx_freeze, htlink→fx_rain_start, else "" (video/flash/subliminal carry own audio). Then FireScaledPayload.
- `HeavyEffectActive` (`CMS:2192`): `App.Video.IsPlaying || ChaosGifCascadeOverlay.IsRaining || now < _heavyUntilUtc`. `VIDEO_HARD_CAP_SEC=15` (=VideoPayload.SEGMENT_SEC), `VIDEO_TEARDOWN_QUARANTINE_SEC=3`.

### 2.3 EffectPayload.Ambient + ArmRandomSegment gate (#456/#458)
- `Ambient` is a PER-INSTANCE flag set at bubble build time — run bubbles `false`, dashboard Trigger Bubbles `true`. NOT `App.Chaos.IsRunning`.
- `VideoPayload.Fire()`: `if (!Ambient) App.Video?.ArmRandomSegment(15); App.Video?.TriggerVideo(silentIfEmpty: true);` — only chaos caps playback (~15s via `_chaosVideoCapUtc` in RunTick 1050-1073) making a random start a "slice"; ambient videos are uncapped so arming would start mid-video (#456/#458).
- PORTING TRAP: (a) per-instance Ambient comes from the builder, not run state; (b) 15s cap enforcement lives in RunTick, not the payload; (c) `cfg.AmbientMode` (config, forced true in FreeDesktop) and `payload.Ambient` are DIFFERENT concepts — do not conflate.

---

## 3. RUN LIFECYCLE

### 3.1 StartRun (`CMS:295`) — setup + countdown
1. `if (_active) return;` 2. engine/session running → `StopEngineAndSession("Chaos")` (302). 3. `CloseLoadoutSidebar(); cfg = config ?? FromSettings()` (306).
4. Play-mode lock (313-322): `resolvedMode = StoryModeEnabled ? SelectedPlayMode : FreeDesktop` (Story disabled → always FreeDesktop); `cfg.PlayMode`, `ActiveMode`, `ChaosWindowZ.DesktopMode`, `PinTopmost = ChaosPinOnTop ?? true`; **`if (FreeDesktop) cfg.AmbientMode = true;`** (322).
5. `App.Bubbles.PauseAndClear(); _state = new ChaosRunState(cfg); _active = true` (326-328).
6. `_hud = new ChaosHudWindow` + show; `RefreshSidebarLoadout(); _overlay = new ChaosOverlayWindow{OnRunAgain=RunAgain, OnDismissed=OnOverlayClosed}` (330-340).
7. `if (!isRestart) ChaosSfx.Play("fall_in", 0.28f)` (342); `ChaosTunnelService.Preload()` (345).
8. `_overlay.ShowCountdown(BeginRun, shortFlash: isRestart)` (348) — 3·2·1·GO; restart countdown 1000ms.
9. `App.AvatarWindow.SetChaosRunActive(true)` (351). Exception → `CleanupAfterRun(); App.Bubbles.Resume(); MessageBox` (354-360).

### 3.2 BeginRun (`CMS:359`) — GO; ordered
1. `App.Bubbles.BeginChaosMode(...)` with callbacks: OnBenignPopped/OnDefused/OnDetonated/OnDarterCaught/OnFreezeCaught + lambdas (chainReach, hitboxScale, bubbleOpacity, cursorPull = CursorPullStrength − CamGirlFlee, rabbitHoming, spankerOn, spankGrow, liveMagnet, onTreatExpired, onEStimArc, rabbitTrailSec, electrifiedRabbits, canChannelDefuse, onChannelBroken, onTeaseTouched, onTeaseDenied, onBoundEnraged, onBrittleShattered) (361-381).
2. `NotifyChaosRunStarted`; "🐇 the descent begins"; reset per-run flags (382-390).
3. `ChaosLessonHooks.OnRunStarted()` (391). 4. `EndSlowMo(); EndFreeze();` reset transient (392-402).
5. `ChaosDvdOverlay.SpankerRedirect` gate (404); overlay pre-creates: WarmSpiralCache, EffectBanner/FieldFx/SkiaFx EnsureCreated, BoonBar EnsureCreated + SetPicks (406-421).
6. Start boon (425-437): unless ScriptedFirstRun, `EquippedStartBoon` via ApplyBoon + announce; welcome_shower start-boon → SpawnWelcomeShower (439).
7. **`ChaosMeta.ApplyLifetimeBoons(_state)`** (440). 8. HUD loadout; `SetClockVisible(ShowWaveTimer)`; toy overlay pre-creates (441-451).
9. `StartShields = Shields`; reset focus/invuln/tease (455-461). 10. `ChaosHappyPath.OnRunStarted(_state, this)` (463). 11. first-run defuse tutorial (464-470).
12. `BuildActiveToys(); StartKeyHook(); StartRippleHook();` + `ChaosToyButtonWindow` per toy (476-487).
13. `_spawning = true` (489); `_fx = new ChaosFxWindow()` (491). 14. subscribe `App.Video.VideoStarted/VideoEnded` (494-495).
15. `_runTimer` 250ms → RunTick (517-520). 16. `_spawnTimer` 800ms → SpawnTick (522-524).
17. `ChaosNarrativeHooks.OnRunStarted()`; backdrop/tunnel; `_state.PropertyChanged += OnStateChangedForTunnel`; `OnMoment("run_start")` (528-537).
18. **Crash telemetry arming** (540-543): `_peakNativeMb=0; _memSampleTick=0; LogMemSample("run-start"); _lastRenderTs=MinValue; _hitchCount=0; _hitchScore=0; _perfBackoff=1.0; CompositionTarget.Rendering += OnChaosRendering;`

### 3.3 Duration / waves
- `ActWaveText = "DEPTH {Roman(ActIndex)} · LOOP {WaveIndex}/{WaveCount}"`; Act = `1+(WaveIndex-1)/5`.
- End: `elapsed >= RunDurationSec` → if `RelapseLoopArmed && !RelapseLoopActive` → `ExtendOneLoop()` ("☠ RELAPSE" + sin_accept 0.6f); else `EndRun()` (`CMS:1078-1090`).
- T-10s "the hole is closing" once at `RunDurationSec - elapsed <= 10` (`CMS:1063`).
- Clock held while `_paused || _manualPaused` (RunTick early-return, `CMS:944`).

### 3.4 EndRun (`CMS:3122`) — exact order
1. `if (!_spawning || _state == null) return;` 2. `LogMemSample("run-end")` (3124). 3. **`ChaosCrashSentinel.Clear()`** (3126). 4. `Rendering -= OnChaosRendering` (3127).
5. `ranFullCourse = ElapsedSec >= RunDurationSec` (3128); full → `AwardLoopTip()` (3131). 6. `ChaosLessonHooks.OnRunCompleted(Shields, difficulty, ranFullCourse)` (3132).
7. `_spawning=false`; unsubscribe video; stop timers; `StopKeyHook/StopRippleHook/CloseToyButtons/EndChaosMode/EndSlowMo/EndFreeze`; close ALL overlays (ChaosFlash, GifCascade, Announcer, UnlockCard, Dvd, EffectBanner, WaveTimer, BoonBar, CursorGlow, VibeTrail, EStim, FieldFx, SkiaFx, PopText.ShutdownPool); `_fx.Close()` (3134-3160).
8. XP payout (3162-3169): `baseXp = min(Score, 250·durMin·diff)`; `AddXP(baseXp, XPSource.Chaos)`.
9. `previousBest = BestScore` (3172). 10. `sparksEarned = ChaosMeta.AwardRunRewards(_state)` (3175). 11. `RevealService.Sync("run_end")` (3178); rank-up card (3182-3189).
12. `NotifyChaosRunCompleted`; `_hud.Close()` (3193-3196). 13. `_overlay.ShowResults(_state, baseXp, skillMult, finalXp, previousBest, sparksEarned, rankUp)` (3197). 14. `App.Bubbles.Resume()` (3199).

### 3.5 Stop / panic / cleanup
- Panic (`OnPanicKeyDuringRun` `CMS:285`): 1st press = manual pause; 2nd while held = `RequestStop()`. Routed via `OnToyKey` vs `Settings.PanicKey`, works while paused.
- `RequestStop()` (`CMS:3071`): drop pause → `if (_spawning) EndRun()` (pays out; `ranFullCourse=false`).
- `ForceShutdown()` (`CMS:3085`): hard teardown (app exit) — flags false, timers stopped, EndChaosMode, Resume, StopKeyHook, close ALL overlays + fx/overlay/hud, `CleanupAfterRun()`. No results, no payout. Reaches sentinel Clear via CleanupAfterRun.
- **`CleanupAfterRun()` (`CMS:3227`) — single funnel for every teardown path**: **`ChaosCrashSentinel.Clear()` (3229)**; unsubscribes; StopKeyHook/Ripple/ToyButtons; close remaining overlays + backdrop + tunnel; `SetChaosRunActive(false)`; `ChaosHappyPath.OnRunEnded()`; `ChaosNarrativeHooks.OnRunEnded()`; null timers/hud/overlay/fx/state; flags false; `ActiveMode=Story`, `ChaosWindowZ.DesktopMode=false`, `PinTopmost=true`; clear lesson-card state (3229-3274).
- `OnOverlayClosed` (`CMS:3213`) → teardown if spawning → CleanupAfterRun. `RunAgain` (`CMS:3204`): `_overlay.Close()` → OnOverlayClosed → CleanupAfterRun → `StartRun(isRestart:true)`.

### 3.6 ChaosCrashSentinel (X1-13: Avalonia never arms)
- ARMED by `ChaosCrashSentinel.Mark(context)` called ONLY from `LogMemSample(phase)` (`CMS:860`): at BeginRun "run-start" (541), every ~15s in RunTick (`_memSampleTick >= 60`, `CMS:1048`, "tick"), EndRun "run-end" (3124).
- Sentinel file: `%LOCALAPPDATA%/ConditioningControlPanel/logs/chaos_session.active` with context `"v{ver} mode={mode} skiaFx={..} pinTop={..} difficulty={..} monitors={N} elapsed={s}s peakNative={MB}MB bubbles={N}"` (`CMS:863`).
- DISARMED (`Clear()`) at exactly two sites: EndRun (3126) + CleanupAfterRun (3229). Next launch `ConsumeAndReport(logger)` logs `[CHAOSCRASH] DETECTED` if file survived.
- PORT: `Mark` must fire at run-start + periodically; `Clear` at both teardown sites. Do NOT gate `Mark` on `ChaosMemTelemetry` (only the `[CHAOSMEM]` log line is gated). OPEN-QUESTION: sentinel path needs a platform seam.

---

## 4. LESSON HOOKS

### 4.1 Engine call-site → hook map (all reset by `OnRunStarted()` at `CMS:391`)
- RunTick → `SampleCursor()` (`CMS:1006`) — the_pull cursor sampling (250ms).
- OnBenignPopped treat → `OnTreatPopped(spec)` (`CMS:1852`) → vibe_popping (10 in 5s), chain_reaction (170px/0.40s), the_pull (<3px rest), intrusive_thoughts (subliminal), blindfold screen-busy.
- OnBenignPopped prism → `OnPrismPopped()` (`CMS:1831`) → taking_chances.
- OnDefused → `OnDefuseCompleted(fuseSecLeft, viaChannel)` (`CMS:2016`) → snap_field, blindfold, last_breath (channel ≤0.8s), slow_fuses (banked seconds).
- CanChannelDefuse approved → `OnChannelStarted()` (`CMS:1932`); OnChannelBroken → `OnChannelBroken()` (`CMS:1941`).
- OnDetonated/OnTeaseTouched → `OnDetonation()` (`CMS:2074`, `1338`) → dirties loop for silk_touch.
- BeginWaveTransition → `OnLoopCompleted()` (`CMS:1450`) → judges silk_touch, resets per-loop.
- OnBoonChosen → `OnDraftCardTaken(isSin)` (`CMS:1547`) → draft4 + surrender.
- OnDarterCaught → `OnRabbitCaught()` (`CMS:2286`); OnFreezeCaught → `OnFreezeCaught()` (`CMS:2314`).
- Video slice completed → `OnVideoEndured()` (`CMS:1070`, `1076`) → porn_dvd.
- FireScaledPayload → `OnPayloadFired(kind)` (`CMS:2198`) → blindfold screen-busy.
- EndRun → `OnRunCompleted(shieldsLeft, difficulty, ranFullCourse)` (`CMS:3132`) → ends channel; full course: OnLoopCompleted + popup_notification (shields>0) + extreme_tier (difficulty ≥ Hard).

### 4.2 Rabbit-spank hook (X3-13)
`ChaosLessonHooks.OnRabbitSpanked()` (`ChaosLessonHooks.cs:134`) → `ChaosLessons.Tick("rabbit_caller")`. Fired from **`BubbleService.cs:3789`** on a darter's FIRST smack (`if (!_isSpanked)`; sweepers born spanked never land here). With The Spanker equipped rabbits can never be CAUGHT — the first smack is the only reachable tick; omitting it makes `rabbit_caller` impossible for Spanker users.

### 4.3 Thresholds (`ChaosLessons.cs`)
`T_VIBE_POPPING=10, T_FREEZE_TRIGGER=15, T_PORN_DVD=10, T_SNAP_FIELD=5, T_RABBIT_CALLER=25, T_CHAIN_REACTION=50, T_BLINDFOLD=10, T_LAST_BREATH=10, T_TAKING_CHANCES=8, T_THE_PULL=15, T_INTRUSIVE=50, T_SURRENDER=5, T_SLOW_FUSES=60, T_SILK_TOUCH=1, T_POPUP_NOTIF=3, T_DRAFT4=15, T_EXTREME_TIER=10`. Lessonless: {e_stim, the_spanker}. Curse-bound (waived if `ChaosAllowCurses==false`): {taking_chances, surrender}.

---

## 5. SFX CUES FIRED BY THE RUN ENGINE

`ChaosSfx.Play(name, scale)` resolves `Resources/sounds/chaos/{name}.mp3` (mod-overridable, silent no-op if absent).

- `glass_shatter` (X3-2): `OnBrittleShattered` `CMS:1374` — `Play(ResolvePath("glass_shatter").Length > 0 ? "glass_shatter" : "trigger", 0.55f)`; then resist_crumble/resist_absorb (1381) or mimic payload.
- `ui_unlock` (X3-2): `ResumeAfterDraft` `CMS:1638` — inside `_pendingLessonCards.Count > 0` branch before each `ChaosUnlockCardOverlay` (deferred-at-draft lesson cards).
- Full list (name @ line — trigger): fall_in@342 start (0.28f); toy_ready@987 ripple ready (0.3f), @2681/2694 toys armed (0.5f), @2939 cooldown lapsed (0.45f); sin_accept@1086 relapse (0.6f), @1569 sin accepted (0.6f); resist_crumble/absorb@1348 tease soak, @1381 brittle soak, @~2093 detonation soak (0.6f); trigger@1355 tease payload, @~2103 unshielded hit (0.55f); toy_denied@1401 bound enrage (0.5f), @2522 ripple not ready (0.45f), @2613/2617/2638 toy denied (0.4f); PlayWaveClear@1469/1503; ui_unlock@1638 (0.55f); resist_absorb@1787 heart caught (0.55f); golden_pop@1799 droplet (0.35f), @1814 golden (0.6f); rabbit_spawn@1893 sweepers (0.5f), @2837 rabbit caller (0.5f); focus_empty@1951 no-focus grab (0.55f); collar_save@2113 (0.6f); time_slow_in@2343 / time_slow_out@2390 (guarded flags); vibe_buzz@2631; freeze_trigger@2640/2669; dvd_launch@2654; estim_zap@2714; PlayTickTock@2889 pendulum; freeze_shatter@2987 (guarded); streak_milestone@3007 combo 25/50/100 (0.5f); depth_change@3037 (0.55f); PlayRippleCast@2556.
- Payload stingers `PlayPayloadStinger(name, 0.45f)`: fx_rain_start/fx_text@2220 (ambient remap); fx_drain/fx_freeze/fx_rain_start@2247 (by variant).
- Non-ChaosSfx: blindfold heartbeat `App.Bubbles.PlayCue("chaos/heartbeat.mp3", 0.5f)` (TickBlindfoldHeartbeat); `PlayChime` golden 0.30f / welcome shower 0.25f / heart 0.22f (pooled).
- PORT NOTE: preserve resolve-then-fallback (`PlayFirstAvailable`, presence-check pattern) in the Avalonia audio seam.

---

## 6. RUN-START CONFIG SNAPSHOT

`ChaosRunConfig.FromSettings()` reads from `App.Settings.Current`: `ChaosDifficulty` (ClampDifficulty: Gentle open; Teasing needs PillTeasing reveal; Relentless PillRelentless; Inescapable PillInescapable), `ChaosRunDurationSec` clamp(60,900) default 180, `ChaosWaveCount` clamp(1,12) default 5, `ChaosMotionMode` → MotionOverride (null=Mixed), `ChaosEnabledVariants` → ClampVariants, `ChaosScreenShakeEnabled`, `ChaosColorFlashesEnabled`, `ChaosShakeIntensity` clamp(0,1), `ChaosEffectIntensity` clamp(0.2,1.5), `ChaosBoonDraftEnabled`, `ChaosAllowCurses`, `ChaosDartersEnabled`; `SinChance = DefaultSinChance(RunsCompleted)`.

Config defaults: `StartingShields=0, DraftAutoResumeSec=15, AmbientMode=false, FuseTimeMult=1.0, MagnetEnabled=false, BaseMult=1.0, SparkGainMult=1.0, DraftChoices=3, HitboxScale=1.0, PopupHeartEnabled=false, PendulumSwing=false, SpawnRateMult=1.0, ScriptedFirstRun=false`.

`ChaosMeta.ApplyTo(cfg)` at end of FromSettings (both paths) — see economy contract §6.

StartRun overrides AFTER FromSettings: `cfg.PlayMode` (forced FreeDesktop while StoryModeEnabled==false); `if (FreeDesktop) cfg.AmbientMode = true`; `ChaosWindowZ.PinTopmost = ChaosPinOnTop ?? true`.

BeginRun meta reads: `EquippedStartBoon` (unless scripted); `ApplyLifetimeBoons(_state)`; `SlotsFor(Skill)` + `IsBoonActive` for toys; `ChaosAccessoryKey1/2` ("Q"/"E"); `PanicKey`/`PanicKeyEnabled`; `Seen*` flags; `ChaosMemTelemetry`/`ChaosSkiaFxEnabled`/`ChaosPinOnTop`.

Engine-read spec fields: `Payload, Strength, PayMult, VariantId, SizePx, IsHeart, IsDroplet, IsGolden, IsPrism, IsEcho, IsFreeze, IsBoundHalf, DarterSpeed`.
