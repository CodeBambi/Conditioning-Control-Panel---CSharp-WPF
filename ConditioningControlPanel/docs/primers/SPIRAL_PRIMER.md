# SPIRAL OVERLAY — Feature Primer

> **Purpose.** One-load orientation for the hypnotic **Spiral overlay** so a future engineer (or
> Claude) can maintain it WITHOUT re-exploring the codebase. §0 = one-paragraph model. §1 = the
> ownership surprise (there is **no `SpiralService`** — read this first). §2 = file map. §3 = the two
> render paths + how one is chosen. §4 = where spiral images come from (the library + the Loom). §5 =
> the opacity-ownership stack (the load-bearing, bug-scarred part). **§6 = how it's invoked & how it
> touches the rest of the app (the call graph — read before wiring a new trigger).** §7 = settings.
> §8 = where-to-change-X. §9 = gotchas. §10 = dated status.
>
> **Freshness.** Tracks the code as of **2026-07-23**, branch `fix/web-video-interruptions`, HEAD
> `ded7725f` (v6.5.0-era; the sibling primers were written one commit earlier at `95586020`).
> §1–§9 track the code and rarely rot; every `file:line` was read-verified when written, but
> `OverlayService.cs` is ~2,800 lines and moves — confirm a line with a quick read before quoting.
> **§10 is a dated snapshot — verify with `git log` before acting on it.**

---

## 0. What the Spiral overlay is, in one paragraph

The Spiral is a **single fullscreen, click-through, topmost hypnotic-spiral GIF (or video)** drawn
over everything at a deliberately *very faint* opacity. It is **not its own service** — it is one of
three effects (**Pink Filter · Spiral · Brain Drain**) owned by **`OverlayService`** (`App.Overlay`,
`Services/Notifications/OverlayService.cs`), gated by `Settings.SpiralEnabled` and tuned by
`Settings.SpiralOpacity`. Its *visual* renders on one of **two interchangeable paths**: the modern
**compositor `SpiralLayer`** (default; a Skia draw-item on the shared per-monitor host, GIF/animated
only) or the legacy **per-monitor layered windows** (`CreateSpiralGifWindow` for GIFs,
`CreateSpiralVideoWindow` via `MediaElement` for video spirals). The image itself is a **single
chosen spiral** — the built-in `spiral.gif`, a file the user dropped in the Spirals folder, or a
spiral **woven by the Loom** (cross-link `Resources/web/dtrh/LOOM_PRIMER.md`) — resolved by one
function, `GetSpiralPath()`. It is triggered by ~a dozen subsystems (the dashboard card, sessions,
the AI `spiral` command, Deeper enhancement bands, chaos/DTRH bubble payloads, autonomy, remote) and
its opacity is arbitrated by a small **ownership stack** (settings-sync vs ramp-hold vs timed-bump vs
pulse) that carries most of the feature's bug history.

---

## 1. THE OWNERSHIP SURPRISE — there is no `SpiralService`

If you grepped for `SpiralService`, stop: **it does not exist.** The spiral is a `#region Spiral`
(`OverlayService.cs:1113`) inside the shared **`OverlayService`**, which multiplexes three
fullscreen effects onto the same lifecycle (`Start`/`Stop`/`RefreshOverlays`/`UpdateOverlays`), the
same 500 ms reconcile timer, the same z-order reconciler, and the same ad-hoc/sustained/timed hold
machinery. Pink Filter is the sibling covered by its own primer; Brain Drain (screen-blur) is the
third. The DTO the AI uses is even shared: **`SpiralPinkFiler`** (`Models/CommandData/SpiralPinkFiler.cs`)
backs **both** the `spiral` and `pink` commands.

Two distinct compositor classes carry the "Spiral" name — don't confuse them:
- **`Services/Compositor/SpiralLayer.cs`** — the *render* path (a `BaseLayer` Skia draw-item). Dumb:
  it holds frames + one opacity and draws. All the *logic* is in `OverlayService`.
- **`Models/CommandData/SpiralPinkFiler.cs`** — the AI command *data* (shared with Pink).

Everything below is the spiral half of `OverlayService` plus its render layer.

---

## 2. WHERE IT LIVES — file map

All paths under `.../ConditioningControlPanel/`. `file:line` verified 2026-07-23.

| File | Role |
|---|---|
| `Services/Notifications/OverlayService.cs` | **The owner.** The `#region Spiral` (`:1113`) holds `StartSpiral` (`:1115`), `StopSpiral` (`:1660`), the two legacy window builders (`CreateSpiralGifWindow` `:1512`, `CreateSpiralVideoWindow` `:1585`), the GIF decoder/cache (`DecodeGifFrames` `:1339`, `LoadSpiralGifFrames` `:1275`, `WarmSpiralCache` `:1304`), opacity (`UpdateSpiralOpacity` `:1704`, `ApplySpiralOpacityDirect` `:904`), the frame + video loop timers, and `GetSpiralPath` (`:264`). Also the shared lifecycle, the hold/ramp/bump stack (§5), and the z-order reconciler (§6c). |
| `Services/Compositor/SpiralLayer.cs` | **Render path A (default).** `BaseLayer` that decodes frozen WPF frames → persistent `SKImage[]` off-thread (`ShowFrames` `:47`), steps them on the engine tick (`Update` `:125`), draws `UniformToFill` (`Render` `:138`). `ZIndex => CompositorLayers.Spiral` (= 60). GIF/animated path only. `IsShowing` (`:39`) covers the async-decode window. |
| `Services/Compositor/CompositorLayers.cs` | Z-order constants. **`Spiral = 60`** (`:18`) — between `BrainDrain = 55` and `PinkTint = 70`. Numbers are shared with the Avalonia port; don't renumber unilaterally. |
| `Models/CommandData/SpiralPinkFiler.cs` | The AI DTO `record SpiralPinkFiler(bool On, int Intensity)` — **shared with the Pink Filter feature** (see its primer). |
| `Services/Commands/SpiralCommand.cs` | Executes an AI `spiral` command → clamps `Intensity` to `MaxIntensity = 30` (`:10/17`) → writes `SpiralOpacity`/`SpiralEnabled`, starts `App.Overlay` with `BypassLevelCheck`, `RefreshOverlays`. |
| `Services/Commands/CommandFactory.cs` | `AICommandType.spiral => new SpiralCommand((SpiralPinkFiler)…)` (`:33`). (`pink` shares the DTO at `:32`.) |
| `Features/SpiralFeatureControl.xaml(.cs)` | The **Spiral dashboard card**: enable toggle + opacity slider (two-way to `SpiralEnabled`/`SpiralOpacity`), the **Spiral Library** grid (pick the one active spiral → `SpiralPath`), and launch buttons for **THE LOOM** (`BtnOpenLoom_Click` → `LoomHostService.Launch()`, `:340`) and **Corner GIFs**. Subscribes to `DtrhLoomStore.Changed` (`:41`) so Loom saves appear live. No spiral *render* logic here. |
| `Services/CornerGifService.cs` | Consumer, not owner: a corner-GIF slot left on "built-in" **follows the same pool selection** (`App.Settings.SpiralPath`) as the overlay (`:75-86`), so the card re-renders corner GIFs when `SpiralPath` changes (`SpiralFeatureControl.xaml.cs:84`). |
| `Services/Chaos/DtrhLoomStore.cs` / `LoomHostService.cs` | The Loom's file authority + window host. The Loom writes `loom_<slug>.gif` into the **Spirals folder** the library enumerates. See §4 + `LOOM_PRIMER.md`. |
| `App.xaml.cs` | Constructs `Overlay = new OverlayService()` (`:1402`); owns `CompositorEnabled` (`:307`, the predicate that selects render path A); creates the Spirals folder (`:1284`) and migrates a legacy one (`:3077`). |
| `Models/AppSettings.cs` | `#region Spiral Overlay (Unlocks Lv.10)` (`:2160`) — see §7. |

---

## 3. THE TWO RENDER PATHS + how one is chosen

The path is decided **per show** at the top of `StartSpiral` (`OverlayService.cs:1115`):

```
_isGifSpiral = _spiralPath.EndsWith(".gif");
if (UseCompositor && _isGifSpiral && _spiralPath != _spiralLayerFailedPath)  → Path A (SpiralLayer)
else                                                                          → Path B (legacy windows)
```

`UseCompositor => App.CompositorEnabled` (`:117` → `App.xaml.cs:307`, on when `UnifiedOverlayHost`
is true, the default). **Video spirals always take Path B** — `SpiralLayer` has no video frame
source yet (see the class comment, `SpiralLayer.cs:11`). A Path-A decode that yields **zero frames**
latches `_spiralLayerFailedPath` so the next `StartSpiral` routes that asset to Path B instead of
retrying forever (`:1155`).

| | Path A — Compositor `SpiralLayer` (default) | Path B — Legacy per-monitor windows |
|---|---|---|
| Where | one Skia draw-item on the shared per-monitor host | one `WS_EX_LAYERED` topmost transparent window **per screen** (`:1540`/`:1618`) |
| Assets | **GIF / animated only** | GIF (`CreateSpiralGifWindow`) **or** video via `MediaElement` (`CreateSpiralVideoWindow`) |
| Frames | frozen `BitmapSource[]` → `SKImage[]` converted **off-thread** (`ShowFrames`, `SpiralLayer.cs:59`) | `Image.Source` swapped each `_gifFrameTimer` tick (`GifFrameTimer_Tick` `:1467`); video loops via `_gifLoopTimer` (`VideoLoopTimer_Tick` `:1486`) |
| Stepping | engine tick (`Update`, `SpiralLayer.cs:125`), dirty-gated (`#550`) | `DispatcherTimer` at `Render` priority, `_gifFrameDelay` |
| Fill | `UniformToFill`, center-crop (`Render`, `SpiralLayer.cs:138`) | `Stretch.UniformToFill` on the `Image`/`MediaElement` |
| Multi-monitor | `ShouldRenderOnScreen => PrimaryOnlyUnlessDualMonitor` (`SpiralLayer.cs:33`) | one window per screen, honoring `DualMonitorEnabled` (`:1181`) |
| Z-order | `CompositorLayers.Spiral = 60`; host reconciled by `ReassertZOrder` (§6c) | window reconciled by `ReassertZOrder` (§6c) |

**`SpiralShowing`** (`:149`) is the truth for "is a spiral up on *either* path" and must cover the
async-decode gap: `_spiralWindows.Count > 0 || _spiralLayer?.IsShowing == true || _spiralLayerDecodePending != null`.
`StopSpiral` (`:1660`) clears **both** paths unconditionally (`_spiralLayer?.Hide()` + close windows)
because the compositor flag can flip mid-run.

---

## 4. WHERE THE SPIRAL IMAGE COMES FROM

One resolver decides the single active asset — **`GetSpiralPath()`** (`OverlayService.cs:264`):

1. `Settings.SpiralPath` **if set and the file exists** → that file (a library pick, a Loom spiral,
   or a browse-anywhere GIF/video).
2. otherwise → `ModResourceResolver.ResolveUri("spiral.gif")` — the **built-in** spiral (a
   `pack://` resource, or a `file://` mod override). `DecodeGifFrames` unwraps both (`:1350/1356`).

So the overlay uses **one** spiral at a time, not a random pool. Where the choice is made:

- **The Spiral Library card** (`SpiralFeatureControl`) shows a "Default" card (empty `SpiralPath`)
  plus one card per file in the **Spirals folder** (`Path.Combine(App.UserDataPath, "Spirals")`,
  `SpiralFeatureControl.xaml.cs:139`). Clicking a card sets `SpiralPath` (`SelectSpiral`, `:274`);
  clicking a missing file is a no-op. Extensions: GIF/PNG/JPG/WEBP/BMP images + MP4/WEBM/MOV/AVI/MKV
  video (`:130-133`).
- **THE LOOM** writes Loom-authored `loom_<slug>.gif` into that same Spirals folder
  (`DtrhLoomStore`, 12-file cap, slug whitelist — see `LOOM_PRIMER.md` §7c). `DtrhLoomStore.Changed`
  fires on every save/delete → the card's `RefreshLibrary` runs live (`:370`). This is the primary
  producer of custom spirals.
- The Spirals folder is created at startup (`App.xaml.cs:1284`) and a legacy location is migrated
  into it (`:3077`).

> **Note the "random spiral pool" nuance.** The DTRH web game has its *own* picker
> (`engine/loomSpirals.js`) that mixes saved spirals ~50/50 with a bundled pool for the in-game tube
> overlays — that is a separate consumer inside DTRH, not the CCP desktop overlay. The desktop Spiral
> overlay is single-asset via `GetSpiralPath`. `CornerGifService` is the one other desktop consumer
> that *follows* `SpiralPath` (§2).

---

## 5. THE OPACITY-OWNERSHIP STACK (the load-bearing, bug-scarred part)

The spiral is drawn **very faint**: final alpha = `(SpiralOpacity / 100) × 0.1` — a hard **90 %
reduction** applied in **five places** that must stay in lockstep: `CreateSpiralGifWindow` (`:1521`),
`UpdateSpiralOpacity` (`:1708`), `ApplySpiralOpacityDirect` (`:906`), the two Path-A opacity pushes
in `StartSpiral` (`:1127/1168`), and `PulseOverlays` (`:411`). Because `SpiralOpacity` is clamped
**0–50** (`AppSettings.cs:2180`; slider 5–50), the real on-screen alpha maxes at **~0.05**.

At any instant, exactly one *owner* sets that opacity. In priority order:

1. **Settings-sync (default owner)** — the 500 ms `UpdateOverlays` (`:510`) / `UpdateSpiralOpacity`
   (`:1704`) reads `SpiralOpacity`. It **early-returns while a ramp hold is set** (`:1706`) so it
   can't stomp a live ramp.
2. **Ramp hold `_rampSpiralOpacity`** — set by `SetSustainedOverlayOpacity("spiral", …)` (`:839`,
   Deeper bands / session ramp) and by `ShowOverlaySustained` (`:782`). Normalized 0..1; the ×0.1 is
   still applied on top. Cleared on band exit (`HideOverlaySustained` `:812`) or via
   `ReleaseOpacityRampHolds` (`:882`) — **if you don't clear it the spiral freezes at the ramp's
   final opacity forever** (#563).
3. **Timed bump `_bumpSpiralActive`** (#573) — a spiral-payload bubble pop *while a spiral is already
   up* would otherwise be swallowed by the ad-hoc early-return. Instead it bumps the live opacity
   (never downward) and parks the previous owner, restored when the last `_timedSpiralHolds` releases
   (`:713-723`).
4. **Pulse** — `PulseOverlays` (`:375`) doubles opacity for 1 s then restores to the **ramp value if
   one is active, else settings** (`:451`) — routing the restore through `UpdateSpiralOpacity` left it
   stuck fully boosted (#535).

Two independent **holds** stop the reconcilers (`RefreshOverlays`/`UpdateOverlays`) from tearing an
ad-hoc spiral down just because `SpiralEnabled` is off:
- `_timedSpiralHolds` (counter) — `ShowOverlayTimed` (`:655`), released by its hide timer (`:710`).
- `_sustainedSpiralHeld` (bool) — `ShowOverlaySustained` (`:782`), released by `HideOverlaySustained`.

`StopSpiral` clears `_rampSpiralOpacity`, `_sustainedSpiralHeld`, and `_lastAppliedSpiralOpacity`
(`:1697-1699`) so a force-stop/panic can't strand a stale hold.

---

## 6. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

Read this before wiring a new trigger. Public entry points are all on **`App.Overlay`**:
`Start` / `Stop` / `RefreshOverlays` / `PulseOverlays` / `ShowOverlayTimed` / `ShowOverlaySustained`
/ `HideOverlaySustained` / `SetSustainedOverlayOpacity` / `WarmSpiralCache`. There is **no
`SpiralService` and no `App.Spiral`** — the whole trigger surface goes through `OverlayService`.

### 6a. Who triggers the spiral (the trigger map)

| Caller | `file:line` | What it does |
|---|---|---|
| **Spiral dashboard card** | `SpiralFeatureControl.xaml.cs:94` (enable → `SpiralEnabled` + `RefreshOverlays`), `:115` (opacity slider → `SpiralOpacity` + `RefreshOverlays`), `:284` (library pick → `SpiralPath` + `RefreshOverlays`), `:396` (browse GIF). | The primary user path: master toggle, opacity, and asset selection. |
| **Progression tab checkbox** | `MainWindow.UiUpdates.cs:1032` (`ChkSpiralEnabled_Changed`) → `SpiralEnabled` + `App.Overlay.RefreshOverlays()`. | Duplicate enable toggle on the Progression tab. |
| **Session engine start/stop** | `SessionEngine.cs:455/1162` (`App.Overlay.Start()`), `:416` (`Stop`), `:1034` (immediate `SpiralEnabled=true`), `:654-658` (delayed start → `_mainWindow.EnableSpiral(true)`), `:829/1207` (save/restore user settings). | A session copies `SpiralOpacity`/`SpiralStartMinute`/`SpiralOpacityEnd`, optionally delays the spiral N minutes, then ramps it. |
| **Session opacity ramp** | `SessionEngine.cs:569` → `SetSustainedOverlayOpacity("spiral", _currentSpiralOpacity/100)` each tick after the randomized start minute (`:562-568`); `:1254` `ReleaseOpacityRampHolds` on end. | Interpolates `SpiralOpacity → SpiralOpacityEnd` over the session (owner #2 above). |
| **AI `spiral` command** | `PromptService`/AI → `CommandFactory.cs:33` → `SpiralCommand.cs:15` → sets `SpiralOpacity`/`SpiralEnabled`, `Start()` with `BypassLevelCheck = true`, `RefreshOverlays`. | The AI companion can raise/lower the spiral. `Intensity` clamped 0–30. |
| **Deeper enhancement bands** | `Services/Deeper/IActionDispatcher.cs:395-396` (`BeginDeeperOverlayBand` + `ShowOverlaySustained("spiral")`), `:412-413` (`HideOverlaySustained` + `EndDeeperOverlayBand`), `:423` (`SetSustainedOverlayOpacity`), `:428` (`ShowOverlayTimed`). | A Deeper `.ccpenh` region turns the spiral into the enhanced video's own effect — which forces the **above-video** z-order (§6c). |
| **Chaos / DTRH-WPF bubble payloads** | `Services/Chaos/EffectPayload.cs:154` → `ShowOverlayTimed("spiral", duration, opacity)`; `ChaosModeService.cs:405` → `WarmSpiralCache()` (pre-decode off-thread so the first pop doesn't hitch). | A spiral-payload bubble flashes the spiral for a beat. |
| **Autonomy / voice** | `AutonomyService.VoiceCommands.cs:311/324` (`ShowOverlaySustained`/`HideOverlaySustained("spiral")`); `AutonomyService.cs:1572-1577` starts the overlay engine. Gated by `AutonomyCanTriggerSpiral` (`AppSettings.cs:3663`). | "Show me the spiral" spoken command. |
| **Remote control** | `RemoteControlService.cs:955-963` (`Start` with `BypassLevelCheck`), `:836/936/1016+` (`RefreshOverlays`); `MainWindow.RemoteControl.cs:1314/1421` (`StopSpiral`). | Partner/companion-app remote. |
| **Intensity Ramp (non-session)** | `MainWindow.StartStop.cs:472` drives `SpiralOpacity` from the ramp when `RampLinkSpiralOpacity` (`AppSettings.cs:2124`). | Standalone ramp feature links the spiral's opacity. |
| **Keyword trigger / gaze minigame** | `KeywordTriggerService.cs:1545`, `Lab/GazeMinigame/GazeMinigameWindow.xaml.cs:1254` → `PulseOverlays()`. | A spoken keyword / minigame beat pulses all live overlays (incl. spiral). |
| **Presets** | `MainWindow.Presets.cs:808` toggles `SpiralEnabled` + `RefreshOverlays`. | Applying a preset. |
| **Engine start/stop** | `MainWindow.StartStop.cs:175` (`Start`), `:316` (`Stop`); `MainWindow.xaml.cs:411` (`Stop`), `:300/WindowChrome:281` (`Dispose`). | The dashboard Start/Stop button owns the overlay engine lifecycle. |

### 6b. The lifecycle + reconcilers

`Start()` (`:275`) shows the spiral immediately if `SpiralEnabled && GetSpiralPath()` non-empty, then
arms a 500 ms `_updateTimer → UpdateOverlays` (`:510`). `RefreshOverlays()` (`:330`) is the on-demand
reconcile (start if enabled+not showing, else update opacity, else stop unless a hold keeps it).
`UpdateOverlays` is the periodic twin plus `ReassertZOrder`. `Stop()` (`:309`) tears everything down.
Ad-hoc entry is `ShowSpiralAdHoc` (`:941`) → `StartSpiral` (no-op if no asset configured).

### 6c. Z-order — the #497 dance (what the spiral touches on screen)

`ReassertZOrder` (`:2247`) pins the spiral (and pink/brain-drain) **below a playing mandatory video**
by default (`ResolveZOrderAction`, `:2309`) — otherwise the reconciler and `NotifyTopWindowClosed`
would bury the deliberately non-re-raising video behind the spiral and the next clip would show "only
by flashes" (#497). The **one exception** is a live Deeper overlay band (`DeeperOverlayBandActive`,
`:46`): then the spiral **is** the video's effect and is pinned **above** it. This runs for **both**
render paths — legacy windows (`:2269`) and compositor host handles (`:2286`). `SpiralLayer`'s
`ZIndex = 60` puts it above Brain Drain (55) and below the Pink Tint (70) within the compositor.

### 6d. What listens to it

Almost nothing — the spiral awards **no XP**, fires no events, and has no achievement (unlike Flash
and Bubble Pop). Its only outbound coupling is `CornerGifService` following `SpiralPath` (§2) and
`SeasonRecapService.TrackFeature(Overlay)` when a session had spiral/pink on (`SessionEngine.cs:232`).

---

## 7. SETTINGS (`Models/AppSettings.cs`, `#region Spiral Overlay` `:2160`)

| Setting | `:line` | Default | Effect |
|---|---|---|---|
| `SpiralEnabled` | 2163 | **true** | Master gate for the persistent overlay. |
| `SpiralPath` | 2170 | `""` | The single active spiral; `""` → built-in `spiral.gif`. Set by the library / Loom / browse. |
| `SpiralOpacity` | 2177 | 10 (clamped **0–50**) | User opacity %; real alpha = `/100 × 0.1` (§5). |
| `SpiralLinkRamp` | 2184 | false | Link spiral opacity to the **session** ramp. |
| `RampLinkSpiralOpacity` | 2124 | false | Link spiral opacity to the standalone **Intensity Ramp** (`MainWindow.StartStop.cs:472`). |
| `AutonomyCanTriggerSpiral` | 3663 | true | Lets Autonomy Mode show/hide the spiral. |
| `SpiralStartMinute` / `SpiralOpacityEnd` | `Session.cs:870/873` | 0 / 15 | **Session-model** fields: delayed start + ramp target (not live app settings). |
| `UnifiedOverlayHost` | (Pink/Flash primer) | **true** | Behind `App.CompositorEnabled` → selects render **path A**. |
| `DualMonitorEnabled` | (shared) | | Spread the spiral across screens vs primary only (both paths honor it). |

**Level gate.** The region is labelled *"Unlocks Lv.10"* and the dashboard card is level-locked in
the UI, but — unlike Brain Drain (which checks `IsLevelUnlocked(70)` in `Start`, `:296`) — there is
**no hard level check for the spiral inside `OverlayService`**. AI/remote paths additionally set
`BypassLevelCheck` before `Start()`. Treat the level gate as UI-side only.

---

## 8. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Add a new trigger | Call `App.Overlay.ShowOverlayTimed("spiral", ms, opacity)` (auto-dismiss), `ShowOverlaySustained`/`HideOverlaySustained("spiral")` (band), or set `SpiralEnabled`/`SpiralPath` + `RefreshOverlays()`. Do **not** invent an `App.Spiral`. |
| Change the render-path choice | The `UseCompositor && _isGifSpiral && …` decision in `StartSpiral` (`:1125`). |
| Add/replace a render path | `SpiralLayer.cs` (path A) or `CreateSpiral*Window` (path B) + a branch in `StartSpiral`/`StopSpiral` + an opacity applier (§5's five sites) + `SpiralShowing` (`:149`). |
| Change the opacity math / the ×0.1 | **All five** sites in §5 in lockstep. The clamp is `SpiralOpacity` (`AppSettings.cs:2180`). |
| Change decode / frame budget | `DecodeGifFrames` (`:1339`); caps at `:1398-1407` (1280 px long side, 120 frames, 300 MB — #572). Pre-warm via `WarmSpiralCache` (`:1304`). |
| Change asset resolution / the pool | `GetSpiralPath` (`:264`) + the library UI (`SpiralFeatureControl`). Loom output lands via `DtrhLoomStore` (`LOOM_PRIMER.md`). |
| Change z-order vs video | `ResolveZOrderAction` (`:2309`) / `ReassertOne` (`:2318`); layer index `CompositorLayers.Spiral` (`:18`). |
| Change the AI clamp | `SpiralCommand.MaxIntensity` (`:10`). New DTO field: `SpiralPinkFiler` (shared with Pink!) + the prompt + `CommandFactory`. |
| Add a spiral setting | `AppSettings.cs` region (`:2160`) + a control in `SpiralFeatureControl.xaml(.cs)`; read it in `StartSpiral`/`UpdateSpiralOpacity`. |
| Change the ramp/hold behavior | The ownership stack in §5: `SetSustainedOverlayOpacity` (`:839`), `ShowOverlaySustained`/`HideOverlaySustained` (`:744/793`), the `_bump*` block (`:655/713`), `PulseOverlays` (`:375`). |

---

## 9. GOTCHAS (the expensive ones — most are load-bearing comments in the source)

1. **The ×0.1 opacity reduction lives in five places.** Change one and the paths diverge. With
   `SpiralOpacity` clamped 0–50, real alpha tops out near **0.05** — the spiral is *meant* to be a
   whisper. Don't "fix" a faint spiral by editing only one site.
2. **Two render paths, one truth.** `SpiralShowing` must cover the legacy windows **and** the layer
   **and** the in-flight off-thread decode (`_spiralLayerDecodePending`) or the 500 ms sync re-enters
   and double-shows (`:149`). `StopSpiral` clears both paths unconditionally because the compositor
   flag can flip mid-run (`:1662`).
3. **Legacy decode freezes the UI ~1 s; the layer path decodes off-thread.** Path A converts frames
   in `Task.Run` (`SpiralLayer.cs:59`) and counts the pending decode as "showing". Path B's
   `LoadSpiralGifFrames` decodes **on the UI thread** — hence the frozen-frame **cache** keyed by
   path (`:1282`) and `WarmSpiralCache` (`:1304`) pre-warming before chaos re-shows the spiral on
   every detonation (`ChaosModeService.cs:405`).
4. **Frame cache is budgeted (#572).** A fullscreen custom spiral at native size × 120 Bgra32 frames
   retained ~1 GB. `DecodeGifFrames` caps the long side to 1280 px, 120 frames, and 300 MB
   (`:1398-1407`). A too-big spiral is silently downscaled/decimated.
5. **Spiral decode still uses GDI+ (`System.Drawing`), not SKCodec.** `DecodeGifFrames` /
   `ConvertToBitmapSource` (`:1339/1440`) go through `System.Drawing.Image` — unlike Flash, which
   migrated off GDI+ (#486). Frozen `BitmapSource`s are shared between the legacy cache and the layer
   (the layer converts them to `SKImage` off-thread). Keep them frozen.
6. **Video spirals are Path B only.** `MediaElement` windows, no compositor layer, no
   capture-exclusion, and `MediaEnded` is unreliable so a `_gifLoopTimer` re-seeks to 0 manually
   (`VideoLoopTimer_Tick`, `:1486`). Don't assume a video spiral rides the compositor.
7. **A zero-frame layer decode routes to legacy permanently.** `_spiralLayerFailedPath` (`:1155`)
   stops the retry churn — clear it if you change what makes a decode fail.
8. **Ramp holds must be released or the spiral freezes (#563).** `_rampSpiralOpacity` makes the
   settings-sync early-return; a Deeper band that exits without `HideOverlaySustained` /
   `ReleaseOpacityRampHolds` leaves the spiral pinned at the band's final opacity. See §5.
9. **Pulse restores to the ramp value, not settings (#535).** Routing the 1 s pulse-restore through
   `UpdateSpiralOpacity` (which early-returns under a ramp) left the spiral stuck fully boosted.
10. **The spiral must never bury a mandatory video (#497).** `ReassertZOrder` pins it below a playing
    video except during a Deeper band. Any new topmost-raise you add near the overlay must reconcile
    through `ReassertOne`, not `HWND_TOPMOST` directly.
11. **`SpiralPinkFiler` is shared with Pink Filter.** Adding a field for the spiral changes the pink
    command's DTO too. Coordinate with the Pink primer.
12. **Fire-and-forget guards.** The off-thread decode continuations and `PulseOverlays`' restore use
    null-dispatcher / `HasShutdownStarted` checks (`SpiralLayer.cs:72`, `OverlayService.cs:438/1148`)
    — the standing CCP async rule (root `CLAUDE.md` §6). Keep them.
13. **Stale path comment.** `SpiralFeatureControl.xaml.cs:138` says the folder is under
    `%LOCALAPPDATA%`, but it uses `App.UserDataPath`, which is `%APPDATA%/ConditioningControlPanel`
    (root `CLAUDE.md`). Trust the constant, not the comment.

---

## 10. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

> This section rots. Confirm with `git log --oneline -- Services/Notifications/OverlayService.cs
> Services/Compositor/SpiralLayer.cs` and `git branch` before acting.

- **State: mature and shipping.** No dedicated in-flight spiral branch. HEAD `ded7725f` on
  `fix/web-video-interruptions` (v6.5.0-era). Spiral work rides the general compositor + triage
  batches.
- **Recent spiral-touching commits.** `SpiralLayer` line: `2d75a5bd` (pink+spiral as compositor
  layers, flag/arg gated) → `4d4d9eb2` (layer decodes `pack://` default) → `05b66039` (layer sources
  frames from the legacy loader+cache) → `0b0bfbc9` (#550 stop the host lagging the UI thread) →
  `f98f6132` (honor `DualMonitorEnabled` for fill layers). `OverlayService` spiral: `a32cec92`
  (decode off the UI thread on the layer route), `a192438a` (#563 Deeper bands no longer
  freeze/stomp/nuke base overlays), `83f18eb1` (compositor pre-merge review), `49bba471` (post-6.4.1
  batch incl. Deeper z-order). Card: `3ed065b0` (library + folder picker), `4e6903c4`/`fe4acb58`
  (corner GIFs follow the pool).
- **Compositor path is the default** (`UnifiedOverlayHost = true`). Per the auto-memory the
  compositor cluster is "default ON PUSHED, play-test pending" (`compositor-premerge-review-batch-0716`,
  `compositor-default-on-regression-cluster`, `bug-550-compositor-spiral-uithread-raster`) — Path A
  is the hot path but keep Path B working (it's the only video-spiral path and the fallback for
  zero-frame decodes).
- **Related memory.** `pink-filter-stuck-opaque-pulse-ramp` (#535, shared pulse/ramp code),
  `deeper-overlay-ramp-frozen-563` (#563), `bug-497-overlay-buries-video-plus-496-quest-wedge`
  (#497), `standalone-corner-gif-overlays` (CornerGifService follows the pool),
  `loom-primer-doc`/`web-loom-public-page` (the Loom that authors spirals).
- **Known limits.** Video spirals never use the compositor (no layer video source). The layer path
  is GIF/animated only. Spiral awards no XP/achievement. The "Lv.10 unlock" is UI-side only (no hard
  gate in the service).
- **No dedicated unit test** for the spiral itself; the extracted testable seam near it is
  `ResolveZOrderAction` (`:2309`, #497). Play-test remains the standing gate for overlay changes
  (freeze clusters historically hid in the overlay window churn).

---

## 11. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```

Then: open the **Spiral** card, enable it, and press the dashboard Start button (or start a session).
Drop a `.gif` into `%APPDATA%/ConditioningControlPanel/Spirals` (or weave one in **THE LOOM**), hit
Refresh, and pick it in the library. To force Path B, turn `UnifiedOverlayHost` off (or select a
video spiral, which is always Path B). Watch `logs/crash.log`; spiral decode/warm/show all log at
`Debug` under the `Spiral:` prefix.
