# SP-107 — record

Branch `lane/SP-107-gate-determinism`, base `3c1572b4`.
Floor: pin **1472 unit / 90 headless**; observed **1476 unit / 90 headless**; declared delta
**+4 unit / +0 headless** (`floor-delta.json`). 1476 = 1472 + 4 and 90 = 90 + 0, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-107-gate-determinism`.
Two skips, both pre-existing (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); **none added, and
`allowedSkips` was never opened**. Build: 0 errors, 0 warnings.
`client/tests/floor/floor.json` was never opened.

---

## 0. THE HEADLINE

> **The gate's verdict was a function of how many lanes were gating, not of the tree.** Every
> real-desktop fixture in `CcpClient.Tests` addressed machine-global state by CONSTANT — one
> evidence-file path, one image colour, one spawn seed, and rectangles and hit-test points derived
> from the screen size, which is the same number in every process on the machine. The port's own
> gate wrapper permits three concurrent runs (`client/tools/gate/with-slot.mjs --slots 3`).

Measured on ONE unchanged tree, at base `3c1572b4`, before any edit:

| Experiment | Concurrency | Runs | Red | Rate |
|---|---|---|---|---|
| A (before) | 1 floor run at a time | 20 | **0** | 0 % |
| B (before) | 4 waves x 3 concurrent | 12 | **8** | **67 %** |

Experiment A is what rules the intra-process explanation OUT. Experiment B is the flake, on
demand, with its cause in the failure text.

## 1. THE CAUSE, NAMED, FROM THE FAILURE TEXT

Three distinct collisions, every one of them a shared name rather than a shared timing.

**C1 — the shared evidence FILE.** `FlashDrawObservations.EvidenceFolder` is the fixed path
`%TEMP%/ccp-sp100-flash-draws`, and both flash observation runs write fixed file names into it.

```
System.IO.IOException : The process cannot access the file
'...\ccp-sp100-flash-draws\desktop-with-a-real-flash.bmp' because it is being used by another process.
```

Thrown inside a `Lazy` factory, so the cached exception then failed FOUR tests of the class at once
(BEFORE-PAR w1r2). This was the single largest contributor.

**C2 — the shared desktop COLOUR.** `FlashEndToEndObservations` counts pixels of one constant
colour over the WHOLE desktop and asserts the count is exactly 0 before the flash and exactly 0
after it is hidden:

```
CcpClient.Tests.FlashDrawTests.ARealImageFile_ReachesTheCompositedDesktop_AndLeavesItWhenTheFlashIsHidden
Assert.Equal() Failure: Values differ   Expected: 0   Actual: 676161
```

**676161 is exactly 951 x 711** — the pixel area of one whole flash, which is the bounding box this
lane measured independently out of the run's own evidence bitmap
(`%TEMP%/ccp-sp100-flash-draws/desktop-with-a-real-flash.bmp`, physical bbox (356,305)-(1306,1015)).
That is not an inference: the number is another process's entire flash, of the same colour, at the
same seeded place, counted as ours. The same fact also failed the other way round
(`after the flash was hidden the desktop still carries 676161 pixels ... the picture outlived the
effect that put it there`) — the foreign flash was still up when this run looked.

**C3 — the shared POINT and the shared topmost band.** With two identical surfaces at identical
rectangles, each raising `HWND_TOPMOST` in a loop, `Win32OverlayPresence.Present` refused, and every
downstream fact went with it:

```
the OS rendered 0 of 43200 pixels as the frame (interactive desktop = True). Backend said:
Unavailable(overlay-nothing-presented: ...)
the differential did not close: capture live = True, the desktop showed the flash = False,
and after Withdraw it reads 0x10070B
Expected: "overlay-frame-size-mismatch"   Actual: "overlay-nothing-presented"
```

This is the same shape SP-106 §6.2 recorded for the whole `OverlayCapabilityTests` fixture: the
product's `Confirm` walks the OS's own z-order and asks `WindowFromPoint` at the surface's centre,
and a second process's window at the same coordinates beats it.

**Why the intra-process channel is real but was NOT the cause.** The three real-desktop fixtures are
xunit-parallel today (no `xunit.runner.json`, no `[assembly: CollectionBehavior]`), and pulling each
class's `[min startTime, max endTime]` out of seven consecutive TRX files shows they overlap
nondeterministically — all three overlapped in `ccp-floor-OzBw69` and `ccp-floor-4SIfLo`, none did in
`ccp-floor-5weJZJ` and `ccp-floor-YF6PeX`. That is a genuine hazard with exactly the right shape,
and experiment A's 0-in-20 says it is not what was firing. It is closed anyway (§2), as a hazard,
not as the diagnosis.

## 2. THE FIX

Both halves are membership in one place; neither is a retry, a skip, or a weakened assertion.

**`RealDesktopCollection`** (`client/tests/CcpClient.Tests/RealDesktopCollection.cs`) —
`OverlayCapabilityTests`, `FlashDrawTests` and `TrayCapabilityTests` now carry
`[Collection(nameof(RealDesktopCollection))]`. xunit's intra-collection sequentiality serializes
them in-process, which is the same mechanism (and the same honesty about `DisableParallelization`
being a non-relied-upon hint) that `ProcessEnvCollection` has used since SP-062
(`DataRootOverrideTests.cs:116-122`).

**`RealDesktopLease`** — the collection's `ICollectionFixture`, so it is taken before the
collection's first test and released after its last. It is an exclusive `FileShare.None` handle on
`%TEMP%/ccp-real-desktop.lease`. A file handle and not a named `Mutex` because a `Mutex` has thread
affinity and xunit may construct and dispose a fixture on different threads; and because the OS
closes a handle when a process dies, so a crashed run cannot wedge the lease the way `with-slot.mjs`'s
lock-file-existence scheme needs a reaper for. The wait is `TestWait.UntilSync` with
`TestWait.InjectedBudget` — the suite's ONE approved bounded wait — and when it expires the
collection FAILS, loudly, naming the other process. It never skips and never retries.

**`RealDesktopCollectionGuardTests`** makes the convention mechanical rather than textual: a test
class that mentions a real-desktop helper, or calls `CreateWindowExW`/`Shell_NotifyIconW`/
`TrackPopupMenu`/`GetDC(0)`, must carry the attribute. Proven to bite: removing the attribute from
`OverlayCapabilityTests` reds the guard with
`CcpClient.Tests/OverlayCapabilityTests.cs: declares tests and reaches the real desktop
[OverlayWindowProbe; OverlayObservations] but does not carry [Collection(nameof(RealDesktopCollection))]`.
The one exemption — a `HWND_MESSAGE` window is never on the desktop — is pinned by file name so it
cannot be taken silently.

**`check-floor.mjs` names the failures.** It now reads the TRX on red and prints every failed test
with its message. It gained nothing else, and it must never gain a retry: re-running until green is
how an intermittent gets laundered into a pass. SP-106's run 7 had six failures and a six-line
stdout tail, which is why that flake had to be reconstructed by hand.

**An empty screen read no longer reads as an absent flash.** `CountOf([])` is 0 and so is "the flash
was not there". `FlashEndToEndObservations` now records how many pixels the screen read actually
returned and the failure text says so. Diagnostic only — nothing is asserted about it, nothing is
silenced by it.

## 3. THE NUMBERS, BEFORE AND AFTER

Same machine, same harness, every launched run counted, nothing re-run. After the fix the floor
reports `total drift: 1476 (pin 1472)` on every run by design (the declared delta the orchestrator
applies at land), so an AFTER run counts as green only when the TRX carries **zero** failed tests in
both projects AND the drift is check-floor's only complaint.

| | Concurrency | Runs | Red | Rate |
|---|---|---|---|---|
| **BEFORE** | sequential | 20 | 0 | 0 % |
| **BEFORE** | 3 concurrent | 12 | **8** | **67 %** |
| **AFTER** | 3 concurrent, round 1 | 18 | 2 | 11 % |
| **AFTER** | 3 concurrent, round 2 | 18 | **0** | 0 % |
| **AFTER** | 3 concurrent, both rounds | **36** | **2** | **5.6 %** |
| **AFTER** | sequential | 20 | SEQ_RED_PLACEHOLDER | SEQ_RATE_PLACEHOLDER |

The three collisions of §1 are gone: not one of the 36 concurrent AFTER runs produced an
`IOException` on the evidence file, a foreign flash's pixel count, or an
`overlay-nothing-presented` cascade.

## 4. THE RESIDUE, AND WHAT IS NOT KNOWN ABOUT IT

Two of the 36 concurrent AFTER runs failed, both on the same fact and both with a signature that is
NOT any of the three collisions:

```
a desktop capture can see a painted layered window on this machine = True, and while the flash was
up the composited desktop carried 0 pixels of the image's colour against an expected area of at
least 112614
```

What is known: `SurfacesShown` was 1, and `OverlaySurfaceSet.Place` only increments it after BOTH
`Present` and `Paint` returned `Available` — so the operating system had confirmed the window
visible, topmost, click-through, holding the requested alpha, and holding the painted frame in its
own device context. `DesktopPixelsBefore` and `DesktopPixelsAfterHide` were both 0 and passed. So a
surface the OS confirmed was on screen produced zero pixels of its colour in a full-screen
CAPTUREBLT read taken with no wait, on a machine running three test processes at once.

**The mechanism is NOT established.** Two candidates were not separated:

1. the full-screen DIB allocation (2880x1800x32 = ~20 MB) failing under three-way memory pressure,
   in which case `CaptureDesktop` returns an empty array and `CountOf` reports 0 — indistinguishable
   in the old failure text from an absent flash;
2. DWM not having composited the new layered window when the screen was read. SP-100 measured that
   an immediate CAPTUREBLT already carries the painted pixel (SP-100 record §1), but that
   measurement was taken on an idle machine.

Rather than guess, the instrument was taught to tell them apart: the failure now reports how many
pixels the read RETURNED. If it recurs, the next occurrence names its own cause instead of being
re-diagnosed from scratch. **No wait was added and no assertion was relaxed to make this go away** —
either of those would be the barred move, and the honest state is a named open question with an
instrument pointed at it. It did not recur in the 18-run round that followed the diagnostic, which
is not evidence that it is fixed.

## 5. WHAT THE FLOOR NOW CLAIMS, AND WHAT IT ADMITS

Written into `client/docs/verification-harness.md`.

**Claims:** no two real-desktop fixtures contend inside a process, and no two `check-floor.mjs`
runs contend across processes.

**Admits, and cannot cover:** a FOREIGN topmost window can still own a point while these facts run —
the shipping WPF product re-asserting `HWND_TOPMOST` on a cadence
(`Services/Flash/FlashService.cs:206-243`, and empirically the window that won the point while
SP-099 was being written), a locked workstation, a UAC secure desktop, a full-screen exclusive
application, Magnifier, a mirror driver, RDP. When that happens these facts fail loudly and name the
winning window's class, which is the right outcome: a red that can be read beats a green that cannot
be trusted. **None of those are `allowedSkips` candidates** — that list is for properties of the
MACHINE, and "something else was on top just then" is a property of the MOMENT. Sustained topmost
under real contention, multi-monitor placement, cross-DPI behaviour and delivered (rather than
routed) input stay tier-2 headed claims and the named manual gates in the SP-093/SP-099/SP-100
records.

## 6. What this work does NOT prove

- **It does not prove the residue of §4 is gone.** 0 red in the 18 runs after the diagnostic landed
  is one sample of a fault that showed at 2-in-18; the honest reading is "not observed again", not
  "fixed".
- **It proves nothing about a machine with more than three concurrent gate runs.** The lease is
  correct for any number, but only 3 were measured, because 3 is what `with-slot.mjs` permits.
- **It proves nothing on Linux.** The lease's exclusivity was exercised on Windows only. Every
  fixture it guards is Windows-only by construction (they assert refusals on Linux), so the lease is
  inert there, but `FileShare.None` semantics under .NET on Unix were not measured by this lane.
- **No rendering, interaction, focus, animation or headed claim is discharged here.** Nothing in
  this packet is a `presentation-verified` result; the tier-2 headed capture is untouched and
  undischarged by anything above.
- **It does not prove any earlier land was wrong.** Every prior three-green claim was true as
  observed. What was weaker than it looked was the EVIDENCE, and only for the runs that happened to
  overlap another lane's gate.

## 7. Files changed

| File | Why |
|---|---|
| `client/tests/CcpClient.Tests/RealDesktopCollection.cs` | NEW. The collection and the machine-wide lease. |
| `client/tests/CcpClient.Tests/RealDesktopLeaseTests.cs` | NEW. The lease is held while the facts run; it excludes and it releases. |
| `client/tests/CcpClient.Tests/RealDesktopCollectionGuardTests.cs` | NEW. Membership made mechanical, plus a closed helper census. |
| `client/tests/CcpClient.Tests/OverlayCapabilityTests.cs` | `[Collection(nameof(RealDesktopCollection))]`. |
| `client/tests/CcpClient.Tests/FlashDrawTests.cs` | Same, plus the sample-size diagnostic in the composited-desktop failure text. |
| `client/tests/CcpClient.Tests/TrayCapabilityTests.cs` | Same attribute. |
| `client/tests/CcpClient.Tests/FlashEndToEndObservations.cs` | Records how many pixels the screen read returned. |
| `client/tests/floor/check-floor.mjs` | Names the failing tests from the TRX on red. No retry, ever. |
| `client/docs/verification-harness.md` | What tier 1 now covers on the real desktop, and what it admits it does not. |
| `spine-tasks/SP-107-gate-determinism/floor-delta.json` | +4 unit / +0 headless. |
| `spine-tasks/SP-107-gate-determinism/plan.md` | The plan checkpoint, written before the first test edit. |

## 8. Three things a reviewer should check first

1. **That nothing retries.** `check-floor.mjs` gained one reporting function and no control flow;
   the lease waits for a lock, which is not a re-run of a failed assertion. Grep the diff for
   `retry`, for a second `runProject`, for a loop around a verdict.
2. **That no assertion moved.** The OS-level facts in `OverlayCapabilityTests`, `FlashDrawTests` and
   `TrayCapabilityTests` are byte-identical except for one attribute line and one enriched failure
   MESSAGE. `git diff 3c1572b4 -- client/tests/CcpClient.Tests/OverlayCapabilityTests.cs` should show
   exactly one added line.
3. **That `allowedSkips` and `floor.json` were never touched.** `git diff 3c1572b4 --
   client/tests/floor/floor.json` must be empty; the delta is declared in this packet's folder.
