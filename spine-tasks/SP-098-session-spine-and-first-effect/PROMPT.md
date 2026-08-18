# SP-098 — The session spine, and the first effect that actually runs

## Mission

The port has a shell and no app. WPF's product is a **conditioning session**: `START` drives a set of effects, and the Studio rack lists fifteen modules. **The port has zero.** Its one rack row says so in its own UI: *"the spiral overlay effect itself is not ported yet, so this module has no dials and no on/off state."*

Your outcome: **a session that starts and stops, and ONE effect that really runs under it, wired to a rack row with the dials, the live dot and the right-click toggle the grammar promises.**

This is the pattern the other fourteen modules follow. **Getting the spine right matters more than getting the effect elaborate.**

## Which effect

**Flash Images** — WPF's first EFFECTS row (`client/docs/wpf-surface-reachability.md` §8.3), a dashboard mosaic tile, and the simplest to make honest: it draws images on a timer. Read `ConditioningControlPanel/Services/` for its real service and settings, and cite what you find.

If reading the source shows a different module is a materially better first (simpler surface, fewer platform seams, less overlay dependency), **say so at the plan checkpoint with the evidence and take that instead.** The spine is the deliverable; the effect is its first proof.

## THE TRAPS, named at authoring

### 1. The overlay is where the first attempt died
`docs/constitution.md` makes `CCP.*` failure evidence, and overlay/compositor work is the reason. **Do not build a compositor.** If Flash Images needs an always-on-top click-through surface, that is a **platform capability with its own packet** — scope this one to what can be honestly proven, and if the effect cannot draw without an overlay, say so and deliver the spine with the effect's non-drawing half proven.

### 2. A session that only sets a flag is not a session
WPF's `START` starts real work and its stop really stops it. **A spine that flips `IsRunning` and raises an event has proved nothing.** The effect must observably do something and observably stop, and the test must fail if stop leaks — that is the async-lifecycle contract (`client/docs/architecture.md` A-004 and the operation registry), and the port already has the machinery.

### 3. The live dot and the toggle are load-bearing, not decoration
The rack grammar is **left-click opens the module, right-click toggles the effect**, with a dot that shows what is running (§8.3, and the WPF onboarding text quoted there). SP-091 recorded the missing dot and toggle as gaps D5/D6 with the reason *"the port has nothing to wire yet."* **You are what closes them** — and if you add a dot that cannot report truthfully, you have built the fake-available shape the capability contract bans.

### 4. No wall-clock waits
An effect on a timer is the most tempting place in this codebase to sleep in a test. Use the injected clock the port already has (`ISoundClock` is the precedent) and the shared `TestWait`.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Session/**` (new), `client/src/CcpClient.Desktop/Effects/**` (new), `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Lifecycle/**`, `client/src/CcpClient.Desktop/Persistence/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-098-session-spine-and-first-effect/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Features/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/Tray/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-098-session-spine-and-first-effect/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Session` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Features/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/Tray/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-098-session-spine-and-first-effect/record.md`, `spine-tasks/SP-098-session-spine-and-first-effect/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1128 unit / 70 headless). **The gate refuses stale builds — build the solution first.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. At the plan checkpoint report: the WPF session start/stop semantics you found with citations, which effect you are taking and why, and **what the effect can honestly do without an overlay**.
2. Build the session spine: start, stop, the persisted preset it reads, and ownership through the operation registry so a stop really stops.
3. Build the one effect under it, with its rack row: dials, a **truthful** live dot, and the right-click toggle.
4. Close D5 and D6 for that row, or say precisely why they remain open.
5. **Prove stop stops.** A test that fails if the effect keeps working after stop, driven by the injected clock.
6. **Prove it bites:** make stop a no-op and confirm the test reds. Restore byte-identically; do not commit.
7. Record divergences from D45 onward.

## Completion Criteria

- A session starts and stops from the UI; the effect observably runs and observably stops.
- One rack row has real dials, a truthful dot and a working right-click toggle.
- No wall-clock wait; no overlay/compositor built here.
- Build 0 warnings / 0 errors.

## Do NOT

- Build a compositor or an always-on-top surface. That is its own packet.
- Ship a dot that cannot report truthfully.
- Let a session flag stand in for work actually starting and stopping.
- Claim `presentation-verified`; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-098): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md`, plus divergence entries in `client/docs/wpf-surface-reachability.md`.
