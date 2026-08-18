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

*(Experiment A is running as this section is written; experiment B has not been run. This section
is filled in from the harness logs and NOTHING is written here before it is observed.)*

- Experiment A (20 sequential): **pending**
- Experiment B (waves of 3 concurrent): **pending**

## 5. The named cause

**Pending §4.** It will be stated only as the experiments support it, and the failure text of a
reproduced red will be quoted verbatim rather than paraphrased.

## 6. Candidate fixes under consideration (none applied yet)

Nothing will be skipped, retried, weakened, or moved to `allowedSkips` under any of these.

1. If channel B is the cause: **make the desktop resource each process uses unique to that
   process** — rectangles, hit-test points, flash colour and the flash spawn seed all derived from
   the current process id, so two concurrent runs never share a point, never overlap a rectangle
   and never count each other's pixels. The assertions and the OS calls stay exactly as they are;
   only coordinates and a colour change, and both are already arbitrary conveniences ("the centre
   of the screen", "a colour nothing on a desktop is").
2. If channel A contributes: **serialize the three real-desktop fixtures into one xunit
   collection**, closing the §2 overlap hazard by construction. Zero waits, zero retries.
3. Whatever remains impossible in process (a FOREIGN topmost window: the shipping WPF app
   re-asserting `HWND_TOPMOST`, a locker, a full-screen game) is **admitted** in
   `client/docs/verification-harness.md` as a fact the floor does not cover, with the headed tier-2
   gate named as where a guaranteed desktop is asserted.
4. `check-floor.mjs` gains the ability to **NAME failures from the TRX on red** — explicitly
   allowed by the packet, and never a retry. Today it prints only the last six lines of
   `dotnet test` output, which is why SP-106's run 7 had to be reconstructed by hand.
