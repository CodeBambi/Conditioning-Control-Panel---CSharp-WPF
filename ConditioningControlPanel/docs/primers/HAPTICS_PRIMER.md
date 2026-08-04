# Haptics — Feature Primer

> **Purpose.** One-load orientation for the device-vibration feature so you can maintain it WITHOUT
> re-reading the ~5,000 lines of `Services/Haptics/`. §0 is the one-paragraph model. §1 is the v2
> contract set. §2 the mixer (the heart). §3 the device manager + the three concurrent providers.
> §4 the `HapticService` facade. §5 patterns & temperament. §6 the Phase-F feature services
> (Toy Events input, FunScript, band-split, luminance). §7 audio-sync and the DTRH director.
> §8 settings + the v2 migration. **§9 is the load-bearing section — every way a vibration gets
> fired and every system it touches.** §10 the UI tiers, §11 file map, §12 where-to-change-X,
> **§13 gotchas**, §14 dated status, §15 build/run.
>
> **Verified against source 2026-08-03** on branch `feat/haptics-overhaul` (worktree
> `C:\Projects\ccp-wt-haptics`, off `f516cb25` = v6.6.3), i.e. **the v6.7 haptics overhaul**,
> Phases A–G. Every `file:line` below was read-verified when written and is git-verifiable, but
> line numbers drift — confirm with a quick read before quoting. §1–§13 track the code and rarely
> rot; **§14 is a dated snapshot — verify with `git log` before acting.**
>
> The blow-by-blow record of WHY each decision was made is
> `docs/HAPTICS_OVERHAUL_PLAN.md` (historical; not maintained). This primer describes the code
> as it now stands.

---

## 0. What Haptics is, in one paragraph

A **premium** subsystem that drives the user's vibrating toys from in-app events. Consumers no
longer talk to devices: they post **semantic events** (`PostEvent(HapticEventKind)`) or set
**continuous layers** (`SetLayer(HapticLayer, 0..1)`) on a single **`HapticMixer`**, which combines
layers by MAX, sums transient envelopes within a priority group, applies the **temperament** dial,
then `min(raw × GlobalIntensity, MasterCap) × per-device trim`, and pushes the result through one
**10 Hz per-device output loop**. Below it, **`HapticDeviceManager`** keeps **three providers
connected concurrently** — `LovenseProviderV2` (LAN Game Mode JSON + a Toy Events WebSocket),
`ButtplugProviderV2` (Buttplug 5.0.1 / Intiface), `MockProviderV2` (three virtual toys with an
on-screen toast) — merges their device lists, de-dupes the same physical toy seen twice, and
remembers each toy's **Role / trim / enabled / nickname** by a stable `"{provider}:{id}"` key.
Routing is **by role**, not by toy: every `HapticEventKind` and every `HapticLayer` has a settings
row saying *enabled / intensity / pattern / target role*. `HapticService` (`App.Haptics`) survives
as a **facade** — every pre-v6.7 public method keeps its exact signature, so all ~25 consumer call
sites are untouched. On top sit `AudioSyncService`, `DtrhHapticDirector`, `FunScriptService` and
`ToyInputService` (two-way: toy buttons come back IN). Premium is enforced **once**, in the mixer's
gate, not sprinkled through the UI.

---

## 1. The v2 contracts (`Services/Haptics/Core/HapticContracts.cs`, 198 lines)

Written first, everything else implemented against them. **Do not redesign; extend by adding
members only.**

| Type | What it is |
|---|---|
| `ActuatorType` (`:16`) | `Vibrate, Rotate, Thrust, Finger, Suction, Oscillate, Pump, Depth, Position, Stroke, Constrict`. The addressing unit the old `IHapticProvider` never had. |
| `HapticActuator` (`:32`) | One channel: `Type` + `Index` (disambiguates Edge's 2 vibes / Lapis's 3) + `Steps` (native resolution: 20 vibe/rotate/thrust, 3 pump/depth, 100 position). |
| `ToyRole` (`:42`) | `All / Reward / Punish / Ambient`. **The routing matrix targets roles.** A toy set to `All` hears everything. |
| `HapticDevice` (`:52`) | Id, `ProviderKey`, name, nickname, actuator list, battery, plus the persisted `Role`/`IntensityTrim`/`Enabled`. `DeviceKey => ProviderKey + ":" + Id`. |
| `ActuatorOutput` (`:74`) | `(Type, Index, Intensity 0..1)` — the mixer's per-tick target, already trimmed and capped. **Level-set semantics:** providers hold the level until the next call. |
| `ToyEventKind` / `HapticToyEvent` (`:85`/`:97`) | Two-way input: `ButtonDown/Up/Pressed`, `StrengthChanged`, `BatteryChanged`, `Shake`, `MotionChanged`. |
| `IHapticProviderV2` (`:108`) | `Key`/`DisplayName`/`IsConnected`/`Devices`, events `DevicesChanged`/`ToyEvent`/`Error`, and `ConnectAsync`/`DisconnectAsync`/`SetOutputsAsync(deviceId, outputs, ct)`/`StopAllAsync`/`PingAsync`. Implementations must be thread-safe. |
| `HapticLayer` (`:144`) | Continuous sources: `Video, AudioSync, Luminance, Dtrh, Session, Manual, Pattern`. |
| `HapticEventKind` (`:161`) | 16 transient events. **The names ARE settings keys — never rename one.** |
| `HapticPulse` (`:183`) | `{Intensity, AttackMs, HoldMs, DecayMs, Priority, Target}` — one transient envelope. |

The old `IHapticProvider` (`IHapticProvider.cs`, 37 lines) and its three implementations still
compile but are **orphaned** — nothing constructs them (§13.11).

---

## 2. `HapticMixer` — the heart (`Core/HapticMixer.cs`, 933 lines)

One instance, owned by `HapticService`. It is the ONLY thing that talks to devices.

**Tunables** (`:70-92`): `DefaultTickMs = 100` (the 10 Hz loop), `IdleTickMs = 250`,
`DefaultSoftRampMs = 800`, **`DefaultMasterCap = 0.70`**, `DefaultMaxConcurrentPulses = 4`,
`MinPerceptibleIntensity = 0.06`, `ShutdownFlushCap = 2s`.

**The gate** (`IsGateOpen`, `:151`). Premium + master toggle, evaluated **once, here** — not in
every consumer as before. `AllowTestWindow(ms)` (`:164`) waives only `Settings.Enabled`, for a few
seconds, so the Test button can prove hardware works with the master toggle off. **Premium is still
required.**

**Per tick** (the pipeline, `BuildOutputs` `:~300-420`):
1. **Per-motor split** (Phase F band-split) if a layer carries a `double[]` breakdown.
2. **Continuous floor** — layers combined by MAX, filtered by the target role, each scaled by its
   layer rule's intensity × the temperament's `ContinuousScale`.
3. **Soft ramp** — the floor's RISE is slew-limited over `SoftRampMs`; falls are instant.
4. **Transients** — active pulses SUM within a priority group, MAX across groups, then ride over
   the floor by MAX. Transients are NOT slew-limited, so short accents stay sharp.
5. **`Finish()`** — `min(raw × GlobalIntensity, MasterCap) × deviceTrim`.
6. **Quantize** to the actuator's native `Steps` and **suppress unchanged sends**.

**Public surface**: `SetLayer(layer, value, autoZeroMs=0)` (`:476`),
`SetLayerPerActuator(layer, double[]?)` (`:502`), `SuppressLayersUntil(utc)` / `AreLayersSuppressed`
(`:546`/`:556`), `GetLayer` (`:561`), `PlayLayerEnvelope(layer, values, totalMs)` (`:569`),
`Post(pulse)` (`:623`), `Play(steps) → HapticSequence` (`:628`, awaitable via `.Completion`,
cancellable via `.Cancel()`), `CancelSequence` (`:656`), **`PanicStop()`** (`:749`), `ClearAll()`
(`:762`), `FlushStopAsync(cap)` (`:806`), `SetPositionAsync(deviceKey, 0..1)` (`:830`),
`Activity` event (`:138`, fires **on the loop thread** — subscribers must marshal themselves).

**Safety model.** Master cap (default 0.70, so max output is 0.70 not 1.0), soft ramp on start and
resume, explicit zeroing on disconnect/close, a `ProcessExit` hook (`:144`), and `PanicStop()` which
bypasses everything.

`SetPositionAsync` is deliberately **outside** the generic mix: Position is placement, not
intensity. FunScript owns it.

---

## 3. `HapticDeviceManager` + the three providers

### 3a. The manager (`Core/HapticDeviceManager.cs`, 302 lines)
Constructs and registers all three providers up front (`:36-38`) and calls
`settings.EnsureV2Migrated()` in its ctor (`:34`). `EnabledProviders()` (`:91`) reads
`V2.Provider(key).Enabled` — **per-provider checkboxes, several at once**. `ConnectAsync` connects
every enabled provider in parallel; `Rebuild()` (`:183`) re-merges all device lists, applies the
persisted `HapticDeviceConfig`, and **de-dupes the same physical toy seen through two providers by
name**, preferring `lovense > buttplug > mock` (`ProviderPreference`, `:21`). `SetRole` / `SetTrim`
/ `SetEnabled` / `SetNickname` (`:270-273`) write straight into settings.

### 3b. `LovenseProviderV2` (`LovenseProviderV2.cs`, 1235 lines) + `LovensePatterns.cs` (350) + `LovenseToyEventsClient.cs` (525)
LAN **Game Mode** only — the app never routes users through the Lovense cloud.
- **Base URL**: tries HTTPS `https://{dashed-ip}.lovense.club:30010` then falls back to plain
  `http://{ip}:20010`; the winner is session-only (`ActiveBaseUrl`, `:76`) and **never written to
  settings**. The configured address comes from `ConfiguredUrlOverride` (`:85`), which integration
  sets explicitly.
- **`X-platform: Conditioning Control Panel`** on every request (`LovensePatterns.cs:25`) — it is
  displayed inside Lovense Remote, so it is a branding surface.
- **Per-toy registry** from `GetToys`, parsed as BOTH an escaped-JSON string and an object
  (firmware varies). Capabilities come from `shortFunctionNames` (`v1`,`v2` → Edge's two motors);
  firmware that omits both name lists falls back to a small model table, else one Vibrate.
- **Keep-alive = `timeSec:0` + a 25 s refresh** (`KeepAliveInterval`, `:54`; `RunKeepAliveAsync`,
  `:594`). Every Function command is indefinite, so **we own the stop**: zeros are always
  transmitted explicitly (as `Vibrate:0`, never `Stop` spam) and `StopAllAsync` clears the
  suppression cache before firing per-toy `Stop`s.
- **One request per device, comma-combined** (`Vibrate:5,Rotate:10`). Parallelism is across
  DEVICES, not verbs. `Stroke` is a RANGE not a level (`Stroke:0-{20..100}`, span ≥ 20); a zero
  request omits the fragment because `Thrusting:0` is what actually stops it.
- **Patterns/presets**: `SendPresetAsync` (`:443`, pulse/wave/fireworks/earthquake),
  `SendPatternV1Async` (`:459`), and the PatternV2 keyframe set `Setup/Play/InitPlay/Stop/SyncTime`
  (`:488-510`). Sending any of these **clears the unchanged-send cache**.
- **Toy Events** (`LovenseToyEventsClient.cs`): `ws://{ip}:20010/v1` (or
  `wss://{dashed}.lovense.club:30010/v1`), access-request handshake, 5 s ping, reconnect with
  backoff, `IsSupported` feature-detect for older Remote versions.
- `ConnectAsync` returns **true when Remote answers with zero toys** (raising an informational
  `Error`); a 20 s poll picks toys up as they pair. Do not treat that as failure.

### 3c. `ButtplugProviderV2` (`ButtplugProviderV2.cs`, 707 lines)
Package **`Buttplug` 5.0.1**, which speaks **message spec v4, NOT v3** (verified by reflecting over
the shipped DLL — there is no `ScalarCmd`, no `VibrateCmd`, no `device.VibrateAsync()`). The v4
surface: `OutputCmd`/`InputCmd`, `device.Features` (per-feature capabilities),
`feature.TryGetOutputRange(type, out min, out max)`, `device.RunOutputAsync(featureIndex, cmd, ct)`,
`client.InputReadingReceived`. The connector ships **inside** the same package
(`Buttplug.Client.ButtplugWebsocketConnector`). Requires Intiface Central.

Mapping: `Vibrate→Vibrate`, `Rotate→Rotate`, `Oscillate→Oscillate`,
`Position`+`HwPositionWithDuration`→`Position`, `Constrict→Constrict`; `Led`/`Temperature`/`Spray`
ignored. There is **no** Buttplug output type for Thrust/Finger/Suction/Pump/Depth/Stroke — those
are Lovense-only, which is why the dual path exists. Buttplug outputs **latch**, so
`SetOutputsAsync` sends only on a quantized-step change and there is **no keep-alive**. `PingAsync`
is a real wire round-trip (`RequestDeviceList` through the retained connector). Device id =
Intiface display name (`:` stripped, `#n` for duplicates) — the numeric Buttplug index is
session-scoped and unusable as a persisted key.

### 3d. `MockProviderV2` (`Core/MockProviderV2.cs`, 191 lines) + `MockToast.cs` (101)
Three virtual toys with real capability shapes: **Mock Lush** (1 vibe), **Mock Edge** (2 vibes),
**Mock Solace** (Thrust 20 steps + Depth 3 steps, **no vibrate**). Feedback goes to the hoisted
singleton toast in `Core/MockToast.cs` — **one window, shared with the legacy mock** (§13.10).

---

## 4. `HapticService` — the facade (`Services/Haptics/HapticService.cs`, 872 lines)

`App.Haptics` (declared `App.xaml.cs:354`, constructed `:1563`, disposed `:3483`). It owns the
device manager, the mixer, `ToyInput` and `FunScript`, and re-raises their events.

**Every pre-v6.7 public method keeps its exact signature** and now posts into the mixer:
`TriggerAsync`, `ApplyVibrationModeAsync`, `LevelUpPatternAsync`, `AchievementPatternAsync`,
`FlashDecayVibeAsync`, `FlashClickVibeAsync`, `BubblePopAsync`, `BouncingTextBounceAsync`,
`BlinkPulseAsync`, `StartVideoBackgroundVibeAsync`/`StopVideoBackgroundVibeAsync`,
`VideoTargetHitAsync`, `TriggerSubliminalPatternAsync`, `TriggerKeywordPatternAsync`,
`AvatarEasterEggPatternAsync`, `SetSyncIntensityAsync`, `SetSyncPatternAsync`,
`LiveIntensityUpdateAsync`, `RampUpAsync` (a thin shim; it had no callers), `TestAsync`, `StopAsync`.

**New first-class API**: `PostEvent(kind, intensityOverride?)` (`:232`), `SetLayer` (`:237`),
`GetLayer` (`:240`), `SetLayerPerActuator` (`:249`), `SuppressContinuousLayers(seconds)` (`:254`),
`MaxVibrateMotors` (`:259`), `HasPositionActuator` (`:281`), `SetPositionAsync(0..1)` (`:303`),
`PanicStop()` (`:331`), `PlayPatternAsync(...)` (`:334`),
**`TestDeviceAsync(deviceKey, mode, intensity, ms)`** (`:485` — drives ONE device directly, because
the mixer mixes by role and a per-toy test is not a role; master multiplier, cap and trim still
apply and the device is always explicitly zeroed on the way out), plus the band-split overload
`SetSyncIntensityAsync(lowBand, highBand)` (`:595`).

**Live-stop** (`OnSettingsChanged`, `:~140`): master `Enabled` off ⇒ `PanicStop()`. A single feature
toggled off mid-pattern cancels **only that** kind's live `HapticSequence` (`_liveByKind`), where
the old code stopped the device outright and killed unrelated features.

**Connection.** `ConnectAsync` (`:155`) **deliberately no longer sets `Settings.Enabled = true`** —
connecting a toy is not consent to buzz. A 30 s ping timer needs **3 consecutive** failures (~90 s)
before dropping (#302). Auto-connect is `App.xaml.cs:1602` and **no longer skips Mock**.

Bugs this rewrite closed (from the class doc, `:20-39`): the force-enable on connect, two CTS
dispose races that threw `ObjectDisposedException` into `UnobservedTaskException`, the single
unsynchronized `_currentEventType`, and `Dispose` doing `.Wait(1000)` on the UI thread.

---

## 5. Patterns and temperament

### 5a. `HapticPatterns` (`Core/HapticPatterns.cs`, 137 lines)
The six `VibrationMode`s (`Constant, Pulse, Wave, Heartbeat, Escalate, Earthquake`) rendered as
`HapticPulseStep` sequences: `Render(...)` (`:18`), `TotalMs` (`:93`), `Append` (`:106`), and
`SampleAt(steps, timeMs)` (`:117`) — which is what the tab's envelope preview and the per-toy test
draw, so the picture is the ENGINE's own curve rather than a look-alike. **This is the single
plug-in point** for any future keyframe designer.

### 5b. `HapticTemperament` (`Core/HapticTemperament.cs`, 132 lines)
Five presets keyed by a stable lowercase string in `V2.Temperament` (default `"balanced"`;
unrecognised values fall back to Balanced).

| Preset | Continuous | Transient | Attack | Decay | Pulse-priority bias |
|---|---:|---:|---:|---:|---:|
| Gentle | 0.70 | 0.75 | 1.40 | 1.30 | −1 |
| **Balanced** | 1.00 | 1.00 | 1.00 | 1.00 | 0 |
| Tease | 0.85 | 0.90 | 1.60 | 1.80 | 0 |
| Intense | 1.15 | 1.20 | 0.80 | 0.90 | +1 |
| Cruel | 1.25 | 1.40 | 0.45 | 0.70 | +2 |

Where each column lands: **Continuous** multiplies the layer rule's intensity in `BuildOutputs`
(so the band-split path inherits it for free — same `layerScale`); **Transient** multiplies each
pulse sample as priority groups are summed, before the group clamp to 1.0; **Attack/Decay** scale
the envelope segments in `PromotePending` (HOLD is untouched, so a pattern keeps its rhythm and
only changes its EDGE); **Pulse-priority bias** is added to `MaxConcurrentPulses` (clamped 1–12) —
priority is only consulted when that window is full and the weakest pulse must be evicted, so
biasing the window IS what "priority bias" means here. All of it lands **before** `Finish()`, so
the cap always wins and a >1.0 scale can never exceed the user's ceiling.

### 5c. The other two "pattern" worlds (unchanged, don't conflate)
- **AI `haptic` command** — `Services/Commands/HapticCommand.cs`, duration clamped to 10 s and
  intensity clamped to `CompanionPrompt.MaxAiHapticIntensity` (default 0.6).
- **Deeper stock patterns** — `Models/Deeper/StockHapticPatterns.cs`, six named keyframe curves
  sampled into a `float[]` and sent via `SetSyncPatternAsync`, which now lands on
  **`HapticLayer.Pattern`** so it cannot stomp AudioSync or Manual.

---

## 6. Phase-F feature services

### 6a. Toy input (`ToyInputService.cs`, 227 lines) — two-way
Subscribes to the device manager's `ToyEvent`. `ButtonPressed` is debounced at
`DebounceMs = 350` (`:30`) because Lovense sends button-down **and** button-pressed for one squeeze;
raised on the UI dispatcher. `WaitForButtonAsync(timeoutMs, ct)` (`:185`) is the awaitable form.
`StrengthChanged` ⇒ `mixer.SuppressLayersUntil(now + UserOverrideCooldownSec)` and the
`UserOverrode` event — **continuous layers mute, transients deliberately still fire** (an
achievement buzz is an event, not the app taking the dial back). `IsAvailable` (`:55`) is true only
when a connected device belongs to a provider that can raise input.
**One consumer is wired:** video attention checks (`AttentionCheckToyButton`, default **off**) — a
press ADDS to the mouse click, never replaces it, and success posts `ToyButtonReward`.

### 6b. FunScript (`FunScript.cs` 188 + `FunScriptService.cs` 300)
`FunScript` is a pure parser/sampler (unit-tested): `TryParse`, `PositionAt`, `SpeedAt`,
`IntensityAt`, `SpeedToIntensity` with `MinSpeedUnitsPerSec = 10` / `MaxSpeedUnitsPerSec = 500`.
The service auto-loads `<video>.funscript` then `<video-dir>\funscripts\<name>.funscript`
(`CandidatePaths`, `:75`) — **discovery is beside-the-video by design; there is no folder picker.**
Position actuators get a **300 ms lead** (`PositionLeadMs`); everything else gets the
speed→intensity envelope on `HapticLayer.Pattern`. Sync rides the existing
`PrimaryPlaybackTimeMsChanged` event, extrapolated with the wall clock at 20 Hz
(`RenderIntervalMs = 50`); no report for `StaleMs = 900` ⇒ paused/stopped ⇒ the layer zeroes. A
seek is just a report that disagrees with the extrapolation, so it self-corrects. Default **on**,
zero-config.

### 6c. Audio band-split
`AudioSyncSettings.BandSplit` (`band_split`, default **off**) → `AudioAnalyzer.Analyze` emits low
and high bands **from the same FFT pass** → `ChunkManager.LowBandTrack/HighBandTrack` →
`HapticService.SetSyncIntensityAsync(low, high)` → `HapticMixer.SetLayerPerActuator`. It only
engages when a connected toy has **≥2 Vibrate actuators**; one-motor toys are byte-identical to
before (the scalar layer value is kept at `max(perMotor)`). The split only sets the RATIO between
motors — soft-ramp, master multiplier, cap and trim still apply once each.

### 6d. Flash-luminance sync
`HapticSettings.LuminanceSyncEnabled` (default **off**) + `LuminanceSyncIntensity` (0.5).
`FlashService.ApplyLuminanceSync` samples the **already-decoded** frozen `BitmapSource` down to 8×8
(WIC, no re-decode, cached per file) into `HapticLayer.Luminance` with `autoZeroMs` = the flash's
own lifetime, so there is no hide hook to get wrong. **SubliminalService has no image path**
(text-only + whisper audio), so nothing is wired there.

---

## 7. Audio-sync and the DTRH director

**`Services/AudioSyncService.cs`** — unchanged in shape: analyzes a **web** video's audio into a
runtime `HapticTrack` (chunked `float[]` buffer), reports playhead at frame rate, computes a
look-ahead of `currentTime + 300 + SubliminalAnticipationMs + ManualLatencyOffsetMs`, and calls
`SetSyncIntensityAsync`. It now lands on `HapticLayer.AudioSync` instead of driving the device.
The JS is injected only when `AudioSync.Enabled && App.Haptics.IsConnected`
(`MainWindow.Browser.cs`), re-armed on a late connect. This is the WebView2 `<video>` path, distinct
from mandatory LibVLC video.

**`DtrhHapticDirector.cs`** (423 lines) — re-based onto the mixer with its tuning values unchanged.
The slow "depth gauge" floor is now `HapticLayer.Dtrh` (was long 30 s Constant commands); the tiered
accents are priority-tagged pulses posted as `HapticEventKind.DtrhAccent`. The curated verb table
(`Map`), the 3 tiers, the shared cooldown, the 700 ms tier-1 coalescer and `DtrhDensity`
(0 Sparse / 1 Balanced / 2 Rich) are all as before. This director was the only well-designed piece
of the old stack and is the shape the mixer was modelled on.

---

## 8. Settings and the v2 migration (`Models/HapticSettings.cs`, 821 lines)

`App.Settings.Current.Haptics`. The file has **three tiers**, and which one a value lives in matters:

1. **Legacy flat properties, no `[JsonProperty]`** (PascalCase JSON). `Enabled`, `Provider`,
   `AutoConnect`, `GlobalIntensity`, ten `{Feature}Enabled/Intensity/Mode` triples, `LovenseUrl`,
   `ButtplugUrl`, the four `Dtrh*`, and `AudioSync`. **Renaming any of these silently resets that
   user's setting, so none of them ever move.** `VideoMode` is `[Obsolete]` (dead; kept so old
   files round-trip).
2. **Phase-F additions, explicit snake_case `[JsonProperty]`** (`:367-439`): `toy_input_enabled`,
   `attention_check_toy_button`, `user_override_cooldown_sec` (30), `funscript_enabled` (true),
   `funscript_to_vibe` (true), `luminance_sync_enabled` (false), `luminance_sync_intensity` (0.5).
3. **`V2` — `HapticSettingsV2`** (`:714`), key `"v2"`, every member explicitly named:
   `schema_version`, `master_cap` (0.70), `soft_ramp_ms`, `output_hz` (10),
   `max_concurrent_pulses`, `temperament`, and four dictionaries — `providers`, `devices`,
   `events` (key = `HapticEventKind` member name), `layers` (key = `HapticLayer` member name).
   `EnsureRows()` creates every row up front so the mixer only ever READS existing rows and never
   races a dictionary mutation.

**`GlobalIntensity` is no longer vestigial.** It is the live master multiplier applied by the mixer
before the cap. Combined with `MasterCap = 0.70`, **max output is 0.70, not 1.0** — patch notes must
say so.

### 8a. `EnsureV2Migrated()` (`:479`) — the one-shot migration
Idempotent, cheap, called from the `HapticService`, `HapticMixer` and `HapticDeviceManager`
constructors (never from `SettingsService`, so the loader stays untouched).

- **Schema 0 → 1**: `SeedRoutingFromLegacy` maps each legacy triple onto its routing row, including
  the rows that historically rode another feature's settings — `QuestComplete` and
  `AvatarEasterEgg` inherit **Achievement**, `GazeReward` inherits **Subliminal**, and
  `KeywordTrigger` is seeded **always-enabled** with Subliminal's intensity/mode because it never
  had its own toggle. Layers: `Video` ← `VideoEnabled`, `AudioSync` ← `AudioSync.Enabled`,
  `Dtrh` ← `DtrhEnabled`, all at scale 1.0. `SeedProvidersFromLegacy` carries the single old
  provider choice forward and copies both URLs. **A stored `GlobalIntensity <= 0.05` is rescued to
  0.7** — that slider did nothing before, so a parked 0 was never a real preference.
- **Schema 1 → 2**: enables the `Luminance` LAYER row. Schema 1 seeded it disabled ("off until the
  feature exists"); without this pass, turning `LuminanceSyncEnabled` on would feel like nothing
  because the layer rule would silently veto it. The FEATURE toggle stays the real gate.

### 8b. `SyncLegacyToRouting` (`:581`)
`HapticSettings.OnPropertyChanged` mirrors legacy property writes into the matching v2 rows. It
exists because old call sites (and any settings-file edit) still write the flat properties. The
Phase-E UI writes rows directly, so this is a compatibility bridge, not the main path.

---

## 9. HOW IT'S INVOKED & HOW IT INTERACTS WITH THE REST OF THE APP

Read this section first. There is still no single command sink — features call `App.Haptics?.*`
directly, always null-safe, and each call early-returns behind the mixer's gate.

### 9a. The trigger map

| Caller | Method | Lands on |
|---|---|---|
| **Bubble-pop minigame** (`Services/BubbleService.cs`) | `BubblePopAsync()` | `HapticEventKind.BubblePop` |
| **Flash images** (display) (`Services/Flash/FlashService.cs`) | `FlashDecayVibeAsync()` | `FlashDecay` |
| **Flash images** (click) | `FlashClickVibeAsync()` | `FlashClick` |
| **Flash images** (brightness) | `ApplyLuminanceSync` | layer `Luminance` |
| **Mandatory video** (bed) (`Services/Video/VideoService.cs`) | `StartVideoBackgroundVibeAsync()` | layer `Video` |
| **Mandatory video** (target hit) | `VideoTargetHitAsync()` | `VideoTargetHit` |
| **Mandatory video** (toy-button alternative) | `ToyInput.WaitForButtonAsync` in `SpawnTarget` | `ToyButtonReward` |
| **Mandatory video** (script) | `FunScriptService.OnVideoStarted/Stopped` | layer `Pattern` + Position |
| **Subliminals** (`Services/Subliminal/SubliminalService.cs`) | `TriggerSubliminalPatternAsync(text)` | `SubliminalTrigger` |
| **Bouncing text** (`Services/Subliminal/BouncingTextService.cs`) | `BouncingTextBounceAsync()` | `BouncingTextBounce` |
| **Blink trainer** (Lab) | `BlinkPulseAsync()` | `BlinkPulse` |
| **Level-up** (`Services/Progression/ProgressionService.cs`) | `LevelUpPatternAsync()` | `LevelUp` |
| **Achievement** (`AchievementService.cs`) | `AchievementPatternAsync()` | `Achievement` |
| **Quest complete** (`QuestService.cs`) | `AchievementPatternAsync()` | `QuestComplete` |
| **Avatar 20-click egg** | `AvatarEasterEggPatternAsync()` | `AvatarEasterEgg` |
| **Keyword triggers** (`KeywordTriggerService.cs`) | `TriggerKeywordPatternAsync(word, intensity)` | `KeywordTrigger` |
| **Gaze minigame** (Lab) | `TriggerSubliminalPatternAsync(tag)` | `GazeReward` |
| **AI `haptic` command** | `ApplyVibrationModeAsync(..., Pulse)` | `AiCommand` |
| **Remote control** (`RemoteControlService.cs`) | `TriggerAsync("remote_control", …)` | facade shim |
| **DTRH director** | ambient + tiered accents | layer `Dtrh` + `DtrhAccent` |
| **Audio-sync** (web video) | `SetSyncIntensityAsync(…)` | layer `AudioSync` |
| **Deeper enhancements / editor preview** | `SetSyncPatternAsync(samples, ms)` | layer `Pattern` |

The old double-fire in `AchievementService` and `QuestService` (each called the pattern twice) was
removed in Phase D.

### 9b. Video coupling (still the richest touchpoint)
`StartVideoBackgroundVibeAsync` reads the **legacy** `VideoIntensity` level and sets
`HapticLayer.Video`; the layer rule only gates and scales it. Target hits are pulses that ride
over the floor by MAX, so a stale resume can no longer flatten a newer spike — the mixer's
generation problem simply doesn't exist any more.

### 9c. Premium gating
Enforced **once**, in `HapticMixer.IsGateOpen` (`:151`) — `App.Patreon?.HasPremiumAccess` plus the
master toggle. The tab still shows its Patreon overlay, but the service-level hole is closed: an
automated caller (session, preset, remote command) can no longer vibrate a non-premium device.

### 9d. What it does NOT touch
No audio ducking, no XP award, no InteractionQueue membership, no Discord presence, no achievements
of its own. It is a pure output sink — plus, since Phase F, one narrow INPUT source (toy buttons)
that feeds video attention checks.

---

## 10. The UI, in three tiers (`Views/Tabs/HapticsTabView.xaml`, 1322 lines)

The owner rejected the first (flat) redesign — *"so many sliders in plain sight… a recipe for choice
fatigue"* — so the tab is deliberately tiered. Nothing was removed; what changed is **what is
visible when**. At rest, on a fresh maximised 1080p open, there is **one** slider and about ten
reachable controls.

- **Tier 1 — first paint.** Header, a shrunk "what is this?" card, the connection strip
  (provider chips, device count, Connect/Disconnect, **PANIC STOP**), a **How it feels** card
  holding the single `Intensity` slider (`GlobalIntensity`) plus the five temperament chips, and
  the toy cards (name/nickname, battery, capability chips, Role picker, trim slider, Test). Then
  two collapsed doors. **Max power is NOT here** — it is a safety net, not a volume knob.
- **Tier 2 — `Customize`** (`HapticCustomizeSection`). The routing matrix, grouped
  Core/Rewards/Media/Games. A row at rest is `[toggle] icon Name … "50% · Pulse · All" ›`; the
  summary strip is a `ToggleButton` that reveals that row's strength slider, pattern combo and
  target combo inline. `HapticRowExpansionScope` keeps **one row open at a time** across every
  group. Below the list, the **Extras** strip: FunScript enable, flash-brightness enable,
  toy-button input, band split, and the indented "squeezing passes attention checks".
- **Tier 3 — `Advanced`** (`HapticAdvancedSection`). Safety ceiling (Max power + warning),
  toy-input back-off slider, FunScript "convert to vibration", flash-brightness strength, the DtRH
  ambient/density pair, the Video-Haptic-Sync card (delay/power + the 6-knob DSP drawer), and the
  Pattern lab.

**It is data-driven.** `Views/Controls/HapticUiModels.cs` (514 lines) holds `HapticRoutingRowVm`,
`HapticRoutingGroupVm`, `HapticToyCardVm`, `HapticProviderChipVm`, `HapticRowExpansionScope` and two
converters. Each VM writes straight through to the settings object the engine reads and calls
`App.Settings.Save()` (itself 500 ms-debounced, so slider drags coalesce into one write). **Adding a
routing row is one line in `MainWindow.InitializeHapticsTab()`** (`MainWindow.Haptics.cs:58`), not a
XAML copy-paste.

**Which property a row writes is per-row and is NOT guessable** (`HapticRowLegacyBinding`,
`HapticUiModels.cs:56`). Event rows → `V2.Rule(kind)`. The **Video** row writes the LEGACY
`VideoIntensity`/`VideoEnabled` because `StartVideoBackgroundVibeAsync` reads the legacy level. The
**AudioSync** row writes BOTH `AudioSync.Enabled` and the layer rule, because the service early-outs
on the former.

**Setup wizard v2** (`Windows/HapticsSetupWindow.xaml(.cs)`, 248+249 lines) is no longer
informational: three pages (`Provider → Guide → Connect`) that write the v2 provider flags and the
Lovense address, run the real Connect with progress, list the devices found, give actionable failure
hints and offer a Test buzz. Mock/demo is a first-class path.

---

## 11. Where it lives — file map

All paths under `.../ConditioningControlPanel/`. Line counts verified 2026-08-03.

| File | Lines | Role |
|---|---|---|
| `Services/Haptics/Core/HapticContracts.cs` | 198 | **The v2 contract set** (§1). Extend, never redesign. |
| `Services/Haptics/Core/HapticMixer.cs` | 933 | **The heart**: layers, pulses, temperament, safety, the 10 Hz loop. |
| `Services/Haptics/Core/HapticDeviceManager.cs` | 302 | N concurrent providers, device registry, per-toy config, dedupe. |
| `Services/Haptics/Core/HapticPatterns.cs` | 137 | The six `VibrationMode`s as envelopes + `SampleAt`. |
| `Services/Haptics/Core/HapticTemperament.cs` | 132 | Five presets and their multiplier sets. |
| `Services/Haptics/Core/MockProviderV2.cs` | 191 | Three virtual toys with real capability shapes. |
| `Services/Haptics/Core/MockToast.cs` | 101 | The **single** shared toast window (HWND-leak history). |
| `Services/Haptics/HapticService.cs` | 872 | `App.Haptics` — the facade; legacy signatures preserved. |
| `Services/Haptics/LovenseProviderV2.cs` | 1235 | LAN Game Mode: per-toy registry, keep-alive, patterns, presets. |
| `Services/Haptics/LovensePatterns.cs` | 350 | Verb/step tables, `X-platform` header, quantizer, action fragments. |
| `Services/Haptics/LovenseToyEventsClient.cs` | 525 | Toy Events WebSocket (`/v1`): buttons, strength, battery, shake. |
| `Services/Haptics/ButtplugProviderV2.cs` | 707 | Buttplug 5.0.1 (**spec v4**) over Intiface. |
| `Services/Haptics/ToyInputService.cs` | 227 | Debounced button input + user-override back-off. |
| `Services/Haptics/FunScript.cs` | 188 | Pure parser/sampler (unit-tested). |
| `Services/Haptics/FunScriptService.cs` | 300 | Discovery beside the video, 20 Hz sync, position lead. |
| `Services/Haptics/DtrhHapticDirector.cs` | 423 | DtRH ambient layer + tiered accent pulses. |
| `Services/AudioSyncService.cs` | 409 | Web-video audio → `HapticLayer.AudioSync` (+ the band split). |
| `Models/HapticSettings.cs` | 821 | Legacy props + Phase-F props + `HapticSettingsV2` + the migration. |
| `Models/AudioSyncSettings.cs` | 171 | Audio-sync tuning, all snake_case (incl. `band_split`). |
| `Views/Tabs/HapticsTabView.xaml` | 1322 | The three-tier tab. |
| `Views/Controls/HapticUiModels.cs` | 514 | Row/toy/provider VMs, expansion scope, converters. |
| `MainWindow/MainWindow.Haptics.cs` | 1062 | Tab handlers + the small `Load*ToUi` helpers. |
| `Windows/HapticsSetupWindow.xaml(.cs)` | 248+249 | Setup wizard v2 (writes settings, really connects). |
| `Services/Haptics/IHapticProvider.cs` + `LovenseProvider.cs` + `ButtplugProvider.cs` + `MockHapticProvider.cs` | 37+355+361+68 | **DEAD** v1 path — compiles, nothing constructs it (§13.11). |
| `Services/Haptics/LockdownService.cs` | 226 | **NOT haptics** — mis-filed lockdown mode (§13.12). |

**C# wiring:** `App.Haptics` declared `App.xaml.cs:354`, constructed `:1563`, `AudioSync` `:1564`,
auto-connect `:1602`, disposed `:3483-3484`. `ToyInputService` and `FunScriptService` are
constructed by `HapticService`'s ctor (not `App.xaml.cs`) and reached as `App.Haptics.ToyInput` /
`App.Haptics.FunScript`.

---

## 12. Where to change X

| Want to… | Edit |
|---|---|
| Add a device provider | Implement `IHapticProviderV2`; `Register(...)` it in the `HapticDeviceManager` ctor (`:36-38`); add its key to `ProviderPreference` (`:21`) and to `EnsureRows`'s provider list (`HapticSettings.cs:770`); add a checkbox + `HapticProviderChipVm`. |
| Add a routing row | Add a member to `HapticEventKind` (**append** — names are settings keys), post it from the consumer, and add **one line** to `MainWindow.InitializeHapticsTab()`. `EnsureRows` creates the settings row for you. |
| Add a continuous source | Add a `HapticLayer` member + `SetLayer` from the producer. Bump `HapticSettingsV2.SchemaVersion` **only** if an existing install needs the row flipped (see the Luminance precedent, §8a). |
| Add a vibration mode | `VibrationMode` enum (`HapticSettings.cs:12`) + a case in `HapticPatterns.Render` (`:18`). |
| Change the safety ceiling | `HapticMixer.DefaultMasterCap` (`:77`) / `HapticSettingsV2.MasterCap`. The Max-power slider is in Tier 3. |
| Change the master multiplier | It **is** `HapticSettings.GlobalIntensity`, applied in `HapticMixer.Finish()`. |
| Retune a temperament | The five static presets in `HapticTemperament.cs:93-101`; the table in §5b. |
| Change Lovense wire behaviour | `LovenseProviderV2.SetOutputsAsync` (`:289`) for verbs/combining; `RunKeepAliveAsync` (`:594`) for the 25 s refresh; `LovensePatterns.StepsFor`/`FormatActionFragment` for quantization and action strings. |
| Change FunScript feel | `PositionLeadMs` / `SpeedToIntensity` (`FunScript.cs:138`) / `MinSpeedUnitsPerSec`+`MaxSpeedUnitsPerSec`. |
| Change DtRH mapping | The `Map` table in `DtrhHapticDirector.cs`; density in `GapMult`/`PlayAccent`. |
| Change premium gating | `HapticMixer.IsGateOpen` (`:151`) — one place now. |

---

## 13. Gotchas

1. **`GetToys` returns two different shapes.** Depending on Lovense Remote's firmware, `data.toys`
   is either a JSON **object** or an **escaped JSON string**. Parse both. Runtime capability truth
   is `shortFunctionNames`, not the model name.
2. **The HTTPS Lovense path frequently dies on router DNS-rebinding protection.** Always try
   `https://{dashed-ip}.lovense.club:30010` **and** fall back to plain `http://{ip}:20010`. The
   winner is session-only (`ActiveBaseUrl`) and must **not** be persisted — the user's network can
   change between launches.
3. **`timeSec:0` has no server-side watchdog — WE own the stop.** Lovense commands are indefinite,
   refreshed by a 25 s keep-alive. Zeros must always be transmitted explicitly (`Vibrate:0`), and
   `StopAllAsync` must clear the unchanged-send suppression cache **before** firing `Stop`, or the
   cache will swallow the very command that stops the toy. Sending a pattern or preset also
   invalidates that cache — the toy is no longer where the cache thinks it is.
4. **Buttplug 5.0.1 is message spec v4, not v3.** There is no `ScalarCmd`, no `VibrateCmd`, no
   `device.VibrateAsync()`, no `VibrateAttributes`. Use `OutputCmd` / `device.RunOutputAsync` /
   `device.Features`. The WebSocket connector ships **inside** the `Buttplug` package; the separate
   `Buttplug.Client.Connectors.WebsocketConnector` package is 3.x-only. Buttplug 5.0.1 also forces
   `Newtonsoft.Json >= 13.0.4` (the pinned 13.0.3 fails restore with NU1605).
5. **Settings-migration traps.** (a) No legacy property may EVER be renamed — they have no
   `[JsonProperty]`, so PascalCase IS the JSON key and a rename silently resets that user.
   (b) `EnsureV2Migrated` sets `_v2Migrated = true` **first**, before seeding, so the seeding writes
   rows rather than bouncing back through `SyncLegacyToRouting`. (c) Only bump `SchemaVersion` when
   an EXISTING install genuinely needs another pass — each pass runs once, forever, on every user.
   (d) `GlobalIntensity <= 0.05` is rescued to 0.7 on the schema-0→1 pass only; do not "fix" a
   legitimately low value later.
6. **Max output is 0.70, not 1.0.** `GlobalIntensity` (0.70) × `MasterCap` (0.70) both default to
   0.70 and the cap is applied after everything, including temperament. A user who says "it feels
   weaker than the old version" is describing the intended safety default, not a bug — Max power in
   Tier 3 is the opt-in.
7. **The routing rows resist UI automation.** Their `CheckBox`es live in a `DataTemplate` with a
   custom `ToggleStyle`, and UIA realises at most one at a time — a smoke test cannot toggle
   "Media > Audio sync" by AutomationId. Click the on-screen coordinate of the row's label `Text`
   element, and **scroll it into view first**: a click on a row that is only half in the viewport
   lands on the wrong control. Once a row is open, its slider IS reachable through
   `RangeValuePattern`, which is how "an edit still saves" is verified.
8. **`en.json` is byte-fragile.** It is CRLF with hand-grouped blank lines. Edit it with targeted
   text edits — **never** round-trip it through a JSON dumper or a format-on-save. The same applies
   to the other eight language files: write `\n`, never a literal newline inside a string, and let
   `core.autocrlf` handle line endings (all nine are LF in git, CRLF in the worktree).
9. **`PinkSlider` now has a real disabled visual.** `Resources/Theme/MainWindow.xaml:416` gained an
   `IsEnabled=False` trigger (`:462`) that dims groove, fill and thumb. The old per-control
   `Opacity = 0.4` workaround is gone app-wide — do not reintroduce it, and do bind `IsEnabled`
   rather than poking opacity.
10. **The mock toast window is a singleton for a reason.** Per-call windows leaked HWNDs and
    crashed the WPF render thread with `UCEERR_RENDERTHREADFAILURE` at audio-sync frame rate. The
    singleton was hoisted to `Core/MockToast.cs` so `MockProviderV2` and the legacy
    `MockHapticProvider` share **one** window. Keep it that way.
11. **The whole v1 provider path is dead code that still compiles.** `IHapticProvider`,
    `LovenseProvider`, `ButtplugProvider`, `MockHapticProvider` are constructed by nothing
    (`grep` for `new LovenseProvider()` returns zero call sites). A grep for "haptic provider" will
    surface them; they are not the shipping path.
12. **`LockdownService.cs` is mis-filed under `Services/Haptics/`.** Its namespace is
    `ConditioningControlPanel.Services` and it has zero haptics coupling — it is strict-lock/panic
    lockdown plumbing. Ignore it when reasoning about haptics.
13. **Two unrelated classes named `HapticTrack`.** `Models/HapticTrack.cs` is the runtime
    audio-analysis buffer used by AudioSync; `Models/Deeper/HapticTrack.cs` is the `.ccpenh.json`
    event schema used by Deeper. Same name, different namespace, unrelated.
14. **`EndAt` is computed from the UNSCALED envelope.** `HapticMixer.Play` derives a sequence's
    `EndAt` before temperament scales attack/decay, so under Tease/Cruel an **awaited** legacy call
    can complete a few tens of ms early or late relative to the audible tail. `ExpireActive` uses
    the SCALED length, so nothing is ever cut short on the toy — this is cosmetic, but do not
    "fix" it by making `ExpireActive` use the unscaled value.
15. **`IsConnected` can still lie.** A VPN or routing change can break reachability while the flag
    stays true. `PingAsync` touches the wire on both real providers; the 30 s watchdog tolerates
    **3 consecutive** failures (~90 s) before dropping, so one blip does not kill a session (#302).
16. **`ConnectAsync` returning true does not mean a toy exists.** `LovenseProviderV2` reports
    success when Remote answers with zero toys (raising an informational `Error`) and picks toys up
    on a 20 s poll as they pair. Neither the manager nor the UI should treat that as failure —
    the wizard has a dedicated "Connected, but no toy yet" state for it.
17. **Toy input is real hardware that most toys do not have.** Only some Lovense toys (via Game
    Mode) and some Intiface devices raise button/strength events. `ToyInputService.IsAvailable`
    gates the UI. A button press must always **ADD** to the mouse click for attention checks, never
    replace it.
18. **Still unverified against real hardware** (inherited open items): the Toy Events
    access-request frame shape (two plausible forms are sent; the first five received frames log at
    Debug — trim after a capture), `Position:` inside a Lovense `Function` action string
    (Solace Pro), PatternV2 `Setup`→`Play` timing semantics, `battery`/`status` field types across
    firmwares, and FunScript's 300 ms lead + 10→500 units/s speed mapping.

---

## 14. STATUS & BACKLOG — snapshot 2026-08-03 (VERIFY with git before acting)

- **State: the v6.7 overhaul is code-complete on `feat/haptics-overhaul`.** Phases A–F are done and
  ticked in `docs/HAPTICS_OVERHAUL_PLAN.md`; Phase G (ship prep) is in progress. `dotnet build` is
  clean (0 errors) in that worktree.
- **Done in Phase G so far:** all nine `Localization/Languages/*.json` carry the full
  `haptics_*` / `wizard_*` / `btn_mock_test_mode` set (182 keys per language, strict-JSON verified);
  the settings-migration round-trip has been exercised end to end (legacy v6.6.3 fragment → load →
  `EnsureV2Migrated` → serialize → reload, nothing lost, rows seeded, `GlobalIntensity` preserved,
  the ≤0.05 rescue and the schema-2 Luminance pass both confirmed).
- **Left on Phase G:** Mock play-test via the automated UIA method (ABORT if an app instance you did
  not start is already running), a real-toy pass by the owner, and the PR to `main`.
- **Deferred on purpose:** the full keyframe designer that would unify the six stock modes with
  Deeper's `StockHapticPatterns`. `Core/HapticPatterns.cs` is the single plug-in point and
  `UpdateHapticPatternPreview()` already accepts an arbitrary envelope and carries the marked TODO.
- **Engine-only, no consumer yet:** toy-button input for the lock-card alternative confirm and for
  quest interaction. Only the video attention check is wired.
- **Dead code awaiting deletion:** the v1 `IHapticProvider` path (§13.11). Left in-tree during the
  overhaul so nothing had to be deleted and re-added; it is a safe removal once the branch lands.
- **Test coverage:** `FunScript` (the pure parser/sampler) has unit tests. The mixer, the routing
  migration and the Lovense quantizer are the next most testable seams. Verify current coverage
  with a grep before claiming any.

---

## 15. Build / run / dev

```bash
cd ConditioningControlPanel && dotnet build && dotnet run
```

Haptics is Patreon-gated. Unlock premium, open the **Haptics** tab, tick a provider (**Mock needs no
hardware** and shows a corner toast with three virtual toys), **Connect**, then **Test** — or just
run the setup wizard, which does all of it with progress and failure hints. For real toys: Lovense
Remote on a phone with **Game Mode ON**, on the same Wi-Fi (the app finds the port itself), or
Intiface Central running with its server started. Watch `logs/` for `LovenseV2 …` /
`ButtplugV2 …` / `HapticMixer …` / `ToyInput …` / `FunScript …` / `DtrhHaptics …` Serilog lines.
**PANIC STOP** in the connection strip is always live and bypasses everything.
