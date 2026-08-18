# SP-107 — plan checkpoint (written BEFORE the first edit to any test file)

Branch `lane/SP-107-gate-determinism`, base `3c1572b4`. Worktree
`.claude/worktrees/agent-a7b15ebe18a1d337b`.

This file is written at the plan checkpoint and is updated once with the measured numbers before
any product/test edit is made. Nothing under `client/tests/**` has been edited at the time the
first half of this file was written.

## 1. The census: what in this suite touches the REAL desktop

Grepped `client/tests/**` for `CreateWindowExW|Shell_NotifyIcon|TrackPopupMenu|GetDesktopWindow|
BitBlt|WindowFromPoint|SetWindowPos`. Three fixtures put windows on the user's screen or read the
user's screen; one more match is a false positive.

| Fixture | What it puts on the real desktop | Where (virtual px, this 1920x1200 host) |
|---|---|---|
| `OverlayCapabilityTests` -> `OverlayObservations.Lifecycle` | one layered topmost 240x180 surface, alpha 153, plus a hit test at its centre | rect 840..1080 x 510..690, point **(960, 600)** |
| `OverlayCapabilityTests` -> `OverlayWindowProbe.RunNegativeControl` | three scratch top-level windows (catcher / click-through / ghost) and a hit test | rect 590..810 x 520..680, point **(700, 600)** |
| `FlashDrawTests` -> `FlashDrawObservations.Run` | one painted layered topmost 240x180 subject + one painted control window, and a `GetDC(0)` CAPTUREBLT screen read | subject 720..960 x 640..820; control 1020..1260 x 300..480 |
| `FlashDrawTests` -> `FlashEndToEndObservations.Measured` | a REAL 640x480 flash image (colour `0x1E7FD2`) at a seeded-random point, and three **whole-display** CAPTUREBLT screen reads that count pixels of that colour | measured from the run's own evidence bitmap: physical bbox (356,305)-(1306,1015) => **virtual 237..877 x 203..683** |
| `TrayCapabilityTests` -> `TrayObservations` | a hidden owner window + a real notification-area icon; `TrackPopupMenu`'s modal loop is a SEAM and is not entered | notification area only |
| `AiAwarenessTests` | `CreateWindowExW` with `HWND_MESSAGE` parent — message-only, never visible, never hit-tested | not on the desktop |

**Note the collision that is already in the tree:** the flash image's rectangle
(237..877 x 203..683) **contains the negative control's hit-test point (700, 600)**. Inside one
process that is harmless only because the two fixtures happen not to overlap in time.

## 2. Measured fact #1 — the three real-desktop fixtures are xunit-parallel, and whether they
overlap in wall-clock time varies run to run

`CcpClient.Tests` has no `xunit.runner.json` and no `[assembly: CollectionBehavior]`, so xunit v3's
default applies: one collection per class, collections run in parallel. Extracting each fixture
class's `[min startTime, max endTime]` from the preserved TRX of seven consecutive floor runs:

```
ccp-floor-JX3Cje: Flash 1.153..3.018 | Overlay 0..0.39   | Tray 1.945..2.385  OVERLAP=[Flash~Tray]
ccp-floor-OzBw69: Overlay 0.524..0.789 | Tray 0.784..1.15 | Flash 0..1.765    OVERLAP=[all three]
ccp-floor-5weJZJ: Overlay 3.287..3.495 | Tray 2.354..2.759 | Flash 0..1.911   OVERLAP=[]
ccp-floor-4SIfLo: Flash 0.105..1.92  | Tray 0.007..0.44  | Overlay 0..0.246   OVERLAP=[all three]
ccp-floor-y5UJ2O: Tray 3.168..3.535 | Overlay 0.018..0.303 | Flash 0..1.913   OVERLAP=[Overlay~Flash]
ccp-floor-YF6PeX: Tray 0.275..0.637 | Overlay 0..0.274  | Flash 2.051..3.588  OVERLAP=[]
ccp-floor-O0Ha7u: Flash 0.304..2.025 | Overlay 0..0.235 | Tray 1.338..1.721   OVERLAP=[Flash~Tray]
```

So the suite already has a nondeterministic scheduling variable of exactly the shape a 1-in-5 flake
needs. Whether it is SUFFICIENT to produce the reds is the thing experiment A settles.

## 3. The two candidate channels, and the experiment that separates them

| Channel | Mechanism | How it would produce the two observed reds |
|---|---|---|
| **A — intra-process** | xunit runs `OverlayCapabilityTests` and `FlashDrawTests` concurrently; the flash image covers the negative control's point (700,600); `Win32OverlayPresence.ConfirmInputRouting` **temporarily clears click-through** on a surface to prove it wins its own centre, so during that window a flash IS hit-testable and can steal another fixture's point | `TheHitTestOracle` red; every `Lifecycle` fact downstream of `Present` red |
| **B — cross-process** | `client/tools/gate/with-slot.mjs --slots 3` explicitly allows **three concurrent floor runs**. Every rectangle above is derived from the SCREEN SIZE, and the flash's placement seed is the constant `new Random(1000)`, so two concurrent runs put their windows at **identical coordinates** and count **the same colour** over the whole desktop | run 1: another run's flash of colour `0x1E7FD2` is on screen during our `DesktopPixelsBefore`/`AfterHide` capture (both asserted `== 0`); run 7: two identical surfaces at (960,600) and two identical scratch rigs at (700,600), each raising `HWND_TOPMOST` in a loop against the other |

Experiment A: **20 consecutive floor runs, strictly one at a time.** Isolates channel A.
Experiment B: **N waves of 3 concurrent floor runs**, each run's verdict recorded separately.
Isolates channel B. Neither harness ever re-runs anything; every verdict is counted.

## 4. Measured results

Both experiments ran on the unchanged tree at `3c1572b4`, before any edit under `client/`.

- **Experiment A — 20 runs, one at a time: 0 red / 20 (0 %).** Channel A (intra-process fixture
  overlap) is therefore real (§2) but NOT sufficient to produce the flake. Recorded as a hazard,
  not as the diagnosis.
- **Experiment B — 4 waves x 3 concurrent = 12 runs: 8 red / 12 (67 %).**

Channel B reproduced on demand, and the failure text named the collision rather than leaving it to
be inferred:

```
System.IO.IOException : The process cannot access the file
'...\ccp-sp100-flash-draws\desktop-with-a-real-flash.bmp' because it is being used by another process.

ARealImageFile_ReachesTheCompositedDesktop_AndLeavesItWhenTheFlashIsHidden
Assert.Equal() Failure: Values differ   Expected: 0   Actual: 676161

the OS rendered 0 of 43200 pixels as the frame (interactive desktop = True).
Backend said: Unavailable(overlay-nothing-presented: ...)
```

676161 is exactly 951 x 711 — the whole pixel area of one flash, independently measured in §1 out of
the run's own evidence bitmap. That number IS another process's flash counted as ours.

## 5. The named cause

> **Every real-desktop fixture addresses machine-global state by CONSTANT — one evidence-file path,
> one image colour, one spawn seed, and rectangles and hit-test points derived from the screen size,
> which is the same number in every process on the machine. Two `check-floor.mjs` runs therefore
> write the same file, paint the same colour in the same place, and contest the same points. The
> port's own gate wrapper permits three concurrent runs. The gate's verdict was a function of how
> many lanes were gating, not of the tree.**

## 6. The fix applied

Per-process-unique coordinates were considered and rejected: rectangles cannot be guaranteed
disjoint for an unbounded number of processes on a finite screen, and making the evidence path
per-process would break the stable artifact path SP-100 documented for the headed capture.
Exclusive use is complete where disjointness is only probable.

1. **`RealDesktopCollection`** — the three real-desktop classes join one xunit collection, so
   intra-collection sequentiality closes channel A by construction. Same mechanism as
   `ProcessEnvCollection` (SP-062/SP-086). Zero waits, zero retries.
2. **`RealDesktopLease`** — the collection's fixture holds an exclusive `FileShare.None` handle on
   `%TEMP%/ccp-real-desktop.lease` for the life of the collection, closing channel B. A file handle
   rather than a `Mutex` (thread affinity) and rather than a lock-file-existence scheme (the OS
   releases a handle when a process dies, so no reaper is needed). The wait is `TestWait.UntilSync`,
   the suite's one approved bounded wait; on expiry the collection FAILS and names the holder.
3. **`RealDesktopCollectionGuardTests`** — membership made mechanical, so the next probe file cannot
   rejoin the racy default collection silently.
4. **`check-floor.mjs` NAMES failures from the TRX on red.** No retry. Nothing else changed.
5. The residue no in-process mechanism can cover (a FOREIGN topmost window) is **admitted** in
   `client/docs/verification-harness.md` rather than hidden.

Predicted effect at the time of writing: experiment B's rate falls from 67 % toward 0, experiment A
stays 0. Measured outcome is in `record.md` §3 — 8/12 became 2/36 under the same concurrency, with
the two residual reds carrying a DIFFERENT signature that §4 of the record names as an open
question rather than as a fixed defect.
