# SUBLIMINALS — Feature Primer

> **Load this instead of re-exploring the feature.** One-load orientation for the Subliminals
> feature — the periodic full-screen text/word flash. @-mention this file for coding or design
> sessions. §0 = what it is in one paragraph. §1 = disambiguation (four things flash text — read
> first). §2 = file map. §3 = the render paths (the load-bearing architecture decision). §4 = **how
> it's invoked & how it touches the rest of the app** (the call graph — read before wiring a new
> trigger). §5 = text sources + the Bambi Freeze/Reset ritual + audio whispers. §6 = the
> `GetActiveTextScreenRects` OCR contract (why other subsystems query live subliminal rects). §7 =
> the **Bouncing Text** sibling service. §8 settings. §9 where-to-change-X. §10 gotchas. §11 status
> (dated). §12 build/run.
>
> **Freshness.** Tracks the code as of **2026-07-23** (branch `fix/web-video-interruptions`, HEAD
> `95586020`, v6.5.0). §2–§10 track the code and rarely rot; `file:line` refs were read-verified
> when written, but confirm a line with a quick read before quoting. **§11 is a dated snapshot —
> verify with `git log`/`git branch` before acting on it.**

---

## 0. What Subliminals is, in one paragraph

Subliminals flashes short, large, outlined **text** ("GOOD GIRL", "OBEY", user-defined phrases)
briefly and centered on every monitor at a scheduled cadence — the classic subliminal-message
conditioning loop, the text sibling of Flash Images. It is a **pure C# / WPF service**,
`Services/Subliminal/SubliminalService.cs`, owned as the singleton **`App.Subliminal`** and driven
by a `DispatcherTimer`. Each flash picks one enabled phrase from `Settings.SubliminalPool`,
optionally plays a matching **whispered audio clip** and a haptic pattern, then fades text in →
holds → out over one of **three interchangeable render paths** (the modern compositor **Skia
layer**; a **solid-mode** card on the shared click-through host; or the legacy **per-screen
keep-alive layered window**). It carries a special ritual — **Bambi Freeze → Bambi Reset** — used
before mandatory videos and minigames, and it deliberately stays in screen recordings while
exposing its live text rects (`GetActiveTextScreenRects`) so the companion's awareness OCR can skip
its own words. It is triggered by ~a dozen subsystems (the scheduled loop, sessions, the AI, chaos,
Deeper, autonomy, voice, remote, quiz). Do not confuse it with **Bouncing Text** (§7), a separate
Level-60 DVD-corner service in the same folder.

---

## 1. DISAMBIGUATION — four things flash CCP text (read this first)

| Feature | Code | What it is |
|---|---|---|
| **Subliminals** (this doc) | `Services/Subliminal/SubliminalService.cs` (`App.Subliminal`) | Periodic centered text flash, fade in/hold/out, per-monitor. |
| **Bouncing Text** (§7 here) | `Services/Subliminal/BouncingTextService.cs` (`App.BouncingText`) | DVD-screensaver text that bounces edge-to-edge, awards XP on corner hits. Lv.60. Same folder, **separate service**, window-only. Covered in §7 because it shares the OCR contract and the folder. |
| **Web subliminals** (Graded Intake) | `Resources/web/intake/render/subliminals.js` | The intake web core's *own* DOM/canvas subliminal bed. Reads the same `SubliminalPool` keys (shipped in BootConfig) but is **fully self-contained** — zero coupling to `App.Subliminal`. See `Resources/web/intake/CLAUDE.md`. |
| **Deeper subliminal effect** | `Services/Deeper/IActionDispatcher.cs:360` | A Deeper timeline item that *calls into* `App.Subliminal.FlashSubliminalCustom(...)` — a trigger, not a separate renderer. Covered in §4. |

Everything below §1 is about **`App.Subliminal`** unless the header says Bouncing Text.

---

## 2. Where it lives — file map

| File | Role |
|---|---|
| `Services/Subliminal/SubliminalService.cs` (~1,370 lines) | **The whole engine.** Scheduling, phrase pick, linked-audio search/playback, haptic-anticipation timing, the Bambi Freeze/Reset ritual, all three render paths, the OCR rect accessor, Win32 styling. |
| `Services/Compositor/SubliminalLayer.cs` | **Render path A (default).** A `BaseLayer` Skia draw-list: queued items, per-item 50 ms fade envelope, most-recent-card-wins render, WPF outlined-text parity (8 border offsets, Arial Bold 120 DIP), glyph-fallback (#615), and its own `GetActiveTextRectsPx`. `SubliminalService` owns ALL state; the layer just draws a fully-resolved card. |
| `Chaos/ChaosBubbleHostOverlay.cs` | **Render path B host (solid mode).** The one shared click-through fullscreen host. `SubliminalService` takes a single ref-count on it (`EnsureHostRef`/`ReleaseHostRef`, `:886`/`:894`) and adds a card per screen. *(Shared with Flash solid-mode + chaos bubbles — treat as the shared host contract.)* |
| `Models/CommandData/Subliminal.cs` | The AI command DTO: `record Subliminal(string Text, int Opacity)`. |
| `Services/Commands/SubliminalCommand.cs` | Executes an AI `subliminal` command → clamps (`MaxOpacity 60`, `MaxTextChars 80`, `:10-11`) → strips HTML-ish text → `App.Subliminal.FlashSubliminalCustom`. |
| `Features/SubliminalFeatureControl.xaml(.cs)` | The **Subliminals settings card** (dashboard tile). Binds enable/per-min/frames/opacity/whispers/volume/solid-mode to `App.Settings.Current.*`; "📝 Messages" opens `TextEditorDialog` on `SubliminalPool`; "⚙ Advanced" opens `ColorEditorDialog` (colors + steal-focus). No subliminal *logic* lives here. |
| `Services/Subliminal/BouncingTextService.cs` | **The Bouncing Text sibling** (§7). `App.BouncingText`. Includes the internal `BouncingTextWindow`. |
| `Features/BouncingTextFeatureControl.xaml(.cs)` | Bouncing Text's settings card. |
| `Services/Commands/BounceCommand.cs` | AI/remote command for Bouncing Text (`Start(true, words)` / `Stop`). |
| `App.xaml.cs` | Declares `public static SubliminalService Subliminal` (`:290`) + `BouncingTextService BouncingText` (`:317`); constructs `Subliminal` at `:1397` and `BouncingText` at `:1424` in `OnStartup`. Also owns `App.CompositorEnabled` (the predicate behind render path A) and `GetActiveTextScreenRects` consumption at `:683` (§6). |
| `Models/AppSettings.cs` | `#region Subliminals` (`:1026`+) and `#region Bouncing Text` (`:2779`+). See §8. |
| `Services/Compositor/CompositorLayers.cs` | Z-order constants: `Subliminal = 40` (`:14`), between `Flash = 30` and `Bubbles = 45`. |

**No `BouncingTextLayer.cs` exists.** `CompositorLayers.BouncingText = 50` (`:16`) is a *reserved*
z-slot; Bouncing Text is still window-only and has **no** compositor render path (§7, §10.9).

---

## 3. The three render paths + how one is chosen

Unlike Flash, a subliminal is **not** a persistent state-bag object — each show is transient. But it
still resolves to one of three render surfaces, decided **per show** inside `ShowSubliminalVisuals`
(`:599`). Precedence: **compositor > solid host > per-screen window**, with each path falling
through to the next on any failure (a subliminal must *never* be silently invisible).

```
// ShowSubliminalVisuals, :599
compositor  = UseCompositor (App.CompositorEnabled) && !SubliminalStealsFocus   // → SubliminalLayer.Flash
solid host  = SubliminalSolidMode && !SubliminalStealsFocus                     // → ShowHostedSubliminal per screen
per-window  = otherwise (or any fallthrough)                                    // → GetOrCreateScreenWindow per screen
```

| | Path A — Compositor (default) | Path B — Solid host | Path C — Per-screen window (legacy) |
|---|---|---|---|
| Entry | `ShowCompositorSubliminal` (`:682`) → `SubliminalLayer.Flash` (`SubliminalLayer.cs:107`) | `ShowHostedSubliminal` (`:724`) | `GetOrCreateScreenWindow` (`:908`) + `AnimateSubliminal` (`:1247`) |
| Visual | one Skia layer item covering ALL screens | one WPF `Grid` card per screen, child of `ChaosBubbleHostOverlay` canvas | one keep-alive `WS_EX_LAYERED` window per screen, content swapped per show |
| Windows created | none | none (shares host) | one per monitor, **kept alive** at Opacity 0 between shows |
| Fade envelope | 50 ms in / hold / 50 ms out, run by the layer on the engine tick (`Item.Envelope`, `SubliminalLayer.cs:74`) | same, WPF `Storyboard` on the card (`:822`) | same, WPF `Storyboard` on the window (`AnimateSubliminal`, `:1247`) |
| Multi-card rule | most-recent-card-wins (`_items[^1]`, `SubliminalLayer.cs:205`) | one card per screen, replaced outright | one content-swap per window, generation-guarded (`_showGeneration`, `:34`) |
| Steal focus? | **no** (host is NOACTIVATE) | **no** | **yes** when `SubliminalStealsFocus` (falls here for that reason) |
| Z / draw | `CompositorLayers.Subliminal = 40` (above Flash 30, below Bubbles 45) | ZIndex 1200 on the host, above hosted flashes (1000) | topmost layered window |
| In recordings? | yes (main surface, `WDA_NONE`) | yes | yes (`WDA_NONE`, `:1042`) |

**Why keep-alive windows (path C):** creating/closing a layered window per subliminal is
render-thread churn that can wedge WPF's single render thread while other layered surfaces animate
(Application Hang 1002 — same class of bug as Flash pooling). So path C keeps one window per screen
**shown at Opacity 0** with null content between flashes and only ever `Close()`s them in
`Dispose()` (`:1320`). `Stop()` (`:105`) blanks + hides them but does **not** close (a Stop can land
mid-chaos-run, and closing layered windows then is the deadlock trigger).

**Why the fallthroughs exist:** the solid host's stage bounds are fixed at creation, so a card can
land off-canvas (host created primary-only before `DualMonitorEnabled` flipped, or `Show()` failed)
— `ShowHostedSubliminal` checks `ChaosBubbleHostOverlay.CoversPoint` (`:791`) and returns `false` so
the caller renders that screen classically. Likewise `ShowCompositorSubliminal` returns `false` on
any exception and the caller drops to solid/per-window.

**Steal focus is the odd one out.** `SubliminalStealsFocus` (an "Advanced" opt-in) requires a
window that can activate; the shared host and compositor are both NOACTIVATE by contract, so setting
it forces path C and calls `win.Activate()` (`:662`).

---

## 4. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

This is the section to read before wiring a new trigger. Two entry styles: **run mode** (the
scheduled loop, gated on `_isRunning`) and **one-shot** (works even when the loop is off via
`_oneShotActive`, set true on entry to `FlashSubliminal`/`FlashSubliminalCustom`, `:197`/`:256`, and
consumed once in `ShowSubliminalVisuals`, `:603`).

### 4a. Public API surface (all on `App.Subliminal`)

| Method | `:line` | Purpose |
|---|---|---|
| `Start()` / `Stop()` | 92 / 105 | Begin/end the scheduled loop. `Stop` blanks windows, pulls hosted cards, clears the layer, stops audio (does **not** close windows). |
| `SetEnabled(bool)` | 146 | **Single authority** for the toggle. Persists `SubliminalEnabled` and, when the engine is running, Start/Stop on an actual transition only (checkbox + popup mirror each other without churning). |
| `FlashSubliminal()` | 195 | Fire one scheduled flash now (picks a random enabled pool phrase). Works with the loop off (sets `_oneShotActive`). Awards 20 XP with audio, 10 without. |
| `FlashSubliminalCustom(text, opacity?, overrideDurationMs?, suppressHaptic?)` | 250 | **The main on-demand entry.** Sanitizes + caps text at 200 chars, strips tags. `overrideDurationMs` lets Deeper drive duration from a timeline segment width. |
| `TriggerBambiFreeze(deferReset=false)` | 268 | The freeze ritual (§5). Works even when subliminals are **disabled** (special trigger). |
| `TriggerDeferredBambiReset()` | 321 | Fire the deferred reset (called when a video ends). Interlocked one-shot; 90% roll. |
| `PlayTriggerAudio(trigger)` | 398 | Play just the whisper clip for a phrase (Trigger-Mode bubbles), no visual. |
| `GetActiveTextScreenRects()` | 1060 | Live padded text rects for the OCR skip-list (§6). UI-thread only. |
| Property / event | 73 / 78 | `IsRunning`; `SubliminalDisplayed` event (fires once per shown flash, `:608`). |

### 4b. Who calls it (the trigger map)

| Caller | `file:line` | What it does |
|---|---|---|
| **The scheduled loop** | `Timer_Tick` → `FlashSubliminal` → `ScheduleNext` (`:181`/`:188`/`:164`) | The heartbeat. `ScheduleNext` computes `60/SubliminalFrequency` s ± 30%, min 1 s. |
| **Dashboard card** | `SubliminalFeatureControl.xaml.cs:72` → `SetEnabled` | Enable toggle live-applies via the single authority. |
| **MainWindow engine Start/Stop** | `MainWindow.StartStop.cs:171/315/755/757`, `MainWindow.UiUpdates.cs:1212`, `MainWindow.xaml.cs:919`, `MainWindow.Presets.cs:807` | The dashboard Start/Stop engine button + preset apply gate on `SubliminalEnabled`. |
| **SessionEngine** (AI sessions) | `SessionEngine.cs:443` (Start if enabled), `:923-937` (swap session `SubliminalPhrases` into the live `SubliminalPool`, mod-aware), `:943-951` (start now or `DeferFeatureStart` at `SubliminalStartMinute`), `:408/:956` (Stop), `:818/:1188` (save/restore the user's pool around the session). | Sessions temporarily overwrite the enabled pool with the session's phrases and restore it after. |
| **AI command dispatch** | `PromptService`/`CommandFactory` → `SubliminalCommand.cs:27` → `FlashSubliminalCustom(text, opacity)`. Clamped 60 opacity / 80 chars. | The AI companion can flash a custom line. |
| **Deeper enhancement** | `IActionDispatcher.cs:360` → `FlashSubliminalCustom(text, overrideDurationMs: DurationMs, suppressHaptic)`. | Deeper effect-timeline items pin a subliminal beat; the segment width drives on-screen duration. |
| **Deeper "Speak/Say It"** | `SpeakPromptSession.cs:186/204` → `FlashSubliminalCustom(cue, suppressHaptic:true)`. | Shows the prompt cue as a subliminal. |
| **Chaos / Rabbit Hole (WPF)** | `EffectPayload.cs:118/227` (`FlashSubliminal`), `:240` (`TriggerBambiFreeze`). | Chaos subliminal "bubbles" reuse the loop-agnostic entries. |
| **VideoService** | `:1064` and `:1156` (`TriggerBambiFreeze(deferReset:true)` ~800 ms before a mandatory video / bubble-count), `:4070` (`TriggerDeferredBambiReset()` in `Cleanup`). | The freeze/reset ritual brackets mandatory video (§5). |
| **Autonomy Mode** | `AutonomyService.cs:1232` (`FlashSubliminal`), `AutonomyService.VoiceCommands.cs:255/268/456` (Start/Stop/`TriggerBambiFreeze`). Gated by `AutonomyCanTriggerSubliminal` (`AppSettings.cs:3603`). | Autonomous companion can fire subliminals independent of the loop. |
| **Voice / keyword** | `KeywordTriggerService.cs:1524` (`FlashSubliminal`), `:1534` (`FlashSubliminalCustom(keyword.ToUpper())`). | Spoken/typed trigger words flash text. |
| **Remote control** | `RemoteControlService.cs:981` (`FlashSubliminal`), `:997/:1001` (Start/Stop), `:1007` (`FlashSubliminalCustom`), `:760` (reports `subliminal_loop` service state), `:809/:912` (Stop on session/panic), `MainWindow.RemoteControl.cs:1404`. | Companion-app / partner remote. |
| **Quiz** | `QuizWindow.xaml.cs:1221` → `FlashSubliminal()`. | Reward/punish flashes during the classic quiz. |

### 4c. Who listens / what it touches

- **`SubliminalDisplayed` event** (`:78`, raised `:608`): **AvatarTube companion**
  (`AvatarTubeWindow.xaml.cs:269`) and **BarkService** (`BarkService.cs:520`) subscribe so the
  companion can react to a shown subliminal.
- **AvatarTube Trigger Mode** calls `PlayTriggerAudio(trigger)` (`AvatarTubeWindow.Speech.cs:1813`)
  to voice a phrase without a visual.
- **ProgressionService**: XP on each flash — `AddXP(20, XPSource.Subliminal)` with audio,
  `AddXP(10, ...)` without (`:234`/`:239`/`:258`).
- **AudioService**: an audible whisper ducks other audio (`Duck`/`Unduck` around
  `PlayWhisperAudio`, `:216`/`:537`) and calls `MarkWhisperAudio` (`:543`) so the bark system won't
  talk over it. Duck generation is captured so a stale `PlaybackStopped` after `ForceUnduck` is
  ignored (`:531`).
- **HapticsService**: `TriggerSubliminalPatternAsync(text)` (pattern depends on the phrase) fires
  **before** the visual; the anticipation delay (`SubliminalAnticipationMs`, Buttplug ~1.3 s /
  Lovense ~250 ms) is why the visual is deliberately delayed (`TriggerSubliminalWithHapticPattern`,
  `:573`).
- **ModService**: the freeze/reset phrases are mod-derived (`App.Mods.GetFreezeTriggerText()` /
  `GetResetTriggerText()`, `:276`/`:368`); linked audio searches the active mod's
  `resources/sounds/flashes_audio` first (`GetModAudioPath`, `:445`). Session phrases run through
  `App.Mods.MakeModAware` (`SessionEngine.cs:935`); the pool is pruned cross-mod
  (`PruneCrossModSubliminals`, protected by `UserAddedSubliminals`, `AppSettings.cs:1104`).
- **Awareness OCR** (`App.GetCcpWindowRectsCached` / `ScreenOcrService`): consumes
  `GetActiveTextScreenRects` — see §6.
- **The compositor** (`App.Compositor`): path A registers `SubliminalLayer` lazily on first use
  (`:701`).

### 4d. Interaction with Flash / video / z-order

Subliminals do **not** stop for mandatory video the way flashes/bubbles do — instead the
**Bambi Freeze** ritual is fired *by* VideoService right before the video (§5). On the compositor,
`Subliminal = 40` draws **above** Flash (30) and **below** Bubbles/BouncingText/BrainDrain/Spiral/
PinkTint — so subliminal text stays legible over a flash image but is washed by the pink filter and
spiral, by design.

---

## 5. Text sources, phrase selection, the Freeze/Reset ritual, whispers

### 5a. Where the words come from
The runtime pool is **`Settings.SubliminalPool`** (`AppSettings.cs:1080`), a `Dictionary<phrase,
enabled>` (21 built-in defaults, `:1056`). `FlashSubliminal` picks uniformly from the enabled subset
(`:199`); empty → no-op with a debug log. The pool is editable via the card's "📝 Messages"
(`TextEditorDialog`, `SubliminalFeatureControl.xaml.cs:144`); hand-added phrases are tracked in
`UserAddedSubliminals` so the cross-mod prune never deletes them. Sessions carry their own
`SubliminalPhrases` (`List<string>`) which SessionEngine merges into the live pool for the session
and restores after (§4b). Custom one-shots (`FlashSubliminalCustom`) bypass the pool entirely.

### 5b. Colors / size / duration
Colors come from settings (`SubTextColor` default magenta `#FF00FF`, `SubBackgroundColor` black,
`SubBorderColor` white, `SubBackgroundTransparent`), parsed with a fallback (`ParseColor`, `:1306`).
Text is **Arial Bold 120 DIP**, centered, with **8 border-offset copies** under the main text for an
outline (`Offsets`, both in `BuildSubliminalContent`/`ShowHostedSubliminal` and mirrored in
`SubliminalLayer`). Duration = `SubliminalDuration × 17 ms` (frames→ms, min 100), or a caller
override (`:612`). Opacity scales the whole card (bg + text), default 80% (`:615`).

### 5c. The Bambi Freeze → Bambi Reset ritual
`TriggerBambiFreeze` (`:268`) is a special trigger that fires **even when subliminals are disabled**.
It resolves the mod's freeze phrase, plays the whisper + haptic + visual, then schedules a
**Bambi Reset** follow-up — either immediately (`ScheduleBambiReset`, 4–8 s later, `:346`) or
**deferred** (`deferReset:true`, an `Interlocked` flag consumed by `TriggerDeferredBambiReset` when
the video ends, `:300`/`:321`). Both reset paths roll a **90% chance** to actually fire (`:329`/
`:349`). VideoService brackets a mandatory video with `TriggerBambiFreeze(deferReset:true)` before
and `TriggerDeferredBambiReset()` in cleanup — freeze the subject going in, release coming out.

### 5d. Whispered audio
If `SubAudioEnabled` and a clip matching the phrase exists, the flash becomes audible.
`FindLinkedAudio` (`:417`) searches the **active mod's** `flashes_audio` dir first, then
`Resources/sub_audio`, trying case + curly/straight-apostrophe variants and a cached
case-insensitive directory scan (`SearchAudioDirectory`, 60 s cache, `:454`). Playback is NAudio
`WaveOutEvent` at `(subVol × masterVol)^1.5` (`PlayWhisperAudio`, `:513`). Audible flashes duck
other audio and delay the visual by the haptic anticipation window; silent flashes go through
`TriggerSubliminalWithHapticPattern` (`:573`).

---

## 6. `GetActiveTextScreenRects` — the OCR skip contract (why other systems query live rects)

The companion's **awareness OCR** (`ScreenOcrService`, via `App.GetCcpWindowRectsCached`) reads the
screen to know what the user is looking at. Subliminal cards are **intentionally left in screen
capture** (`WDA_NONE`, `:1042`) so they show up in the user's recordings — which means OCR would
otherwise read the app's *own* flashed words and create a feedback loop. The fix (#287 pattern): the
full-screen overlay window/host is dropped from the exclusion set by the per-monitor span filter
(`App.xaml.cs:687`+), and only the **small centered text rect** of any *currently visible*
subliminal is excluded.

`GetActiveTextScreenRects()` (`:1060`, **UI-thread only**) returns those padded physical-px rects,
walking all three render paths:
1. **Keep-alive windows** (`:1065`): for each shown window with Opacity > 0.01, measure the live
   `TextBlock`s and convert card-local → physical via the window rect scale, +40 DIP pad.
2. **Hosted cards** (`:1112`): card-local DIP → physical via captured screen origin + scale.
3. **Compositor** (`:1143`): delegates to `SubliminalLayer.GetActiveTextRectsPx()`
   (`SubliminalLayer.cs:158`), which computes rects from metrics measured at `Flash` time (only the
   most-recent, actually-drawn card).

Consumers: `App.xaml.cs:683` snapshots these on the UI thread into `subliminalRects` for the OCR
rect cache. `ShowSubliminalVisuals` calls `App.InvalidateCcpWindowRectsCache()` right after a show
(`:637`/`:671`) to force the cache to rebuild *now* rather than wait out its ~250 ms window — a flash
can be shorter than that. **Bouncing Text has its own identical accessor** (`BouncingTextService.
GetActiveTextScreenRects`, `:71`, consumed at `App.xaml.cs:676`).

---

## 7. THE BOUNCING TEXT SIBLING (`App.BouncingText`)

`BouncingTextService` (`Services/Subliminal/BouncingTextService.cs`) is a **separate feature** in the
same folder: DVD-screensaver text that bounces across the whole virtual desktop, changes color on
each wall bounce, and rewards **corner hits**. Unlocks at **Level 60**. It shares the folder, the
OCR-rect contract, and the phrase-pool idea with subliminals but **none of the render code**.

- **Model:** one moving word (`_currentText`, `_posX/_posY`, `_velX/_velY` in DIP/second) drawn by
  one `BouncingTextWindow` (`:553`) per screen — always **per-window** (WS_EX_LAYERED, transparent,
  click-through, topmost). **There is no compositor or shared-host path** despite the reserved
  `CompositorLayers.BouncingText = 50` slot (§10.9).
- **Motion driver:** `CompositionTarget.Rendering` (vsync-aligned, delta-time), **not** a
  `DispatcherTimer` — a timer beats against refresh and judders (`Animate`, `:305`). The loop is
  **paused entirely** during mandatory video (`OnVideoStartedPause`/`OnVideoEndedResume`, `:187`/
  `:197`) — leaving a no-op render callback subscribed was a contributor to an idle-freeze (#453).
- **Corner hits:** a simultaneous X+Y bounce (or near-corner within `CORNER_TOLERANCE = 15px`) fires
  `TrackCornerHit` + `OnCornerHit` (`:381`). Plain bounces award 15 XP (`XPSource.BouncingText`)
  rate-limited to 150 XP/min with a 2 s cooldown (`:412`), fire `OnBounce`, pulse haptics, 10% chance
  to change phrase, and re-color. Z-order is re-asserted every ~500 ms (`:435`).
- **Pool:** `Settings.BouncingTextPool` (`AppSettings.cs:2822`, 10 defaults), or an explicit pool
  passed to `Start(bypassLevelCheck, pool)`. Empty → falls back to "GOOD GIRL".
- **Triggers:** the dashboard card, `MainWindow` engine Start/Stop + level unlock
  (`MainWindow.LevelFeatures.cs:141`), `SessionEngine.cs:448/1002-1019` (with `BouncingTextStartMinute`
  defer), `BounceCommand.cs` (AI/remote, carries `words`), `RemoteControlService.cs:1170`,
  `AutonomyService` (`:1284`, gated by `AutonomyCanTriggerBouncingText`), `MainWindow.UiUpdates.cs:2280`.
- **Listeners:** `BarkService.cs:502/505` wires `OnBounce`/`OnCornerHit` to companion barks.

---

## 8. Settings (`Models/AppSettings.cs`)

### 8a. Subliminals (`#region Subliminals`, `:1026`)

| Setting | `:line` | Range / default | Effect |
|---|---|---|---|
| `SubliminalEnabled` | 1029 | `false` | Master gate for the loop (`ScheduleNext`/`Timer_Tick` bail if off). |
| `SubliminalFrequency` | 1036 | 1–30, **5** | Messages per **minute**; `ScheduleNext` = `60/freq` ± 30%, min 1 s. |
| `SubliminalDuration` | 1043 | 1–10, **2** | Frames; on-screen ms = `×17` (min 100). |
| `SubliminalOpacity` | 1050 | 10–100, **80** | Peak alpha (scales card + text). |
| `SubliminalPool` | 1080 | 21 defaults | The phrase dictionary (enabled subset is drawn from). |
| `RemovedDefaultSubliminals` / `UserAddedSubliminals` | 1092 / 1106 | | Migration/prune bookkeeping (don't re-add removed; don't prune hand-added). |
| `SubBackgroundColor` / `SubBackgroundTransparent` | 1114 / 1121 | `#000000` / `false` | Card background. |
| `SubTextColor` | 1128 | `#FF00FF` | Text color. |
| `SubBorderColor` | 1142 | `#FFFFFF` | Outline color. |
| `SubliminalSolidMode` | 1155 | `false` | Selects render **path B** (when compositor off). Ignored while StealsFocus. Applies to the *next* flash (no service bounce). |
| `SubliminalStealsFocus` | 1161 | `false` | Advanced opt-in; forces **path C** and `Activate()`s the window. |
| `SubAudioEnabled` | 1168 | `false` | Play matching whisper clips. |
| `SubAudioVolume` | 1175 | 0–100, **50** | Whisper volume (× master, `^1.5` curve). |
| `SubliminalPoolByMode` / `...ByMod` | 1290 / 1303 | | Per-content-mode / per-mod saved pools (migration + mod switching). |
| `RampLinkSubliminalAudio` | 2145 | `false` | Link whisper cadence to session ramp. |
| `AutonomyCanTriggerSubliminal` | 3603 | `true` | Lets Autonomy fire subliminals. |
| `SubliminalStartMinute` | (session) | — | Delay the loop N minutes into a session (`SessionEngine.cs:943`). |

Related (shared, not in-region): `DualMonitorEnabled` (all screens vs primary), `AudioDuckingEnabled`
/ `DuckingLevel`, `MasterVolume`, `UnifiedOverlayHost` (behind `App.CompositorEnabled` → path A).

### 8b. Bouncing Text (`#region Bouncing Text`, `:2779`)

| Setting | `:line` | Range / default | Effect |
|---|---|---|---|
| `BouncingTextEnabled` | 2782 | `false` | Master gate (Lv.60). |
| `BouncingTextSpeed` | 2789 | 1–10, **5** | Travel speed (maps to DIP/sec). |
| `BouncingTextSize` | 2796 | 50–300, **100** | % of the 72 px base font. |
| `BouncingTextOpacity` | 2803 | 0–100, **100** | Text opacity. |
| `BouncingTextPool` | 2822 | 10 defaults | Phrase pool. |
| `BouncingTextAlwaysOnTop` | 2829 | `false` | (Legacy z toggle.) |
| `AutonomyCanTriggerBouncingText` | 3683 | `true` | Autonomy gate. |

---

## 9. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Add a new trigger source | Call `App.Subliminal.FlashSubliminalCustom(text, ...)` (one-shot) or `FlashSubliminal()` (random from pool). Don't call `Start()` unless you own the loop. |
| Change the schedule / cadence | `ScheduleNext` (`:164`) — `60/freq` + variance + 1 s floor. |
| Change duration / fade | Duration in `ShowSubliminalVisuals` (`:612`); the 50 ms fade envelope lives in **all three paths** (`AnimateSubliminal` `:1247`, `ShowHostedSubliminal` storyboard `:822`, `SubliminalLayer.Item.Envelope` `SubliminalLayer.cs:74`) — change all three to stay in parity. |
| Change text look (font/outline/size) | `CreateTextBlock` (`:1233`) + the `Offsets` array + `fontSize=120` in `BuildSubliminalContent` (`:985`) **and** `ShowHostedSubliminal` (`:764`) **and** `SubliminalLayer` (`FontDip`/`Offsets`, `:24`/`:29`). Three copies. |
| Add a render path / change path selection | The `compositor/useHost` decision in `ShowSubliminalVisuals` (`:633`/`:643`). A new path needs a spawn branch, a fade envelope, a teardown branch in `Stop`/`Dispose`, and a rect branch in `GetActiveTextScreenRects` (`:1060`). |
| Change phrase sources / pool behavior | `Settings.SubliminalPool` + the card's `TextEditorDialog` (`SubliminalFeatureControl.xaml.cs:139`); selection in `FlashSubliminal` (`:199`); session merge in `SessionEngine.cs:923`. |
| Change the Freeze/Reset ritual | `TriggerBambiFreeze` (`:268`), `ScheduleBambiReset` (`:346`), `TriggerDeferredBambiReset` (`:321`); the mod phrases via `App.Mods.GetFreeze/ResetTriggerText`. VideoService bracket at `:1064`/`:4070`. |
| Change whisper audio lookup/playback | `FindLinkedAudio`/`SearchAudioDirectory` (`:417`/`:454`), `PlayWhisperAudio` (`:513`). |
| Change the AI command clamps / add a field | `SubliminalCommand.cs:10-11`; DTO `Models/CommandData/Subliminal.cs`. |
| Change the OCR skip rects | `GetActiveTextScreenRects` (`:1060`) + `SubliminalLayer.GetActiveTextRectsPx` (`SubliminalLayer.cs:158`); consumer `App.xaml.cs:683`. |
| Change Bouncing Text | `BouncingTextService.cs` — motion `Animate` (`:305`), corner logic (`:381`), settings §8b. |

---

## 10. GOTCHAS (the expensive ones)

1. **Keep-alive windows are shown-but-blank between flashes, never closed mid-run.** Closing a
   `WS_EX_LAYERED` window while other layered surfaces animate can wedge the shared render thread
   (Hang 1002). `Stop()` blanks + hides (`:110`); real `Close()` only in `Dispose()` (`:1328`).
2. **A hidden `AllowsTransparency` window keeps its last layered bitmap.** So path C stays *shown* at
   Opacity 0 with null content between flashes — `Hide()`+`Show()` would re-present the **previous**
   phrase for a frame ("previous-then-next double"). The load-bearing comment is at `:1289`.
3. **A newer show must invalidate the old storyboard's `Completed`.** `_showGeneration` (`:34`,
   bumped `:1283`) guards the per-window cleanup so the old fade-out doesn't blank the new text early.
   The compositor's equivalent is most-recent-card-wins (`_items[^1]`).
4. **All three fade envelopes must match (50 ms in / hold / 50 ms out).** They're implemented three
   times (§9). Divergence shows as a subliminal that flickers differently depending on the render
   path the user's settings select.
5. **Never let a subliminal be silently invisible.** Every path falls through to the next on failure:
   compositor `false` → solid, solid `CoversPoint` `false` → per-window (`:652`). A half-built hosted
   card whose `Begin()` never ran is pulled off the canvas in the catch (`:859`) — otherwise it sits
   there forever with no `Completed` to remove it.
6. **The single host ref-count is NOT per-show.** `EnsureHostRef`/`ReleaseHostRef` (`:886`/`:894`)
   take one hold while any card *could* be up; released on Stop/Dispose or when a one-shot's last
   card fades with the service not running (`:847`). Ref-counting per show would churn the shared host
   — exactly what solid mode exists to avoid.
7. **Haptics fire BEFORE the visual, on purpose.** The visual is delayed by the provider anticipation
   window (Buttplug ~1.3 s / Lovense ~250 ms) so the buzz and the flash land together
   (`TriggerSubliminalWithHapticPattern`, `:573`; the audio path uses a fixed 50 + 250 ms, `:220`).
   `suppressHaptic:true` skips the delay and shows immediately.
8. **Subliminal cards stay in screen capture (`WDA_NONE`) — OCR is kept out via rects, not capture
   exclusion.** If you change the render so text isn't reported by `GetActiveTextScreenRects`, the
   awareness OCR will read the app's own words (feedback loop, #287). Keep the rect accessor in sync
   with any new render path.
9. **Bouncing Text has NO compositor path** despite `CompositorLayers.BouncingText = 50`. That z-slot
   is reserved; the service is still per-window only. Don't assume a `BouncingTextLayer` exists.
10. **`GetActiveTextScreenRects` is UI-thread only** (it reads live WPF visual state). Callers marshal
    (see `App.xaml.cs:683` inside a dispatcher block).
11. **Fire-and-forget continuations are guarded** — every `Task.Delay(...).ContinueWith(...)` marshals
    through `Application.Current?.Dispatcher?.Invoke` with null checks (the general CCP async-crash
    rule, root `CLAUDE.md` §6). Bouncing Text's video events also re-marshal to the UI thread
    (`:187`).
12. **Mixed-DPI multi-monitor:** the shared host renders at ONE scale, so a hosted card on a
    differently-scaled screen compensates with a `LayoutTransform` (`:802`); the compositor layer is
    `WorldSpacePx` (`SubliminalLayer.cs:99`); path C positions via physical-px `SetWindowPos`
    (`:1033`). Cross-ref root `CLAUDE.md` #5 (guard `Screen.AllScreens`).

---

## 11. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

> This section rots. Confirm with `git log --oneline -- Services/Subliminal/SubliminalService.cs`
> and `git branch` before acting.

- **State: mature and shipping.** No dedicated in-flight branch for subliminals. HEAD is `95586020`
  on `fix/web-video-interruptions` (v6.5.0). Subliminal work rides in general triage / compositor
  batches.
- **Compositor path is the default** (`UnifiedOverlayHost = true` → `App.CompositorEnabled`). Paths B
  (solid host, #461) and C (keep-alive windows) are fallbacks and still fully live. Per the
  auto-memory, the compositor cluster is "default ON PUSHED, play-test pending" — path A is the hot
  path but keep B/C working.
- **Glyph fallback (#615)** landed in `SubliminalLayer` (Arial for Latin, a system fallback family
  for CJK/Cyrillic/emoji, resolved once via `GlyphFallback.Resolve` and used for both measure and
  draw). Color-emoji outline copies read as a slight thickening — left as-is for parity
  (`SubliminalLayer.cs:230`).
- **Graded Intake reuse:** the web core reads `SubliminalPool` keys but renders its own subliminals
  (§1). The intake **drafted session** seeds `SessionTextContent.SubliminalPhrases` verbatim
  (`QuizSessionGenerator.cs:235`), which SessionEngine then merges into the live pool — so a run's
  affirmed mantras can become the next session's subliminals. That's a *content* coupling, not a
  render coupling.
- **No dedicated unit tests** cover `SubliminalService`/`SubliminalLayer` directly (unlike Flash's
  `ResolveFlashCap`). The standing gate is play-test plus the broader xUnit suite. `SubliminalLayer`
  is the extractable seam if pure-function coverage is ever wanted.
- **This primer is new** and not previously committed.

---

## 12. Build / run / dev entry points

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```
Then: open the **Subliminals** card, enable it, and (for whispers) drop `<PHRASE>.mp3` files into
`Resources/sub_audio` (or the active mod's `resources/sounds/flashes_audio`). One-shot entries
(`FlashSubliminalCustom`) fire immediately regardless of the loop, so AI/voice/remote/Deeper
triggers work without starting the engine.

- **Force a render path:** compositor is default; toggle `UnifiedOverlayHost` off + `SubliminalSolidMode`
  on for path B; off + `SubliminalStealsFocus` on (or both off) for path C.
- **Test the freeze ritual:** trigger a mandatory video — `TriggerBambiFreeze(deferReset:true)` fires
  ~800 ms before it and the reset ~1–2 s after it ends (90% roll).
- **Bouncing Text** unlocks at Lv.60; its card + `App.BouncingText.Start(bypassLevelCheck:true)` (via
  remote/AI) bypass the gate for testing.
