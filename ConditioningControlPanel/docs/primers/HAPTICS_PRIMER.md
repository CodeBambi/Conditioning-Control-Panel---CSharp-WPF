# Haptics — Feature Primer

> **Purpose.** One-load orientation for the device-vibration feature (Lovense / Buttplug-Intiface /
> Mock, with audio-sync and per-feature patterns) so you can maintain it WITHOUT re-reading the
> ~950-line `HapticService.cs`. §0 is the one-paragraph model. §1 is the provider abstraction. §2 is
> the `HapticService` orchestrator. §3 connection lifecycle. §4 the pattern/command model. §5
> audio-sync. §6 the DTRH director. §7 the (mis-filed) LockdownService. §8 settings. **§9 is the
> load-bearing section — every way a vibration gets fired and every system it touches.** §10
> file map, §11 where-to-change-X, §12 gotchas, §13 dated status.
>
> **Verified against source 2026-07-23** on branch `fix/web-video-interruptions` (HEAD `6571e5f4`,
> v6.5.0). Every `file:line` below was read-verified when written and is git-verifiable, but line
> numbers drift — confirm with a quick read before quoting. §1–§12 track the code and rarely rot;
> **§13 is a dated snapshot — verify with `git log` before acting.**

---

## 0. What Haptics is, in one paragraph

A **premium** subsystem that drives a user's vibrating device from in-app events. It is built on a
small provider abstraction (`IHapticProvider`) with three concrete backends —
**Lovense** (HTTP to the Lovense Remote/Connect local API), **Buttplug.io** (WebSocket to Intiface
Central), and a **Mock** provider (an on-screen toast, no hardware) — selected by a single
`HapticProviderType` setting. One orchestrator, **`HapticService`** (`App.Haptics`), owns all three
provider instances, activates one at a time, translates a slider intensity (0..1) + a `VibrationMode`
enum into device commands, and exposes a large menu of per-feature methods (bubble pop, flash decay,
video background vibe + target-hit spikes, subliminal pulses, level-up / achievement celebrations,
bouncing-text bounces, blink pulses, an avatar easter egg, and an AI `haptic` command). Two
higher-level directors sit on top of it: **`AudioSyncService`** (analyzes a web video's audio into a
`HapticTrack` and streams intensity in sync with playback) and **`DtrhHapticDirector`** (a two-layer
ambient+accent envelope for the Down-the-Rabbit-Hole browser game). The feature is gated behind
`HasPremiumAccess` at the UI (tab lock + enable toggle + connect button); the AI `haptic` command is
additionally clamped to a user ceiling. The whole thing is wired at `App.OnStartup` and can
auto-connect on launch.

---

## 1. The provider abstraction (`Services/Haptics/`)

`IHapticProvider` (`Services/Haptics/IHapticProvider.cs`, 37 lines) is the contract:
`Name` / `IsConnected` / `ConnectedDevices`, three events (`ConnectionChanged`, `DeviceDiscovered`,
`Error`), and five async ops: `ConnectAsync`, `DisconnectAsync`, `VibrateAsync(intensity, durationMs)`,
`StopAsync`, `PingAsync`. `PingAsync` (`:35`) exists specifically because `IsConnected` **can lie** —
the OS routing table can change after connect (e.g. a VPN flip) and leave `IsConnected == true` while
the device is unreachable (see the interface doc-comment and §12.2). The `HapticProviderType` enum
(`:7`) has four members: `None`, `Mock`, `Lovense`, `Buttplug`.

### 1a. LovenseProvider (`LovenseProvider.cs`, 355 lines)
Talks HTTP/JSON to the Lovense app's local server. Two modes (`LovenseConnectionMode`, `:12`):
**Lan** (Lovense Remote on a phone — POST `/command` with JSON, requires a `timeSec`) and **Local**
(Lovense Connect on PC — GET with query args, holds the level until the next command). Default mode
is **Lan** (`:23`). `ConnectAsync` (`:72`) issues `GetToys` and parses the toy list (handles toys as
either a JSON string (Remote) or object (Connect), `:98-114`); it grabs the **last** enumerated toy
id into `_toyId` (`:132` — single-toy control). Intensity → level mapping is 0..1 → **0..20**, with
`intensity <= 0.05` clamped to level 0 ("off") and 0.05..1.0 mapped to levels 3..20 (`:200-204`).
There is a second, differently-shaped mapper `IntensityToLevel` (static, `:345`) used by the
audio-sync pattern path (`SetSyncPatternAsync`) — note the two mappers do NOT agree (§12.6). Short
commands (`durationMs < 500`) are treated as "continuous" and **rate-limited to 5/s** (`:207-224`).
`VibratePatternAsync` (`:279`) collapses an array of levels to a single weighted-average (or a
transient max) command and skips a same-level re-issue within 1s. SSL validation is bypassed **only**
for `127.0.0.1`/`localhost` (self-signed local certs), enforced for real hosts (`:45-50`). `PingAsync`
(`:163`) re-issues `GetToys` with a short 1.5s timeout so VPN-blocked routes fail fast.

### 1b. ButtplugProvider (`ButtplugProvider.cs`, 282 lines)
Wraps the `Buttplug.Client` NuGet package over a WebSocket to Intiface Central
(`ws://127.0.0.1:12345` default, `:18`). `ConnectAsync` (`:37`) connects, scans 2s, then adds **every**
vibrating device to `_activeDevices` (`:73-81`); if none can vibrate it falls back to the first device
(`:83-90`). `IsConnected` is true only when the client is connected **and** at least one active device
exists (`:24`). `VibrateAsync` (`:189`) fans the command out to all devices and schedules a
fire-and-forget auto-stop after `durationMs` (Buttplug has no built-in duration, so this provider
emulates it with a `CancellationTokenSource`-guarded `Task.Delay` → `Stop`, `:222-248`). Live device
add/remove is handled via `DeviceAdded`/`DeviceRemoved`/`ServerDisconnect` events (`:112-147`).
`PingAsync` (`:149`) just returns the cached connected state — the SDK raises `ServerDisconnect` when
the WS drops, so the cached state is reliable and localhost tunnels rarely break.

### 1c. MockHapticProvider (`MockHapticProvider.cs`, 149 lines)
No hardware. `ConnectAsync` (`:27`) immediately reports two fake devices ("Mock Vibrator 1/2"). Every
`VibrateAsync`/`StopAsync` shows a pink **toast window** in the bottom-right (`ShowHapticToast`, `:85`)
with the intensity% and duration. Critically it **reuses a single toast window** — audio-sync fires at
~30 Hz and spawning a Window per call previously crashed the WPF render thread with
`UCEERR_RENDERTHREADFAILURE` after ~60s of leaked HWNDs (`:87-89`). `Error` is declared but never
raised (`#pragma warning disable CS0067`, `:23`). **This is the DEFAULT provider** (§8, §12.1) — a
first run vibrates nothing real, only toasts.

---

## 2. The `HapticService` orchestrator (`HapticService.cs`, 954 lines)

`App.Haptics`. Constructs all three providers up front (`:56-58`), wires their events through to its
own (`WireProviderEvents`, `:103`), and subscribes to `HapticSettings.PropertyChanged` for **live
stop** (`OnSettingsChanged`, `:69`): flipping master `Enabled` off, or flipping off the specific
feature whose event is currently mid-flight (`_currentEventType`), immediately calls `StopAsync`.

- **Active provider.** `_activeProvider` is null until `ConnectAsync` (§3). `IsConnected`,
  `ProviderName`, `ConnectedDevices` all delegate to it. `IsButtplugProvider` (`:43`) is a settings
  read, used to widen durations/anticipation because **Buttplug carries ~1.3s command latency**
  (`SubliminalAnticipationMs` = 1300 for Buttplug else 250, `:48`).
- **Intensity floor.** `MinPerceptibleIntensity = 0.06` (`:214`) — must clear LovenseProvider's
  `<= 0.05 = off` cutoff (#516), so every floor in the service uses 0.06, never 0.05.
  `GetSliderIntensity` (`:220`) clamps a slider value to `[0.06, 1.0]`. The design is "**slider value
  directly controls device power**" — there is no global multiply (see §12.3 on the vestigial
  `GlobalIntensity`).
- **`ApplyVibrationModeAsync(intensity, durationMs, mode, token?)`** (`:230`) is the pattern engine.
  It renders the six `VibrationMode`s (Constant / Pulse / Wave / Heartbeat / Escalate / Earthquake)
  as sequences of `VibrateAsync` calls with `Task.Delay` gaps, cancellable via the optional token.
  Every per-feature method funnels through this (except the video-background loop and ramp, which call
  `VibrateAsync` directly — see §12.4).
- **`TriggerAsync(eventType, sliderIntensity, durationMs)`** (`:335`) is the generic entry: it checks
  master `Enabled` + provider connected + `IsFeatureEnabled(eventType)` (`:317`), sets
  `_currentEventType`, fires the `HapticTriggered` UI event, and vibrates. Feature-name strings
  ("BubblePop", "Video", "Subliminal", …) map to the per-event enable flags.
- **`TestAsync`** (`:359`) returns a `HapticTestResult` (Success / NotConnected / Unreachable): it
  `PingAsync`-verifies reachability first (dropping the connection + returning Unreachable on a VPN
  break), then runs a 3-step 30/60/100% pattern.
- **Special/celebration patterns** live here too: `LevelUpPatternAsync` (`:505`),
  `AchievementPatternAsync` (`:541`), `RampUpAsync` (`:573`), `FlashDecayVibeAsync` (`:602`, 2s
  exponential decay), `FlashClickVibeAsync` (`:648`), `BubblePopAsync` (`:692`, with a 2s combo
  counter), `BouncingTextBounceAsync` (`:721`), `BlinkPulseAsync` (`:744`),
  `TriggerSubliminalPatternAsync` (`:862`, duration keyed off trigger words), and
  `AvatarEasterEggPatternAsync` (`:910`, ~8s). Each re-checks its enable flag between steps so a
  live-toggle-off aborts mid-pattern.
- **Live control:** `LiveIntensityUpdateAsync` (`:411`, a 1.5s preview for slider drags),
  `SetSyncIntensityAsync` (`:438`, one continuous 200ms sample clamped to the audio-sync min/max) and
  `SetSyncPatternAsync` (`:468`, a float[] pattern; routes to Lovense's `VibratePatternAsync` or falls
  back to the average level on other providers).
- **Ping watchdog:** a 30s `System.Threading.Timer` started on connect (`StartPingTimer`, `:161`);
  requires **3 consecutive** ping failures (~90s) before disconnecting, so one Wi-Fi/cloud blip
  doesn't kill the session (#302, `:29-33`, `PingTickAsync` `:174`).
- **`Dispose`** (`:941`): unhooks settings, cancels the flash/video CTS, stops the ping timer,
  `DisconnectAsync().Wait(1000)`.

---

## 3. Connection lifecycle

- **`ConnectAsync`** (`:110`) first `DisconnectAsync`es any current provider, selects the concrete
  provider from `Settings.Provider` (`None → null`, `:116-122`), pushes the saved URL into it
  (`SetUrl`, `:131-138`), connects, and — **on success — auto-sets `Settings.Enabled = true`** and
  starts the ping timer (`:144-147`). A null provider (`None`) raises `Error` and returns false.
- **Auto-connect on startup:** `App.OnStartup` checks `Settings.AutoConnect && Provider != Mock`
  (`App.xaml.cs:1534`) and fires `AutoConnectHapticsAsync()` (`App.xaml.cs:1536` → body at
  `App.xaml.cs:2450`) — a 2s delay then `Haptics.ConnectAsync()`, silent on failure. **Mock is
  deliberately excluded** from auto-connect.
- **Construction:** `Haptics = new HapticService(Settings.Current.Haptics)` (`App.xaml.cs:1499`),
  static property declared `App.xaml.cs:340`. `AudioSync` is built one line later (`:1500`).
- **Disposal:** `Haptics?.Dispose()` then `AudioSync?.Dispose()` on shutdown (`App.xaml.cs:3291-3292`).
- **Manual connect/disconnect/test** come from the Haptics tab (§9d): `BtnHapticConnect_Click`
  (`MainWindow.Haptics.cs:205`) toggles connect/disconnect and paints the status label;
  `BtnHapticTest_Click` (`:298`) runs `TestAsync` and surfaces the Unreachable/NotConnected results.

---

## 4. The command / pattern model

There are **two unrelated "pattern" worlds** — don't conflate them.

### 4a. The AI `haptic` command
`Models/CommandData/HapticCommandData.cs` (7 lines) — a `record (double Intensity, int Duration)`.
`Services/Commands/HapticCommand.cs` (34 lines) clamps `Duration` to `[0, 10]s`
(`MaxDurationSec = 10`, `:10`) and clamps `Intensity` to the user ceiling
`CompanionPrompt.MaxAiHapticIntensity` (default **0.6**, `:19`), then fires
`ApplyVibrationModeAsync(..., VibrationMode.Pulse)` (`:24`). Built by `CommandFactory.cs:36`,
dispatched from `AiCommandService.cs:166`; the model is emitted by the AI per `PromptService.cs:66-67`
("vibrate"/"buzz me"/"haptic" → `{command:"haptic", data:{Intensity, Duration}}`). The ceiling slider
lives in the Lab tab (`MainWindow.Patreon.cs:1622/1703`).

### 4b. The six built-in `VibrationMode`s
`Models/HapticSettings.cs:10` — `Constant, Pulse, Wave, Heartbeat, Escalate, Earthquake`. Rendered by
`ApplyVibrationModeAsync` (§2). This is what per-event `*Mode` settings select.

### 4c. Deeper stock haptic patterns (creator-authored keyframes)
`Models/Deeper/StockHapticPatterns.cs` (103 lines) — six **named keyframe curves** ("Pulse", "Throb",
"Wave", "Steady", "Climax", "Tease", `:13`) as `[t_frac, intensity]` lists. `Sample()` (`:43`)
interpolates a curve into an evenly-spaced `float[]` (N clamped to [8, 64] by duration) scaled by
intensity; `TryGet` / `SeedCustomFrom` support the Deeper editor's curve UI. These feed
`HapticService.SetSyncPatternAsync` at dispatch time — see §9c (Deeper haptic tracks).

### 4d. The two `HapticTrack` classes (DIFFERENT — a naming collision, §12.5)
- **`Models/HapticTrack.cs`** (203 lines, namespace `ConditioningControlPanel.Models`) — a **runtime
  audio-analysis buffer**: chunked `float[]` intensity samples indexed by time (`SamplesPerSecond`,
  `ChunkDurationSeconds`, sparse/progressive chunk loading, linear-interpolated `GetIntensityAt`,
  `HasDataForTime`, `GetBufferAhead`). Consumed by `AudioSyncService` (§5). No JSON, no persistence.
- **`Models/Deeper/HapticTrack.cs`** (59 lines, namespace `ConditioningControlPanel.Models.Deeper`) —
  a **JSON schema**: a `HapticTrack{ id, List<HapticEvent> events }` where each `HapticEvent` carries
  `start/duration/intensity` and **exactly one of** `pattern_name` (a StockHapticPatterns name) or
  `custom_pattern` (raw keyframes), plus an `activation`. It's the serialized shape inside a
  `.ccpenh.json` Deeper enhancement; `IHapticPatternTarget` lets the editor bind either. Nothing to do
  with the audio-analysis class above beyond the shared type name.

---

## 5. Audio-sync (`Services/AudioSyncService.cs`, 380 lines)

Streams device intensity in time with a **web video's** audio. Constructed
`AudioSync = new AudioSyncService(Haptics, Settings.Current.Haptics.AudioSync)` (`App.xaml.cs:1500`);
`App.AudioSync` is nullable (`App.xaml.cs:341`). Settings in `Models/AudioSyncSettings.cs` (157 lines).

Flow:
1. **`OnVideoDetectedAsync(url)`** (`:91`): bails (but still fires `ProcessingCompleted` so the page's
   JS overlay unblocks — §12.7) if audio-sync disabled or haptics not connected, or the URL doesn't
   look like a video. Otherwise builds a `ChunkManager`, downloads+analyzes the first chunk (bass /
   RMS / onset weighted, per `AudioSyncSettings`), waits up to 2min for the first chunk, then signals
   ready. Analysis produces the runtime `HapticTrack` (§4d, the audio-analysis one).
2. **`OnPlaybackStateUpdate(currentTime, paused)`** (`:163`): the JS reports playhead + paused at
   frame rate. Handles pause (→ `StopAsync`), triggers background chunk processing, and computes a
   **look-ahead** time = `currentTime + (300 + SubliminalAnticipationMs + ManualLatencyOffsetMs)` ms
   to compensate device+network latency (`:214`); every 5s it force-resyncs to the exact time to kill
   drift (`:199-209`). It reads `track.GetIntensityAt(lookAhead)` and calls `SendHapticAsync`.
3. **`SendHapticAsync`** (`:319`) maps track intensity [0,1] into the device range
   `[0.08, LiveIntensity]` (preserving dynamics while guaranteeing the device responds), then
   `HapticService.SetSyncIntensityAsync` (which itself clamps to the `Min/MaxIntensity` settings).
4. Seek (`OnVideoSeek`, `:236`) pauses the video while an unloaded chunk loads; `OnVideoEnded` /
   `StopSync` / `Reset` tear down.

**Wiring into the browser:** the audio-sync JS is injected only when
`Haptics.AudioSync.Enabled && App.Haptics.IsConnected` (`MainWindow.Browser.cs:457`), and a late
device connection re-arms it via `App.Haptics.ConnectionChanged` (`MainWindow.Browser.cs:967`). This
path is for the in-app WebView2 browser's `<video>`, distinct from mandatory LibVLC video (§9a).

---

## 6. The DTRH haptic director (`DtrhHapticDirector.cs`, 403 lines)

A `static` class that drives haptics during a **Down-the-Rabbit-Hole** browser-game descent. This is
device-haptics plumbing layered on the DTRH feature — it is **not** DTRH gameplay (see the DTRH
primer for the game itself). It is deliberately **not** a 1:1 event→buzz mapper because the game emits
dozens of events/min and Buttplug adds ~1.3s latency. Instead it keeps a **two-layer envelope**:

- **AMBIENT** (`AmbientTick`, `:347`): a slow "depth gauge" floor driven by the page's throttled
  `{running, depth, melt}` feed (`OnHapticState`, `:203`). Scaled from `DtrhAmbientIntensity` by the
  game's 0..1 depth and the Surfacing "melt", issued as long **30s** Constant commands refreshed
  rarely (the same near-zero-traffic trick as the video background vibe). A 5s `System.Threading.Timer`.
- **ACCENTS** (`PlayAccent`, `:244`): short pattern spikes on meaningful game events, tapped from the
  bark event stream. A curated verb table (`Map`, `:35`) assigns each event a **tier** (1/2/3), a
  fraction of `DtrhIntensity`, a `VibrationMode`, and a duration. Tier 3 preempts; tier 2 respects a
  shared cooldown; tier 1 micro-events are **coalesced** into one swell scaled by count
  (`Coalesce`/`FlushCoalesced`, `:297-322`). `DtrhDensity` (0=Sparse/1=Balanced/2=Rich) scales the
  cooldowns and gates tier-1 entirely at Sparse.

`Ready` (`:91`) requires host active, not test-mode, `Enabled && DtrhEnabled`, and
`App.Haptics.IsConnected`. Lifecycle taps come from `DtrhHostService.cs`: `OnLaunch` (`:133`),
`OnRunStarted` (`:276`), `OnGameEvent` (`:286`), `OnHapticState` (`:292`), `OnRunEnded` (`:551`),
`OnWorldFreeze` (`:738`), `OnVideoCovering` on/off (`:790`/`:829`), `OnClosed` (`:957`). It yields the
device to a covering mandatory video (`OnVideoCovering`, `:194`) and an in-world Freeze
(`OnWorldFreeze`, `:180`), and stops everything on run-end/close/settings-off. Buttplug durations are
doubled (`:274`).

---

## 7. `LockdownService.cs` — MIS-FILED, not a haptics file (§12.8)

`Services/Haptics/LockdownService.cs` (226 lines) sits in the Haptics folder but its namespace is
`ConditioningControlPanel.Services` and it has **zero** haptics coupling. It manages **lockdown mode**
— a timed state that forces `StrictLockEnabled = true` / `PanicKeyEnabled = false`, writes a
`lockdown_recovery.json` so a crash mid-lockdown can't leave the panic key permanently stuck off
(#162), counts down, and exits via timer or the secret phrase `"let me out"` (`TryExitWithPhrase`,
`:189`). Treat the folder placement as a filing accident: grepping `Services/Haptics/` for the haptics
feature will pull this in and it means nothing. (Exactly the same red-herring shape as Mind Wipe
living under `Services/LockCard/`.)

---

## 8. Settings that gate & tune it (`Models/HapticSettings.cs`, 347 lines)

`App.Settings.Current.Haptics`. All properties raise `PropertyChanged` (drives live-stop, §2).

| Setting | `:line` | Default | Effect |
|---|---|---|---|
| `Enabled` | 70 | **true** | Master gate for all triggers. Auto-set true on successful connect (`HapticService.cs:145`). UI enable is Patreon-gated (§9e). |
| `Provider` | 76 | **Mock** | Which backend `ConnectAsync` activates. |
| `AutoConnect` | 82 | false | Connect on startup (skipped for Mock, `App.xaml.cs:1534`). |
| `GlobalIntensity` | 88 | 0.7 | **Largely vestigial (§12.3)** — only the top slider's live-preview value; never multiplied into event triggers. |
| `{Feature}Enabled` | 94–157 | all true | Per-event gates: BubblePop, FlashDisplay, FlashClick, Video, TargetHit, Subliminal, LevelUp, Achievement, BouncingText, Blink. |
| `{Feature}Intensity` | 160–218 | 0.5 (TargetHit 0.7, Blink 0.6) | Per-event device power (slider directly = power). |
| `{Feature}Mode` | 221–279 | Constant (TargetHit/Subliminal/BouncingText/Blink=Pulse, LevelUp=Escalate, Achievement=Heartbeat) | Per-event `VibrationMode`. **`VideoMode` is set in UI but never read (§12.4).** |
| `LovenseUrl` | 281 | `http://192.168.1.1:30010` | Lovense endpoint. |
| `ButtplugUrl` | 287 | `ws://localhost:12345` | Intiface endpoint. |
| `DtrhEnabled` | 300 | true | DTRH director on. |
| `DtrhIntensity` | 308 | 0.6 | DTRH accent ceiling. |
| `DtrhAmbientIntensity` | 316 | 0.12 | DTRH ambient floor at full depth (0 = accents only). |
| `DtrhDensity` | 324 | 1 | 0 Sparse / 1 Balanced / 2 Rich. |
| `AudioSync` | 336 | `new()` | Nested `AudioSyncSettings` (see below). |

`BlinkEnabled` has **no Haptics-tab UI** — it's JSON-configurable only, a deferred polish item
(doc-comment `:148-152`).

`AudioSyncSettings` (`Models/AudioSyncSettings.cs`): `Enabled` (default **false**, `:15`),
`Sensitivity`, `BassWeight`/`RmsWeight`/`OnsetWeight` (0.40/0.35/0.25), `Smoothing`, `MinIntensity`
(0.05) / `MaxIntensity` (1.0), `ManualLatencyOffsetMs` (±600), `ChunkDurationSeconds` (300),
`MinBufferAheadSeconds` (120), `LiveIntensity` (1.0). These are the only haptic settings with
`[JsonProperty]` snake_case names.

**Setup wizard** — `Windows/HapticsSetupWindow.xaml(.cs)` (152 lines .cs): a 3-slide tutorial with a
**local `Provider` enum `{ None, Lovense, Buttplug }`** (`:9`, distinct from the service's 4-member
`HapticProviderType` — Mock has no tutorial). Purely informational; opened by `BtnHapticsHelp_Click`
(`MainWindow.Haptics.cs:196`). It does not itself set the provider.

---

## 9. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

This is the section to read first. There is no single command sink — features call `App.Haptics?.*`
methods directly, always null-safe (`App.Haptics` is `?.`-guarded everywhere and each method early-
returns when not `Enabled`/connected/feature-enabled, so fire-and-forget is safe).

### 9a. The trigger map (who fires a vibration)

| Caller | `file:line` | Method |
|---|---|---|
| **Bubble-pop minigame** | `Services/BubbleService.cs:963` | `BubblePopAsync()` (combo-scaled) |
| **Flash images** (display) | `Services/Flash/FlashService.cs:1368/1395/1431` | `FlashDecayVibeAsync()` (2s decay) |
| **Flash click** | `Services/Flash/FlashService.cs:1704` | `FlashClickVibeAsync()` |
| **Mandatory video** (background bed) | `Services/Video/VideoService.cs:1723` | `StartVideoBackgroundVibeAsync()` on play |
| **Mandatory video** (attention target hit) | `Services/Video/VideoService.cs:2847` | `VideoTargetHitAsync()` (intensity spike) |
| **Mandatory video** (teardown) | `Services/Video/VideoService.cs:4073` | `StopVideoBackgroundVibeAsync()` |
| **Subliminals** | `Services/Subliminal/SubliminalService.cs:222/289/379/584` (anticipation read `:580`) | `TriggerSubliminalPatternAsync(text)` |
| **Bouncing text** (edge bounce) | `Services/Subliminal/BouncingTextService.cs:421` | `BouncingTextBounceAsync()` |
| **Blink trainer** (Lab) | `Services/BlinkTrainerService.cs` → `BlinkPulseAsync` | per-blink pulse |
| **Level-up** | `Services/Progression/ProgressionService.cs:95`; also `Services/Companion/CompanionService.cs:289` | `LevelUpPatternAsync()` |
| **Achievement / quest complete** | `Services/Progression/AchievementService.cs:754-755`; `Services/Progression/QuestService.cs:979-980` | `AchievementPatternAsync()` (fired twice for emphasis) |
| **Avatar 20-click easter egg** | `Services/Progression/AchievementService.cs:583` | `AvatarEasterEggPatternAsync()` (~8s) |
| **Avatar speech triggers** | `AvatarTube/AvatarTubeWindow.Speech.cs:1789` | `TriggerSubliminalPatternAsync(trigger)` |
| **Keyword triggers** (voice/typed) | `Services/KeywordTriggerService.cs:1190/1442` | `TriggerSubliminalPatternAsync(keyword)` |
| **Gaze minigame** (Lab) | `Lab/GazeMinigame/GazeMinigameWindow.xaml.cs:1268` (`FireVibration`, from `:1196/:1204`) | `TriggerSubliminalPatternAsync(tag)`, gated by `GazeVibrationMode` (None/OnCorrect/OnWrong, `:575-594`) |
| **AI `haptic` command** | `Services/Commands/HapticCommand.cs:24` | `ApplyVibrationModeAsync(..., Pulse)` clamped to `MaxAiHapticIntensity` |
| **Remote control** | `Services/RemoteControlService.cs:1117` (`TriggerAsync("remote_control", 0.7, 2000)`), stop `:800` | partner-app verb |
| **DTRH director** | `Services/Haptics/DtrhHapticDirector.cs:292/399` (via `DtrhHostService` taps, §6) | `ApplyVibrationModeAsync` / `StopAsync` |
| **Audio-sync** (web video) | `Services/AudioSyncService.cs:345` | `SetSyncIntensityAsync` (§5) |
| **Deeper enhancements** | `Services/Deeper/IActionDispatcher.cs:572` (stop `:527/:557`) | `SetSyncPatternAsync(samples, ms)` (§9c) |
| **Deeper editor preview** | `Views/Deeper/DeeperEditorWindow.xaml.cs:2646` | `SetSyncPatternAsync` |

### 9b. Mandatory-video coupling (the richest touchpoint)
On play, `VideoService` starts a constant background vibe at **10% of the Video slider** so target-hit
spikes feel impactful (`StartVideoBackgroundVibeAsync`, `HapticService.cs:764`); a slider of 0 means
"spikes only, no bed" (`:771`). Each attention-target hit fires `VideoTargetHitAsync` (`:823`) — a
100ms spike that then, after a 150ms felt-delay, resumes the background bed, but **only if it's the
newest hit** (a generation counter, `:846`) so a stale resume can't flatten a newer spike (#516).

### 9c. Deeper enhancement haptic tracks
`IActionDispatcher.DispatchHaptic` (`Services/Deeper/IActionDispatcher.cs:516`) resolves a
`HapticEvent`'s `custom_pattern` or `pattern_name` (via `StockHapticPatterns.TryGet`) into keyframes,
samples them (`StockHapticPatterns.Sample`), and sends `SetSyncPatternAsync`. On a `Restart` phase it
sends `StopAsync` first to clear Lovense's 1s same-level debounce (`:556`). This is the
`.ccpenh.json`-authored path — the `Models/Deeper/HapticTrack` schema (§4d) made runnable.

### 9d. The Haptics tab UI (`MainWindow/MainWindow.Haptics.cs`, 514 lines; `Views/Tabs/HapticsTabView.xaml`, 818 lines)
The tab view is a thin passthrough (`HapticsTabView.xaml.cs`, 101 lines — every handler forwards to
the `MainWindow` partial). Handlers: enable toggle (`ChkHapticsEnabled_Changed`, `:35`), provider
combo (`CmbHapticProvider_SelectionChanged`, `:155` — maps ComboBox tags to `HapticProviderType`,
Mock/Lovense/Buttplug), URL text (`:322`), auto-connect (`:334`), connect/disconnect (`:205`), test
(`:298`), the global intensity slider (`:273`, debounced 150ms live-preview), per-feature
enable/intensity/mode (`ChkHapticFeature_Changed` `:340`, `SliderHapticFeature_Changed` `:393`,
`CmbHapticMode_SelectionChanged` `:470`), the audio-sync toggle + latency/power sliders (`:56-140`),
DTRH density (`:385`), and the help/setup button (`:196`). Settings load-back is in
`MainWindow.Settings.cs` (e.g. `:260` global slider, `:319` video-mode combo).

### 9e. Premium gating (WHERE it's enforced)
Haptics is one of the five `HasPremiumAccess` gates (Remote Control, Bambi Takeover, Haptics,
Awareness, Lockdown — all share the single toggle). Enforcement:
- **Tab overlay:** `RefreshPremiumGate(HapticsTab.HapticsGate)` (`MainWindow.Patreon.cs:249`) shows the
  `HapticsGate` border (`HapticsTabView.xaml:799`) with an "Unlock with Patreon" CTA
  (`BtnGateUnlock_Click`).
- **Enable toggle:** `ChkHapticsEnabled_Changed` (`MainWindow.Haptics.cs:41`) blocks + shows
  `msg_haptic_feedback_patreon_only` if `HasPremiumAccess != true`.
- **Connect button:** `BtnHapticConnect_Click` (`MainWindow.Haptics.cs:208`) same check.
- **AI command:** gated by the AI stack's `HasAiAccess` (the `haptic` command flows through
  `AiCommandService`); `HapticCommand` itself adds the `MaxAiHapticIntensity` ceiling. There is **no
  separate premium check inside `HapticService`** — the service will vibrate for anyone who reaches it
  (§12.9). Gating is entirely at the UI/command layer.

### 9f. What it does NOT touch
No audio ducking, no XP award (haptics react to XP events, they don't grant them), no InteractionQueue
membership, no Discord presence, no achievements of its own. It is a pure output sink reacting to other
subsystems.

---

## 10. Where it lives — file map

All paths under `.../ConditioningControlPanel/`. All `file:line` verified 2026-07-23.

| File | Lines | Role |
|---|---|---|
| `Services/Haptics/IHapticProvider.cs` | 37 | Provider contract + `HapticProviderType` enum. |
| `Services/Haptics/HapticService.cs` | 954 | **The orchestrator** (`App.Haptics`): provider selection, pattern engine, all per-feature methods, ping watchdog, live-stop. |
| `Services/Haptics/LovenseProvider.cs` | 355 | Lovense HTTP provider (Lan/Local, 0-20 levels, rate-limit, pattern averaging). |
| `Services/Haptics/ButtplugProvider.cs` | 282 | Buttplug/Intiface WebSocket provider (multi-device, emulated durations). |
| `Services/Haptics/MockHapticProvider.cs` | 149 | Toast-only test provider. **Default.** |
| `Services/Haptics/DtrhHapticDirector.cs` | 403 | DTRH ambient+accent envelope (static). |
| `Services/Haptics/LockdownService.cs` | 226 | **NOT haptics** — strict-lock/panic lockdown mode, mis-filed (§7/§12.8). |
| `Services/AudioSyncService.cs` | 380 | Web-video audio → synced intensity stream. |
| `Models/HapticSettings.cs` | 347 | All haptic settings + `VibrationMode` enum. |
| `Models/AudioSyncSettings.cs` | 157 | Audio-sync tuning (snake_case JSON). |
| `Models/HapticTrack.cs` | 203 | Runtime audio-analysis intensity buffer (chunked). |
| `Models/Deeper/HapticTrack.cs` | 59 | Deeper JSON schema (`HapticEvent`s) — **different class, same name**. |
| `Models/Deeper/StockHapticPatterns.cs` | 103 | Six named keyframe curves + sampler. |
| `Services/Commands/HapticCommand.cs` | 34 | AI `haptic` command (clamped duration + `MaxAiHapticIntensity`). |
| `Models/CommandData/HapticCommandData.cs` | 7 | `record(Intensity, Duration)`. |
| `Views/Tabs/HapticsTabView.xaml` (+`.xaml.cs` 101) | 818 | The Haptics tab UI + gate overlay. |
| `Windows/HapticsSetupWindow.xaml` (+`.xaml.cs` 152) | 316 | 3-slide Lovense/Buttplug setup wizard. |
| `MainWindow/MainWindow.Haptics.cs` | 514 | All tab handlers (connect/test/provider/sliders/features). |

**C# wiring:** declared `App.xaml.cs:340`; constructed `:1499`; `AudioSync` `:1500`; auto-connect
`:1534-1536` (body `:2450`); disposed `:3291-3292`.

---

## 11. Where to change X

| Want to… | Edit |
|---|---|
| Add a new device provider | Implement `IHapticProvider`; add a `HapticProviderType` member (`IHapticProvider.cs:7`); construct it in `HapticService` ctor (`:56-58`) + `WireProviderEvents`; add it to the `ConnectAsync` switch (`:116`) and any `SetUrl` branch (`:131`); add a ComboBox item + `CmbHapticProvider_SelectionChanged` case (`MainWindow.Haptics.cs:162`). |
| Add a new built-in vibration mode | Add to the `VibrationMode` enum (`HapticSettings.cs:10`) + a `case` in `ApplyVibrationModeAsync` (`HapticService.cs:234`). |
| Add a new stock Deeper pattern | Add to `StockHapticPatterns.Names` + `_patterns` (`StockHapticPatterns.cs:13/18`). |
| Add a new trigger source | Call `App.Haptics?.TriggerAsync(name, intensity, ms)` (or a specific method); add a `IsFeatureEnabled` case (`HapticService.cs:317`) + settings if you want a per-event gate. |
| Change intensity→device level | Lovense: `VibrateAsync` mapping (`LovenseProvider.cs:200`) **and** `IntensityToLevel` (`:345`) — keep them consistent (§12.6). Global floor: `MinPerceptibleIntensity` (`HapticService.cs:214`). |
| Change audio-sync latency/mapping | Look-ahead calc `AudioSyncService.cs:214`; device-range map `SendHapticAsync:319`; weights/floors in `AudioSyncSettings.cs`. |
| Change DTRH event→pattern mapping | The `Map` table (`DtrhHapticDirector.cs:35`); cooldown/density in `GapMult`/`PlayAccent` (`:237/244`); ambient shape `AmbientTick` (`:347`). |
| Change the AI haptic ceiling | `CompanionPromptSettings.MaxAiHapticIntensity` + the Lab slider (`MainWindow.Patreon.cs:1622`). |
| Change premium gating | `RefreshPremiumGate` call (`MainWindow.Patreon.cs:249`) + the two `HasPremiumAccess` checks (`MainWindow.Haptics.cs:41/208`). |

---

## 12. Gotchas

1. **Mock is the default provider.** `HapticSettings.Provider` defaults to `Mock`
   (`HapticSettings.cs:25`) and `Enabled` defaults true. A fresh install "works" but only shows a pink
   toast — no real device buzzes until the user picks Lovense/Buttplug and connects. Auto-connect is
   **skipped for Mock** (`App.xaml.cs:1534`), so a Mock user never even shows as connected on launch.
2. **`IsConnected` can lie; always `PingAsync` before load-bearing ops.** Lovense's `IsConnected`
   stays true after a VPN/route change breaks localhost reachability. `TestAsync` and the 30s ping
   watchdog re-verify; a single blip is tolerated (3 consecutive failures / ~90s before drop, #302,
   `HapticService.cs:29-33`).
3. **`GlobalIntensity` is vestigial.** The "global intensity" slider writes
   `Settings.GlobalIntensity` (`MainWindow.Haptics.cs:279`) and loads it back (`MainWindow.Settings.cs:260`),
   and drives a live-preview buzz — but it is **never multiplied into any event trigger**. The design
   is "each per-event slider directly controls device power." Don't assume it scales anything.
4. **`VideoMode` is set but never read.** The UI persists `Settings.VideoMode`
   (`MainWindow.Haptics.cs:492`, loaded `MainWindow.Settings.cs:319`) but `HapticService`'s video
   paths (`StartVideoBackgroundVibeAsync`, `RampUpAsync`) call `VibrateAsync` directly / hard-code
   Constant — they never consult `VideoMode`. Changing it does nothing. (`BlinkMode`, by contrast, IS
   used — `HapticService.cs:754`.)
5. **Two different `HapticTrack` classes.** `Models/HapticTrack.cs` (audio-analysis buffer, used by
   AudioSync) vs `Models/Deeper/HapticTrack.cs` (JSON event schema, used by Deeper). Same name,
   different namespace, unrelated. Grep with the namespace or folder to disambiguate (§4d).
6. **Lovense has two disagreeing intensity→level mappers.** `VibrateAsync` maps 0.05..1.0 → levels
   3..20 (`LovenseProvider.cs:200`), while `IntensityToLevel` (used by the pattern/audio-sync path,
   `:345`) maps 0.02..1.0 → 0..20 with a different curve. The same requested intensity can produce
   different levels depending on which path fires. Intentional-ish (pattern path favors dynamics) but
   surprising.
7. **A "skip" in audio-sync must still complete the page handshake.** The WebView2 overlay waits for a
   ready signal before letting the video play; `OnVideoDetectedAsync` fires `ProcessingCompleted` even
   when it bails (disabled / not connected / bad URL) so the user isn't stuck on "Preparing Haptic
   Sync…" (`AudioSyncService.cs:99-101`).
8. **`LockdownService.cs` is mis-filed under `Services/Haptics/`.** It is lockdown-mode plumbing with
   no haptics coupling (§7). Ignore it when reasoning about the haptics feature; a `Services/Haptics/`
   grep will surface it as noise.
9. **The service enforces no premium gate.** `HasPremiumAccess` is checked only in the tab handlers
   (`MainWindow.Haptics.cs:41/208`) and the tab overlay. Any code path that reaches `App.Haptics.*`
   directly (a session, preset, remote command, or AI command with `HasAiAccess`) can vibrate a
   device regardless of premium state. Gate at the caller if that matters.
10. **Buttplug emulates durations with a fire-and-forget timer.** Unlike Lovense (`timeSec`), Buttplug
    holds until stopped, so `ButtplugProvider.VibrateAsync` schedules a `Task.Delay(durationMs)` →
    `Stop` guarded by a `CancellationTokenSource` (`ButtplugProvider.cs:222`). A new vibration cancels
    the prior stop. Plus the ~1.3s protocol latency (`SubliminalAnticipationMs`) that widens every
    duration/anticipation path.
11. **Mock's toast window is a singleton for a reason.** Per-call windows leaked HWNDs and crashed the
    render thread at audio-sync frame rate (`MockHapticProvider.cs:87-89`). Keep the reuse.

---

## 13. STATUS & BACKLOG — snapshot 2026-07-23 (VERIFY with git before acting)

- **State: mature and shipping.** No dedicated in-flight haptics branch; current HEAD `6571e5f4`
  ("docs(primers): …") on `fix/web-video-interruptions` (v6.5.0). The provider/orchestrator core is
  stable; recent-ish additions are the DTRH director (§6) and the Deeper stock-pattern path (§9c).
- **Known unused / vestigial** (documented so they aren't "fixed" blindly): `GlobalIntensity` isn't
  multiplied into triggers (§12.3); `VideoMode` is never read (§12.4); `BlinkEnabled` has no tab UI
  (§8); `MockHapticProvider.Error` is never raised. None are user-facing bugs.
- **Naming/filing hazards:** the two `HapticTrack` classes (§12.5) and the mis-filed
  `LockdownService` (§12.8). Both are traps for grep-driven edits.
- **Gating asymmetry:** premium enforcement is UI-only (§12.9) — worth remembering before wiring a new
  automated caller.
- **No dedicated xUnit coverage** was found for the haptics services in this pass; the pure-ish seams
  worth a test if regressions appear are `ApplyVibrationModeAsync`, the Lovense level mappers, and
  `HapticTrack.GetIntensityAt`. Verify current coverage with a quick grep before claiming any.
- This primer is **new** and not previously committed.

---

## 14. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```
Haptics is Patreon-gated: unlock premium, open the **Haptics** tab, pick a provider (Mock needs no
hardware and shows a corner toast), set the URL, **Connect**, then **Test**. For real devices, run the
Lovense app (Remote on phone / Connect on PC) or Intiface Central first, then match the URL. Watch
`logs/` for `Lovense …` / `ButtplugProvider …` / `SetSyncIntensity …` / `DtrhHaptics …` Serilog lines.
