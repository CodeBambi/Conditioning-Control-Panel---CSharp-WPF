# PINK FILTER — Feature Primer

> **Purpose.** One-load orientation for the Pink Filter — the full-screen pink tint overlay — so a
> future engineer (or Claude) can maintain it WITHOUT re-exploring the codebase. §0 = what it is (and
> what it is NOT). §1 = the file map. §2 = the two render paths + how one is chosen + the opacity math.
> **§3 is the load-bearing section — every trigger and every system it touches** (read it before wiring
> anything new). §4 = the opacity-ownership model (the ramps/holds/bump machinery that half the bug
> history lives in). §5 = settings. §6 = where-to-change-X. §7 = gotchas. §8 = dated status.
>
> **Freshness.** Tracks the code as of **2026-07-23** on branch `fix/web-video-interruptions`
> (HEAD `ded7725f`, v6.5.0-era). §1–§7 track the code and rarely rot; **§8 is a dated snapshot — verify
> with `git log`/`git blame` before acting.** Every `file:line` below was read-verified when written,
> but `OverlayService.cs` is ~2,700 lines and churns — confirm a line with a quick read before quoting.

---

## 0. What Pink Filter is, in one paragraph

Pink Filter lays a **solid, click-through, full-screen pink wash** over every (enabled) monitor — a
constant hot-pink (`#FF69B4`, `255,105,180`) tint at a user-set opacity. It is the simplest of the
three overlays and has **no dedicated service**: it is owned by **`OverlayService`** (`App.Overlay`,
constructed at `App.xaml.cs:1402`), the same service that owns **Spiral** and **Brain Drain**. It
renders on one of **two interchangeable paths** — the modern **compositor `PinkTintLayer`** (a Skia
solid-fill draw-item on the shared per-monitor host, default when the Unified Overlay Host is on) or
the legacy **per-screen layered `Border` windows** (a `WS_EX_LAYERED` topmost window per monitor).
OverlayService keeps ALL the opacity math (settings-sync, Deeper ramps, timed/sustained holds, pulses)
and pushes final values to whichever path is live. It is driven by ~a dozen subsystems (dashboard
card, AI, sessions, chaos/DTRH bubbles, autonomy, voice, Deeper enhancements, remote). **Do NOT
confuse it with "Pink Rush"** — a `SkillTreeService` 3× XP bonus window (§0.1) that shares the word
"pink" but is unrelated to the visual tint.

### 0.1 Disambiguation — three "pink" things

| Thing | Code | What it is |
|---|---|---|
| **Pink Filter** (this doc) | `OverlayService` pink methods + `PinkTintLayer` + `PinkFilterFeatureControl` | The full-screen pink tint overlay. |
| **Pink Rush** | `Services/Progression/SkillTreeService.cs:330` (`#region Pink Rush`) | A skill-tree bonus: a random 60s window of **3× XP** (`StartPinkRush`, `:350`). Fires `PinkRushStarted`/`PinkRushEnded` (barked by `BarkService.cs:573`). **Not a visual effect.** |
| **The filter color** | `App.Mods.GetFilterColorRgb()` (`OverlayService.cs:590`) | The pink is mod-retintable — a creator mod can change `(255,105,180)` to its own brand color. Both render paths read it via `GetFilterRgb()`. |

---

## 1. Where it lives — file map

| File | Role |
|---|---|
| `Services/Notifications/OverlayService.cs` (~2,700 lines) | **The owner.** All pink lifecycle + opacity math lives in the `#region Pink Filter` (`:586`) plus the shared reconcilers (`Start`/`Stop`/`RefreshOverlays`/`UpdateOverlays`/`PulseOverlays`) and the z-order machinery (`ReassertZOrder`, `:2247`). Pink shares this file with Spiral + Brain Drain. |
| `Services/Compositor/PinkTintLayer.cs` (54 lines) | **Render path A.** A `BaseLayer` that draws one fullscreen `SKColor` rect; opacity rides in the alpha channel. `Show`/`Set`/`Hide` + a `_dirty` flag (#550: a steady tint is static — only repaint on change). OverlayService owns all opacity math; the layer just draws. `ZIndex => CompositorLayers.PinkTint` (= **70**, top of the session-effect band). |
| `Services/Compositor/CompositorLayers.cs:19` | `PinkTint = 70` — the authoritative z-index (above Spiral 60, BrainDrain 55; below the chaos band 100+). Kept in sync with the Avalonia port — don't renumber unilaterally. |
| `Features/PinkFilterFeatureControl.xaml` / `.xaml.cs` | The **dashboard settings card**: an enable toggle + an opacity slider (5–50%). Two-way binds to `App.Settings.Current.PinkFilter*`, saves, and calls `App.Overlay?.RefreshOverlays()`. No pink *logic* lives here. |
| `Models/CommandData/SpiralPinkFiler.cs` | The AI-command DTO — `record SpiralPinkFiler(bool On, int Intensity)`. **SHARED with the Spiral feature** (both `pink` and `spiral` AI commands deserialize to it — see `CommandFactory.cs:32-33`). See the Spiral primer for the other consumer. (The filename typo "Filer" is load-bearing — don't "fix" it without updating references.) |
| `Services/Commands/PinkCommand.cs` | Executes the AI `pink` command: clamps `Intensity` to **0–30** (`MaxIntensity`), writes `PinkFilterOpacity`/`PinkFilterEnabled`, starts the overlay service with `BypassLevelCheck`, `RefreshOverlays`, saves. |
| `Models/AppSettings.cs:2839` (`#region` pink) | `PinkFilterEnabled`, `PinkFilterOpacity` (clamped 0–50, default 10), `PinkFilterLinkRamp`. Plus `AutonomyCanTriggerPinkFilter` (`:3673`), `PinkRushActive`/`PinkRushEndTime` (`:571`/`:582`, the *skill*, not the filter), session `PinkFilterStart/EndOpacity`/`StartMinute` (on `Models/Session.cs`). See §5. |
| `App.xaml.cs:1402` | `Overlay = new OverlayService();` — construction. `App.CompositorEnabled` (`:307`) is the predicate that selects render path A. |

---

## 2. The two render paths + how one is chosen

The path is decided **at each show** by the static predicate `UseCompositor => App.CompositorEnabled`
(`OverlayService.cs:117`; `App.CompositorEnabled` = `(CompositorForced || UnifiedOverlayHost) &&
Compositor != null`, `App.xaml.cs:307`). Every show method (`StartPinkFilter` `:964`,
`ShowPinkFilterAdHoc` `:913`) branches on it up front.

| | Path A — Compositor `PinkTintLayer` (default) | Path B — Legacy per-screen windows |
|---|---|---|
| Type | `Compositor.PinkTintLayer` on the shared Skia host | one `Border` in a `WS_EX_LAYERED` topmost `Window` **per screen** (`_pinkFilterWindows`) |
| Created | lazily via `GetPinkLayer()` (`:118`, registers with `App.Compositor`) | `CreatePinkFilterForScreen` (`:1004`) per `App.GetAllScreensCached()` (or primary only) |
| Draws | `canvas.DrawRect` with `SKColor(r,g,b, opacity*255)` (`PinkTintLayer.cs:48`) | `SolidColorBrush(Color.FromArgb(opacity*255, r,g,b))` on a `Border` |
| Multi-monitor | one host per screen; `ShouldRenderOnScreen => PrimaryOnlyUnlessDualMonitor` (honors `DualMonitorEnabled`) | one window per screen; `DualMonitorEnabled ? all : primary` |
| Repaint | **dirty-gated** — only repaints when color/opacity changes (#550) | live — mutating `brush.Color` re-renders |
| Click-through | host is click-through | `WS_EX_TOOLWINDOW\|NOACTIVATE\|TRANSPARENT\|LAYERED` (`:1049`) |
| Positioning | host owns it | `SetWindowPos` in **physical px** on `SourceInitialized` (`PositionWindowOnScreen`) — bypasses WPF DPI virtualization on mixed-DPI (#457); `HookBoundsRestore` re-pins on move |

`PinkShowing` (`:147`) = `_pinkFilterWindows.Count > 0 || _pinkLayer?.IsActive == true` — it covers
**both** paths, because a mid-run compositor-flag flip must never strand a visible layer. That's why
`StopPinkFilter` (`:1069`) unconditionally `_pinkLayer?.Hide()` **and** closes every legacy window.

**The opacity math (both paths, identical intent):** opacity is a percentage 0–100 in the code, stored
0–50 in settings. The alpha byte is a **linear** `opacity/100 × 255` — there is deliberately no
exponential curve (comment at `:1011`). The compositor layer clamps to `0..1` internally
(`PinkTintLayer.Set` `:38`); `Render` early-returns at `_opacity <= 0`.

---

## 3. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

Read this before touching anything. Pink Filter has a **wide, shallow** fan-in: many callers, but they
almost all funnel through the same handful of `OverlayService` methods. The service must be
**running** (`_isRunning`, set by `Start()` `:275`) for the reconcilers to act — `RefreshOverlays`,
`PulseOverlays`, and `UpdateOverlays` all early-return if not (`:332`, `:377`, and the 500ms timer
only exists while running). **There is no level gate on pink** (only Brain Drain checks
`IsLevelUnlocked(70)`); `BypassLevelCheck` exists on the service but pink's start path doesn't consult
a level.

### 3a. The OverlayService public surface pink rides on

| Method | `:line` | Purpose |
|---|---|---|
| `Start()` / `Stop()` | 275 / 309 | Arm/stop the whole overlay engine (spiral+pink+braindrain) + the 500ms reconcile timer. `Start` shows pink if `PinkFilterEnabled`. |
| `RefreshOverlays()` | 330 | Reconcile now: start pink if enabled & not showing; else sync opacity; else stop it (unless a timed/sustained hold keeps it up). **The main "settings changed" entry.** |
| `UpdateOverlays` (500ms timer) | 510 | The periodic reconciler — same logic as Refresh, plus `UpdatePinkFilterOpacity` drift-sync. |
| `PulseOverlays()` | 375 | Briefly **doubles** intensity (up to 100%) for ~1s then restores (see #535 in §7). |
| `ShowOverlayTimed(kind, ms, opacity)` | 601 | Ad-hoc timed show (auto-hides). `kind == "pink_filter"`. Deeper timed effects + chaos bubbles. |
| `ShowOverlaySustained(kind, opacity)` / `HideOverlaySustained(kind)` | 744 / 793 | Band-mode show with no hide timer (voice, Deeper region bands). |
| `SetSustainedOverlayOpacity(kind, opacity)` | 839 | Live opacity ramp for a sustained overlay (Deeper/session ramps). |
| `ReleaseOpacityRampHolds()` | 882 | Drop ramp ownership without tearing down (session end). |
| `Begin/EndDeeperOverlayBand()` | 51 / 59 | Z-order: flips overlays ABOVE a playing video while a Deeper band is live (§3c). |
| `RefreshForDualMonitorChange()` | 474 | Stop+restart pink to match a new monitor topology. |
| `NotifyTopWindowClosed()` | 2347 | Called by Flash/Video/MainWindow after closing a topmost window → `ReassertZOrder`. |
| Ad-hoc/internal | 913 / 964 / 1069 / 1087 / 890 | `ShowPinkFilterAdHoc`, `StartPinkFilter`, `StopPinkFilter`, `UpdatePinkFilterOpacity`, `ApplyPinkOpacityDirect`. |

### 3b. Who triggers it (the trigger map)

| Caller | `file:line` | What it does |
|---|---|---|
| **Dashboard card** | `Features/PinkFilterFeatureControl.xaml.cs:60/73` | Toggle → `PinkFilterEnabled`; slider (5–50) → `PinkFilterOpacity`; both `Save()` + `RefreshOverlays()`. Reflects live setting changes back via `PropertyChanged` (`:46`). |
| **Progression tab checkbox** | `MainWindow.UiUpdates.cs:1049` (`ChkPinkFilterEnabled_Changed`), `MainWindow.Presets.cs:1487` (`EnablePinkFilter`) | A second UI surface for the same setting. |
| **AI command (`pink`)** | `PromptService`→`CommandFactory.cs:32` → `PinkCommand` → writes settings + `Start` + `RefreshOverlays`. Gated on `AllowAiOverlay` (`AiCommandService.cs:194`); clamped **0–30** (`AiCommandService.cs:163`, `PinkCommand.MaxIntensity`). DTO = `SpiralPinkFiler`. | The companion can turn the tint on/off + set intensity. Sets `BypassLevelCheck` before `Start`. |
| **SessionEngine** (AI sessions) | apply session settings `:1022`; delayed start `:624` (`EnablePinkFilter(true)` at `_randomizedPinkStartMinute`); **ramp** `:559` (`SetSustainedOverlayOpacity("pink_filter", …)` lerping `PinkFilterStartOpacity`→`EndOpacity`); restore on end `:1204`; `ReleaseOpacityRampHolds()` `:1254`. Start-minute randomized ±3 (`:715`). | Sessions ramp the tint up over time. The ramp writes to the overlay **directly**, NOT to `Settings.PinkFilterOpacity` (that caused the "keeps getting more pink and stays that way" bug #471/#476). |
| **Chaos / DTRH bubbles** | `Services/Chaos/EffectPayload.cs:154` (`ShowOverlayTimed("pink_filter", duration, 0.25–0.70)`); the `"pink"` effect bubble variant `Services/Chaos/ChaosBubbleVariants.cs:658`. | Popping a "Pink Filter" effect bubble snaps the tint on for a few seconds. |
| **Autonomy Mode** | `AutonomyService.cs:1636` (`PulsePinkFilter`, a `PinkFilterPulse` action, weight 20 `:1029`); gated `AutonomyCanTriggerPinkFilter` (`:1028`). Saves/restores prior state (`:1671`+), 30s pulse. | The autonomous companion flashes the tint up at higher opacity then restores. |
| **Voice commands** | `AutonomyService.VoiceCommands.cs:340` ("go pink" → `ShowOverlaySustained("pink_filter", 0.4)`), `:353` (`HideOverlaySustained`). | Spoken on/off, no timer (sustained hold). |
| **Deeper enhancements** | `Services/Deeper/IActionDispatcher.cs:396` (`ShowOverlaySustained` + `BeginDeeperOverlayBand` `:395`), `:423` (`SetSustainedOverlayOpacity` ramp), `:428` (`ShowOverlayTimed`), `:412/413` (`HideOverlaySustained` + `EndDeeperOverlayBand`). | Creator effect timelines drive the tint as a video effect (see §3c z-order). |
| **Remote control** | `RemoteControlService.cs:1010` (`show_pink_filter`), `:1021` (`stop_pink_filter`), `:1054` (`set_pink_opacity`, clamped 0–50), plus start `:959`, disable `:831/931`; reports `"pink_filter"` in the service list `:752`. | Partner/companion-app remote verbs; route through `MainWindow.EnablePinkFilter` + `RefreshOverlays`. |
| **Keyword / Gaze minigame** | `KeywordTriggerService.cs:1545`, `Lab/GazeMinigame/GazeMinigameWindow.xaml.cs:1254` | Both call `PulseOverlays()` (a shared pulse across all active overlays). |
| **Presets / randomizer** | `MainWindow.Presets.cs:809` (card toggle), `MainWindow.StartStop.cs:145` (`Chaos Start` randomizes `PinkFilterEnabled`). | |

### 3c. What a live pink tint touches (the interaction map)

- **Z-order vs mandatory video (#497).** `ReassertZOrder` (`:2247`) is the reconciler. While a video is
  playing (`App.Video.IsPlaying` + `PrimaryVideoWindow`), overlays are pinned **just below** the video
  window (`ZOrderAction.PinBelowVideo`, `:2311`) so the tint can't bury the clip the user is meant to
  watch — the video is deliberately non-re-raising, so if buried it never recovers. **Exception:** a
  live **Deeper overlay band** (`DeeperOverlayBandActive`, `:46`) means the tint IS the enhanced
  video's own effect, so it's pinned **above** the video (`aboveVideo` branch). The decision is a pure,
  **unit-tested** function `ResolveZOrderAction` (`:2309`). This applies to both the legacy windows and
  the compositor hosts (`:2286`).
- **Compositor host.** When `App.CompositorEnabled`, the tint is a `PinkTintLayer` at z-index 70 (top
  of the session-effect band: above Flash 30 / Subliminal 40 / Bubbles 45 / BouncingText 50 /
  BrainDrain 55 / Spiral 60; below the chaos band 100+). Chaos re-raises its own windows topmost, so a
  sparkle burst still renders over the pink tint (comment in `CompositorLayers.cs:22`).
- **Progression / achievements / quests.** `AchievementService` (`:161`) accumulates
  `TotalPinkFilterMinutes` while the tint is actually running (600-minute achievement `:173`); it feeds
  `App.Quests?.TrackPinkFilterMinutes` (`QuestService.cs:594`, `QuestCategory.PinkFilter`). It's also
  one of three conditions for the **Total Lockdown** achievement (Strict Lock + no panic key + pink,
  `AchievementService.cs:294`). `ProfileSyncService` round-trips `total_pink_filter_minutes` to the
  cloud (`:960/1331/1771`). `SeasonRecapService.TrackFeature(Overlay)` on session start
  (`SessionEngine.cs:232`).
- **Mods.** The tint color is `App.Mods.GetFilterColorRgb()` — a creator mod reskins the pink.
- **NOT XP-on-pop / haptics.** Unlike Flash/Bubble, the tint itself awards no per-event XP or haptic;
  its only progression contribution is the passive minute-accumulation above.

---

## 4. The opacity-ownership model (the load-bearing complexity)

Pink opacity can be "owned" by several sources at once. Getting this wrong is the entire #471/#476/
#535/#563 bug family. The fields (all on `OverlayService`, UI-thread-only):

| Field | `:line` | Meaning |
|---|---|---|
| `_lastAppliedPinkOpacity` | 88 | Drift-sync cache. `UpdatePinkFilterOpacity` (`:1087`) early-returns if settings opacity is unchanged. Set to `-1` to force a re-apply next tick. |
| `_rampPinkOpacity` (`double?`) | 94 | **A Deeper/session ramp owns the opacity.** When set, `UpdatePinkFilterOpacity` early-returns (`:1089`) so the 500ms settings-sync can't stomp the ramp. |
| `_timedPinkHolds` (`int`) | 27 | Count of in-flight `ShowOverlayTimed("pink_filter")` overlays. `>0` stops the reconcilers from tearing the tint down when the persistent setting is off. |
| `_sustainedPinkHeld` (`bool`) | 34 | A `ShowOverlaySustained` (voice / Deeper band) hold. Same "don't reconcile me away" role, but a bool (the ad-hoc show is idempotent). |
| `_bumpPinkActive` / `_bumpPrevRampPink` | 104 / 105 | **#573 intensity bump.** A timed overlay firing while the tint is ALREADY up would be swallowed by the show's early-return; instead it bumps the live opacity (never downward), parks it in `_rampPinkOpacity`, and restores the previous owner when the last timed hold releases. |

**The rules that keep them consistent:**
- `ApplyPinkOpacityDirect(opacity)` (`:890`) is the single low-level writer — pushes to `_pinkLayer.Set`
  AND every legacy window's brush, then forces a post-ramp settings re-apply (`_lastAppliedPinkOpacity
  = -1`).
- `UpdatePinkFilterOpacity` (`:1087`) is the settings-sync path: bails if a ramp owns it, else applies
  `PinkFilterOpacity/100`.
- `PulseOverlays` (`:375`) doubles opacity for 1s then restores **to the ramp value if a ramp is
  active, else via `UpdatePinkFilterOpacity`** — routing the restore through the settings-sync while a
  ramp owned the overlay left it stuck at the boosted (up to fully opaque) value (**#535**, comment at
  `:433`).
- `HideOverlaySustained("pink_filter")` (`:811`) clears `_rampPinkOpacity` on band exit BEFORE the
  conditional teardown — otherwise the tint stayed frozen at the ramp's final opacity forever
  (**#563**).
- `StopPinkFilter` (`:1069`) clears **everything** (`_rampPinkOpacity`, `_sustainedPinkHeld`,
  `_lastAppliedPinkOpacity = -1`) so a force-stop/panic can't leave a stale hold that freezes a future
  overlay.

---

## 5. Settings that gate & tune it (`Models/AppSettings.cs`)

| Setting | `:line` | Range / default | Effect |
|---|---|---|---|
| `PinkFilterEnabled` | 2840 | `false` | Master gate. Reconcilers start/stop the tint on this (unless a hold keeps it up). |
| `PinkFilterOpacity` | 2847 | **clamped 0–50, default 10** | Tint alpha (%). UI slider allows **5–50** (`PinkFilterFeatureControl.xaml:37`); AI command clamps **0–30**; remote clamps **0–50**. |
| `PinkFilterLinkRamp` | 2854 | `false` | Link opacity to the session ramp. |
| `AutonomyCanTriggerPinkFilter` | 3673 | `true` | Lets Autonomy fire the `PinkFilterPulse` action. |
| `DualMonitorEnabled` | (shared) | | All monitors vs primary only (both render paths honor it). |
| `UnifiedOverlayHost` | (shared) | **`true`** | Behind `App.CompositorEnabled` → selects render path A. |
| **Session-scoped** (`Models/Session.cs`) | | | `PinkFilterStartMinute`, `PinkFilterStartOpacity`, `PinkFilterEndOpacity` — the ramp endpoints (e.g. `:194-196`). |
| **Not the filter:** `PinkRushActive` / `PinkRushEndTime` | 571 / 582 | | The Pink **Rush** XP skill (§0.1). |

---

## 6. Where to change X

| Want to… | Edit |
|---|---|
| Change the tint color | `GetFilterRgb()` (`OverlayService.cs:588`) / the mod hook `App.Mods.GetFilterColorRgb()`. The compositor default is also hard-coded in `PinkTintLayer.cs:12`. |
| Change the opacity curve / range | Alpha math in `CreatePinkFilterForScreen` (`:1011`) + `ApplyPinkOpacityDirect` (`:890`) + `PinkTintLayer.Set`. Clamp in `AppSettings.PinkFilterOpacity` (`:2850`); slider bounds in the XAML (`:37`); command clamp `PinkCommand.MaxIntensity` (`:10`). |
| Change render-path selection | `UseCompositor` (`:117`) / `App.CompositorEnabled` (`App.xaml.cs:307`). Both `StartPinkFilter` and `ShowPinkFilterAdHoc` branch on it. |
| Change compositor drawing / z-index | `PinkTintLayer.Render` (`:48`); `CompositorLayers.PinkTint` (`:19`, coordinate with the Avalonia port). |
| Change legacy window creation | `CreatePinkFilterForScreen` (`:1004`) — styles, ex-styles, DPI positioning. |
| Change the ramp/hold/pulse behavior | The §4 fields + `ShowOverlayTimed`/`ShowOverlaySustained`/`SetSustainedOverlayOpacity`/`PulseOverlays`. Keep the ownership rules symmetric. |
| Change z-order vs video | `ReassertZOrder` (`:2247`) + the pure `ResolveZOrderAction` (`:2309`, unit-tested) + the Deeper-band depth (`:51`/`:59`). |
| Add a new trigger | Call `App.Overlay?.ShowOverlayTimed("pink_filter", ms, 0..1)` (timed) or `ShowOverlaySustained`/`HideOverlaySustained` (band), or write `PinkFilterEnabled`/`PinkFilterOpacity` + `RefreshOverlays()`. Don't poke `_pinkFilterWindows` directly. |
| Change the AI command clamp / DTO | `PinkCommand.cs` + shared `SpiralPinkFiler.cs` (mind the Spiral consumer) + `AiCommandService.cs:194` gate. |

---

## 7. GOTCHAS (the expensive ones — most are load-bearing comments in the source)

1. **#535 — pink stuck opaque after a pulse.** `PulseOverlays` doubles opacity then restores. Routing
   the restore through `UpdatePinkFilterOpacity` while a Deeper/session ramp owned the overlay hit the
   ramp's early-return (`:1089`) and left the tint frozen at the boosted value (up to fully opaque),
   recoverable only by toggling the filter off/on. **Fixed** by restoring to `_rampPinkOpacity` when a
   ramp is active (`:446`). *Confirmed present in current code.* Any change to pulse restore must
   preserve this branch.
2. **#563 — ramp hold not cleared on band exit.** `HideOverlaySustained` / the timed hide-timer must
   clear `_rampPinkOpacity` (and only touch it while `PinkShowing`) or the tint freezes at the ramp's
   final opacity forever — or, worse, re-parking a stale value freezes a *future* overlay. See the long
   comments at `:690` and `:804`.
3. **#471/#476 — session ramp must NOT write `Settings.PinkFilterOpacity`.** The session ramp drives
   the overlay directly via `SetSustainedOverlayOpacity` (`SessionEngine.cs:559`) rather than mutating
   the saved setting — otherwise the user's saved opacity ratchets up permanently ("screen keeps
   getting more pink and stays that way"). Keep ramps off the persisted setting.
4. **#497 — the tint must sit BELOW a playing mandatory video** (except a Deeper band). The reconciler
   pins overlays just under the non-re-raising video window; burying it means the next chained clip
   shows "only by flashes". Both legacy windows and compositor hosts go through the same
   `ResolveZOrderAction` decision.
5. **#550 — the compositor layer is dirty-gated.** A steady tint is a static image; `PinkTintLayer`
   only repaints when color/opacity changes (`_dirty`, `:14`). `Show` forces a paint even on unchanged
   values (`:33`) so a re-show after a hide is visible.
6. **Both paths cleared unconditionally on stop.** `StopPinkFilter` hides the layer AND closes every
   window (`:1071`) because `App.CompositorEnabled` can flip mid-run; route checks use
   `_pinkLayer?.IsActive == true`, never the flag, so a stranded layer can't hide.
7. **Legacy windows are per-screen and positioned in physical px.** WPF DPI virtualization lands the
   window half-width on a mixed-DPI secondary; `SourceInitialized` re-pins via `SetWindowPos`
   (`:1053`), and `NotifyTopWindowClosed`→`ReassertBounds` (`:2360`) fixes a DPI re-eval provoked by a
   closing fullscreen video (#457). Dual-monitor changes require a full stop+restart
   (`RefreshForDualMonitorChange`, `:474`) — windows can't be re-homed live.
8. **Reconcilers early-return when the service isn't running.** `RefreshOverlays`/`PulseOverlays`/the
   500ms timer all no-op unless `_isRunning`. A trigger that hasn't called `Start()` (e.g. an ad-hoc
   AI/remote path) must start the service — `PinkCommand` does (`:29-33`), and MainWindow re-runs
   `RefreshOverlays` after voice/Deeper ad-hoc shows because those bypass the reconcile loop
   (`MainWindow.RemoteControl.cs:1419`).
9. **Timed hides use `DispatcherTimer`, not `Task.Delay`.** `ShowOverlayTimed` deliberately uses a
   `DispatcherTimer` (`:673`) per root `CLAUDE.md` async gotcha #6 (fire-and-forget Tasks at shutdown).
   The fire-and-forget continuations that do exist (pulse restore `:438`, autonomy restore) are
   try-catch guarded.
10. **"Pink Rush" is not this feature.** Searching "pink" hits `SkillTreeService`'s 3× XP window and
    its `PinkRush*` settings/events/barks. It's XP, not pixels. Don't wire tint logic to it.
11. **`SpiralPinkFiler` is shared with Spiral** and its filename is misspelled. Both `pink` and
    `spiral` AI commands deserialize the same DTO — a field change affects both features.

---

## 8. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

> This section rots. Confirm with `git log --oneline -- Services/Notifications/OverlayService.cs
> Services/Compositor/PinkTintLayer.cs` and `git branch` before acting.

- **State: mature and shipping.** No dedicated in-flight pink branch. HEAD is `ded7725f` on
  `fix/web-video-interruptions` (v6.5.0-era; the immediately prior commit `95586020` is the intake
  fake-captcha wave 5). Pink work rides in the general overlay/compositor batches.
- **Both render paths are live.** `PinkTintLayer` (compositor, default when `UnifiedOverlayHost` on)
  and the legacy per-screen windows (fallback). The compositor path is the strategic direction — see
  `Services/Compositor/DESIGN.md` (PinkTint z70 listed DONE, `:89`). Per the auto-memory the compositor
  cluster is "default ON, play-test pending" — keep the legacy path working.
- **Recent bug history is closed in code:** #535 (`c1ca37e1`, "Pink filter stuck opaque / pulse-ramp"
  per `memory/pink-filter-stuck-opaque-pulse-ramp.md`), #563 (`a192438a`, ramp hold clear), #471/#476
  (ramp-not-to-settings), #497 (below-video pin), #550 (dirty-gate), #573 (timed bump). All verified
  present in the current source above.
- **Unit-tested seam:** `ResolveZOrderAction` (`:2309`) is pure and covered by the overlay z-order
  tests (grep the xUnit project to confirm before claiming coverage). The rest is play-test-gated.
- **This primer is new** and documents Pink Filter specifically. Spiral and Brain Drain share
  `OverlayService` but have their own (Spiral) / no (Brain Drain) primer — Spiral is a candidate
  follow-up given the shared `SpiralPinkFiler` DTO.

---

## 9. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```

Enable in the **Pink Filter** dashboard card (or the Progression tab checkbox), set opacity, and the
tint appears once the overlay engine is running (`App.Overlay.Start()` — the session/engine start, or
an AI/remote trigger that starts it). Toggle `UnifiedOverlayHost` off to force the legacy per-screen
window path. Watch `App.Logger` debug lines ("Pink filter started/stopped…") to confirm which path is
live.
