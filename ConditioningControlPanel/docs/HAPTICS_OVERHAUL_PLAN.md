# Haptics Overhaul — Master Plan (2026-08-03)

Branch: `feat/haptics-overhaul` (worktree `C:\Projects\ccp-wt-haptics`, off origin/main f516cb25 = v6.6.3).
Status legend: [ ] todo, [x] done. Update this file as phases land.

## Locked decisions (owner-approved)
1. **One big release** — nothing ships early; everything lands on this branch.
2. **No direct-BLE** (`LovenseBLE_Lib.dll`) in v1 — LAN Game Mode + Buttplug/Intiface only. BLE is v2.
3. **All 5 Phase-4 features are in scope**: Toy Events input, FunScript, audio band-split, flash-luminance sync, temperament dial.
4. **Routing matrix is by ROLE** (Reward / Punish / Ambient / All), not per-individual-toy columns.
5. Only Opus agents implement; Fable coordinates. Premium gating stays (`App.Patreon?.HasPremiumAccess`) — now enforced in the service choke point too, not just UI.

## Why (current-state audit, key findings)
- `IHapticProvider.VibrateAsync(double intensity, int durationMs)` has **no device/actuator addressing** — root constraint. `ConnectedDevices` is display-only strings.
- `LovenseProvider` keeps a single `_toyId` overwritten per toy in `ParseToys` → **only the last toy is ever addressable** (v5.3.0 "multi-toy" commit c6f86f67 never worked).
- 200 ms client throttle drops sub-500 ms commands + `Math.Max(1, durationMs/1000)` rounds every duration up to 1 s → **all 6 vibration modes feel like the same mush** on Lovense.
- Two disagreeing intensity→0-20 mappers inside LovenseProvider; HttpClient never disposed; `SetMode` never called so Lovense-Connect GET branch is dead code.
- `ButtplugProvider` uses the DEAD `Buttplug.Client` 0.2.x-era package; broadcasts one intensity to all devices; `_activeDevices` list mutated from event threads without lock; `PingAsync` never touches the wire.
- `HapticService`: `ConnectAsync` force-sets `Settings.Enabled=true`; fire-and-forget patterns with unsynchronized `_currentEventType` + CTS dispose races (ObjectDisposedException → UnobservedTaskException); dead `RampUpAsync`; `Dispose` does `.Wait(1000)`.
- `HapticSettings`: **no `[JsonProperty]`** anywhere (PascalCase JSON — property renames silently reset users); dead `GlobalIntensity` + `VideoMode`; Blink settings exist with no UI row; `LovenseUrl` default `http://192.168.1.1:30010` (wrong pairing of scheme+port).
- UI (`Views/Tabs/HapticsTabView.xaml`, 824 lines): 9 copy-pasted feature rows; provider combo is MISSING Mock (the default!) → renders blank; white `#FFFFFF` URL TextBox; 3 dead gate overlays + 1 live; 2 fake "Coming Soon" sync algorithms.
- `HapticsSetupWindow`: informational-only; tutorial PNGs broken since `55f87208` (relative `Source="Resources/haptics_guide/..."` vs `/Windows/` BaseUri — fix with absolute pack URIs like `HapticsTabView.xaml:40`); says port 20010 while the tab says 30010.
- Consumers double-fire: `AchievementService.cs:760-761` and `QuestService.cs:983-984` call the pattern twice.
- No `App.Settings.Save()` on any per-feature haptics change (`MainWindow/MainWindow.Haptics.cs`).
- `DtrhHapticDirector` is the ONLY well-designed piece (static Gate lock, ambient layer + tiered accents + 700 ms coalescer) — its shape is the template for the mixer.

## Lovense LAN API cheat-sheet (from research)
- POST `https://{dashed-ip}.lovense.club:30010/command`, fallback **plain `http://{ip}:20010/command`** (router DNS-rebinding protection frequently kills the HTTPS path — MUST auto-fallback). Body: `{"command":"Function","action":"Vibrate:0-20","timeSec":N,"apiVer":1}`.
- Send header `X-platform: Conditioning Control Panel` on every request — it is displayed inside Lovense Remote = our branding surface.
- Actions: Vibrate/Rotate/Thrusting/Fingering/Suction/Oscillate/All 0-20; Pump/Depth 0-3; Stroke 0-100 (pair with Thrusting, span ≥20). Comma-combine actions. `timeSec:0` = indefinite (WE must guarantee stop — no server-side watchdog exists). `loopRunningSec/loopPauseSec` for loops; `stopPrevious:0` stacks commands.
- Multi-toy: omit `toy` = broadcast; `"toy":"<id>"` single; `"toy":[ids]` array needs Remote ≥7.71.0. One command TYPE per request → heterogeneous control = parallel requests.
- `GetToys` → parse `data.toys` as BOTH escaped-JSON-string and object (firmware varies). Runtime capability truth = `shortFunctionNames`. Reference: `https://developer.lovense.com/LovenseToySupportConfig.csv` (43 toys). Solace = thrust+depth NO vibrate; Solace Pro = thrust+position; Edge = 2 vibe motors; Lapis = 3.
- Patterns: v1 `{"command":"Pattern","rule":"V:1;F:v,r;S:1000#","strength":"20;15;10",...}` (S>100 ms, ≤50 strengths, apiVer:2); PatternV2 = keyframe Setup/Play/InitPlay/Stop/SyncTime (ts ≤ 7,200,000 ms; pos 0-100). Presets: pulse/wave/fireworks/earthquake.
- Position (Solace Pro): coasts ~300 ms after arrival, 1-2 s spin-up — lead the target.
- **Toy Events API**: `ws://{ip}:20010/v1` (wss 30010), access-request handshake, ping every 5 s. Events: `button-down/up/pressed`, `function-strength-changed`, `battery-changed`, `motion-changed`, `shake`. This is two-way input — toy buttons as app input.
- No documented rate limit → self-impose the 10 Hz output loop.
- NEVER route users through Lovense cloud (2025 breach history; privacy). LAN only.
- Buttplug fallback: NuGet **`Buttplug` 5.0.1** (BSD-3, pure .NET; the old `Buttplug.Client` is abandoned). Needs Intiface Central. Intiface has REMOVED Lovense-Connect + dongle support. Buttplug cannot do Solace depth (issue #611), button events, or presets → that's why dual-path. Verify at coding time whether 5.0.1 speaks spec v3 (`ScalarCmd`) or v4 (`OutputCmd`) — sources disagreed.

## Architecture (target)
```
Consumers (Flash, Video, Subliminal, Bubble, Quest, Achievement, Keyword,
           Blink, Gaze, Remote, AI, Deeper, DTRH, AudioSync, FunScript, Luminance)
        │  semantic API: PostEvent(HapticEventKind) / SetLayer(HapticLayer, 0..1)
        ▼
HapticService (facade; legacy method names kept as thin shims → mixer)
        ▼
HapticMixer  — continuous layers combined per-channel by MAX
             + transient envelopes {intensity, attack, hold, decay, priority}
             + temperament dial modulation
             + SAFETY: master cap (default 0.7), soft-ramp on start/resume,
               zero-on-disconnect/close/crash watchdog, panic stop (bypasses all)
        ▼  10 Hz per-device output loop: coalesce latest per actuator,
           quantize to native steps, suppress unchanged sends
HapticDeviceManager — N providers connected CONCURRENTLY, device registry,
                      per-device Role/Trim/Enabled persisted by stable key,
                      dedupe same physical toy seen via two providers (prefer Lovense)
        ▼
IHapticProviderV2:  LovenseProviderV2 (LAN JSON + Toy Events WS)
                    ButtplugProviderV2 (Buttplug 5.0.1)
                    MockProviderV2 (N virtual toys, capability presets, visualizer)
```
Contracts live in `Services/Haptics/Core/HapticContracts.cs` (written by Fable — DO NOT redesign; extend via new members only if genuinely required, and note it here).

Routing: config maps each `HapticEventKind` row → enabled + intensity + mode/pattern, targeted at a `ToyRole` column. Continuous layers route to roles too (Ambient default). A toy with Role=All hears everything.

## Phases & task list

### Phase A — Engine core (Agent A)
Files: `Services/Haptics/Core/HapticMixer.cs`, `HapticDeviceManager.cs`, `MockProviderV2.cs`,
`HapticPatterns.cs`, `MockToast.cs`; reworked `Services/Haptics/HapticService.cs`,
`DtrhHapticDirector.cs`, `Models/HapticSettings.cs`.
- [x] `HapticMixer`, `HapticDeviceManager`, safety layer, 10 Hz loop per device.
- [x] `MockProviderV2`: 3 virtual toys ("Mock Lush" vibe×1, "Mock Edge" vibe×2, "Mock Solace" Thrust 20 steps + Depth 3 steps, no vibe), toast visualization sharing the old Mock toast (the singleton was hoisted to `Core/MockToast.cs` so BOTH mocks use ONE window — HWND leak history).
- [x] `HapticSettingsV2` nested config with explicit `[JsonProperty]` everywhere + migration from old `HapticSettings` (no legacy property renamed or removed; 10× per-feature Enabled/Intensity/Mode → routing rows; `AudioSyncSettings` untouched).
- [x] Rewire `HapticService` as facade: every existing public method keeps its exact signature and now posts into the mixer; premium gate + Enabled gate at the choke point; CTS dispose races, force-enable on connect, dead RampUpAsync and `.Wait(1000)` dispose all gone.
- [x] `DtrhHapticDirector` re-based onto mixer (ambient → `HapticLayer.Dtrh`, accents → priority-tagged pulses). Tuning values unchanged.

**Decisions made in Phase A (integration + Phases E/F need these):**
- **Provider registration**: `HapticDeviceManager`'s ctor registers `MockProviderV2` only and carries
  the marker `// PROVIDER-REGISTRATION-POINT: LovenseProviderV2 + ButtplugProviderV2 added at
  integration`. Until those are wired in, a Lovense/Buttplug user connects to nothing.
- **Contract extension (the only one)**: `HapticLayer.Pattern` added to `HapticContracts.cs`. Authored
  Deeper keyframe envelopes (`SetSyncPatternAsync`) need their own layer so they cannot stomp
  `AudioSync` or `Manual`. Members added only; nothing renamed or removed.
- **Output ceiling changed**: every output is `min(raw × GlobalIntensity, MasterCap)` then × the
  per-device trim. Defaults `GlobalIntensity = 0.70` (the dead "Intensity" slider, now live) and
  `MasterCap = 0.70` mean **max output is 0.70, not 1.0**. Phase E must surface both and the patch
  notes must say so. Migration rescues a stored `GlobalIntensity <= 0.05` back to 0.7 (that slider did
  nothing before, so a parked 0 was never a real preference).
- **Mixing rules**: continuous layers combine by MAX (role-filtered, per-layer scaled); transient
  envelopes sum within a priority group and MAX across groups, then ride over the floor by MAX. The
  floor's RISE is slew-limited (soft ramp); transients are not, so short accents stay sharp.
- **Legacy props still drive the engine**: today's Haptics tab writes the flat properties, so
  `HapticSettings.OnPropertyChanged` mirrors them into the matching v2 routing rows. Keep that mirror
  until the Phase E UI binds the rows directly and the old controls are gone.
- **`HapticSettings.VideoMode` is now `[Obsolete]`** — 2 expected CS0618 warnings at
  `MainWindow.Haptics.cs:514` and `MainWindow.Settings.cs:344`. Phase E deletes that combo row.
- **Mode rendering** lives in `Core/HapticPatterns.cs` (the six `VibrationMode`s as envelope
  sequences). That is where the Phase E pattern editor should plug in.
- `HapticService.TestAsync` calls `HapticMixer.AllowTestWindow(4000)` so the Test button still works
  with the master toggle off (premium is still required).
- **New first-class API** for Phases E/F: `PostEvent(HapticEventKind, double?)`,
  `SetLayer(HapticLayer, double, int autoZeroMs = 0)`, `PanicStop()`,
  `PlayPatternAsync(intensity, ms, mode, priority, role, ct)`, plus
  `HapticMixer.SetPositionAsync(deviceKey, 0..1)` — Position is deliberately NOT driven by the
  generic mixer (it is placement, not intensity); FunScript owns it.

### Phase B — LovenseProviderV2 (Agent B)
Files: `Services/Haptics/LovenseProviderV2.cs`, `Services/Haptics/LovenseToyEventsClient.cs`,
`Services/Haptics/LovensePatterns.cs`. Legacy `LovenseProvider.cs` untouched (dies at integration).
- [x] Per-toy registry from `GetToys` (both parse shapes), capabilities from `shortFunctionNames`, battery, nickname.
- [x] `SetOutputsAsync` per device+actuator; heterogeneous = parallel requests; `timeSec:0` + refresh keep-alive OR short-timeSec repeats (pick one, document); StopAll bypass.
- [x] HTTPS `.lovense.club:30010` → HTTP `:20010` auto-fallback, one persisted working base URL, `X-platform` header.
- [x] Pattern v1 + PatternV2 senders for the pattern editor + presets (pulse/wave/fireworks/earthquake).
- [x] Toy Events WS client (`/v1`): handshake, 5 s ping, reconnect w/ backoff; surface `ToyEvent` (buttons, strength-changed, battery, shake). Feature-detect gracefully (older Remote versions lack it).
- [x] Dispose HttpClient properly; single intensity mapper.

**Decisions made in Phase B (integration + Phases E/F need these):**
- **Keep-alive = `timeSec:0` + 25 s refresh.** Every Function command is indefinite; a 1 Hz maintenance
  loop re-sends a device's current NON-ZERO level set once 25 s have passed since its last send.
  Short-`timeSec` repeats were rejected — they race their own expiry and leave audible seams at the
  seams. Because `timeSec:0` has no server watchdog, WE own the stop: zeros are always transmitted
  explicitly (as `Vibrate:0`, never a `Stop` spam), and `StopAllAsync` clears the suppression cache
  before firing per-toy `Stop`s.
- **Unchanged-send suppression** keys off the quantized value per Lovense ACTION VERB
  (`Vibrate`/`Vibrate1`/`Thrusting`/…), so the 10 Hz mixer loop puts nothing on the wire while levels
  hold. Sending a pattern/preset clears the cache (the toy is no longer where the cache thinks it is).
- **One request per device, comma-combined** (`Vibrate:5,Rotate:10`) — different verbs legitimately
  combine in one `Function` action string. Parallelism is across DEVICES, not across verbs.
- **Multi-motor discovery** comes from the short codes themselves (`v1`,`v2` = Edge; `v1..v3` = Lapis)
  → one `HapticActuator` per motor, 0-based `Index`, verb suffix preserved. Firmware that omits both
  `shortFunctionNames` and `fullFunctionNames` falls back to a small model table, else one Vibrate.
- **`Stroke` is a range, not a level**: intensity → `Stroke:0-{20..100}` (span always ≥ 20); a zero
  request omits the fragment (`Thrusting:0` is what actually stops it).
- **No settings writes.** The winning base URL is session-only (`ActiveBaseUrl`). The configured address
  comes from `ConfiguredUrlOverride` first, else reflectively from `Haptics.LovenseUrl` /
  `LovenseAddress` / `LovenseIp` / `LovenseHost`, so the HapticSettings→V2 rework cannot break provider
  compilation. **Integration should set `ConfiguredUrlOverride` explicitly and retire the reflection.**
- `ConnectAsync` returns **true when Remote answers with zero toys** (raises an informational `Error`);
  the 20 s poll picks toys up as they pair. The device manager should not treat that as a failure.
- **Open / needs a real toy:** (a) the Toy Events access-request FRAME is not in the published Standard
  API docs — two plausible shapes are sent and the first 5 received frames log at Debug; trim to one
  after a capture. (b) `Position:` inside a `Function` action string is assumed (Solace Pro).
  (c) `battery`/`status` field types vary by firmware — both string and number are accepted, unverified.
  (d) PatternV2 `Setup`→`Play` timing (`startTime`/`offsetTime` semantics) is unexercised.

### Phase C — ButtplugProviderV2 (Agent C)
- [x] Swap csproj package to `Buttplug` 5.0.1; rewrite client against the real 5.x API (verify spec v3 vs v4 empirically).
- [x] Per-device dispatch (scalar per feature index), device add/remove events under lock, real ping.
- [x] Map Buttplug device features → `HapticActuator` list.

**VERDICT: `Buttplug` 5.0.1 speaks message spec v4, NOT v3.** Verified empirically by reflecting over
`buttplug\5.0.1\lib\netstandard2.1\Buttplug.dll`. There is **no** `ScalarCmd`/`ScalarSubcommand`,
no `VibrateCmd`, no `device.VibrateAsync()`, no `VibrateAttributes`. The v4 surface actually present:
- `Buttplug.Core.Messages.OutputCmd` / `InputCmd`; enums `OutputType`
  {Unknown, Vibrate, Oscillate, Rotate, Position, HwPositionWithDuration, Led, Temperature, Constrict, Spray}
  and `InputType` {Unknown, Battery, RSSI, Button, Pressure, Depth, Position}.
- Capabilities are **per-feature**: `ButtplugClientDevice.Features` (`IReadOnlyDictionary<uint, ButtplugClientDeviceFeature>`),
  `HasOutput(OutputType)`, `GetFeaturesWithOutput(OutputType)`, `feature.TryGetOutputRange(type, out min, out max)`
  (= native step range), `feature.FeatureDefinition` (`DeviceFeature.Output` is `Dictionary<string, DeviceFeatureOutput>`).
- Output: `device.RunOutputAsync(uint featureIndex, DeviceOutputCommand, ct)` /
  `feature.RunOutputAsync(...)`; commands built with `new DeviceOutputCommand(OutputType, PercentOrSteps, uint? duration)`
  or the `DeviceOutput.Vibrate.Steps(n)/.Percent(x)` builders. `device.StopAsync()`, `client.StopAllDevicesAsync(ct)`.
- Input: `device.BatteryAsync(TimeSpan?, ct)`, `feature.RunInputAsync(DeviceInput.Button.Subscribe())`,
  `client.InputReadingReceived` → `InputReadingEventArgs { DeviceIndex, FeatureIndex, Reading }`.
- Connector ships **inside** the same package (`Buttplug.Client.ButtplugWebsocketConnector`); the separate
  `Buttplug.Client.Connectors.WebsocketConnector` package is 3.x-only and was removed from the csproj.
- Buttplug 5.0.1 requires `Newtonsoft.Json >= 13.0.4`; the project's pinned 13.0.3 had to be bumped to
  13.0.4 or restore fails with NU1605 (package downgrade). That is the only other csproj change.

Actuator mapping (Buttplug has no Thrust/Finger/Suction/Pump/Depth/Stroke output type — Lovense-only):
`Vibrate→Vibrate`, `Rotate→Rotate`, `Oscillate→Oscillate`, `Position`+`HwPositionWithDuration`→`Position`,
`Constrict→Constrict`; `Led`/`Temperature`/`Spray` ignored. `HapticActuator.Index` is a per-type 0-based
ordinal (Edge = Vibrate#0/#1) mapped internally back to the Buttplug feature index; `Steps` is the
feature's own advertised max (fallback 100 for Position, 20 otherwise). Buttplug outputs **latch**, so
`SetOutputsAsync` sends only on a quantized-step change — no keep-alive. `PingAsync` is a real wire
round-trip: `RequestDeviceList` sent through our retained connector (the client exposes no public ping).
Device id = Intiface display name (else model name), ':' stripped, `#n` for duplicates — the numeric
Buttplug device index is session-scoped and unusable as a persisted key.

### Phase D — Standalone fixes (Agent D, files disjoint from A/B/C)
- [x] Wizard images → absolute pack URIs; wizard port text 30010-vs-20010 unified.
      All 4 PNGs verified present + embedded (`Resource Include="Resources\haptics_guide\*.png"`
      already covered them, no csproj edit needed). Wizard now says the app finds the port
      automatically and to keep Game Mode ON; canonical example is `http://IP:30010`.
- [x] De-dupe double pattern calls: `QuestService.cs:983-984`, `AchievementService.cs:760-761`.
- [x] `App.Settings.Save()` on every haptics handler in `MainWindow/MainWindow.Haptics.cs`
      (`SettingsService.Save()` is itself 500 ms-debounced, so per-tick slider calls coalesce
      into one write — no extra drag-completed plumbing needed).
- [x] Fix provider-combo load-match failure for Mock (`MainWindow/MainWindow.Settings.cs:267-274`)
      + added the `Mock (Test Mode)` row to `HapticsTabView.xaml` (loc key `btn_mock_test_mode`,
      **en.json only** — the other 8 languages fall back to English until Phase G).

### Phase E — UI rebuild (after A-D merge)
- [x] `HapticsTabView` redesign, app visual language (cards #252542, pink #FF69B4 accents, pill badges):
      status strip (provider chips, device count, Connect/Disconnect, PANIC STOP);
      toy cards grid (name/nickname, battery %, capability chips, Role picker, trim slider, Test);
      routing matrix (groups Core/Rewards/Media/Games; per row: toggle + intensity + pattern + target ROLE);
      dead overlays + fake Coming-Soon rows removed; Blink row added; per-provider checkboxes replace the
      single-choice combo; white TextBox restyled.
- [x] Pattern lab: the six `VibrationMode`s with a live envelope preview drawn from the ENGINE's own
      renderer, plus play-on-a-chosen-toy. Full keyframe designer deliberately deferred (see below).
- [x] Setup wizard v2: writes the v2 provider flags + Lovense address, runs the real Connect with
      progress, device list, actionable failure hints and a Test buzz; Mock/demo path included.
- [x] Converters in the UserControl's own Resources root (DataTemplate rule); `DispatcherPriority.Normal`
      for every device-manager callback (Loaded is starved in this app).

**Decisions made in Phase E (Phase F needs these):**
- **The tab is data-driven now.** `Views/Controls/HapticUiModels.cs` holds `HapticRoutingRowVm` /
  `HapticRoutingGroupVm` / `HapticToyCardVm` / `HapticProviderChipVm` plus two converters. Each VM writes
  straight through to the settings object the engine reads and calls `App.Settings.Save()` (debounced).
  Adding a row in Phase F = one line in `MainWindow.InitializeHapticsTab()`, not a XAML copy-paste.
- **Which property a row writes is per-row and is NOT guessable.** Event rows -> `V2.Rule(kind)`.
  The **Video** row writes the LEGACY `VideoIntensity`/`VideoEnabled` because
  `HapticService.StartVideoBackgroundVibeAsync` reads the legacy level (the layer rule only gates/scales).
  The **AudioSync** row writes BOTH `AudioSync.Enabled` and the layer rule, because the service early-outs
  on the former. `HapticRowLegacyBinding` encodes exactly this.
- **New facade method**: `HapticService.TestDeviceAsync(deviceKey, mode, intensity, durationMs)` drives ONE
  device directly (the mixer mixes by role; a per-toy test is not a role). Master multiplier, master cap
  and the per-device trim still apply, and the device is always explicitly zeroed on the way out.
  `HapticPatterns.SampleAt(steps, tMs)` was added alongside it so the preview strip and the per-toy test
  use the engine's real envelope shape rather than a look-alike curve.
- **Provider selection is now per-provider `V2.Provider(key).Enabled`** (checkboxes), which is what
  `HapticDeviceManager.EnabledProviders()` reads. The legacy `Provider` enum is still written
  (lovense > buttplug > mock) for old call sites. `App.xaml.cs` auto-connect no longer skips Mock.
- **URL inconsistency resolved**: one box, always `Haptics.LovenseUrl`. The old box switched between
  LovenseUrl and ButtplugUrl based on the combo, so a typed value could land on the wrong setting.
  Intiface's address is not user-editable here (the provider owns the Intiface default).
- **Audio-sync enable moved** from its own card checkbox to the Media > Audio sync routing row.
  `MainWindow.xaml.cs`'s init block for `ChkHapticAudioSync` + the Video-Haptic-Sync sliders was deleted;
  `LoadHapticsSettingsToUi()` owns all of it now. The tuning card (delay/power) kept its element names.
- **Deleted UI**: `HapticsConnectionLock`, `HapticsFeatureLock`, `HapticsComingSoonOverlay` (3 dead
  overlays; `MainWindow.Patreon.cs` updated), the two fake "Coming Soon" sync-algorithm cards, the
  provider combo, and the `VideoMode` combo (both expected CS0618 warnings are gone).
- **Deferred on purpose** (architecture hook left in place): the full keyframe designer unifying the six
  stock modes with Deeper's `StockHapticPatterns`. `Core/HapticPatterns.cs` is still the single plug-in
  point; `UpdateHapticPatternPreview()` carries the marked TODO and already accepts an arbitrary envelope.
- **Phase F slots** (where the new dials belong in this layout):
  * **Temperament dial** -> the "Power" card (`haptics_global_dials`) as a third row under Master
    intensity / Max power: a preset picker (Gentle/Tease/Cruel) writing mixer multipliers. That card is
    already the home for global modifiers and has room for exactly one more row.
  * **FunScript** -> one more row in the Media group (new `HapticEventKind`/layer + a `HapticRoutingRowVm`
    line), with the script-folder picker in a small sub-card under the routing list next to the DtRH
    extras block — that block is the established place for knobs the matrix cannot express.
  * **Toy-button input** -> a "Toy input" sub-card under the toy grid; per-toy bindings belong on
    `HapticToyCardVm`, which already carries DeviceKey and the capability chips.
  * **Audio band-split / flash luminance** -> extra Media rows; the six hidden DSP knobs fit the existing
    Video Haptic Sync tuning card as an Advanced expander.

### Phase F — Features (after E; each cuttable)
Engine AND UI are done. The defaults are still the shipping behaviour — the UI pass exposed the
knobs without changing a single default, and everything it added is collapsed at rest (see the
Phase F UI decisions at the end of this section).
- [x] **Toy-button input** (engine): `Services/Haptics/ToyInputService.cs` — debounced `ButtonPressed`
      + awaitable `WaitForButtonAsync(timeoutMs, ct)`, `StrengthChanged` → mixer back-off.
      ONE consumer wired: VideoService attention checks (`AttentionCheckToyButton`, default OFF —
      a press ADDS to the mouse click, never replaces it; success posts `ToyButtonReward`).
  - [x] UI: "Toy buttons and dials" collapsed Expander under the toy grid — master toggle,
        the attention-check opt-in, the 5-120 s back-off slider, and a live
        "Backing off - you took control" badge in the expander HEADER (visible while collapsed).
  - [ ] Remaining consumers (lock-card alternative confirm, quest interaction) — still engine-only.
- [x] **FunScript** (engine): `Services/Haptics/FunScript.cs` (pure parser/sampler, unit-tested) +
      `Services/Haptics/FunScriptService.cs` (wiring). Auto-loads `<video>.funscript` then
      `<video-dir>\funscripts\<name>.funscript`; Position actuators get a 300 ms lead, everything
      else gets the speed→intensity envelope on `HapticLayer.Pattern`. Default ON, zero-config.
  - [x] UI: "Video sync scripts" collapsed Expander in the Media extras block — enable toggle +
        "convert to vibration for toys that cannot stroke" + a "Following &lt;file&gt;.funscript"
        badge in the header, bound (1 Hz poll) to `FunScriptService.LoadedScriptPath` and
        Collapsed while it is null. No folder picker: discovery is beside-the-video by design,
        so a picker would imply a choice that does not exist.
- [x] **Audio-sync v2 band-split** (engine): `AudioSyncSettings.BandSplit` (`band_split`, default OFF)
      → `AudioAnalyzer.Analyze(..., wantBands, out low, out high)` from the SAME FFT pass →
      `ChunkManager.LowBandTrack/HighBandTrack` → `HapticService.SetSyncIntensityAsync(low, high)`
      → `HapticMixer.SetLayerPerActuator`. Only engages when a connected toy has ≥2 Vibrate
      actuators; 1-motor toys are byte-identical to before.
  - [x] UI: "Advanced tuning" collapsed Expander inside the Video Haptic Sync card (it follows the
        same `AudioSync.Enabled` visibility gate as the delay/power sliders): the band-split toggle
        plus the six DSP knobs — Sensitivity (0.10-3.00x), Smoothing (0-95%), Bass weight,
        Volume weight (RMS), Beat weight (onset), Ceiling (`MaxIntensity`, 50-100%) — and a
        "Reset to defaults" button whose values are read from a fresh `AudioSyncSettings`
        instance, never hard-coded. `MinIntensity` is deliberately NOT exposed: the mixer's own
        `MinPerceptibleIntensity` already owns the floor, and a user-set floor reads as a toy that
        will not stop buzzing.
- [x] **Flash-luminance sync** (engine): `HapticSettings.LuminanceSyncEnabled` (default OFF) +
      `LuminanceSyncIntensity` (0.5). `FlashService.ApplyLuminanceSync` samples the ALREADY-DECODED
      frozen `BitmapSource` down to 8×8 (WIC, no re-decode, per-file cached) → `HapticLayer.Luminance`
      with `autoZeroMs = the flash's own lifetime`, so there is no hide hook to get wrong.
      **SubliminalService has no image path** (text-only + whisper audio), so nothing was wired there.
  - [x] UI: enable toggle in the Extras strip, 0-100% strength slider (`LuminanceSyncIntensity`)
        in Advanced. (The per-control 0.4-opacity workaround this used to carry is GONE —
        `PinkSlider` itself now has a real `IsEnabled=False` visual; see the design pass below.)
- [x] **Temperament dial**: `Services/Haptics/Core/HapticTemperament.cs` — five presets, each a
      multiplier set applied inside `HapticMixer`. Settings key `HapticSettingsV2.Temperament`
      (`[JsonProperty("temperament")]`, default `"balanced"`; an unrecognised value falls back to
      Balanced). UI: a 5-chip segmented `RadioButton` row in the Power card with a one-line
      description that updates on selection — the ONE new control kept in plain sight.

  **Multiplier table** (Balanced = all 1.0 / bias 0 = pre-temperament behaviour):

  | Preset   | Continuous | Transient | Attack | Decay | Pulse-priority bias |
  |----------|-----------:|----------:|-------:|------:|--------------------:|
  | Gentle   | 0.70 | 0.75 | 1.40 | 1.30 | -1 |
  | Balanced | 1.00 | 1.00 | 1.00 | 1.00 |  0 |
  | Tease    | 0.85 | 0.90 | 1.60 | 1.80 |  0 |
  | Intense  | 1.15 | 1.20 | 0.80 | 0.90 | +1 |
  | Cruel    | 1.25 | 1.40 | 0.45 | 0.70 | +2 |

  Where each column lands in the mixer:
  - **Continuous** multiplies the layer rule's own intensity in `BuildOutputs` (so the band-split
    per-motor path inherits it for free — it reuses the same `layerScale`).
  - **Transient** multiplies each pulse sample as the priority groups are summed, before the
    group clamp to 1.0.
  - **Attack / Decay** scale the envelope segments in `PromotePending` as a pending step becomes an
    active pulse. HOLD is untouched, so a pattern keeps its rhythm and only changes its EDGE.
  - **Pulse-priority bias** is added to `MaxConcurrentPulses` (clamped 1-12). Priority is only ever
    consulted when that window is full and the weakest active pulse must be evicted, so biasing
    the window is the concrete meaning of "priority bias" in this mixer.
  - Everything lands BEFORE `Finish()` = `min(raw x GlobalIntensity, MasterCap) x deviceTrim`, so
    **the cap always wins** and a >1.0 scale can never exceed the user's safety ceiling.
  - Known cosmetic wrinkle: `HapticMixer.Play` computes a sequence's `EndAt` from the UNSCALED
    envelope, so with Tease/Cruel an awaited legacy call can complete a few tens of ms early/late
    relative to the audible tail. `ExpireActive` uses the SCALED length, so nothing is ever cut
    short on the toy.

**Decisions made in Phase F (Phase E/G need these):**
- **Contract extensions: none.** `HapticContracts.cs` was not touched — `HapticLayer.Luminance` and
  `HapticEventKind.ToyButtonReward` were already reserved by Phase A.
- **New mixer surface (additive):**
  - `HapticMixer.SetLayerPerActuator(HapticLayer, double[]? perMotor)` — a layer may carry a
    per-vibration-motor breakdown. The scalar layer value is kept at `max(perMotor)`, so a toy with
    <2 Vibrate actuators sees exactly the old behaviour. The split only sets the RATIO between
    motors; soft-ramp, master multiplier, master cap and per-device trim still apply once each.
    Null clears it; `SetLayer`/`PlayLayerEnvelope`/`ClearAll` all clear it implicitly.
  - `HapticMixer.SuppressLayersUntil(DateTime utc)` + `AreLayersSuppressed` — mutes every CONTINUOUS
    layer for a while when the user changes strength ON the toy. Transients deliberately still fire
    (an achievement buzz is an event, not the app taking the dial back).
- **New facade surface (additive):** `HapticService.ToyInput`, `.FunScript`, `.SetLayerPerActuator`,
  `.SuppressContinuousLayers(seconds)`, `.MaxVibrateMotors`, `.HasPositionActuator`,
  `.SetPositionAsync(0..1)` (fans out to every Position-capable toy via the mixer's per-device
  `SetPositionAsync`), and an overload `SetSyncIntensityAsync(low, high)` for the band split.
- **No App.xaml.cs registration.** That file belongs to the UI rebuild, so `ToyInputService` and
  `FunScriptService` are constructed by `HapticService`'s ctor and disposed by its `Dispose`.
  Consumers reach them as `App.Haptics.ToyInput` / `App.Haptics.FunScript`.
- **`HapticSettingsV2.SchemaVersion` is now 2.** Schema 1 seeded the `Luminance` LAYER row disabled
  ("Phase F, off until it exists"); pass 2 enables that row, because the FEATURE toggle
  (`LuminanceSyncEnabled`, default false) is the real gate and a disabled layer row would have
  silently vetoed it. No legacy property was renamed, removed or reset.
- **Video hooks are three surgical edits** in `Services/Video/VideoService.cs`: FunScript start next
  to `StartVideoBackgroundVibeAsync` in `StartVideoPlayback`, FunScript stop next to
  `StopVideoBackgroundVibeAsync` in `CloseAll`, and the toy-button alternative inside `SpawnTarget`
  (subscribe on spawn, unhook on hit or expiry, `FloatingText.Hit()` = the same idempotent pipeline
  the mouse and gaze-click use).
- **FunScript sync uses the existing `PrimaryPlaybackTimeMsChanged` event**, extrapolated with the
  wall clock between reports at 20 Hz; no report for 900 ms = paused/stopped ⇒ the layer zeroes.
  A seek is just a report that disagrees with the extrapolation, so it self-corrects.
- **Open / needs a real toy:** (a) `Position:` inside a Lovense `Function` action string is still
  assumed (Phase B's open item) — the FunScript position path inherits that risk. (b) The 300 ms
  position lead and the 10→500 units/s speed→intensity mapping are from the published conversion
  conventions, not measured on hardware. (c) Toy Events button frames were never captured from a
  real toy (Phase B open item (a)), so the attention-check alternative is untested end to end.

**Decisions made in the Phase F UI pass (the design agent needs these):**
- **Owner's mid-flight call: the tab was already too dense.** So of the five Phase F features,
  only the temperament picker is visible at rest. Toy input, FunScript, flash brightness and the
  audio DSP knobs each live in a `CollapsibleCard` Expander that is `IsExpanded="False"`, showing
  a plain-language header plus one line of description. No new always-visible slider was added.
- **Live state lives in expander HEADERS, not bodies** ("Backing off - you took control",
  "Following &lt;script&gt;"), so a collapsed section can still tell you something is happening.
- **Layout and logic are separated on purpose.** Every new control is loaded by one small
  `Load*ToUi(HapticSettings)` helper in `MainWindow.Haptics.cs` and written by one handler; none
  of them know where the control sits. Moving the XAML requires touching no C# beyond the
  `HapticsTab.<name>` references.
- **The routing rows resist UI automation.** Their `CheckBox`es sit in a `DataTemplate` with a
  custom `ToggleStyle`, and UIA realises at most one of them at a time, so a smoke test cannot
  toggle "Media > Audio sync" by AutomationId — it has to click the on-screen coordinate. Worth
  knowing before anyone writes a UIA test against the redesigned tab.
  *(Still true after the F2 design pass, and now it also applies to OPENING a row: click the
  coordinate of the row's label `Text` element, and scroll it into view first — a click on a row
  that is only half in the viewport lands on the wrong control. The row's slider, once open, IS
  reachable through `RangeValuePattern`, which is how the "an edit still saves" check is done.)*

### Phase F2 — Design / information-architecture pass (2026-08-03)

**Why:** the owner reviewed the finished tab and rejected it — *"so many sliders in plain sight and
options, this is a recipe for choice fatigue. If I had seen this UI at first glance I'd have probably
closed it."* Nothing was added or removed functionally; every control that existed still exists and is
still reachable. What changed is **what is visible when**.

**Before → after (maximised 1080p, tab freshly opened, disconnected):**
| | before | after |
|---|---|---|
| sliders visible on first paint | 3 (Master intensity, Max power, + the DtRH/row wall one scroll down) | **1** (Intensity) |
| cards/sections on first paint | 6 (intro, connection, Power, toys, routing matrix, …) | **5** (intro, connection, How it feels, Your toys, the Customize/Advanced doors) |
| controls reachable without a click | ~60 | ~10 |
| height of the tab at rest | ~5.5 viewports | **~1.5 viewports** |

**Tier 1 — first paint.** Header, a shrunk "what is this?" card (image 160→96 px, stale bullet list
dropped), the connection strip, a **How it feels** card holding the ONE `Intensity` slider
(`GlobalIntensity`) plus the five temperament chips at full size, and the toy cards. Then two
collapsed doors. Max power is NOT here any more — it is a safety net, not a volume knob.

**Tier 2 — `Customize` (`HapticCustomizeSection`, one click).** The routing matrix, redesigned:
a row at rest is `[toggle] icon Name .......... "50% · Pulse · All" ›`. The strip right of the toggle
is one `ToggleButton`; clicking it reveals that row's strength slider, pattern combo and target combo
inline. `HapticRowExpansionScope` keeps **one row open at a time** across every group. Group headers
(Core/Rewards/Media/Games) are unchanged. Below the list, the **Extras** strip: five plain switches
that belong to no single row (FunScript enable, flash-brightness enable, toy-button input, band split,
and the indented "squeezing passes attention checks").

**Tier 3 — `Advanced` (`HapticAdvancedSection`, collapsed, at the bottom).** Safety ceiling
(Max power + its warning), the toy-input back-off slider, FunScript "convert to vibration", flash
brightness strength, the DtRH ambient/density pair, the Video-Haptic-Sync card (delay/power + the
`HapticAudioAdvanced` DSP drawer, now 6 knobs — band split was promoted to Extras), and the Pattern lab.

**Mechanics worth knowing:**
- **Nothing moved in C# except two lines.** `MainWindow.Haptics.cs` gained `_hapticRowScope` and
  assigns it to each row; every other handler and every `Load*ToUi` helper is untouched, because the
  x:Names were kept. This is exactly what the Phase F "layout and logic are separated" note bought.
- **x:Names that moved but did not change**: `SliderHapticMaxPower`/`TxtHapticMaxPower`/
  `HapticMaxPowerWarning`, `SliderHapticOverrideCooldown`/`TxtHapticOverrideCooldown`,
  `ChkHapticToyInput`, `ChkHapticToyAttentionCheck`, `ChkHapticFunScript`, `ChkHapticFunScriptVibe`,
  `ChkHapticLuminance`, `SliderHapticLuminance`/`TxtHapticLuminance`, `ChkHapticBandSplit`,
  `SliderHapticDtrhAmbient`/`TxtHapticDtrhAmbient`/`CmbHapticDtrhDensity`, the whole pattern lab,
  the whole audio-sync card, `TxtHapticActivity`, `BtnHapticTest`, `BtnHapticsHelp`, the three
  `ChkHapticProvider*` and `TxtHapticUrl`/`TxtHapticUrlHint`/`ChkHapticAutoConnect`.
- **x:Names deleted**: `HapticToyInputSection`, `HapticFunScriptSection`, `HapticLuminanceSection`
  (those three Expanders dissolved into Extras/Advanced). **New**: `HapticConnectionSetup`,
  `HapticCustomizeSection`, `HapticAdvancedSection`.
- **`HapticsFeatureBox` is now an invisible wrapper** around the two doors (it only ever existed so
  `MainWindow.Patreon.cs` could disable the feature half wholesale — that still works).
- **Live badges follow their feature.** `HapticOverrideBadge` ("Backing off - you took control") moved
  into the **Your toys** header, so it is visible at rest even though its settings are two tiers down;
  `HapticFunScriptLoadedBadge` ("Following x.funscript") moved into the **Customize** header.
- **`PinkSlider` finally has a disabled visual** (`Resources/Theme/MainWindow.xaml`): an
  `IsEnabled=False` trigger dims the groove, the pink fill and the thumb. `SetSliderEnabled` no longer
  pokes `Opacity` per control, and the routing rows can now bind `IsEnabled` to `RowEnabled` and look
  right for free. This fixes the open issue noted in the Phase F luminance entry — app-wide, not just here.
- **New VM surface** in `Views/Controls/HapticUiModels.cs`: `HapticRowExpansionScope`,
  `HapticRoutingRowVm.IsExpanded` / `.Scope` / `.ValueSummary` (the pill text, `"Off"` when the row is
  off), and `ModeVisibility` changed `Hidden` → `Collapsed` now that the combo is in a stacked body.
- **Loc (en.json only, as before).** Added: `haptics_feel`, `haptics_feel_sub`, `haptics_intensity`,
  `haptics_customize`, `haptics_customize_sub`, `haptics_advanced`, `haptics_advanced_sub`,
  `haptics_extras`, `haptics_extras_sub`, `haptics_connection_setup`, `haptics_connection_setup_sub`,
  `haptics_row_off`, `haptics_row_tap_hint`, `haptics_safety`. Removed (verified unused):
  `desc_haptics_bullets`, `haptics_global_dials`, `haptics_global_dials_sub`, `haptics_routing_sub`,
  `haptics_band_split_sub`. Reworded: `haptics_audio_advanced(_sub)` (band split left that drawer),
  `haptics_band_split_tip` (absorbed the removed sub-line).
- **Do not "fix" the file by reformatting it.** `en.json` is CRLF with hand-grouped blank lines; edit it
  with targeted text edits, never by round-tripping through a JSON dumper.

### Phase G — Ship prep
- [ ] Localization keys → all 9 `Localization/Languages/*.json` (STRICT JSON, `\n` escaped, never hand-flip line endings).
- [ ] Settings migration test: old JSON loads → nothing resets.
- [ ] Update `docs/primers/HAPTICS_PRIMER.md` (§12 gotchas, §13 backlog).
- [ ] Build clean; Mock play-test (automated UIA method — ABORT if an app instance we didn't start is running); real-toy pass by owner.
- [ ] PR to main.

## Verification bar (every agent)
- `dotnet build` clean in THIS worktree (main builds; the other worktree's breakage was grace-pause WIP, not ours).
- No public-API breaks: all ~25 consumer call sites compile untouched (Phases A-D).
- No settings resets: old `settings.json` haptics section round-trips.
- WPF traps: converters in Window.Resources; check `IsLoaded`/`Template != null` before animations; no fire-and-forget without dispatcher-null + try/catch guard.
