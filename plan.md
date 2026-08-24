# Plan — lift the audio seams out of the DTRH window and make them app-wide

Board row: "The audio dials are NOT 'dials over landed seams'". Branch `worktree-agent-a1487ad583466f1a6`.

## What the source says (read before planning)

- `Audio/SoundArbitration.cs:88-108` already calls itself "the APP-WIDE sound arbitration core"; its
  only construction site is `Features/Dtrh/DtrhHostWindow.axaml.cs:232`
  (`DtrhBarkRouting.CreateArbitration`), and `:211-213` says the app-wide lift is a future row.
- `Audio/SoundArbitration.cs:583` `PlaySfx` is ALREADY the overlapping one-shot path: bounded pool
  (`MaxSfxVoices` default 8), drop-on-overflow, reclaim on the real `PlaybackEnded`. Upstream's
  `PlayOneShot` is the same shape with cap 10 (`Services/Audio/AudioService.Playback.cs:111`,
  enforced `:212`). So #96 needs no NEW mechanism — it needs this one to be REACHABLE.
- `Companion/BarkPipeline.cs:262 MasterVolume` is settable and drives the gain law at `:613`
  (`Pow(clamp(master,0,100)/100, 1.5) * BarkVolumeScale`), upstream `AudioService.cs:643`.
  Nothing sets it, because there is no persisted document to set it from.
- No app-wide audio document exists. WPF has three settings this row names:
  `MasterVolume` (default 32, clamp 0..100, `Models/AppSettings.cs:1128-1132`),
  `VideoVolume` (default 50, clamp 0..100, `:1135-1139`),
  `AudioOutputDeviceId`/`AudioOutputDeviceName` (`:1241-1255`, empty = system default).

## Shape

1. `Audio/AudioSettingsDocument.cs` — new persisted document, `audio.json`, three fields + schema
   version + `[JsonExtensionData]`. **Lives in `Audio/`, not `Session/`**: the twelve `session_*.json`
   documents are exactly the set a scripted run borrows (`Session/ScriptedSessionDials.cs:51-61`
   takes eleven of them); an app-wide output-device choice is not a session dial. Precedent for an
   app-lifetime document beside its owner: `Motion/MotionSettings.cs` (`motion.json`),
   `Haptics/HapticSettingsDocument.cs`, and `Effects/PopQuizPresetDocument.cs`'s own note.
2. `Audio/AudioParticipant.cs` — the app-lifetime owner (`IBackgroundParticipant`, the shape
   `Haptics/HapticParticipant.cs` set): owns the settings store AND the one `SoundArbitration`.
   Phase 3 loads the document and opens NO device — same discipline as the haptic participant's
   ungated-probe refusal (`HapticParticipant.StartAsync`): a device opened on every launch for a
   user who plays nothing is a real endpoint grab, and the F1 crash class lives in device init.
   `EnsureDevice()` brings it up once with the persisted name; `SelectOutputDevice(name)` persists
   then `SetPreferredDevice`; `Of(host)` resolves it from the participant list (the
   `HostedMotion.StoreOf` precedent).
3. `Lifecycle/CompositionRoot.cs` — construct + register it, and flush its document in the reserved
   pre-drain slot (a volume moved on the way out is a setting like any other).
4. `Features/Dtrh/DtrhHostWindow.axaml.cs` — CONSUMES: no backend construction, no arbitration
   ownership, `PanicReset()` instead of `Dispose()` at close (same user-observable outcome — the
   barks stop — without killing the app's device).
5. `Features/Dtrh/DtrhBarkRouting.Composition.cs` — `CreatePipeline` takes the master volume so the
   document→pipeline wire is drivable (optional parameter; existing call sites unaffected).

The BARK PIPELINE STAYS WINDOW-SCOPED. Its document is `<DtrhDataDirectory>/companion.json`, its
rules are DTRH events (`DtrhBarkRouting.cs` routing table), and its only consumer is the DTRH
window's `BarkSurfaced`. What had to move is the shared DEVICE, not the content pipeline.

## Facts (unit, `client/tests/CcpClient.Tests/`)

Every one mutation-checked; literals pinned, never the product constant.

- document round-trips through a real store on disk (32/50/"" defaults; clamp 0..100).
- participant start opens NO device (`DeviceInitAttempts == 0`), and the settings still loaded.
- `EnsureDevice` uses the PERSISTED device name (recording fake backend records the request).
- `SelectOutputDevice` persists AND re-probes.
- reachability: `AudioParticipant.Of(host)` over the PRODUCT participant list from
  `CompositionRoot.Build` — reached with no DTRH window anywhere.
- overlap: `PlaySfx` through the app-wide owner overlaps N and drops the N+1th.

## Not in scope, stated so the next row inherits it

No dials, no UI, no effect wiring (#115/#96 own those). No device-level claim: a fake backend
proves the wiring, never that Windows opened an endpoint.
