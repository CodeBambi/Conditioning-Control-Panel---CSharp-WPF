# SP-116 — plan checkpoint (written BEFORE the first measurement and the first edit)

Branch `lane/SP-116-flake-characterisation`, base `11036bbc`, worktree
`.claude/worktrees/agent-a76eaa63a39ac984b`.

## 0. What I am NOT allowed to buy

No retry, no quarantine, no `allowedSkips` addition, no widened window, no loosened assertion.
`client/src/**` is closed: if the cause is a product defect I record it as a finding and a board
row and stop. `client/tests/floor/floor.json` is never opened.

## 1. THE PROTOCOL, STATED BEFORE IT IS RUN

**Unit of observation: one full `dotnet test` run of `client/tests/CcpClient.Tests`, sequential,
one at a time, `--no-build`, TRX preserved.** Not `check-floor.mjs` — that adds the headless
project and a build to every sample and would halve the number of samples per hour for facts that
all three recorded strands place in the unit project. The floor gate itself is run ALONE, as a
gate rather than as a sample.

A run counts RED if its TRX carries one or more failed results. On red the harness extracts every
failed test's fully-qualified name AND its message into the round log. **The message is the
artifact SP-106 lost and SP-115 did not capture, and it is the whole point of the instrument.**

Load is not simulated. xunit's own default parallelism over ~2060 tests is the load that produced
all three strands; adding synthetic load would measure a machine that never runs the gate.

### 1.1 How many runs

The question is whether the base rate is nearer 1-in-7 (p = 0.143, SP-115's reading) or 1-in-20
(p = 0.05, SP-107's post-fix residue). One-sided binomial, alpha ~ 0.05, power ~ 0.8:

    n = (z_a*sqrt(p0*q0) + z_b*sqrt(p1*q1))^2 / (p1-p0)^2
      = (1.645*sqrt(.05*.95) + 0.84*sqrt(.143*.857))^2 / (0.093)^2
      = (0.3585 + 0.2940)^2 / 0.008649 = 49.2

**So 50 runs is the floor and I will run 60 per arm.** At n = 60, p0 = 0.05 gives a mean of 3.0
reds and p1 = 0.143 gives 8.6; the decision line is 6. Anything under 50 runs cannot separate the
two hypotheses and I will not report a rate from fewer.

**What no run count here can do:** 60 runs cannot detect a 1-in-200 fault at all (expected 0.3
events), and cannot prove a rate is ZERO — 0 red in 60 bounds p at 4.9 % (95 % one-sided
Clopper-Pearson), not at 0. Every rate below is reported with that interval.

### 1.2 The arms

| arm | tree | runs | purpose |
|---|---|---|---|
| **BASE** | `11036bbc`, unmodified | 60 | the rate this packet was written about |
| **A/B** | 30 BASE + 30 LANE, strictly alternating in one window | 60 | attribution |

The A/B arm alternates so that "the tree changed" and "the desktop drifted" are separated by
design (SP-112's lesson). Base runs in the A/B arm are taken by checking the specific test files
back out of `11036bbc` and rebuilding; every run is launched, every run is counted, nothing is
re-run and nothing is discarded.

## 2. THE THREE STRANDS AND THE EXPERIMENT FOR EACH

### Strand 2 first, because it is the discriminator

`SpiralOverlayEffectTests.DisarmReleasesTheWorkUNCONDITIONALLY_EvenWhenItThoughtItWasNotArmed`
touches no OS. Reading the rig and the spine, the hypothesis SP-106 §6.2 named and retired is
re-opened with a specific mechanism:

1. `OwnedSessionEffect.Arm` registers a parked operation and `AsyncOperationOwner.RunAsync` starts
   its body with `Task.Run` (`Lifecycle/OperationRegistry.cs:216`), so the body lives on a
   thread-pool thread.
2. `ParkUntilCancelledAsync` calls `ReleaseIfStillOurs(generation)` twice — once in the
   cancellation registration (`Session/OwnedSessionEffect.cs:352`) and once after
   `await stopped.Task` (`:357`), where the TCS carries `RunContinuationsAsynchronously` (`:345`),
   so that second call is a POOL continuation that arrives an unbounded time after `Disarm()`
   returned.
3. `Disarm()` does not clear `_generation`, so `ReleaseIfStillOurs` (`:417`) passes its guard and
   calls `ReleaseWork()` for real.
4. `SpiralOverlayEffect.ReleaseWork` posts `_surface.Withdraw` through `EffectSignal.Post`
   (`Effects/SpiralOverlayEffect.cs:268`). In the product that post is `Dispatcher.UIThread.Post`
   and the surface is only ever touched on the UI thread. **In the rig it is `InlineDispatch`,
   which runs the action on the calling thread — the pool thread.**
5. The test then re-engages the surface DIRECTLY behind the module's back
   (`SpiralOverlayEffectTests.cs:340`, the only direct `Surface.Engage` in the whole project) and
   asserts on `Showing` and `Withdrawals`, which are plain non-volatile fields of a rig double,
   from the test thread.

Predicted signatures: `Assert.True(lab.Surface.Showing)` at line 341 failing, or
`Assert.Equal(2, lab.Surface.Withdrawals)` reporting 3 (or 2 after a lost `++`).

**Control that fails if I am wrong:** an instrument that records the managed thread id of every
`Withdraw` and every `Engage` on the rig surface. If the mechanism is real, a run must show at
least one `Withdraw` arriving on a thread that is not the test's, ordered after `Disarm()`
returned. If every touch is on one thread, the hypothesis is dead again and I say so.

**Second control:** the observed failure MESSAGE from the base arm. If the reds are neither of the
two predicted signatures, the hypothesis is wrong regardless of how good the reading is.

### Strand 1 / 3 — the real-desktop facts

`RealDesktopCollection` membership is already mechanical (`RealDesktopCollectionGuardTests`) and 14
classes carry it; I re-check the census for a fixture that reaches the desktop through a helper the
guard does not name. SP-107 §4 left the residue instrumented: a composited read that returns a
complete desktop with none of the flash's colour, with four verdicts now printable. **My base arm
either fires that instrument or it does not, and either way the answer is the first evidence anyone
has of which verdict it is.** SP-107 named the geometry fix conditional on the metrics having
moved; `FlashPixelProbe` is inside this packet's File Scope, so that fix is available to me IF the
instrument says so, and is not available on a guess.

### Order dependence

SP-112 proved adding test classes changes within-class ordering. If the base arm's reds are not
explained by either mechanism above, I compare the TRX start/end ordering of the failing class
between red and green runs before inventing anything.

## 3. STEP ORDER

1. This file (checkpoint).
2. Build once. Time one run to price the protocol.
3. BASE arm, 60 runs, logs in this packet folder.
4. Attribution experiments and controls.
5. Fix only what is fixable in `client/tests/**`; name what is not.
6. A/B arm, 60 interleaved runs, same protocol.
7. `check-warnings.mjs` alone, `check-floor.mjs` alone, `sum-deltas --check`.
8. `record.md`, `floor-delta.json`, and any harness change in
   `client/docs/verification-harness.md`.

## 4. What this plan already concedes

A rate measured on ONE machine on ONE day is a property of that machine on that day. Nothing here
will license a claim about CI, about Linux, or about a machine with a different core count, and
nothing here discharges any headed gate.
