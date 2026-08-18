# SP-099 — The overlay surface, approached the way the first attempt was not

## Mission

SP-098 landed a session and Flash Images that runs, picks images and paces them — and **draws nothing**, because effects draw on an always-on-top click-through surface the port does not have. Fourteen more modules need the same surface.

Your outcome: **a typed overlay capability that either presents a real click-through always-on-top surface, or refuses honestly — with the Windows half proven by the OS, not by a method returning.**

## READ THIS FIRST: this is where the first port attempt died

`docs/constitution.md` makes `CCP.*` failure evidence, and overlay/compositor work is the largest reason. Before planning, read what that attempt did and why it failed — `ConditioningControlPanel/CCP.*` and `client/docs/port-lessons.md`. **Report at the plan checkpoint what specifically went wrong there and how your approach differs.** A plan that does not mention the prior failure has not looked.

## WPF ground truth

`ConditioningControlPanel/Services/FlashService.cs` — `Topmost = true` (`:3612`), `WS_EX_TRANSPARENT` (`:3666`), `SetWindowPos ... HWND_TOPMOST` (`:3862`). Read the surrounding code for the real lifecycle: creation, per-monitor placement, teardown, and what happens on multiple displays.

## THE TRAPS, named at authoring

### 1. "It compiles" is not "it presents"
A window that exists but is invisible, behind everything, or swallowing clicks is **worse than no overlay** — it breaks the desktop while looking implemented. **Availability must be earned from the OS**, the way SP-093's tray earns it from `Shell_NotifyIcon`: place it, then ask the system whether it is really there, click-through, and on top. If you cannot ask, you cannot claim.

### 2. Click-through is the property most easily faked
`WS_EX_TRANSPARENT` is one flag; proving input passes through is not. **A test asserting the flag was set is not evidence the surface is click-through** — the same shape as asserting a tray method returned. Get as close to an input-level fact as this machine allows, and name precisely what remains headed-only.

### 3. Linux is not Windows and must not pretend
`ISecretStore` and `ITrayPresence` both return a typed `Unavailable` on Linux with a named manual gate. **Do the same.** Wayland guarantees no always-on-top click-through, and claiming one because it compiles is the banned shape. A `BLOCKED` row naming the exact manual gate is the honest outcome.

### 4. Do not wire it to an effect in this packet
Capability first, consumed later — the SP-093 pattern. Wiring it to Flash Images entangles this packet's evidence with the effect's, and a headed gate you cannot discharge would then block a capability that is otherwise sound. Say plainly that nothing draws yet.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Overlay/**` (new), `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-099-overlay-surface/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-099-overlay-surface/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Overlay` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-099-overlay-surface/record.md`, `spine-tasks/SP-099-overlay-surface/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1175 unit / 81 headless). **The gate refuses stale builds — build first.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Report at the plan checkpoint: what the first attempt did and why it failed; your design; **and exactly which properties you can prove from the OS versus which stay headed-only**. An honest short list beats a long unprovable one.
2. Build the typed capability: `Available` only when the OS confirms it, `Unavailable(reason)` otherwise, following `ITrayPresence`'s shape.
3. Prove the Windows half by asking the system, with the test declaring its own P/Invokes independently of the product's — the SP-093 oracle discipline.
4. Non-Windows returns a typed refusal with a named manual gate.
5. **Prove it bites:** make the backend claim `Available` without creating the window, and confirm a test reds. Restore byte-identically; do not commit the mutation.
6. Record divergences from D52 onward.

## Completion Criteria

- Availability is earned from the OS, never from a method returning.
- Non-Windows refuses honestly with a named gate; no test skipped to conceal it.
- Nothing is wired to an effect; the packet says so.
- Build 0 warnings / 0 errors.

## Do NOT

- Claim click-through because a flag was set.
- Wire this to Flash Images or any effect.
- Ship a Linux no-op.
- Introduce a wall-clock wait.
- Claim `presentation-verified`; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-099): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md`, plus divergences in `client/docs/wpf-surface-reachability.md`.
