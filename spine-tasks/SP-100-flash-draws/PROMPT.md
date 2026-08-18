# SP-100 — Make the first effect actually appear on screen

## Mission

SP-098 landed Flash Images: it picks images, paces them WPF's way, and stops when told. SP-099 landed an overlay surface that the OS confirms is present, click-through and on top. **Neither draws anything.** The two have never met.

Your outcome: **a flash the user can see.** The first composited pixel this port has ever produced.

## Dependencies (LANDED — consume, do not rebuild)

- `client/src/CcpClient.Desktop/Effects/FlashImagesEffect.cs` and `Session/` (SP-098). The effect already draws from a pool on an injected clock; it has nowhere to send the result.
- `client/src/CcpClient.Desktop/Overlay/` (SP-099). `Win32OverlayPresence` owns a plain `WS_POPUP` HWND, earns `Available` from eight OS round-trips, and refuses honestly on non-Windows. **You are its first consumer.**

## READ THIS FIRST — the residuals SP-099's final review left you

These are yours to answer, and they were written for this packet:

1. **How content reaches a raw HWND is undecided.** GDI/D2D painting, `UpdateLayeredWindow`, or attaching an Avalonia top-level. **The third answer would replace the HWND**, and `Confirm`/`ConfirmInputRouting` are private and bound to `_window`, so that route costs a refactor. Choose and defend it at the plan checkpoint.
2. **`Present` walks every top-level window** and can issue up to 64 round-trips. Correct at flash cadence, **wrong as a render loop**. Do not call it per frame.
3. **`IsPresenting` is a latch, not a live fact.** It records the last operation's outcome. Do not treat it as "still on screen".
4. **Every `Present`/`SetClickThrough` briefly makes the surface input-opaque** while it flips polarity to hit-test. Bounded, but real on a live desktop.
5. **D53: WPF re-asserts topmost about once a second** (`FlashService.cs:206-243`); the port does it on demand only, and `Raise()` is private. If sustained topmost matters for a visible flash, that is your problem to solve or to name.

## THE TRAPS, named at authoring

### 1. This packet's whole point is a pixel, so a headless frame cannot discharge it
Every prior packet honestly said "nothing is drawn". **You cannot.** The claim here is `presentation-verified` and the evidence is a **headed capture** — which the orchestrator runs, not you. Build so that a capture can be taken: a deterministic flash the harness can trigger and observe. **Say precisely what a capture would have to show to prove you right.**

### 2. Do not weaken the effect to make it drawable
Flash Images' pacing, pool and stop semantics are landed and reviewed. If drawing needs them changed, that is a finding to report, not a licence to edit them.

### 3. Do not let Windows-only drawing quietly become the contract
The overlay refuses on non-Windows. **The effect must still start, run and stop with no overlay** — that is what every Linux user gets, and SP-098's tests must keep passing unchanged. A flash with no surface is a flash nobody sees; it is not a crash and not a refusal to run.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Session/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-100-flash-draws/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Views/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-100-flash-draws/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Effects` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Views/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-100-flash-draws/record.md`, `spine-tasks/SP-100-flash-draws/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1191 unit / 81 headless). **The gate refuses stale builds — build first.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. At the plan checkpoint: your content route with its defence, how you avoid calling `Present` per frame, what happens with no overlay, and **exactly what a headed capture must show**.
2. Wire the effect's draw to the surface.
3. Keep every SP-098 fact passing unchanged. If one must change, that is a finding.
4. Prove the no-overlay path: the effect still runs and stops, nothing throws, nothing pretends.
5. **Prove it bites:** break the draw path and confirm a test reds.
6. Provide a deterministic way for a headed capture to trigger and observe a flash, and document the exact command.
7. Record divergences from D57 onward.

## Completion Criteria

- The effect draws to the overlay on Windows; the code path is exercised by a test.
- With no overlay the effect still runs and stops; SP-098's facts pass unchanged.
- A documented, deterministic trigger exists for the orchestrator's headed capture.
- Build 0 warnings / 0 errors.

## Do NOT

- Claim `presentation-verified` yourself — the capture is the orchestrator's.
- Call `Present` per frame.
- Treat `IsPresenting` as a live fact.
- Weaken SP-098's pacing, pool or stop semantics to make drawing easier.
- Introduce a wall-clock wait.

## Git Commit Convention

Conventional commit, `feat(SP-100): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md`, plus divergences in `client/docs/wpf-surface-reachability.md`.
