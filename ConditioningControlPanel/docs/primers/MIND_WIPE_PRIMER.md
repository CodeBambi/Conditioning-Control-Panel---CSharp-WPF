# MIND WIPE — Feature Primer

> **Purpose.** One-load orientation for the Mind Wipe feature so a future engineer (or Claude) can
> maintain it WITHOUT re-exploring the codebase. Mind Wipe is small (one ~600-line service, no render
> path, no visual), so this primer is short by design. §0 = what it is in one paragraph. §1 = file
> map. §2 = the service internals (the two playback modes + lifecycle). **§3 is the load-bearing
> section — every way it's triggered and every system it touches** (read it before wiring a new
> trigger). §4 = settings. §5 = the "why is it under `Services/LockCard/`?" answer. §6 =
> where-to-change-X. §7 = gotchas. §8 = dated status.
>
> **Freshness.** Tracks the code as of **2026-07-23** (branch `fix/web-video-interruptions`, HEAD
> `95586020`, v6.5.0). §1–7 track the code and rarely rot; **§8 is a dated snapshot — verify with
> `git log` before acting on it.** Every `file:line` below was read-verified when written, but line
> numbers drift — confirm with a quick read before quoting.

---

## 0. What Mind Wipe is, in one paragraph

Mind Wipe is a **pure-audio** conditioning effect: it plays a short "mind wipe" sound clip (a ~2 s
whoosh/wipe is recommended) at random intervals — or continuously loops one clip in the background —
to punctuate a session. It is **audio only**: no window, no overlay, no visual, no XP award on play,
and — unlike Flash and Video — it **deliberately does NOT duck other audio** (see the class comment,
`MindWipeService.cs:16`). It is one service, **`MindWipeService`** (`App.MindWipe`), built on NAUDIO
(`WaveOutEvent` + `AudioFileReader`). It unlocks at **Level 75** (a UI-only gate — see §7.1). Audio
comes from a user-picked custom clip if set, else the shipped `Resources/sounds/mindwipe/` folder
(currently one file, `mindwipe.mp3`). It has two playback modes — an **interval mode** (a 10 s
`DispatcherTimer` rolls a per-tick probability derived from a per-hour frequency, with an escalating
"session mode" variant) and a **loop mode** (two crossfaded players for a seamless background bed) —
plus a `TriggerOnce()` test/one-shot. On each interval play it raises `MindWipeTriggered`, which the
companion avatar and the bark system react to. Do not confuse the folder it lives in
(`Services/LockCard/`) with any Lock Card coupling — there is none (§5).

---

## 1. Where it lives — file map

| File | Role |
|---|---|
| `Services/LockCard/MindWipeService.cs` (~600 lines) | **The whole feature.** Audio load, the interval timer + probability math, session-mode escalation, the two-player crossfade loop, `TriggerOnce`, lifecycle. Namespace is `ConditioningControlPanel.Services` — the folder is organizational only (§5). |
| `Features/MindWipeFeatureControl.xaml` / `.xaml.cs` | The **dashboard feature card** (opened as a popup): enable toggle, frequency slider (1–180/h), volume slider (0–100), "loop in background" toggle, custom-audio picker + reset, "Test now" button. Two-way binds to `App.Settings.Current.*` and live-applies to `App.MindWipe`. No audio logic lives here. |
| `Models/AppSettings.cs` `#region Mind Wipe (Unlocks Lv.75)` (`:2887`) | The five persisted settings (see §4). `AutonomyCanTriggerMindWipe` lives separately at `:3643`. |
| `Models/Session.cs` (`:918`) | **Session-only** knobs (`MindWipeStartMinute`, `MindWipeEndMinute`, `MindWipeBaseMultiplier`, `MindWipeVolume`) — these drive the escalating session mode, not the standalone settings. |
| `Resources/sounds/mindwipe/mindwipe.mp3` | The one shipped built-in clip. Glob-included via `Resources\**`; add more `.mp3/.wav/.ogg` here (rebuild/hot-copy required — §7.5). |
| `Resources/sounds/companion_audio/mods/builtin-*/bark_rules.json` | The **Mind Wipe bark voicelines** (per mod): `mindwipe` (on trigger), `feat_mindwipe`, `set_mindwipe_on`, `set_mindwipeloop_on`, and the frequency/volume threshold + easter-egg barks (§3d). |
| `App.xaml.cs` | Declares `public static MindWipeService MindWipe` (`:318`), constructs it in `OnStartup` (`MindWipe = new MindWipeService();`, `:1425`), `Stop()`s it on engine stop (`:803`), and `Dispose()`s it on shutdown (`:3256`). |

Sibling controls in the **ProgressionTab** (`Views/Tabs/ProgressionTabView.xaml`) mirror the popup
card's controls inline (handlers in `MainWindow/MainWindow.LevelFeatures.cs:203-276`).

---

## 2. The service — internals

### 2a. Audio sourcing (`LoadAudioFiles`, `:102`)
Custom clip wins: if `AppSettings.MindWipeAudioPath` is a non-empty existing file, `_audioFiles` is
just that one path (`:107-113`). Otherwise it enumerates
`{BaseDirectory}/Resources/sounds/mindwipe/` for `.mp3/.wav/.ogg` (`:115-132`) — note **BaseDirectory
(the bin/install dir), not `%APPDATA%`**, so the built-in pool is a shipped resource. A missing folder
is **created empty** (`:119-124`) so the user has somewhere to drop files. `ReloadAudioFiles()`
(`:155`) re-runs this after a custom-clip change.

### 2b. Interval mode (`Start` → `Timer_Tick` → `PlayAudioNow`)
- `Start(frequencyPerHour, volume)` (`:160`): if already running, delegates to `UpdateSettings`
  (`:223`); else sets normal mode, starts the 10 s `DispatcherTimer` (ctor `:93`), and pushes Discord
  presence (`App.DiscordRpc.SetMindWipeActivity()`, `:178`).
- `Timer_Tick` (`:440`): **early-returns while `_loopMode`** (loop and interval never both fire) and
  when not running / no files. Computes a per-tick probability and rolls: **normal mode**
  `probability = frequencyPerHour / 360` (360 ten-second windows/hour, so 180/h ≈ 0.5/tick, `:481`);
  if `roll < probability` → `PlayAudioNow()`.
- `PlayAudioNow` (`:496`) picks a random file, calls `PlayAudio` (`:516`), then **fires
  `MindWipeTriggered`** (`:508`). `PlayAudio` stops any current clip, builds a fresh
  `AudioFileReader` + `WaveOutEvent`, routes it through `App.Audio.ApplyPreferredDevice(_waveOut)`
  (`:527`, honors the user's chosen output device — `AudioService.cs:141`), sets volume, and disposes
  on `PlaybackStopped`. Only **one** interval clip plays at a time (`StopCurrentAudio` first).

### 2c. Session mode (escalating) (`StartSession` → `Timer_Tick`)
`StartSession(baseFrequencyMultiplier)` (`:187`) sets `_sessionMode`. The tick then escalates: every
5-minute block adds +1 to `playsThisBlock` (`base + fiveMinBlocks`, capped **15**), and
`probability = playsThisBlock / 30` (5 min = 30 ten-second windows, `:460-471`). This is how sessions
ramp Mind Wipe density over time. `GetCurrentSessionFrequency()` (`:586`) reports it for UI (note it
caps at **30**, not 15 — a display/logic mismatch, §7.7).

### 2d. Loop mode (crossfade) (`StartLoop` / `StopLoop`)
`StartLoop(volume)` (`:246`) picks one random file, reads its duration, and starts a two-player
crossfade: players **A** and **B** (`_loopWaveOutA/B` + `_loopReaderA/B`) alternate via `_usePlayerA`.
A `_crossfadeTimer` (`:287`) fires `CROSSFADE_OVERLAP_SECONDS` (**0.12 s**, `:44`) before the track
ends and calls `StartNextLoopPlayer` (`:318`), which spins up the *other* player (creating the overlap)
and schedules the old one's disposal (`SchedulePlayerCleanup`, `:372`). The **Clean Slate**
achievement fires at 60 s of continuous loop (`App.Achievements.TrackMindWipeDuration`, `:309`).
`StopLoop` (`:422`) tears down the timer + both players. **Loop mode does NOT raise
`MindWipeTriggered`** (only interval/`TriggerOnce` do) — so avatar/bark reactions don't happen during
a loop.

### 2e. One-shot / test (`TriggerOnce`, `:565`)
Plays one clip immediately using the settings volume; if no files exist it shows a **`MessageBox`**
(must be on the UI thread). This is the entry used by the card's "Test now" button, autonomy, voice
keywords, and remote control.

### 2f. Lifecycle summary
`ctor` (timer + `LoadAudioFiles`) → `Start`/`StartSession`/`StartLoop` → `Stop` (`:206`: stop timer,
cancel cts, `StopCurrentAudio` + `StopLoop`, Discord idle) → `Dispose` (`:595`: `Stop` + unhook timer).
`Volume` (`:68`) and `UpdateSettings` (`:223`) push volume live into any active reader(s).

---

## 3. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

This is the section to read before wiring a new trigger. The public surface is small —
`Start` / `StartSession` / `Stop` / `StartLoop` / `StopLoop` / `TriggerOnce` / `UpdateSettings` /
`ReloadAudioFiles`, plus the `Volume`/`FrequencyPerHour` properties and the `IsRunning`/`IsLooping`/
`AudioFileCount` reads — but it is driven from ~7 subsystems. **Mind Wipe is NOT
`InteractionQueue`-gated** (unlike Video / BubbleCount / LockCard); it is pure background audio and
coexists with everything.

### 3a. Who starts/stops/triggers it (the trigger map)

| Caller | `file:line` | What it does |
|---|---|---|
| **Dashboard card (popup)** | `MainWindow.Presets.cs:938` (`CardMindWipe_Click` → `MindWipeFeatureControl`) | Enable/freq/volume/loop/custom-audio + "Test now". Live-applies via `App.MindWipe.UpdateSettings`/`StartLoop`/`StopLoop`/`TriggerOnce`. |
| **ProgressionTab inline controls** | `MainWindow.LevelFeatures.cs:203-276` | The same knobs inline: `ChkMindWipeEnabled_Changed` starts/stops when the engine is running (`:215/:219`); sliders `UpdateSettings`; loop toggle `StartLoop`/`StopLoop`; `BtnTestMindWipe_Click` → `TriggerOnce` (`:275`). |
| **Engine Start/Stop** | `MainWindow.StartStop.cs:207-216` (Start + `StartLoop` if `MindWipeLoop`), `:319` (Stop) | The dashboard Start/Stop button starts interval mode (and loop if set). Also `App.MindWipe?.Stop()` on the global stop path (`App.xaml.cs:803`, `MainWindow.xaml.cs:923`). |
| **SessionEngine (AI/scripted sessions)** | `SessionEngine.cs:187-203` (StartSession or `DeferFeatureStart` at `MindWipeStartMinute`), `:449-450` (resume `Start`), `:285/:414` (Stop) | Sessions run the **escalating** session mode and copy the session's `MindWipeVolume`/`MindWipeBaseMultiplier` in. Also flags Season Recap (`:237`). |
| **Autonomy Mode** | `AutonomyService.cs:1020-1021` (candidate, weight **15**), `:1266` (`TriggerOnce`); voicelines `:178`; `AutonomyService.VoiceCommands.cs:368` (`TriggerOnce`) | The autonomous companion can fire a one-shot wipe. Gated by `AppSettings.AutonomyCanTriggerMindWipe` (default `true`). |
| **Voice / keyword** | `KeywordTriggerService.cs:1548-1551` (`KeywordVisualEffect.MindWipe` → `TriggerOnce` if `AudioFileCount > 0`) | A spoken/typed trigger word fires one wipe. |
| **Remote control** | `RemoteControlService.cs:1154-1167` (`trigger_mind_wipe`/`start_mind_wipe`/`stop_mind_wipe`), `:762` (reports `mind_wipe` service state), `:813/:916` (Stop); `MainWindow.RemoteControl.cs:1408` (Stop) | Companion-app / partner remote verbs. |
| **QuizSessionGenerator (Graded Intake draft)** | `QuizSessionGenerator.cs:328` (`MindWipeBaseMultiplier` nudge), `:467/:515/:570/:626` (enable + tier values) | The drafted session from a Graded Intake run turns Mind Wipe on with tiered base/volume. Not a live trigger — it writes session settings. |
| **Awareness presets** | `Resources/AwarenessPresets/trance.json`, `bimbo.json` | Presets that enable Mind Wipe as part of a bundle. |

### 3b. Audio system
- **No ducking.** Mind Wipe never calls `App.Audio.Duck/Unduck` — by design (`MindWipeService.cs:16`).
  It layers on top of whatever else is playing.
- **Output-device routing.** Every `WaveOutEvent` (interval, loop A, loop B) is passed through
  `App.Audio.ApplyPreferredDevice(_waveOut)` (`:527/:334/:353`, → `AudioService.cs:141`) so wipes
  follow the user's chosen output device.

### 3c. Progression / achievements / Discord
- **Achievements**: only one hook — `App.Achievements.TrackMindWipeDuration(elapsed)` at 60 s of
  continuous **loop** (`:309` → `AchievementService.cs:547`, unlocks `clean_slate`, stores
  `ContinuousMindWipeSeconds`). **Interval plays award no XP and no achievement.**
- **Discord RPC**: `SetMindWipeActivity()` on `Start`/`StartSession` ("Deep conditioning / Mind wipe
  in progress", `DiscordRichPresenceService.cs:242`); `SetIdleActivity()` on `Stop`.
- **Season Recap**: `SeasonRecapService.TrackFeature(SeasonFeatureKeys.MindWipe)` once per session
  when enabled (`SessionEngine.cs:237`).

### 3d. Avatar & bark reactions (the `MindWipeTriggered` consumers)
`PlayAudioNow` raises **`MindWipeTriggered`** (interval + `TriggerOnce` only). Consumers:
- **AvatarTube companion**: subscribes at `AvatarTubeWindow.xaml.cs:307-310`, unsubscribes at
  `AvatarTubeWindow.Windowing.cs:859-862`. `OnMindWipeTriggered` (`AvatarTubeWindow.Reactions.cs:514`)
  giggles **1-in-6** via `GiggleFromCategory("MindWipe")` (counter at `Speech.cs:2334`), marshalled to
  the UI thread.
- **BarkService**: wires `MindWipeTriggered` → `Raise("MindWipeTriggered")`
  (`BarkService.cs:525-527`), which drives the companion bark rules.
- **Bark voicelines** (per-mod `bark_rules.json`): `mindwipe` (3 variants, on the trigger),
  `feat_mindwipe` (feature announce), `set_mindwipe_on` / `set_mindwipeloop_on` (setting toggles),
  and the **threshold** barks the CLAUDE note flags — `thr_mindwipefreq_high` (`MindWipeFrequency ≥
  120`, `:5413`), `thr_mindwipevol_high` (`MindWipeVolume ≥ 90`, `:5438`), plus the easter egg
  `egg_mindwipefreq_69` (`MindWipeFrequency == 69`, once, `:4868`). These fire off `SettingChanged`,
  not off the service.
- **CompanionPhraseService** (`:134`) lists `"MindWipe"` among the giggle categories mods can supply.

---

## 4. Settings that gate & tune it

### Standalone settings (`Models/AppSettings.cs`, `#region Mind Wipe (Unlocks Lv.75)` `:2887`)
| Setting | `:line` | Range / default | Effect |
|---|---|---|---|
| `MindWipeEnabled` | 2890 | `false` | Master on/off (checked at engine start, `StartStop.cs:207`). |
| `MindWipeFrequency` | 2897 | 1–180, **6** | Plays per **hour** in interval mode → `freq/360` per-tick probability. |
| `MindWipeVolume` | 2904 | 0–100, **50** | Playback volume (÷100 → the service's 0..1 `Volume`). |
| `MindWipeLoop` | 2911 | `false` | Use the crossfaded background loop instead of interval plays. |
| `MindWipeAudioPath` | 2921 | `""` | Custom clip; overrides the built-in folder when it points at an existing file. |
| `AutonomyCanTriggerMindWipe` | 3643 | `true` | Lets Autonomy Mode fire one-shot wipes. |

### Session-only settings (`Models/Session.cs`, `:918`) — drive escalating session mode
| Setting | `:line` | Default | Effect |
|---|---|---|---|
| `MindWipeStartMinute` | 920 | 0 | Delay N minutes into a session (0 = start immediately, else `DeferFeatureStart`). |
| `MindWipeEndMinute` | 921 | -1 | Defined but **not consumed** by `SessionEngine` today (§7). |
| `MindWipeBaseMultiplier` | 922 | 1 | Escalation base (Easy 1 / Medium 2 / Hard 3). |
| `MindWipeVolume` | 923 | 50 | Volume for that session. |

---

## 5. Why it lives under `Services/LockCard/` (and the NON-relationship to Lock Cards)

`MindWipeService.cs` sits in the folder `Services/LockCard/` alongside `LockCardService.cs` and
`BrainDrainService.cs` — but its **namespace is `ConditioningControlPanel.Services`**, exactly like
its two folder-mates. The folder is **purely an organizational grouping** of the late-game "ambient
progression-unlock" services (Lock Card, Brain Drain @ Lv.25, Mind Wipe @ Lv.75), not a namespace or
an architectural cluster.

**There is no code coupling to Lock Cards or the dead-man's-switch machinery.** `MindWipeService`
does not reference `LockCardService`, does not participate in the `InteractionQueue` mutual-exclusion
(so it is *not* one of the mutually-exclusive fullscreen interactions), and shares none of the Lock
Card timeout/FailFast logic. Treat the folder name as a filing decision only — grepping `LockCard`
will pull in Mind Wipe hits that mean nothing. (This is a documented gotcha, §7.4.)

---

## 6. WHERE TO CHANGE X

| Want to… | Edit |
|---|---|
| Add a new trigger source | Call `App.MindWipe?.TriggerOnce()` (one-shot) or `Start(freq, vol)` / `StartLoop(vol)`. Guard on `AudioFileCount > 0` if you want to avoid the empty-pool `MessageBox`. |
| Change the interval probability / cadence | `Timer_Tick` (`:440`) — normal-mode `freq/360` (`:481`), session-mode `plays/30` (`:471`). The 10 s tick is the ctor `Interval` (`:95`). |
| Change session-mode escalation | `Timer_Tick` session branch (`:460-471`) — the +1-per-5-min ramp and the 15 cap; keep `GetCurrentSessionFrequency` (`:586`) in sync. |
| Change the loop crossfade | `CROSSFADE_OVERLAP_SECONDS` (`:44`), `StartLoop`/`StartNextLoopPlayer`/`SchedulePlayerCleanup` (`:246/:318/:372`). |
| Change the audio pool / add built-in clips | Drop files in `Resources/sounds/mindwipe/`; sourcing logic in `LoadAudioFiles` (`:102`). Rebuild or hot-copy (§7.5). |
| Change the Clean Slate threshold | `CrossfadeTimer_Tick` 60 s check (`:306`) + `AchievementService.TrackMindWipeDuration` (`:547`). |
| Add/adjust a Mind Wipe bark | `bark_rules.json` per mod (see §3d ids); use the `/add-barks` skill for cross-mod parity. |
| Add a setting | `AppSettings.cs` region (`:2887`), then a control + handler in `MindWipeFeatureControl.xaml(.cs)` **and** the ProgressionTab mirror (`MainWindow.LevelFeatures.cs`). |
| Change the level-75 gate | It is UI-only — see §7.1 (`MainWindow.UiUpdates.cs:630`, `SettingsTab.CardMindWipe.IsLocked`). |

---

## 7. GOTCHAS

1. **The Level-75 unlock is UI-only.** The service has no level check — `Start()`/`TriggerOnce()`
   work at any level. Gating is purely the card lock + ProgressionTab visibility
   (`MainWindow.UiUpdates.cs:630-645`, `SettingsTab.CardMindWipe.IsLocked`). A session/preset/remote
   call can drive Mind Wipe below Lv.75. Don't assume the service enforces the gate.
2. **It never ducks.** Unlike Flash/Video, Mind Wipe plays *over* everything (deliberate,
   `MindWipeService.cs:16`). If a wipe needs to be heard under loud media, that's a content-volume
   decision, not a ducking bug. (Cross-ref root `CLAUDE.md` audio notes.)
3. **`MindWipeTriggered` fires in interval + `TriggerOnce` only, never in loop mode**
   (`Timer_Tick` early-returns while `_loopMode`, `:443`). So avatar giggles + `mindwipe` barks are
   silent during a background loop — that's expected, not a wiring gap.
4. **The `Services/LockCard/` folder is a red herring.** No Lock Card / dead-man's-switch /
   InteractionQueue coupling exists (§5). Ignore `LockCard` grep hits when reasoning about Mind Wipe.
5. **Built-in clips load from `BaseDirectory`, not `%APPDATA%`** (`:115`). New files under
   `Resources/sounds/mindwipe/` need a rebuild (or a hot-copy into
   `bin/.../Resources/sounds/mindwipe/`) before the running app sees them. A **custom** clip
   (`MindWipeAudioPath`) can live anywhere and takes effect after `ReloadAudioFiles()`.
6. **Fire-and-forget crossfade cleanup.** `SchedulePlayerCleanup` uses
   `Task.Delay(...).ContinueWith(...)` → `DispatcherHelper.RunOnUI(...)` (`:375-388`). It relies on
   `RunOnUI` for the dispatcher-marshal + guard; it does not itself check `HasShutdownStarted`. If you
   extend it, keep the root `CLAUDE.md` §6 async-crash guard in mind.
7. **Session-mode play cap (15) ≠ UI report cap (30).** `Timer_Tick` caps `playsThisBlock` at 15
   (`:468`) but `GetCurrentSessionFrequency` caps its display at 30 (`:592`). The displayed number can
   overstate the real ceiling. Reconcile both if you retune escalation.
8. **`MindWipeEndMinute` is defined but unused.** `Models/Session.cs:921` declares it; `SessionEngine`
   only reads `MindWipeStartMinute`. A session can't currently schedule Mind Wipe to *stop* mid-run
   short of a full engine stop.
9. **`TriggerOnce` can pop a `MessageBox`** when the pool is empty (`:570`). It must run on the UI
   thread; background callers should pre-check `AudioFileCount > 0` (as remote/keyword do) to stay
   silent.
10. **10 s timer resolution.** Interval timing is quantized to 10 s windows; at low frequencies plays
    cluster to tick boundaries rather than spreading evenly. Fine for the intended use, but don't
    expect sub-10 s precision.

---

## 8. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

- **State: mature and shipping.** No dedicated in-flight branch for Mind Wipe; HEAD `95586020` on
  `fix/web-video-interruptions` (v6.5.0). The feature is stable and rarely touched — most recent
  churn is peripheral (custom-audio picker, bark thresholds, Graded Intake session drafting).
- **Custom-audio clip** (`MindWipeAudioPath` + the picker in `MindWipeFeatureControl`) is the newest
  addition; the built-in pool ships exactly one clip (`mindwipe.mp3`).
- **Known limits / dead code** (see §7): the Lv.75 gate is UI-only; `MindWipeEndMinute` is unused;
  the session play-cap (15) vs UI-report-cap (30) mismatch; loop mode emits no `MindWipeTriggered`.
  None are bugs users have reported — documented so they aren't "fixed" blindly.
- **No dedicated unit tests** cover `MindWipeService`; the standing gate is play-test. The probability
  math (`Timer_Tick`) and the escalation ramp are the only pure-ish seams worth a test if regressions
  appear.
- This primer is **new** and not previously committed.

---

## 9. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```
Then: reach Lv.75 (or edit progression), open the **Mind Wipe** card, enable it, and Start the engine
— or hit **Test now** (`TriggerOnce`) to hear a wipe immediately regardless of the loop/engine state.
Drop extra clips into `Resources/sounds/mindwipe/` (rebuild/hot-copy) or pick a custom clip via the
card. Watch `logs/` for the `MindWipe: …` Serilog lines (load count, per-tick probability, triggers).
