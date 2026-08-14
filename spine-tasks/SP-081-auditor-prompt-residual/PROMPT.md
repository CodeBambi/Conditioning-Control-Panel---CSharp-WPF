# SP-081: T-17's residual, the blind auditor actually run

## Mission

T-17 is half discharged. SP-066 landed the prompt edit: `client/tools/port-audit-prompt.md` now invokes the floor wrapper instead of re-deriving the floor itself, and `FloorWrapperGuardTests.AuditorPrompt_InvokesTheFloorWrapper_NeverBareDotnetTest` pins that text mechanically. The board row was explicit that this does **not** close the row.

**The residual is the row's other half: an audit RUN.** The blind auditor must execute the current prompt end to end against a tree with an induced skip (must FAIL, naming the wrapper's reason) and against a clean tree (must PASS). Nothing in the repository proves that today. `.port/` does not exist in this checkout, so under the Claude Code engine the auditor has never run once.

Your outcome: **both runs performed, both transcripts committed as evidence, and the prompt corrected wherever those runs prove it wrong.** The corrections are bounded and every branch is pre-authorized below. What you may not deliver is a third thing: an auditor that is easier to pass.

Three premise corrections found at authoring, stated so you do not re-derive them:

1. The row says the prompt is "READ BY TWO TESTS". **It is read by one.** `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:251` is the only place in either test project that opens this file; the class's other two facts read `spine-tasks/*/PROMPT.md` and never touch it. The rule the note encodes (editing this file is a code change, never a docs drive-by) still holds, through one fact rather than two.
2. The row's `.gitignore` caution is stale. The bare `tools/` rule was root-anchored to `/tools/` on 2026-08-14 (`.gitignore:185-191`), and `git check-ignore -v client/tools/port-audit-prompt.md` now returns nothing. The file is tracked and unignored. The SP-066-era ban on creating new files under `client/tools/` no longer has a mechanism behind it.
3. **The prompt is one revision newer than the text the row describes.** Commit `42b2992c` (engine retirement, 2026-08-14) edited it after SP-066: it added the sixth step-3 check (pre-wave SHA, unclaimed commits), rewrote the repository-root line to "the directory you were started in", added the context-independence paragraph, and changed the `CCP_DATA_ROOT` citation from `port-workflow.md:204` to `port-workflow.md, Unattended loop`. **You are running the current file, not the SP-066 file**, and that sixth check has never been executed either.

## Dependencies

SP-065 (the wrapper, `client/tests/floor/check-floor.mjs`) and SP-066 (the prompt edit plus its guard fact) are landed. Commit `42b2992c` re-edited the prompt afterwards. None of the three blocks you. No other lane in this wave owns `client/tools/`.

## Context to Read First

Verified by the orchestrator at authoring. Every line below was opened and confirmed in the port tree, not transcribed from the board:

- `client/tools/port-audit-prompt.md:15-20`: step 2's fenced block, `dotnet build client/CcpClient.sln -c Debug --nologo` then `node client/tests/floor/check-floor.mjs`, in that order and nothing else.
- `:22-28`: the wrapper paragraph. `:25` is the load-bearing sentence, "A non-zero exit is an audit FAIL, name the wrapper's reason verbatim". `:26-28` carries the never-set-`CCP_DATA_ROOT` note.
- `:30-42`: step 3's six checks. `:32-33` requires the `FLOOR OK` totals to match the digest claim EXACTLY. `:34-36` states the skip check as "exactly the names pinned in `allowedSkips`". `:37` requires `git status --short` empty. `:38` requires HEAD to equal `origin/feat/crossplatform`. `:39-42` is the newest check, unclaimed commits in the pre-wave range.
- `:44-46` the FAIL rule, `:48-51` the verdict format: the last line must be `VERDICT: PASS` or `VERDICT: FAIL - <one line naming the check, the claimed value and the observed value>`.
- `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:244-261`: the only fact that reads the prompt. It asserts exactly three things: the literal substring `node client/tests/floor/check-floor.mjs` is present, the literal `CCP_DATA_ROOT` is present, and the regex at `:55-56` (`\bdotnet\s+test\b`, case-insensitive, applied to the WHOLE file) does not match. **Everything else in that prompt is unpinned prose.**
- `client/tools/port-loop.ps1:310-319` `Get-AuditArgs`: the exact blind invocation, `-p --safe-mode --model <AuditModel> --permission-mode <PermissionMode> --no-session-persistence --tools Bash,Read,Grep,Glob`, prompt delivered on **stdin** as a byte-exact file handle (`:321-325`), fresh process, no persisted session. `:129` defaults `AuditModel` to `sonnet`. `:362` refuses to start when the audit model equals the phase model, in the script's own words because "the blind audit would be self-certification".
- `client/tests/floor/check-floor.mjs`: `runProject` invokes the runner with `--no-build`; `main()` fails with a runner-level message naming the non-zero exit ("... exited 1 for CcpClient.Tests, runner-level failure") **before** any TRX check, so a red suite and an unexpected skip are distinguishable by the wrapper's own wording; `verifyProjectResults` fails with `FLOOR VIOLATION - unexpected skip: <name>` for a `NotExecuted` result absent from `allowedSkips`, and with `FLOOR VIOLATION - total drift` on a count change in either direction.
- `client/tests/floor/floor.json`: pin `CcpClient.Tests` total **1018** with five named `allowedSkips`, `CcpClient.HeadlessTests` total **35** with none. The two permanently banned names are in `admissionRule`.
- `client/tests/CcpClient.Tests/VacuousShapeDetector.cs:188-190` and `:233`: any `Assert.Skip*` token classifies the enclosing fact as shape `dynamic-skip`. `VacuousShapeGuardTests.cs:64-99` then fails on a detected site with no ledger entry. **This is why the obvious way to induce a skip does not work; see Step 2.**
- `client/docs/port-workflow.md`, Verification floor: "Always build immediately before the gate, in the same tree", with the dated wave-30 observation that the gate reported **1022 against a source tree containing 1018** after a reset left stale build output in place.

## File Scope

| | |
|---|---|
| May change | `client/tools/port-audit-prompt.md`, `spine-tasks/SP-081-auditor-prompt-residual/**` |
| Must not change | everything else, and specifically the files named in the contract below |

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-081-auditor-prompt-residual/floor-delta.json` |
| fileScopeMustChange | `client/tools/port-audit-prompt.md` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-081-auditor-prompt-residual/record.md`, `spine-tasks/SP-081-auditor-prompt-residual/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** That file is the shared pin and concurrent lanes collide on it. Write your count change into `floor-delta.json` in your own folder instead:

```json
{ "packet": "SP-081-auditor-prompt-residual", "unit": 0, "headless": 0, "reason": "no tests added; evidence-only packet plus a prose correction to the auditor prompt" }
```

This packet adds no tests, so `0`/`0` is the expected content, but declare it explicitly: omitting the file is not the same as declaring zero. `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` enforces both halves of this and will fail your run if the row or the disclaimer is missing.

## Review Level: 2 (Plan, Final)

Level 2, not 3, and the reasoning is recorded so a reviewer can disagree with it on the record. Nothing here touches `client/src/**`, concurrency, a privacy boundary, or a user-visible path. The deliverable is two executed runs plus a bounded prose correction to a single tooling document. The counter-argument is real, that document gates every land, so a weakened auditor is high consequence; it is answered by mechanism rather than by review attention: the guard fact still pins the three load-bearing strings, and every weakening this packet could plausibly reach for is forbidden by name below.

## Steps

### Step 1: Establish the two trees, and never do the induction in your own worktree

Both runs happen in **scratch worktrees created outside your lane worktree** and detached, so your own tracked diff never contains an induced defect:

- a CLEAN tree at exactly the commit your lane is based on, untouched;
- an INDUCED-SKIP tree at the same commit, modified per Step 2 and never committed.

Record the base SHA in `record.md`. Remove both worktrees at the end with `git worktree remove` and show that your lane's `git status --short` lists only File Scope paths.

Confirm `claude` is on PATH before you start. If it is not, or if the invocation in Step 3 cannot be made faithfully, **STOP and report**. Do not simulate the auditor, and do not read the prompt and render your own verdict: you are the session that produced the work, and `port-loop.ps1:362` calls exactly that self-certification.

### Step 2: Induce the skip so that the wrapper's ONLY failure is the unexpected skip

This is the step that decides whether the run proves anything, and the obvious method is a trap.

`Assert.Skip*` in an un-ledgered fact is classified `dynamic-skip` by `VacuousShapeDetector.cs:188-190`, so `VacuousShapeGuardTests` goes RED, the runner exits non-zero, and `check-floor.mjs` reports a **runner-level failure** instead of `FLOOR VIOLATION - unexpected skip: <name>`. The auditor would then FAIL for the wrong reason and the run would prove the wrong thing. That is the vacuous-evidence class this project has hit repeatedly; do not walk into it.

**Method A (primary):** put `[Fact(Skip = "SP-081 induced")]` on one ordinary fact in `CcpClient.Tests` that is not in `allowedSkips` and is neither of the two permanently banned names. The attribute carries no `Assert.Skip` token, so the detector does not see it and the ledger stays consistent. Then **build**, and read the TRX yourself: the test must appear once as `outcome="NotExecuted"` carrying its own name, the result count must be unchanged, and the wrapper's single reported failure must be the unexpected skip naming that test.

**Method B (fallback, pre-authorized):** if Method A does not produce a `NotExecuted` result row on this runner stack, use `Assert.Skip(...)` at the top of the same fact **and** add the matching entry to `client/tests/floor/vacuous-shape-ledger.json` in the scratch tree only, so the shape guard stays green. Re-verify the single-failure condition.

**If neither method yields exactly one failure and that failure is the named unexpected skip, STOP and report.** A contaminated induction does not discharge this half, and reporting that honestly is worth more than a FAIL verdict with the wrong cause.

Build immediately before the wrapper, in the same tree, every time. The wrapper does not rebuild; a skip you induced but did not compile is not in the binaries the wrapper measures.

### Step 3: Run the blind auditor twice, faithfully

Reproduce `Get-AuditArgs` exactly, from each scratch worktree as the working directory, with `client/tools/port-audit-prompt.md` delivered on **stdin**, the audit model `sonnet` (it must differ from yours), and stdout captured to:

- `spine-tasks/SP-081-auditor-prompt-residual/evidence/audit-clean.log`
- `spine-tasks/SP-081-auditor-prompt-residual/evidence/audit-induced-skip.log`

Each run builds and runs the full suite, so it will exceed a single foreground shell timeout. **Run it in the background against a log file and wait for it to exit.** Do not kill it early; the loop's own budget is 60 minutes per audit. Parse the verdict the way `port-loop.ps1:578-596` does: the LAST line matching `^\s*VERDICT:`, with PASS requiring exactly `VERDICT: PASS`.

### Step 4: Resolve both verdicts against the pre-authorized rules

**THESE RULES ARE PRE-AUTHORIZED BOTH WAYS. Resolve them on your evidence; do not ask.**

**Induced-skip run, expected FAIL.**

- **Discharged** when the last line is `VERDICT: FAIL` *and* the transcript shows the auditor ran the wrapper and reported its reason, naming the skipped test. The induction also makes `git status --short` non-empty by construction, so the auditor will legitimately flag that check too; that is expected and does not by itself contaminate the result.
- **NOT discharged, and it is a defect in the prompt**, if the auditor short-circuits on the dirty tree and never runs step 2, or runs it and never reports the floor reason. In that case the fix is in your file scope: make step 2 unconditional and make the FAIL line carry the wrapper's reason, then re-run and record both attempts. Do not paper over it in `record.md`.
- **NOT discharged** if the FAIL is caused by anything other than the induced skip. Return to Step 2.

**Clean run, expected PASS.**

- **P1, `VERDICT: PASS`:** discharged. Quote the `FLOOR OK` line and the pinned skip names the auditor observed.
- **P2, FAIL on a floor check** (wrapper non-zero, or `FLOOR OK` totals not matching the digest claim): that is a real finding about the tree, not about T-17. **Do not fix the tree, do not touch the pin, do not edit the digest, do not re-run hoping for a different number.** Record the verdict verbatim, state that the clean-tree half is NOT discharged, and name observed against pin against digest claim for the land.
- **P3, FAIL only on a non-floor check** (HEAD behind `origin/feat/crossplatform`, an unclaimed commit in the pre-wave range, a digest entry describing an earlier wave): the floor half still passed. Record the wrapper's `FLOOR OK` line and the auditor's own words, then state plainly that a `VERDICT: PASS` was not obtainable on this tip and why. The row's residual then narrows to exactly "a clean-tree PASS on a reconciled, pushed tip" and you say so instead of claiming closure. **Do not manufacture a PASS** by fetching, resetting, pushing, or editing any document the auditor reads.

### Step 5: Correct the prompt, and only where the runs prove it wrong

**E1, required.** Step 2 of the prompt currently lists the build and the wrapper without saying they are inseparable. Add the clause: the two commands run adjacent, in the same checkout, with nothing in between, because the wrapper does not rebuild and a count taken from stale binaries is evidence about `bin/`, not about the tree. Cite the dated observation, 1022 reported against a tree containing 1018 (2026-08-14). Your own Step 2 is the demonstration: the induced skip is invisible to the wrapper until it is compiled.

**Write this clause without the token that the guard forbids.** `FloorWrapperGuardTests.cs:55-56` matches `\bdotnet\s+test\b` anywhere in the file, so naming the runner invocation verbatim turns the guard RED. Phrase around it, for example "the wrapper invokes the runner with `--no-build`".

**E2, conditional, both branches pre-authorized.** The verdict template at `:51` demands "the claimed value and the observed value", and a floor FAIL caused by an unexpected skip has no numeric pair to report, only a name. If your induced-skip run shows the auditor could not express the wrapper's reason in that shape, widen the template to admit a named reason with no numeric pair, keeping the single-line and `VERDICT: FAIL` prefix contract the loop parses. If the run shows the auditor produced a well-formed FAIL naming the skip under the current wording, **make no edit and say so in `record.md`.** Do not edit for tidiness.

**E3, conditional.** Any other wording either run proves wrong may be corrected, but only with the run output that proves it quoted alongside the change in `record.md`.

Nothing else. In particular the prompt gains **no new checks** in this packet; see Do NOT.

### Step 6: Record

`record.md` carries: the base SHA and both scratch worktree paths; the induction method used and why; the TRX evidence that the induced skip was a single clean unexpected skip; both verdict lines verbatim; which branch of each pre-authorized rule your evidence selected; the exact diff you made to the prompt with the run output that justifies each hunk; and an honesty section naming what is still NOT proven.

The honesty section must include, at minimum, that **every sentence this packet adds to the prompt is unpinned**: the guard fact asserts only the wrapper substring, `CCP_DATA_ROOT`, and the absence of the bare invocation, so nothing stops a later edit from removing your clause. Extending that fact would edit `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs`, which is outside this packet's scope and belongs to another lane this wave. **File it, do not do it.**

File one more obligation in `record.md`, discovered at authoring and deliberately excluded: under the floor-delta mechanism a land sums per-packet deltas into the shared pin, and the auditor never checks that the new pin equals the old pin plus the declared deltas. A land that instead set `total` to the observed count would pass the auditor while committing exactly the vacuous-green the pin exists to catch. That is a new check, not T-17's residual. Name it for the board; do not add it here.

### Step 7: Verification

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```

Run them as **separate commands**. The worktree isolation guard refuses compound shell commands (`cd X && ...`), so chain nothing. Build immediately before the gate in the same tree, for the same reason you are writing into the prompt.

Your declared delta is `0`/`0`, so the observed totals must equal the pin **exactly**, 1018 unit and 35 headless. If they do not, something outside this packet moved the count: state both numbers and stop. Do not touch the pin, and do not reconcile the difference yourself.

## Completion Criteria

- Both audit runs executed as separate `claude` processes against separate scratch worktrees, transcripts committed under `evidence/`.
- The induced-skip run reached the wrapper and its FAIL names the induced skip; the induction is demonstrated to have been the wrapper's only failure.
- The clean run's outcome is resolved against P1, P2 or P3 with the branch stated and the evidence quoted.
- `client/tools/port-audit-prompt.md` carries E1, plus E2/E3 only where a run justifies them, and the guard fact is still green.
- `record.md` and `floor-delta.json` exist and are accurate; the honesty section names the unpinned-prose limit and the delta-arithmetic obligation.
- Build 0W/0E; the floor gate observes exactly pin plus zero.
- Both scratch worktrees removed; the lane diff contains only File Scope paths.

## Do NOT

- Weaken the auditor in any form: no softening of "a non-zero exit is an audit FAIL", no "unless", no tolerance, no retry, no permission to continue past a floor failure. This packet exists because the auditor is the strictest consumer of the floor, and a packet that makes it easier to pass has inverted its own row.
- Relax, suppress, or narrow the guard regex at `FloorWrapperGuardTests.cs:55-56`, or add an exception so the prompt may quote the runner invocation. Phrase around it. Editing that file at all is out of scope this wave.
- Add new checks to the auditor prompt, including the pin-versus-summed-deltas arithmetic named in Step 6. It is a real gap and it is a different row; filing it is the deliverable, fixing it is not.
- Induce the skip by setting `CCP_DATA_ROOT`. It is banned by `port-workflow.md` and by the wrapper's own header, it is process-wide, and it would make your induction indistinguishable from the accident the ban exists to prevent.
- Induce the skip with `Assert.Skip*` in an un-ledgered fact, for the reason in Step 2.
- Commit the induced skip, induce it in your own worktree, or leave a scratch worktree behind.
- Act as the auditor yourself, or reuse a session, or run the audit with your own model. Fresh process, `--safe-mode`, `--no-session-persistence`, read-shaped tool set, different model.
- Edit `client/tests/floor/floor.json`, `client/tests/floor/vacuous-shape-ledger.json` in the lane tree, `client/docs/task-board.md`, or anything under `client/docs/`, `.claude/`, `.spine/`, `.pi/`, or `ConditioningControlPanel/`.
- Close or edit the T-17 board row, or any neighbouring row. Report what you proved; the row is reconciled at land.
- Leave a TODO, a placeholder, or a half-run whose second half is asserted rather than executed.

## Git Commit Convention

Conventional commits, `docs(SP-081): ...` if the prompt correction is the only tracked change, `fix(SP-081): ...` if a run proved the prompt behaviourally wrong. One coherent slice, no unrelated files. Leave the tree buildable at every commit. Commit your own work on your branch; do not merge, do not land, and do not touch the shared pin.

## Documentation Requirements

**Must update:** `spine-tasks/SP-081-auditor-prompt-residual/record.md`, `spine-tasks/SP-081-auditor-prompt-residual/floor-delta.json`, and the evidence logs named in Step 3.

`client/docs/port-workflow.md` and `client/docs/port-lessons.md` both carry text this run bears on, and both are outside your scope. If your evidence changes a fact stated there, say so in `record.md` and quote the wording you believe is owed. **Do not edit those documents yourself**; policy-touching text is applied by the orchestrator at land (SP-059 precedent, followed by SP-071, SP-072 and SP-073).
