# SP-108 — The fifth module, and the first that does not draw

## Mission

Four modules run under the spine: Flash Images, Subliminals, Pink Filter, Spiral Overlay. **All four are from WPF's EFFECTS group and all four draw on an overlay surface.** Every seam the port has proven — `OwnedSessionEffect`, `OverlaySurfaceSet`, the dot's three states, the surface presenter owning the cadence — was proven against modules that put pixels on a screen.

WPF's rack has **four groups**: EFFECTS, GAMES & CARDS, IMMERSION, TIMING (`client/docs/wpf-surface-reachability.md` §8.3). The port has rows in one.

Your outcome: **a module from a DIFFERENT group running under the same spine — or an honest finding that the spine does not reach it.**

## Which module

**Read the rack and choose**, then defend the choice at the plan checkpoint with `File.cs:line` citations from `ConditioningControlPanel/`. The criterion is not difficulty; it is **distance from what is already proven**. A module whose whole job is drawing an overlay teaches nothing new. Prefer one that:

- changes state the user can observe **without** an overlay surface, or
- is driven by session progress rather than by a repaint cadence, or
- interacts with an existing module rather than running beside it.

**If every candidate in the other three groups turns out to need a capability the port lacks**, say so at the plan checkpoint with evidence per candidate and propose the best in-scope alternative. That finding would itself be worth the wave — it would mean the port's next gap is a capability, not a module.

## THE TRAPS, named at authoring

### 1. The dot has meant three different things; find out if it means a fourth
Paced `Live` is a claim about the **clock**. Continuous `Live` is a claim about the **screen** (SP-105). A non-drawing module has neither. **Decide what `Live` honestly means when there is nothing to see, and say why.** If the three states stop describing reality, that is a template finding, not a licence to pick the closest one.

### 2. Do not invent an overlay to make the module fit
If your candidate needs no surface, it must not acquire one for symmetry. `OverlaySurfaceSet` is not a membership badge. A module that holds a surface it never paints is worse than one that holds none.

### 3. `ISessionEffect` has been right twice; do not assume a third
SP-105 found the interface innocent and the base class guilty. SP-106 found the base class sound for per-frame work. **Predict before you build**, and if the seam fights you, report it rather than bending the module.

### 4. Four landed modules must not regress
`FlashImagesEffect`, `SubliminalsEffect`, `PinkFilterEffect`, `SpiralOverlayEffect` and their facts are landed and reviewed. Extraction is fine; changing pacing, pool, tint, motion or stop semantics is a finding.

### 5. A rack row or it is not finished
Left-click opens, right-click toggles, the dot reports truthfully. If your module belongs to a group the port's rack does not yet have, **create that group** — the rack is 1 group / 4 rows against WPF's 4 / 15 (D14).

## The gate rule that is now law

**Never run `check-floor.mjs` concurrently with anything else** — not another gate, not a build. SP-107 proved concurrent gate runs corrupt real-desktop facts, and the lease now serializes them, but a queued run is wasted wall-clock, not safety. Run it alone.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-108-non-drawing-module/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-108-non-drawing-module/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Effects` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-108-non-drawing-module/record.md`, `spine-tasks/SP-108-non-drawing-module/floor-delta.json` |

**Pin: 1477 unit / 90 headless.** Build before the gate; run `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Plan checkpoint: your candidate with citations, why it is far from what is proven, **and your prediction about the seam and the dot**.
2. Build it under the existing spine.
3. Add its rack row, creating its group if the port lacks one.
4. **Report the fourth template verdict**: what a non-drawing module needed that a drawing one did not.
5. **Prove it bites:** break it and confirm a test reds without touching the four landed modules' facts.
6. Record divergences from D92 onward.

## Completion Criteria

- A module from a different rack group runs under the same spine, or a recorded finding that it cannot.
- Its rack row switches it on with a truthful dot.
- The four landed modules' facts pass unchanged.
- `record.md` carries the fourth template verdict.
- Build 0 warnings / 0 errors.

## Do NOT

- Acquire an overlay surface a module does not need.
- Report a dot state that does not describe reality.
- Run the floor gate concurrently with anything.
- Claim `presentation-verified`; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-108): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` including the fourth template verdict, plus divergences in `client/docs/wpf-surface-reachability.md`.
