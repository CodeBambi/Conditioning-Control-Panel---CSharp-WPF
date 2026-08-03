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
- [ ] `HapticMixer`, `HapticDeviceManager`, safety layer, 10 Hz loop per device.
- [ ] `MockProviderV2`: 2-3 virtual toys (e.g. "Mock Lush" vibe×1, "Mock Edge" vibe×2, "Mock Solace" thrust+depth no-vibe), toast/log visualization compatible with the old Mock toast (singleton HWND pattern preserved — HWND leak history).
- [ ] `HapticSettingsV2` nested config with explicit `[JsonProperty]` everywhere + migration from old `HapticSettings` (old property names must keep loading; map 10× per-feature Enabled/Intensity/Mode → routing rows; keep `AudioSyncSettings` snake_case keys working).
- [ ] Rewire `HapticService` as facade: keep EVERY existing public method compiling (~25 call sites) as shims → mixer; premium gate + Enabled gate at the choke point; kill CTS races, force-enable on connect, dead RampUpAsync, `.Wait(1000)` dispose.
- [ ] `DtrhHapticDirector` re-based onto mixer (ambient → continuous layer, accents → transients). Preserve its exact tuning values.

### Phase B — LovenseProviderV2 (Agent B)
- [ ] Per-toy registry from `GetToys` (both parse shapes), capabilities from `shortFunctionNames`, battery, nickname.
- [ ] `SetOutputsAsync` per device+actuator; heterogeneous = parallel requests; `timeSec:0` + refresh keep-alive OR short-timeSec repeats (pick one, document); StopAll bypass.
- [ ] HTTPS `.lovense.club:30010` → HTTP `:20010` auto-fallback, one persisted working base URL, `X-platform` header.
- [ ] Pattern v1 + PatternV2 senders for the pattern editor + presets (pulse/wave/fireworks/earthquake).
- [ ] Toy Events WS client (`/v1`): handshake, 5 s ping, reconnect w/ backoff; surface `ToyEvent` (buttons, strength-changed, battery, shake). Feature-detect gracefully (older Remote versions lack it).
- [ ] Dispose HttpClient properly; single intensity mapper.

### Phase C — ButtplugProviderV2 (Agent C)
- [ ] Swap csproj package to `Buttplug` 5.0.1; rewrite client against the real 5.x API (verify spec v3 vs v4 empirically).
- [ ] Per-device dispatch (scalar per feature index), device add/remove events under lock, real ping.
- [ ] Map Buttplug device features → `HapticActuator` list.

### Phase D — Standalone fixes (Agent D, files disjoint from A/B/C)
- [ ] Wizard images → absolute pack URIs; wizard port text 30010-vs-20010 unified.
- [ ] De-dupe double pattern calls: `QuestService.cs:983-984`, `AchievementService.cs:760-761`.
- [ ] `App.Settings.Save()` on every haptics handler in `MainWindow/MainWindow.Haptics.cs`.
- [ ] Fix provider-combo load-match failure for Mock (`MainWindow/MainWindow.Settings.cs:267-274`).

### Phase E — UI rebuild (after A-D merge)
- [ ] `HapticsTabView` redesign, app visual language (cards #252542, pink #FF69B4 accents, pill badges):
      status strip (provider chips, device count, last-ping age, Connect/Disconnect, PANIC STOP);
      toy cards grid (icon, name/nickname, battery %, capability chips, Role picker, trim slider, Test);
      routing matrix (rows = event groups Core/Rewards/Media/Games; columns = Reward/Punish/Ambient; cell = toggle+intensity; shared mode/pattern picker per row);
      remove dead overlays + fake Coming-Soon rows; add Blink row; Mock in provider list (or auto-provider UI);
      white TextBox restyled.
- [ ] Pattern editor: unify 6 `VibrationMode`s + 6 Deeper `StockHapticPatterns` into one keyframe editor w/ live preview on selected toy (borrow Deeper editor curve UI).
- [ ] Setup wizard v2: actually writes settings + runs Connect with live feedback; Mock/demo path; phone-free note (official Lovense Remote for Windows exists).
- [ ] Converters in Window.Resources (DataTemplate rule); `DispatcherPriority.Normal` not Loaded (starvation trap).

### Phase F — Features (after E; each cuttable)
- [ ] **Toy-button input**: attention-check option "squeeze your toy" (VideoService attention checks), lock-card alternative confirm, quest interaction; strength-changed → user-override backoff.
- [ ] **FunScript**: auto-load `<video>.funscript` beside videos + script folder; position for thrusters (lead 300 ms coast), stroke→vibe envelope conversion otherwise.
- [ ] **Audio-sync v2**: band-split bass→motor0 / highs→motor1 on multi-vibe toys; surface the 6 hidden DSP knobs in an Advanced expander.
- [ ] **Flash-luminance sync**: flash/subliminal frame brightness → continuous layer (sample the already-decoded bitmap, cheap).
- [ ] **Temperament dial**: presets (Gentle/Tease/Cruel/…) = multipliers over mixer params.

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
