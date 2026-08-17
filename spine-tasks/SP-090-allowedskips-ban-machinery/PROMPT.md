# SP-090 — Make the two `allowedSkips` permanent bans mechanical instead of prose

## Mission

`client/tests/floor/floor.json` carries an `admissionRule` naming **two permanently banned test names** — names that may never appear in `allowedSkips` because a skip there would hide the exact defect the pin exists to catch. Both bans are **text**. Nothing enforces them.

The floor pin is the port's one mechanical pre-land gate, and its integrity rests entirely on `allowedSkips` being honest. A banned name entering that list would be a silent, permanent blinding of the gate — and the only thing standing in the way today is that a human reads a JSON string field and chooses to obey it.

Your outcome: **a guard that fails, naming the offending test, if either banned name ever appears in `allowedSkips` — and that also fails if the ban declaration itself is quietly removed.**

## Dependencies

SP-066 (which created `allowedSkips` and wrote the admission rule), SP-065 (the floor wrapper). Board row: "The two `allowedSkips` permanent bans are TEXT, not machinery", P2, OPEN. **You do not edit the board.**

## Context to Read First

Verified by the orchestrator at authoring, at HEAD `f579fbb6`:

- `client/tests/floor/floor.json` — `projects.CcpClient.Tests.allowedSkips` currently holds **7** fully-qualified names, each with a matching entry in `allowedSkipsMachineClasses`. **Neither banned name is present today**; this packet must keep it that way mechanically, not hopefully.
- The same file's `admissionRule` string names the two bans verbatim: **(1)** `CcpClient.Tests.DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault` — the SP-057 pin, whose skip means someone exported `CCP_DATA_ROOT` process-wide, which is the vacuous `896/1` green SP-062 closed; **(2)** `CcpClient.Tests.ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` — the named privacy flake guarding a route-classes-only logging boundary, which SP-085 has since fixed at source but which stays banned regardless.
- `client/tests/floor/check-floor.mjs:237` — the ONLY place the bans appear outside the JSON, and it is **an error-message string**, not a check. Read it and confirm that yourself; it is the whole reason this row exists.
- **No test enforces the bans.** Grep `allowedSkips` across `client/tests/CcpClient.Tests` and you find three files, and all three are tests whose own names are pinned — none reads the list to police it. Confirm this before you plan; if a fourth appears, say so.
- Precedent guards, for shape and message style — this suite has three and you should match them rather than invent a fourth idiom: `FloorWrapperGuardTests.cs` (parses packet files, fails closed on unparseable input, aggregates `file:line` violations), `DataRootChokePointGuardTests.cs` (repo-root walk, `FindRepoRoot` that throws rather than skips), and `ProcessEnvCollectionGuardTests.cs` (landed as SP-086 this run — the closest analogue, a convention made mechanical).

## THE TRAP THAT DECIDES THE DESIGN, named at authoring

The obvious implementation reads the banned names **out of `admissionRule`** and checks them against `allowedSkips`. **That guard is defeated by one edit.** Delete a name from `admissionRule`, add it to `allowedSkips`, and the guard passes with nothing to compare — the ban evaporates and the suite stays green. A guard whose own rule lives in the file it guards is not machinery, it is a longer piece of prose.

So the banned names must be **hard-coded in the test**, and the guard must ALSO assert that `admissionRule` still names both — so drift reds in **both** directions: a ban removed from the JSON reds, and a banned name added to `allowedSkips` reds. State in `record.md` why the redundancy is the point rather than duplication to be factored away.

## File Scope

| | |
|---|---|
| May change | `client/tests/CcpClient.Tests/` — ONE new guard file of your naming, and `spine-tasks/SP-090-allowedskips-ban-machinery/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json` (**you are guarding it, not editing it**), `client/tests/floor/check-floor.mjs`, `client/tests/floor/vacuous-shape-ledger.json`, `client/docs/task-board.md`, `client/src/**`, `client/docs/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-090-allowedskips-ban-machinery/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/tests/floor/check-floor.mjs`, `client/tests/floor/vacuous-shape-ledger.json`, `client/docs/task-board.md`, `client/src/**`, `client/docs/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-090-allowedskips-ban-machinery/record.md`, `spine-tasks/SP-090-allowedskips-ban-machinery/floor-delta.json` |

**READ THE PIN FROM `client/tests/floor/floor.json`**, never from this packet. Your gate reports `observed == pin + your declared delta` and exits non-zero on that drift; that is the designed state for a bound lane.

## Review Level: 2 (Plan, Code)

## Steps

1. Re-derive the three facts yourself and report what you measure: the current `allowedSkips` membership, that `check-floor.mjs:237` is a message and not a check, and that no test polices the list.
2. Write the guard. Hard-code both banned names; assert absence from `allowedSkips`; assert `admissionRule` still names both. Fail closed on anything you cannot parse — an unreadable `floor.json` is a violation, never a skip.
3. **Prove it bites, in both directions, one mutation at a time.** (a) Add a banned name to `allowedSkips` in a scratch copy → your guard must red, naming that test. (b) Delete a banned name from `admissionRule` → your guard must red. Restore byte-identically between mutations and verify (hash it). **Do not commit either mutation.**
4. Consider, and answer in `record.md`: should the guard also assert every `allowedSkips` entry has an `allowedSkipsMachineClasses` entry? The admission rule requires the machine class be named. If you add it, it is a third fact; if you decline, say why.

## Completion Criteria

- Both bans mechanically enforced, with the ban list hard-coded in the test rather than read from the file it guards.
- Both mutation directions red, with the tree restored byte-identically between them.
- `record.md` explains why the deliberate redundancy between the hard-coded list and `admissionRule` is the mechanism, not duplication.

## Do NOT

- Edit `client/tests/floor/floor.json` for any reason, including "to test the guard" — use a scratch copy and restore it.
- Read the banned names out of `admissionRule` as the guard's only source of truth.
- Add either banned name to `allowedSkips`.
- Weaken, quarantine, or skip your own guard on any platform. It reads JSON; it has no OS dependency.

## Git Commit Convention

Conventional commit, `test(SP-090): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md` only. The orchestrator writes the board and the digest at land.
