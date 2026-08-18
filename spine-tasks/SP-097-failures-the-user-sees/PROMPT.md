# SP-097 — Failures the user should see, and the paths nothing has ever run

## Mission

When something breaks past the entitlement gate, WPF **tells the user** and the port **does not**. That is a user-visible parity gap needing no owner decision.

WPF's `BtnStartChaos_Click` wraps the whole handler and shows a blocking `MessageBox` titled "Down the Rabbit Hole" reading "Couldn't start Down the Rabbit Hole:" plus the message (`MainWindow/MainWindow.Lab.cs:266-271`). The port catches only around `ResolveAsync`, and `Views/Pages/PlayPage.axaml.cs` fires `_ = dtrh.FallInAsync();` — so a throw from the descent becomes an **unobserved task exception**: logged, and invisible. `IntakePage` has the same shape.

Your outcome: **a failure past the gate reaches the user, and the paths that have never been executed get their first coverage.**

## What is owed, in priority order

1. **The failure surface.** A throw anywhere in the launch flow must produce something the user sees, on both the DTRH and Intake pages. WPF uses a modal dialog; **the port's shell already has an honest in-page band idiom** (the refusal plate from SP-094/SP-095). Reusing it is likely better than importing a modal, but decide and record — this is board row P3.
2. **`RequestApplicationExit` has no test.** It is the action behind the tray menu's `Exit`, the one menu item whose effect is unproven end to end. The no-lifetime branch is assertable headlessly and was left unasserted.
3. **The `catch` -> `Unavailable(tier-authority-fault)` fallback** on the DTRH user path (`Features/Dtrh/DtrhLaunch.cs`) is exercised by nothing. A throwing authority seam closes it cheaply.
4. **`IntakePage`'s `RefusedSpent` and `RefusedNeedsAccount` render arms** are unreachable in this build and proved only at the pure-gate layer. Drive them through the page with an injected gate result.
5. **`RestoreOwner`** — SP-096 covered the duck; confirm the restore side has a fact too, and add one if not.

## THE TRAP, named at authoring

**Do not turn an unobserved exception into a swallowed one.** The defect is that a failure is invisible, not that it is unhandled. A `catch` that renders a band and drops the detail is the same bug wearing a nicer coat: the diagnostic must still carry the exception type, and the user-facing text must say something happened rather than pretending nothing did.

Second: **the refusal band already means "we could not determine your entitlement".** A failure band that looks identical teaches the user that both mean the same thing. **Make a failure visibly distinct from a refusal**, or say why you did not.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/src/CcpClient.Desktop/Features/Intake/**`, `client/src/CcpClient.Desktop/Views/**`, `client/src/CcpClient.Desktop/Tray/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-097-failures-the-user-sees/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**`Entitlement/**` is read-only for you.** Nothing here changes who is entitled; this is about what happens when the machinery breaks.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-097-failures-the-user-sees/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-097-failures-the-user-sees/record.md`, `spine-tasks/SP-097-failures-the-user-sees/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`** (1117 unit / 62 headless). **The gate refuses to run against a stale build — build the solution first.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. Report at the plan checkpoint: your failure surface (band or dialog), how it differs visibly from a refusal, and which of items 2-5 you are taking.
2. Implement the failure surface on both pages. The diagnostic keeps the exception type; the user-facing text says a failure occurred.
3. Close items 2-5, each with a fact that fails if the path regresses.
4. **Prove it bites:** make the launch throw and confirm a test reds because the user saw nothing. Restore byte-identically; do not commit the mutation.
5. Record divergences from D40 onward.

## Completion Criteria

- A throw past the gate produces something the user sees, on both pages, visibly distinct from a refusal.
- `RequestApplicationExit`, the `tier-authority-fault` fallback, both unreachable render arms, and `RestoreOwner` each have a fact.
- No exception detail is swallowed; no test skipped.
- Build 0 warnings / 0 errors.

## Do NOT

- Swallow the exception to make the band appear.
- Make a failure indistinguishable from a refusal.
- Touch `Entitlement/**` or `client/tools/**`.
- Introduce a wall-clock wait. Use `TestWait`.
- Claim `presentation-verified`; the headed capture is the orchestrator's.

## Git Commit Convention

Conventional commit, `feat(SP-097): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md`, plus divergence entries in `client/docs/wpf-surface-reachability.md`.
