# Scoped channel stop — plan (LIVE REGRESSION: DTRH close silences the second consumer)

## The defect, traced
- `Features/Dtrh/DtrhHostWindow.axaml.cs:298` — `TeardownBarkPipeline()` calls
  `_barkArbitration?.PanicReset()`, which stops EVERY channel on the app-wide arbitration
  (`Audio/SoundArbitration.cs:713-755`).
- Consumers of that one arbitration today:
  - DTRH bark pipeline -> **Voice only** (`Companion/BarkPipeline.cs:617-618`).
  - `Effects/EffectSounds.cs` -> **Whisper** (flash, `:212`) and **Sfx** (pops, `:247`).
  - `Views/Pages/StudioPage.axaml.cs:1104` -> **Sfx** (test-audio button).
- DTRH's own whispers/sfx are the DTRH-LOCAL engine (`DtrhNativeEffects` / `SoundFlowDtrhAudio`,
  `IDtrhAudioPlayer`) — not this arbitration (`SoundArbitration.cs:104-106`). Nothing in the DTRH
  window acquires a duck either (no `AcquireDuck` outside tests), so the panic reset's ForceUnduck
  is a no-op on that path.

## "Owner" = a CHANNEL
The only ownership axis the arbitration has is `SoundChannel` (`SoundArbitration.cs:3-14`, "§6
channel ownership"): per-channel exclusive state + generation, typed outcomes keyed by channel,
and no consumer identity anywhere (every play method takes only path+gain).
Rejected: a per-play owner token/handle — a genuinely new concept, and for Voice/Whisper it would be
indistinguishable from a channel stop (both are exclusive stop-replace, so a second consumer has
already replaced the first's player). It would add resolution only inside the SFX pool, which the
DTRH window never touches.

## Changes
1. `Audio/SoundArbitration.cs`: `public void StopChannel(SoundChannel channel, string why)` over a
   shared `DetachChannels(ReadOnlySpan<SoundChannel>, out int, out bool)`.
   `PanicReset()` keeps its name/signature/log line and now detaches `Enum.GetValues<SoundChannel>()`
   — total by construction. Private `StopAllChannels` folds into the same core.
2. `Features/Dtrh/DtrhBarkRouting.Composition.cs`: `StopPipelineAudio(SoundArbitration?)` — the stop
   that mirrors `CreatePipeline`, so the window's CHANNEL CHOICE is drivable (the window itself is
   not; same reason that file exists at all).
3. `Features/Dtrh/DtrhHostWindow.axaml.cs`: teardown calls the seam; the "ceiling" paragraph becomes
   the record of the fix.

## Facts (`client/tests/CcpClient.Tests/ScopedChannelStopTests.cs`)
1. Second consumer survives: real bark pipeline (voice) + whisper + sfx on ONE arbitration ->
   `DtrhBarkRouting.StopPipelineAudio` -> voice stopped, whisper/sfx still playing, WhisperBusy true.
   **Staged red-first**: seam body = `PanicReset()` (today's code) -> must FAIL; then scoped -> pass.
2. Panic still total: `PanicReset` after the same setup stops all three (plus the landed
   `SoundArbitrationTests.PanicReset_ReleasesEverything_NoWedgedPlayers_Idempotent`).
3. Lexical: DTRH teardown does not call `PanicReset` and does call the seam (precedent:
   `AudioOwnershipGuardTests` — the window is not drivable).

Floor delta expected: unit +3, headless 0.
