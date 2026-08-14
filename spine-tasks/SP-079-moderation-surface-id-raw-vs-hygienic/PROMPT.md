# SP-079 — Make the hygienic half of the SP-069 union moderation gate distinguishable from the raw half

## Mission

SP-069 landed a UNION output moderation gate at the reply seam: the boundary evaluates the RAW model text and the HYGIENIC text, and blocks if EITHER hits. That was correct and is not in question here. The gate can only ever refuse more, never less, and this packet inherits that as a hard invariant.

It left a residue, and the SP-069 land filed it deliberately rather than fixing it inside a File Scope that could not move the coverage pin honestly: **both halves report under the same surface id**. `AiOperationPipeline.cs:350` passes `outputSurface` for the raw scan and `:362` passes the same `outputSurface` for the hygienic scan, so a block that only the hygienic scan can see is attributed to the raw text's surface. The orchestrator opened both call sites and both surface definitions before authoring this packet: **the premise is TRUE, verbatim as the row states it.**

The orchestrator also found a fact the row does not state, and it changes the shape of your work: **`SurfaceId` currently dies inside the pipeline.** It is stamped on the verdict at `AiModerationBoundary.cs:290-291` and then dropped at `AiOperationPipeline.cs:353` and `:365`, which build the refusal from `CategoryCode` and `Source` only. Nothing in `client/src/**` reads `SurfaceId`. The mis-attribution is therefore real but latent: it is wrong on an object no product consumer inspects.

Your outcome: **the two halves of the union become distinguishable to the suite, through the one channel this packet's File Scope actually owns, and the shared-surface question is RESOLVED in writing rather than deferred a second time.** What you may not deliver is a third thing: a gate that blocks different text than it blocks today.

## Dependencies

SP-069 (landed, reply hygiene + the union rule). SP-068 (link strip, downstream of the gate) and SP-047 (memory assembly, downstream again) are adjacent and out of scope. The board row is `client/docs/task-board.md:108`, P2, OPEN. **You do not edit it.**

## Context to Read First

Verified by the orchestrator at authoring. Every line below was opened in the PORT tree and confirmed, not transcribed from the board:

- `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs:245-247` — `outputSurface` is `AiModerationSurfaces.InteractiveReplyOutput` for Interactive and `AwarenessReplyOutput` for Awareness. One value, computed once, before the operation body.
- `:350` — `switch (_moderation.EvaluateOutput(generated.Text, outputSurface))`, the RAW scan.
- `:360` — `if (!string.Equals(hygienic, generated.Text, StringComparison.Ordinal))`. This guard is what makes "hygienic-only" a well-defined case at all: the second scan runs **only** when hygiene changed the text.
- `:362` — `switch (_moderation.EvaluateOutput(hygienic, outputSurface))`, the HYGIENIC scan, **same surface value**. This is the defect, and it is exactly what the row describes.
- `:353` and `:365` — both build `new AiReply.Refused(new AiModerationRefusal(outputBlock.CategoryCode, AiModerationSource.Output))`. The verdict's `SurfaceId` is discarded here.
- `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs:145` — `public sealed record AiModerationRefusal(string CategoryCode, AiModerationSource Source);`. No surface field exists on the carrier.
- `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs:279-297` — `Evaluate` is pure (no counter, no escalation, no state), which is why the second call cannot double-count, and `:290-291` is where `SurfaceId = surface.Id` is stamped.
- `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs:101-151` — `AiModerationSurfaces`, the closed inventory: 6 Wired, 5 Reserved.
- `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs:254` — `public sealed class AiModerationBoundary`. **Sealed, no interface**, and `AiOperationPipeline.cs:40` / `:54` hold and take the concrete type. Read the Testability Constraint below before you plan around this.
- `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs:242-243` — the 6 Wired / 5 Reserved pin.
- `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs:169-172` — the `default:` arm is a hard `Assert.Fail` for any Wired row without an executable assertion. A new Wired surface reds this suite twice, not once.
- `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs:180-195` — the entry-point tripwire constrains **Input** surfaces only. Note it so you do not misread which tripwire an output surface would trip.
- `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs:319-339` — the content-free diagnostics test: every emitted record is JSON-serialized and asserted to contain neither the category token nor the reply text, and `:337-338` pin `refused:input` / `refused:output` with `Assert.Contains`. `Contains`, not an exact set. That distinction is load-bearing for Step 2.
- `client/tests/CcpClient.Tests/AiModerationPipelineBoundaryTests.cs:130` — the other `refused:output` pin, also `Assert.Contains`, driven by a token visible in the RAW text (`$"model said {Forbidden}"`).
- `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs:427`, `:436`, `:514-526` — the diagnostic stable code is composed **inside this file**. `refused:input` / `refused:output` are bare literals here, derived from `Refusal.Source`, never from a surface. They are not members of `AiReplyCodes`.
- `client/tests/CcpClient.Tests/AiReplyHygienePipelineTests.cs:118-180` — the SP-069 union pins in both directions. `UnionRule_TokenJoinedAcrossTagBoundary_Blocks_NeverPersisted` (`"sensi<thinking>scratch</thinking>tive-token here"`) is the canonical hygienic-only shape; `UnionRule_TokenInsideStrippedThinkBlock_StillBlocks` is the raw-only shape that proves the raw scan is not redundant. **This file is OUT of your scope.** Read it for the shapes; build your own facts in your own test file.
- `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs:63-73` — the existing `Harness`, with `CollectingAiDiagnosticsSink Diagnostics` already wired. It constructs `new AiModerationBoundary()` with the default Empty policy; a policy-bearing harness is yours to add.

## THE TESTABILITY CONSTRAINT, NAMED AT AUTHORING

This project has hit "the mechanism sits somewhere no headless fact can reach" three times. Here is where it sits this time, so you do not discover it in review:

1. `AiModerationBoundary` is **sealed with no interface** and the pipeline field is the concrete type. **No test can observe the `AiModerationSurface` argument** passed at `:350` / `:362`. "Assert the pipeline passed surface X" is not a reachable fact. Do not try to reach it with a spy, a mock, an `InternalsVisibleTo` shim, a subclass, or reflection over a local.
2. `AiModerationRefusal` has no surface field, so `SurfaceId` cannot ride out on the reply. Adding one means `AiOperationVocabulary.cs`, which is **not in your File Scope**.
3. Adding a real registry surface means `AiModerationBoundary.cs` plus the 6/5 pin and a new assertion arm in `AiModerationCoverageTests.cs`, none of which are in your File Scope.

Therefore: **the only in-scope, pipeline-owned, headless-observable channel is the diagnostic `StableCode`**, composed at `AiOperationPipeline.cs:427` and read through `CollectingAiDiagnosticsSink`. If your distinction does not surface there, it does not surface. Plan the mechanism to live there or come back with an escalation, not with an unobservable edit.

No headed evidence is owed by this packet. These are pure-logic facts in `CcpClient.Tests`; nothing here is `draw-verified` or `presentation-verified`.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs`, `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs`, `spine-tasks/SP-079-moderation-surface-id-raw-vs-hygienic/**` |
| Must not change | everything else, and specifically the files named in the contract below, plus `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs`, `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs`, `client/tests/CcpClient.Tests/AiModerationCoverageTests.cs`, `client/tests/CcpClient.Tests/AiModerationPipelineBoundaryTests.cs`, `client/tests/CcpClient.Tests/AiReplyHygienePipelineTests.cs` |

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-079-moderation-surface-id-raw-vs-hygienic/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs`, `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-079-moderation-surface-id-raw-vs-hygienic/record.md`, `spine-tasks/SP-079-moderation-surface-id-raw-vs-hygienic/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** That file is the shared pin and concurrent lanes collide on it. Write your count change into `floor-delta.json` in your own folder instead:

```json
{ "packet": "SP-079-moderation-surface-id-raw-vs-hygienic", "unit": 0, "headless": 0, "reason": "one line naming the facts you added" }
```

Declare `0`/`0` if you add no tests; omitting the file is not the same as declaring zero. The land sums every packet's delta and applies one bump. `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` enforces both halves of this and will fail your run if the row or the disclaimer is missing.

## SCOPE PROBLEM (read before Step 1; it constrains which branch you may land)

The board row offers two acceptances: give the hygienic evaluation **its own Wired surface**, or **record the shared-surface decision** on the surface table. Your assigned File Scope makes the first one unreachable, and the scopes in this wave were pre-assigned pairwise disjoint, so you may not widen it.

Concretely, a new Wired surface requires all three of: the registry row in `AiModerationBoundary.cs:101-151`; the `6`→`7` move at `AiModerationCoverageTests.cs:242`; and a new assertion arm before `AiModerationCoverageTests.cs:169`, because the `default:` case is an unconditional `Assert.Fail`. None of those files are yours.

**Consequence, and it is an instruction, not an observation:** this packet executes the SECOND acceptance. You resolve the shared-surface question in writing, and you make the two halves distinguishable in the channel the pipeline owns. If your Step 1 evidence says a real registry surface is genuinely owed, you say so in `record.md` naming those three edits by file and line and leave the follow-up to the orchestrator. **You do not create the surface, and you do not touch the pin.** The row already forbids closing it by "adding a surface literal in a packet that cannot move the pin honestly", and that is precisely the packet you are.

## Review Level: 3 (Plan, Code, Final)

Level 3, not 2, for two independent reasons. First, this is the **live moderation refusal path**: every edit sits between the model returning text and that text reaching the bubble, memory, or disk, and the SP-069 invariant is that the change may only ever BLOCK MORE, never admit text blocked today. Second, the only reachable mechanism writes a new string into a **diagnostic record**, and content-free diagnostics is a privacy contract (contract §12) actively guarded by `AiModerationCoverageTests.cs:319-335`, which serializes every record and asserts neither category text nor reply text appears. A change that adds text to that record is a privacy-surface change and gets the full gate.

## Steps

### Step 1: Establish the consumer census, then decide the surface question against the pre-authorized rule

Establish by grepping the PORT tree yourself, not by inheriting the orchestrator's finding:

1. Every reader of `AiModerationVerdict.SoftHit.SurfaceId` and `AiModerationVerdict.Block.SurfaceId` in `client/`, split into `client/src/**` and `client/tests/**`.
2. Whether any of them can distinguish the two halves of the union today.
3. Whether the surface value reaches any persisted, logged, displayed, or transmitted artifact.

**THE SURFACE DECISION IS PRE-AUTHORIZED BOTH WAYS. Resolve it on your evidence; do not ask.**

- **If the census finds NO reader of `SurfaceId` in `client/src/**`**: the shared-surface answer is correct on the merits, not merely forced by scope. Record it as a decision with its reason (a surface id with no consumer is inventory, not behavior), and note that the moment a consumer appears the decision must be revisited. This is the branch the orchestrator's own grep points at, which is exactly why you must re-derive it rather than trust it.
- **If the census finds a reader in `client/src/**`**: the mis-attribution has a live consumer and a real Wired surface is owed. You still do not create it here. `record.md` states the consumer with `File.cs:line`, states that a follow-up packet is owed, and names the three edits from the SCOPE PROBLEM section as its minimum File Scope. Then you land the same in-scope distinction as the other branch, because it is honest under either answer.

Either way the surface question stops being deferred. A third deferral is a failed packet.

### Step 2: Decide whether the distinguishing mechanism ships, against the second pre-authorized rule

The mechanism is a distinct, content-free stable code for the hygienic-only block, emitted where `AiOperationPipeline.cs:427` composes the diagnostic, in the same shape the existing `softHitCode` local already uses. It is not a surface, it does not claim to be one, and it must be commented as such at the call site.

**THIS IS ALSO PRE-AUTHORIZED BOTH WAYS.**

- **LAND IT** if all four hold, each proven by a command you ran and not by argument: (a) which texts block is unchanged, (b) nothing outside your two-file scope needs an edit, (c) `AiModerationCoverageTests.cs:338` and `AiModerationPipelineBoundaryTests.cs:130` stay green because their inputs hit the RAW scan and still emit `refused:output`, (d) the serialization assertion at `:332-334` stays green because the new code is a fixed literal carrying no category, token, or reply text.
- **DO NOT LAND IT** if any of those fails. Most plausibly: you discover a closed-vocabulary pin over stable codes, or a document under `client/docs/**` enumerating the code set, either of which would force an out-of-scope edit. In that case the deliverable degrades to the recorded decision plus an in-scope fact that pins the CURRENT attribution as deliberate, and `record.md` names the blocker by `File.cs:line`. A degraded deliverable that is honest beats a widened scope.

Take the pre-approach advisory gate here, with the census and your chosen branch attached. Do not ask before you have the census.

### Step 3: Bind the behaviour, one source at a time

Every fact you add must be proven to bite by an **independent revert** of the single source line it guards, run one at a time, restoring the tree byte-identically between reverts. Record the red count per revert. At minimum:

1. Revert the hygienic-half emission: the hygienic-only fact goes red, the raw fact stays green.
2. Revert the raw-half emission: the raw fact goes red, the hygienic-only fact stays green. If reverting one reds both, your two facts are one fact wearing two names.
3. Revert the raw `EvaluateOutput` call at `:350` entirely: your own in-scope BLOCK-MORE fact must go red. **Build this fact in `AiOperationPipelineTests.cs`.** The equivalent pin exists in `AiReplyHygienePipelineTests.cs`, which is out of your scope, and a lane that leans on an out-of-scope file for its own load-bearing evidence has proven nothing it owns.

**The vacuity bar, stated because this run has hit it three times (SP-067, SP-070, and the class SP-072 designed out):** an assertion that passes with the mechanism reverted is not a fact. Assert on the emitted diagnostic record, which is the observable, not on a shape you constructed in the test.

Cover both operation classes. Interactive and Awareness compute `outputSurface` from the same ternary at `:245-247` and a fix that only lands on one is half a fix.

### Step 4: Record

`record.md`: the consumer census as a table, which branch of each of the two decision rules your evidence selected and why, the revert matrix with red counts, the exact wording of the shared-surface decision so the next reader does not re-derive it, and an honesty section naming what is NOT proven. State plainly that no registry surface was added and why. `floor-delta.json` with your real counts.

### Step 5: Verification

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```

Run them as **separate commands**. The worktree isolation guard refuses compound shell commands (`cd X && ...`), so chain nothing.

**Build immediately before the gate, every time.** The floor wrapper is `--no-build`; a stale `bin/` once reported 1022 against a tree containing 1018 tests, and that number was believed for a while.

Your floor run will report a total that does NOT match the pin, because the pin is bumped at land from the summed deltas and not by you. That is expected and is not a failure of your work: confirm the observed total equals `pin + your declared delta`, and state both numbers in your report.

## Completion Criteria

- The consumer census is complete and both decision-rule branches are stated with `File.cs:line` evidence.
- The shared-surface question is RESOLVED in `record.md`, not deferred.
- The two halves of the union are distinguishable to the suite, or the degraded branch is taken with its blocker named.
- Every new fact bites under its own independent revert, and no revert reds a fact it does not own.
- The BLOCK-MORE invariant is intact: no text that is refused today is admitted after your change. Say how you know.
- Both operation classes covered.
- `record.md` and `floor-delta.json` exist and are accurate.
- Build 0W/0E.
- The 6/5 pin, the surface registry, and the SP-069 board row are all untouched.

## Do NOT

- **Add a row to `AiModerationSurfaces`.** Out of scope, and it reds `AiModerationCoverageTests.cs:242` and `:171` in files you cannot fix.
- **Construct an ad-hoc `AiModerationSurface` literal inside the pipeline** (including `outputSurface with { Id = ... }`). It manufactures a surface outside the closed inventory, defeats the coverage honesty the registry exists for, and is the exact move the board row forbids by name.
- **Relax, bump, or "temporarily" adjust the 6 Wired / 5 Reserved pin.** The row says never by relaxing the pin first, and you cannot move it honestly from this scope anyway.
- **Add a field to `AiModerationRefusal` or `AiModerationVerdict`.** `AiOperationVocabulary.cs` is not yours.
- **Reorder the two scans so hygienic runs first.** SP-069 fixed that when both soft-hit the RAW verdict is the one surfaced (`AiOperationPipeline.cs:343-349`), and the `softHitCode` precedence at `:356` / `:368` encodes it. Reordering silently changes which verdict a user gets.
- **Delete the raw scan at `:350` on the theory that hygiene only removes text so the hygienic scan is a superset.** It is not, and this is the tempting wrong fix. `UnionRule_TokenInsideStrippedThinkBlock_StillBlocks` exists because a token inside a stripped `<thinking>` block is invisible to the hygienic scan; removing the raw scan ADMITS text refused today and breaks the packet's binding invariant.
- **Remove the `!string.Equals` guard at `:360`.** It is what defines the hygienic-only case; without it the two halves are no longer separable at all.
- **Put a category code, a policy token, a surface id derived from content, or any reply text into a diagnostic record.** Contract §12, guarded at `AiModerationCoverageTests.cs:332-334`.
- Unseal `AiModerationBoundary`, extract an interface from it, add `InternalsVisibleTo`, or reach the surface argument by reflection.
- Edit `client/tests/floor/floor.json`, `client/docs/task-board.md`, or anything under `client/docs/`, `.claude/`, `.spine/`, `.pi/`, or `ConditioningControlPanel/`.
- Close, edit, or claim the SP-069 board row or any neighbouring row. A packet that "helpfully" closes a neighbouring row has changed a mechanism nobody reviewed.
- Add a wall-clock wait. `client/tests/CcpClient.Tests/TestWait.cs` is the only approved helper; `Thread.Sleep`, bare `Task.Delay`, and `DateTime`/`Environment.TickCount64` polls fail the timing guard mechanically and `TestTimingGuardTests` will red your run.
- Export `CCP_DATA_ROOT` process-wide. It makes the SP-057 pin skip and the floor goes blind.
- Leave a TODO, a placeholder, or a partially wired mechanism.

## Git Commit Convention

Conventional commits, `fix(SP-079): ...`. One coherent slice, no unrelated files. Leave the tree buildable at every commit. Commit your own work on your branch; do not merge, do not land, and do not touch the shared pin.

## Documentation Requirements

If your work changes a fact stated in `client/docs/ai-operation-contract.md` or `client/docs/ai-companion-admission.md`, say so in `record.md` and quote the wording you believe is owed. **Do not edit either document yourself**; policy-touching text is applied by the orchestrator at land (SP-059 precedent, followed by SP-071, SP-072 and SP-073). The same applies to the board row at `client/docs/task-board.md:108`: state in `record.md` the exact replacement text you believe the row should carry, and let the orchestrator apply it.
