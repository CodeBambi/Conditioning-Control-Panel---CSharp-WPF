# Takeover (Autonomy Mode) — Feature Primer

> **Purpose.** One-load orientation for the **Takeover** premium feature — the companion "drives"
> the app on her own, announces + fires effects, and (with the mic) runs spoken "repeat after me"
> mantras and "Hey Bambi" voice commands. Read this instead of re-exploring the ~3,600 lines across
> three partial-class files. §0 = the one-paragraph model **and the Takeover↔Autonomy naming
> mapping**. §1 = architecture (the 3 partials + App wiring). §2 = lifecycle (Start/Stop, opt-in
> resume). §3 = the action/decision loop. §4 = the voice mechanics. **§5 is the load-bearing section —
> every way Takeover is invoked and every system it drives** (read it before wiring anything new).
> §6 file map, §7 settings, §8 where-to-change-X, §9 gotchas, §10 dated status.
>
> **Freshness.** Tracks the code as of **2026-07-23** on branch `fix/web-video-interruptions`
> (HEAD `95586020`, v6.5.0). §1–§9 track the code and rarely rot; **§10 is a dated snapshot — verify
> with `git log` before acting.** Every `file:line` below was read-verified when written, but line
> numbers drift — confirm with a quick read before quoting.

---

## 0. What Takeover is, in one paragraph — and the Autonomy naming

**"Takeover" is the user-facing name; the code namespace is "Autonomy."** The feature is the AI
companion acting on her own initiative: on idle/random/context/time-of-day triggers she picks a
weighted effect (flash, video, subliminal, bubbles, pink-filter pulse, lock card, mind wipe, web
video, wallpaper shuffle, a spoken mantra, or just a giggled comment), optionally announces it in
voice/text, then fires it — coexisting with, but standing off, whatever else is on screen. It is one
service, **`AutonomyService`** (exposed as `App.Autonomy`), split across three partial-class files.
The service class, all settings (`AutonomyModeEnabled`, `AutonomyConsentGiven`, `AutonomyCanTrigger*`,
etc.), the on-screen cue (`TakeoverAnnouncerOverlay`), and the dev methods (`ForceStart`,
`TestTrigger`) all use "Autonomy" internally; the UI, tab names, and localization keys say
"Takeover." The two are the **same thing** — do not go looking for a separate "Takeover" service.
It is **Patreon-gated** (`HasPremiumAccess`) and **double-consent-gated** (`AutonomyConsentGiven` for
the takeover itself; a *separate* `MicConsentGiven` before the microphone ever opens). It nominally
"unlocks at Lv.100," but that is a **UI-only gate** — the service has no level check (§9). The voice
layer (wake-word / push-to-talk / spoken mantras / "Hey Bambi" commands) was **deliberately
decoupled** from Takeover and now lives under the **"She's Listening"** Exclusive: the mic can run
with Takeover off, and Takeover can run with no mic.

---

## 1. Architecture — the three partials + App wiring

`AutonomyService` is `partial`, split by concern:

| File | Lines | Role |
|---|---|---|
| `Services/AutonomyService.cs` | ~2,069 | **The core.** Enums (`AutonomyActionType`/`TriggerSource`/`Mood`), the timers (idle/random/cooldown/heartbeat), `Start`/`Stop`/`CanStart`, the decision loop (`SelectAction` → `PerformAction`), all the effect drivers (`TriggerVideoSafely`, `TriggerWebVideoFullscreen`, `PulseSpiralOverlay`, `PulsePinkFilter`, `MakeComment`, `TriggerSpokenMantra`/`RunSpokenMantraAsync`), announcements, mood, and `Dispose`. Class opens at `:77`. |
| `Services/AutonomyService.Voice.cs` | ~470 | **User-driven mic plumbing** (decoupled from Takeover). Wake-word loop + push-to-talk hook, the serialized `RequestVoiceCommand` funnel, `RefreshVoiceInputModes`, `StopVoiceInput`, wake-name phonetic expansion, and `OnWakeWordHeard` (Tier-0 "listen before speaking"). Partial opens at `:24`. |
| `Services/AutonomyService.VoiceCommands.cs` | ~1,047 | **The "Hey Bambi" command grammar** (v2). The `VoiceCommandIntent` model, the whole intent table (`VoiceCommandIntents`, `:125`), fuzzy matching (`MatchVoiceIntent`), the listen/chain state machine (`TryHandleVoiceCommandAsync` / `ListenForCommandAsync`), and `ExecuteIntentAndConfirm`. Partial opens at `:29`. |

**App wiring (`App.xaml.cs`):**
- Static accessor declared `:345` (`public static AutonomyService Autonomy`); constructed in
  `OnStartup` at `:1550`.
- Offline speech services it depends on: `App.Speech` (Vosk "repeat after me" recognizer, declared
  `:347`, ctor `:1555`) and `App.WakeWord` (sherpa-onnx KWS "Hey Bambi" spotter, declared `:349`,
  ctor `:1560`). Both report `IsAvailable=false` (no model/mic) instead of throwing.
- `App.MantraVoice` (`MantraVoiceService`, ctor `:1628`) loads per-mod `mantras.json` on demand.
- Deferred voice-mode arm-up at `ApplicationIdle`, after warming both models off-thread, then
  `Autonomy?.RefreshVoiceInputModes()` on the UI thread (`:1765-1779`).
- Opt-in resume-on-startup at `:2008-2027` (see §2).
- `Autonomy?.Stop()` inside the panic-grade `KillAllAudio()` teardown (`:831`, method at `:792`).
- `Autonomy?.Dispose()` on shutdown (`:3269`).

---

## 2. Lifecycle — Start / Stop / opt-in resume

### 2a. `Start()` (`:430`) and `CanStart()` (`:598`)
`CanStart` requires **all three**: `AutonomyModeEnabled` && `AutonomyConsentGiven` &&
`App.Patreon?.HasPremiumAccess == true`. **No level check** — the "Lv.100" unlock is UI-only (§9.1).
`Start()` marshals to the UI thread (timers must be created there or they never fire), is
**re-entrancy-guarded** on `_isEnabled` (`:460` — three entry points plus remote/chat), then sets
`_isEnabled`, starts the idle/random/heartbeat timers, `UpdateMood()`, arms opt-in mic modes via
`RefreshVoiceInputModes()`, and raises `EnabledChanged(true)`. A 2 s verify timer logs whether the
timers actually armed.

### 2b. `Stop()` (`:507`)
Clears `_isEnabled`, `StopAllTimers()`, `CancelActivePulses()` (restores spiral/pink-filter opacity,
stops autonomy-started bubbles/bouncing-text), bumps `_globalPulseGeneration` to invalidate pending
pulse callbacks, and raises `EnabledChanged(false)`. **Stop does NOT tear down the mic modes** — they
are owned by "She's Listening" and follow their own toggles (explicit comment `:513-515`). Only
`Dispose()` (`:2056`) and `StopVoiceInput()` (the privacy pill) tear the mic down.

### 2c. Opt-in resume-on-startup (a deliberate fix)
**Takeover always starts OFF on launch.** In `InitializePatreonAndSyncAsync` (`App.xaml.cs:2008-2027`):
only if `AutonomyResumeOnStartup` **and** `AutonomyModeEnabled` **and** `AutonomyConsentGiven` (and
Patreon access) does it auto-`Start()`. `AutonomyResumeOnStartup` **defaults false**. If the enabled
flag persisted but resume-on-startup is off, it **clears `AutonomyModeEnabled`** so the UI shows OFF
on a fresh launch (`:2022-2027`). This fixed "it stays on after a restart." The enabled/consent flags
persist so the toggle *remembers its label*, but the service does not auto-run without the explicit
opt-in.

### 2d. Dev/test affordances
`ForceStart()` (`:397`, 30 s test interval, bypasses `CanStart`), `TestTrigger()` (`:307`, bypasses
cooldown), `TestVoiceCommand()` (`:345`, forces a mantra, still gates on speech availability + avatar
+ mic consent). All pop `MessageBox`es and are not part of normal flow.

---

## 3. The action / decision loop

### 3a. Timers → tick → gate
- **Random timer** (`ScheduleNextRandomTick`, `:673`): interval = `AutonomyRandomIntervalSeconds` ×
  jitter (0.667–1.333, i.e. ±~33% around the slider) ÷ `GetTimeMultiplier()`, clamped 15–900 s. In
  `_forceTestMode` it is a flat 30 s. On a tick that **can't act** it re-schedules a short **12–24 s
  retry** instead of a full fresh interval (`:865`) so cadence tracks the slider. `_nextRandomFireTime`
  + `_lastRandomIntervalSeconds` back the avatar countdown bar (`NextRandomFireFraction`, `:243`).
- **Idle timer** (`StartIdleTimer`, `:620`): fires after `AutonomyIdleTimeoutMinutes` of no
  `ReportUserActivity()`. Reset on activity.
- **Heartbeat timer** (`:752`): logs state every 30 s (no behavior).
- **`CanTakeAction()`** (`:882`) — the gate every tick passes through: not disabled, not on cooldown,
  `BrowserMedia.ShouldDeferInterruptions != true` (don't fire over a user's browser video),
  `InteractionQueue.IsBusy != true` (don't interrupt a fullscreen video/bubble-count/lock-card), and
  time-since-last ≥ `AutonomyCooldownSeconds`.

### 3b. `SelectAction` (`:991`) — weighted pick
Builds a candidate list from the `AutonomyCanTrigger*` settings with base weights: Flash 30,
Subliminal 25, Comment 20, PinkFilter 20, WebVideo 20, SpokenMantra 18, Video 15, Bubbles 15,
MindWipe 15, BouncingText 15, LockCard 10, Wallpaper 10. Then `ApplyMoodWeights` (time-of-day mood
nudges) and `ApplyIntensityScaling` (Video/BrainDrain scale with `AutonomyIntensity`), then a weighted
random draw. **Vestigial:** `BrainDrainPulse`, `SpiralPulse`, and `BubbleCount` are **removed from the
candidate list** (comments at `:1009`, `:1026`, `:1067`) even though their enum values, settings
(`AutonomyCanTriggerBrainDrain`/`...Spiral`/`...BubbleCount`, all default true), `PerformAction`
cases, and mood modifiers still exist. Those three settings are dead for the auto-scheduler (§9.4).
**SpokenMantra** is self-gating — only a candidate when speech is available, mic consent is given,
neither wake-word nor push-to-talk is armed, the recognizer is idle, and the active mod ships mantras
(`:1056-1065`). **WebVideo** is excluded while a mandatory video plays or during a web-video cool-off
(`:1041-1044`).

### 3c. `ExecuteAutonomousAction` (`:931`) → `PerformAction` (`:1187`)
`ShouldAnnounce()` rolls against `AutonomyAnnouncementChance` (default 50%). If announcing:
`AnnounceAction` giggles a per-type phrase (voiced if the mod ships matching event audio), then a
2 s delay, then the effect. `PerformAction` (on the UI thread) shows the `TakeoverAnnouncerOverlay`
HUD banner (only when `announce` is true, and never for `Comment` — `TakeoverEffectLabel`, `:1167`),
sets `IsActionInProgress` (drives the Cult-Bunny +50% XP bonus), and `switch`es to the driver. Then
`StartCooldown()` and `ActionTriggered` fire.

---

## 4. Voice mechanics (mic-driven; "She's Listening"-owned)

Three distinct capabilities, all funneled through one serialized entry point so the single-session
recognizer is never double-opened.

### 4a. The funnel — `RequestVoiceCommand(allowCommands)` (`Voice.cs:121`)
Atomically claims `_voiceBusyFlag` (drops re-entrant calls), cancels the wake loop's wait so the mic
session releases, waits for the recognizer to free, then: on user-initiated paths first tries the
**command grammar** (`TryHandleVoiceCommandAsync`); on a miss it falls back to a **mantra** *only if*
`SpokenMantrasEnabled`; the auto-scheduler and dev test pass `allowCommands:false` so they always
deliver a mantra.

### 4b. Wake-word ("Hey Bambi") — `Voice.cs:196-313`
`WakeLoopAsync` prefers the sherpa-onnx **KWS spotter** (`App.WakeWord`) when its model is installed
(better on the OOV name "Bambi"), else falls back to **Vosk** with a phonetically-expanded grammar
(`ExpandWakeVariants`, `bambi`→`bamby/bambie/…`). On a hit → `OnWakeWordHeard` (`:315`): **Tier-0**
behavior (listen *before* speaking, like Alexa) — pop the "listening" dots bubble, stash the ack line,
open the command mic immediately; the ack is only *spoken* if you stay silent. A command that rode in
on the wake utterance ("hey bambi show me bubbles") is handled inline (`TryHandleInlineCommand`,
`:372`).

### 4c. Push-to-talk — `Voice.cs:427-468`
A `GlobalKeyboardHook` on `PushToTalkKey()` (default **F8**). On press → `OnWakeWordHeard()` (same
Tier-0 flow). Decoupled from `_isEnabled` — works whenever PTT is armed + engine available.

### 4d. Spoken mantras ("repeat after me") — `AutonomyService.cs:1821-1949`
`TriggerSpokenMantra()` → funnel → `RunSpokenMantraAsync`: pull `App.MantraVoice.NextMantra()`, speak
the prompt (voiced if the clip ships), **open the mic only after she finishes speaking** (waits the
clip duration + spins until `AvatarWindow.IsSpeaking` clears — avoids self-matching), then
`App.Speech.RecognizePhraseAsync(phrase, 10 s)`. One gentle retry on any non-match. On a match: speak
the bespoke response + `App.Mantra.TryCompleteMantra()`. Raises `VoicePromptStarted`/`VoicePromptFinished`.
Content is per-mod `mantras.json` under `Resources/sounds/companion_audio/mods/builtin-*/` (ships for
`builtin-bambisleep`, `builtin-sissyhypno`, `builtin-locked`).

### 4e. "Hey Bambi" command grammar — `VoiceCommands.cs`
A closed grammar of ~35 intents (safety/"red", bubbles, video +pause/resume, flash-once,
subliminals, bouncing text, spiral, pink filter, wipe-once, lock-once, quiz-once, keyword triggers,
count/freeze/shake, deeper, **takeover on/off**, session pause/resume, volume mute/unmute/louder/
quieter, stop-listening, "again"/"more" replay, help, explicit mantra). Vosk is grammar-constrained to
the aliases; the transcript is fuzzy-matched (`MatchVoiceIntent`, floor 0.5 for content nouns, 0.6 for
terse utility verbs). Supports **command chaining** (up to 3 follow-ups without re-waking) and a polite
re-listen on a near-miss. `takeover_off` is **blocked during Lockdown** (`:513`, #514). "panic"/"red"
routes to the exact panic-key teardown.

---

## 5. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

This is the section to read first. `App.Autonomy` is driven from the UI, the avatar, remote control,
the AI voice-command grammar, and its own timers.

### 5a. Who starts / stops Takeover (the trigger map)

| Caller | `file:line` | What it does |
|---|---|---|
| **Takeover tab toggle** | `MainWindow.Autonomy.cs:82-110` (`ChkAutonomyEnabled`), `:126-189` (`BtnAutonomyStartStop_Click`) | The canonical on/off. Runs the consent dialog on first enable, gates on `HasPremiumAccess`, then `Start()`/`Stop()`. **Lockdown blocks stopping a running Takeover** (`:136`, #514). |
| **"She's Listening" Exclusive** | `MainWindow.SheListening.cs` | Mirrors the mic toggles (wake/PTT/mantras) onto the Takeover-tab controls so there is one source of truth; each change calls `RefreshVoiceInputModes()`. |
| **Startup opt-in resume** | `App.xaml.cs:2008-2027` | Auto-`Start()` only if `AutonomyResumeOnStartup` (§2c). |
| **Engine Start** | `MainWindow.StartStop.cs:228` | Starting the session also starts Takeover if enabled+consented. |
| **Avatar quick-menu / chat** | `AvatarTubeWindow.ChatInput.cs:1043`; also `.Speech.cs:1258` / `.ChatInput.cs:316` call `ReportUserActivity()` | The avatar's own menu can start it; typed/spoken activity resets the idle timer. |
| **"Hey Bambi" voice command** | `AutonomyService.VoiceCommands.cs:499` (`takeover_on` → `App.Autonomy?.Start()`), `:520` (`takeover_off` → `Stop()`, Lockdown-blocked) | She can take/release control by voice. |
| **Remote control** | `RemoteControlService.cs:791`, `:1130` | Partner/companion-app remote verb. |
| **Various window paths** | `MainWindow.xaml.cs:588/839`, `MainWindow.WindowChrome.cs:370`, `MainWindow.Settings.cs:385` | Restore/settings/chrome paths that re-arm it when appropriate. |
| **Global teardown** | `App.xaml.cs:831` (`KillAllAudio`) | Panic-grade stop-everything also stops Takeover. |

Settings changes re-arm live via `RefreshRandomTimer()` / `RefreshIdleTimer()` /
`RefreshVoiceInputModes()` (handlers in `MainWindow.Autonomy.cs:266/274/282/448/462/481/513`).

### 5b. How it drives other services (the effect map — `PerformAction`, `:1213-1348`)

| Action | Drives | Notes |
|---|---|---|
| Flash | `App.Flash.TriggerFlashOnce()` | Standalone one-shot (no running session needed). |
| Video | `TriggerVideoSafely()` → `App.Video.TriggerVideo()` | **No `strictOverride`** — a Takeover video is a plain mandatory video following global `StrictLockEnabled` (`:1366-1387`). Skipped if browser media is deferring. |
| Subliminal | `App.Subliminal.FlashSubliminal()` | |
| StartBubbles | `App.Bubbles.Start(bypassLevelCheck:true)`, auto-stop after 30 s | Guarded by `_bubblesPulseActive`. |
| Comment | `MakeComment` → `App.Ai.GetBambiReplyExAsync` (if `AiChatEnabled` + `App.Ai.IsAvailable`) else preset giggle | Refusals silently dropped on this surface. |
| MindWipe | `App.MindWipe.TriggerOnce()` | |
| LockCard | `App.LockCard.ShowLockCard()` | Single card, no continuous service. |
| PinkFilterPulse | `PulsePinkFilter` — boost opacity, start `App.Overlay`, restore after 30 s | Respects the user moving the slider mid-pulse (#441a, `:1687/1740`). |
| SpiralPulse | `PulseSpiralOverlay` | **Only reachable via voice command / dead auto-path** (not an auto candidate). Skipped during an AI session. |
| BouncingText | `App.BouncingText.Start(bypassLevelCheck:true)`, auto-stop 30 s | |
| BubbleCount | `App.BubbleCount.TriggerGame(forceTest:true)` | Auto path is dead; reachable via `count_once` voice command. |
| WebVideo | `TriggerWebVideoFullscreen` → `App.BrowserMedia.BeginTakeover(...Autonomy)` + `MainWindow.NavigateToUrlInBrowser(url, autoPlayFullscreen:true)` | Picks from `AvatarTubeWindow.KnownVideoLinks`, dedupes via `_shownWebVideos`. Lifecycle owned by `BrowserMediaService`. |
| WallpaperShuffle | `App.Wallpaper.Activate()/Shuffle()`, auto-deactivate 30 s unless user enabled it | |
| SpokenMantra | `TriggerSpokenMantra()` (§4d) | |
| BrainDrainPulse | `PulseBrainDrain` | **Dead auto-path** (removed from candidates); "deeper" voice command uses `App.BrainDrain.Start` instead. |

### 5c. SessionEngine relationship
Takeover is **independent of the engine/session** — `SelectAction` checks only Autonomy-specific
settings (comment at `:999`), and it can run with no session. The two *coordinate* defensively:
`PulseSpiralOverlay`/`PulsePinkFilter` **bail if `App.IsSessionRunning`** (`:1532`, `:1639`) because
the AI session owns the overlays itself. Takeover does not call into `SessionEngine`.

### 5d. Coexistence gates (what makes it stand off)
- **InteractionQueue** — `CanTakeAction` refuses while `InteractionQueue.IsBusy` (an active fullscreen
  video / bubble-count / lock-card). Takeover itself does **not** hold the queue slot for its own
  pulses; the underlying services (video/lock-card) claim it when they run.
- **BrowserMediaService** — `CanTakeAction` refuses while `ShouldDeferInterruptions`; the WebVideo
  candidate additionally checks `ShouldDeferNewVideo` and `App.Video.IsPlaying` to avoid **stacked
  video/audio** (BUG-XRFQH4AHDN). `IsWebVideoActive` now delegates to `BrowserMedia.IsPlaying` (sees
  user-started playback too, `:110`).
- **Lockdown** — a running Takeover can't be stopped while `App.Lockdown.IsActive` (tab button `:136`,
  voice `takeover_off` `:513`).

### 5e. "She's Listening" voice-input decoupling
The mic modes are **not** gated on `_isEnabled` — `RefreshVoiceInputModes()` deliberately arms
wake-word/PTT from their own settings + `MicConsentGiven` + engine availability (explicit comment
`Voice.cs:64-67`), so "Hey Bambi" works with Takeover off. `Stop()` leaves them running; only
`Dispose()`/`StopVoiceInput()` tear them down. The auto-scheduler's surprise mantra is **suppressed**
whenever the user has armed wake-word or push-to-talk (`SelectAction:1058-1059`) — if she's driving the
mic, the app won't surprise-open it.

### 5f. Consent + Patreon gating (two separate consents)
1. **`AutonomyConsentGiven`** — the takeover consent (dialog on first enable, `MainWindow.Autonomy.cs:71/165`).
2. **`MicConsentGiven`** — a *separate* gate; the mic never opens until true. Enforced in
   `SelectAction` (mantra candidate), `RefreshVoiceInputModes`, and even the dev `TestVoiceCommand`
   (`:371`, pops `MicConsentDialog`).
- **Patreon:** `CanStart` requires `HasPremiumAccess` (canonical live gate, **not** the stale
  `Settings.PatreonTier`, #465). Takeover does **not** require `HasAiAccess`; only the AI-comment
  path additionally needs `App.Ai.IsAvailable` and falls back to preset giggles otherwise.

---

## 6. File map (read-verified `file:line`)

| Point | `file:line` |
|---|---|
| Enums (`AutonomyActionType` etc.) | `AutonomyService.cs:14-53` |
| Class open (main partial) | `AutonomyService.cs:77` |
| `IsWebVideoActive` → BrowserMedia | `AutonomyService.cs:110` |
| Countdown-bar `NextRandomFireFraction` | `AutonomyService.cs:243` |
| `Start` / `CanStart` | `AutonomyService.cs:430` / `:598` |
| `Stop` / `CancelActivePulses` | `AutonomyService.cs:507` / `:537` |
| `ScheduleNextRandomTick` (interval math) | `AutonomyService.cs:673` |
| `OnRandomTick` / `OnIdleTick` | `AutonomyService.cs:841` / `:827` |
| `CanTakeAction` (the gate) | `AutonomyService.cs:882` |
| `ExecuteAutonomousAction` | `AutonomyService.cs:931` |
| `SelectAction` (weights, dead candidates) | `AutonomyService.cs:991` |
| `TakeoverEffectLabel` (HUD cue) | `AutonomyService.cs:1167` |
| `PerformAction` (the effect switch) | `AutonomyService.cs:1187` |
| `TriggerVideoSafely` (no strict override) | `AutonomyService.cs:1373` |
| `TriggerWebVideoFullscreen` | `AutonomyService.cs:1392` |
| `PulseSpiralOverlay` / `PulsePinkFilter` | `AutonomyService.cs:1529` / `:1636` |
| `MakeComment` / `MakeAICommentAsync` | `AutonomyService.cs:1761` / `:1787` |
| `TriggerSpokenMantra` / `RunSpokenMantraAsync` | `AutonomyService.cs:1821` / `:1838` |
| `ShouldAnnounce` / `AnnounceAction` | `AutonomyService.cs:1955` / `:1961` |
| `UpdateMood` / `GetTimeMultiplier` | `AutonomyService.cs:1979` / `:1995` |
| `StartCooldown` | `AutonomyService.cs:2016` |
| `OnContextTrigger` | `AutonomyService.cs:2042` |
| `Dispose` | `AutonomyService.cs:2056` |
| Voice partial open + `_voiceBusy` | `AutonomyService.Voice.cs:24` / `:30` |
| `RefreshVoiceInputModes` (decoupled) | `AutonomyService.Voice.cs:60` |
| `StopVoiceInput` (privacy pill) | `AutonomyService.Voice.cs:100` |
| `RequestVoiceCommand` (the funnel) | `AutonomyService.Voice.cs:121` |
| Wake loop (`WakeLoopAsync`) | `AutonomyService.Voice.cs:232` |
| `OnWakeWordHeard` (Tier-0) | `AutonomyService.Voice.cs:315` |
| `TryHandleInlineCommand` | `AutonomyService.Voice.cs:372` |
| Push-to-talk (`OnPushToTalkKey`) | `AutonomyService.Voice.cs:456` |
| Command partial open | `AutonomyService.VoiceCommands.cs:29` |
| Match thresholds (0.5 / 0.6) | `AutonomyService.VoiceCommands.cs:74` / `:82` |
| `VoiceCommandIntents` table | `AutonomyService.VoiceCommands.cs:125` |
| `takeover_on` / `takeover_off` intents | `AutonomyService.VoiceCommands.cs:495` / `:508` |
| `TryHandleVoiceCommandAsync` / `ListenForCommandAsync` | `AutonomyService.VoiceCommands.cs:718` / `:789` |
| `MatchVoiceIntent` / `ExecuteIntentAndConfirm` | `AutonomyService.VoiceCommands.cs:919` / `:949` |
| `App.Autonomy` accessor / ctor | `App.xaml.cs:345` / `:1550` |
| `App.Speech` / `App.WakeWord` ctors | `App.xaml.cs:1555` / `:1560` |
| `App.MantraVoice` ctor | `App.xaml.cs:1628` |
| Deferred voice arm-up | `App.xaml.cs:1765-1779` |
| Opt-in resume-on-startup | `App.xaml.cs:2008-2027` |
| `Stop()` in `KillAllAudio` / `Dispose()` | `App.xaml.cs:831` / `:3269` |
| Settings region | `Models/AppSettings.cs:3409` (`#region Autonomy Mode (Unlocks Lv.100)`) |
| Takeover tab toggle handlers | `MainWindow/MainWindow.Autonomy.cs` |
| "She's Listening" mirror handlers | `MainWindow/MainWindow.SheListening.cs` |
| HUD cue | `Overlays/TakeoverAnnouncerOverlay.cs` |
| Per-mod mantras | `Resources/sounds/companion_audio/mods/builtin-*/mantras.json` |

---

## 7. Settings that gate & tune it (`Models/AppSettings.cs`, region `:3409`)

| Setting | `:line` | Default | Effect |
|---|---|---|---|
| `AutonomyModeEnabled` | 3416 | **false** | Master on/off (part of `CanStart`). Cleared on launch unless resume opted in. |
| `ShowTakeoverCountdownBar` | 3427 | true | The pink countdown bar under the avatar. |
| `AutonomyConsentGiven` | 3438 | **false** | Takeover consent (required by `CanStart`). |
| `AutonomyIntensity` | 3448 | 5 (1–10) | Scales disruptive-action weights. |
| `AutonomyCooldownSeconds` | 3458 | 30 (10–300) | Min gap between actions. |
| `AutonomyIdleTriggerEnabled` / `AutonomyIdleTimeoutMinutes` | 3470 / 3480 | true / 5 (1–30) | Idle trigger. |
| `AutonomyRandomTriggerEnabled` | 3490 | true | Random trigger. |
| `AutonomyRandomIntervalMinutes` | 3500 | 2 | **LEGACY** — only logged; real cadence uses the seconds field. |
| `AutonomyRandomIntervalSeconds` | 3510 | 60 (30–300) | The real random cadence midpoint. |
| `AutonomyContextTriggerEnabled` | 3521 | false | Context trigger (needs Awareness). |
| `AutonomyTimeAwareEnabled` + `Autonomy{Morning,Afternoon,Evening,Night}Multiplier` | 3531 / 3541-3571 | false | Time-of-day cadence multiplier (`GetTimeMultiplier`). |
| `AutonomyCanTriggerFlash/Video/Subliminal/Bubbles/Comment/MindWipe/LockCard/PinkFilter/BouncingText/Wallpaper` | 3583-3716 | mixed | Per-effect enable for the auto-scheduler. |
| `AutonomyCanTriggerBrainDrain/Spiral/BubbleCount` | 3613 / 3663 / 3693 | true | **Dead for the auto path** (not read by `SelectAction`, §9.4). |
| `AutonomyCanTriggerWebVideo` | 3704 | false | Fullscreen HypnoTube web video. |
| `TakeoverVideosStrict` | 3727 | false | **RETIRED** — no longer read; kept only for deserialization (§9.3). |
| `AutonomyAnnouncementChance` | 3737 | 50 (0–100) | Announce-before-acting chance; 0 also suppresses the HUD banner. |
| `AutonomyResumeOnStartup` | 3751 | **false** | Opt-in auto-arm on launch (§2c). |
| `AutonomyCanTriggerVoiceCommand` | 3765 | true | Takeover's *surprise* auto-mantra (self-gates on speech/consent/idle mic). |
| `SpokenMantrasEnabled` | 3779 | false | "She's Listening" on-demand mantra fallback + Test. |
| `MicConsentGiven` | 3791 | **false** | Mic never opens until true. |
| `SpeechWakeWordEnabled` / `SpeechWakeWords` | 3955 / 3964 | false / "hey bambi" | Wake-word loop. |
| `SpeechPushToTalkEnabled` / `SpeechPushToTalkKey` | 3973 / 3982 | false / "F8" | Push-to-talk. |
| `SpeechHeadphonesMode` | 4009 | false | Allow barge-in (skip the echo guard). |

---

## 8. Where to change X

| Want to… | Edit |
|---|---|
| Add/adjust an autonomous effect | Add a case in `PerformAction` (`:1213`) + a weight in `SelectAction` (`:991`) + an `AutonomyCanTrigger*` setting + a Takeover-tab control. |
| Change the random cadence / jitter / time multiplier | `ScheduleNextRandomTick` (`:673`); jitter `:702`, `GetTimeMultiplier` divide `:709`, clamp 15–900 `:714`. |
| Change what blocks an action | `CanTakeAction` (`:882`). |
| Change announcement text/voice | `_announcementPhrases` (`:138`), `AnnounceAction` (`:1961`); HUD label `TakeoverEffectLabel` (`:1167`). |
| Add/adjust a "Hey Bambi" voice command | The `VoiceCommandIntents` table (`VoiceCommands.cs:125`); use `OnAliases`/`OffAliases` templates; set `VoiceRuleId` for a voiced confirm bark. |
| Change fuzzy-match strictness | `VoiceCommandMatchThreshold` (0.5) / `UtilityCommandMatchThreshold` (0.6), `VoiceCommands.cs:74/82`. |
| Change the wake-word behavior | `WakeLoopAsync` (`Voice.cs:232`); phonetic variants `WakeNameVariants` (`:165`). |
| Change the mantra flow | `RunSpokenMantraAsync` (`:1838`); content in per-mod `mantras.json` (see `MantraVoiceService.cs`). |
| Re-enable a resume-on-startup default | `AppSettings.cs:3745` (`AutonomyResumeOnStartup`) + the `App.xaml.cs:2008` block. |
| Revive BrainDrain/Spiral/BubbleCount auto-candidates | Un-comment the candidate adds in `SelectAction` (`:1009/1026/1067`). |

---

## 9. Gotchas

1. **The "Lv.100" unlock is UI-only.** `CanStart` checks Patreon + consent + enabled — **no level
   check**. A session/remote/voice call can drive Takeover below Lv.100. Don't assume the service
   enforces the gate (mirrors Mind Wipe's Lv.75 gotcha).
2. **"Takeover" = "Autonomy."** All code (service, settings, enum, overlay, dev methods) says
   Autonomy; the UI says Takeover. Grepping either name is correct.
3. **`TakeoverVideosStrict` is RETIRED** (`AppSettings.cs:3720`). Takeover videos are plain mandatory
   videos following the global `StrictLockEnabled` (see the `TriggerVideoSafely` comment, `:1366`).
   The flag is kept only so old `settings.json` deserializes; nothing reads it.
4. **Three `AutonomyCanTrigger*` settings are dead for the auto-scheduler.** `BrainDrain`, `Spiral`,
   and `BubbleCount` were removed from `SelectAction`'s candidate list (comments `:1009/1026/1067`)
   but keep their settings (default true), enum values, `PerformAction` cases, and mood modifiers.
   They only run now via the voice-command grammar (`deeper`, `spiral_on/off`, `count_once`). Don't
   assume flipping those settings changes auto behavior.
5. **Voice input is decoupled from Takeover.** Wake-word / PTT / mantras arm from their own
   toggles + `MicConsentGiven` + engine, NOT from `_isEnabled` (`Voice.cs:64`). `Stop()` intentionally
   leaves them running — only `Dispose()`/`StopVoiceInput()` tear them down. "She's Listening" is
   their home UI.
6. **Two separate consents.** `AutonomyConsentGiven` (takeover) and `MicConsentGiven` (microphone)
   are independent; the mic never opens without the latter, even for dev test.
7. **Surprise mantra is suppressed while the user drives the mic.** If wake-word or PTT is armed,
   `SelectAction` drops the SpokenMantra candidate (`:1058`) so the app doesn't surprise-open the mic.
8. **`AutonomyRandomIntervalMinutes` is legacy** — the real cadence is `AutonomyRandomIntervalSeconds`.
   The minutes field is only logged in diagnostics.
9. **Timers must be created on the UI thread** or they never fire (`Start()` self-dispatches, `:450`).
   Fire-and-forget pulse restores use the root `CLAUDE.md §6` guard pattern
   (`Task.Delay().ContinueWith` + `Dispatcher` null-check + generation counters).
10. **Pulses respect a session and the user.** Spiral/pink pulses bail during `IsSessionRunning`
    (`:1532/1639`); pink-filter restore won't stomp a slider the user moved mid-pulse (#441a).
11. **`GetTimeMultiplier` was previously computed but never applied** — it now actually shortens/
    stretches the random gap (`:709`). If you touch the time-aware settings, that's the consumer.
12. **`KillAllAudio` (`App.xaml.cs:792`) stops Takeover**, but the `mute` voice command deliberately
    does NOT call it (it would kill the very voice loop that heard "mute" — see `VoiceCommands.cs:572`).

---

## 10. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

- **State: mature and shipping**, actively iterated on the voice layer. Branch
  `fix/web-video-interruptions`, HEAD `95586020` (v6.5.0). File sizes at this snapshot:
  `AutonomyService.cs` ~2,069 lines, `AutonomyService.Voice.cs` ~470, `AutonomyService.VoiceCommands.cs`
  ~1,047.
- **Recent deliberate fixes** (all present in-code): Takeover always-off + opt-in resume
  (`AutonomyResumeOnStartup`); mic decoupling into "She's Listening"; the "Hey Bambi" **voice-command
  v2** layer (chaining, replay, help, Lockdown-blocked release); the BrowserMediaService web-video
  gating that stops effects stacking over a user's video (BUG-XRFQH4AHDN); the retired
  `TakeoverVideosStrict`.
- **Known dead/vestigial** (see §9): `BrainDrain`/`Spiral`/`BubbleCount` auto-candidates removed but
  settings/enum/handlers retained; `TakeoverVideosStrict` retired; `AutonomyRandomIntervalMinutes`
  legacy. None are bugs — documented so they aren't "fixed" blindly.
- **No dedicated xUnit coverage** was found for `AutonomyService` in this pass; the standing gate is
  play-test (the memory notes "takeover-rework-design" / "mic-feature-expansion" track the design
  intent, but rely on source, not memory). The pure-ish seams worth a test if regressions appear are
  `ScheduleNextRandomTick`'s interval math and `MatchVoiceIntent`.
- This primer is **new** and not previously committed.

---

## 11. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```
Then: have Patreon access, open the **Takeover** tab, accept consent, click **Start** (or use
`ForceStart()`/`TestTrigger()` via the dev affordances). For voice: drop a Vosk model into
`Resources/Models/vosk/` (and optionally the sherpa-onnx KWS model into `Resources/Models/sherpa-kws/`
for reliable "Hey Bambi"), connect a mic, accept the **mic consent** dialog, then enable wake-word /
push-to-talk / mantras under "She's Listening." Watch `logs/` for `AutonomyService:` Serilog lines
(HEARTBEAT, scheduling, action selection, voice-command matches).
