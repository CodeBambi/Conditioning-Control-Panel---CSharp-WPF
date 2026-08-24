# Plan — the one overlay safety invariant with no coverage

Invariant (overlay-clickthrough/SKILL.md:30-31): "No failure leaves an invisible input-blocking or
permanently topmost surface" / "Teardown and display/window transitions restore normal desktop input."

## Census (what already exists, so nothing is rebuilt)

- `RealDesktopCollection` + `RealDesktopLease` (machine-wide lease) + `RealDesktopFacts` base
  (arms the thread-static window floor on the fact's own thread). Membership is enforced by
  `RealDesktopCollectionGuardTests`.
- Probes: `OverlayWindowProbe`, `PointerWindowProbe`, `InputWindowProbe`, `GlyphWindowProbe`.
  None enumerates *our process's* visible top-level windows — that is the one missing reading.
- `PointerSurfaceObservations.RunCoexistence` brings four surfaces up together;
  `GlyphSurfaceObservations.RunCoexistence` brings five. Both tear down and never look back.
- `TeardownTests.cs`: `ApplicationHost.ShutdownAsync` is the single guarded teardown entry point.
  Zero mentions of topmost/overlay/input/pointer — confirmed.

## Shape

Two new files, `TeardownTests.cs` gets one `<seealso>` line.

1. `client/tests/CcpClient.Tests/SurfaceTeardownObservations.cs` — one cached real-desktop run,
   no `[Fact]`, so it is outside the guard's membership rule and carries no banned P/Invoke token
   (`CreateWindowExW(`/`GetDC(0)` are absent: it only READS the z-order).
2. `client/tests/CcpClient.Tests/SurfaceTeardownTests.cs` —
   `[Collection(nameof(RealDesktopCollection))] : RealDesktopFacts`.

## The run, one thread, no waits

- baseline = our process's visible top-level windows before anything is placed.
- bring up five: overlay, glyph, video, pointer target (NOT click-through), input card
  (takes foreground + keyboard). Disjoint rectangles.
- POSITIVE CONTROL: each handle visible to the OS; the pointer target wins its own point;
  the card holds foreground AND system keyboard focus; a fifth new window (video, no handle
  accessor) appears in the set difference.
- PHASE B, the leak: `ApplicationHost.ShutdownAsync()` over a participant holding four of the
  five — the pointer surface is deliberately withheld. Read the OS: a survivor exists, it is
  still topmost, and it still eats its own point. This is the negative control that proves the
  instrument can say NO.
- PHASE C, the restore: a second host whose participant holds the pointer surface. Read the OS:
  the set difference is EMPTY, every handle is gone, every surface point's owner is another
  process, and the foreground is not ours.

## Why `ShutdownAsync` is safe to read synchronously

`OperationRegistry.CancelAndDrainAsync` (`Lifecycle/OperationRegistry.cs:93-97`) awaits nothing
when the registry is empty, and the participants return completed tasks, so `ShutdownAsync` runs
inline on the caller's thread. That is asserted (`ShutdownCompletedSynchronously`,
`StopRanOnTheCreatingThread`) rather than assumed — a native window belongs to the thread that
made it, and a harness that disposed from a pool thread would red for its own reason.

Disposal order copies `Session/SessionParticipant.cs:884-940` (drawing surfaces, video, pointer,
input). No `.GetAwaiter().GetResult()` (pinned-exemption token in `UnboundedWaitGuardTests`),
no sleeps, no polls.

## Out

Tray (`Win32TrayPresence`): its owner window is never visible, so it is not an input-blocking or
topmost surface, and the file is live under another lane. Named, not covered.

## floor delta

unit +N (report exactly), headless 0. `floor.json` is never opened.
