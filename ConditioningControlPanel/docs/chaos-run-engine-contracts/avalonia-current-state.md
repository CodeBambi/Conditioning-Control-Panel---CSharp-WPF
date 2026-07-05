# Avalonia/Core Chaos — Current-State Map

Extracted 2026-07-04 by archaeology agent for the chaos run-engine faithful port
(claim `bac65e4a`, plan `docs/chaos-run-engine-port-plan.md`). Every claim `file:line`
on `feat/crossplatform`.

---

## 1. `AvaloniaChaosService` — the current run engine (the stand-in)

**File:** `CCP.Avalonia/Services/AvaloniaHeadStubs.cs` line 33 → ~1580 (file 2025 lines, also holds avatar/bark/video/mainwindow stubs).

### 1a. `IChaosService` — `CCP.Core/App.cs:418-437` (14 members)
`IsRunning; IsManuallyPaused; LastRunScore; ShowLoadoutSidebar(); CloseLoadoutSidebar(); NotifyLoadoutChanged(); StartRun(object cfg); StartRunFromSidebar(); ToggleManualPause(); RequestStop(); CloseWarrenPhase(); OpenWarrenAt(string); UnequipFromSidebar(string); UseToyById(string);`
`StartRun(object)` boxes `ChaosRunConfig` (cast at `AvaloniaHeadStubs.cs:225`).

### 1b. Consumers of `IChaosService`
DI `ServiceCollectionExtensions.cs:256` · facade `AvaloniaChaosStubs.cs:1080` (`AvaloniaChaosApp.Chaos`) · `ChaosHudWindow.axaml.cs:19,52` · `ChaosToyButtonWindow.cs:20,38` · `BenchmarkContext.cs:116,433` · `LayerVerification.cs:111-113` (downcasts to concrete for layer seams) · `SmokeTestRunner.cs:1175`.
Layer-driving methods (`ShowChaosPopText`, `LaunchChaosDvd`, `AnnounceChaos`, `ChaosFieldRipple`, …) are NOT on `IChaosService` — public members of the concrete class only.

### 1c. Current behavior highlights
- **ctor (:100-190):** 20 deps; constructs + registers all 8 chaos compositor layers on `CompositorEngine` (:155-186; announcer LineCompleted → queue advance :165; DVD policy delegates :172-178); `AvaloniaChaosCatalogs.EnsureInitialized()`.
- **StartRun (:225-330):** cast cfg / `FromSettings()`; first-run `ChaosHappyPath.BuildFirstRunConfig()` when RunsCompleted==0 (:231); PinTopmost snapshot BEFORE windows (:240); PauseAndClear; builds `ChaosRunState` (:243-272); HUD + overlay + `ShowCountdown(BeginRun)` (:283-315).
- **BeginRun (:390-495):** lesson/narrative hooks; **sentinel `Mark` armed (:399)**; `_bubbles.BeginChaosMode(...)` with **only 6 callbacks**; SFX fall_in; backdrop/tunnel; start boon + `ChaosMeta.ApplyLifetimeBoons`; toys/hooks/toy buttons; `StartTimers()`.
- **Timers (:497-503):** run 250ms; **spawn 900ms fixed** (no retuning).
- **RunTick (:513-595):** dt 0.25; topmost churn every 4th tick; heat decay; freeze/snap/ripple/toys ticks; no passive focus regen (parity); wave boundary → `ShowDraft()` / advance / `EndRun(true)`.
- **SpawnTick (:597-640):** variant from `EnabledVariants` else `["flash","pink","subliminal"]`; **live = flat 45% (:623)**; size 80-160px, fuse 4-8s, speed 1-2x. NO behavioral bubbles/darters/golden/riders/density curve.
- **Scoring:** OnBenignPopped (:642-690) `basePay = 100 * DifficultyMult * (1+Heat)`, `pay = basePay * ComboMult * BoonMult * UrgeMult`; Heat += 0.02. OnDefused (:692-720) `basePay = 250 * ...`; Heat += 0.03. OnDetonated (:722-760) shield-absorb/combo-reset; Heat -= 0.15. **All divergent from WPF formulas.**
- **Payloads:** own `BuildPayload` switch (:763-800) — PARALLEL to `AvaloniaEffectPayloadFactory` (two maps).
- **Draft (:897-1000):** `PickDraftOptions` = 3 from `ChaosBoonPool.All`, curses filtered unless allowed — no sin-slot ramp, no duo/trio Requires gating, no Unique exclusion, no reroll, no draft4.
- **EndRun (:1002-1075):** `baseXp = sqrt(max(0,Score))*1.5 + RunDurationSec/60*35*DifficultyMult` (WRONG: that's the SPARKS formula); `sparks = round(baseXp)`; `AddXP(sparks, XPSource.Chaos)` (X1-2); banks meta; `RevealService.Sync("run_complete")` (WPF: "run_end").
- **CleanupAfterRun (:1077-1160):** sentinel clear; layer teardowns all wired (WPF CloseActive parity); resets pin/mode.
- **Toys (:1213-1290):** vibe_popping, freeze_trigger, porn_dvd, snap_field, rabbit_caller (RabbitAimTick 16ms + IPointerState), e_stim. Ripple via `IMouseHook.RightButtonUp` ref-counted (:1170-1200).
- **Layer seams (:1358-1560):** cursor glow, pop text, flash wash, DVD, gif cascade, field FX, effect banner, announcer (+priority queue, byte-equivalent to WPF).

---

## 2. `CCP.Core/Services/Chaos/*` fidelity

| File | Status |
|---|---|
| **BubbleEngine.cs** (1390L) | **FAITHFUL** ported WPF BubbleService: physics 32ms, FIELD_PACE, MaterializeChaosSpec, full behavioral support (darter/freeze/chaperone/bound/tease/brittle/echo/prism), hold-to-defuse channel, field hazards (Size-Queen ripples, player ripple + darter-fling, residue, tail-plug trails). Rich `BeginChaosMode` with **13 callbacks**. **Feed it; do not rewrite it.** |
| BubbleState.cs | Faithful/complete |
| ChaosBubbleSpec.cs | Faithful — all behavioral flags present |
| ChaosImagePool.cs | Faithful (60s TTL cache) |
| ChaosMetaState.cs | Faithful/complete (SchemaVersion 2) |
| ChaosMotion.cs | Complete |
| ChaosTuning.cs | Faithful canonical constants — **but stand-in uses a PARALLEL `Avalonia.Chaos.ChaosTuning`** (aliased `AvaloniaChaosTuning`, `AvaloniaHeadStubs.cs:26`). Two tuning classes coexist. |
| EffectPayload.cs | Faithful abstraction; concretes in Avalonia |
| ChaosCrashSentinel.cs | Faithful/complete; DI singleton; wired BeginRun/EndRun/Cleanup |
| IBubbleRenderer.cs | Faithful/complete |
| IChaosTunnelService.cs | Faithful/complete seam (Windows-only impl) |

## 3. `CCP.Avalonia/Chaos/` helpers
- `AvaloniaChaosCompat.cs` — `AvaloniaChaosEnv`, `IAvaloniaBubbleService`, `AvaloniaChaosMode` (StoryModeEnabled=false kill-switch ~:78), **`AvaloniaChaosSfx`** (static; mod-overridable resolve; IAudioPlayer), `AvaloniaChaosArt`, `ChaosSidebarBoon`.
- `AvaloniaEffectPayloadFactory.cs` — `ForVariant(string)` → payload; used by Core BubbleEngine for Trigger-Bubbles. (Parallel to stand-in's BuildPayload.)
- `AvaloniaEffectPayloads.cs` — 9 concrete payloads; braindrain OverlayPayload → `ShowChaosFlashWash` (:77); GifCascadePayload → `ShowChaosGifCascade` (:244).
- `AvaloniaChaosCatalogs.cs` — provides `ChaosBoonPool`, `ChaosLifetimeBoons`, `ChaosBubbleVariants`; `EnsureInitialized()` from ctor.
- `AvaloniaChaosStubs.cs` — static facades: `ChaosMeta` (:472 → IChaosMetaService), `RevealService` (:524), `ChaosRanks`, `ChaosNarrativeHooks` (:940), `AvaloniaChaosApp` (:1078). `ChaosNarrator.Speak` → `AnnounceChaosNarrator` (:860).
- `ChaosHudWindow` (window, stays), `ChaosHubWindow` (hub UI), `ChaosBoonBarOverlay` (window; EnsureCreated/SetPicks/RaiseActive/CloseActive wired).

## 4. The 8 chaos compositor layers (z-band 100-199, `CompositorLayers.cs:60-105`)

| Layer (Z) | Driven today | Status |
|---|---|---|
| ChaosFieldFxLayer (100) | seam defs + harness only | **SEAM-ONLY, 0 production callers** |
| ChaosDvdLayer (105) | porn_dvd toy (:1247); SpankerRedirect DEAD | wired via toy |
| ChaosGifCascadeLayer (110) | GifCascadePayload.Fire (:244) | via payload only |
| ChaosFlashWashLayer (115) | OverlayPayload braindrain (:77) | via payload only |
| ChaosCursorGlowLayer (130) | rabbit_caller aim loop (:1304) | wired via toy |
| ChaosEffectBannerLayer (140) | porn_dvd toy (:1253) | wired via toy |
| ChaosPopTextLayer (145) | seam def (:1374) + harness only | **SEAM-ONLY, 0 production callers** |
| ChaosAnnouncerLayer (150) | ChaosHappyPath ×8, overlay :603, narrator :860 | wired |

**The docs' "6 unwired passives"** = RESOLVED 2026-07-05. All 6 formerly-unmigrated window overlays are DONE: `ChaosWaveTimerOverlay`->`ChaosWaveTimerLayer`, `ChaosFxWindow`->`ChaosFxLayer`, `ChaosVibeTrailOverlay`->`ChaosVibeTrailLayer`, `ChaosEStimOverlay`->`ChaosEStimArcLayer` (arc); `ChaosEStimGlowOverlay` + `ChaosSkiaFxOverlay` windows DELETED (dead/unwired — EStimGlow's charge-glow is the deferred charged-pop FEATURE, SkiaFx's effects render via the simpler layers). **All 6 window classes are removed from the tree — do NOT look for or migrate them.** Remaining port obligation (a) stands only for any still-seam-only layer methods (production callers arrive with the run-engine port).

## 5. DI
`ServiceCollectionExtensions.cs`: `:106` IBubbleService→AvaloniaBubbleService (owns 2 BubbleEngines, implements IBubbleRenderer) · `:256` IChaosService→AvaloniaChaosService · `:281` ChaosCrashSentinel · chaos block: IChaosMetaService→ChaosMetaService, IRevealService→RevealServiceImpl, IChaosEnvironment, IChaosModeState (`Chaos/ChaosServices.cs`) · `:257-260` IAvatarWindowService/IBarkService/IVideoInfo/IMainWindowService.
Head: `CCP.Avalonia.Desktop.Windows/Program.cs:86-88` IChaosTunnelService→ChaosTunnelService (Windows only; null elsewhere = feature off). CompositorEngine nullable → layers unregistered when null.

## 6. Tests
**NO unit tests exist for BubbleEngine / AvaloniaChaosService / WPF ChaosModeService.** Peripheral only: `AchievementServiceTests.cs:123-134` (TrackBubblePopped milestone), `AiCommandParsingTests` (bubbles command DTO), `ScreenSelectionTests.cs:11` (dual-monitor GetEffectScreens), `SessionEngineTests.cs:66-83` (stub). The port must add its own Core test coverage.

## 7. Seams to adjacent systems
- **XP:** `IProgressionService.AddXP(int, XPSource)` (`App.cs:285`); `AvaloniaProgressionService.AddXP` (`Platform/AvaloniaProgressionService.cs:39`) applies `GetTotalXpMultiplier()` INTERNALLY (~:48). Pass PRE-multiplier amount; `XPSource.Chaos` exists.
- **Meta:** `ChaosMeta` facade (`AvaloniaChaosStubs.cs:472`) → `IChaosMetaService` (`ChaosServices.cs:90`, impl :130); `chaos_meta.json` at `env.UserDataPath` (:147); atomic tmp+Move Save (:203-216); Init at `App.xaml.cs:1331`.
- **SFX:** NO IChaosSfx interface — static `AvaloniaChaosSfx` (mod-overridable resolve + IAudioPlayer + master volume). DVD layer gets `PlaySfx` delegate (:177).
- **Lessons:** static `ChaosLessonHooks` (`Chaos/ChaosLessons.cs:185`) + `ChaosLessons` (:25); persists via ChaosMeta.
- **Reveal:** `RevealService` facade (:524) → `IRevealService` (`ChaosServices.cs:551`, impl :565): `Sync(reason)`, `IsUnlocked/IsPending/IsSeen`, `Clamp`, `PendingIds`, `MarkSeen`, event Pending.
- **Narrative:** static `ChaosNarrativeDirector` (`Chaos/ChaosNarrativeDirector.cs:19`), `Pick(ctx, category)` (:34), gated `AvaloniaChaosMode.NarrativeActive` (forced off); `ChaosNarrativeHooks` (:940).
- **Sentinel/tunnel/bark/avatar/video:** all present + driven.

## PORT SURFACE

### (a) Files that must change
1. `CCP.Avalonia/Services/AvaloniaHeadStubs.cs` — replace stand-in internals; EXTRACT AvaloniaChaosService to its own file(s).
2. `CCP.Core/Services/Chaos/IBubbleService.cs` — `BeginChaosMode` seam has only 6 callbacks; widen (DIMs) or route via `AvaloniaBubbleService`'s existing richer 16-arg overload (`AvaloniaBubbleService.cs:201`).
3. `CCP.Avalonia/Services/AvaloniaBubbleService.cs` — 6-arg path passes `null` for 9 behavioral callbacks (:185-199); supply them.
4. `AvaloniaChaosCatalogs.cs` + ChaosBoonPool/ChaosLifetimeBoons/ChaosBubbleVariants — faithful variant pool/spawn director (BuildDarter exists; density curve/acts do not).
5. Production callers for ChaosPopTextLayer + ChaosFieldFxLayer.
6. `CompositorLayers.cs` + new layers for the 6 unmigrated overlays (follow-up rows).
7. New CCP.Core.Tests coverage (none exists).

### (b) Seams to preserve
`IChaosService` (don't narrow) · Core `BubbleEngine` (feed, don't rewrite) · Core `ChaosTuning` (collapse the parallel Avalonia one INTO it) · `IBubbleRenderer` · all 8 layer seams (verify-layers-proven) · `IChaosMetaService` atomic save · `AddXP` pre-multiplier contract · sentinel/tunnel/bark/avatar/video · lessons/reveal/narrative facades · `AvaloniaEffectPayloads` + factory (collapse stand-in's parallel BuildPayload).

### (c) Gaps with NO seam
1. Behavioral-bubble spawn direction (the heart of the port).
2. 9 behavioral callbacks unbound on the service side (scoring/lesson/narrative reactions missing).
3. PopText + FieldFx production callers (pop floaters; ripple/residue/trail/tether from BubbleEngine field-hazard state).
4. The 6 unmigrated passive overlays.
5. E-Stim visual chain (ArmEStim exists on IBubbleService; glow/bolt visuals have 0 callers).
6. No full-run test harness (verify-layers exercises layers in isolation).
7. Two parallel tuning + payload maps — collapse to single source.
8. `AwardLoopTip` clean-loop proxy: stand-in lacks per-loop detonation counter (uses whole-run `Detonated==0`); WPF judges per final loop.
