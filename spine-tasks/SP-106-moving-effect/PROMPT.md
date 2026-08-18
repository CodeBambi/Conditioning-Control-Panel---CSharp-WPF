# SP-106 — The fourth effect: one that has to keep moving

## Mission

Three effects run under the spine. Two are **paced** (fire, wait, fire) and one is **continuous but static** (Pink Filter goes up and stays up). SP-105 proved `ISessionEffect` is a real spine and that `PacedSessionEffect` had been impersonating it. `Session/OwnedSessionEffect.cs` is the seam that came out of that.

**No module has yet had to keep changing while it is on.** A tint that is placed once and a schedule that fires occasionally are both cheap; something that must move every frame is the axis neither covers. That is what this packet is for.

Your outcome: **a moving effect running under the same spine, and an honest answer to whether `OwnedSessionEffect` survives per-frame work.**

## Which effect

**Bouncing Text** — WPF's moving on-screen text. Read its real service in `ConditioningControlPanel/Services/` for the actual motion law, speed, bounds handling, text source, clamps and defaults, and cite what you find with `File.cs:line`.

**If reading the source shows Bouncing Text is a poor fourth** — needing a capability the port lacks, or so close to Pink Filter that it proves nothing new — say so at the plan checkpoint with evidence and propose a better one from the rack.

## THE TRAPS, named at authoring

### 1. If `OwnedSessionEffect` cannot carry motion, that finding is the deliverable
SP-105's whole value was discovering that the base class demanded a clock from things that had none. The equivalent question here: does a per-frame module end up **re-implementing a scheduler** to get its frames? If it does, the seam is wrong again, and **saying so beats shipping a module that hides it**. Do not quietly reintroduce a timer that `OwnedSessionEffect` was created to remove.

### 2. Motion must not be driven by a wall clock
No `Thread.Sleep`, no bare `Task.Delay`, no `DateTime`/`TickCount64` polls — the timing guard fails them and the rule has no exception for animation. If frames need a cadence, it comes from an injected clock the tests control, exactly as pacing does.

### 3. The dot has a third meaning to find
Paced `Live` is a claim about the **clock**. Continuous `Live` is a claim about the **screen** (SP-105, `record.md` §0). For something that moves, decide what `Live` honestly means and **say why** — "on screen but frozen" is a real state and the dot must not call it healthy if it is not.

### 4. Do not weaken the three landed effects
`FlashImagesEffect`, `SubliminalsEffect` and `PinkFilterEffect` and their facts are landed and reviewed. Extraction is fine; changing pacing, pool, tint or stop semantics is a finding, not a licence.

### 5. A rack row or it is not finished
D72 was closed because a module nobody can switch on is not finished. Yours needs its row: left-click opens, right-click toggles, the dot reports truthfully.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-106-moving-effect/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-106-moving-effect/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Effects` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-106-moving-effect/record.md`, `spine-tasks/SP-106-moving-effect/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1372 unit / 87 headless). **Build before running the gate — it refuses stale builds.** Run `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Plan checkpoint: which effect, the WPF motion law with citations, **and your prediction of whether `OwnedSessionEffect` carries per-frame work without a scheduler creeping back**.
2. Build it. If the seam fights you, stop and report rather than bending it.
3. Add its rack row.
4. **Report the third template verdict**: what a moving module needed that neither a paced nor a static one did.
5. **Prove it bites:** break the motion and confirm a test reds without touching the three landed effects' facts.
6. Record divergences from D83 onward.

## Completion Criteria

- A moving effect starts, moves, and stops under the same spine, or a recorded finding that it cannot.
- Its rack row switches it on, with a truthful dot.
- The three landed effects' facts pass unchanged.
- `record.md` carries the third template verdict.
- Build 0 warnings / 0 errors.

## Do NOT

- Reintroduce a timer that `OwnedSessionEffect` exists to remove, without reporting it as a finding.
- Drive motion from a wall clock.
- Touch `Overlay/**`.
- Claim `presentation-verified`; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-106): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` including the third template verdict, plus divergences in `client/docs/wpf-surface-reachability.md`.
