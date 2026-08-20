# SP-116 — Three strands, no shared explanation, and every land rests on them

## Mission

**The suite is intermittently red on an unmodified tree.** SP-115's lane checked out base in its own worktree and ran seven times: **one red, on a test it had not touched.**

There are now **three strands and they do not share an explanation**:

| strand | filed | what failed | the recorded explanation |
|---|---|---|---|
| 1 | SP-106 §6.2 / SP-107 | `OverlayCapabilityTests` (6 at once), `FlashDrawTests` | real-OS tests fail when the **desktop is contended** |
| 2 | SP-114 | `SpiralOverlayEffectTests.DisarmReleasesTheWorkUNCONDITIONALLY` | **touches no OS at all** — contradicts strand 1's conclusion |
| 3 | SP-115 | `FlashDrawTests` (2 in 8 on lane), `SpiralOverlayEffectTests` (1 in 7 **at base**) | none |

SP-107 already fixed one real cause — concurrent gate runs colliding on machine-global state — and left a ~5% residual it instrumented but could not diagnose.

**Consequence: no land in this port can currently claim determinism.** "Three consecutive greens" is a weaker statement than it reads, and this packet exists to make it mean something again.

Your outcome: **the mechanism named with evidence, or a bounded honest statement of what remains unexplained after a real attempt.**

## THE CENTRAL TRAP: do not make it green, make it understood

**A retry, a quarantine, an `allowedSkips` entry, a widened timeout or a loosened assertion is a failure of this packet**, not a fix. SP-107 refused all of those and its record is the model. If a fact is genuinely environment-dependent, the honest outcomes are a **precondition guard that names what it requires and fails loudly**, or **moving it behind the headed gate** — with the floor then stating it no longer covers it.

**And remember SP-115's tolerance lesson**: a window sized to an observed error is exactly the size of the defect it will next hide.

## WHERE TO LOOK — measured, not assumed

- **Strand 2 is the discriminator.** `SpiralOverlayEffectTests` touches no OS, so "desktop contention" cannot explain it. SP-106 §6.2 named `InlineDispatch` aliasing the presenter's lock-free fields against the asserting thread, then **retired that hypothesis**. Re-open it: read `MovingEffectSpineTests`' rig and `SpiralSurfacePresenter`'s fields.
- **Order dependence.** SP-112 proved adding test *classes* changes xunit's within-class ordering and exposed a latent defect. Ordering is a real mechanism here, not a theory.
- **Shared machine state.** SP-107 found evidence files, image colours and spawn seeds addressed by constant. `RealDesktopCollection` serialises real-desktop fixtures — check whether every fixture that needs it is in it, and whether the lease covers what it claims.

## Measure, don't argue

**Report run counts and red counts, before and after.** SP-107's record is the standard: 0/20 sequential against 8/12 concurrent, then 4/76. One green run proves nothing about a 1-in-7 flake. **Interleave base and lane runs** when attributing — SP-112 showed that is the only design separating "the tree changed" from "the desktop drifted".

## File Scope

| | |
|---|---|
| May change | `client/tests/**`, `client/docs/verification-harness.md`, `client/docs/port-workflow.md`, and `spine-tasks/SP-116-flake-characterisation/**` |
| Must not change | everything else, and specifically `client/src/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

`client/src/**` is closed: **if a product defect is the cause, that is a finding and a board row, not a licence.** Say so and stop.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-116-flake-characterisation/floor-delta.json` |
| fileScopeMustChange | `client/tests` |
| fileScopeMustNotChange | `client/src/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-116-flake-characterisation/record.md`, `spine-tasks/SP-116-flake-characterisation/floor-delta.json` |

**Pin: 2062 unit / 121 headless.** Run `check-warnings.mjs` and `check-floor.mjs` **ALONE**. `sum-deltas` before deleting any delta file. **Keep every artifact inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** your reproduction protocol and how many runs it needs to distinguish a 1-in-7 from a 1-in-20. State it before running.
2. Reproduce at base. **Report the rate.**
3. Attribute the mechanism per strand, with a control that would have failed if you were wrong.
4. Fix what is fixable in `client/tests/**`; **name what is not**.
5. **Re-measure with the same protocol** and report before/after counts.
6. Record the residual honestly, including what your run count can and cannot exclude.

## Completion Criteria

- Each strand's mechanism named with evidence, or recorded as unexplained after a stated number of runs.
- Before/after rates measured with the same protocol.
- No retry, quarantine, `allowedSkips` addition, widened timeout or loosened assertion.
- Warning gate green; build 0/0.

## Do NOT

- Make it green without understanding it.
- Retry, skip, quarantine or relax anything.
- Change `client/src/**`.
- Report a rate from fewer runs than your own protocol requires.

## Git Commit Convention

Conventional commit, `fix(SP-116): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the protocol, per-strand attribution, before/after rates and the residual; any harness change in `client/docs/verification-harness.md`.
