# SP-107 — record

Branch `lane/SP-107-gate-determinism`, base `3c1572b4`.
Floor: pin **1472 unit / 90 headless**; observed **1477 unit / 90 headless**; declared delta
**+5 unit / +0 headless** (`floor-delta.json`). 1477 = 1472 + 5 and 90 = 90 + 0, confirmed by
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
collection's first test and released after its last. It is an exclusive `FileShare.Read` handle on
`%TEMP%/ccp-real-desktop.lease`. A file handle and not a named `Mutex` because a `Mutex` has thread
affinity and xunit may construct and dispose a fixture on different threads; and because the OS
closes a handle when a process dies, so a crashed run cannot wedge the lease the way `with-slot.mjs`'s
lock-file-existence scheme needs a reaper for. The wait is `TestWait.UntilSync` with
`TestWait.InjectedBudget` — the suite's ONE approved bounded wait — and when it expires the
collection FAILS, loudly. It never skips and never retries.

**The share mode is `FileShare.Read` and NOT `FileShare.None`, and that was a review finding.** The
first draft used `FileShare.None`, wrote nothing into the file, and then had three prose sites and a
failure message claiming the failure "names the holder" — while the message actually interpolated
`Environment.ProcessId`, which is the CONTENDER's own pid, beside an assertion that a peer process
existed. Under `FileShare.None` the file is unreadable while held, so that claim was not merely
unsupported, it was unsupportable. It is now true: the holder opens for WRITE and writes `pid=N`,
a contender's write-open is refused because write sharing is not granted, and a contender's
read-open still succeeds, so `RealDesktopLease.HolderProcessId` returns the real holder. Two facts
assert it (`RealDesktopLeaseTests`), one from inside the collection and one on a private temp path.
The same draft mapped `UnauthorizedAccessException` to "held", so an ACL, a read-only volume or a
file-locking scanner would have produced a 60-second wait and then blamed a peer that did not exist;
that branch now reports itself as what it is and says explicitly that no peer should be hunted.

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

| | Concurrency | Window (UTC) | Runs | Red | Rate |
|---|---|---|---|---|---|
| **BEFORE** | sequential | 21:12–21:36 | 20 | 0 | 0 % |
| **BEFORE** | 3 concurrent | 21:36–21:55 | 12 | **8** | **67 %** |
| **AFTER** | 3 concurrent, round 1 | 21:56–22:04 | 18 | 2 | 11 % |
| **AFTER** | 3 concurrent, round 2 | 22:09–22:17 | 18 | 0 | 0 % |
| **AFTER** | sequential, round 1 | 22:17–22:42 | 20 | 2 | 10 % |
| **AFTER** | sequential, round 2 | 22:42–23:09 | 20 | 0 | 0 % |
| **AFTER, all** | both | 21:56–23:09 | **76** | **4** | **5.3 %** |
| **AFTER, 3 concurrent only** | | | **36** | **2** | **5.6 %** |

**The per-run verdict list for every one of these runs is committed** under
`spine-tasks/SP-107-gate-determinism/evidence/`, one line per launched run, so the table above is
checkable rather than merely asserted. Nothing was re-run and no run is missing from those files.

**The flake this packet was written about is gone.** At the concurrency that produced it, 8 red in
12 became 2 red in 36, and not one of the 36 concurrent AFTER runs produced an `IOException` on the
evidence file, a foreign flash's pixel count, or an `overlay-nothing-presented` cascade. The three
collisions of §1 do not appear again anywhere in 76 post-fix runs.

**What is left is a different fault, and it is NOT concurrency-driven** — §4. All four post-fix reds
are the same single fact, and they occur at the same rate with three concurrent runs (2/36) as with
one (2/40).

## 4. THE RESIDUE: A SECOND, INDEPENDENT FAULT, MEASURED AND NAMED BUT NOT FIXED HERE

All four post-fix reds are the same fact,
`FlashDrawTests.ARealImageFile_ReachesTheCompositedDesktop_AndLeavesItWhenTheFlashIsHidden`, with a
signature that is NOT any of the three collisions:

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

**One candidate was measured and REFUTED.** The first guess was that the full-screen DIB allocation
(2880x1800x32 = ~20 MB) fails under memory pressure, in which case `CaptureDesktop` returns an empty
array and `CountOf([])` is 0 — indistinguishable from an absent flash. So the instrument was taught
to report how many pixels the read actually returned, and the next occurrence answered:

```
The screen read RETURNED 5184000 pixels (SP-107)
```

5,184,000 is exactly 2880 x 1800: **a complete desktop came back, and none of it was the flash's
colour.** The allocation hypothesis is dead.

**A SECOND candidate, found in review, and it is the stronger one.** Reconciling the printed
`expected area of at least 112614` against this host's geometry (see `plan.md` §1, corrected) shows
the frame is **548x411**, so the virtual desktop is about **1646x1029** — the 2880x1800 panel at
**175 %**, not the 150 % this lane first assumed. That correction exposes a desynchronisation the
first diagnosis never considered:

> `display` is read ONCE from `OverlayWindowProbe.PrimarySize` at the top of `Measure()`, and every
> rectangle in the run is derived from it. `FlashPixelProbe.CaptureDesktop` re-reads
> `HorizontalResolutions`/`VerticalResolutions` through `GetDC(0)`/`GetDeviceCaps` on EVERY call. If
> the desktop's scale or resolution changes between those two reads, the capture maps the requested
> rectangle through a different ratio than the placement used and samples a region the flash was
> never in.

That produces exactly the observed signature — a complete desktop returned, none of it the flash's
colour — and it is bursty in the way the rate table is. Critically, **the 5184000 figure cannot
discriminate here**: it is a count of physical pixels and is therefore scale-invariant.

**What is left, and is NOT established.** Three live candidates now, not one: the scale/resolution
desynchronisation above; DWM not having composited the layered window when the screen was read (SP-100
measured that an immediate CAPTUREBLT already carries the painted pixel, SP-100 record §1 — on an
idle machine, and nothing contracts it); and a blank or asleep display, which a second diagnostic
already separates by reporting how many returned pixels are one colour.

**So the instrument now records the display metrics at BOTH reads** — `PlacementScreen`,
`PlacementHorizontal`, `PlacementVertical` taken with the placement, and `CaptureHorizontalDuring`,
`CaptureVerticalDuring` taken by the capture itself — and the failure text states whether they
agree. The next occurrence discriminates between four verdicts instead of speculating between two.
This matters beyond bookkeeping: the board options below were, in the first draft of this record,
both predicated on the DWM hypothesis, and one of them would have bought a bounded wait against
SP-100's measured no-wait fact **to fix what may be a geometry desynchronisation instead**.

**Two facts about the rate that a reviewer must weigh, because they point opposite ways.**

- It is NOT concurrency-driven: 2 red in 36 runs at three-way concurrency, 2 red in 40 sequential.
- It did NOT appear in the 20-run pre-fix sequential baseline. **So this lane cannot rule out that
  its own change raised the fault's visibility** — the three real-desktop fixtures now run
  back-to-back in one collection instead of being spread across the run, which changes what the
  compositor is doing in the milliseconds before the screen read. The counter-evidence is timing:
  all four reds fall in 21:56–22:42 UTC while 32 pre-fix runs before that window and 20 post-fix
  runs after it were clean, in both cases with the fixture ordering unchanged within each group.
  That is bursty, which fits an environment event better than a code change, and it is not proof.

**What was deliberately NOT done.** No wait was added around the composited read, and no assertion
was relaxed. Polling until the desktop shows the flash would make the fact "within N seconds the
flash arrived" instead of SP-100's measured "immediately", and waiting until an assertion passes is
retry-until-green wearing a different hat. Both are barred by this packet and both would have made
the numbers above look better. The honest state is a named open question with an instrument pointed
at it, and a decision for the board **that should not be taken until the next occurrence reports
which of the four verdicts fired**: if the metrics moved, the fix is to read the display once and
map every capture through THAT reading (a geometry fix, no wait, no divergence); only if the metrics
held still does the question of SP-100's no-wait fact arise at all, and then the choice is a bounded
`TestWait` as a deliberate recorded divergence or moving the composited-pixel fact behind the tier-2
headed gate. This lane had no mandate to choose, and now has a reason not to guess.

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

- **It does not fix the residue of §4, and does not prove it is unrelated to this change.** 4 red in
  76 post-fix runs on one fact, against 0 red in 20 pre-fix sequential runs. The evidence that it is
  environmental is a timing burst, not a mechanism.
- **The gate is therefore better, not deterministic.** A green floor run now means "no two test
  processes fought over the desktop", which it did not mean before. It does not yet mean "green
  implies the tree is green" unconditionally, because §4 is still open. Three consecutive greens are
  worth much more than they were and are still not proof.
- **It proves nothing about a machine with more than three concurrent gate runs.** The lease is
  correct for any number, but only 3 were measured, because 3 is what `with-slot.mjs` permits.
- **It proves nothing on Linux.** The lease's exclusivity was exercised on Windows only. Every
  fixture it guards is Windows-only by construction (they assert refusals on Linux), so the lease is
  inert there, but `FileShare.Read` write-exclusion semantics under .NET on Unix were not measured
  by this lane.
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
| `client/tests/CcpClient.Tests/FlashEndToEndObservations.cs` | Records how many pixels the screen read returned and how many were one colour — the three verdicts behind a count of zero. |
| `client/tests/floor/check-floor.mjs` | Names the failing tests from the TRX on red. No retry, ever. |
| `client/docs/verification-harness.md` | What tier 1 now covers on the real desktop, and what it admits it does not. |
| `client/tests/CcpClient.Tests/RealDesktopCollectionGuardTests.cs` | Also binds `CcpClient.HeadlessTests` by the stronger no-probe rule (collections do not span assemblies). |
| `spine-tasks/SP-107-gate-determinism/evidence/*.log` | The per-run verdict list behind every number in §3. |
| `spine-tasks/SP-107-gate-determinism/floor-delta.json` | +5 unit / +0 headless. |
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
