# Scripted session, slice 2: the ramp and the delayed features

Read first: `docs/constitution.md`, `client/docs/port-workflow.md`, the P1 board row, and all four
slice-1 sources under `client/src/CcpClient.Desktop/Session/`.

## What is being ported

1. `UpdateRampingValues` — upstream `Services/Session/SessionEngine.cs:564-661`.
2. `CheckDelayedFeatures` — upstream `:663-772`.
3. `RandomizeStartTimes` — upstream `:777-805`. **Ported**, with the randomness injected the way
   the clock is: an optional `Random` constructor parameter, which is this repo's own established
   seam (`Effects/AudioCueEffect.cs:86`, `Effects/BubbleCountEffect.cs:168`). A fact pins it to an
   exact minute with a `Random` whose sample is fixed.

## Design decisions, with their evidence

- **The ephemeral ramp.** Upstream parks the flash trio on `AppSettings` through
  `SetSessionFlashRamp` (`Models/AppSettings.cs:908-916`) — never persisted, silent, read through
  getters that prefer the overlay — and drives the pink/spiral overlay DIRECTLY
  (`SessionEngine.cs:604-618`, `:620-633`) rather than writing the ramped value into the user's
  persisted setting, because writing it auto-saved and froze the ramp maximum into settings.json
  after an app kill (#471, #476). The port has ONE ephemeral value, `ScriptedSessionRun.Ramp`,
  carrying the flash trio and both overlay opacities. No module reads it yet: the port has no
  sustained-overlay hold (`Effects/PinkFilterEffect.cs` records `OverlayService.cs:900-965` as
  unported) and its modules read their dials when they ARM. Named at the call site.
- **Delayed starts** use the port's existing pair-of-acts: the dial write plus
  `SessionEngine.QuickToggle`, which flips the persisted flag and arms the module live when the
  engine is running — upstream's `App.Settings.Current.X = true; _mainWindow.EnableX(true)`
  (`:692-693`).
- **The port writes the ramp's FIRST SAMPLE into the opacity dial at a delayed start** (pink and
  spiral). Upstream writes nothing for a ramping feature because its overlay hold supplies the
  value; without that hold the port's module would arm at the USER's opacity, which is neither the
  session's start nor its ramp. For a non-ramping spiral this is upstream's own write (`:721-722`).
- **The curve** is `settings.RampCurve ?? global` (`:569`). The port's global is
  `IntensityRampPresetDocument.Curve` — upstream's single `AppSettings.RampCurve` is read by both
  ramp systems (`Features/IntensityRampFeatureControl.xaml:84`), which is the second caller
  `Effects/RampCurves.cs:54` predicts. The run takes that store, reads it per tick, never captures
  or restores it.
- **Not ported, each with a reason:** the `_pendingFeatureStarts` timeline queue (`:668-685`) — the
  model has no timeline events and the editor that writes them is unported; the spiral file probe
  (`:702-715`) — `SpiralLibrary` and the module's own arm refusal already answer it, recorded
  verbatim in `ArmOutcomes`; brain drain (`:652-660`, `:762-771`) — commented out upstream; the
  corner GIF and intermittent bubbles — out of this slice by the packet.

## Source-vs-brief discrepancy found

Slice 1's prose calls `flashScale` "read by nobody at runtime"
(`Session/ScriptedSession.cs`, `Session/ScriptedSessionDials.cs`). `SessionEngine.cs:596-599` reads
it and pushes it through `SetSessionFlashRamp`. The prose is corrected in this slice.
`flashSmallSize` really is written-only (grep: assignments only).

## Files

- `client/src/CcpClient.Desktop/Session/ScriptedSessionRamp.cs` (new): the arithmetic, pure.
- `client/src/CcpClient.Desktop/Session/ScriptedSessionRun.cs`: the two tick methods, the jitter,
  the ephemeral ramp, the clear at stop.
- `client/src/CcpClient.Desktop/Session/ScriptedSessionDials.cs`: the delayed starts' dial half.
- `client/src/CcpClient.Desktop/Session/ScriptedSession.cs`: `RampCurve` member + prose correction.
- `client/tests/CcpClient.Tests/ScriptedSessionTests.cs`: the facts, on slice 1's rig extended with
  the three real modules a delayed start turns on.
