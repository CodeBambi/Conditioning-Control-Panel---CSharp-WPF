# FLASH IMAGES — Feature Primer

> **Load this instead of re-exploring the feature.** One-load orientation for the Flash Images
> feature — the oldest, most-poked-at effect in CCP. @-mention this file for coding or design
> sessions. §1–2 = what it is + where the code lives. §3 = the three render paths (the load-bearing
> architecture decision). §4 = **how it's invoked & how it touches the rest of the app** (the call
> graph — read this if you're wiring a new trigger). §5 = settings. §6 = the chaos flash-pool
> sharing. §7 = where-to-change-X. §8 = status (dated). §9 = gotchas.
>
> **Freshness.** Tracks the code as of **2026-07-23** (branch `fix/web-video-interruptions`, HEAD
> `95586020`). §1–7 + §9 track the code and rarely rot; verify a `file:line` with a quick read
> before quoting, since this is a 3195-line file that moves. **§8 is a dated snapshot — verify with
> git.** Keep this updated as the feature evolves rather than re-deriving it.

---

## 0. What Flash Images is, in one paragraph

Flash Images pops user-supplied images/GIFs onto the desktop at a scheduled cadence (or on demand),
holds each for a lifetime, fades it out, and awards XP — the classic subliminal-flash conditioning
loop. It is a **pure C# / WPF service** (`Services/Flash/FlashService.cs`, ~3.2k lines), owned as the
singleton **`App.Flash`** and driven by one vsync-aligned heartbeat. Each flash is modeled by a
`FlashWindow` **state bag** whose *visual* is rendered on one of **three interchangeable paths** —
the modern **compositor layer** (default; a Skia draw-item on the shared per-monitor host), legacy
**solid mode** (a child of the shared click-through host), or the original **per-flash layered
window** (pooled) — while the state bag carries lifetime, hydra, gaze and opacity identically in all
three. It reads images from `App.EffectiveAssetsPath/images` (recursed) plus active content packs,
supports animated GIF + animated WebP, and is triggered by ~a dozen subsystems (sessions, the AI,
chaos/DTRH, Deeper, Autonomy, voice, remote control, quizzes). Do not confuse it with
`Helpers/FlashWindowHelper.cs`, which is an unrelated Win32 taskbar-button flasher.

---

## 1. Where it lives — file map

| File | Role |
|---|---|
| `Services/Flash/FlashService.cs` | **The whole engine.** Scheduling, image load/decode/cache, spawn, the heartbeat, hydra, gaze hooks, three render paths, audio, monitor math. Also defines the internal `FlashWindow`, `LoadedImageData`, `ImageGeometry`, `MonitorInfo` classes (bottom of the file, `:2933`+) and a private `NativeMethods` (`:3164`). |
| `Services/Compositor/FlashLayer.cs` | **Render path A (default).** A `BaseLayer` that is a pure DRAW LIST — FlashService's heartbeat writes each `FlashItem`'s opacity/frame/dwell; the layer just draws (glow halo, rounded clip, letterbox fit, lucky pulse) on the shared Skia host. Owns the `SKImage[]` frames. |
| `Chaos/ChaosBubbleHostOverlay.cs` | **Render path B host (solid mode).** The one shared click-through fullscreen host that solid-mode flashes (and chaos bubbles) mount into. FlashService takes a ref-count on it (`EnsureHostRef`/`ReleaseHostRef`, `:2854`/`:2863`). *(Not read for this primer — treat as the shared host contract.)* |
| `Models/CommandData/FlashImage.cs` | The AI command DTO: `record FlashImage(int Amount, int Duration, int Size, int Opacity)`. |
| `Services/Commands/FlashImageCommand.cs` | Executes an AI `flash_image` command → clamps (`MaxAmount 8`, `MaxDurationSec 10`, `MaxSizePct 150`, `:10-12`) → `App.Flash.TriggerFlashOnce`. |
| `Features/FlashFeatureControl.xaml(.cs)` | The **Flashes settings card** (dashboard tile). Two-way binds every flash setting to `App.Settings.Current.*`, live-applies Start/Stop and `RefreshSchedule`. No flash *logic* lives here. |
| `Chaos/ChaosFlashOverlay.cs` | The chaos **"braindrain" full-screen wash** — a *separate* singleton window (NOT a FlashService window) that borrows one image from FlashService's enabled pool via `GetChaosImagePaths(1)`. |
| `Chaos/ChaosGifCascadeOverlay.cs` | The chaos **gif-rain / "cascade"** — likewise borrows a batch from `GetChaosImagePaths(n)`. |
| `Helpers/FlashWindowHelper.cs` | **UNRELATED** — Win32 `FlashWindowEx` taskbar-button attention flash. Named "Flash" but nothing to do with flash images. |
| `App.xaml.cs` | Declares `public static FlashService Flash` (`:285`) and constructs it in `OnStartup` (`Flash = new FlashService();`, `:1375`, splash step "Initializing flash service"). Also owns `App.CompositorEnabled` (`:307`), the predicate that selects render path A. |
| `Models/AppSettings.cs` | The "Flash Images" settings region (`:608`+) plus the gaze toggles (`:1618`+) and `UnifiedOverlayHost` (`:2398`). See §5. |

---

## 2. The core data model — `FlashWindow` as a state bag

The single most important design fact: **a flash is a `FlashWindow` instance, but that instance is
only *sometimes* a real window on screen.** `FlashWindow : Window` (`:2933`) always carries the
per-flash state — `Frames`, `FrameDelay`, `StartTime`/`CurrentFrameIndex` (GIF stepping), `ExpiresAt`,
`LifetimeCts` + `LifetimeRegistration`, `HydraGeneration`, `OriginalLifetimeMs`, `Monitor`,
`IsClickable`, `IsFadingOut`, `IsLucky`, and `Left/Top/Width/Height` as the DIP bookkeeping rect. Its
*visual* depends on the render path:

- **Compositor (path A):** `UsesLayer = true`. The Window is **never `Show()`n**; the visual is a
  `FlashLayer.FlashItem` (`window.LayerItem`). It is still a constructed `Window` registered in
  `Application.Windows`, so teardown must `Close()` it (`CloseStateBagWindow`, `:2794`) or it leaks.
- **Solid (path B):** `UsesHost = true`. Also never shown; the visual is `window.HostedRoot`, a
  `FrameworkElement` added to `ChaosBubbleHostOverlay`'s canvas. Same close-the-state-bag rule.
- **Per-window (path C):** neither flag. The Window itself is realized and `Show()`n (pooled).

`VisualOpacity` (`:2976`) routes the heartbeat's fade to the right target (layer item / hosted root /
`Window.Opacity`). `SetGazeDwellProgress` (`:3082`) and `BoostLifetime` (`:3050`) likewise branch on
the flags. **Because all three paths share the same state bag, lifetime / hydra / gaze / XP behavior
is identical by construction** — this is the invariant that keeps the three paths from diverging.

`LoadedImageData` (`:3111`) is the decode product: `Frames` (frozen `BitmapSource`s), `Width/Height`,
`FrameDelay`, plus `Geometry`/`Monitor` filled at load time.

---

## 3. The three render paths + how one is chosen

The path is decided **per spawn** at the top of `SpawnFlashWindow` (`:1088`):

```
bool useLayer = UseCompositor;                       // App.CompositorEnabled  (:106 / App.xaml.cs:307)
bool useHost  = !useLayer && settings.FlashSolidMode;
// else: classic per-flash layered window
```

Precedence: **compositor > solid > per-window.** Compositor wins whenever `UnifiedOverlayHost` is on
(default `true`, `AppSettings.cs:2387`) and an engine exists.

| | Path A — Compositor (default) | Path B — Solid mode | Path C — Per-window (legacy) |
|---|---|---|---|
| Flag | `UsesLayer` | `UsesHost` | (neither) |
| Visual | `FlashItem` on shared Skia host | `HostedRoot` on `ChaosBubbleHostOverlay` | one `WS_EX_LAYERED` topmost window each |
| Shown? | never (state bag) | never (state bag) | `Show()`n, **pooled** (`_windowPool`, max 12) |
| Concurrency cap | 30 (`MAX_CONCURRENT_FLASH_HOST`) | 30 | **10** (`MAX_CONCURRENT_FLASH`) |
| Clicks | global mouse hook hit-test (`OnLayerFlashLeftDown`) | click-through — **gaze only** | native `MouseLeftButtonDown` |
| Glow | drawn in Skia (`FlashItem` blur) | WPF `DropShadowEffect` | WPF `DropShadowEffect` |
| In recordings? | yes (main surface) | yes | yes |

The mode-aware cap is a pure, unit-tested function: `ResolveFlashCap(useLayer, useHost)` (`:62`) →
`(useLayer||useHost) ? 30 : 10`. The **10-cap on path C exists because each layered window is a native
compositor surface** — 30 of them backed up the render thread and drove a 3 GB native-memory ramp
(#601). Paths A/B are cheap shared-host items, so they honor the `SimultaneousImages` slider (max 20)
plus hydra headroom.

**Path A spawn detail (`SpawnLayerVisual`, `:1492`):** the per-frame pixel copies (`BitmapSource` →
`SKImage`) run **off the dispatcher in `Task.Run`** to avoid a spawn hitch, then the item spawns on
the dispatcher when conversion lands. `window.LayerSpawnPending` (`:2965`) keeps the heartbeat from
sweeping the still-itemless window during that gap. The continuation re-checks that the flash wasn't
torn down mid-conversion before spawning; ownership of the `SKImage[]` transfers to `FlashLayer`,
which disposes it on `Remove`.

**Path A clicks:** the compositor host is click-through, so a `GlobalMouseHook` (`_layerHook`,
installed via `EnsureLayerHook` `:1593`) hit-tests an immutable snapshot (`_layerHits`) rebuilt every
heartbeat (`RebuildLayerHitSnapshot`, `:1652`). Clicks inside a *playing mandatory video's* rect are
excluded (`_layerVideoExcludePx`) because the host is pinned below the video (#497) and the flash
there is invisible — swallowing those clicks would eat the video's attention-check taps.

---

## 4. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

This is the section to read before wiring a new trigger. There are two public entry styles: **run
mode** (scheduled loop, gated on `_isRunning`) and **one-shot** (fire once, works even when the loop
is off via `_oneShotActive`).

### 4a. Public API surface (all on `App.Flash`)

| Method | `:line` | Purpose |
|---|---|---|
| `Start()` / `Stop()` | 312 / 329 | Begin/end the scheduled loop. `Stop` cancels, closes all windows, releases host/hook, clears the decode cache. |
| `TriggerFlash()` | 351 | Fire one scheduled flash now (no-op if not running or busy). Used by the scheduler tick. |
| `TriggerFlashOnce(amount?, duration?, size?, suppressHaptic?)` | 367 | **The main on-demand entry.** Works with the loop OFF (`_oneShotActive`). Duration is **ms**. |
| `TriggerFlashOnceWithImage(path, durationMs, playSound, suppressHaptic?)` | 405 | One-shot pinned to a specific image (Deeper timeline effects). Falls back to random if the path is missing. |
| `RefreshSchedule()` | 489 | Re-arm the timer after a frequency change. |
| `RefreshImagesPath()` / `LoadAssets()` / `ClearFileCache()` / `ClearImageCache()` | 296 / 472 / 2501 / 2513 | Re-point/reload after assets or the custom folder change. |
| `GetChaosImagePaths(count)` | 2223 | **Shared pool accessor** — see §6. |
| `GetGazeTargets()` / `GazePop(window)` | 239 / 267 | Gaze integration (see 4d). |
| `RaiseAllToFront()` | 189 | Re-assert topmost above chaos bubbles (chaos re-raise). |
| `PlayRandomSound()` | 2339 | Play a random flash-folder sound (quest-complete celebration). |
| Properties | | `IsRunning` (174), `ActiveWindowCount` (180), `LastDisplayedImagePaths` (229), `GifDecodes`/`StaticDecodes` (151/152). |
| Events | 162–165 | `FlashAboutToDisplay`, `FlashDisplayed`, `FlashClicked`, `FlashAudioPlaying`. |

### 4b. Who calls it (the trigger map)

| Caller | `file:line` | What it does |
|---|---|---|
| **MainWindow** (manual Start/Stop) | `MainWindow.StartStop.cs:165/298`, `:366` (ClearImageCache); `MainWindow.UiUpdates.cs:1126/1130` | The dashboard Start/Stop engine button. |
| **SessionEngine** (AI sessions) | `SessionEngine.cs:442` (Start on session start), `:899/:904/:905` (start now or `DeferFeatureStart` at `FlashStartMinute`), `:407/:911` (Stop). Copies session flash params into `App.Settings.Current` (`:890-894`). | Sessions override the live settings with session-specific flash config and start/schedule the loop. |
| **AI command dispatch** | `PromptService.cs:46` (AI emits `{"command":"flash_image","data":{Amount,Duration,Size,Opacity}}`) → `CommandFactory.cs:26` → `FlashImageCommand.cs:30` → `TriggerFlashOnce`. | The AI companion can fire flashes as an action. Hard-clamped (8 / 10s / 150%). |
| **Chaos / Rabbit Hole (WPF)** | `EffectPayload.cs:104` (chaos "Trigger Bubble" pop-flash → `TriggerFlashOnce`); `ChaosModeService.cs:771/801` (`RaiseAllToFront`), `:848-849` (reads `GifDecodes`/`StaticDecodes` for the OOM hunt). | Chaos pop-flashes reuse the one-shot path. Chaos *washes/rain* use the **pool** not the spawner — see §6. |
| **Chaos washes (separate windows)** | `ChaosFlashOverlay.cs:229` (`GetChaosImagePaths(1)`); `ChaosGifCascadeOverlay.cs:59/65` (`GetChaosImagePaths(batch)`). | Braindrain wash + gif-rain draw the **same enabled image pool** but render in their own overlays. |
| **DTRH (web game)** | `Services/Chaos/DtrhSpike.cs:135` (`TriggerFlashOnce(1, 1200, suppressHaptic:true)`). | Pipeline-proof harness. (DTRH's own in-page effects are self-contained and do **not** use `App.Flash`.) |
| **Deeper enhancement** | `Services/Deeper/IActionDispatcher.cs:349` → `TriggerFlashOnceWithImage(null, DurationMs, flashSound, SuppressHaptic)`. | Deeper effect-timeline items pin a flash beat. |
| **Autonomy Mode** | `AutonomyService.cs:1216-1222`, `AutonomyService.VoiceCommands.cs:240` → `TriggerFlashOnce()`. Gated by `AppSettings.AutonomyCanTriggerFlash` (`:3583`). | Autonomous companion can fire flashes independent of the loop. |
| **Voice / keyword** | `KeywordTriggerService.cs:1540` → `TriggerFlashOnce()`. | Spoken trigger words fire a flash. |
| **Remote control** | `RemoteControlService.cs:977` (`TriggerFlashOnce`), `:987/:991` (Start/Stop), `:758` (reports `flash_loop` service state), `MainWindow.RemoteControl.cs:1403`. | Companion-app / partner remote. |
| **Quiz + Gaze minigame + Lab** | `Windows/QuizWindow.xaml.cs:1213`, `Lab/GazeMinigame/GazeMinigameWindow.xaml.cs:1241/1664/756-758`. | Reward/punish flashes; the minigame pauses the loop while it owns the screen. |
| **VideoService** | `VideoService.cs:1557` (Stop before a mandatory video), `:4082` (Start after). | Flashes stand down for mandatory video, then resume. |
| **Quests** | `MainWindow.Quests.cs:45` → `PlayRandomSound()`. | Celebratory chime on quest complete. |

### 4c. Who listens to it (the event/consumer map)

- **AvatarTube companion** (`AvatarTubeWindow.xaml.cs:259-263`, unsub `Windowing.cs:826-830`)
  subscribes to `FlashAboutToDisplay` / `FlashClicked` / `FlashAudioPlaying` — the avatar announces a
  flash (1 s pre-roll, `ShowImages` waits `Task.Delay(1000)` at `:567`) and shows the audio filename
  as a speech bubble.
- **BarkService** (`BarkService.cs:516`) wires `FlashDisplayed` to a companion bark.
- **SessionLogService** (`SessionLogService.cs:155/177`) subscribes to `FlashDisplayed`, then reads
  `LastDisplayedImagePaths` (`:229`, snapshotted at `:1066` right before the event) to attribute the
  displayed files in the session log.
- **PerformanceProfile** (`PerformanceProfile.cs:30`) reads `ActiveWindowCount` as a live-load signal
  for automatic performance-tier escalation (also feeds `ComputeDecodeMaxDim` `:916`).
- **ProgressionService / AchievementService / SkillTree**: `SpawnFlashWindow` awards XP
  (`App.Progression.AddXP(xp*multiplier, XPSource.Flash)`, `:1473`), tracks the achievement
  (`TrackFlashImage`, `:1478`), and rolls lucky-flash multipliers + sparkle-glow tiers via
  `App.SkillTree` (`:1236/:1249`). Base XP 4, or 8 with audio; hydra children decay 75%→10% (`:1220`+).
- **AudioService**: on an audible flash, `PlaySound` ducks other audio (`Duck`/`Unduck`, `:969/:977`)
  and `App.Audio.MarkWhisperAudio` tells the bark system not to talk over it (`:965`).
- **HapticsService**: `App.Haptics?.FlashDecayVibeAsync()` on spawn (unless `suppressHaptic`),
  `FlashClickVibeAsync()` on pop.
- **OverlayService**: `App.Overlay?.NotifyTopWindowClosed()` after sweeps (`:1943/:2901`) so the
  overlay z-order reconciler re-runs; the layer path also coordinates with the #497 video pin.
- **GazeFocusService**: see 4d.

### 4d. Gaze integration

`GazeFocusService` (`:310/:434/:645`) calls `GetGazeTargets()` (`:239`, returns live non-fading
windows only when `FlashGazePopEnabled` **or** `FlashGazeLingerEnabled` is set), drives
`SetGazeDwellProgress` for the inflate tell, `BoostLifetime` for stare-linger, and `GazePop` (`:267`,
same pipeline as a mouse click) when a dwell completes. Gaze works in **all three render paths** (it
reads the state bag), which is why solid-mode flashes are still gaze-poppable despite being
click-through.

---

## 5. Settings that gate & tune it (`Models/AppSettings.cs`)

All two-way bound in `FlashFeatureControl` and auto-saved. Clamps are enforced in the setters.

| Setting | `:line` | Range / default | Effect |
|---|---|---|---|
| `FlashEnabled` | 611 | `true` | Master gate for the scheduled loop (`ScheduleNextFlash` bails if off, `:504`). |
| `FlashFrequency` | 618 | 1–180, **10** | Flashes per **hour**; `ScheduleNextFlash` computes `3600/freq` ± 30% variance, min 3 s (`:511`). |
| `SimultaneousImages` | 684 | 1–20, **5** | Images per flash event (`GetNextImages(count)`). |
| `HydraLimit` | 677 | 1–20, **20** | Max on-screen during hydra multiplication (also hard-capped 20 in `OnFlashClicked`). |
| `CorruptionMode` | 657 | `false` | **Hydra**: clicking a flash spawns 2 more (`TriggerMultiplication`, `:1732`). |
| `HydraLinkedTiming` | 670 | `true` | Linked = children inherit parent's remaining lifetime; Independent = fresh full lifetime + XP decay. |
| `ImageScale` | 695 | 50–250, **100** | % over the base size (40% of monitor); feeds `CalculateGeometry` + `ComputeDecodeMaxDim`. |
| `FlashOpacity` | 702 | 10–100, **100** | Peak alpha the heartbeat fades to (`maxAlpha`, `:1854`). |
| `FlashDuration` | 742 | 1–30 s, **5** | On-screen seconds when audio is off (lifetime = duration·1000 + 1000 ms). |
| `FlashAudioEnabled` | 716 | `true` | Play one sound per event; when on, the **audio length drives the duration** (`:959`). |
| `FadeDuration` | 709 | 0–200, **40** | Legacy fade-percentage; the current heartbeat fades at a fixed `FADE_PER_SEC = 2.4` (`:1800`) — this setting is **not read** by the live fade. |
| `FlashGlowEnabled` | 723 | `true` | Enables the lucky/sparkle glow halo (also gated by performance tier). |
| `FlashSolidMode` | 735 | `false` | Selects render **path B** when compositor is off (some fullscreen games dislike path C's window churn). |
| `FlashClickable` | 625 | `true` | Mouse-poppable. Setter self-heals the gaze toggles after the v3.4 decoupling migration (`:637`, `RunFlashClickableDecouplingMigration` `:5124`). |
| `FlashGazePopEnabled` | 1618 | | Gaze dwell → pop. |
| `FlashGazeLingerEnabled` | 1625 | | Gaze holds a flash alive (stare-linger). |
| `FlashGazeLingerExtensionMs` | 1635 | | Per-dwell lifetime boost. |
| `DualMonitorEnabled` | (shared) | | `PickMonitor`/`GetMonitors` spread flashes across screens vs. primary only. |
| `AudioDuckingEnabled` / `DuckingLevel` | 909 / 916 | | Duck other audio during an audible flash. |
| `UnifiedOverlayHost` | 2398 | **`true`** | The setting behind `App.CompositorEnabled` → selects render **path A**. |
| `AutonomyCanTriggerFlash` | 3583 | `true` | Lets Autonomy Mode fire flashes. |

---

## 6. The chaos flash-pool sharing — `GetChaosImagePaths`

The chaos "glitch/braindrain" wash (`ChaosFlashOverlay`) and "cascade" gif-rain
(`ChaosGifCascadeOverlay`) do **not** spawn flash windows — they render in their own overlays but
must draw from the **exact same enabled image set** the flashes use. `GetChaosImagePaths(count)`
(`:2223`) is that shared accessor: disk images (recursed, honoring the asset manager's
`DisabledAssetPaths`) **plus** active content-pack images (decrypted to temp on demand). It differs
from the flash pipeline's own `GetNextImages` (`:2142`) in one way: picks are **distinct** (dedup on
source identity), because a wash/rain that repeats the same image looks broken. It can do disk I/O
(pack decrypt), so **callers fetch it off the UI thread** — a synchronous pack-decrypt on the
dispatcher was the dashboard-freeze bug (`8343e1e0`). Before this existed, chaos washes re-listed the
raw images folder (`ChaosImagePool`) and silently drew nothing for content-pack / curated users.

Image sourcing generally (`RefreshImageLists`, `:2280`): `_imageList` from `GetMediaFiles` (60 s
cached, security-validated, disabled-asset-filtered, subfolder-recursed) + `_packImageList` from
`App.ContentPacks`. Supported extensions include png/jpg/gif/webp/bmp/tif/heic/avif/ico.

---

## 7. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Add a new trigger source | Call `App.Flash.TriggerFlashOnce(...)` (one-shot) or `TriggerFlashOnceWithImage(path,...)`. Do **not** call `Start()` unless you own the loop. |
| Add a new render path / change path selection | The `useLayer`/`useHost` decision in `SpawnFlashWindow` (`:1088`) + `ResolveFlashCap` (`:62`). A new path needs: a `FlashWindow` flag, a `VisualOpacity` branch (`:2976`), a `SetGazeDwellProgress` branch (`:3082`), a spawn branch (`:1360`+), and a teardown branch in `SafeCloseFlashWindow` (`:2731`+). |
| Change the schedule / cadence | `ScheduleNextFlash` (`:499`) — frequency math + variance + 3 s floor. |
| Change flash lifetime / fade | Lifetime in `ShowImages` (`:990`); fade speed `FADE_PER_SEC` (`:1800`); the heartbeat itself `Heartbeat_Tick` (`:1849`). |
| Change decode size / caps | `ComputeDecodeMaxDim` (`:916`); cache caps `MAX_IMAGE_CACHE_ENTRIES`/`_BYTES` (`:143-144`); GIF/webp frame caps in `LoadGifFrames`/`TryLoadAnimatedWebpFrames` (`:847`/`:826`). |
| Change hydra behavior | `OnFlashClicked` (`:1692`) + `TriggerMultiplication` (`:1732`); XP decay in `SpawnFlashWindow` (`:1220`). |
| Change size / placement | `CalculateGeometry` (`:2072`, base = 40% of monitor); overlap avoidance `IsOverlapping` (`:2105`); monitor pick `PickMonitor`/`GetMonitors` (`:1969`/`:1987`). |
| Add a flash setting | `AppSettings.cs` flash region (`:608`), then a control + binding in `FlashFeatureControl.xaml(.cs)`. Read it in `SpawnFlashWindow`/`ShowImages`. |
| Change the AI command clamps | `FlashImageCommand.cs:10-12`. Add a field: `Models/CommandData/FlashImage.cs` + `PromptService.cs` prompt + `CommandFactory.cs`. |
| Change the glow | Path A: `FlashLayer.Render` glow block (`FlashLayer.cs:161`). Paths B/C: the `DropShadowEffect` build (`:1290`). Params computed once (`:1249`+). |
| Change chaos-pool sharing | `GetChaosImagePaths` (`:2223`); consumers `ChaosFlashOverlay`/`ChaosGifCascadeOverlay`. |

---

## 8. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

> This section rots. Confirm with `git log --oneline -- Services/Flash/FlashService.cs` and
> `git branch` before acting.

- **State: mature and shipping.** No dedicated in-flight feature branch for flashes. HEAD is
  `95586020` on `fix/web-video-interruptions` (v6.5.0-era); flash work rides in the general triage
  batches. The most recent flash-touching commits: `db92d814` (v6.5.0 triage #614–#627, incl. flash
  pool), `49bba471` (post-6.4.1: flash cap #601, pop-flash #593), `83f18eb1`/`e6d41eab` (the
  compositor `FlashLayer` + pre-merge review), `9b511b45`/`8343e1e0` (off-UI-thread pack fetch),
  `6a0c2e54` (CompleteRender UI-hang fix + hang-hunt harness), `6c46317f` (native-memory / #476 #486).
- **Compositor path is the default** (`UnifiedOverlayHost = true`, `MigrateEnableUnifiedOverlayHost`
  flips old installs on). Paths B and C are fallbacks. Per the auto-memory, the compositor cluster is
  "default ON PUSHED, play-test pending" — so path A is the hot path but keep B/C working.
- **Retired code:** the GDI+ decode helpers (`ScaledSize`/`DownscaleBitmap`/`ConvertToBitmapSource`)
  were removed with the SKCodec migration (#486); no flash path touches `System.Drawing` for decode
  anymore. `FadeDuration` (setting) is effectively legacy — the heartbeat uses a fixed fade rate.
- **Known limits:** the per-window path is capped at **10** concurrent (native-memory ceiling) while
  the slider allows 20 — only paths A/B honor >10. `ChaosFlashOverlay` fills the primary monitor
  unless `DualMonitorEnabled`.

---

## 9. GOTCHAS (the expensive ones — most are load-bearing comments in the source)

1. **Never resize a realized layered window.** Setting `Width`/`Height` on a shown path-C window runs
   a synchronous `MediaContext.CompleteRender`; on a backed-up render thread that **never returns and
   wedges the UI** (Application Hang 1002, dump-confirmed 2026-06-13). Hence: shells are **bucketed**
   (`FLASH_SHELL_BUCKET 128` + slack, `:70`) and sized **before** first `Show()`; the pool only reuses
   a size-matched window (`AcquireFlashWindow`, `:2589`); glow expands the *bookkeeping rect*, never
   the live window.
2. **`WM_DPICHANGED` is swallowed on flash windows** (`SwallowDpiChanged`, `:2816`, hooked in
   `HideFromAltTab`) — WPF's auto DPI-rescale is the same `OnResize → CompleteRender` deadlock. Flash
   geometry is computed manually per target monitor, so dropping it is safe. `ChaosFlashOverlay`
   caps its own DPI re-stamps at 4 to avoid a mixed-DPI oscillation loop (`:298`).
3. **Layered windows are recycled, never closed mid-run.** Closing a `WS_EX_LAYERED` window while
   other layered surfaces animate can wedge the shared render thread — `SafeCloseFlashWindow` hides +
   pools (`:2772`); real `Close()` happens only in `Dispose` / pool overflow / unloaded-shell
   eviction. An **unloaded pooled window must still be `Close()`d** or its HWND leaks for the process
   lifetime (#627, `:2611`).
4. **State-bag windows (paths A/B) must be `Close()`d too.** They're never shown but *are* registered
   in `Application.Windows`; skipping `CloseStateBagWindow` leaked one `Window` per flash (`:2745`,
   `:2761`).
5. **Lucky-glow `RepeatBehavior.Forever` animations pin native GPU blur targets.** They survive run
   teardown until cleared with `BeginAnimation(prop, null)` — this was a chunk of the chaos OOM climb
   (managed heap stayed flat). `SafeCloseFlashWindow` stops them (`:2718`); path A pulses inside the
   Skia render instead (no Forever clocks to leak).
6. **Decode off the UI thread, at display size, through WIC/SKCodec — never GDI+.** GDI+ decoded onto
   the native Win32 heap and never returned pages under high-frequency churn (VMMap: managed flat,
   private bytes to multi-GB, empty crash log). Static → WIC with `DecodePixelWidth/Height`; GIF +
   animated WebP → `AnimatedWebp`/SKCodec (≤1280px, ≤60 frames, ≤30 MB). Cache is keyed by **path
   only** (decodeMax stored alongside) — keying by path+dim halved capacity under tier shifts (#486).
7. **Pack-image fetch must be off the UI thread.** `GetChaosImagePaths` can synchronously AES-decrypt
   a pack file; calling it on the dispatcher froze the dashboard for pack users (`8343e1e0`). Both
   chaos overlays `Task.Run` the fetch then `BeginInvoke` the show.
8. **The heartbeat is `CompositionTarget.Rendering`, not a `DispatcherTimer`.** A 33 ms timer beats
   against vsync and makes GIFs judder. A live `Rendering` subscription forces continuous WPF
   rendering, so it is subscribed **only while flashes are active** (`StartHeartbeat`/`StopHeartbeat`,
   `:1803`/`:1820`) — leaving it on is a silent battery/GPU drain.
9. **Fire-and-forget continuations are guarded.** Every `Task.Delay(...).ContinueWith(...)` uses
   `TaskContinuationOptions.NotOnCanceled` + a null-dispatcher/`HasShutdownStarted` check — the
   general CCP async-crash rule (root `CLAUDE.md` §6).
10. **`ShowImages` clears `stage`/`Content` semantics differ per path** — an element has one logical
    parent, so `content` is assigned to `window.Content` (path C), added to the host canvas (path B),
    or left null (path A draws frames directly). Don't assume a WPF visual tree exists in path A.
11. **`FlashWindowHelper` is not this feature.** Cross-referencing "Flash" in the codebase will hit
    the Win32 taskbar flasher — ignore it.

---

## 10. Build / run / dev entry points

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```
Then: drop images into `%APPDATA%/ConditioningControlPanel/assets/images` (or the custom assets
folder), open the **Flashes** card, enable + Start the engine. `TriggerFlashOnce` fires immediately
regardless of the loop, so voice/AI/remote triggers work without pressing Start.

- **Force a render path:** compositor is default; toggle `UnifiedOverlayHost` off + `FlashSolidMode`
  on for path B, or both off for path C.
- **Concurrency-cap unit test:** `ResolveFlashCap` (`:62`) is pure — covered by the flash xUnit tests
  (see the `triage-post641` memory: 35 green). `AmbientFlashDurationMs` (`EffectPayload.cs:85`) is
  likewise pure/testable.
- **Native-memory / decode watch:** `App.Flash.GifDecodes` / `.StaticDecodes` (cumulative) are the
  attribution counters for the chaos OOM hunt (read by `ChaosModeService.cs:848`).
