# SP-101 — plan checkpoint

Branch `lane/SP-101-second-effect`, worktree `.claude/worktrees/agent-a580ee0ae2e1699da`, base `f471455b`.

## 1. Which effect: Subliminals, confirmed by reading the source

`ConditioningControlPanel/Services/Subliminal/SubliminalService.cs` (64 KB, read in slices) and
`ConditioningControlPanel/CCP.Core/Models/AppSettings.cs:1234-1290` (sliced — 305 KB file; it is the
shipping WPF app's own settings model, which is why SP-098 cites it).

**It is the right second effect, and the reason is that it is NOT a near-copy.** Five places where it
differs are exactly the places a bad template breaks:

| # | Flash Images | Subliminals | What it stresses |
|---|---|---|---|
| 1 | `3600.0 / max(1,freq)`, ±30 %, floor **3 s** (`FlashService.cs:549-555`) | `60.0 / max(1,freq)`, ±30 %, floor **1 s** (`SubliminalService.cs:172-187`) | the pacing law must become DATA, not a copied function |
| 2 | `FlashEnabled` default **true** (`AppSettings.cs:751`); `StartEngine` calls `App.Flash.Start()` **unconditionally** (`MainWindow.StartStop.cs:178`) | `SubliminalEnabled` default **false** (`AppSettings.cs:1234-1235`); `StartEngine` calls it **only if the dial is on** (`:186-187`) | the dot's `Off` is the DEFAULT state; arm-with-dial-off must be expressible |
| 3 | an empty pool still **counts a flash** and still fires (`FlashService.cs:2589-2593`; port's `FlashEvent.PoolWasEmpty`) | an empty active-text pool **counts nothing and fires nothing** — `FlashSubliminal` returns at `:207-212`, before the `_subliminalCount++` / `SubliminalDisplayed` at `:611-612` — but still re-schedules (`:189-201`) | the base cannot assume every firing produces an event |
| 4 | pool = a folder of image files | pool = `Dictionary<string,bool>` of 21 default PHRASES in settings, drawn uniformly over the `true` subset (`AppSettings.cs:1263-1290`, `SubliminalService.cs:206-215`) | the pool SEAM generalises; the implementations share nothing |
| 5 | N surfaces per firing, random 40 %-of-monitor placement, 300 ms stagger, 6 s lifetime, 1 s topmost cadence | **one full-screen card**, no stagger, no placement roll, lifetime = `50 + max(100, duration*17) + 50` ms (`SubliminalService.cs:615-617`, `:1253-1255`), no cadence | the surface PRESENTER must not be copied; only its pooled-slot core is shared |

Rack facts: key `"subliminal"`, title `Subliminals`, third EFFECTS row
(`Views/Tabs/StudioTabView.xaml.cs:486-487`); quick-toggle body
`MainWindow/MainWindow.Presets.cs:1252`; stopped at `MainWindow.StartStop.cs:337`.

Draw facts: Arial Bold 120 px (`SubliminalService.cs:1237-1248`), centred, 8 outline copies at
(±3,±3)/(0,±4)/(±4,0) in white (`:990-1008`, `:1354-1358`), magenta `#FF00FF` main text (`:1340-1344`),
opaque black background (`SubBackgroundTransparent` default **false**, `:1333-1338`), window opacity
`SubliminalOpacity/100` default 80 (`:1256-1261`), full-screen per monitor (`:629-631`).

Clamps: frequency `Math.Clamp(v,1,30)` default 5 (`:1242-1247`); duration `Math.Clamp(v,1,10)` default 2
(`:1249-1254`); opacity `Math.Clamp(v,10,100)` default 80 (`:1256-1261`).

## 2. What I expect to SHARE vs DUPLICATE (predicted before building)

**Share (this is the packet's deliverable):**

1. `Effects/EffectSchedule.cs` — `IntervalLaw(SecondsPerUnit, VarianceFraction, MinimumSeconds)` plus
   base/min/max/next. `FlashSchedule` keeps its exact public surface as a named facade over it, so every
   SP-098 fact passes verbatim. `SubliminalSchedule` is a second facade.
2. `Session/PacedSessionEffect.cs` — the whole body of `FlashImagesEffect` minus its payload: arm,
   disarm, generation, park-until-cancelled, the one-shot, the stale-generation checks, the count, the
   last firing, the dot, `RefreshSchedule`, `Changed`. Hooks: `NextInterval()`, `Compose()` (outside the
   gate, **nullable** — that is difference #3), `Stamp()`, `Deliver()` (UI thread), `OnDisarmed()`.
3. `Session/ScheduledFire.cs` — a one-shot token, so `Fire` can `CompareExchange` its own identity out
   of the pending slot instead of blindly nulling it (hazard 3).
4. `Session/EffectSignal.cs` — the marshalled `Changed` raiser (hazard 2).
5. `CapabilityState Arm()` on `ISessionEffect` (hazard 1).
6. `Effects/OverlaySurfaceSet.cs` — extracted from `FlashSurfacePresenter`: pooled slots, the
   Present→Paint→withdraw-on-paint-failure sequence, the verbatim `Last*` bookkeeping, the no-display
   refusal, the optional topmost cadence, `HideAll`/`Dispose`.

**Duplicate, and correctly so:** the two pools (files vs phrases), the two geometries (random box vs
full screen), the two frame sources (GDI+ image decode vs GDI+ text raster), the two dial sets and their
clamps, the two firing records.

## 3. Hazards

- **H1 `Arm()` cannot refuse.** Closed: `Arm()` returns `CapabilityState`. It is load-bearing on day
  one and the two effects produce DIFFERENT states — Flash: `Available`, or `Unavailable(effect-dial-off)`
  when the module's dial is off (today indistinguishable from a successful arm); Subliminals: additionally
  `Degraded("the schedule is armed and paced", subliminal-no-active-phrase)` when no phrase is active,
  which is exactly WPF's outcome (`:207-212` — schedule runs, nothing shows). `SessionEngine` keeps the
  per-effect outcomes so a caller can see which modules took the session. No invented refusal.
- **H2 `Changed` on arbitrary threads.** Closed: raised through `EffectSignal`, which is inline when
  unbound or already on the UI thread and posted otherwise — the exact rule both current consumers
  hand-roll. `StudioPage.OnSessionChanged` and `MainWindow.OnSessionEngineChanged` drop their copies.
  `Lifecycle/UiDispatch.cs` is out of File Scope, so the thread-identity query is injected into
  `EffectSignal` (default `Dispatcher.UIThread.CheckAccess`) rather than added to `UiDispatchBoundary`.
- **H3 `Fire`'s handle race.** Closed by `ScheduledFire` + `Interlocked.CompareExchange`, in the shared
  base, so it is closed for all fifteen.
- **H4 `SystemSessionClock` uncovered.** Taking it: facts using only deterministic signals through
  `TestWait` — a zero-due callback really fires, a negative due is clamped rather than throwing, a
  disposed handle's callback is suppressed (proved with a scheduling barrier, not a wait), and `UtcNow`
  is UTC and monotone.

## 4. SCOPE DISCOVERY — reported, not silently widened

Subliminals needs persisted dials, and `SessionPresetDocument` lives in
`client/src/CcpClient.Desktop/Persistence/`, which is **outside this packet's File Scope**. I am not
editing it. Resolution taken: a per-module document, `Session/SubliminalPresetDocument.cs` →
`session_subliminal.json`, on the `AssetSelectionDocument` precedent the session preset itself cites.
It is additive, it is inside scope, and it is arguably the right long-term shape (fifteen modules that
each edit one shared document is the same chokepoint argument as `floor.json`; a corrupt subliminal
preset then quarantines subliminals only, instead of taking every dial to defaults). Recorded as a
divergence with the follow-up named: fold into `session_preset.json` if the owner prefers one file.

## 5. Also in scope, and why

`Views/Pages/StudioPage.axaml:152` — the "not ported yet" overlay notice, false on Windows since
SP-100. Fixed by reading the presenter's own last outcome (`CapabilityState`, verbatim) rather than
asserting a platform, per the SP-100 record's own instruction that the replacement must say both halves.

## 6. What this cannot prove

Compile + headless + pure-logic only. No headed capture, so nothing here claims a human sees a
subliminal; `presentation-verified` stays the orchestrator's. The GDI+ text raster is Windows-only and
its pixels are asserted in the pure-logic project on Windows only, exactly like SP-100's image path.
