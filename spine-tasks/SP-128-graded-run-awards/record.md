# SP-128 — the graded-run award path, and the defect it did not import

Branch `lane/SP-128-graded-run-awards`, worktree `.claude/worktrees/agent-a943493ef6684d075`,
base `71ab1bac2`. Plan checkpoint at `plan.md`, committed `dbbaa4c7e` **before any product edit**
and corrected at the plan gate (§6).

**Floor: pin 2399 unit / 144 headless; observed 2425 unit / 144 headless; declared delta
+26 / 0** (`floor-delta.json`). 2399 + 26 = 2425 and 144 + 0 = 144. The shared pin was never
opened. Warning gate: **0 warnings, 0 errors** across 4 projects, forced non-incremental.

---

## 1. The three thresholds, re-verified against the shipping source

Every line below was opened with `awk` on the numbered bytes at `71ab1bac2`. The first two are
additionally **re-derived from the shipping bytes on every test run**
(`ThePortsThresholds_MatchTheShippingSourceBytes`), so the port cannot drift from the source it
claims, and an upstream change reports here rather than surfacing later.

### 1.1 `top_of_the_class` — the 90% bar

| What | Where | Bytes |
|---|---|---|
| Constant | `Services/Quiz/IntakeHostService.cs:55` | `private const double TopMarksPercent = 90.0;` |
| Why not 100 | `IntakeHostService.cs:49-53` | *"Deliberately NOT full marks … a banded descent scores partly on pacing, so 100% is not a thing a real run reaches"* |
| Grade | `IntakeHostService.cs:426` | `var pct = run.MaxScore > 0 ? run.TotalScore / run.MaxScore * 100.0 : 0.0;` |
| Predicate | `IntakeHostService.cs:434` | `perfect: run.MaxScore > 0 && pct >= TopMarksPercent,` |
| Award | `Services/GamificationBridge.cs:598`, `:600` | `if (e.Perfect)` → `Ach?.TryUnlockExclusive("top_of_the_class");` |

`perfect ⇔ MaxScore > 0 ∧ (TotalScore / MaxScore)·100 ≥ 90.0`. **`≥`, not `>`.** The arithmetic
was already in the port verbatim (`IntakeQuizRun.cs`) and this slice added none of it — it added
the award. `IntakeGraded_Record_TreatsAZeroMaxRunAsNeverTopMarks` pins the OUTCOME at the award
path — a zero-max run awards nothing — and **not** the `MaxScore > 0` conjunct itself, which is
redundant with the percentage it guards (`:426`'s ternary already returns `0.0`) and therefore
unpinned by anything: removing it leaves every fact here green. Corrected at code review, which
installed exactly that edit and watched 40 facts stay green. It is ported verbatim regardless,
because a zero-max run has no grade at all.

### 1.2 `honor_roll` — three DISTINCT categories, and the clause order is behaviour

| What | Where | Bytes |
|---|---|---|
| Constant | `GamificationBridge.cs:40` | `private const int HonorRollCategories = 3;            // "top marks in 3 different categories"` |
| The set | `CCP.Core/Models/AchievementProgress.cs:169` | `public HashSet<string> PerfectedQuizCategories { get; set; } = new();` |
| Add-and-count | `GamificationBridge.cs:602-603` | `if (!string.IsNullOrEmpty(e.Category) && p.PerfectedQuizCategories.Add(e.Category)` / `    && p.PerfectedQuizCategories.Count >= HonorRollCategories)` |
| Award | `GamificationBridge.cs:605` | `Ach?.TryUnlockExclusive("honor_roll");` |
| Requirement text | `CCP.Core/Models/Achievement.cs:686` | *"Score 90% or better in 3 different categories"* |

Three clauses joined by `&&`, and **`&&` short-circuits, so the order is behaviour**:

1. an empty category never enters the set, and never blocks `top_of_the_class` (already awarded
   at `:600`);
2. `Add` runs next — **the award is attempted only on the run that GROWS the set**, including the
   case where three are already recorded but nothing was ever awarded;
3. `Count >= 3` — `≥`, so a fourth distinct category satisfies it too.

Ported clause for clause and pinned twice: behaviourally by
`HonorRoll_FiresOnlyOnTheRunThatGrowsTheSet`, and against the shipping bytes by
`TheUpstreamClauseOrder_IsStillEmptyThenAddThenCount`, so the two sides cannot be "corrected"
independently.

### 1.3 `held_back` — deliberately fail-streak-only, and ported as a FACT, not as code

| What | Where | Bytes |
|---|---|---|
| Constant | `GamificationBridge.cs:42` | `private const int HeldBackFailStreak = 3;             // "fail 3 in a row" (classic quiz only)` |
| Counter + award | `GamificationBridge.cs:592-595` | `p.QuizFailStreak++;` → `if (p.QuizFailStreak >= HeldBackFailStreak)` → `Ach?.TryUnlockExclusive("held_back");` |
| Reset on pass | `GamificationBridge.cs:586` | `p.QuizFailStreak = 0;` |
| Deliberateness, site 1 | `GamificationBridge.cs:574-575` | *"still fail-streak only … Left as-is deliberately (product decision)."* |
| Deliberateness, site 2 | `IntakeHostService.cs:418-420` | *"held\_back is deliberately left unwired (product decision): an intake has no fail state to be held back by, so `passed` is always true here"* |
| The proof | `IntakeHostService.cs:433` | the literal `passed: true,` |

`held_back ⇔ consecutive runs with passed == false ≥ 3`. **Not built.** The port's only producer
emits `passed: true` by construction, so the counter would be an integer nothing reachable can
increment — census §6.2 B10, *"GAP, and dead on arrival"*, and the packet names it residue.
`teachers_pet` (`:41`, `:588-589`) is likewise not built: census §6.1 names the unit's awards as
exactly two. Both recorded as **D231**, with the closed `AwardableIds` list pinned so neither can
be half-added later.

---

## 2. Where I normalised, and why that point is the port's to own

Upstream's chain, re-derived: the consumer adds the category **raw** (`GamificationBridge.cs:602`)
into a **default** `HashSet<string>` (`AchievementProgress.cs:169` — ordinal, case-sensitive), while
its two producers disagree — the intake normalises (`IntakeHostService.cs:427-429`) and the classic
quiz passes an unnormalised PascalCase enum name (`QuizWindow.xaml.cs:540`,
`QuizCategory.cs:6-13`). `"sissy"` and `"Sissy"` fill two of three slots, `honor_roll` fires early,
and `AchievementService.cs:71-74` persists it so it never un-fires. **Latent, not reproduced** — the
classic launcher is `Visibility="Collapsed"` (`GradedIntakeTabView.xaml:140`) and nothing was run.

**The port normalises at the CONSUMER**, because that is new port code (nothing upstream is copied
there), it is the one point every producer must pass through, and producer-side normalisation is
safe only while exactly one producer is reachable — precisely the property
`GamificationBridge.cs:566-567` says the handler does not want to depend on.

**Two layers, and they catch different edits** (§4 measured this rather than assuming it):

| Layer | What | The edit it alone catches |
|---|---|---|
| Entry normalisation | `GradedRunAwards.NormalizeCategory` — `Trim().ToLowerInvariant()`, the same call as `IntakeHostService.cs:427-429` | a future producer shaped like `QuizWindow` |
| The named comparer | one `StringComparer.OrdinalIgnoreCase`, re-wrapped onto the set at JSON bind | a value that never passed the entry point — a hand-edited `graded_run_awards.json`, or a file written by another build |

`IntakeGraded.Category` was **not** moved or weakened: it stays byte-identical to upstream, which
is what the closed census guard pins (`TrainerCardCensusTests`, `NormalisationExpression`).

---

## 3. What was built, and what was deliberately not

Built: `Features/Progression/GradedRunAwards.cs` (document + consumer), the adapter
`IntakeGraded.Record`, the fourth store on `IntakeHostContext`, and the call site in
`IntakeHostWindow.OnQuizResult`.

**Two decisions the packet did not pre-decide, both recorded as divergences rather than taken
silently:**

1. **A narrow awarded-id record IS built (the packet's "finding and a board row").** An award path
   with nothing to award into is a no-op, and upstream's idempotence
   (`AchievementService.cs:1115`) is unportable without a persisted record. Census §6.1 puts
   exactly this inside the buildable unit. What is built is a closed two-id set with `IsAwarded`
   — **not** the B4 achievement ledger: no catalogue of 70, no badge art, no popup, no cloud
   restore, no wardrobe gate. **B4 stays GAP.** Proposed board row for the orchestrator, since
   `client/docs/task-board.md` is out of scope for a lane: *"the achievement ledger proper (B4) —
   SP-128 built its first consumer and a two-id record; the catalogue, the gating predicate and
   the 63 gated wardrobe items are unbuilt."*
2. **The patron entitlement gate is NOT ported** — D228, with the revisit triggers named in the
   row body. Full reasoning in `plan.md` §3.2; the coordinator verified every leg at the plan gate.

Mechanisms deliberately not ported, each with its reason in the ledger: the static event and its
args (D229), `MarkDirty` as a flag (D230), the passed branch (D231). One further upstream mechanism
has no port analogue and is recorded here rather than as a divergence: `TryUnlock`'s unknown-id
refusal (`AchievementService.cs:864-868`) exists because upstream's method is public and called
with 70 literals; here the award ids are a closed list with two call sites, so an unknown-id branch
would be unreachable code. `AwardableIds` is pinned instead.

---

## 4. The red-on-regression demonstrations — every one installed, built, run, and reverted

Ten edits. Each row is an observed run, not a prediction.

| # | Edit installed | Observed |
|---|---|---|
| 1 | `CategoryComparer` `OrdinalIgnoreCase` → `Ordinal`, **entry normalisation intact** | **3 RED**, 23 pass: `DistinctCategories_StayCaseInsensitive_AcrossThePersistedRoundTrip` (`Assert.False() Failure … Actual: True`, line 183), `ADocumentAlreadyHoldingBothCasings_CollapsesOnLoad…`, `TheNamedComparers_FoldCaseForCategories…`. **`HonorRoll_DoesNotFireEarly…` PASSED** |
| 2 | `NormalizeCategory` → identity, **comparer intact** | **5 RED**, 21 pass: four `NormalizeCategory_TrimsAndFoldsCase…` theory cases + `WhitespaceOnlyCategory_IsRejected…`. **`HonorRoll_DoesNotFireEarly…` PASSED** |
| 3 | **BOTH of the above** | **`HonorRoll_DoesNotFireEarly_WhenTheTwoProducersDisagreeAboutCase` RED** — `duplicate.CategoryWasNew` was `True`, i.e. the port reproduced upstream's early fire exactly |
| 4 | ignore `Add`'s return value (drop `:602`'s middle clause) | **3 RED** incl. `HonorRoll_FiresOnlyOnTheRunThatGrowsTheSet` |
| 5 | `HonorRollCategories` 3 → 2 | **3 RED** incl. `HonorRoll_FiresAtTheThirdDistinctCategory_AndNotTheSecond` **and `ThePortsThresholds_MatchTheShippingSourceBytes`** — the source re-derivation caught the constant drifting from `GamificationBridge.cs:40` |
| 6 | delete `IntakeGraded.Record(...)` from `OnQuizResult` (kept compiling) | **1 RED**, 25 pass: only `TheIntakeCompletionPath_CallsTheAwardRecorder_AtItsOwnSeam`. Every behavioural fact stayed green — which is the gap it exists to close and the exact limit of what it proves |
| 7 | drop the `if (!topMarks)` guard | **RED** incl. `NotTopMarks_AwardsNothing_AndRecordsNoCategory`, `IntakeGraded_Record_TreatsAZeroMaxRunAsNeverTopMarks` |
| 8 | drop `TryAward`'s `IsAwarded` pre-check | **RED** incl. `TopOfTheClass_IsAwardedOnce_AcrossRepeatedTopMarksRuns`, `Awards_SurviveTheRoundTrip_AndNeverReFire` |
| 9 | move the empty-category guard above the `top_of_the_class` award | **2 RED**: `EmptyCategory_StillAwardsTopOfTheClass_ButRecordsNothing`, `WhitespaceOnlyCategory_IsRejected…` |
| 10 | save unconditionally (drop the change test) | **see §4.1** |

### 4.1 One guard failed its own demonstration, and that is the most useful thing here

Demo 10 was run twice.

**First mechanism — `ARunThatChangesNothing_WritesNothing`, asserting `store.IsDirty` stays false:
IT PASSED WITH THE DEFECT INSTALLED.** A completed write clears the dirty flag
(`PersistenceStore.cs:498-503`), so `IsDirty` is false whether or not a save was issued. The name
said "writes nothing"; the mechanism observed "is not dirty". **A description outrunning its
mechanism — the packet's named failure class, in my own test, caught only because the demonstration
was actually run.**

**Corrected mechanism** — count COMPLETED atomic renames through the injected `AtomicWriteHooks`,
and quiesce on the chained writer rather than any clock (`SaveImmediate()` awaits the whole enqueued
chain, so a stray write issued earlier has necessarily completed and been counted). Renamed
`ARunThatChangesNothing_EnqueuesNoWrite`. Re-run with the same defect: **RED, `Expected: 3, Actual:
4`.** The count reconciles exactly — the below-bar run still returns at the intact `!topMarks`
guard and never reaches the save, so the defect adds one write, not two.

### 4.2 The claim I had to withdraw

My first doc comment said `HonorRoll_DoesNotFireEarly…` "reds if the consumer stops normalising OR
if the comparer goes ordinal". **Demos 1 and 2 disproved it**: either layer alone still yields the
right answer, so it reds only when BOTH regress. The comment now states the measured matrix
instead, and each single-layer regression has its own pin. **No fact was weakened to make this
true** — the mapping was corrected to match what the mechanisms do.

---

## 5. The sweep: predicates discharged or withdrawn

- **"Awards what upstream awards at upstream's thresholds"** — **scoped, not withdrawn.** True for
  the perfect branch (`:598-609`). The passed branch is D231 and the claim explicitly excludes it.
- **"Every consumer enumerated by `grep`"** — **discharged.** `grep -rn "IntakeGraded\.\|GradedRunAwards\|AwardsStore"`
  over `client/src`: exactly one product call site (`IntakeHostWindow.axaml.cs:551`), one
  construction site (`IntakeHostContext.cs:167-180`), one adapter (`IntakeQuizRun.cs:188`). No
  other consumer exists.
- **"The clause order is ported"** — **discharged** twice (§1.2).
- **"The comparer is case-insensitive and pinned"** — **discharged** behaviourally (demo 1) and
  structurally.
- **"A run that changes nothing writes nothing"** — **discharged only after the mechanism was
  replaced** (§4.1).
- **"The seam is wired"** — **NOT discharged as behaviour.** Source-level only; see §7.
- **"Cross-platform by construction"** — **withdrawn as a claim, kept as an argument.** The path
  has no OS interop, which is an argument, not an execution. Nothing ran on Linux.

---

## 6. Citation audit — and one wrong accusation I made and corrected

Full tables in `plan.md` §7. Summary:

- **The census is clean: 22 of 22 references re-derive exactly, including the one I accused.** My
  first draft claimed `trainer-card-census.md:180`'s `IntakeHostService.cs:418-420` was off by one.
  **It is exact; my `:419-421` was the error.** Corrected in `plan.md` in both places at the plan
  gate, before `record.md` could carry it. Two census ranges are over-inclusive by one line at the
  tail (`:578-611`, `:574-576`); both are defensible range choices, not errors. **The census was
  not edited.**
- **I did not invent that number, I inherited it** (D232): `IntakeQuizRun.cs`'s category citation
  pointed at `:418-420`, which today is the `held_back` comment — the semantic opposite of its
  claim. Reasoning from it shifted the real block by one in my head. **A wrong citation propagated
  into a new packet within one reading.**
- **Seven stale citations in `IntakeGraded`, all repaired.** Classified against SP-058's own
  baseline `0c9947a6`: **four were wrong the day they were written** — two recorded by D223, and
  **two not previously recorded** (`:437-438` points at prose rather than the mantra credit;
  `:406-422`'s head lands inside the `held_back` comment). Three merely drifted. Cause verified,
  not guessed: `git log 0c9947a6..HEAD` on that file returns **exactly one commit**, `f7b4c317c`
  (+106/-1).
- **Thirteen more repaired** in `IntakeHostWindow.axaml.cs` — the completion-loop block I edited
  through, its section marker, and the boot-contract citations, each target re-derived.
- **Two repaired in `Persistence/`** (plan-gate finding 8): `PersistenceStore.cs:204` and
  `PersistenceStoreTests.cs:110` cited `IntakeHostContext.cs:126-127` for a flush-before-stop.
  **My first repair was itself wrong** — I wrote `:186-188` from arithmetic instead of opening the
  file; the flushes are at `:212-214`. Corrected by opening it.
- **Remaining debt, named rather than absorbed:** the transport, protocol and teardown blocks of
  `IntakeHostWindow.axaml.cs` were **not** audited and are likely stale by the same shift. **No
  standing gate re-derives port-side citations**, which is why four instances accumulated in one
  file unnoticed.

---

## 7. What this packet does NOT prove

- **Nothing was rendered, composited, clicked, or run as an application.** Every fact is pure logic
  over a temp directory and an injected write hook. **No headless frame, no headed capture** —
  `draw-verified` and `presentation-verified` are untouched.
- **The window call site is compile-only.** `TheIntakeCompletionPath_CallsTheAwardRecorder_AtItsOwnSeam`
  reads the product TEXT. Demo 6 proved it reds on deletion **and** that every behavioural fact
  stayed green through that same deletion. It is not evidence the window opens, that a
  `quiz-result` arrives, or that the handler ever runs. The end-to-end path is the headed
  `--intake-drive` transcript, **not run in this slice**.
- **Linux is unproven, and so is Windows at runtime.**
- **The award is written to a store nothing in this port can render** (census B1). A user observes
  it today only by opening the file — SP-127's own named soft spot, which this slice does not
  harden.
- **The upstream defect remains reasoning over source, not a reproduction.** The app was never run.
  What demo 3 shows is that the PORT reproduces the same arithmetic when both its layers are
  removed — evidence about this port, not about the shipping app.
- **`IntakeHostContext.Dispose`'s worst case rises from 12 s to 17 s** (three flushes at 3 s, four
  stops at 2 s), reusing SP-073's existing bounds unchanged. That is a bound, not a measurement:
  no teardown was timed here.
