# SP-105 — A continuous effect, and the rack rows that switch modules on

## Mission

Two effects run under the spine and **both are timed**. SP-101's template verdict named the untested axis outright: a continuous effect is what shows whether `ISessionEffect` is a spine or whether the spine has quietly been assumed to be a scheduler. WPF drives Spiral Overlay and Pink Filter with **no timer at all** (`MainWindow/MainWindow.Presets.cs:1254-1255` — `App.Overlay?.RefreshOverlays()`, no `Start`/`Stop`).

Also: **Subliminals has no rack row.** Today only a test or a hand-edited file switches it on (D72). A module a user cannot turn on is not finished.

Your outcome: **a continuous effect running under the same spine, and rack rows that switch both it and Subliminals on.**

## THE TRAPS, named at authoring

### 1. If the spine is a scheduler, say so — do not bend the effect to fit it
The point of this packet is the answer, not the effect. If `ISessionEffect` turns out to assume paced firing, **the finding is worth more than a working module**: report it and propose the split. Wrapping a continuous effect in a fake timer to make it fit is the failure this packet exists to catch.

### 2. The dot must stay truthful for a continuous module
`EffectDotState` was designed around arm/fire/disarm. A module that is simply *on* has no firing. **If the three states no longer describe reality, that is a template finding**, not a licence to show `Live` for something that never draws.

### 3. Rack rows are the grammar, not decoration
Left-click opens the module, right-click toggles, the dot reports what is running — established in the survey and honoured by every landed row. A row whose toggle does not really toggle, or whose dot cannot report truthfully, is worse than no row.

### 4. Do not weaken the two landed effects
`FlashImagesEffect` and `SubliminalsEffect` and their facts are landed and reviewed. Extracting shared machinery is fine; changing pacing, pool or stop semantics is a finding.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-105-continuous-effect/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

While in `Views/**`, fix the stale duplicate `<summary>` at `Views/MainWindow.axaml.cs:207-211`, whose text is now false and sits directly above the block that says the opposite.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-105-continuous-effect/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Effects` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-105-continuous-effect/record.md`, `spine-tasks/SP-105-continuous-effect/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1314 unit / 81 headless). **The gate refuses stale builds — build first, and run `sum-deltas` BEFORE deleting any delta file.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. At the plan checkpoint: which continuous effect, the WPF semantics with citations, **and your prediction of whether `ISessionEffect` fits it**. Predicting first is how you notice the spine fighting you.
2. Build the effect. If the spine does not fit, stop and report rather than bending it.
3. Add rack rows for the new module **and for Subliminals**, honouring the grammar.
4. **Report a second template verdict**: what a continuous module needed that a paced one did not.
5. Fix the stale `<summary>`.
6. **Prove it bites:** break the new effect's on/off and confirm a test reds without touching the landed effects' facts.
7. Record divergences from D73 onward.

## Completion Criteria

- A continuous effect starts and stops under the same spine, or a recorded finding that it cannot.
- Rack rows switch on the new module and Subliminals, with truthful dots and working toggles.
- The two landed effects' facts pass unchanged.
- `record.md` carries the second template verdict.
- Build 0 warnings / 0 errors.

## Do NOT

- Wrap a continuous effect in a fake timer to fit the spine.
- Ship a dot that cannot report truthfully, or a toggle that does not toggle.
- Touch `Overlay/**`.
- Introduce a wall-clock wait.
- Claim `presentation-verified`; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-105): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` including the second template verdict, plus divergences in `client/docs/wpf-surface-reachability.md`.
