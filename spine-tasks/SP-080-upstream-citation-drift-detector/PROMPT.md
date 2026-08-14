# SP-080: Make upstream citation rot mechanically visible

## Mission

The port's parity claims are `File.cs:line` citations into the read-only WPF tree. When upstream rewrites one of those files the citation stops describing the code it points at, and **nothing in this repository notices**. The v6.8.0 sync's single most valuable finding (the `AiTextHygiene` transcript defect, now its own P1 row) was produced by intersecting 344 upstream-changed files against the port's citations **by hand**.

The data half already landed: `client/docs/upstream-citation-inventory.json` holds 297 cited WPF files keyed by real path, tiered, with a `verdict` per changed entry. **What is missing is the CHECK.** Your outcome is a mechanical detector under `client/tools/citations/` that regenerates the citation set, diffs it against the committed inventory, and emits a **REVIEW LIST**: a named, actionable set of rows for the next sync ledger.

It is a review list and **not a red test**. A changed upstream file is not automatically a defect, and a guard that cries wolf gets disabled. Exit non-zero only when the detector could not run honestly.

The premise was verified against the port tree before this packet was written, and it holds. Verification also turned up a live instance of the exact rot, which is your known-answer case (Step 1).

## Dependencies

SP-056 (`UpstreamPayloadInventoryTests`, landed) is the precedent for the guard shape and for its fixture pattern; it is not a dependency you edit. The inventory JSON is an input you read and never write. No product code is involved and no other lane in this wave touches `client/tools/`.

## Context to Read First

Every line below was opened and confirmed by the orchestrator at authoring. Nothing here is transcribed from the board.

- `client/docs/task-board.md:124`: your row. Read it in full, including the two named defects of the hand-rolled first version, which are your spec.
- `client/docs/upstream-citation-inventory.json`: `schemaVersion: 1`; `baseline.upstreamVersion: "v6.8.0"`, `baseline.merge: "db3e842f"`, `baseline.previous.merge: "42286638"` (both SHAs resolve in this checkout, verified); `ambiguousBasenames: ["MainWindow.xaml"]`; **297** entries, each `{ path, tier, citedBy[], changedAtSync, verdict }`. Tier split **81 / 146 / 70**; **106** entries carry a `changedAtSync`; **19** of the tier-1 entries changed. `citedBy` strings are prefixed: `src:src/CcpClient.Desktop/Ai/AiAwarenessService.cs` and `docs:startup-shutdown-contract.md`. **232 of the 297 entries are cited ONLY by `docs:` sources.**
- `client/docs/upstream-sync.md:104-112`: the two defects of the hand-rolled scan, in the sync's own words: it scanned `client/src/**` only and missed every citation in the port's authority documents where the LANDED contracts live, and its "already covered?" check was a basename `includes()` that matched rows from earlier syncs and reported coverage that did not exist.
- `client/docs/upstream-sync.md:145-146`: *"No dangling citations: zero cited files were deleted or renamed upstream, so every `File.cs:line` in the port still resolves to a real file. That was checked, not assumed."* **Four inventory paths do not resolve on disk today** (Step 1). The claim is defensible for the sync window and wrong as written; your detector is what makes that difference mechanical instead of rhetorical.
- `client/tests/CcpClient.Tests/UpstreamPayloadInventoryTests.cs:42`: `WebTreeParts = ["ConditioningControlPanel", "Resources", "web"]`. This is the proof that the only existing upstream guard is payload-tree-only and cannot see a `.cs` citation. `:98-114` is the repo-root walk anchored on `client/CcpClient.sln` (works in worktrees, where `.git` is a file), and `:540-586` is the temp-dir fixture-repo helper. Copy both ideas; do not import the file.
- `client/tests/floor/check-floor.mjs:80-107` and `:253`: the floor gate discovers only `tests/`-prefixed csproj entries from `client/CcpClient.sln` and runs them with `--no-build`. **Nothing under `client/tools/` is executed by the floor gate.** This is the constraint that shapes your whole verification story; see SCOPE PROBLEM.
- `client/tools/gate/with-slot.mjs:46-49`: the house style for node tooling in this repo: "Node 20+, zero npm dependencies, no shell", core modules only, identical on Windows and Linux. `client/tools/verify/self-test.ps1` is the precedent for a tool that carries its own self-test outside the suite.
- `docs/constitution.md:32`: `ConditioningControlPanel/` is read-only; the legacy WPF app is behavioral evidence and the first Avalonia attempt `CCP.*` is **lessons/failure evidence only**. This is not decoration here: it decides how your resolver must label a candidate path (Decision A).
- `ConditioningControlPanel/ConditioningControlPanel.csproj:10` and `:52`: the WPF csproj excludes `CCP.Core\**`, `CCP.Avalonia*\**`, `CCP.WindowsOnly\**`, `tests\**` from its own globs, and then takes `<ProjectReference Include="CCP.Core\CCP.Core.csproj" />`. So a file under `CCP.Core/` is compiled into the shipping product **and** sits in the lessons-only tree at the same time. Your detector reports that fact; it does not adjudicate it.
- `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:155-241`: the guards that parse **this PROMPT.md**: the `testCommand` row must route through the floor wrapper, the `floorDelta` row must name this packet's own delta file, and `fileScopeMustNotChange` must list the shared pin. Do not restructure the Contract table.

## File Scope

| | |
|---|---|
| May change | `client/tools/citations/**`, `spine-tasks/SP-080-upstream-citation-drift-detector/**` |
| Must not change | everything else, and specifically the files named in the contract below |

`client/tools/citations/` does not exist yet. You create it. Every line of detector logic and every one of its facts lives inside it.

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-080-upstream-citation-drift-detector/floor-delta.json` |
| fileScopeMustChange | `client/tools/citations/**` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-080-upstream-citation-drift-detector/record.md`, `spine-tasks/SP-080-upstream-citation-drift-detector/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** That file is the shared pin and concurrent lanes collide on it. Write your count change into `floor-delta.json` in your own folder instead:

```json
{ "packet": "SP-080-upstream-citation-drift-detector", "unit": 0, "headless": 0, "reason": "one line naming the facts you added" }
```

**Your scope forbids adding .NET tests, so your declared delta is `0` / `0`.** Declare it anyway. Omitting the file is not the same as declaring zero, and `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` enforces both halves and will fail your run if the row or the disclaimer is missing.

## Review Level: 2 (Plan, Final)

A bounded mechanical change: one read-only node tool, no product code, no concurrency, no privacy surface, no network, no user-visible path. Level 2 is the right level **only while that stays true**. If your work reaches for a product file, a test project, or the inventory JSON, you have left the packet; stop and escalate rather than promoting yourself.

## SCOPE PROBLEM

Named at authoring rather than discovered at review, because this project has hit this class three times.

**The floor gate cannot see your work.** `check-floor.mjs` discovers only csproj entries under `tests/` in `client/CcpClient.sln` and runs `dotnet test` on them (`:80-107`, `:253`). A node script under `client/tools/citations/` is invisible to it. Your assigned `fileScopeMustChange` is exactly `client/tools/citations/**`, and the wave's scopes were pre-assigned pairwise disjoint, so you **may not** add a `.cs` fact under `client/tests/**` to close that gap.

The work is still fully deliverable inside the scope. What is not deliverable inside the scope is a **standing** gate that runs your facts. So:

1. Your facts live in `client/tools/citations/self-test.mjs`, invoked by one command, exit 0 / non-zero. Precedent: `client/tools/verify/self-test.ps1`.
2. Your `record.md` must state **plainly and in its own section**, not in a footnote, that no standing gate in this repository runs the self-test, and that the detector can therefore rot exactly the way the citations it watches can.
3. Name the follow-up precisely so it is owed work rather than phantom debt: a single `.cs` fact in `client/tests/CcpClient.Tests/` that shells `node client/tools/citations/self-test.mjs` and asserts exit 0, which would put the detector on the floor for the cost of one test. **It is out of your scope.** Do not write it, do not stub it, do not leave a commented-out version. Write the sentence and stop.

Do not resolve this by wiring the detector into `check-floor.mjs` (out of scope, and it would convert a review list into a red gate, which the row forbids by name).

## Steps

### Step 1: Reproduce the premise and correct its numbers before building on them

Do this first, with executed commands, and record what you actually observed rather than what is written below.

1. **Confirm the absence.** `client/tools/citations/` does not exist and no script, test, or tool in this repository reads `upstream-citation-inventory.json`. Verified at authoring by a repo-wide grep over `*.mjs`, `*.js`, `*.cs`, `*.ps1`, `*.sh`, which returned nothing. Re-run it; if a detector has appeared in your worktree, stop and report.

2. **The known-answer case, verified at authoring.** Four inventory entries carry a `path` that does not exist on disk:

   | inventory `path` | tier | where the file actually is |
   |---|---|---|
   | `ConditioningControlPanel/Models/AppSettings.cs` | 1 | `ConditioningControlPanel/CCP.Core/Models/AppSettings.cs` |
   | `ConditioningControlPanel/Models/AiCommandData.cs` | 1 | `ConditioningControlPanel/CCP.Core/Models/AiCommandData.cs` |
   | `ConditioningControlPanel/Models/CompanionPromptSettings.cs` | 1 | `ConditioningControlPanel/CCP.Core/Models/CompanionPromptSettings.cs` |
   | `ConditioningControlPanel/Models/KeywordTrigger.cs` | 1 | `ConditioningControlPanel/CCP.Core/Models/KeywordTrigger.cs` |

   They moved in commit `adccc2e9` ("crosspatform v1 done"), which predates this sync window, which is why the ledger's "deleted or renamed **upstream**" phrasing survived. Worse, and the reason this is your first-run proof rather than a curiosity: the `AppSettings.cs` entry records `changedAtSync: { add: 441, del: 5 }`, and `git diff --numstat 42286638..db3e842f` reports `441 5` for **`ConditioningControlPanel/CCP.Core/Models/AppSettings.cs`** and nothing at all for the entry's own `path`. The recorded delta was computed against a path the entry does not name. Confirm all of this yourself before you rely on it.

3. **Reconcile the citation count.** The board row says "84 distinct WPF filenames are cited from `client/src/**` today". The inventory carries **65** entries with at least one `src:` citation. Both numbers are recorded facts and at most one is right. Your regenerator produces a third number: report it, and explain the gap against both. The likely explanation is the basename dedup the row already names as a defect, but **do not assert that without the delta list**. You may not edit the board or the ledger to correct either number; you state the correction in `record.md` and the orchestrator applies it at land.

### Step 2: Settle the pre-authorized decisions on your evidence

**All three rules below are PRE-AUTHORIZED BOTH WAYS. Resolve them from what you observe. Do not stall for the orchestrator.**

**Decision A: what an unresolvable inventory path becomes.**
- If the basename resolves at **exactly one** other real path under `ConditioningControlPanel/`, emit `UNRESOLVED (MOVED?)` naming the recorded path and the candidate, **and label the candidate's tree**: `shipping-wpf` when it is under `ConditioningControlPanel/` outside the `CCP.*` project folders, `ccp-first-attempt` when it is inside one of them. The label is mandatory because `docs/constitution.md:32` makes `CCP.*` lessons-only evidence, so a citation that silently re-points there has changed the *class* of evidence backing a landed claim. `ConditioningControlPanel/ConditioningControlPanel.csproj:52` means such a file is simultaneously compiled into the shipping product, which is precisely why this is a labelled report and not a decision.
- If it resolves at **zero** paths, emit `UNRESOLVED (VANISHED)`. If it resolves at **two or more**, emit `UNRESOLVED (AMBIGUOUS)` naming every candidate.
- In every branch: name, label, never pick, never rewrite the JSON.

**Decision B: whether the `DELTA-MISMATCH` class ships at all.** This class compares each changed entry's recorded `add`/`del` against `git diff --numstat` for that entry's own `path` across the recorded window. It is what exposed the `AppSettings.cs` inconsistency, so it is worth trying.
- Run it across all 106 changed entries first. **If it fires on a small, individually explicable set** (single digits, each one traceable to a moved or mis-keyed path), keep the class and list every hit in `record.md`.
- **If it fires broadly** (say above ten percent of changed entries), the recorded numbers were produced by a method you cannot reproduce, most likely rename-following or a different diff base. **Drop the class entirely**, record the observed fire rate and the reason, and ship the rest. A rule that fires on a tenth of the inventory is the cry-wolf shape the row forbids.
- Do not split the difference with a tolerance threshold or a suppression list. Ship it or drop it, and say which and why.

**Decision C: the change window.** Default to `baseline.previous.merge..baseline.merge` read out of the JSON (`42286638..db3e842f`; both resolve today, and `git diff --name-status` across them yields **344** paths under `ConditioningControlPanel/`, matching the ledger). Accept `--since <sha> --until <sha>` so the next sync can point it at a new window.
- If **either** SHA fails to resolve in the checkout, the detector **exits non-zero naming the missing SHA**. It must never print an empty or partial review list on a broken input. A detector that reports "nothing to review" because it could not read its own baseline is worse than no detector, and this repository has already paid for that class once.

### Step 3: Build the detector, inside your scope only

`client/tools/citations/` holds all of it. Node 20+, core modules only, zero npm dependencies, no lockfile, no shell string: invoke `git` through `execFileSync` with an argv array so it behaves identically on Windows and Linux. Normalize every path to forward slashes before comparing. Anchor the repo root by walking up to `client/CcpClient.sln`, the way `UpstreamPayloadInventoryTests.cs:98-114` does, so it works from a worktree.

**Regenerate.** Scan **both** citation sources: `client/src/**` code comments and `client/docs/**` authority documents. Emit `citedBy` strings in the committed prefix format (`src:src/...`, `docs:<basename>`) so the diff is comparable. Resolve every citation to a **real path** under `ConditioningControlPanel/`. Never collapse by basename; `ambiguousBasenames: ["MainWindow.xaml"]` is the live proof that collision is not hypothetical.

**Diff** the regenerated set against the committed inventory and emit the REVIEW LIST, grouped by class, each row naming the real path, the tier, the citing sources, and the action owed:

- `NEEDS-VERDICT`: a tier-1 entry changed in the window whose `verdict` is missing or empty.
- `NEW-CITATION`: a WPF path the port cites now that the inventory does not carry.
- `CITATION-GONE`: an inventory entry no port source cites any more.
- `UNRESOLVED`: per Decision A.
- `AMBIGUOUS`: a citation whose basename resolves to more than one real path, every candidate named, none chosen.
- `DELTA-MISMATCH`: per Decision B, if it survives.

**Exit contract, and it is the whole difference between a useful tool and a disabled one:** exit **0** when the detector ran and produced a review list, whether that list is empty or two hundred rows long. Exit **non-zero** only when it could not run honestly: inventory missing or unparseable, `ConditioningControlPanel/` absent, a baseline SHA that does not resolve, a repo root it cannot find. Print to stdout by default and write no file unless an explicit `--out <path>` is passed; a generated report committed into the tree is the next thing to rot.

### Step 4: Bind every detection rule with an independent revert

Facts live in `client/tools/citations/self-test.mjs` and run against **temp-dir fixture repositories**, never against today's real tree. This is not optional style: a self-test pinned to today's 297 entries goes red the day someone adds a citation, which is the day the detector is most needed. `UpstreamPayloadInventoryTests.cs:540-586` builds exactly this kind of fixture repo; copy the idea.

Every detection class needs a fixture that fires it and a negative control that does not. Then prove each one bites: **revert that single rule in the detector, run the self-test, record the red count, restore the tree byte-identically, and move to the next.** One source at a time, tree restored between reverts. A rule whose self-test still passes with the rule reverted is not a fact, and the record must say so rather than quietly dropping it.

Pin the exit contract itself with its own facts: a non-empty review list exits 0, an unparseable inventory exits non-zero, an unresolvable baseline SHA exits non-zero.

### Step 5: Run it against the real repository and record what it actually found

One honest run against this checkout, output captured verbatim into `record.md`. State the counts per class. Confirm the four `UNRESOLVED` rows from Step 1 appear, and state the number you observed rather than repeating four. If you observed a different number, that is a finding and it goes at the top of the record, not in a footnote.

The `VacuousShapeGuardTests` ledger (`client/tests/floor/vacuous-shape-ledger.json`) scans test sources only. You add no tests, so you owe it no entry. Do not go hunting for one.

### Step 6: Record

`record.md`: the Step 1 reproduction with your observed numbers, the branch each pre-authorized decision took and the evidence that selected it, the revert matrix with red counts, the verbatim real run, the SCOPE PROBLEM section required above, and an honesty section naming what is not proven. `floor-delta.json` with `0` / `0` and a reason line.

### Step 7: Verification

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```
```
node client/tools/citations/self-test.mjs
```

Run them as **separate commands**. The worktree isolation guard refuses compound shell commands (`cd X && ...`), so chain nothing.

**Build immediately before the gate even though you changed no C#.** The floor wrapper runs `dotnet test --no-build` (`check-floor.mjs:253`), so it reports whatever is in `bin/`; a stale tree once reported 1022 against a tree containing 1018.

Your floor run reports a total against a pin that is bumped at land from the summed deltas and never by you. **Your declared delta is 0 / 0, so the observed total must equal the pin exactly.** State both numbers in your report. If they differ, something in your tree moved the count and that is a finding to escalate, not a rounding error to explain away.

## Completion Criteria

- The premise reproduction in Step 1 is executed, and its three numbers (absence, the unresolved set, the citation count) are stated as observed.
- Each of Decisions A, B and C names the branch it took and the evidence that selected it.
- The detector regenerates from **both** `client/src/**` and `client/docs/**`, keys by real path, and never collapses by basename.
- The review list emits every class with path, tier, citing sources and owed action.
- Exit 0 on a non-empty list; non-zero only on an input the detector cannot honestly read.
- Every detection rule bites under its own independent revert, with red counts recorded.
- The real run is captured verbatim and its counts are stated.
- `record.md` carries the SCOPE PROBLEM section, naming the missing standing gate and the out-of-scope follow-up.
- `record.md` and `floor-delta.json` exist and are accurate.
- Build 0W/0E. Floor observed total equals the pin.

## Do NOT

- **Edit `client/docs/upstream-citation-inventory.json`, or anything else under `client/docs/`.** It is the baseline your diff is measured against. A lane that "fixes" the four unresolved paths, backfills a verdict, or re-keys an entry has made its own diff clean and proved nothing. Report; never repair.
- **Wire the detector into `client/tests/floor/check-floor.mjs`, or into any test that can red the suite.** The row is explicit: a changed upstream file is not automatically a defect, and a guard that cries wolf gets disabled. Out of scope besides.
- **De-duplicate by basename.** Named defect of the hand-rolled version; `ambiguousBasenames: ["MainWindow.xaml"]` is the live collision.
- **Scan `client/src/**` only.** The other named defect. 232 of the 297 entries are cited **only** by `client/docs/**`, which is where the LANDED and RATIFIED contracts live; a src-only scan is blind to 78 percent of the inventory and to every one of them.
- **Validate citation line numbers.** The row forbids widening there without evidence that it can be specified, and you will not have that evidence by Step 5.
- **Add an npm dependency, a `package.json`, or a lockfile.** House rule for node tooling in this repo (`with-slot.mjs:46-49`).
- **Invoke `git` or anything else through a shell string,** and add no retry-sleep loop around a subprocess. Argv arrays only, so Windows and Linux behave the same.
- **Pick a candidate when a basename is ambiguous, or treat a `CCP.*` path as the shipping WPF tree without labelling it.** Both erase the distinction `docs/constitution.md:32` exists to preserve.
- Edit `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tests/floor/vacuous-shape-ledger.json`, or anything under `client/docs/`, `.claude/`, `.spine/`, `.pi/`, or `ConditioningControlPanel/`.
- Add a `.cs` file anywhere, including the one-line floor bridge named in SCOPE PROBLEM. It belongs to another packet.
- Close, edit, or claim a neighbouring board row, including the tier-1 citation-review row and the `window-behavior-manifest.md` row that your review list will obviously touch. Your output is evidence for them, not a verdict on them.
- Commit a generated review-list file into the tree.
- Leave a TODO, a placeholder, or a partially wired detection class. A class you decided to drop is dropped and explained, not left half-built behind a flag.

## Git Commit Convention

Conventional commits, `feat(SP-080): ...`. One coherent slice, no unrelated files. Leave the tree buildable at every commit. Commit your own work on your branch; do not merge, do not land, and do not touch the shared pin.

## Documentation Requirements

Your findings will almost certainly owe text to `client/docs/upstream-sync.md`, whose §D "No dangling citations" claim at `:145-146` is contradicted as written by the four unresolved paths, and whose §F count of citations from `client/src/**` your regenerator will correct. They may also owe a hook in the `wpf-upstream-sync` skill, so the detector is invoked at the next sync instead of being rediscovered.

**Do not edit any of those.** `client/docs/**` and `.claude/**` are both outside your scope. Say in `record.md` which document is owed what, and quote the exact wording you believe is correct. Policy-touching text is applied by the orchestrator at land (SP-059 precedent; SP-071 and SP-072 both followed it).
