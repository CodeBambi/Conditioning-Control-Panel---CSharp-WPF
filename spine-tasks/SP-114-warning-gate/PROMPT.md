# SP-114 — Make "0 warnings" a fact instead of a habit

## Mission

Every landed wave in this port reports **0 warnings / 0 errors**. **None of those claims was ever mechanically checked.**

`client/tests/floor/check-floor.mjs` runs `--no-build` by design (`client/docs/port-lessons.md:204`) and contains no warning handling at all. So each claim rests entirely on a lane reading its own build output — and SP-113 discovered its own filter, `grep -E "error|warning CS|Build succ"`, **could never match `warning xUnit2013`**. It had reported clean **four times** off a filtered stream, and the two real warnings were found only because a reviewer forced a full rebuild.

Your outcome: **a gate that observes build warnings, so no future claim depends on a lane's grep.**

## THE CENTRAL TRAP: do not break the staleness guard to gain a build

`check-floor.mjs` is `--no-build` for a recorded reason — it once measured the previous wave's assemblies and reported them as a regression, which is why the **stale-build guard** exists and why it fires. **Do not make the floor build.** A separate gate, or an added mode, is fine; silently turning the floor into a builder is not, and would re-open a defect this port already paid for.

Read `client/docs/port-lessons.md` around `:204` before designing.

## THE OTHER TRAPS

### 1. A warning gate that cannot fail is worse than none
**Prove it bites.** Introduce a real warning — an unused variable, a `xUnit2013`, whatever the analyzers genuinely emit — confirm the gate reddens, then restore byte-identically. A gate asserted rather than demonstrated is exactly the shape this packet exists to end.

### 2. Do not silence warnings to make the gate pass
If the tree has warnings today, **the honest outcome is a red gate and a report**, not a `#pragma`, not a `NoWarn`, not a raised `WarningLevel`. The build reports 0/0 at `69586b97` on this machine — if you find otherwise, that is the finding.

### 3. Say what the gate cannot see
Analyzer warnings depend on configuration, SDK version and which projects are built. **Name the boundary**: what it covers, what it does not, and whether a warning suppressed in a `.editorconfig` or csproj would be invisible to it. The SP-109/SP-110/SP-111 discipline — name where the chain stops — applies to tooling too.

### 4. This is the port's own instrument, so the bar is higher, not lower
SP-107 rebuilt the floor gate's determinism and its record is the model: **measure before and after with counts**, not adjectives.

## File Scope

| | |
|---|---|
| May change | `client/tests/floor/**` (EXCEPT `floor.json`), `client/tools/**`, `client/docs/port-workflow.md`, `client/docs/verification-harness.md`, `client/tests/**`, and `spine-tasks/SP-114-warning-gate/**` |
| Must not change | everything else, and specifically `client/src/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

`client/src/**` is closed: **if closing this gate requires a product change, that is a finding, not a licence.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-114-warning-gate/floor-delta.json` |
| fileScopeMustChange | `client/tests/floor` |
| fileScopeMustNotChange | `client/src/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-114-warning-gate/record.md`, `spine-tasks/SP-114-warning-gate/floor-delta.json` |

**Pin: 1930 unit / 117 headless.** **Run `check-floor.mjs` ALONE.** `sum-deltas` before deleting any delta file. **Keep every artifact inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** where the gate lives, why it does not compromise the staleness guard, what it can and cannot see, and how you will prove it bites.
2. Build it.
3. **Prove it bites** — real warning introduced, gate reddens, restored byte-identically.
4. **Run it against the current tree and report the count**, whatever it is.
5. Record the gate in `client/docs/verification-harness.md` and the rule in `client/docs/port-workflow.md`.
6. Divergences only if you find product-side ones; otherwise none.

## Completion Criteria

- A gate observes build warnings and is demonstrated to fail on one.
- The floor's `--no-build` behaviour and stale-build guard are intact.
- The boundary is named: what the gate cannot see.
- No warning was silenced to make it pass.
- Build 0 warnings / 0 errors, **read unfiltered**.

## Do NOT

- Make `check-floor.mjs` build.
- Silence, suppress or `NoWarn` any warning.
- Change `client/src/**`.
- Report a count you obtained through a filter you did not verify against a known-matching case.

## Git Commit Convention

Conventional commit, `fix(SP-114): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the before/after counts, the bite demonstration, and the named boundary; the gate in `client/docs/verification-harness.md`; the rule in `client/docs/port-workflow.md`.
