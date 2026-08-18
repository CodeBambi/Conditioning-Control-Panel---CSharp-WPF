# SP-101 — The second effect, which is really a test of the first one's template

## Mission

SP-098 built a session spine and one effect. SP-100 made it draw. **Fourteen more modules will copy that shape, and nobody has yet copied it once.**

Your outcome: **a second effect running under the same spine, drawing through the same surface — and an honest report on what the template cost you.**

The effect matters less than the answer to: *does this pattern hold?*

## Which effect

**Subliminals** — WPF's third EFFECTS row, a dashboard mosaic tile, and a near neighbour of Flash Images: timed, image-or-text based, drawn on an overlay. Close enough that the template should fit; different enough that a bad template will show.

Read `ConditioningControlPanel/Services/` for its real service, settings, defaults and clamps, and cite what you find. **If reading the source shows Subliminals is a poor second — needing a capability the port lacks, or so identical to Flash Images that it proves nothing — say so at the plan checkpoint with evidence and propose a better one.**

## THE TRAPS, named at authoring

### 1. Copy-paste is the failure this packet exists to catch
If you find yourself duplicating `FlashImagesEffect` and renaming, **stop and report it**. Two effects sharing 90% of their body means the template is wrong and the right output is a refactor proposal, not a fourteen-fold duplication. **The shared machinery you find is the deliverable.**

### 2. Do not weaken the first effect to share code with the second
`SessionSpineTests` and `FlashEffectTests` are landed and reviewed. Extracting shared machinery is fine; changing Flash Images' pacing, pool or stop semantics is a finding, not a licence. Its facts must pass unchanged.

### 3. The template hazards SP-098's review named are yours to close
- `Arm()` returns `void` with **no typed way to refuse** — and the modules still to come are the ones with platform capabilities. If Subliminals can fail to arm, this is where that gets fixed.
- `Changed` fires on arbitrary threads with marshalling pushed onto every consumer. Two consumers do it right; the fifteenth will not.
- `FlashImagesEffect.Fire` has a benign handle race a `CompareExchange` closes. **It is in the file that gets copied.**

Close them or record why not, per hazard.

### 4. `SystemSessionClock` still has zero coverage
Every test substitutes a manual clock, so the real timer wiring all fifteen modules pace on is compiled and never executed. Named at the SP-098 land as structural. **If you see an honest way to cover it, take it; if not, leave it named.**

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-101-second-effect/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

`Views/**` is open **only** so you may fix `Views/Pages/StudioPage.axaml:152`, which still tells the user the drawing half is not ported and became false when SP-100 landed. The right fix reads the presenter's state rather than asserting a platform.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-101-second-effect/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Effects` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-101-second-effect/record.md`, `spine-tasks/SP-101-second-effect/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1231 unit / 81 headless). **The gate refuses stale builds — build first, and apply `sum-deltas` before deleting any delta file.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. At the plan checkpoint: which effect and why, what WPF semantics you found with citations, **and what you expect to share versus duplicate**. Predicting it before you build it is how you notice if the template fights you.
2. Build the effect under the existing spine, drawing through the existing surface.
3. **Report the template verdict in `record.md`: what generalised, what did not, and what a third effect should change.** This is the packet's real output.
4. Close or record the three SP-098 template hazards.
5. Fix `StudioPage.axaml:152`.
6. **Prove it bites:** break the new effect's draw or stop and confirm a test reds without touching Flash Images' facts.
7. Record divergences from D65 onward.

## Completion Criteria

- A second effect starts, runs, draws and stops under the same spine and surface.
- Flash Images' facts pass unchanged.
- `record.md` carries an explicit template verdict.
- The three hazards are closed or recorded per hazard.
- `StudioPage.axaml:152` no longer says something false.
- Build 0 warnings / 0 errors.

## Do NOT

- Duplicate `FlashImagesEffect` and rename it.
- Change Flash Images' pacing, pool or stop semantics.
- Touch `Overlay/**` — that capability is landed and reviewed.
- Introduce a wall-clock wait.
- Claim `presentation-verified`; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-101): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` including the template verdict, plus divergences in `client/docs/wpf-surface-reachability.md`.
