# SP-089 — Give the real foreground-title capture path a guard that survives a locked session

## Mission

SP-082 made the two window-title facts skip on the product's own typed `no-foreground-window` answer. That was correct and is not in question. It bought a reproducible floor gate at a price its own final review named: **`no-foreground-window` is the product's answer to "capture returned false", not to "the session is locked".** Those coincide today and diverge the instant the capture path breaks.

The exposure is total, not partial. `client/tests/CcpClient.Tests/AiAwarenessTests.cs:415` is the **only** execution of the real capture path anywhere in `client/tests`, and it now sits inside a fact that skips. So a broken P/Invoke, a renamed entry point, a CharSet regression or an always-false return is caught by **nothing**: not the gate (the may-skip pin accepts it), not `VacuousShapeGuardTests` (`dynamic-skip` either way). Before SP-082 that class reddened the gate on every interactive run.

Your outcome: **a fact that exercises the real Win32 capture path and still executes when there is no foreground window, so a capture-path regression reds something on any machine.** What you may not deliver is a fact that only runs on an interactive desktop — that is the hole, not the fix.

## Dependencies

SP-082 (landed, the conditional skip). Board row: "SP-082 left the real foreground-title capture path guarded by nothing on any machine", P1, OPEN. **You do not edit the board.**

## Context to Read First

Every line below was opened in the port tree by the orchestrator at authoring and re-verified at HEAD `2669d0c8`, not transcribed:

- `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs:335-359` — `TryCaptureForegroundTitle`. `:338-341` returns false off-Windows. `:343-347` `GetForegroundWindow()`; **zero → return false, and this is the ONLY in-product route to a false return on Windows.** `:349-353` `GetWindowTextLengthW`; `length <= 0` returns **true** with an empty title (an empty title is a successful capture — SP-068 settled this deliberately). `:355-358` `GetWindowTextW` into a `StringBuilder`, then `return true`.
- `:361-371` — `private static class NativeMethods` with three `[DllImport("user32.dll")]` declarations: `GetForegroundWindow`, `GetWindowTextLengthW` (`CharSet.Unicode`), `GetWindowTextW` (`CharSet.Unicode`).
- `:311-320` — `Probe`. On Windows it is a single ternary on `TryCaptureForegroundTitle`; the false arm is the only producer of `NoForegroundWindowCode`.
- **The whole reference set, grep-verified at HEAD — four hits across `client/`:** `AiAwarenessService.cs:315` (probe), `:335` (definition), `:579` (observation), and `client/tests/CcpClient.Tests/AiAwarenessTests.cs:415`. Confirm this yourself before you plan; if a fifth appears, say so.
- **THE STRUCTURAL FACT YOUR DESIGN TURNS ON:** `GetWindowTextLengthW` and `GetWindowTextW` are reached **only past the zero-handle check**. On a locked or disconnected session `GetForegroundWindow` returns zero, so **two of the three P/Invokes never execute at all** — their marshalling is unexercised precisely when the machine is in the state unattended runs happen in.
- `client/tests/CcpClient.Tests/AiAwarenessTests.cs:386-436` — the two SP-082 facts and their `Assert.Skip` predicates. **Read them; do not duplicate their shape, and do not modify their skip conditions.**
- **Two reachability constraints, both verified, that bound your options:** there is **no `InternalsVisibleTo`** anywhere in `client/src`, and there is **no `DllImport` or `LibraryImport` anywhere in `client/tests`** — so `NativeMethods` is unreachable from a test today and the suite has no precedent for declaring its own.

## THE DECISION, PRE-AUTHORIZED BOTH WAYS — resolve it on evidence in Step 1, do not stall

The capture path must become drivable with a handle the TEST controls, because a handle the OS controls is exactly what disappears on a locked session.

- **Branch A (expected): extract a handle-taking seam in the product.** Add an overload — shape suggested, not mandated — `TryCaptureWindowTitle(IntPtr hwnd, out string title)` containing the existing length-and-text half verbatim, and have `TryCaptureForegroundTitle` call it after its own zero-check. **This must be a pure extraction: no behaviour may change on any path**, including the `length <= 0 → true` case. A test then drives it with `GetDesktopWindow()`, which returns a valid handle on a locked session, so the marshalling of both text P/Invokes executes on every machine class.
- **Branch B: prove the declarations bind without a product change.** If you can red a broken entry point, a renamed export or a CharSet regression from the test assembly alone, take it and skip the product edit entirely. Declaring the test's own `user32` imports does NOT qualify by itself — that tests Win32, not this product's declarations, and it would pass with `NativeMethods` deleted.

State which branch your evidence selects and why. **A branch that cannot red a broken declaration is not a branch, it is a decoration.**

## THE VACUITY TRAP, NAMED AT AUTHORING

The failure mode here is a fact that executes and proves nothing. Your fact must red when the capture path breaks, and you must SHOW that by breaking it: at minimum, revert the `CharSet.Unicode` on `GetWindowTextW`, or rename an entry point, and record which facts red. **A fact that stays green under a mutated P/Invoke declaration has not guarded the capture path** — it has re-tested that Windows has a desktop window.

Equally: the new fact must NOT depend on a foreground window existing. If it would skip or fail on a locked session, it reproduces the exact hole this packet exists to close.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs` (Branch A only, and only the extraction described above), `client/tests/CcpClient.Tests/AiAwarenessTests.cs`, `spine-tasks/SP-089-capture-path-regression-guard/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tests/floor/vacuous-shape-ledger.json`, `client/docs/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |

**If your fact adds a `dynamic-skip`, `platform-predicate` or any other detected shape to an existing ledger entry, STOP and report it** — `vacuous-shape-ledger.json` is orchestrator-owned and out of your scope. Prefer a fact that carries no detected shape at all.

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-089-capture-path-regression-guard/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/AiAwarenessTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tests/floor/vacuous-shape-ledger.json`, `client/docs/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-089-capture-path-regression-guard/record.md`, `spine-tasks/SP-089-capture-path-regression-guard/floor-delta.json` |

Pin note: **READ THE PIN FROM `client/tests/floor/floor.json`, never from this packet.** Your gate will report `observed == pin + your declared delta` and exit non-zero on that drift; that is the designed state for a bound lane.

## Review Level: 3 (Plan, Code, Final)

Level 3 because this touches a P/Invoke boundary, because the packet's whole value is whether one new fact can bite, and because the row it closes was created by the previous wave's own fix.

## Steps

1. **Re-derive the reference set and the two reachability constraints yourself.** Report the counts you measure, not the ones above. Then select and state your branch.
2. Implement the smallest thing that makes the capture path drivable with a test-controlled handle. Under Branch A the product edit is a **pure extraction** — prove it by showing the moved lines are byte-identical apart from the signature.
3. Add the fact. It must execute on a locked session as well as an interactive one.
4. **Break the capture path on purpose, at least twice, one mutation at a time** — a `CharSet` revert and an entry-point rename are the two named — and record which facts red under each. Restore the tree byte-identically between mutations and verify that (hash it).
5. **State plainly what is still unguarded.** At minimum: whether an always-zero `GetForegroundWindow` is distinguishable from a locked session by your fact (it is probably not, and saying so is the honest outcome), and that no headed gate is discharged.

## Completion Criteria

- A fact that exercises the real text-reading P/Invokes and does **not** require a foreground window.
- At least two independent mutations of the declarations each red a named fact, with the tree restored byte-identically between them.
- Under Branch A, the product change is a pure extraction with no behavioural delta on any path, shown rather than asserted.
- `record.md` carries the branch decision, the mutation matrix with red counts, and an honesty section naming what remains unguarded.

## Do NOT

- Change the SP-082 skip predicates, or add a skip to your new fact.
- Add `InternalsVisibleTo`, reflection over `NativeMethods`, or a test-side `DllImport` that would pass with the product's declarations deleted.
- Change any behaviour of `TryCaptureForegroundTitle`, including the `length <= 0 → true` case.
- Edit `client/tests/floor/floor.json` or `client/tests/floor/vacuous-shape-ledger.json`.
- Claim the locked-session leg verified. This machine may be interactive when you run; if it is, say so and name the manual gate instead.

## Git Commit Convention

Conventional commit, `feat(SP-089): ...` or `test(SP-089): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md` only. The orchestrator writes the board and the digest at land.
