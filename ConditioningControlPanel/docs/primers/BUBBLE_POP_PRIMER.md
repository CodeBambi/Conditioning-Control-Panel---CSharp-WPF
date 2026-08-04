# BUBBLE POP — Feature Primer

> **Purpose.** One-load orientation for the floating-bubble popping minigame ("Bubble Pop") — so a
> future engineer (or Claude) can understand and maintain it without re-exploring the codebase.
> Read §0–1 for what it is (and what it is NOT); §2 for the file map; **§3 for how it's invoked and
> how it touches the rest of the app (the load-bearing section)**; §4–5 for the internal engine
> architecture; §6 for the Chaos-Mode reuse; §7 settings; §8 where-to-change-X; §9 gotchas.
>
> **Freshness.** Tracks the code as of **2026-07-23** (branch `fix/web-video-interruptions`,
> HEAD `95586020`, v6.5.0). §2–9 track code and rarely rot; **§10 is a dated snapshot — verify with
> `git log`/`git blame` before acting on it.** `file:line` refs were read-verified when written, but
> `BubbleService.cs` is ~4850 lines and churns — confirm a line with a quick read before quoting it.

---

## 0. What Bubble Pop is, in one paragraph

Bubble Pop is the ambient **floating-bubble popping minigame**: soft `bubble.png` orbs drift up the
screen (`ChaosMotion.FloatUp`), and the user clicks them to pop for XP, a pop SFX, an achievement
tick, and haptic feedback. It unlocks at **Level 20**, is toggled from the dashboard's Bubble Pop
card, and runs on top of everything else as click-through top-level windows (or, by default, on a
single shared render host). The whole thing is one service — **`BubbleService`** (`App.Bubbles`) —
whose `Bubble` inner class is a general-purpose, DPI-aware, poolable floating-orb primitive. That
primitive is deliberately **reused by three very different consumers**: the ambient pop game, opt-in
**Trigger Bubbles** (ambient bubbles that fire a Chaos effect payload on pop), and the entire
**Chaos Mode / "Down the Rabbit Hole" (DTRH-WPF)** roguelite, which drives the same service through a
large callback-based `BeginChaosMode` API. Because of that reuse, `BubbleService.cs` is ~90% Chaos
Mode plumbing wrapped around a small ambient core.

---

## 1. DISAMBIGUATION — four things called "bubble" (read this first)

There are **four** separate bubble features. Do not confuse them.

| Feature | Code | What it is |
|---|---|---|
| **Bubble Pop** (this doc) | `Services/BubbleService.cs`, `Features/BubblePopFeatureControl.*` | The ambient float-up-and-click pop minigame. Lv.20. |
| **Chaos Mode bubbles** | `Services/Chaos/ChaosModeService.cs` + `BubbleService.BeginChaosMode(...)` | The DTRH-WPF roguelite (fuse/defuse "live" bubbles, darters, boons, etc.). **Reuses this same `BubbleService`**, so it is covered here in §6 — but it is its own feature. |
| **Bubble Count** | `Services/BubbleCountService.cs`, `Features/BubbleCountFeatureControl.xaml.cs`, `Windows/BubbleCountWindow.xaml.cs` | A **Level-50+ bubble-counting VIDEO minigame** — count bubbles in a clip, answer, get graded. **Entirely unrelated to Bubble Pop** beyond the name; it even calls `BubbleService.PauseAndClear()`/`Resume()` to get Bubble Pop out of its way. Not covered further here. |
| **AvatarRandomBubble** | `AvatarTube/AvatarRandomBubble.cs` (spawned from `AvatarTube/AvatarTubeWindow.Speech.cs`, gated by `AppSettings.RandomBubbleEnabled`) | A **cosmetic clickable bubble that spawns near the companion avatar**. Its own self-contained window+timer implementation (own pool), NOT part of `BubbleService`. It is *not* the avatar's speech bubble (that's the speech-bubble UI in `AvatarTubeWindow`), and it is *not* Bubble Pop. Only mentioned to rule it out. |

Everything below is about **Bubble Pop** and the shared `BubbleService`/`Bubble` engine it lives in
(including the Chaos Mode reuse, because that is where most of the code and most of the risk is).

---

## 2. WHERE IT LIVES — file map

| File | Role |
|---|---|
| `Services/BubbleService.cs` (~4850 lines) | **The whole engine.** Top half = the `BubbleService` service (ambient game + Chaos API + audio pool + animation driver). Bottom half (from `internal class Bubble` at `BubbleService.cs:2037`) = the per-bubble primitive (physics, fuse, hit-test, three render paths, pop/destroy). |
| `Features/BubblePopFeatureControl.xaml` / `.xaml.cs` | The dashboard settings card (enable, frequency, volume, speed, "Solid mode", Trigger Bubbles + effect-type pickers). Two-way binds to `App.Settings.Current` and live-applies to `App.Bubbles`. |
| `Models/CommandData/Bubbles.cs` | The AI-command DTO: `record Bubbles(bool On, int Frequency)`. |
| `Services/Commands/BubbleCommand.cs` | Dispatches a `Bubbles` command → `App.Bubbles.Start/Stop` (`MaxFrequency = 10`, `BubbleCommand.cs:11`). |
| `Services/Compositor/BubbleLayer.cs` | The **compositor render path**: a pure Skia draw-list twin of the shared-host field. `BubbleService`'s tick drives physics on an (unshown) WPF tree, then copies computed visual state into a `BubbleItem` per frame (`Bubble.SyncLayerItem`). `ZIndex = CompositorLayers.Bubbles = 45`, `WorldSpacePx = true` (mixed-DPI correct). |
| `Chaos/ChaosBubbleHostOverlay.cs` | The **Canvas shared-host render path**: one full-virtual-screen, click-through (`WS_EX_TRANSPARENT`) `Window` whose `Canvas` parents every bubble's `_grid`. Ref-counted; created once per run, closed at teardown. Pops come from the global mouse hook, not WPF hit-testing. |
| `Services/Chaos/ChaosBubbleHints.cs` | First-contact verb-hint pills ("click to pop", "hold to snap") — `KeyFor`/`TextFor`/`MarkLearned`, persisted learned-set. Used by both ambient trigger bubbles and Chaos. |
| `Services/Chaos/ChaosBubbleVariants.cs` | `EffectBubbleSpec` + the variant table (`All`, `Build`, `BuildGolden/Prism/Brittle/Echo/Tease/…`). Bubble Pop's Trigger Bubbles reuse `ChaosBubbleVariants.Build(..., ambient:true)`; Chaos Mode uses the whole menagerie. |
| `App.xaml.cs:1404` | `Bubbles = new BubbleService();` — construction (step 3-ish of `OnStartup`, alongside Flash/Overlay/ScreenShake). Static accessor `App.Bubbles`. |
| `Models/AppSettings.cs:2202` (`#region Bubbles`) | All Bubble Pop settings (see §7). |

---

## 3. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

This is the section to read before touching anything. `BubbleService` is a shared dependency with an
unusually wide fan-in. The public surface is small — `Start` / `Stop` / `SpawnOnce` /
`RefreshFrequency` / `PauseAndClear` / `Resume` / `GetGazeTargets`, plus the whole Chaos API — but it
is called from a dozen subsystems.

### 3a. Who starts/stops the ambient game (the `Start`/`Stop` fan-in)

`BubbleService.Start(bypassLevelCheck, frequency?)` at `BubbleService.cs:169`; `Stop()` at `:715`.

- **Dashboard card** (`BubblePopFeatureControl.xaml.cs`): the enable toggle live-starts/stops when the
  engine is running (`:99-105`); the frequency slider calls `RefreshFrequency()` (`:116`); the "Solid
  mode" toggle bounces the service (`Stop()`+`Start()`, `:153-157`) because the render path is latched
  per Start→Stop session.
- **SessionEngine** (AI sessions): starts on session begin if `BubblesEnabled` (`SessionEngine.cs:444`),
  and starts with `bypassLevelCheck: true` for scripted session steps (`:669`, `:1055`); stops at
  `:291`, `:409`, `:1061`; retunes cadence via `RefreshFrequency()` at `:584`.
- **BubbleCommand** (AI/remote command dispatch): `App.Bubbles.Start(true, freq)` / `Stop()`
  (`BubbleCommand.cs:32/34`). The DTO is `Models/CommandData/Bubbles.cs`; frequency is clamped 0..10 and
  intent is tolerant (`freq > 0` implies start even if `On` is unset).
- **RemoteControlService**: `Start(bypassLevelCheck: true)` / `Stop` (`RemoteControlService.cs:1077/1081`,
  plus stops at `:810/:913`).
- **Deeper action dispatcher**: `App.Bubbles.Start()/Stop()` (`Services/Deeper/IActionDispatcher.cs:497/505`);
  Deeper enhancements carry `EffectMaxBubbles` (`EnhancementEngine.cs:315`, serializer default 5).

### 3b. Single-shot spawns (keyword / quiz triggers)

`SpawnOnce()` (`BubbleService.cs:890`) spawns one bubble immediately even when the service isn't
continuously running (it lazily starts the animation driver and self-idles it via
`StopAnimationTimerIfIdle`). Callers:
- **KeywordTriggerService** (`:1555`) — a spoken/typed keyword pops a bubble on screen.
- **QuizWindow** (`:1218`) — a quiz interaction spawns one.

### 3c. Video coordination (yielding to mandatory video / Bubble Count)

- **VideoService** calls `App.Bubbles.PauseAndClear()` (`VideoService.cs:1708`) before a mandatory
  video and `Resume()` (`:4067`) after — `PauseAndClear`/`Resume` are at `BubbleService.cs:796/810`.
  (The avatar easter-egg also self-suppresses while `App.Video.IsPlaying`, `:600`.)

### 3d. Progression / achievements / XP / haptics (the reward on pop)

All in `AwardAmbientPop` (`BubbleService.cs:941`), shared by plain bubbles (`OnPop`, `:936`) and trigger
bubbles (their benign-pop callback calls it too, `:1051`):
- Lucky roll via `App.SkillTree.RollLuckyBubble()` (`:944`) → XP multiplier; sparkle-boost visual gated
  on skill tier + `FlashGlowEnabled` (`:948`).
- **XP**: `App.Progression.AddXP(5 * multiplier, XPSource.Bubble)` (`:957`).
- **Achievements**: `App.Achievements.TrackBubblePopped()` (`:960`).
- **Haptics**: `App.Haptics.BubblePopAsync()` (combo system, `:963`).
- **Events**: `OnBubblePopped` / `OnBubbleMissed` (`:166-167`) — subscribed by the companion
  (`BarkService.cs:488/490` for barks; `AvatarTubeWindow.xaml.cs:275/276` for avatar reactions,
  unsubscribed in `AvatarTubeWindow.Windowing.cs:836/837`).
- **Discord**: `App.DiscordRpc.SetBubbleActivity()` on Start (`:203`), `SetIdleActivity()` on Stop (`:735`).
- **PerformanceProfile**: counts `App.Bubbles.ActiveBubbles` toward the global overlay load
  (`PerformanceProfile.cs:30`).

### 3e. Gaze / focus targeting

`GetGazeTargets()` (`BubbleService.cs:81`) returns a defensive snapshot of gaze-poppable bubbles;
**GazeFocusService** iterates it (`GazeFocusService.cs:457`) and drives `SetGazeDwellProgress` /
gaze-pop (webcam "stare to pop", gated by `WebcamTriggerBubbleStare`).

### 3f. Trigger Bubbles → the effect/overlay stack (the app-touching pop)

Opt-in (`BubbleTriggersEnabled`): a configurable share of ambient bubbles are built as **benign**
`EffectBubbleSpec` effect bubbles (`RollTriggerSpec` `:973` → `BuildTriggerSpec` `:985` →
`CreateAmbientBubble` `:1028`). On pop they pay the normal ambient reward AND fire an
`EffectPayload` — flash, subliminal, pink filter, spiral, glitch (full-screen `braindrain` wash),
Cascade/GifRain (`htlink`), or video. So a Bubble Pop pop can reach into **FlashService,
SubliminalService, OverlayService, VideoService** via the payload's `Fire()` (`:1053`). Payload
timing is stretched (`LINGER = 2.5`, except glitch at 1.2×). This is the main "Bubble Pop touches
other features" path outside Chaos.

Companion easter egg (`BubbleAvatarEggEnabled`, default on, gated under `BubbleTriggersEnabled`): a
lingering (>4s) ambient effect bubble has a 10% one-shot roll to send the avatar gliding over to
narrate + pop it 50% louder (`TryTriggerAvatarBubbleEgg` `:592`, `RunAvatarBubbleEggAsync` `:622`,
voicelines via `App.Bark.PickVoiceLine` `:680`). 60s cooldown; one at a time.

### 3g. Chaos Mode (the largest consumer) — see §6

**ChaosModeService** drives `BubbleService` through `BeginChaosMode(...)` (`ChaosModeService.cs:365` →
`BubbleService.cs:1096`) and dozens of `SpawnChaos*` / `Set*` / `PopAll*` calls. This is a separate
feature that reuses the engine; §6 maps it.

### 3h. The compositor

When the Unified Overlay Host is on (`App.CompositorEnabled`, mirrored by `Bubble.UseCompositor`
`:2074`), host-rendered bubbles draw on `Services/Compositor/BubbleLayer.cs` instead of a Canvas host
or per-window. Same hook-pop contract, mixed-DPI correct (`WorldSpacePx`). See §5.

---

## 4. THE SERVICE — `BubbleService` internal architecture

### 4a. Lifecycle
`Start` (`:169`): load `bubble.png` (`LoadBubbleImage` `:820`, via `ModResourceResolver` so mods can
reskin), stand up the ambient shared host if enabled (`BeginAmbientHostIfEnabled` `:743`), start the
spawn `DispatcherTimer` at `60000/frequency` ms, start the animation driver, spawn one immediately.
`Stop` (`:715`): stop spawn timer, stop driver, `PopAllBubbles()`, `EndAmbientHost()`. `Dispose`
(`:2016`): `Stop()` + drain the static window pool + drain the static audio-device pool.

### 4b. The animation driver (composition-clock, NOT a timer)
One shared driver for **every** bubble (ambient + chaos), driven off `CompositionTarget.Rendering`
(`StartAnimationDriver` `:1809`, `OnAnimationRenderTick` `:1831`) with a `STEP_MS = 30` (`:53`)
logical-step gate. **Why:** a `DispatcherTimer` at Render priority gets *starved* under UI-thread load
(a mandatory video decode + chaos FX) then fires its backlog back-to-back → the field lurches. The
composition clock coalesces to one callback per rendered frame and simply *drops* frames under load
(graceful degradation). `_lastStepMs` is re-based to real frame time, never `+= STEP_MS`, so a late
frame can't trigger catch-up bursts. Idempotent via `_animDriverHooked`; **must** be detached on
teardown/idle (a stranded `Rendering` subscription keeps the render loop pumping = leak + wasted work).

### 4c. The per-tick pass (`AnimateAllBubbles` `:461`)
1. **Spawn-spike amortization**: drain at most `MaxSpawnsPerFrame = 1` (`:147`) queued chaos-bubble
   construction thunks from `_spawnQueue` — a cadence burst spreads across frames instead of blocking
   the UI thread in one synchronous `BuildChaosLayers` pass.
2. Sample cursor + boon knobs **once per tick** into shared static fields (`CursorPxX/Y`,
   `WandShimmerOn`, `ChaosCursorPullNow`, `ChaosMouseHeld`, etc., `:120-160`, `:487-502`) — one
   `GetCursorPos`/`GetAsyncKeyState` P/Invoke for the whole field instead of per-bubble.
3. `AnimateFrame()` every bubble (reverse index iteration, no alloc).
4. `TickFieldHazards()` (`:1388`, Size Queen ripples / Aftermath residue / Tail-Plug trails),
   `TickBoundPairs()` (`:1523`), `TryTriggerAvatarBubbleEgg()` (`:592`).
5. Rebuild the immutable off-thread hook snapshots: `ChaosBubbleCentersSnapshot` (right-click Ripple
   swallow decision) and `ChaosClickDiscsSnapshot` (left-click pop hit discs — physical px, only
   `UsesHost && HostHitClickable` bubbles). Reference assignment is atomic; the hook thread only ever
   reads these snapshots, never the live `_bubbles` list.

### 4d. Spawning
- `SpawnBubble` (`:847`, timer-driven) and `SpawnOnce` (`:890`, one-shot) both go through
  `CreateAmbientBubble` (`:1028`). Cap: `_bubbles.Count >= MaxAmbientBubbles` (`:34`, = 3 per-window /
  40 hosted). Honors `DualMonitorEnabled` (spawn on any screen) and `DisplayChangeCoordinator.
  SpawnsSuppressed` (hold off during DPI/monitor churn — freeze cluster).
- Chaos spawns (`SpawnChaosBubble` `:1164`, `SpawnChaosChaperone` `:1182`, `SpawnChaosBoundPair`
  `:1229`) enqueue construction into `_spawnQueue` (drained per-frame). `PickScreenFor` (`:1275`) pins
  chaos to the HUD/primary screen (roguelite must stay single-screen even with dual-monitor flashes).

### 4e. Pop path & hit-testing
- **Hosted** (Canvas or compositor): the global mouse hook fires `OnSharedHostLeftDown(px)` (`:551`,
  hook thread) → tests `ChaosClickDiscsSnapshot` → marshals `PopTopmostAt(px)` (`:575`, UI thread,
  last-spawned-first = topmost) → `Bubble.HostHookPop()` → `OnPlayerPress`. Hold-to-defuse live
  bubbles do **not** swallow the click (the channel must see the held button via `GetAsyncKeyState`).
- **Per-window**: each bubble keeps its own WPF click handler → `OnPlayerPress`. `UsesHost` is the
  invariant that keeps a per-window bubble out of the disc snapshot so it can never double-pop.
- `OnPlayerPress` (`:3778`) → for non-live, `PopByClick` (`:3906`) → `Pop` (`:3920`). `Pop` routes by
  spec: darter→catch, freeze→freeze pickup, live→defuse (snap), treat→benign effect fires. Actual
  destruction is deferred to the pop animation's completion (`AnimateFrame` → `Destroy` `:4647` →
  `OnDestroy` `:1070`), so index scans are safe against same-frame pops.

### 4f. Audio
Pooled `WaveOutEvent` devices (`MAX_POOLED_DEVICES = 4`, `:1882`) to avoid per-pop device creation.
`PlayPopSound` (`:1839`) picks `Pop.mp3/Pop2/Pop3` (lucky → `chime1/2/3`), volume =
`(master*bubbles)^1.5 * mult`. Chaos cues via `PlayChaosCue`/`PlayCue` (`:1771/1780`, silent no-op if
the asset is missing — cues ship code-first). `DrainAudioDevicePool` (static, `:1905`) is called when
the output-device setting changes.

---

## 5. THE `Bubble` PRIMITIVE — three render paths

`internal class Bubble` (`BubbleService.cs:2037`); ctor `:2670`; `AnimateFrame` `:3105`. One class,
three mutually-exclusive render modes decided in the ctor (`:2970-2976`):

1. **Per-window** (`_window`, the original path): each bubble is a pooled, hidden, click-through
   `WS_EX_LAYERED` `Window` (`_useHost == false && _useLayer == false`). Windows are **pooled by
   quantized size bucket** (`_windowPool`, `WINDOW_POOL_MAX = 64`, `:2047-2054`) — created/closed
   churn floods the WPF finalizer queue → 2GB+ OOM (see `chaos-bubble-oom-leak`); resizing a rented
   shell triggers `HwndTarget.OnResize`→synchronous `CompleteRender` = UI-thread deadlock (#494), so
   size-bucketing means a shell is never resized after creation. Repositioned via `SetWindowPos` each
   frame (expensive — this is why the cap is 3).
2. **Canvas shared host** (`_useHost`): the bubble's `_grid` is a child of the one
   `ChaosBubbleHostOverlay` Canvas, positioned via `Canvas.SetLeft/Top` (cheap, batched). Pops via the
   global hook. Mixed-DPI: the host renders at one scale; placement is in physical px against the
   hwnd origin, with a per-bubble `LayoutTransform` of `bubbleScale/RenderScale` for off-scale monitors.
3. **Compositor `BubbleLayer`** (`_useLayer`): no per-bubble window and no Canvas child. The WPF
   `_grid` tree still exists (unshown) and is animated by `AnimateFrame`; its computed visual state is
   copied into a `BubbleLayer.BubbleItem` each frame via `SyncLayerItem` (`:2599`, built by
   `BuildLayerItem` `:2537`) and drawn in Skia. This kills the last per-effect layered surface.
   `BubbleLayer.Render` (`BubbleLayer.cs:133`) redraws every item from that copied state — it never
   re-derives animation. Sprites are a decode-once, never-freed `SKImage` cache
   (`_spriteCache`, `BubbleLayer.cs:39`); blur/dash/glyph-fallback (#615) are cached per quantized
   input.

Selection: `wantsHost = !forceWindowMode && (chaosHost || ambientHost)`; `_useLayer` when
`wantsHost && UseCompositor`; else `_useHost` when a Canvas host is up; else per-window
(`forceWindowMode` pins per-window when the host can't cover the target screen). `UsesHost`
(`:2508`) = `_useHost || _useLayer` — the single invariant gating hook-pop vs WPF-click.

---

## 6. CHAOS MODE REUSE (the big consumer)

`ChaosModeService` (`Services/Chaos/ChaosModeService.cs`) is the DTRH-WPF roguelite and the reason
most of `BubbleService.cs` exists. It never touches `_bubbles` directly — it drives the service:

- **Enter/exit**: `BeginChaosMode(...)` (`BubbleService.cs:1096`, called `ChaosModeService.cs:365`)
  installs ~30 callbacks/knobs (benign-pop, defuse, detonate, darter/freeze caught, tease, brittle,
  bound, chain reach, hitbox scale, opacity, wand shimmer, cursor pull, spanker, e-stim, etc.) and
  latches `_sharedHost`. `EndChaosMode()` (`:1675`) nulls every callback, clears field state, and
  `PopAllBubbles()`.
- **Spawn menagerie**: `SpawnChaosBubble` (`:1164`), `SpawnChaosChaperone` (`:1182`),
  `SpawnChaosBoundPair` (`:1229`) — built from `ChaosBubbleVariants` (golden/prism/brittle/echo/
  tease/darter/etc.). Specs are `EffectBubbleSpec` (`ChaosBubbleVariants.cs:29`).
- **Live control**: `SetChaosFrozen` (freeze power-up, `:210`), `SetChaosTimeScale` (darter slow-mo,
  `:216`), `SetChaosInputLocked` (manual pause, `:219`), `SetVibePop` (`:224`), `ArmEStim` (`:314`),
  `TriggerPlayerRipple`/`TriggerChaosRipple` (`:1351/1364`), `BringAllToFront` (`:1665`).
- **Polling**: `MinChaosFuseSec` (`:1733`), `IsCursorOverLiveChaosBubble` (`:1747`), `ActiveBubbles`,
  `ActiveFreezeBubbles` (`:75`).
- **Boon field hazards** run on the shared anim tick (no extra timers): Size Queen ripples, Aftermath
  residue, Tail-Plug trails (`TickFieldHazards` `:1388`), Chain Reaction (`ChainPopNeighbors` `:1634`),
  Spanker (`SpankSweepFromDarter` `:1592`), E-Stim arcs (`OnChaosBubbleClicked` `:329`).

**Memory cross-refs** (in `memory/MEMORY.md`): `chaos-bubble-oom-leak` (sprite cache + shared host is
the cure for the per-window OOM), `bubble-shared-host-refactor` (the `ChaosBubbleSharedHost` flag),
`chaos-bubble-sprite-pipeline`, `chaos-spawn-lag` (spawn amortization), `bubble-hang-flash-pool-ui-decrypt`
(UI-thread decrypt hang), `perf-tier-system` / `wpf-compositor-unified-overlay-host` (the BubbleLayer).

---

## 7. SETTINGS REFERENCE (`Models/AppSettings.cs`, `#region Bubbles` `:2202`)

| Setting | Line | Default | Purpose |
|---|---|---|---|
| `BubblesEnabled` | `:2204` | false | Master on/off (Lv.20). |
| `BubblesFrequency` | `:2210` | 5 | Spawns/min, clamped 1..60. (AI `BubbleCommand` clamps to 10.) |
| `BubbleSharedHost` | `:2224` | **true** | "Solid mode": ambient bubbles ride the shared host (Canvas or compositor). Off = classic per-window (cap 3). Latched per Start→Stop. |
| `BubblesVolume` | `:2230` | 50 | Pop SFX volume (0..100). |
| `BubblesLinkRamp` | `:2236` | false | Link spawn rate to session ramp. |
| `BubblesClickable` | `:2242` | true | Whether bubbles are clickable during sessions (always clickable outside sessions). |
| `BubbleSpeedBoost` | `:2262` | 0 | +0..500% travel speed. |
| `BubbleTriggersEnabled` | `:2250` | false | Opt-in: some bubbles fire a Chaos effect on pop. |
| `BubbleTriggerChance` | `:2256` | 10 | % of spawns carrying an effect (0..50). |
| `BubbleTriggerVariants` | `:2272` | flash/subliminal/pink/spiral/glitch/htlink/video | Effect pool (equal odds). `"htlink"`=Cascade/GifRain; `"glitch"`=full-screen wash. |
| `BubbleAvatarEggEnabled` | `:2280` | true | Companion "I'll pop it for you" easter egg (gated under Trigger Bubbles). |
| `ChaosBubbleSharedHost` | `:2382` | true | Chaos-Mode render path (separate from ambient `BubbleSharedHost`). |

Related (not in the region): `RandomBubbleEnabled` (AvatarRandomBubble — different feature),
`WebcamTriggerBubbleStare`, `AutonomyCanTriggerBubbles`, `DualMonitorEnabled`, `MasterVolume`,
`FlashGlowEnabled` (sparkle boost).

---

## 8. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Change the on-screen cap | `MAX_BUBBLES` / `MAX_BUBBLES_HOST` / `MAX_TRIGGER_WINDOWS` (`BubbleService.cs:25/26/31`). |
| Change the logical frame rate | `STEP_MS` (`:53`). Motion math is tuned to ~30fps — retune both. |
| Change the pop reward (XP/achievement/haptic) | `AwardAmbientPop` (`:941`). |
| Change pop/lucky SFX | `PlayPopSound` (`:1839`); files via `ModResourceResolver`. |
| Add/tune a Trigger Bubble effect type | `BuildTriggerSpec` (`:985`) + the checkbox in `BubblePopFeatureControl.xaml` + the id in `BubbleTriggerVariants` default (`AppSettings.cs:2270`). Standard ids reuse `ChaosBubbleVariants`. |
| Change what a hosted click hits | `HitDiscPx`/`HostHitClickable`/`NeedsHoldDefuse` (`:2521/2511/2517`) + `OnSharedHostLeftDown` (`:551`). |
| Change compositor rendering | `Services/Compositor/BubbleLayer.cs` (`Render` `:133`) + `Bubble.SyncLayerItem` (`:2599`). Keep the two in lockstep — the layer only draws copied state. |
| Change the Canvas host window | `Chaos/ChaosBubbleHostOverlay.cs`. |
| Add a Chaos variant/mechanic | `Services/Chaos/ChaosBubbleVariants.cs` + a callback in `BeginChaosMode` (`:1096`) + `ChaosModeService`. |
| Add a first-contact hint | `Services/Chaos/ChaosBubbleHints.cs` (`KeyFor`/`TextFor`). |
| Wire a new invoke path | `Start`/`Stop`/`SpawnOnce`/`RefreshFrequency` on `App.Bubbles`. |

---

## 9. GOTCHAS (the expensive ones)

1. **Per-window bubbles are a native-resource landmine.** Creating/closing layered windows per bubble
   caused a 2GB+ OOM (no managed exception) — hence the size-bucketed window pool (`:2047`). Never
   create a per-bubble `Window` outside the pool, and never resize a rented shell (triggers
   `HwndTarget.OnResize`→synchronous `CompleteRender`, the #494 UI-thread deadlock). This is why the
   default is the shared host and the per-window cap is 3.
2. **The animation driver MUST be detached on teardown/idle** (`StopAnimationDriver` `:1820`). A
   stranded `CompositionTarget.Rendering` handler keeps the render loop pumping at full refresh rate
   forever. `StopAnimationTimerIfIdle` (`:1081`) handles the `SpawnOnce`-without-`Start` case.
3. **The render path is latched per Start→Stop session** (`_sharedHost`/`_ambientHost`). Changing
   `BubbleSharedHost` mid-run does nothing until the service is bounced — the dashboard toggle
   `Stop()`+`Start()`s deliberately (`BubblePopFeatureControl.xaml.cs:153`).
4. **Hook snapshots are the only cross-thread contract.** The mouse hook runs on its own thread and
   must touch **only** the immutable `ChaosClickDiscsSnapshot` / `ChaosBubbleCentersSnapshot` (rebuilt
   each UI-thread tick, atomic reference swap) — never `_bubbles` or a WPF DP. `UsesHost` keeps
   per-window bubbles out of the snapshot so they never double-pop.
5. **Hold-to-defuse live bubbles must not swallow the click** (`OnSharedHostLeftDown` returns
   `!needsHold`) — the channel reads the held button via `GetAsyncKeyState`, which never sees a
   swallowed low-level click → instant detonate.
6. **Destruction is deferred to the pop animation.** `Pop()` sets `_isPopping` but doesn't remove the
   bubble; `AnimateFrame`→`Destroy`→`OnDestroy` does. Iterate `_bubbles` by reverse index with a
   count re-check (see `PopBubblesInRect` `:283`); don't assume a pop removes same-frame.
7. **DPI/monitor churn**: spawns are suppressed during display changes
   (`DisplayChangeCoordinator.SpawnsSuppressed`, `:851`). Mixed-DPI multi-monitor is why the
   compositor layer is `WorldSpacePx` and the Canvas host does physical-px placement + per-bubble
   `LayoutTransform`. (Cross-ref root `CLAUDE.md` #5 "Screen enumeration crash" — always guard
   `Screen.AllScreens`.)
8. **The avatar easter-egg touches layered surfaces at the worst time.** Popping fires a payload
   burst while the avatar re-attaches/re-styles its own layered window — a captured render-thread
   deadlock (`hang_20260713_110759`). The choreography deliberately waits 2500ms after the pop before
   sending the companion home (`:653`). Don't remove that delay.
9. **Chaos vs ambient share one field.** `TryTriggerAvatarBubbleEgg` early-returns when `_chaosActive`
   (`:597`) or it would claim a chaos bubble. Any new ambient-only behavior must gate on
   `!_chaosActive`. Cross-ref root `CLAUDE.md` async/threading notes #6–8 (fire-and-forget guards,
   `HasShutdownStarted` checks) — `PopAllBubbles` (`:1967`) and the egg both do this.
10. **Missing chaos SFX are silent no-ops** (`PlayChaosCue`/`PlayCue`) — cues ship code-first; a
    missing file is not an error.

---

## 10. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

- **Branch** `fix/web-video-interruptions`, HEAD `95586020` (v6.5.0). `BubbleService.cs` last touched
  on the animation-driver merge `5126272a` ("drive shared bubble animation off
  `CompositionTarget.Rendering`"), preceded by the compositor `BubbleLayer` work
  (`15c2f6d0`/`f98f6132`/`83f18eb1`) and the trigger-bubble shared-host move (`5f05776c`).
- **Render paths are all live**: per-window (fallback), Canvas host (`BubbleSharedHost`/
  `ChaosBubbleSharedHost`, default on), and the compositor `BubbleLayer` (under `App.CompositorEnabled`).
  The compositor path is the strategic direction ("kills the last per-effect layered surface").
- **Related in-flight work** (from `memory/MEMORY.md`, confirm with git): compositor default-on
  regression cluster (`compositor-default-on-regression-cluster`), `bubble-shared-host-refactor`
  (plan-only in places), v6.5.0 triage batch `db92d814` (flash pool + corner gif + others, listed
  UNCOMMITTED/play-test-pending in memory). Several chaos-bubble perf notes remain "play-test pending".
- **This primer is new** and not previously committed. It documents Bubble Pop specifically; the
  Chaos-Mode roguelite it shares an engine with has no dedicated primer yet (candidate follow-up).
- **No dedicated unit tests** cover `BubbleService` directly; the standing gate is play-test plus the
  broader xUnit suite. Treat any perf change to the animation loop or window pool as play-test-gated
  (freeze clusters historically hid here).
