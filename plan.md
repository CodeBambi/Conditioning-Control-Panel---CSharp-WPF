# Plan — foreign-topmost pre-flight for the four OverlayDesktopInputTests drag facts

Scratch file. Deleted before the final report; the branch tip must not carry it.

## The four facts
Confirmed by reading, not by the row: the four that assert a DRAG count off
`OverlayDesktopInputObservations.PassiveChannels` are
`TheDesktopUnderThisPointTakesAllFourChannels_...`,
`AClickThroughOverlayPassesCLICKDRAGSCROLLandTYPE_NotOnlyTheClick`,
`RestoringClickThroughGivesAllFourChannelsBack_...` and
`WithdrawingTheOverlayRestoresAllFourInputChannels_NotOnlyTheHitTest`.
`ADragHoldsItsOwnPath_...` and `AHandledOverlayClickDoesNotLeakThrough_...` assert no drag count
and stay out of it.

Arithmetic that confirms the board's captured coordinate: with `GetSystemMetrics` reporting
1646x1029 (2880x1800 at 175%, this process not per-monitor aware), `UnderneathBounds` centre is
(1446,809) and drag step 1 is (1453,814) — the exact point in the TRX line. So the recurrence was
the passive-channels rig, step 1, not the held-path rig.

## The rule
Before ANY injection of a leg, walk every point the drag will put the cursor on (press point plus
the eight steps, from one shared enumerator so the walk cannot drift from the injection), calling
the leg's own `hold` per point. If the window manager still gives a point to a window owned by
ANOTHER PROCESS after `MaxRaiseAttempts` re-assertions of this run's own stack, refuse by name.
Ours-but-wrong reds as before; 0/off-Windows is not foreign.

## Both directions
One new fact, one child process of this same test executable (module-initializer mode, the
`SurfaceExitChild` precedent, so it never contends for `RealDesktopLease`):
- REFUSES: probe a path whose press point is over our own window and whose later steps cross into
  the child's topmost window. Assert the refusal names the child's exact handle and that the first
  contended index is a STEP and not the press point.
- DOES NOT REFUSE: the same walk kept inside our own window — not contended, owner ours.

## Out-of-scope edits expected (report them)
- `client/tests/floor/vacuous-shape-ledger.json`: `Assert.Skip*` in a fact body is a `dynamic-skip`
  SITE and the guard fails without a disposition.
- `client/tests/CcpClient.Tests/HarnessLeaseGuardTests.cs:24` cites `OverlayDesktopInputTests.cs:149`
  and `intra.mjs` checks the quotation beside it against the cited lines +/-1; adding a gate to
  fact 1 moves that line.
- New members go AFTER `PointerWindowProbe.cs:306`, because `OverlayTaskSwitcherTests.cs:71` cites
  `PointerWindowProbe.cs:302-306`.
