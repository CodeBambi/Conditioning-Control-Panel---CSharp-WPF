# SP-079 record — the hygienic half of the SP-069 output union, made distinguishable

Branch `sp-079-moderation-surface-id`, base `feat/crossplatform` @ `f2db1e25`. Two product files
changed, both in File Scope. No registry surface added, no pin touched, no board row touched.

Claim discipline binding this document: **a claim states what was read or measured, and when,
never more.** Every assertion below is tagged as read at plan time, or measured during execution.

---

## 1. Consumer census (Step 1), re-derived in this tree

Three greps over the worktree, not inherited from the packet: `SurfaceId` across `client/`,
`AiModerationVerdict` across `client/src/`, `refused:output` across `client/`.

### 1a. Every reader/writer of `Verdict.SoftHit.SurfaceId` / `Verdict.Block.SurfaceId`

| Site | Tree | Role |
|---|---|---|
| `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs:129` (SoftHit), `:136` (Block) | src | declaration, `string? { get; init; }` |
| `client/src/CcpClient.Desktop/Ai/AiModerationBoundary.cs:290-291` | src | the only **writer**: `{ SurfaceId = surface.Id }` |
| `client/tests/CcpClient.Tests/AiModerationBoundaryTests.cs:34` | tests | reader, `Assert.Equal("interactive-chat-input", block.SurfaceId)` |
| `client/tests/CcpClient.Tests/AiModerationBoundaryTests.cs:39` | tests | reader, `Assert.Equal("interactive-reply-output", soft.SurfaceId)` |
| `client/tests/CcpClient.Tests/AiModerationBoundaryTests.cs:50-51` | tests | hand-constructed writers in a serialization sample |
| `client/docs/task-board.md:108` | docs | the board row's own prose |

**Readers in `client/src/**`: zero.** Not grep alone: every verdict consumer in `client/src/` was
opened and read, and each reads `CategoryCode` or the type only, never `SurfaceId`.

| Consumer | What it reads off the verdict |
|---|---|
| `AiOperationPipeline.cs:271-279` (input block) | `inputBlock.CategoryCode` only |
| `AiOperationPipeline.cs:361-362` (raw output block) | `outputBlock.CategoryCode` only |
| `AiOperationPipeline.cs:373-375` (hygienic output block) | `outputBlock.CategoryCode` only |
| `AiOperationPipeline.cs:280`, `:364`, `:377` (soft hits) | type only, no properties |
| `AiAwarenessService.cs:215-218` | `block.CategoryCode` only |
| `AiCommandEnvelope.cs:450-451` | `block.CategoryCode` only |
| `CompanionBubbleModel.cs:42`, `:64` | consumes `AiModerationRefusal`, which has no surface field |

### 1b. Can any consumer distinguish the two halves today?

No. Both `EvaluateOutput` calls pass the same `outputSurface` computed once at
`AiOperationPipeline.cs:245-247`, so both verdicts carry the same `SurfaceId`; and both refusals
are built from `new AiModerationRefusal(outputBlock.CategoryCode, AiModerationSource.Output)`
(`AiOperationVocabulary.cs:144`), which has no surface field. The `SurfaceId` is dropped at both
sites. **The packet's premise reproduces verbatim.**

### 1c. Does the surface value reach a persisted, logged, displayed, or transmitted artifact?

No, on all four.

- **Logged / diagnostics**: the only emitter for this seam is `AiOperationPipeline.Emit`
  (`:517-523`), building `AiDiagnosticRecord` from
  `(operationClass, endpointClass, outcome, stableCode, generation, durationMs, 0, [])`. No
  surface is passed; the property set is structurally pinned at `AiOperationContractTests.cs:297-317`.
- **Persisted**: the memory append at `:422-426` writes `AiMemoryTurn` text only, and a blocked
  turn returns at `:363`/`:375` before it.
- **Displayed**: the UI consumes `AiReply.Refused` -> `AiModerationRefusal`
  (`CompanionBubbleModel.cs:42`, `:64`), which has no surface field.
- **Transmitted**: the boundary is pure local (`AiModerationBoundary.cs:279-297`; the no-network
  shape is proven structurally at `AiModerationCoverageTests.cs:293-315`).

**Census verdict: `SurfaceId` is write-only in product code.** Stamped once, read by exactly two
test assertions.

---

## 2. Which branch of each pre-authorized rule the evidence selected

### Rule 1 (Step 1), the surface question: **first branch, "no reader in `client/src/**`"**

> **Decision (SP-079): the hygienic evaluation shares `AiModerationSurfaces.InteractiveReplyOutput`
> / `AwarenessReplyOutput` with the raw evaluation, deliberately.** Both halves of the SP-069 union
> evaluate the same seam of the same operation, and the surface inventory exists to answer "which
> seams does the boundary moderate", not "which pass within a seam produced a hit". A registry row
> is a coverage claim (`AiModerationBoundary.cs:101-151`), and a second output row for the same
> seam would claim a surface the client does not have. The mis-attribution is real but has **zero
> product consumers**: `SurfaceId` is stamped at `AiModerationBoundary.cs:290-291` and read by
> nothing in `client/src/**` (census, §1 above). The pass distinction therefore rides the
> diagnostic stable code the pipeline already owns, not the surface inventory.
> **Revisit trigger, mechanical rather than remembered: the first `client/src/**` read of
> `AiModerationVerdict.Block.SurfaceId` or `SoftHit.SurfaceId` reopens this decision.** The
> follow-up's minimum File Scope is then `AiModerationBoundary.cs:101-151` (the registry row),
> `AiModerationCoverageTests.cs:242` (6 -> 7), and a new assertion arm before
> `AiModerationCoverageTests.cs:169-172` (the `default:` arm is an unconditional `Assert.Fail`),
> all three in one commit with the reason, never by relaxing the pin first.

The shared-surface answer is correct **on the merits**, not merely forced by File Scope. **No
follow-up packet is owed under this branch**; the three edits are named anyway so the trigger is
actionable. The question is resolved here, not deferred a third time.

### Rule 2 (Step 2), does the distinguishing mechanism ship: **LAND IT**

Conditions (a) to (d) were predicted from source at plan time and are **discharged by measurement**
below, not by argument.

- **(a) which texts block is unchanged.** No predicate, no `EvaluateOutput` argument, no policy, no
  hygiene call and no guard was edited. Between `:359` and `:380` the entire change is **one added
  assignment statement inside a `case` arm that already returned `Refused`**. Measured: F3 (the
  in-scope BLOCK-MORE fact) and the out-of-scope SP-069 union pins
  (`AiReplyHygienePipelineTests.cs:121-180`) and `AiModerationPipelineBoundaryTests.cs:117-145` are
  all green at baseline.
- **(b) nothing outside the two-file scope needs an edit.** Measured: `git status --porcelain` after
  the final restore lists exactly `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` and
  `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs`. The guard that could have broken this
  was the vacuous-shape ledger (§5a); measured green, no new ledger site.
- **(c) `AiModerationCoverageTests.cs:338` and `AiModerationPipelineBoundaryTests.cs:130` stay
  green.** Measured green at baseline. **Load-bearing reason: both inputs block on the RAW scan at
  `:359` and `return` at `:362`, so the hygienic guard at `:369` is never reached at all on those
  paths.** `AiModerationCoverageTests.cs:326` sets the reply to `Forbidden` alone;
  `AiModerationPipelineBoundaryTests.cs:121` sets `$"model said {Forbidden}"`. Neither can reach the
  hygienic arm regardless of what hygiene does to the text, so this reason survives a future hygiene
  change. Both pins compare with `==` on the full string, so a differently suffixed sibling can
  neither satisfy nor disturb them.
- **(d) the serialization assertion at `AiModerationCoverageTests.cs:332-334` stays green.**
  Measured green. The added value is a compile-time literal containing no category, token, surface
  id, or reply text. F1 additionally serializes the *new* record and asserts the category code, the
  policy token, and the reply text are all absent, on a path that test does not exercise.

**No blocker found for the degraded branch.** Checked both the packet predicted:

- **Closed vocabulary over stable codes: none exists.** `rg "refused:output"` over `client/` returns
  four sites: two producers (`AiOperationPipeline.cs:445`, `:547`) and two `Assert.Contains` pins
  (`AiModerationCoverageTests.cs:338`, `AiModerationPipelineBoundaryTests.cs:130`). The only shape
  constraint is `AiOperationPipelineTests.cs:350` (`Assert.Matches("^[a-z-]+$", record.StableCode!)`),
  which runs against `Assert.Single(h.Diagnostics.Records)` on the not-configured path (`:343-345`),
  so its subject is `"not-configured"`. Every other assertion is an exact `==` on one code.
- **A doc enumerating the code set: none.** `client/docs/ai-operation-contract.md` §12 rule 1
  requires "stable machine-readable codes" and closes the *property set*, never the code values; the
  only closed code mapping it names is `AiDiagnosticCodes.VerdictCode` for per-command verdicts. No
  test reads that document at runtime (`AiOperationContractTests.cs:10` names it in a comment only).

**No documentation wording is owed** under `client/docs/ai-operation-contract.md` or
`client/docs/ai-companion-admission.md`. No fact stated in either document changes.

---

## 3. The mechanism as landed (three hunks, one product file)

**Hunk A**, `AiOperationPipeline.cs:250-257`: a method-local `string? outputRefusalCode = null;`
beside the existing `softHitCode` at `:248`, with the required call-site comment naming it a
DIAGNOSTIC stable code and NOT a moderation surface.

**Hunk B**, `:374`: one added statement inside the existing hygienic `case` arm:

```csharp
case AiModerationVerdict.Block outputBlock:
    outputRefusalCode = "refused:output-hygienic";
    reply = new AiReply.Refused(new AiModerationRefusal(outputBlock.CategoryCode, AiModerationSource.Output));
    return OperationOutcome.Completed.Instance;
```

**Hunk C**, `:449-451`: substitution, never addition, at the single `Emit`:

```csharp
var refusalCode = appliedReply is AiReply.Refused ? outputRefusalCode : null;
Emit(operationClass, descriptor.EndpointClass, OutcomeOf(outcome, appliedReply),
    softHitCode ?? refusalCode ?? StableCodeOf(outcome, appliedReply), ElapsedMs(started), _owner.Generation);
```

Literal choice: `"refused:output-hygienic"` keeps the one-colon `family:value` grammar already in
use (`refused:output`, `soft-hit:input`, `suppressed:cooldown`, `dropped:privacy-filtered`). A
second colon would be a new grammar for any future splitter. The raw half keeps `"refused:output"`
exactly, from the unchanged `StableCodeOf` arm at `:547`.

**Exactly one emission changes**: a hygienic-only output block with no prior soft hit, from
`refused:output` to `refused:output-hygienic`. No soft-hit emission and no raw-block emission
changes, because `softHitCode` keeps first place in the `??` chain. That is measured, not argued:
R4 below reorders the chain and reds exactly one fact.

The `appliedReply is AiReply.Refused` test is a **totality guard that is unreachable-false today**
(the only writer of `outputRefusalCode` also sets `reply = Refused` and returns `Completed`, and
`AsyncOperationOwner.RunAsync`, `OperationRegistry.cs:195-247` with its `Task.Run` body `:216-245`,
returns the body's outcome verbatim). It closes no live hole and no race is claimed for it; it
exists so a future edit that lets `reply` survive a non-Completed outcome cannot carry this code
onto a Cancelled or Faulted record.

### Concurrency: the change nests no lock and introduces no shared state

`AiOperationPipeline._gate` is taken at `:73`, `:87`, `:109`, `:126`, `:183`, `:211`, `:486`, `:494`,
`:511` (the complete set). It guards `_providers`, `_descriptors`, `_outstanding`, `_selected`, never
the moderation locals. `outputRefusalCode` is a method-local of `RunCoreAsync`, per invocation,
exactly like `softHitCode`; it is written from the body thread only and read after
`await completion`, which is strictly weaker sharing than the invariant `softHitCode` already relies
on (that one is written from two threads, the caller at `:281` and the body at `:365`/`:378`). At the
`Emit` call, `_owner.Generation` (`AsyncOperationOwner._gate`, `OperationRegistry.cs:135-138`) is
evaluated before entering `Emit`, which then takes `CollectingAiDiagnosticsSink._gate`
(`AiDiagnostics.cs:29`): sequential, not nested. `AiModerationEscalation._gate`
(`AiModerationBoundary.cs:184`, held in `RecordHit` `:202` and `GetState` `:223`) is untouched; no
`RecordHit` is added, so "output blocks never escalate" is preserved.

---

## 4. Revert matrix, executed for real

Each revert was applied **alone** to `AiOperationPipeline.cs`, rebuilt through the slot semaphore,
and run against the four affected test classes (`AiOperationPipelineTests`,
`AiModerationPipelineBoundaryTests`, `AiModerationCoverageTests`, `AiReplyHygienePipelineTests`,
46 tests). Between reverts the file was restored from a pristine copy and the restoration verified
by SHA-256: baseline `e2e4104099516c5c8274f1811ec04c6d211d02e918c439d02d581e4af537cfc7`, matched
after every restore. Every build was 0 warnings / 0 errors.

| Revert | Exact source removed/changed | Red | Which |
|---|---|---|---|
| baseline | none | **0 / 46** | all green |
| **R1** | Hunk B's added line `outputRefusalCode = "refused:output-hygienic";` only | **2** | F1, F4 |
| **R2** | the raw half's refusal (`reply = new AiReply.Refused(...)` at `:361`, `return` kept) | **9** | F2, F3, F5 + 6 out-of-scope |
| **R3** | the whole raw `switch (_moderation.EvaluateOutput(generated.Text, outputSurface))` at `:359-367` | **12** | F2, F3, F5, F6 + 8 out-of-scope |
| **R4** | Hunk C's chain reordered to `refusalCode ?? softHitCode ?? StableCodeOf(...)` | **1** | F6 |

Out-of-scope reds, by revert:

- **R2 (6):** `AiModerationPipelineBoundaryTests.Interactive_OutputBlock_TypedRefusal_NoEscalation`,
  `.Awareness_OutputBlock_TypedRefusal_DroppedByType`;
  `AiModerationCoverageTests.Diagnostics_BlockedOperations_CarrySideCodesOnly_NeverPolicyContent`,
  `.Inventory_EveryWiredSurface_HasAnExecutableAssertion_EveryReservedSurface_NamesItsSeam`;
  `AiReplyHygienePipelineTests.UnionRule_TokenInsideStrippedThinkBlock_StillBlocks`,
  `.UnionRule_TokenInsideStrippedMetadataEcho_StillBlocks`.
- **R3 (8):** the same six, plus
  `AiReplyHygienePipelineTests.SoftHit_VisibleOnlyInRaw_SoftHitCode_ReplyShowsHygienicText` and
  `AiModerationPipelineBoundaryTests.SoftHit_Output_PassesThrough_StableCodeDiagnostic`.

### Two measured results that did NOT match the plan's prediction

Recorded as signal, per the plan's own instruction that a mismatch against the predicted table is
signal rather than an expected miss.

1. **F6 reds under R3; the plan predicted it green.** The prediction was wrong, and the measurement
   is right: R3 deletes the raw `switch` **including its `SoftHit` arm at `:364-366`**, which is
   precisely the source of the `soft-hit:output` value F6 pins. F6 therefore genuinely owns raw-scan
   behaviour as well as chain-order behaviour. This does not weaken the independence criterion (§4.1
   below): R4 remains a revert that reds F6 **and nothing else**.
2. **`AiModerationPipelineBoundaryTests.SoftHit_Output_PassesThrough_StableCodeDiagnostic` reds under
   R3; the plan's R3 neighbour list omitted it.** Correct that it reds: its reply
   `"a sensitive-token reply"` is hygiene-invariant, so with the raw scan gone the `:369` guard is
   false, the hygienic scan never runs, and no `soft-hit:output` is emitted. The plan under-listed
   the R3 neighbourhood by one.

### 4.1 Independence

R1 reds only the hygienic facts {F1, F4}. R2 reds only the raw facts {F2, F3, F5}. Neither reverts
the other's fact, so the two halves are two facts and not one wearing two names, which is where the
packet places the bar. R4 reds only {F6}. **F4 and F5 are deliberately class-coverage facts that
share a revert with F1 and F2**, discharging Step 3's "cover both operation classes"; a matrix in
which R1 red only F1 and R2 only F2 would mean Awareness was never covered. The "no revert reds a
fact it does not own" criterion is read against the raw/hygienic split, not pairwise across all six.

---

## 5. The facts

All six land in `client/tests/CcpClient.Tests/AiOperationPipelineTests.cs`. Support (not facts): a
`ModeratedHarness` constructing `new AiOperationPipeline(..., new AiModerationBoundary(policy))` and
a `TextProvider : IAiProvider` returning a fixed reply via `Task.FromResult`, both private nested
classes. Policy: Block `"test-block-category"` over `["sensitive-token", "forbidden-token"]`, plus
SoftHit `"test-soft-category"` over `["soft-token"]` (used only by F6). Every assertion is on the
**emitted diagnostic record**, never on a shape constructed in the test.

| # | Fact | Subject reply text | Core assertion |
|---|---|---|---|
| F1 | `HygienicOnlyOutputBlock_Interactive_EmitsTheHygienicHalfsOwnRefusalCode` | `"sensi<thinking>scratch</thinking>tive-token here"` | `Refused`/`Output`; record `StableCode == "refused:output-hygienic"`; `DoesNotContain(StableCode == "refused:output")`; and on that same record, `JsonSerializer.Serialize(record)` contains neither the category code, nor the policy token, nor the reply text |
| F2 | `RawOutputBlock_Interactive_KeepsTheUnsuffixedRefusedOutputCode` | `"model said forbidden-token"` | `Refused`/`Output`; `StableCode == "refused:output"`; `DoesNotContain("refused:output-hygienic")` |
| F3 | `RawOnlyToken_StrippedByHygiene_StillRefuses_TheUnionBlocksMore` | `"<thinking>sensitive-token</thinking> hello"` | `Refused`/`Output` (hygienic text is `"hello"`, which passes); `StableCode == "refused:output"`. **The in-scope BLOCK-MORE fact.** |
| F4 | `HygienicOnlyOutputBlock_Awareness_EmitsTheHygienicHalfsOwnRefusalCode` | same as F1, via `RunAwarenessAsync(..., Given)` | `OperationClass == Awareness` and `StableCode == "refused:output-hygienic"` |
| F5 | `RawOutputBlock_Awareness_KeepsTheUnsuffixedRefusedOutputCode` | same as F2, via awareness | `OperationClass == Awareness` and `StableCode == "refused:output"` |
| F6 | `RawSoftHitThenHygienicBlock_SoftHitCodeKeepsFirstPlaceInTheChain` | `"soft-token and forbi<thinking>x</thinking>dden-token"` | `Refused`/`Output`; `StableCode == "soft-hit:output"`. Makes §6 item 1's named limit mechanical. |

F6 is not in the approved plan's five-fact table. It was added on the reviewer's explicit
recommendation in the approval ("strongly advised, not required by the packet"), which also
specified the floor-delta consequence (5 -> 6) and the extra matrix row. It converts the honest limit
named in §6 item 1 from a comment into a test, and it is the only defence against a silent reorder of
the `??` chain.

The privacy assertion rides **inside F1** rather than standing alone: on its own it would pass with
the mechanism reverted, which is exactly the vacuity the packet bars. Riding it on a fact whose
primary assertion reds under R1 makes it non-vacuous.

Hygiene behaviour on the four subject texts, read off `AiTextHygiene.cs:37-73` and confirmed by the
measured baseline: F1 raw passes (`"sensi<thinking>"` splits the token) and hygienic blocks (the
`think|thinking` alternation backtracks to `thinking`, the block is removed, the token joins); F2 is
hygiene-invariant; F3 raw blocks and hygienic is `"hello"`; F6 raw soft-hits on `soft-token` while
hygiene joins the split `forbidden-token` so the hygienic half blocks. None trips
`LooksLikeEnvelopeLeak`.

### 5a. Vacuous-shape ledger

`client/tests/floor/vacuous-shape-ledger.json` is enforced by
`VacuousShapeGuardTests.EverySilencingShapeSite_IsDispositionedInTheLedger` and is **not in this
packet's File Scope**. `VacuousShapeDetector.Scan()` (`:67-78`) walks every `.cs` under
`client/tests`, so a new `[Fact]` carrying a silencing shape would become an undispositioned site and
force an out-of-scope edit. `AiOperationPipelineTests.cs` had **zero** ledger entries, so every shape
F1-F6 could introduce would be new. Design rules applied: every `Assert.*` at body depth 0 (no
`if`/`foreach`/`for`/`while`/`switch`, no braced lambda body; expression lambdas such as
`r => r.StableCode == "..."` are fine because they carry no brace); no bare `return;`; at least one
literal `Assert.` token per fact body; none of `File.Exists(`, `Directory.Exists(`,
`Environment.GetEnvironmentVariable`, `OperatingSystem.Is`, `RuntimeInformation.IsOSPlatform`,
`Assert.Skip*`. **Measured: `VacuousShapeGuardTests` green; no ledger entry added or needed.**

No wall-clock wait was added: `TextProvider.CompleteAsync` returns `Task.FromResult(...)` and every
fact awaits the operation task directly, so no `TestWait` is required. **Measured:
`TestTimingGuardTests` green.** `CCP_DATA_ROOT` is never touched and no `AiMemoryStore` is
constructed (the pipeline's `memory` parameter stays null).

---

## 6. Out of File Scope, filed rather than fixed

1. **A soft hit MASKS a refusal in diagnostics.** `AiOperationPipeline.cs:451` reads
   `softHitCode ?? refusalCode ?? StableCodeOf(...)`, and `softHitCode` can be set at `:281` (input
   soft hit) or `:365` (raw output soft hit) before a block at `:361`/`:373`. Such an operation emits
   `Outcome == Refused` with `StableCode == "soft-hit:*"`: a refused operation reporting a
   non-refusal code. It is pre-existing, it equally masks today's `refused:output`, and correcting
   the precedence would change a live emission nobody has reviewed, so this packet **preserves it
   exactly and names it**. It is the honest limit on this packet's claim: the two halves are
   distinguishable **except** when the same operation also soft-hits. As of this packet the limit is
   no longer only a comment: **F6 pins it, and R4 proves F6 bites.**
2. **`SurfaceId` is write-only in product code.** Stamped at `AiModerationBoundary.cs:290-291`,
   declared at `AiOperationVocabulary.cs:129`/`:136`, read only by `AiModerationBoundaryTests.cs:34,39`.
   Either a `client/src/**` consumer is owed, or the property is owed a documented
   "test-observable inventory only" status so a future reader does not assume it is load-bearing.
   This is the §2 revisit trigger in row form.
3. **`refused:output` has TWO producers in shipping code, so it is not a unique signature of the raw
   half.** The second is `client/src/CcpClient.Desktop/Ai/LoopbackOllamaProvider.cs:307`,
   `return new AiReply.Refused(new AiModerationRefusal(category, AiModerationSource.Output));`,
   reached from `Classify` (`:282-318`) whenever the model's JSON carries a `refusal` object.
   `produced` is assigned to `reply` at `AiOperationPipeline.cs:428` with no type check, so a
   provider-authored refusal flows into `StableCodeOf`'s `Refused` arm (`:547`) and emits
   `refused:output` for a refusal the boundary never made. **Consequence, stated one-directionally:
   `refused:output-hygienic` IS a unique signature of the hygienic half; `refused:output` is NOT a
   unique signature of the raw half.** The distinguishability claim survives because it only ever
   needed the first direction. The mechanism is unaffected: a provider-returned `Refused` skips the
   `produced is AiReply.Generated` block, so `outputRefusalCode` stays null and Hunk C falls through
   to `StableCodeOf`. Untested today; a typed-provenance question, not a fix for this packet.
4. **A second, uncoordinated producer of the same code vocabulary.** `AiAwarenessService.cs:552`
   constructs an `AiDiagnosticRecord` with the literal `"refused:input"` and generation `-1`
   directly, bypassing `AiOperationPipeline.Emit`. If the stable-code set is ever closed or
   centralized, that site must be included or it will silently diverge.

---

## 7. Honesty section: what is NOT proven

- **No registry surface was added, and none is owed under the census.** The mis-attribution the board
  row names is real and remains in the object model: both halves still stamp the same `SurfaceId`.
  What this packet delivers is a distinction in the *diagnostic* channel, not in the surface
  inventory. Anyone reading `Verdict.Block.SurfaceId` in future gets the raw surface id for a
  hygienic-only block, exactly as before. §2's revisit trigger is the mechanism for that.
- **The two halves are NOT distinguishable when the same operation also soft-hits** (§6 item 1). F6
  pins that limit rather than removing it. This is the single largest gap between "the two halves are
  distinguishable" as a headline and what the code actually does.
- **`refused:output` is not a unique signature of the raw half** (§6 item 3). Only the forward
  direction is proven.
- **No test observes the `AiModerationSurface` argument** passed to either `EvaluateOutput` call, and
  none can: `AiModerationBoundary` is sealed with no interface and the pipeline field is the concrete
  type. The packet's Testability Constraint is intact; nothing was unsealed, no interface extracted,
  no `InternalsVisibleTo`, no reflection, no spy.
- **The totality guard in Hunk C is unreachable-false today.** It is defensive. No race is claimed to
  be closed by it, and no test covers a Cancelled/Faulted path carrying `outputRefusalCode`, because
  no such path can exist without a further edit.
- **Coverage is the six facts plus the 46-test neighbourhood, not a proof of the whole emission
  space.** No fact covers the interaction of the new code with `Failed`/`Cancelled` outcomes, with a
  provider-returned `Refused` (§6 item 3), or with a memory-bearing pipeline.
- **The plan's revert predictions were wrong in two places** (§4). They are recorded as measured, not
  smoothed over.
- **No headed evidence is owed or claimed.** These are pure-logic facts in `CcpClient.Tests`; nothing
  here is `draw-verified` or `presentation-verified`.

---

## 8. Board row: proposed replacement text, for the orchestrator to apply

`client/docs/task-board.md:108`. **Not edited by this packet.** Proposed disposition: **DONE**, with
the row's text replaced by:

> SP-079 moderation surface id, raw vs hygienic. **DONE** (SP-079). Resolved by the SECOND
> acceptance: the shared surface is a recorded decision, not a defect to fix. Both halves of the
> SP-069 output union deliberately share `interactive-reply-output` / `awareness-reply-output`,
> because a registry row is a coverage claim about a seam and both halves evaluate the same seam;
> `SurfaceId` has zero `client/src/**` consumers (SP-079 record §1). The two halves are instead made
> distinguishable in the channel the pipeline owns: a hygienic-only output block now emits the
> content-free diagnostic stable code `refused:output-hygienic`, while the raw half keeps
> `refused:output`. No surface was added; the 6 Wired / 5 Reserved pin is untouched. Revisit trigger:
> the first `client/src/**` read of `Block.SurfaceId` or `SoftHit.SurfaceId` reopens the decision,
> with minimum File Scope `AiModerationBoundary.cs:101-151`, `AiModerationCoverageTests.cs:242`
> (6 -> 7), and a new assertion arm before `AiModerationCoverageTests.cs:169-172`. Known limit,
> pinned rather than fixed: a soft hit on the same operation still masks the refusal code
> (SP-079 record §6 item 1, fact F6).

---

## 9. Floor accounting

`floor-delta.json`: `unit: 6`, `headless: 0`.

Pin at `client/tests/floor/floor.json:4` = **1022** unit, `:21` = **35** headless. **`floor.json` was
not edited.** Expected observed on this lane: **1028 unit / 35 headless** (pin + declared delta). The
gate reports a mismatch against 1022; that is the designed state for a bound packet, and the land
sums the deltas.
