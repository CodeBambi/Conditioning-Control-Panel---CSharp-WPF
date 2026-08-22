# SP-141 — Delete the old Avalonia port by restoring the WPF tree to `main`, and re-anchor the citations that move

## Mission

**The owner asked for the old Avalonia port to be removed so only `client/` remains. It is removable,
it does not touch the shipping product, and the whole operation has already been measured.**

`docs/constitution.md` and the `wpf-upstream-sync` skill both say `ConditioningControlPanel/**` is
READ-ONLY archaeology that must track `main` **exactly**. It does not: `git diff --stat main HEAD --
ConditioningControlPanel/` is **1180 files**.

- **1032 are the `CCP.*` first-attempt tree.** `main` contains no `CCP.*` directory at all, and its
  shipping csproj has no `CCP.Core` reference — the `ProjectReference` at
  `ConditioningControlPanel.csproj:52` exists only on this branch.
- **~90 are committed build and debug residue**: 30+ `smoke_run*.log`, `bench-run-*.log`,
  `trace-active.nettrace`/`.etlx`/`.speedscope.json`, `msbuild.binlog`, `ccp-run.log`,
  `wpf-launch.log`, `perf-*.json`, `inspect_temp.cs`, `__pycache__/`, and a NuGet cache under
  `.tools/.store/`.
- **~25 are real WPF source** the `CCP.Core` model extraction edited, and **39 are docs**.

Your outcome: **`ConditioningControlPanel/` matches `main` byte for byte, and the two guards that
notice are re-anchored rather than silenced.**

## This was measured before it was asked for. Reproduce it, do not re-litigate it

Restoring the tree to `main`:

- removes all 1032 `CCP.*` files and the residue;
- makes `dotnet build ConditioningControlPanel.sln` produce **main's exact result — 332 warnings,
  1 error**, that error being `NETSDK1151` in the TESTS project (a self-contained exe referenced by a
  non-self-contained test project), which `main` has too and which is **not yours to fix**;
- leaves `client/` building **0 warnings / 0 errors**;
- leaves the four linked payload trees intact at **1542 dtrh / 2138 intake / 9 tunnel / 9 vendor**.

**Verify each of those yourself.** If any disagrees with this packet, stop and report — a premise that
has gone stale is worth more than the task.

## THE COST, AND IT IS THE WHOLE PACKET

Exactly **two** guards go red beyond the standing environmental family:

- `FypCensusTests.TheConsumerSetIsRederivedFromTheShippingBytes_SoTheSurfacesReachCannotDriftUnnoticed`
- `GoonGameCensusTests.EveryPinnedCitation_IsOnTheExactLineItClaims`

Both READ the WPF tree and check that each pinned citation is on the **exact line it claims**. They go
red because **the port's citations are anchored to this branch's diverged copy rather than to the
shipping bytes** — which is the same defect the track-main rule exists to prevent, stated sharply.

**FIX THE DOCUMENTS, NEVER THE TESTS.** Re-derive the line numbers in `client/docs/fyp-census.md` and
`client/docs/goon-game-census.md` against `main`'s tree. `client/tests/**` is **must-not-change**:
these two guards are the only mechanism that noticed, and weakening either to get green would destroy
the evidence that this cleanup was safe. If a citation's referent no longer exists on `main` at all,
that is a FINDING — report it, do not invent a nearby line.

## The other traps

### 1. Do not "improve" the WPF tree while restoring it
It is archaeology. It must equal `main`, not a tidied version of `main`. If `main` contains something
ugly, it stays. `git diff main HEAD -- ConditioningControlPanel/` must be **empty** when you are done.

### 2. The client links payloads out of this tree
`client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` globs `dtrh`, `intake`, `tunnel` and `vendor`
read-only out of `ConditioningControlPanel/Resources/web/`. They exist on `main` at the counts above.
**Check the counts after the restore**, because a silent payload loss would surface much later as a
404 in a page nobody loads in a test.

### 3. Other citations may move too
The two census guards are what the FLOOR catches. `client/tools/citations/detect.mjs` scans
`client/src/**` and `client/docs/**` into `ConditioningControlPanel/**` and is **not** on the floor.
**Run it after the restore and report what it says**, even though it gates nothing. A count is fine;
fixing everything it finds is not this packet.

### 4. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Divergence rows carry exactly five unescaped pipes:
escape `|` inside code spans as `\|`, and **verify by counting delimiters, not by reading.**

### 5. Divergence ids: **D326 onward**

## File Scope

| | |
|---|---|
| May change | `ConditioningControlPanel/**` (restore to `main` ONLY), `client/docs/fyp-census.md`, `client/docs/goon-game-census.md`, `client/docs/wpf-surface-reachability.md` (divergences ONLY, D326 onward), and `spine-tasks/SP-141-restore-wpf-tree/**` |
| Must not change | everything else, and specifically **`client/tests/**` in its entirety** (both census guards included), `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-141-restore-wpf-tree/floor-delta.json` |
| fileScopeMustChange | `client/docs/goon-game-census.md` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/tests/CcpClient.Tests/FypCensusTests.cs`, `client/tests/CcpClient.Tests/GoonGameCensusTests.cs`, `client/src/**`, `client/tools/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-141-restore-wpf-tree/record.md`, `spine-tasks/SP-141-restore-wpf-tree/plan.md`, `spine-tasks/SP-141-restore-wpf-tree/floor-delta.json` |

**Pin: 2622 unit / 152 headless.** Expect a floor delta of **0** — you are deleting and re-anchoring,
not adding tests. `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** how you will restore exactly (and prove `git diff main HEAD --
   ConditioningControlPanel/` is empty); how many citations move in each census document and how you
   will re-derive them; and what you will do if a referent is gone from `main` entirely.
2. Restore the tree.
3. Re-anchor the two census documents. **Never the tests.**
4. Verify: shipping solution 332W/1E, `client/` 0W/0E, payloads 1542/2138/9/9, floor at the pin.
5. Run `detect.mjs` and report its count.
6. Divergences **D326 onward**.

## Completion Criteria

- `git diff main HEAD -- ConditioningControlPanel/` is EMPTY.
- Both census guards green, with the DOCUMENTS re-derived and the TESTS byte-identical.
- `client/` builds 0/0; payload counts unchanged; floor at 2622/152.
- The shipping solution's residual error is main's `NETSDK1151` and nothing else.
- `detect.mjs` output reported, whatever it says.

## Do NOT

- Edit either census TEST, or any file under `client/tests/**`.
- Invent a nearby line for a citation whose referent is gone. Report it.
- Tidy the WPF tree while restoring it.
- Fix `NETSDK1151` — `main` has it too.
- Use a divergence id below D326.

## Git Commit Convention

Conventional commit, `refactor(SP-141): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the restore proof, the per-document citation counts re-derived, any referent gone
from `main`, the four verification results, `detect.mjs`'s count, and the before/after failure sets.
