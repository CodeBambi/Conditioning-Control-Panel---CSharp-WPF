# BUBBLE COUNT — Feature Primer

> **Purpose.** One-load orientation for the **Bubble Count** minigame — the "watch a video, count the
> bubbles, type the total" challenge — so a future engineer (or Claude) can maintain it WITHOUT
> re-exploring the codebase. §0–1 = what it is (and how it differs from Bubble Pop); §2 = file map;
> **§3 = how it's invoked and how it touches the rest of the app (the load-bearing section)**; §4 = the
> game mechanic (spawn/count/answer/scoring); §5 = the video + multi-monitor render; §6 = strict-lock,
> retries, mercy; §7 = the (now-cosmetic) Level-50 gate; §8 = settings; §9 = where-to-change-X;
> §10 = gotchas; §11 = dated status.
>
> **Freshness.** Read-verified against the code on branch `fix/web-video-interruptions`
> (HEAD `ded7725f`, v6.5.0-era) on **2026-07-23**. §2–§10 track the code and rarely rot; **§11 is a
> dated snapshot — verify with `git log`/`git blame` before acting on it.** The feature's files last
> changed at `6e97cee3` (deep-review round 2) / `55f87208` (the monolith split). Every `file:line`
> below was read when written, but line numbers drift — confirm with a quick read before quoting.

---

## 0. What Bubble Count is, in one paragraph

Bubble Count is the **Level-50-flavored bubble-COUNTING VIDEO minigame**. On a scheduler (or on demand),
it takes over every monitor with a mandatory, borderless, topmost **LibVLC video**; while the clip
plays, soft `bubble.png` orbs briefly appear and auto-pop at scattered positions (they are *decorative
tally marks*, **not** clickable). When the video ends, a result window asks the user to **type the total
number of bubbles they saw**; an exact match awards XP, a wrong answer costs an attempt, and — in strict
mode — a wrong final answer forces a **rewatch of a fresh video**. The whole feature is one small
service, **`BubbleCountService`** (`App.BubbleCount`), plus two multi-monitor `Window` classes
(`BubbleCountWindow` = the video+bubbles stage, `BubbleCountResultWindow` = the answer prompt). It
deliberately reuses **VideoService's shared static `LibVLC`** and is one of the three mutually-exclusive
fullscreen interactions arbitrated by `InteractionQueueService` (Video / BubbleCount / LockCard).

---

## 1. DISAMBIGUATION — this is NOT Bubble Pop

Do not confuse Bubble Count with **Bubble Pop** (`docs/primers/BUBBLE_POP_PRIMER.md`,
`Services/BubbleService.cs`). They share only the word "bubble" and the same `bubble.png` sprite +
`Pop.mp3/Pop2/Pop3` SFX.

| | **Bubble Count** (this doc) | **Bubble Pop** |
|---|---|---|
| Code | `Services/BubbleCountService.cs`, `Windows/BubbleCount*Window.xaml.cs` | `Services/BubbleService.cs` (`App.Bubbles`) |
| Loop | Watch a video, tally auto-popping bubbles, **type the total** | **Click** floating orbs to pop them for XP |
| Interaction | Fullscreen, mandatory, **input-blocking** (one of the InteractionQueue trio) | Ambient, click-through, runs on top of everything |
| Bubbles | Decorative, **not** clickable; auto-pop after ~1–1.5 s | The whole point — user pops them |
| Scoring | Right/wrong answer, XP + attempts + mercy | Per-pop XP, no "answer" |
| Nominal gate | "Level 50" (now cosmetic — §7) | "Level 20" (also cosmetic now — §7) |

The one runtime coupling: **Bubble Count tells Bubble Pop to get out of the way** — `TriggerGame` calls
`App.Bubbles?.PauseAndClear()` before the game and `App.Bubbles?.Resume()` after it
(`BubbleCountService.cs:161`, resume at `:179/:225/:285/:572` etc.), so ambient pop-bubbles don't
pollute the count.

---

## 2. WHERE IT LIVES — file map

| File | Role |
|---|---|
| `Services/BubbleCountService.cs` (~637 lines) | **The orchestrator.** Scheduler, InteractionQueue gate, video-list refill (disk + content packs), XP award + anti-exploit cooldown, strict-mode retry/mercy loop, temp-pack cleanup. Owns `App.BubbleCount`. |
| `Windows/BubbleCountWindow.xaml` / `.xaml.cs` (~1224 lines) | **The stage.** One per screen. Hosts the LibVLC `VideoView`, spawns/animates the decorative `CountBubble`s, tracks watch-time, detects video end, then hands off to the result window. Contains the nested `CountBubble` class (`:1032`) and a `NativeMethods`-style Win32 partial (`:1206`). |
| `Windows/BubbleCountResultWindow.xaml` / `.xaml.cs` (~378 lines) | **The answer prompt.** One per screen (secondaries read-only, mirror the primary's input). Numeric entry, 3 attempts, too-high/too-low hints, awards the "correct answer" XP, and on a non-strict fail shows a **`LockCardWindow`** mercy card. |
| `Features/BubbleCountFeatureControl.xaml` / `.xaml.cs` | The Settings-tab option card (enable / per-hour / difficulty / strict-lock / "Test Now"). Two-way binds to `App.Settings.Current` and live-applies to `App.BubbleCount`. **Note:** the *primary* UI lives in the Progression tab (see below); this UserControl is the Settings-tab twin. |
| `Views/Tabs/ProgressionTabView.xaml` (`:322`+) | The **Level-50 card** (`Level50Locked`/`Level50Unlocked` panels, freq slider, difficulty combo, strict toggle, Test button, `BubbleCountFeatureImage`). Handlers forward to `MainWindow.LevelFeatures.cs`. |
| `MainWindow/MainWindow.LevelFeatures.cs` (`:35`+) | The Progression-tab card's event handlers (enable/freq/difficulty/strict/test) → `App.BubbleCount`. |
| `Models/AppSettings.cs` (`:2747` `#region Bubble Count Game (Unlocks Lv.50)`) | The four settings (see §8). |
| `App.xaml.cs` | `public static BubbleCountService BubbleCount` (`:316`); constructed `BubbleCount = new BubbleCountService();` (`:1423`); `Stop()` on shutdown (`:815`); `Dispose()` (`:3254`). |

There is **no** `CommandData` DTO / `Services/Commands/*Command.cs` for Bubble Count (unlike Flash/Video)
— the AI can't emit a `bubble_count` command; every trigger calls the service directly.

---

## 3. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

Read this before touching anything. The public surface is tiny — `Start` / `Stop` / `TriggerGame` /
`RefreshSchedule` / `RefreshVideosPath` / `ReloadAssets` / `ResetBusyState` / `ForceCleanup` — but the
fan-in and the systems a *running game* touches are wide.

### 3a. Who starts/stops the scheduler (`Start`/`Stop`)

`Start()` at `BubbleCountService.cs:53` (bails unless `BubbleCountEnabled`); `Stop()` at `:74`.

- **Engine start/stop** (`MainWindow.StartStop.cs`): start on engine start if `BubbleCountEnabled`
  (`:190/:192`), `Stop()` on engine stop (`:318`), and the stop path also force-closes any live windows
  (`BubbleCountWindow.ForceCloseAll()` / `BubbleCountResultWindow.ForceCloseAll()`, `:342/:343`).
- **Dashboard cards** — two twins, both live-apply:
  - Progression tab (`MainWindow.LevelFeatures.cs`): enable toggle `Start()/Stop()` when engine running
    (`:47/:51`), freq slider → `RefreshSchedule()` (`:67`), difficulty combo (`:80`), strict toggle
    (double-warning dialog, `:85`+), Test → `TriggerGame(forceTest:true)` (`:122`).
  - Settings tab (`Features/BubbleCountFeatureControl.xaml.cs`): same wiring (`:80/:82`, `RefreshSchedule`
    `:94`, `TriggerGame(forceTest:true)` `:148`).
- **SessionEngine** (AI sessions): starts on session begin if enabled and not deferred
  (`SessionEngine.cs:447`); applies session settings and either starts immediately or
  `DeferFeatureStart("bubble count", BubbleCountStartMinute, …)` (`:1140-1158`); stops at `:412` and
  `:1158`; saves/restores the user's own values around the session (`:854-855`, `:1248-1249`); flags the
  feature for Season Recap (`:234`).
- **Remote control** (`RemoteControlService.cs`): `Stop()` on remote stop (`:812/:915`), force-closes
  windows (`:826/:926`), and can fire a one-shot game via `TriggerGame(forceTest:true)` (`:1138`).

### 3b. Single-shot triggers (`TriggerGame`)

`TriggerGame(bool forceTest = false)` at `BubbleCountService.cs:121`. `forceTest:true` **bypasses the
running/busy checks** so it works with the engine off (the Test buttons, remote, autonomy).

- **The scheduler** (`ScheduleNextGame` `:86`): a `DispatcherTimer` at `3600 / gamesPerHour` seconds
  (±20 % variance, floored at 60 s), re-armed after every tick. Fires `TriggerGame()` when
  `_isRunning && !_isBusy`.
- **Autonomy / Takeover** (`AutonomyService.cs:1305-1308`): a `BubbleCount` action case still exists and
  calls `TriggerGame(forceTest:true)`, **but the action was removed from the candidate pool**
  ("*BubbleCount removed from autonomy - too disruptive and unreliable*", `:1067`), so the autonomy
  path is effectively dead code today. `AppSettings.AutonomyCanTriggerBubbleCount` (`:3693`, default
  true) is likewise vestigial.

### 3c. The InteractionQueue gate (mutual exclusion)

`TriggerGame` routes through `App.InteractionQueue` (`InteractionQueueService`). Video, BubbleCount, and
LockCard are mutually exclusive fullscreen interactions (`InteractionType` enum, `:17`). If another
interaction owns the slot, BubbleCount **queues itself** to re-run when free
(`TryStart(..., queue:true)`, `BubbleCountService.cs:133-140`); if the queue already dequeued *us*
(`CurrentInteraction == BubbleCount`, `:132`) it proceeds. On teardown every exit path calls
`Complete(BubbleCount)`; a wedged game is recovered by the queue's stuck-detector, which calls
`App.BubbleCount?.ForceCleanup()` (`InteractionQueueService.cs:343-344`). The service **extends the
stuck timeout** to cover the whole clip + counting phase (`ExtendTimeout(videoDuration + 120)`,
`BubbleCountService.cs:195`) and again during a strict retry gap (`ExtendTimeout(300)`, `:301`) so a
queued Video can't slip in on top of a retry.

### 3d. What a launching / running game touches

- **Bubble Pop** — `App.Bubbles?.PauseAndClear()` before (`:161`), `Resume()` after (every exit path).
- **Subliminals** — `App.Subliminal?.TriggerBambiFreeze()` fires 800 ms before the game
  (`:164`, then a `Task.Delay(800)` gate at `:167`).
- **Audio** — the primary player is unmuted at `MasterVolume` (`BubbleCountWindow.xaml.cs:461-463`);
  secondaries decode `:no-audio` (`:480`). Pop SFX volume = `(master × bubbles)^1.5`
  (`PlayPopSound`, `:788`). Output device honored via `App.Audio?.ApplyPreferredDevice`.
- **LibVLC** — reuses `VideoService.SharedLibVLC` (never creates its own), waiting up to 5 s and calling
  `App.Video?.PreloadLibVLC()` if it isn't ready (`EnsureLibVLCInitialized`, `BubbleCountWindow.xaml.cs:144`).
- **Progression / XP** — **two** awards on a correct answer:
  1. `BubbleCountResultWindow` awards `ScaleXpByDuration(250)` immediately on a correct answer
     (`BubbleCountResultWindow.xaml.cs:183-184`, `XPSource.BubbleCount`).
  2. `BubbleCountService.OnGameComplete(success:true)` awards `ScaleXpByDuration(100)` **gated by a
     3-minute anti-exploit cooldown** (`GameXpCooldown`, `:42`; award at `:229-234`).
  Both scale down for clips under 60 s (`ScaleXpByDuration`, `:211`).
- **Achievements** — `TrackBubbleCountGameStarted()` on launch (`BubbleCountService.cs:188/:320`);
  `TrackBubbleCountResult(correct)` on answer (`BubbleCountResultWindow.xaml.cs:189/:210`) which drives
  a 5-in-a-row streak achievement (`AchievementService.cs:455-476`); `TrackVideoWatched(duration)` when
  the clip ends (`BubbleCountWindow.xaml.cs:838`).
- **Quests** — `App.Quests?.TrackBubbleCountCompleted()` on success (`BubbleCountService.cs:242` →
  `QuestService.cs:690`, `QuestCategory.BubbleCount`).
- **Events** — `GameCompleted` / `GameFailed` (`BubbleCountService.cs:50-51`) fire on the respective
  outcomes. No in-tree subscribers were found beyond the achievement/quest calls above.
- **Discord** — `DiscordRichPresenceService.SetBubbleCountActivity()` exists (`:212`) but is **never
  called** — Discord presence for this feature is unwired (backlog, §11).
- **Content packs** — pack videos are decrypted to temp files on demand
  (`App.ContentPacks?.GetPackFileTempPath`, `:457`) and cleaned up in `CleanupTempPackFiles` (`:613`).

### 3e. Panic / force-close

The panic key (`MainWindow.xaml.cs:886-887`) and `StopAdHocEffects` (`:922`) call
`BubbleCountWindow.ForceCloseAll()` + `BubbleCountResultWindow.ForceCloseAll()` + `App.BubbleCount?.Stop()`.
`ForceCloseAll` (`BubbleCountWindow.xaml.cs:271`) mirrors VideoService teardown: stop players → pump-wait →
detach `VideoView`s → close windows → async-dispose players → `App.BubbleCount?.ResetBusyState()`.

---

## 4. THE GAME MECHANIC — spawn, count, answer, score

### 4a. Target count (`CalculateTargetBubbles`, `BubbleCountWindow.xaml.cs:566`)
`baseRate` by difficulty (Easy **3** / Medium **5** / Hard **8**) is per 30 s of video, scaled to the
real clip duration, ± 20 % variance, floored at 3:
`target = round((baseRate/30) × durationSec + jitter)`, `Math.Max(3, …)`.

### 4b. Spawning (`StartBubbleSpawning` `:643`, `SpawnBubbleOnAllWindows` `:688`)
Only the **primary** window spawns. A `DispatcherTimer` at `(durationSec×1000 / target) × 0.7` ms
(after a 1.5 s layout-settle delay) spawns bubbles until the **shared** `_sharedBubbleCount` reaches
`_targetBubbleCount`. Each spawn picks one random open window and places a bubble at a random relative
position; `_sharedBubbleCount` is the authoritative running total (static, shared across monitors).
Bubbles are **decorative** — there is no click handler; they are `IsHitTestVisible = false`.

### 4c. The `CountBubble` primitive (`:1032`)
Each bubble is its **own** transparent, click-through, topmost, tool-window `Window` (so it never blocks
the LibVLC `VideoView`'s airspace — same rationale as VideoService's `FloatingText`). It grows in, holds
1000–1500 ms, then auto-pops (scale-up + fade + spin) and plays a pop SFX (`StartPopping` `:1184`).
**All** bubbles on a window are driven by **one shared `_bubbleAnimTimer`** at 30 ms
(`EnsureBubbleAnimTimer` `:737`, `AnimateAllCountBubbles` `:750`) — this replaced a prior ~2-timers-per-bubble
model (up to ~24 concurrent `DispatcherTimer`s). Finished bubbles are reaped and `Dispose()`d (which
`Close()`s the window); the timer self-stops when none remain.

### 4d. Answer & scoring (`BubbleCountResultWindow`)
When the video ends (`OnVideoEnded` `:805`), the stage windows hide and
`BubbleCountResultWindow.ShowOnAllMonitors(_sharedBubbleCount, …)` opens the numeric prompt (correct
answer = the final `_sharedBubbleCount`). The primary window accepts digits only; secondaries mirror it
read-only (`OnTextChanged` sync, `:133`). `CheckAnswer` (`:168`):
- **Exact match** → award XP (§3d), green "CORRECT" feedback, disable input, `CompleteAll(true)` after 2 s.
- **Wrong** → decrement from **3 attempts**, show a too-high/too-low hint, break the streak.
- **Out of attempts** → strict: `CompleteAll(false)` (service handles retry/mercy, §6); non-strict:
  `ShowMercyCard()` (§6).

`CompleteAll(success)` closes every result window and invokes the `onComplete` callback, which lands in
`BubbleCountService.OnGameComplete` (`:219`).

---

## 5. THE VIDEO + MULTI-MONITOR RENDER

`BubbleCountWindow.ShowOnAllMonitors` (`:183`) builds **one window per screen** (all screens if
`DualMonitorEnabled`, else primary only), primary with audio, secondaries muted via `:no-audio`
(a second WASAPI session on the same device desyncs/zeroes audio — see the comment at `:477-480`).
Each window creates its own `LibVLCSharp.WPF.VideoView` + `MediaPlayer` off the **shared static
`_libVLC`** (from VideoService). Windows are created small then `WindowState = Maximized` + forced
`HWND_TOPMOST` + `WS_EX_TOOLWINDOW` (hidden from Alt-Tab). Per-screen DPI is handled manually
(`GetDpiForScreen` `:991`, physical-px → DIP division) for mixed-DPI correctness.

- **Video source**: `BubbleCountService.GetRandomVideo` (`:419`) draws from a shuffled pool refilled by
  `RefillVideoLists` (`:494`) = disk videos under `EffectiveAssetsPath/videos` (recursive,
  security-validated, disabled-asset-filtered — same normalization as Flash/Video) **plus** active
  content-pack videos (decrypted to temp). Valid ext: `.mp4/.webm/.avi/.mkv/.mov/.wmv`.
- **Duration** is read via NAudio `MediaFoundationReader` (`GetVideoDuration` `:609`, falls back to 30 s),
  and stashed in `LastVideoDurationSeconds` (static) for XP scaling.
- **End detection**: primary `MediaPlayer.EndReached`/`EncounteredError` (detached off the LibVLC
  callback thread via `Task.Run` — inline stop/dispose deadlocks LibVLC) → `OnVideoEnded`. A
  **safety timer** (duration + 5 s, `StartSafetyTimer` `:622`) force-ends if the events never fire.

---

## 6. STRICT LOCK, RETRIES & MERCY

- **Non-strict** (default): Escape closes the game any time (`OnKeyDown` `:866` / result `:155`); the
  `TxtEscHint` is shown, `TxtStrict` hidden. Running out of attempts in the result window shows a
  **mercy `LockCardWindow`** ("type twice", `ShowMercyCard` `:279`) using mod-aware
  `GetPhrases("BubbleCountMercy")` phrases — deliberately **without the answer in the text** — then
  completes as a fail.
- **Strict lock** (`BubbleCountStrictLock`, guarded behind a `WarningDialog.ShowDoubleWarning` in both
  cards): no Escape; a final wrong answer signals failure to the service, which **replays a fresh random
  video** (`RetryGame` `:293`) with a "WRONG! WATCH AGAIN" fullscreen message
  (`ShowFullscreenMessage` `:335`). After **3 retries**, if `MercySystemEnabled`, the service shows a
  mercy message and lets the user go (`OnGameComplete` `:254-268`).
- A subtle correctness guard: the mercy-card poll bails if `_allWindows` was emptied by a concurrent
  `ForceCloseAll` (panic/engine-stop), so a strict fail can't resurrect a fullscreen game seconds after
  "stop everything" (`BubbleCountResultWindow.xaml.cs:310-315`).

---

## 7. THE LEVEL-50 GATE (now cosmetic)

Historically Bubble Count unlocked at **Level 50**. **Feature level-gating has been removed** — see the
explicit comment in `MainWindow.UiUpdates.cs:603`: *"Feature level gating has been removed — every
feature is available from level 1."* `UpdateUnlockablesVisibility` unconditionally flips
`Level50Locked → Collapsed` / `Level50Unlocked → Visible` and unblurs `BubbleCountFeatureImage`
(`:622-624`), and the Settings-tab `CardBubbleCount.IsLocked = false` (`:643`). The **only remaining
"Level 50" presence is chrome**: the Progression card still shows the `label_lvl_50` badge and a
`Level50Locked` panel (`ProgressionTabView.xaml:322-347`) that is never shown. The functional gate is now
just `BubbleCountEnabled` + (for the scheduler) the engine running; `TriggerGame(forceTest:true)` bypasses
even that. Do not add level checks back without checking this comment first.

---

## 8. SETTINGS REFERENCE (`Models/AppSettings.cs`, `#region Bubble Count Game (Unlocks Lv.50)` `:2747`)

| Setting | Line | Default | Purpose |
|---|---|---|---|
| `BubbleCountEnabled` | `:2750` | false | Master on/off. |
| `BubbleCountFrequency` | `:2757` | 2 | Games/hour, clamped **1..10** (scheduler gap = 3600/N ±20 %, min 60 s). |
| `BubbleCountDifficulty` | `:2764` | 1 | 0=Easy / 1=Medium / 2=Hard (clamped 0..2) → base bubbles-per-30 s (3/5/8). |
| `BubbleCountStrictLock` | `:2771` | false | Can't skip; wrong final answer forces a rewatch (mercy after 3 retries). Double-warning to enable. |

Related (not in the region): `MercySystemEnabled` (shared with Video — grants the 3-retry escape),
`DualMonitorEnabled` (spread windows across screens), `MasterVolume` / `BubblesVolume` (pop SFX),
`DisabledAssetPaths` (video filtering), `AutonomyCanTriggerBubbleCount` (`:3693`, vestigial — §3b).
Session-scoped: `Session.BubbleCountEnabled` / `BubbleCountFrequency` / `BubbleCountStartMinute`
(applied/deferred in `SessionEngine`, §3a).

---

## 9. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Change the schedule rate/variance | `ScheduleNextGame` (`BubbleCountService.cs:86`). |
| Change difficulty → bubble count | `CalculateTargetBubbles` (`BubbleCountWindow.xaml.cs:566`). |
| Change spawn cadence / distribution | `StartBubbleSpawning` (`:643`) + `SpawnBubbleOnAllWindows` (`:688`). |
| Change bubble look / lifespan / pop anim | `CountBubble` (`:1032`), lifespan at `:1126`, pop math in `Tick` (`:1133`). |
| Change the answer/scoring/attempts | `BubbleCountResultWindow.CheckAnswer` (`:168`); attempt count = `_attemptsRemaining` init (`:24`). |
| Change XP amounts | correct-answer XP `BubbleCountResultWindow.xaml.cs:183` (250); completion XP `BubbleCountService.cs:231` (100); scaling `ScaleXpByDuration` (`:211`); cooldown `GameXpCooldown` (`:42`). |
| Change strict retry / mercy behavior | `BubbleCountService.OnGameComplete` (`:219`) + `RetryGame` (`:293`) + `ShowFullscreenMessage` (`:335`); result-side mercy card `BubbleCountResultWindow.ShowMercyCard` (`:279`). |
| Change the pop SFX | `PlayPopSound` (`BubbleCountWindow.xaml.cs:771`); files in `Resources/sounds/bubbles/`. |
| Change video selection / extensions | `GetRandomVideo` (`:419`) / `RefillVideoLists` (`:494`); ext list at `:502`. |
| Change multi-monitor / render | `BubbleCountWindow.ShowOnAllMonitors` (`:183`) + `OnLoaded` (`:388`). |
| (Re)add a level gate | `MainWindow.UiUpdates.cs:UpdateUnlockablesVisibility` (`:599`) — read the "gating removed" comment first (§7). |
| Add a setting | `AppSettings.cs` region (`:2747`) + a control in both `ProgressionTabView.xaml` and `BubbleCountFeatureControl.xaml`. |
| Wire a new trigger | `App.BubbleCount.TriggerGame(forceTest:true)` — respect the InteractionQueue (copy the pattern in `TriggerGame`). |

---

## 10. GOTCHAS (the expensive ones)

1. **Never Stop/Dispose a LibVLC player inside its callback.** `EndReached`/`EncounteredError` detach
   off the callback thread via `Task.Run` before marshalling to the dispatcher
   (`BubbleCountWindow.xaml.cs:503-538`) — an inline stop deadlocks LibVLC (identical rule to
   `VideoService`; cross-ref `MANDATORY_VIDEOS_PRIMER.md` §10.8).
2. **Teardown pump-waits on the UI thread.** `ForceCloseAll`/`CloseAllWindows` stop players, then
   `WaitWithMessagePump` (`:970`) before detaching `VideoView`s and closing windows, then dispose players
   async after 750 ms. Detaching a still-rendering `VideoView` is the multi-monitor freeze — don't remove
   the waits.
3. **Bubbles are separate windows, not visual-tree children.** Each `CountBubble` is its own
   `AllowsTransparency` topmost window so it can't steal the LibVLC child-HWND's airspace. They must be
   `Dispose()`d (which `Close()`s them) or the HWNDs leak; `OnVideoEnded` and `OnClosed` both drain them.
4. **One shared 30 ms anim timer, not per-bubble.** `AnimateAllCountBubbles` iterates by reverse index
   with a bounds re-check (`:752-754`) because bubbles are reaped mid-iteration. It self-stops at zero
   and restarts on the next spawn — don't reintroduce per-bubble timers (~24 concurrent was the old bug).
5. **Two XP awards, one cooldown.** A correct answer pays 250 XP *every time* (result window) **plus**
   100 XP gated by a 3-minute cooldown (service). If you're "fixing" a double-XP report, that's by design
   — only the 100 is throttled.
6. **The Level-50 gate is cosmetic** (§7). Adding a real check silently contradicts the "gating removed"
   design decision.
7. **Screen enumeration must be guarded.** `ShowOnAllMonitors` / `ShowFullscreenMessage` use
   `App.GetAllScreensCached()` and null-check `screens.Length == 0` before use — cross-ref root
   `CLAUDE.md` #5 (screen enumeration can return empty). Per-screen DPI is computed manually.
8. **Fire-and-forget continuations are guarded.** The `Task.Delay(...).ContinueWith(...)` blocks
   (`BubbleCountService.cs:167`, `BubbleCountWindow.xaml.cs:666/:383`) marshal via `DispatcherHelper`/
   check `dispatcher.HasShutdownStarted` — the standard CCP async rule (root `CLAUDE.md` #6–8).
9. **Strict retry can resurrect a game after "stop everything".** The mercy poll bails on an empty
   `_allWindows` (`BubbleCountResultWindow.xaml.cs:310-315`) precisely to stop a strict `OnGameComplete(false)`
   from relaunching a fullscreen game right after a panic/engine-stop. Keep that guard.
10. **LibVLC is borrowed, never owned.** `EnsureLibVLCInitialized` always re-reads
    `VideoService.SharedLibVLC` (which can be *retired/rebuilt* by VideoService's white-screen self-heal)
    and waits up to 5 s. If a bubble-count game white-screens, suspect the *shared* LibVLC state, not this
    feature — see `MANDATORY_VIDEOS_PRIMER.md` §6.

---

## 11. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

- **State: mature and shipping**, no dedicated in-flight branch. HEAD `ded7725f` on
  `fix/web-video-interruptions` (v6.5.0-era). The feature's files last changed at `6e97cee3`
  (deep-review round 2 — queue dispatch / reentrancy hardening) and `55f87208` (the monolithic
  code-behind split that moved handlers into `MainWindow.LevelFeatures.cs`).
- **Known dead / unwired paths** (candidates for cleanup, not bugs): autonomy `BubbleCount` action
  exists but is removed from the candidate pool (`AutonomyService.cs:1067`); `AutonomyCanTriggerBubbleCount`
  setting is vestigial; `DiscordRichPresenceService.SetBubbleCountActivity()` (`:212`) is defined but
  never called (no Discord presence for this feature).
- **The "Level 50" label is cosmetic** (§7) — the Progression card still renders the badge and a
  never-shown `Level50Locked` panel.
- **No dedicated unit tests** cover `BubbleCountService` or the windows; the standing gate is play-test.
  Because it shares VideoService's LibVLC and the InteractionQueue, treat it as covered by the video
  freeze-cluster instrumentation (`logs/video-diag.log`) rather than its own harness.
- **No memory entry** exists for Bubble Count in `memory/MEMORY.md` (as of this snapshot) — this primer
  is the first consolidated doc. The sibling `BUBBLE_POP_PRIMER.md` §1 already points here for the
  disambiguation.

---

## 12. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```
Then: drop videos into `%APPDATA%/ConditioningControlPanel/assets/videos`, open the **Bubble Count** card
(Progression tab, or the Settings-tab twin), enable it, and hit **Test Now** — `TriggerGame(forceTest:true)`
fires immediately regardless of engine/level state. Multi-monitor requires `DualMonitorEnabled`. LibVLC
natives must be present next to the exe (shared with VideoService).
