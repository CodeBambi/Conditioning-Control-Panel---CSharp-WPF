# SP-107 — Make the floor gate mean what every land has claimed it means

## Mission

The port's entire land discipline is "three consecutive `check-floor.mjs` runs". SP-106's land measured that claim: **nine runs on one unchanged tree gave 1 red, four greens, 1 red, two greens.** Roughly one gate run in five fails for reasons that have nothing to do with the code being landed.

- Run 1: `FlashDrawTests.ARealImageFile_ReachesTheCompositedDesktop_AndLeavesItWhenTheFlashIsHidden` (SP-100)
- Run 7: **six** failures, the whole `OverlayCapabilityTests` fixture (SP-099) — presence, z-order, click-through, withdraw

Both create **real windows** and ask the **real OS** about them. Both predate the wave that caught them.

Your outcome: **a floor gate whose green means the tree is green.**

## Why this is P0 and not housekeeping

At a 1-in-5 red rate, three consecutive greens can happen by luck, and the natural human response — run it again — converts an intermittent into a laundered pass. Every prior land's evidence was true as observed and weaker than it looked. **Nothing about the product is known to be wrong. The instrument is.**

## THE TRAPS, named at authoring

### 1. `allowedSkips` is not the answer and using it is the failure
That list is for preconditions that are **properties of the machine or OS**. "The desktop was busy" is a property of the *moment*. Putting these facts there blinds the floor exactly where SP-099 and SP-100 spent whole waves earning OS-level evidence. **If you find yourself editing `allowedSkips`, stop and report instead.**

### 2. Do not delete the evidence to stop the noise
These tests are the port's only proof that an overlay is really present, really click-through and really on top — earned from the OS rather than asserted. **Weakening them to gain stability is the worst available outcome.** The value is precisely that they talk to the real system.

### 3. A retry loop is a lie with a green light
Retrying until green is banned here. If a fact cannot be made reliable in-process, the honest move is a **precondition guard that names the environment it requires** and fails loudly when it is absent, or **moving it behind the headed gate** where a real desktop is guaranteed — with the floor then knowing it is not covering it.

### 4. Diagnose before you fix
Find out **why** they fail. Candidates worth measuring, not assuming: two floor runs overlapping (the gate runs two projects), a prior test's real window still up when the next starts, foreground/lock state, DWM composition timing, z-order contention from another real window in the same suite. **A fix that does not name the cause is a guess.**

## File Scope

| | |
|---|---|
| May change | `client/tests/**`, `client/tools/verify/**`, `client/docs/verification-harness.md`, and `spine-tasks/SP-107-gate-determinism/**` |
| Must not change | everything else, and specifically `client/src/**`, `client/tests/floor/floor.json`, `client/tests/floor/check-floor.mjs`'s pin semantics, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

`client/tests/floor/check-floor.mjs` itself may be changed **only** to add capability that makes flake visible (for example preserving failure names and the TRX directory on red). **It must never gain a retry.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-107-gate-determinism/floor-delta.json` |
| fileScopeMustChange | `client/tests` |
| fileScopeMustNotChange | `client/src/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-107-gate-determinism/record.md`, `spine-tasks/SP-107-gate-determinism/floor-delta.json` |

**Pin: 1472 unit / 90 headless.** Build before running the gate. Run `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Reproduce and diagnose first.** Run the gate repeatedly enough to see red at least twice, capture the names and the failure text, and report the measured cause at the plan checkpoint. Say what you ran and what you saw, not what you suspect.
2. Fix the cause, honouring the traps above.
3. **Prove the fix with numbers, not adjectives.** Report a run count and a red count before and after — for example 20 consecutive runs. One green run proves nothing about a 1-in-5 flake.
4. If a fact cannot be made reliable in-process, move it behind the headed gate and **make the floor state that it is not covering it**. A gap the gate admits beats a gap it hides.
5. Record what the flake rate was, what it is now, and what remains unproven.

## Completion Criteria

- The measured cause is named with evidence.
- The gate's red rate is measured before and after over a stated number of runs.
- No `allowedSkips` addition, no retry, no deleted or weakened OS-level assertion.
- Any fact moved behind the headed gate is named in `client/docs/verification-harness.md`.
- Build 0 warnings / 0 errors.

## Do NOT

- Add to `allowedSkips`.
- Add a retry, anywhere.
- Weaken or delete an OS-level assertion to gain stability.
- Claim a fix without a measured before/after red rate.

## Git Commit Convention

Conventional commit, `fix(SP-107): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the measured before/after, plus any harness changes in `client/docs/verification-harness.md`.
