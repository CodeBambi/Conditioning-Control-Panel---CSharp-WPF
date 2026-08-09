# Mandatory Videos — Feature Primer

> **Purpose.** One-load orientation for the forced-video-playback feature so you can maintain it
> WITHOUT re-reading the ~5,000-line `VideoService.cs`. §0 is the one-paragraph model. §1 is the
> file map. **§2 is the load-bearing section — every way a video gets triggered and every system it
> touches** (read it before wiring anything new to video). §3–7 are the implementation layers
> (playback lifecycle, render paths, attention checks, the watchdog/self-heal machinery, the Deeper
> enhancement layer). §8 settings, §9 where-to-change-X, §10 gotchas, §11 dated status.
>
> **Freshness.** Tracks the code as of **2026-07-23** on branch `fix/web-video-interruptions`
> (HEAD `95586020`, v6.5.0). §1–§10 track the code and rarely rot; **§11 is a dated snapshot —
> verify with `git log` before acting.** Every `file:line` below was read, but line numbers drift —
> confirm with a quick read before quoting. This feature has a long, painful bug history; §10 and
> §6 exist so you don't re-learn it the hard way.

---

## 0. What Mandatory Videos is, in one paragraph

A scheduler that periodically forces a full-screen, borderless, topmost video over everything the
user is doing, decoded by **LibVLC** (codec-independent, VLC's bundled codecs — works on Windows
N/KN with no Media Foundation). Videos come from `App.EffectiveAssetsPath/videos` (recursive) plus
decrypted content-pack videos. The core is one big service, `Services/Video/VideoService.cs`
(`VideoService`, exposed as `App.Video`), which owns a shared static `LibVLC` instance, a per-screen
set of `MediaPlayer`s + fullscreen `Window`s, an optional **attention-check** mini-game (bouncing
"CLICK ME" targets the user must catch to avoid a replay loop), a **strict-lock** mode (the video
can't be closed except by the panic key), and a thick layer of **watchdogs and self-heal** born from
years of white-screen / freeze / handle-exhaustion bug reports. It ducks other apps' audio while
playing (`App.Audio.Duck`), fills secondary monitors, optionally renders a TikTok-style blurred
background for aspect-mismatched clips, credits watch-time toward XP/quests, and can be overlaid by
the **Deeper** effect engine synced to the video's playback clock. It is one of several
`InteractionQueueService`-gated fullscreen interactions (video / bubble-count / lock-card are
mutually exclusive).

---

## 1. Where it lives — file map

All paths under `.../ConditioningControlPanel/`. All `file:line` verified 2026-07-23.

### The core
| File | Owns |
|---|---|
| `Services/Video/VideoService.cs` (~4,976 lines) | **Everything.** The scheduler, LibVLC lifecycle, window creation, attention checks, strict lock, all watchdogs + self-heal, teardown, queue refill. Also contains the nested `BlurVmemSurface` class (`:2200`) and the `FloatingText` attention-target class (`:4500`). |
| `Services/Video/DualMonitorVideoService.cs` | **Separate, single-decoder multi-monitor** service (`App.DualMonitorVideo`, `App.xaml.cs:1546`). One LibVLC memory-render buffer blitted to a `WriteableBitmap` per screen. Used for **browser/URL fullscreen** playback, NOT the mandatory scheduler. Don't confuse it with `VideoService`'s own per-screen decoders. |
| `Services/Video/VideoDiag.cs` | Flush-on-write diagnostic trace (`logs/video-diag.log`) + a UI-thread heartbeat. Its own file + `WriteThrough`/`FlushFileBuffers` so a hard power-reset can't lose the last lines. Born from #616–#623 (§10). `VideoDiag.Log(tag, msg)` is enqueue-only, safe from any thread. Started in `App.OnStartup` (`App.xaml.cs:1186`). |
| `Services/Video/VideoMetadataCache.cs` | On-disk per-video **duration** cache (`video_metadata.json`), keyed by path+size+mtime, parsed lazily via LibVLC. Falls open (a miss never blocks). Feeds the min/max duration filter. Lazily built from the shared LibVLC (`VideoService.MetadataCache`, `:367`). |
| `Features/VideoFeatureControl.xaml(.cs)` | The **Settings-tab option card**: enable toggle, per-hour slider, strict-lock (double-warning), min/max duration, attention-check knobs, gaze-click, "Test Video" button. Pure settings binding + `App.Video.Start/Stop/ForceCleanup/TriggerVideo`. |
| `Controls/InlineLoopVideo.cs` | A small **muted looping preview surface** (memory-render into a WPF `Image`, no VideoView). Reuses `VideoService.SharedLibVLC`. NOT part of mandatory playback — a reusable widget (popups/cards). Shares the vmem callback pattern. |
| `Services/Audio/VideoDownloader.cs` | HTTP video download to a temp file (retry/progress). Used by the **haptics audio-sync** pipeline to fetch a URL for analysis, NOT by mandatory playback. |

### The Deeper enhancement layer (effects synced to video time)
| File | Owns |
|---|---|
| `Services/Deeper/VideoEnhancementBridge.cs` | Ties `VideoService` playback to the Deeper `EnhancementEngine`. On `VideoStarted` it resolves a `.ccpenh.json` for the playing file and binds an engine to the primary player's clock; unbinds on `VideoEnded`. Owns its OWN `EnhancementHostService`. Gated by `VideoEnhanceIfPossible` (default OFF). Constructed at `App.xaml.cs:1615` (`App.VideoEnhanceBridge`). |
| `Services/Deeper/VideoServiceTimeSource.cs` | Adapts `VideoService`'s primary-monitor playback to the generic `IPlaybackTimeSource` the engine consumes (time, seek, pause, play, video-rect for gaze). Marshals LibVLC-thread `TimeChanged` to the UI thread. |
| `Services/Deeper/BrowserVideoTimeSource.cs` | The sibling `IPlaybackTimeSource` for a WebView2 `<video>` (polls `currentTime` at 10 Hz). Not mandatory-video, but the same abstraction — included so you see the family. |
| `Services/Deeper/MandatoryVideoEnhancementScanner.cs` | Background scan of the videos folder answering "does ANY video have a resolvable enhancement / need the webcam?" — drives the engine-start nudge. Cached per path+size+mtime. |

### C# wiring
| Point | File:line |
|---|---|
| Service construction + LibVLC preload | `App.xaml.cs:1378` (`Video = new VideoService()`), `:1379` (`PreloadLibVLC()`) |
| Diag trace start | `App.xaml.cs:1186` |
| Enhancement bridge construction | `App.xaml.cs:1615`; property `:396` |
| Settings (all video knobs) | `Models/AppSettings.cs` — see §8 for the table |

---

## 2. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

This is the section to read first. There is **no `App.Video` command class per se** — callers invoke
the service directly through a handful of public methods. The two entry points that actually start a
video are **`TriggerVideo(silentIfEmpty, strictOverride)`** (`VideoService.cs:935` — picks a random
video) and **`PlaySpecificVideo(path, strict)`** (`:1105` — a named file). `PlayUrl(url)` (`:1230`)
is a third, non-strict, no-attention path used only for browser/URL fullscreen.

### 2a. Who triggers a video

| Trigger | Call site | Notes |
|---|---|---|
| **The scheduler (the "mandatory" part)** | `ScheduleNext` → `_scheduler.Tick` → `TriggerVideo()` (`:1459`) | The heartbeat. `Start()` (`:787`) arms it; each video's `Cleanup()` re-arms it (`:4092`). Interval = `3600/VideosPerHour × (0.8–1.2 jitter)`, min 60s (`:1452`). |
| **AI session commands** | `Services/Commands/MediaCommand.cs:75/108` | The AI's `video` command. `PlaySpecificVideo` for a named file, `TriggerVideo` for "any video". **Gated at the sink** on `MandatoryVideosEnabled` (#512) and skipped if already playing. |
| **`SessionEngine`** (AI sessions) | `SessionEngine.cs:453/1074/1084/1079` | Session start applies settings then `App.Video.Start()` — or `Stop()` + `DeferFeatureStart(...MandatoryVideosStartMinute...)` for a delayed start. `SessionEngine.cs:417` stops video on session end. |
| **Chaos / "Rabbit Hole" mode** | `Services/Chaos/EffectPayload.cs:178–179` | A chaos video "bubble": `ArmRandomSegment(15s)` then `TriggerVideo(silentIfEmpty:true)`. See §2c for the z-order dance. Chaos also force-caps a video at 15s (`ChaosModeService.cs:1050` → `ForceCleanup`). |
| **Autonomy / Takeover** | `AutonomyService.cs:1386`, `AutonomyService.VoiceCommands.cs:178/191` | Voice command "play a video" / autonomy-driven trigger; voice "stop" → `Stop()`. Takeover passes `strictOverride` = `TakeoverVideosStrict` rather than flipping the global (see the `strictOverride` doc, `:929`). |
| **Remote control** | `RemoteControlService.cs:1086/1090/1094` | Remote `video`/`start`/`stop` verbs. |
| **Startup video** | `MainWindow.UiUpdates.cs:1440/1446` | `ForceVideoOnLaunch` → `PlaySpecificVideo(startupPath...)` or `TriggerVideo(silentIfEmpty:true)`. `silentIfEmpty` suppresses the "no videos" dialog (#333). |
| **Manual "Test Video"** | `VideoFeatureControl.xaml.cs:313` | Settings-card button; offers a force-reset if a video seems stuck. |
| **Engine start/stop plumbing** | `MainWindow.StartStop.cs:168/312/745`, `MainWindow.UiUpdates.cs:1195`, `MainWindow.Presets.cs:806` | Start/stop the whole session ↔ start/stop the video scheduler. |
| **Interaction-queue stuck recovery** | `InteractionQueueService.cs:341` | If the Video slot wedges, the queue calls `ForceCleanup()`. |

### 2b. The InteractionQueue gate (mutual exclusion)

Every start routes through `App.InteractionQueue` (`InteractionQueueService`). Video, BubbleCount,
and LockCard are mutually exclusive fullscreen interactions. `TriggerVideo` (`:982`) checks
`CurrentInteraction`/`CanStart`; if another interaction owns the slot it **queues** itself
(`TryStart(..., queue:true)`) to run when the slot frees. If it dequeued *us* (`CurrentInteraction
== Video`), it proceeds. On teardown, `Cleanup` calls `Complete(Video)` (`:4076`); the panic/force
paths call `CompleteIfCurrent(Video)` (`:1220`). **The slot-preservation subtlety**: the
attention-fail retry loop and the vout self-heal replay deliberately do NOT release the slot (so a
queued lock-card can't start on top of the retry video); they `ExtendTimeout(300, Video)` instead
(`:3053`, `:3534`).

### 2c. What a playing video touches

- **Audio ducking** — `PlayVideo` (`:1562`) calls `App.Audio.Duck(DuckingLevel)` if
  `AudioDuckingEnabled`, and sets `_didDuck`. The matching `Unduck` is released **once per teardown
  in `CloseAll`'s finally** (`:3999`), not in `Cleanup`, so the retry/troll loop and engine-stop
  paths can't leak the ref count and pin other apps quiet (#526). Video's own volume =
  `master% × video%` (`GetEffectiveVolume`, `:732`), plus an `_externalMute` switch the DTRH dive
  uses (`SetExternalMute`, `:721`).
- **Flashes / bubbles** — `PlayVideo` stops flashes (`App.Flash.Stop()`, `:1557`); `StartVideoPlayback`
  pauses+clears the ambient bubble game (`App.Bubbles.PauseAndClear()`, `:1708`). `Cleanup` resumes
  both (`App.Bubbles.Resume()` `:4067`, `App.Flash.Start()` `:4082`).
- **Subliminals** — unless attention checks are on (or a minigame is busy), a "Bambi Freeze"
  subliminal fires 800ms before the video (`TriggerBambiFreeze(deferReset:true)`, `:1064`); the
  deferred reset runs in `Cleanup` (`:4070`).
- **Progression / achievements** — attention hits award XP (`App.Progression.AddXP(15, Video)`,
  `:2849`); a passed check awards a bonus + `TrackAttentionCheckPassed` (`:3016/3019`); a fail tracks
  `TrackAttentionCheckFailed` + `App.Companion.OnAttentionCheckFailed` (−25 XP). Watch-time is
  credited on every teardown via `FinalizeWatchCredit` → `App.Achievements.TrackVideoWatched` +
  the `VideoWatchCredited` event (`:3684`, #447).
- **Chaos run z-order** — a video that fires mid-chaos-run is marked `WS_EX_NOACTIVATE`
  (`MakeNonActivating`, `:4384`) and answers `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`
  (`PreventClickRaise`, `:4410`) so it can't steal z-order from the game's bubbles/HUD; clicking it
  lifts the game layer back (`App.Chaos.RaiseGameLayerAboveVideo`, `:2965`).
- **Deeper enhancements** — `VideoEnhancementBridge` subscribes to `VideoStarted`/`VideoEnded`
  (§7). While bound it flips `SetEnhancementDriving(true)` so the safety timer stops guillotining
  the clip at its raw duration (#536).
- **Discord / haptics** — `App.DiscordRpc.SetVideoActivity()` (`:1551`); a background haptic vibe
  starts on play (`App.Haptics.StartVideoBackgroundVibeAsync`, `:1723`) and stops in `Cleanup`.
- **Session log / telemetry** — `SessionLogService` reads `LastVideoPath` in the `VideoStarted`
  handler; DTRH reads `VideoWatchCredited` for watch-time. `LastVideoTitle`/`LastVideoPath` stay set
  after the video ends for AI reactions (`:339/346`).
- **Browser media coexistence** — the scheduler tick defers if a browser video is playing
  (`App.BrowserMedia.ShouldDeferInterruptions` / `WebVideo` interaction) and short-retries in 30s
  (`SkipRetrySeconds`, `:1466`) rather than stacking mandatory audio on top (BUG-XRFQH4AHDN).
- **OS session events** — `Start()` subscribes to `SessionSwitch` (lock) and `PowerModeChanged`
  (suspend); either force-cleans an active video so it doesn't survive Win+L / sleep in a broken
  state (`:865`, `:892`).

---

## 3. The playback lifecycle (LibVLC)

### 3a. LibVLC init (shared, static, retire-able)
- One process-wide native core load (`Core.Initialize`, `:506/513`, guarded by `_coreLoaded` so a
  retire never re-inits the core). One managed `LibVLC` instance (`:523`) with flags:
  `--no-video-title-show --no-osd --gain=1.0 --no-disable-screensaver --no-mouse-events
  --no-keyboard-events --verbose=-1`. **No `--aout` is forced** — DirectSound silently fails to bind
  on some Win11 26200 boxes, so LibVLC auto-picks mmdevice/WASAPI (`:520`).
- Init is deferred to first play (`EnsureLibVLCInitialized`, `:449`) but pre-warmed in the background
  at startup (`PreloadLibVLC`, `:434`). `WaitForLibVLC(ms)` (`:409`) blocks on a `TaskCompletionSource`
  that is **recreated on every retire** so waiters wait for the rebuild, not the stale first init.
- `SharedLibVLC` (`:361`) is the static other consumers read (BubbleCount, `InlineLoopVideo`, Deeper
  previews). A retire nulls it and rebuilds off-thread (`:650`).

### 3b. Trigger → play (the happy path)
1. `TriggerVideo` (`:935`) — re-entrancy guards (`_triggerInProgress`, `_isCleaningUp`), chaos-rain
   drop-guard (`:964`), InteractionQueue gate, force-clean any stuck prior video, resolve strictness,
   `GetNextVideo()`.
2. Optional 800ms Bambi-freeze delay, then `PlayVideo(path, strict)` (`:1507`).
3. `PlayVideo` — sets `_videoPlaying`, arms the **wedge watchdog before any window exists** (`:1548`),
   ducks audio, fires `VideoAboutToStart`, then a **1.3s delay** (avatar announce) →
   `StartVideoPlayback` (`:1580`).
4. `StartVideoPlayback` — arms fallback safety timer + max-length cap, `EnsureLibVLCInitialized`
   (on the UI thread, takes `_libVLCLock` — a known freeze suspect, §10), then on the UI thread
   creates one fullscreen `Window` per screen: primary with audio, secondaries muted (subject to
   `ShouldFillSecondaryMonitors`, `:1284`). Fires `VideoStarted`.

### 3c. Per-video state, events, end
- The **primary** (audio-bearing) player is the only one that wires events: `TimeChanged` (watch
  position + `PrimaryPlaybackTimeMsChanged`), `LengthChanged` (→ `StartSafetyTimer(duration)`;
  also the chaos random-segment seek), `EndReached`, `EncounteredError`, `Playing`, `Vout`.
- `EndReached`/`EncounteredError` **immediately detach from the LibVLC callback thread via
  `Task.Run`** (LibVLC waits for handlers to return before it can be stopped → deadlock if you
  Stop/Dispose inline) and then `dispatcher.BeginInvoke(OnEnded)` (`:1892`).
- `OnEnded` (`:2986`) evaluates the attention result (§5), then either loops (retry/troll) or falls
  through to `Cleanup` (`:4044`). `Cleanup` = stop timers, `CloseAll`, resume flashes/bubbles,
  deferred subliminal reset, `Complete(Video)`, raise `VideoEnded`, `ScheduleNext`.
- **`CloseAll` (`:3716`) is the teardown funnel** and the single most dangerous method. It sets
  `_isCleaningUp`, `FinalizeWatchCredit`, stops every player **off-thread in parallel with a 500ms
  wait**, then up to ~4s more of **pumped** waiting for stragglers (detaching a still-rendering
  VideoView is the multi-monitor freeze), detaches VideoViews, closes windows, tears down blur
  surfaces, and **quarantines (never disposes) any player whose `Stop()` wedged** (§6). It releases
  the audio-duck ref in `finally`. Every phase is `VideoDiag`-timestamped.

---

## 4. Render paths (three of them) + multi-monitor

`CreateLibVLCVideoWindow` (`:1742`) picks the render surface per screen:

1. **Blurred-background composite (DEFAULT in 6.5.0, `VideoBlurredBackgroundEnabled`)** — a
   `BlurVmemSurface` (`:2200`): LibVLC memory (vmem) callbacks fill a native buffer; a
   `CompositionTarget.Rendering` tick blits it into a `WriteableBitmap` shown twice — a Gaussian-blurred
   `UniformToFill` fill behind + a sharp aspect-fit video in front — so an aspect-mismatched clip fills
   the bars with a blurred copy of itself (TikTok/Shorts look). The blur fill auto-hides when the video
   already matches the screen aspect (`needsBlur`). Buffer dims come from `ComputeBufferDims`
   (long side capped 1080). Frame-liveness guarded by `StartBlurFrameWatchdog`.
   **#786 — never size the picture off the vmem buffer.** The format callback reports the *coded*
   frame size with the sample-aspect-ratio dropped, and LibVLC re-reports it mid-setup with different
   numbers (640x368 x4, then 640x386). `BlurVmemSurface.ApplyGeometry` therefore computes an explicit
   fit rect (`FitToAspect`) from the **display** aspect — `DisplayAspectFrom(track W, H, SarNum,
   SarDen, rotated)`, pushed in from `Playing`/`ESSelected`/`Vout`, with the buffer aspect only as a
   fallback — and re-runs on every format callback, aspect update and resize. The sharp layer is
   `Stretch.Fill` inside that rect, so an anamorphic clip can never be drawn stretched.
2. **Classic `VideoView` (HwndHost airspace)** — used when blur is off. A native child HWND; a
   transparent WPF click overlay sits above it to catch clicks (LibVLC's child window bypasses WPF
   events). Guarded by `StartVoutWatchdog`.
3. **`MediaElement` fallback** (`CreateMediaElementVideoWindow`, `:2511`) — only when `_libVLC` is
   null. Requires Windows Media Foundation codecs; shows the one-time codec-missing warning on
   `MediaFailed`. **No `PrimaryMediaPlayer`, so Deeper enhancements can't attach** (bridge logs and
   bails, `VideoEnhancementBridge.cs:77`). `CreateMirrorVideoWindow` (`:2610`) mirrors it to
   secondaries via `VisualBrush` (avoids a second decode stream).

**Decode default = SOFTWARE.** `EnableHardwareDecoding` is off unless `VideoForceHardwareDecoding`
(`:1809`), plus a belt-and-suspenders media option `:avcodec-hw=none` (`:2050`). The HW/DXVA path
intermittently white-screens on Win11 26200 (§10).

**Multi-monitor** — one decoder+window per screen. `ShouldFillSecondaryMonitors` (`:1284`): 1–2
monitors always fill when `DualMonitorEnabled`; **3+ monitors only fill when
`FillAllMonitorsWithVideo`** (avoids N-decoder lag, #389). Secondaries decode `:no-audio` (a second
WASAPI session on the same device desyncs/zeroes audio). `ForceFullScreenBounds` (`:4350`) pins each
window to the monitor's true physical pixels via `SetWindowPos` (PerMonitorV2 DPI correctness on
mixed-DPI setups). `DualMonitorVideoService` is a **different** single-decoder approach used only for
URL/browser fullscreen.

---

## 5. Attention checks (the "forced to watch" mini-game)

Gated by `AttentionChecksEnabled`. `SetupAttention` (`:2748`) runs 2s after start: schedules
`AttentionDensity` targets (or a random 1..N if `RandomizeAttentionTargets`) across the clip with a
3s min gap, stopping ~8s before the end. A 20ms `_attentionTimer` spawns each at its time
(`CheckSpawnTargets`/`SpawnTarget`, `:2799/2810`).

- Each target is a `FloatingText` (`:4500`) — a transparent, tool-window, topmost bouncing window
  with an outlined text label pulled from `AttentionPool`. It re-asserts `HWND_TOPMOST` every ~32ms
  so it stays above the video. On dual-monitor, a target spawns on **every** screen simultaneously;
  catching any one clears the whole batch (`hitRegistered`, `:2831`).
- `Hit()` (`:4895`) is the idempotent hit path (pop sound + `onHit` + fade), shared by mouse-click
  and **gaze-click** (`VideoGazeClickEnabled` → `GetGazeTargets`/`GazeClick`, `:383/399`).
- **The judgement** is in `OnEnded` (`:3007`): `passed = _hits >= _spawned`. Pass → XP bonus, and a
  **10% troll roll** that replays anyway ("GOOD GIRL! WATCH AGAIN 😜"). Fail → replay with a
  "DUMB BAMBI! TRY AGAIN" message. After 3 fails, `MercySystemEnabled` shows a mercy message and lets
  the video end. Each retry picks a **fresh** video (`GetNextVideo`, `:3057`) so timing can't be
  memorised, and preserves the InteractionQueue slot.

---

## 6. Watchdogs & self-heal (the expensive machinery)

Four independent guards, because the failure modes are genuinely different. Read the big comment
block at `VideoService.cs:55–110`.

1. **Safety timers (duration guillotine)** — `StartFallbackSafetyTimer` (`:3236`, fixed
   `MaxVideoFallbackSeconds` = 600s) arms immediately in case `LengthChanged` never fires; replaced by
   `StartSafetyTimer(duration)` (`:3159`, duration+5s) once the real length is known. If a Deeper
   enhancement is driving (`_enhancementDriving`), it switches from a one-shot guillotine to a
   **progress-based stall watch** (rechecks every 15s, force-ends only after 90s of zero progress) so a
   loop/hold isn't cut (#536). The stall watch reads `GetCurrentPlaybackTimeMs()`, so it works on
   **both** engines, and an **unknown** clock (`-1`) counts as *no progress*, not as progress — reading
   `_primaryMediaPlayer.Time` directly and treating `-1` as a fresh sample made the force-close
   unreachable on the browser engine, where that player is null by design (#874). Note that once
   `StartSafetyTimer` runs it **nulls the fallback timer**, so for an enhancement-driven clip this stall
   watch is the *only* remaining guillotine — a hung WebView2 renderer raises no `ProcessFailed` and its
   page-side watchdogs die with it. `StartMaxLengthCapTimer` (`:3263`) separately enforces the user's
   `VideoMaxDurationSeconds` as a wall-clock cap (the selection filter is best-effort, #584).
2. **Vout (video-output) watchdog** — `StartVoutWatchdog` (`:3298`), a **threadpool** timer. If no
   video output exists `VoutGraceMs` (8s) after `Play()`, the screen is white regardless of decode
   health (software decode did NOT fix this — it's output-side). `VoutWatchdogFire` (`:3342`):
   audio-only files (no video track) are let play out; otherwise **retire the shared LibVLC instance +
   replay the same clip once on a fresh one**. `VoutMidPlayTick` (`:3445`, polls every 2s) catches a
   vout that appeared then **vanished mid-clip** (#600) — pure decision extracted to
   `EvaluateVoutMidPlay` (`:3419`) for unit tests. Both heals go through `DispatchVoutHeal` (`:3495`),
   which is `_teardownGeneration`-guarded so a racing teardown can't resurrect a stopped video.
   The blurred path has no vout HWND, so it uses `StartBlurFrameWatchdog` instead (skip-to-next, no
   retire).
3. **UI-thread wedge watchdog** — `StartWedgeWatchdog` (`:3558`), armed in `PlayVideo` **before any
   window exists**. A `DispatcherTimer` heartbeat bumps `_uiHeartbeatTicks`; a **threadpool** timer
   (`WedgeWatchdogTick`, `:3593`) notices when the heartbeat goes stale for `WedgeStallMs` (22s) — the
   multi-minute-lockout reports — and off-thread `Stop()`s the players, retires the instance, and posts
   a teardown for when the dispatcher drains. **The DispatcherTimer-based guards are useless in exactly
   this scenario** (see §10).
4. **Retire + quarantine (the poisoning fix, #559)** — `RetireSharedLibVLC` (`:599`) quarantines the
   suspect `LibVLC` (roots it forever, never disposes) and rebuilds a fresh one; `QuarantineNative`
   (`:575`) roots a player whose `Stop()` wedged (disposing it is the exact step that poisons the shared
   instance → "one bad teardown, then every video white-screens"). A **circuit breaker**
   (`MaxLibVLCRetiresPerSession` = 4, `:105`) stops retiring after 4 so a chronically-failing machine
   doesn't leak unbounded instances.

---

## 7. The Deeper enhancement layer

`VideoEnhancementBridge` (`Services/Deeper/VideoEnhancementBridge.cs`) makes mandatory + asset-folder
videos drivable by the same effect engine the standalone Deeper player uses. Flow:

1. On `VideoStarted` (UI thread), `BindForCurrentVideo` (`:46`): bail unless `VideoEnhanceIfPossible`;
   `EnhancementResolver.ResolveForLocalMedia(LastVideoPath)`; if there is NO playback clock at all —
   no `PrimaryMediaPlayer` **and** no browser session (i.e. the MediaElement fallback) — log-and-bail
   (#874: browser sessions bind like LibVLC ones; the old `PrimaryMediaPlayer == null` bail kept every
   browser-routed mp4/m4v/webm enhancement silently dead 6.7.0→6.7.4); load the `.ccpenh.json` into
   its **own** `EnhancementHostService`; build a `VideoServiceTimeSource` and `Bind` it (attach/detach
   lambdas capture *this* source so a fast next video can't stale-detach the new one).
2. It calls `_video.SetEnhancementDriving(true)` so the safety timer switches to stall-watch mode (§6).
3. `VideoServiceTimeSource` exposes time/seek/pause/play + `GetVideoRect` (contain-fit rect for gaze
   rules, `:105`) + SAR-corrected aspect. Engine-agnostic (#874): duration, playing-state and aspect
   read `VideoService.GetPrimaryDurationSeconds` / `IsPrimaryMediaPlaying` / `BrowserVideoAspect`, so
   the same source drives both engines (browser aspect comes from the page `meta` frame size; the page
   renders `#fg` contain-fit full-window, the same geometry `FitContain` models). It marshals
   LibVLC-thread `TimeChanged` to the UI thread (`:47`) because the engine mutates band/fired-state
   from the UI tick and webcam handlers (browser `time` messages already arrive on the UI thread and
   pass straight through the same marshal).
   **Browser playing-state is never inferred from message arrival.** `doPause()`, the `seeked` handler
   and the natural end all *force* a `time` post while the clip is paused, so the page stamps a
   `paused` flag on every position and `OnBrowserTime` sets `_browserPaused` **from that field**
   (absent ⇒ leave the `PausePrimary`/`PlayPrimary` edge alone; old cached page HTML). Clearing it on
   any `time` message made a paused clip read as playing one message-hop after `PausePrimary` — which
   is exactly the window Deeper's own speak-holds live in (#874).
4. On `VideoEnded` → `Unbind` (`:136`); clears the driving flag, unloads. **Panic/`CloseAll` does NOT
   raise `VideoEnded`**, so the panic path calls `ForceUnbind()` (`:134`) or active overlays keep
   dispatching after the video is gone (#364).
   **Teardown ordering vs. completion credit:** `CloseAll` clears `_browserActive` and nulls
   `_primaryMediaPlayer` *before* `Cleanup` raises `VideoEnded`, so by the time `EnhancementEngine.Stop`
   runs neither engine is live. `GetPrimaryDurationSeconds` therefore falls back to the last known
   `_duration` — without it the engine's duration-less completion fallback (`EnhancementEngine.cs:454`)
   would fire `PlaybackCompleted` for **any** run past 60s, crediting a Deeper completion on skip,
   panic and attention-fail (#874). `_duration` survives teardown by design: only the `PlayVideo`
   prologue and the browser→LibVLC handoff reset it.

`SeekPrimary`/`PausePrimary`/`PlayPrimary` (`:254/275/285`) mirror the operation to **every** screen's
player so a Deeper blink/rewind doesn't desync the monitors (#527).

---

## 8. Settings that gate/tune it (`Models/AppSettings.cs`)

| Setting | Line | Default | Effect |
|---|---|---|---|
| `MandatoryVideosEnabled` | 753 | **true** | Master toggle; also gates AI/random videos at the sink (#512). |
| `VideosPerHour` | 760 | 6 (1–20) | Scheduler rate; base gap = 3600/N × 0.8–1.2 jitter, min 60s. |
| `StrictLockEnabled` | 767 | false | Video can't be closed; only the panic key exits. Double-warning in the card. |
| `VideoMinDurationSeconds` / `VideoMaxDurationSeconds` | 778 / 785 | 0 (off) | Queue-refill duration filter (best-effort, cold-cache falls open) + a hard wall-clock cap (#584). |
| `AttentionChecksEnabled` | 806 | false | The bouncing-target mini-game. |
| `AttentionDensity` | 813 | 3 (1–10) | Target count. |
| `RandomizeAttentionTargets` | 820 | false | Random 1..N instead of exactly N. |
| `AttentionLifespan` | 827 | 12s | How long each target lives. |
| `AttentionSize` | 834 | 70px (30–150) | Target size. |
| `VideoGazeClickEnabled` | 1645 | **true** | Stare-to-click targets (needs webcam). |
| `MercySystemEnabled` | 1826 | **true** | Let the video end after 3 attention fails. |
| `VideoVolume` | 902 | 50 | Video's own volume (× master). |
| `AudioDuckingEnabled` / `DuckingLevel` | 909 / 916 | true / 80 | Duck other apps while playing. |
| `DualMonitorEnabled` | 1556 | **true** | Fill secondary monitors. |
| `FillAllMonitorsWithVideo` | 1570 | false | Fill secondaries even with 3+ monitors (#389). |
| `VideoBlurredBackgroundEnabled` | 1586 | **true** | TikTok blur-fill render path (vs plain black bars). |
| `VideoForceHardwareDecoding` | 2991 | false | Opt back into GPU/DXVA decode (renamed so old true values reset to SW). |
| `VideoEnhanceIfPossible` | 5099 | false | Apply matching `.ccpenh.json` Deeper effects over the video. |
| `ForceVideoOnLaunch` / `StartupVideoPath` | 791 / 798 | false / null | Play a video on launch. |
| `MandatoryVideosStartMinute` | (session) | — | Delay the scheduler N minutes into a session (`SessionEngine.cs:1079`). |

Validation: `StrictLockEnabled` requires the panic key (`:4753`) and warns if `VideosPerHour > 10`
(`:4758`).

---

## 9. Where to change X

| Want to… | Edit |
|---|---|
| Change the schedule rate/jitter | `ScheduleNext` (`:1441`) |
| Change what "no video" does | `TriggerVideo` empty-path branch (`:1019`); `silentIfEmpty` suppresses the dialog |
| Change video selection / filtering | `GetNextVideo` (`:4097`) + `RefillVideoQueues` (`:4144`); extensions list at `:4146` |
| Add/adjust a render path | `CreateLibVLCVideoWindow` (`:1742`); blur composite = `BlurVmemSurface` (`:2200`) |
| Retune a watchdog | constants at `:86–119` (`VoutGraceMs`, `WedgeStallMs`, `MaxLibVLCRetiresPerSession`, `VoutMidPollMs`, `VoutLostGraceMs`); logic in §6's methods |
| Change attention-check behaviour | `SetupAttention`/`SpawnTarget` (`:2748/2810`), judgement in `OnEnded` (`:3007`), the target itself = `FloatingText` (`:4500`) |
| Change strict-lock / ESC / panic | `SetupStrictHandlers` (`:2685`) |
| Change teardown | `CloseAll` (`:3716`) — the funnel; `Cleanup` (`:4044`) is the natural-end wrapper |
| Change ducking balance | `PlayVideo` Duck (`:1562`) + `CloseAll` finally Unduck (`:3999`); `GetEffectiveVolume` (`:732`) |
| Add a new trigger | call `TriggerVideo`/`PlaySpecificVideo`; respect the InteractionQueue + `MandatoryVideosEnabled` gate (copy `MediaCommand.cs`) |
| Wire a Deeper effect to video | it's automatic via `VideoEnhancementBridge` when `VideoEnhanceIfPossible`; engine time comes from `VideoServiceTimeSource` |
| Add a video setting | property in `AppSettings.cs` §8 region + a control in `VideoFeatureControl.xaml(.cs)` |
| Read diag traces | `logs/video-diag.log` (tags VIDEO/VOUT/WEDGE/HEAL/CLOSE/PANIC/BLUR/UI); `VideoDiag.Tail(n)` for bug reports |

---

## 10. Gotchas (the expensive ones — this feature earned them)

1. **White-screen self-heal is output-side, not decode-side.** Forcing software decode
   (#533/#537/#540) did NOT end the white screens — the player decodes fine but never creates a
   *video output* (#557–#560/#574). The cure is the **vout watchdog → retire+replay** (§6). Don't
   "simplify" it back to just software decode.
2. **Disposing a wedged player poisons the whole pipeline (#559).** A player whose `Stop()` never
   returned is **quarantined, never disposed** — disposing it is the exact step that corrupts the
   shared `LibVLC` and turns one bad teardown into "every later video is a white screen". The owning
   instance is retired too. A few leaked MB beats a poisoned pipeline.
3. **Audio-duck leak (#526).** The `Duck`/`Unduck` pair is balanced in `PlayVideo`/`CloseAll` (not
   `Cleanup`) so the retry/troll loop and engine-stop paths — which tear down WITHOUT `Cleanup` —
   can't ratchet the ref count and pin other apps quiet. `_didDuck` is the one-shot guard.
4. **Deeper-enhanced video cut at raw duration (#536).** An enhancement can loop/hold past the
   declared length; the safety timer must switch to a progress-based stall watch while
   `_enhancementDriving`. The flag is set by the bridge and defensively cleared in `CloseAll`.
5. **The DispatcherTimer guards are dead weight during a freeze (#616–#623).** The safety timers AND
   the blurred-path `StartBlurFrameWatchdog` are `DispatcherTimer`s — useless when the UI thread is
   wedged, which is precisely the multi-minute-lockout scenario. Only the **threadpool-based** wedge +
   vout watchdogs can fire then. The blurred-background composite is the 6.5.0 default and its only
   frame-liveness guard cannot fire during a freeze — this is documented, not yet changed.
6. **`EnsureLibVLCInitialized` runs on the UI thread and takes `_libVLCLock` (#616–#623).** A
   background rebuild (`RetireSharedLibVLC`'s `Task.Run`) can hold that lock across a whole native
   `new LibVLC(...)`. If the last diag line of a frozen session is "libvlc-init begin", the dispatcher
   died waiting on that lock. Bracketed in the trace.
7. **`WriteableBitmap.Lock()` on the UI thread can stall the dispatcher.** The blur composite blits
   inside `CompositionTarget.Rendering` behind a fullscreen Gaussian blur; a render thread that falls
   behind parks the UI thread in `bmp.Lock()`. Per-frame blit uses `TryEnter(8ms)`; the teardown
   `lock` waits forever (top freeze suspect — the trace times it).
8. **Never Stop/Dispose a player inside a LibVLC callback.** `EndReached`/`EncounteredError` must
   `Task.Run` off the callback thread first — LibVLC waits for the handler to return before it can
   stop, so an inline stop deadlocks. Same reason `CloseAll` stops players off-thread and only
   *pump-waits* on the UI thread.
9. **Detaching a still-rendering VideoView is the multi-monitor freeze.** `CloseAll` stops players and
   pump-waits (up to ~4.5s) before touching `vv.MediaPlayer = null`. A `Stop()` that outlives that
   wait is the poisoning signature → retire.
10. **PerMonitorV2 DPI.** A window's Left/Top/Width/Height set before show are realized in the
    creation monitor's DPI; on a different-DPI secondary it lands half-width. `ForceFullScreenBounds`
    re-pins via `SetWindowPos` (physical px) on both `SourceInitialized` and `Loaded`.
11. **Don't start small and Maximize** — that briefly exposed an unpainted white frame (#368). Windows
    are created at full screen bounds up-front (the LibVLC path; the MediaElement fallback still
    maximizes because it's the codec-less legacy path).
12. **`DualMonitorVideoService` ≠ `VideoService` multi-monitor.** The former is a single-decoder
    memory-render service for URL/browser fullscreen; the latter uses one decoder+window per screen.
    Editing one does not change the other.
13. **The scheduler swallows its own throw (#388).** A throw in the tick would hit
    `DispatcherUnhandledException` (logged + marked handled), silently killing all further videos while
    the session runs. The tick re-arms itself in `catch`.
14. **`_teardownGeneration` guards every deferred action.** Watchdog heals, wedge rescues, and
    random-segment seeks snapshot it and abort if it moved — otherwise a stale continuation can
    resurrect a panicked-away video or kill a newer one.

---

## 11. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

- Branch `fix/web-video-interruptions`, HEAD `95586020` (v6.5.0). The recent stability history is on
  `main`/this branch: `db92d814` (triage #614–#627 + hang/handle instrumentation — where `VideoDiag`
  landed), `596ff9d2`/`80f43b5b` (web-video interruption gating), `69336bda` (TikTok blurred
  background), `49bba471` (post-6.4.1 batch incl. mid-play vout #600), `d7fa732d` (SW-decode default),
  `4c56c998`/`b3c4d6af` (the white-screen storm self-heal: vout watchdog + LibVLC retire + wedged-player
  quarantine), `e0a36339` (#536 Deeper-enhanced duration).
- **OPEN / the live gate:** the v6.5.0 **freeze-lockout cluster #616/#617/#621/#622/#623** ("fullscreen
  black or white, app frozen, panic key does nothing, had to hard-reset"). Current state = **fully
  instrumented, not root-caused.** `VideoDiag` was added specifically to capture the next report's
  freeze window; the leading suspects (per the in-code notes) are the blurred-background UI-thread blit
  behind a Gaussian blur (§10.7), `_libVLCLock` contention during a background rebuild (§10.6), and a
  native `Stop()` that never returns. When you get a report, read `logs/video-diag.log` — the last line
  before the silence and the `(ui+Nms)` stall column tell you which phase died.
- **Known unresolved design tension:** the two DispatcherTimer guards (safety + blur-frame) can't fire
  during the very freeze they'd guard against — replacing them with threadpool timers is a behaviour
  change, deliberately deferred (§10.5).
- The MediaElement fallback path is legacy (only when LibVLC fails to init) and can't host Deeper
  enhancements — acceptable, logged, don't "fix" by binding a dead source.
- No dedicated xUnit file for `VideoService` was found in this pass; the only unit-testable seam
  extracted is `EvaluateVoutMidPlay` (`:3419`) — verify current test coverage with a quick grep before
  claiming any.

---

## 12. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```

- Enable in Settings → Videos card; "Test Video" forces one immediately (with a stuck-state reset
  offer). `App.IsEngineRunning` gates live start/stop.
- Watch `logs/video-diag.log` (flush-on-write) alongside `logs/crash.log`. An empty `crash.log` +
  a `video-diag.log` that goes silent mid-teardown = a hang, not a throw.
- libvlc natives must be present next to the exe (`libvlc/win-x64/libvlc.dll` — see the search order
  at `:484`). A missing native drops to the MediaElement fallback (needs Windows codecs).
