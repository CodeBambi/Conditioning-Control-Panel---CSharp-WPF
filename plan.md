# Four overlay-clickthrough safety invariants — plan

Packet: close as many of (a) passive channels, (b) handled-click leak, (c) task-switcher
visibility, (d) display transitions, as the evidence allows. Test-side only; product code under
`client/src/**` is read-only for this lane.

## Census of what already exists (read, not assumed)

- `PointerWindowProbe` — `ScratchTarget` (mouse-down/up counting, `WS_EX_NOACTIVATE`),
  `InjectClickAt` (SendInput, virtual-desk normalised), `HitTestAfterRaising`,
  `Pump`/`PumpUntil`/`Drain`. Its `InputUnion` ALREADY declares `KeybdInput`, so key and wheel
  injection is additive.
- `InputWindowProbe` — `TakeForeground` (two-rung escalation), `SystemKeyboardFocus`
  (`GetGUIThreadInfo(0)`), `InjectKey`, `VkF13`, activatable/non-activatable `ScratchWindow`.
- `OverlayWindowProbe` — `HitTestExpecting(expectSurface:)`, `RaiseTopmost`, ex-style read-back.
- `VideoSurfaceObservations.RunInputRouting` — the measured-routing shape: three legs, a drained
  budget for "nothing arrived", every injection gated on the OS already saying the point is ours.
- `SurfaceTeardownObservations` — the teardown half of (d), five surfaces, already landed.
- Machine: ONE display, DISPLAY1 at 1646x1029.

## Decisions

1. ONE new run covers (a) and (b): they are each other's differential. Overlay click-through ON
   passes all four channels; click-through OFF eats the click and must not activate the window
   underneath. Legs L0 baseline / L1 through / L2 activation control / L3 handled / L4 restored /
   L5 after withdraw.
2. The keyboard and wheel channels are FOCUS-routed, not hit-test routed, so their differential is
   the foreground moving to a keeper window — which leg L3 needs anyway for (b) to be non-vacuous.
3. Extend `PointerWindowProbe.ScratchTarget` additively (counters plus an `activatable` flag)
   rather than duplicating an 80-line window class. Existing counters and defaults untouched.
4. (c): no public API exposes the Alt-Tab list. Measure the shell's DOCUMENTED task-window
   predicate over the OS's own read-backs, with an ordinary unowned non-tool window as the control
   that must answer YES. Frame the rendered switcher and taskbar as a headed claim this cannot
   discharge.
5. (d) display half: nothing in client/src subscribes to a display change (grep found only
   OverlayDisplays.EnumDisplayMonitors), one monitor on this machine, and the only in-process API
   that changes topology is ChangeDisplaySettingsEx on the interactive user's real desktop.
   Report as not reachable; strengthen the teardown half to four channels instead.

## Files

- client/tests/CcpClient.Tests/PointerWindowProbe.cs (extend)
- client/tests/CcpClient.Tests/OverlayDesktopInputObservations.cs (new)
- client/tests/CcpClient.Tests/OverlayDesktopInputTests.cs (new)
- client/tests/CcpClient.Tests/OverlayTaskSwitcherTests.cs (new)
- client/tests/CcpClient.Tests/RealDesktopCollectionGuardTests.cs (extend: register the new
  helper and bind the new control files)
