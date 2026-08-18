# SP-098 — plan checkpoint (written before the first product edit)

Branch `lane/SP-098-session-spine-and-first-effect`, worktree
`.claude/worktrees/agent-aa7e6dc0ec3d394c1`, base `237a2156`.

---

## 1. WPF's session start/stop semantics, from source

**There are TWO things called "session" in WPF and they are not the same thing.** The packet's
"START drives fifteen effect modules" is the **ENGINE**:

* `MainWindow/MainWindow.StartStop.cs:34` `BtnStart_Click` — ONE button, toggling on `_isRunning`.
* `:159` `StartEngine()` and `:296` `StopEngine()` -> `:302` `StopEngineCore()`.
* `App.IsEngineRunning` is the app-global flag (`:269`, `:387`).

The other one — `Services/Session/SessionEngine.cs`, `SessionManager.cs` — is the **scripted
session** (a `Session` definition with phases, XP, a duration, a stop-confirmation dialog at
`MainWindow.StartStop.cs:52-88`). It runs *on top of* the engine and is **not ported here**. The
port's class is therefore named for what it ports and its doc says so, so nobody later "completes"
it by adding XP.

### Facts the port must reproduce (ordering, gating, re-entrancy)

| Fact | WPF citation |
|---|---|
| One button toggles: running -> stop, else start | `MainWindow.StartStop.cs:34,50,105` |
| Start **saves settings first**, before any service starts | `:161` `SaveSettings()` |
| Start reads the persisted dials and **gates each service on its own flag** | `:181,184,200,206,212,218,228,240` (`if (settings.XEnabled) App.X.Start();`) |
| `_isRunning`/`IsEngineRunning` flips **after** the work has started, never before | `:268-269` |
| Stop has a **re-entrancy guard** (`_stopInProgress`) because the body pumps the dispatcher | `:292-296` |
| Stop stops the work **first**; `_isRunning` flips **after** | `:305` ("Stop flash first"), `:385-387` |
| Flash is the **first** service started and the **first** stopped | `:178`, `:305` |
| A stopped engine also drops the effect's cached resources | `:405-408` (`ClearImageCache`, LOH compaction) |

### The effect's own start/stop (`Services/Flash/FlashService.cs`)

| Fact | Citation |
|---|---|
| `Start()` is idempotent (`if (_isRunning) return;`) and schedules the first tick **synchronously** | `:345-352` |
| `Stop()` clears the flag, cancels the CTS, then stops the scheduler timer, then tears down | `:367-380` |
| The schedule **refuses to arm** when the module's own dial is off — the service is "running" but nothing fires | `:538-546` |
| Interval formula: `3600.0 / freq` seconds, then **±30 % uniform variance**, then `Math.Max(3, …)` | `:548-553` |
| Each tick stops its own timer, fires if `_isRunning && !_isBusy`, then **re-schedules** | `:566-573` |
| A draw takes `SimultaneousImages` **uniform random picks with replacement** from the pool | `:2595-2652` |
| An empty pool yields an empty draw — the flash "fires" and shows nothing; WPF calls this its most common first-run dead end | `:585-597` |

### The rack row's two gestures (`Views/Tabs/StudioTabView.xaml.cs`)

* Row is a `RadioButton`; left-click selects and opens the panel (`:645-651,664-665`).
* Right-click quick-toggles (`:660`, handler `:1109-1133`), and rows with **no** toggle fall
  through unhandled (`:659`).
* The toggle body for `flash` (`MainWindow/MainWindow.Presets.cs:1250`):
  `var on = s.FlashEnabled = !s.FlashEnabled; if (running) { if (on) App.Flash?.Start(); else App.Flash?.Stop(); }`
  then `App.Settings?.Save()` (`:1264`).
* The dot's source is the **persisted flag**: `Add("flash", …, () => App.Settings?.Current?.FlashEnabled)`
  (`StudioTabView.xaml.cs:484-485`).

### DISCREPANCY found, and how it is resolved

WPF's own onboarding copy (quoted in `client/docs/wpf-surface-reachability.md` §8.3) says *"The dot
on each row is **live**: at a glance you can see everything that is **currently running**."* The
mechanism reads the **persisted enable flag** (`StudioTabView.xaml.cs:484-497`), which is "armed",
not "running" — the two coincide only while the engine runs, because start gates on the flag and
the toggle starts/stops the live service. **Source wins over the spec, and the port is honest about
both**: the port's dot is a three-state dot — `Off` (not armed), `Armed` (armed, session not
running), `Live` (armed and its owned operation really is scheduled). Recorded as a divergence.

---

## 2. Which effect — Flash Images, taken as nominated

I read `ConditioningControlPanel/Services/` for a materially better first. The result:

| Candidate | Draws through | Verdict |
|---|---|---|
| Flash Images | topmost + `WS_EX_TRANSPARENT` windows (`FlashService.cs:3615,3667-3668,3862-3868`) | **overlay for the drawing half only**; scheduler + pool are pure |
| Mandatory Video / Subliminals / Spiral / Magenta / Bouncing Text | overlay windows / the `Services/Compositor/` layers | worse |
| Bubble Pop / Count / Lock Card | own windows | worse |
| Mind Wipe | **no window at all** — NAudio only (`Services/LockCard/MindWipeService.cs:29,194,240`) | genuinely fewer *drawing* seams, but its output is audio: unverifiable in every gate this port owns, and its only observable is a device the CI box may not have. It trades an honest named gap for an unfalsifiable claim |
| Scheduler / Intensity Ramp | nothing | pure logic, but both are *meta*: the ramp ramps other effects' dials (`MainWindow.StartStop.cs:413-520`) and the scheduler drives the engine rather than running under it. Neither is a first proof of "an effect runs under the session" |

**Taking Flash Images**, as nominated. It is WPF's first EFFECTS row, the first service `StartEngine`
starts and the first `StopEngineCore` stops, and it carries the most behaviour-visible, citable
parity surface in the rack (an exact interval formula with a variance band and a floor clamp, three
clamped dials, a documented draw policy and a documented empty-pool outcome).

### What it can honestly do WITHOUT an overlay

**Can** (this packet): compute the schedule from the persisted dials, arm and disarm it under the
session, fire on the injected clock, draw `ImagesPerFlash` picks from the real active image pool,
count what it did, report an empty pool honestly, and stop dead.

**Cannot** (a separate platform packet): put those images on screen **above other applications**.
That needs an always-on-top, click-through, per-monitor surface — `SetWindowPos(HWND_TOPMOST)` +
`WS_EX_TRANSPARENT` in WPF — which is exactly the compositor work `docs/constitution.md` classes as
failure evidence. **No overlay, no compositor, and no in-window imitation of one is built here.**
The module panel says this in words; the dot never claims otherwise.

---

## 3. What gets built

### `client/src/CcpClient.Desktop/Session/` (the spine — fourteen modules follow it)

* `SessionClock.cs` — `ISessionClock` (`UtcNow` + `Schedule(due, fire)` -> `IDisposable`) and
  `SystemSessionClock`. Shape taken from the established `ISoundClock` precedent
  (`Audio/AudioSeams.cs:118-137`); declared in `Session/` rather than reused so no effect has to
  take a dependency on the audio stack to own a timer.
* `SessionEffect.cs` — `ISessionEffect`: stable `Id`, display `Title`, `Enabled` (persisted dial),
  `Dot` (the truthful three-state), `Arm`/`Disarm`, and the owned `Completion`.
* `SessionEngine.cs` — start/stop, WPF's ordering and re-entrancy guard, `Running`, `StateChanged`.
* `SessionParticipant.cs` — `IBackgroundParticipant`: owns the preset store and the effect list;
  **construction starts nothing** and phase-3 start does **not** start a session (WPF's engine only
  starts when the user presses START).

### `client/src/CcpClient.Desktop/Effects/`

* `FlashSchedule.cs` — the pure interval function (formula, variance band, floor clamp).
* `FlashImagePool.cs` — the draw, over `<dataDir>/assets/images`, through the port's ONE
  active-pool seam (`DtrhUserMedia.BuildDisabledSet`/`IsAssetActive`, SP-055) so no second scan can
  disagree. Injected as an interface so no spine test touches a disk.
* `FlashImagesEffect.cs` — the effect: owned operation per arm, clock-driven, `Live` derived from
  the operation authority (the `StatusTickerParticipant.IsOperationLive` precedent).

### Persistence

* `Persistence/SessionPresetDocument.cs` — `session_preset.json`, a NEW document (additive by
  construction, the `AssetSelectionDocument` precedent), carrying WPF's clamps verbatim:
  `FlashEnabled` default true, `FlashesPerHour` default 10 clamp 1..180, `ImagesPerFlash` default 5
  clamp 1..20 (`CCP.Core/Models/AppSettings.cs:752,763-770,831-836`).

### UI

* `Views/MainWindow.axaml(.cs)` — a session action bar above the diagnostic footer with the ONE
  START/STOP button, WPF's toggling single control (`MainWindow.StartStop.cs:34`, caption/livery
  `:751-796`).
* `Views/Pages/StudioPage.axaml(.cs)` — a **Flash Images** rack row above Spiral Overlay (WPF's
  order, §8.3) with a live dot and a right-click quick-toggle, and a module panel with three real
  dials, the live counter, the empty-pool line and the named platform gap.

### Composition

* `Lifecycle/CompositionRoot.cs` — register `SessionParticipant`; add a `SessionClockFactory` seam
  so a headless test can drive the REAL shell on a manual clock.

`Program.cs` is **not** in this packet's file scope and is not touched; the shell reaches the
session through `host.Participants`, as it already reaches the ticker.

## 4. How stop is proved to really stop

Unit, zero wall-clock: manual clock; press start; advance twice and watch the flash count rise;
stop; advance ten more windows; assert the count is **unchanged**, the dot is not `Live`, the clock
has **no pending timer left**, the owned completion terminates `Cancelled`, and the registry's
outstanding-operation count is zero. Making `SessionEngine.Stop` a no-op makes the count keep
rising -> red (step 6, restored byte-identically, never committed).

## 5. Named risks

1. `RunAsync` bodies run on the pool, so the FIRST tick is scheduled **synchronously inside the
   arm call** (WPF does the same, `FlashService.cs:352`) and the owned operation parks until its
   generation is cancelled. Otherwise a manual-clock advance could race the pool thread.
2. `Stop` disarms the pending timer handle **itself**, synchronously, rather than relying on the
   cancellation callback — so the "advance after stop" assertion is deterministic.
3. The existing headless fact `RightClickOnTheRackRow_OpensNoMenu_AndSelectsNothing` targets the
   **Spiral Overlay** row and stays true: that row still has no toggle, which is WPF's own
   unhandled-row case.
