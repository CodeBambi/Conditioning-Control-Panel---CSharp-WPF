# SP-128 — plan checkpoint (step 1). Written BEFORE any product edit.

Branch `lane/SP-128-graded-run-awards`, worktree
`.claude/worktrees/agent-a943493ef6684d075`, base `71ab1bac2`.
Floor pin at plan time: **2399 unit / 144 headless**.

Every line quoted below was opened in this worktree at `71ab1bac2` with `awk`/`sed` on the
shipping tree. Nothing here is inherited from the census, from SP-058's comments, or from the
packet text — each was re-derived, and the three places where the census's range ends differ
from the bytes are named in §7.

---

## 1. The three thresholds, re-verified against the shipping source

### 1.1 `top_of_the_class` — the 90% bar

| What | Where, opened | Exact bytes |
|---|---|---|
| The constant | `ConditioningControlPanel/Services/Quiz/IntakeHostService.cs:55` | `private const double TopMarksPercent = 90.0;` |
| Why not 100 | `IntakeHostService.cs:49-53` | *"Deliberately NOT full marks and deliberately the same 90 the classic quiz used … a banded descent scores partly on pacing, so 100% is not a thing a real run reaches"* |
| The grade | `IntakeHostService.cs:426` | `var pct = run.MaxScore > 0 ? run.TotalScore / run.MaxScore * 100.0 : 0.0;` |
| The predicate | `IntakeHostService.cs:434` | `perfect: run.MaxScore > 0 && pct >= TopMarksPercent,` |
| The award | `Services/GamificationBridge.cs:598`, `:600` | `if (e.Perfect)` → `Ach?.TryUnlockExclusive("top_of_the_class");` — **unconditional inside the perfect branch**, no category test |
| Prose restatement | `GamificationBridge.cs:571` | *"one run graded at or above the top-marks bar (90%)"* |

**Arithmetic, not paraphrase:** `perfect ⇔ MaxScore > 0 ∧ (TotalScore / MaxScore) * 100 ≥ 90.0`.
`≥`, not `>`. The `MaxScore > 0` guard is redundant with the percentage (a zero max already
yields `0.0`) and is ported anyway, because a zero-max run has no grade at all.

**Already present in the port, verbatim** — `Features/Intake/IntakeQuizRun.cs:139` (`TopMarksPercent
= 90.0`), `:142-143` (`ScorePercent`), `:147-148` (`IsTopMarks`), pinned WITH its comparison by
`IntakeGradedTests`. **This packet adds no arithmetic here; it adds the award.**

### 1.2 `honor_roll` — three DISTINCT categories

| What | Where, opened | Exact bytes |
|---|---|---|
| The constant | `Services/GamificationBridge.cs:40` | `private const int HonorRollCategories = 3;            // "top marks in 3 different categories"` |
| The set | `CCP.Core/Models/AchievementProgress.cs:169` | `public HashSet<string> PerfectedQuizCategories { get; set; } = new();` — a **default** comparer |
| The add-and-count | `GamificationBridge.cs:602-603` | `if (!string.IsNullOrEmpty(e.Category) && p.PerfectedQuizCategories.Add(e.Category)`<br>`    && p.PerfectedQuizCategories.Count >= HonorRollCategories)` |
| The award | `GamificationBridge.cs:605` | `Ach?.TryUnlockExclusive("honor_roll");` |
| Prose restatement | `GamificationBridge.cs:572-573` | *"top marks in `HonorRollCategories` distinct categories; from the intake that is distinct niches"* |
| Requirement text | `CCP.Core/Models/Achievement.cs:686` | *"Score 90% or better in 3 different categories"* |

**Arithmetic, not paraphrase.** Three ordered clauses joined by `&&`, and the order is
behaviour:

1. `!string.IsNullOrEmpty(category)` — an empty category never enters the set, and never blocks
   `top_of_the_class`, which already fired at `:600`.
2. `set.Add(category)` — **the award is attempted only on the run that GROWS the set.** A perfect
   run in an already-recorded category short-circuits here and never reaches the count. This is
   not incidental: with `Add` returning false the third clause is not evaluated at all.
3. `set.Count >= 3` — `≥`, so a fourth distinct category also satisfies it (and
   `TryUnlockExclusive` is idempotent, `AchievementService.cs:1115`).

### 1.3 `held_back` — deliberately fail-streak-only, and therefore NOT BUILT here

| What | Where, opened | Exact bytes |
|---|---|---|
| The constant | `Services/GamificationBridge.cs:42` | `private const int HeldBackFailStreak = 3;             // "fail 3 in a row" (classic quiz only)` |
| The counter + award | `GamificationBridge.cs:592-595` | `p.QuizFailStreak++;` → `if (p.QuizFailStreak >= HeldBackFailStreak)` → `Ach?.TryUnlockExclusive("held_back");` |
| The deliberateness, first site | `GamificationBridge.cs:574-575` | *"still fail-streak only. An intake has no fail state, so this can only ever come from the classic quiz. Left as-is deliberately (product decision)."* |
| The deliberateness, second site | `Services/Quiz/IntakeHostService.cs:418-420` | *"held\_back is deliberately left unwired (product decision): an intake has no fail state to be held back by, so `passed` is always true here and the bridge's fail streak is never incremented from this path."* |
| The producer that proves it | `IntakeHostService.cs:433` | the literal `passed: true,` |

**Arithmetic:** `held_back ⇔ (consecutive runs with passed == false) ≥ 3`, and the streak resets
to `0` on any pass (`GamificationBridge.cs:586`).

**So the threshold is ported as a FACT, not as code.** The only producer this port has emits
`passed: true` by construction — `IntakeGraded` has no fail concept at all — so a fail-streak
counter here would be an integer that no reachable path can ever increment. The census books it
GAP and *"dead on arrival"* (`trainer-card-census.md` §6.2, row B10), and the packet's trap #1
names `held_back`'s dead-on-arrival status as residue. **Not built.** §5 states what that costs.

### 1.4 The fourth threshold, named so its absence is deliberate rather than missed

`TeachersPetPasses = 25` (`GamificationBridge.cs:41`), awarded at `:588-589` off a persisted
`p.QuizzesPassed` counter. The census puts it in **§6.2 residue** (*"Not in the row's phrases;
found by the walk"*) and **§6.1 names the buildable unit's awards as exactly two** —
*"award `top_of_the_class` and `honor_roll`"*. The packet's threshold section lists three and
`teachers_pet` is not among them. **Not built**, and §5 prices it.

---

## 2. Where I normalise, and why that point is the port's to own

### 2.1 The defect, re-derived rather than accepted

| Link | Bytes, opened | Cited |
|---|---|---|
| The distinct set | `public HashSet<string> PerfectedQuizCategories { get; set; } = new();` | `CCP.Core/Models/AchievementProgress.cs:169` |
| The consumer | `p.PerfectedQuizCategories.Add(e.Category)` — raw, no trim, no fold | `Services/GamificationBridge.cs:602` |
| Producer A (intake) | `run.Niche.Trim().ToLowerInvariant()` | `Services/Quiz/IntakeHostService.cs:427-429` |
| Producer B (classic quiz) | `var categoryId = catDef?.Id ?? result.Category.ToString();` | `Windows/QuizWindow.xaml.cs:540` |
| B's fallback alphabet | `Sissy, Bambi, Obedience, Mindlessness, Submission` | `CCP.Core/Models/Quiz/QuizCategory.cs:6-13` |
| The handler's own stated property | *"The handler is source-agnostic on purpose: it reads a grade and a category and does not care which surface produced them."* | `GamificationBridge.cs:566-567` |

`new()` on a `HashSet<string>` is `EqualityComparer<string>.Default` — ordinal, case-sensitive,
whitespace-sensitive. So `"sissy"` and `"Sissy"` are two of the three slots, `honor_roll` fires a
category early, and `AchievementService.cs:71-74` writes the set to
`%APPDATA%/ConditioningControlPanel/achievements.json`, so the duplicate is permanent.

**Bounded exactly as SP-127 bounded it: latent, not reproduced.** Nothing was executed here
either. The port carries no defect today because it raises nothing at all.

### 2.2 The port's boundary, and why it is the consumer

The port owns **new code on the consumer side**. Nothing upstream is being copied there — the
consumer does not exist yet. That makes the consumer the first point the port gets to choose,
and it is the ONE point every producer must pass through. Producer-side normalisation is safe
only while exactly one producer is reachable, which is precisely the property
`GamificationBridge.cs:566-567` says the handler does not want to depend on.

**Two layers, and they are not redundant — they catch different edits:**

| Layer | What it is | The edit it survives |
|---|---|---|
| **Entry normalisation** | `GradedRunAwards.NormalizeCategory` — `Trim().ToLowerInvariant()`, the same call as `IntakeHostService.cs:427-429` — applied at the consumer, on every category from every caller | a future producer that forgets to normalise (producer B's exact shape) |
| **The set's comparer** | ONE named `StringComparer.OrdinalIgnoreCase`, `GradedRunAwards.CategoryComparer`, and the document re-wraps any set it is handed with it | a value that never passed the entry point at all: a hand-edited `graded_run_awards.json`, or a file written by another build |

The second layer is the one the packet asks to be pinned, and it is pinnable **behaviourally**
precisely because the disk is a producer the entry point does not see. See §4.

`IntakeGraded.Category` (`IntakeQuizRun.cs:153-154`) is NOT moved or weakened: it stays verbatim
upstream-matching (census §5.1). The consumer normalising again is idempotent on its output.

### 2.3 One further consequence, taken deliberately

Upstream guards with `!string.IsNullOrEmpty(e.Category)` (`:602`), which lets a whitespace-only
category into the set. The port normalises FIRST, so `"   "` becomes `""` and is rejected. This
is a second, smaller place where consumer-side normalisation makes the port behave better than
its source. Recorded as part of the divergence, and pinned.

---

## 3. What gets built (and the shape of it)

New, `client/src/CcpClient.Desktop/Features/Progression/`:

- **`GradedRunAwardsDocument`** — schema-versioned store document: the distinct perfected
  categories and the awarded ids. Both `HashSet<string>` properties re-wrap through a named
  comparer on set, so the comparer survives JSON binding no matter which binding strategy
  `System.Text.Json` picks for a collection property.
- **`GradedRunAwards`** — the consumer: `CategoryComparer`, `HonorRollCategories = 3`, the two
  award ids as a CLOSED set, `IsAwarded(id)`, `DistinctPerfectedCategories`, and
  `RecordGradedRun(bool topMarks, string? category)` which ports `GamificationBridge.cs:598-609`
  clause for clause, returning a typed outcome naming what it did.

Changed, `client/src/CcpClient.Desktop/Features/Intake/`:

- **`IntakeQuizRun.cs`** — `IntakeGraded.Record(awards, run)`: the source-agnostic adapter,
  `RecordGradedRun(IsTopMarks(run), Category(run))`. One place, unit-testable.
- **`IntakeHostContext.cs`** — the awards store started beside the other three, flushed and
  stopped in `Dispose`, exposed as `Awards`.
- **`IntakeHostWindow.axaml.cs`** — step 1b at `:538-543` stops discarding the verdict: it calls
  `IntakeGraded.Record` and logs the OUTCOME instead of logging "typed seam".

Nothing else. No wardrobe, no achievement catalogue, no badge art, no popup, no card page, no
banners, no leaderboard, no fail streak, no pass counter.

### 3.1 The one residue member the award path genuinely needs — stated as a finding

An award path with nothing to award INTO is a no-op, and upstream's idempotence
(`AchievementService.cs:1115`, *"if (\_progress.IsUnlocked(id)) return false"*) is unportable
without a persisted record of what was awarded. Census §6.1 puts exactly this inside the
buildable unit: *"an award ledger with `IsUnlocked(id)`/`TryUnlockExclusive(id)` over the existing
`PersistenceStore<TModel>`"*.

**So I build the narrowest thing that satisfies it and say so plainly:** a persisted set of
awarded ids, restricted to a CLOSED two-id set, with `IsAwarded` and an internal `TryAward`.
It is **not** the B4 achievement ledger: no catalogue of 70, no categories, no hidden/exclusive
metadata, no badge images, no popup, no cloud restore, no wardrobe gate. B4 stays GAP.
This is the packet's *"if the award path genuinely needs one, that is a finding and a board row"*
— reported in `record.md` and in the final report as a proposed board row, since
`client/docs/task-board.md` is out of scope for a lane.

### 3.2 The entitlement question, which the census did not price

**Found by opening the code, not stated anywhere in the packet.** All four graded-run
achievements are `IsExclusive = true` (`CCP.Core/Models/Achievement.cs:670, 680, 690, 700`), and
upstream awards them through `TryUnlockExclusive`, which refuses unless
`App.Patreon?.HasPremiumAccess == true` (`AchievementService.cs:1107`, gate at `:1116-1120`).
`GamificationBridge.cs:88` labels the whole group *"(patron: …)"*.

The port has **no Patreon authority at all**. `IntakePassService.NoEntitlementSource` answers
`IsPremium = false, IsLoggedIn = null` (`IntakePassService.cs:88-97`) — which in this port's own
vocabulary is *"I could not tell"*, not *"you are not a patron"*.
`Entitlement/EntitlementOutcome.cs:7-17` states the rule in as many words: **those two answers
"must never collapse into each other"**, and `NotEntitled` has exactly one legitimate producer,
an authority that explicitly answered.

Gating the port's award on `IsPremium` would therefore (a) violate that rule by reading absence
as refusal, and (b) make this entire packet a path that no run of this build can reach — the
dead-letter outcome `IntakeHostService.cs:49-53` and `GamificationBridge.cs:565-566` both
complain about.

**Decision: the port records the award unconditionally, and that is a recorded divergence, not a
silent drop.** It is in the user-favourable direction, which is the port's standing tie-break at
this exact seam (`IntakeHostWindow.axaml.cs:520-522`). It becomes revisitable the day an
entitlement authority exists. Divergence row, cited on both sides.

---

## 4. Which edit each new guard must red on

Written before the assertions exist. Each row names the single-token edit I will actually make
and watch, and the demonstration is recorded in `record.md` with the observed failure text.

| # | Fact | The edit it must RED on |
|---|---|---|
| G1 | `TopOfTheClass_Awards_OnTheFirstTopMarksRun` | delete the `TryAward(TopOfTheClassId)` call at the head of the perfect branch |
| G2 | `NotTopMarks_AwardsNothing_AndRecordsNoCategory` | drop the `if (!topMarks) return` guard (upstream `:598`) |
| G3 | `TopOfTheClass_IsAwardedOnce_AcrossRepeatedTopMarksRuns` | remove the `IsAwarded` pre-check (upstream `AchievementService.cs:1115`) |
| G4 | `HonorRoll_FiresAtTheThirdDistinctCategory_AndNotTheSecond` | `HonorRollCategories` 3 → 2 (and → 4) |
| G5 | **`HonorRoll_DoesNotFireEarly_WhenProducersDisagreeAboutCase`** — `"sissy"`, `"Sissy"`, `"bambi"` ⇒ 2 distinct, NO award | remove the entry normalisation **or** flip the comparer to `Ordinal` (either alone reds it) |
| G6 | **`DistinctCategories_StayCaseInsensitive_AcrossThePersistedRoundTrip`** — a `graded_run_awards.json` on disk holding `"Sissy"`, then a perfect `"sissy"` run ⇒ still 1 | **`CategoryComparer` `OrdinalIgnoreCase` → `Ordinal`, with the entry normalisation left fully intact.** This is the comparer-specific pin the packet asks for: the disk value never passes the entry point, so only the comparer can catch it |
| G7 | `TheDistinctSetsComparer_IsTheNamedCaseInsensitiveOne` — structural, on a fresh document AND on a loaded one | the same comparer flip; reds even if some future caller pre-normalises everything |
| G8 | `HonorRoll_FiresOnlyOnTheRunThatGrowsTheSet` — 3 already on disk, unawarded, then a perfect run in an already-recorded category ⇒ no award | ignore `Add`'s return value (drop the middle clause of `GamificationBridge.cs:602`) |
| G9 | `EmptyCategory_StillAwardsTopOfTheClass_ButRecordsNothing` | move the empty-category guard above the `top_of_the_class` award |
| G10 | `WhitespaceOnlyCategory_IsRejected_ByTheConsumersNormalisation` | remove the entry normalisation (upstream's raw `IsNullOrEmpty` would admit `"   "`) |
| G11 | `Awards_SurviveTheRoundTrip_AndNeverReFire` | drop the `Save()` after an award, or drop `AwardedIds` from the document |
| G12 | `AwardIds_AreAClosedSet_AndAnUnknownIdIsRefused` | widen the closed set (upstream's `Achievement.All.TryGetValue` refusal, `AchievementService.cs:864-868`) |
| G13 | `ThePortsThresholds_MatchTheShippingSourceBytes` — re-derives `HonorRollCategories` from `Services/GamificationBridge.cs` and `TopMarksPercent` from `Services/Quiz/IntakeHostService.cs` by regex and compares to the port's constants | change the port's constant, **or** the shipping source changing under it. This is the row that stops the packet pinning a number it merely copied from prose |
| G14 | `TheIntakeCompletionPath_CallsTheAwardRecorder` — the call site exists in `IntakeHostWindow.axaml.cs` | delete the `IntakeGraded.Record(` call from `OnQuizResult`. **Named for exactly what it is:** a source-level chokepoint pin. It is NOT evidence the window runs, renders, or is reachable |
| G15 | `IntakeGraded_Record_FeedsTheConsumerTheNormalisedNicheAndTheTopMarksVerdict` | swap `IsTopMarks(run)`/`Category(run)` for anything else in the adapter |

G6 and G7 together are the packet's step 3. G6 is behavioural and G7 is structural, and neither
subsumes the other: G7 would survive a document that stopped being the thing consulted, G6 would
survive a comparer that was correct for the wrong reason. Both will be watched red.

## 5. What the eleven absent members mean for what I am NOT building

Census §4 enumerates sixteen consumer-side members; five present, eleven absent. This packet
closes **six** of the eleven and deliberately leaves **five**, each with the reason:

| # | Absent member | This packet |
|---|---|---|
| C6 | `QuizService.QuizCompleted` static event | **not ported as an event.** A process-wide static event is the mechanism, not the outcome; the port calls the consumer directly from the one producer it has. The source-agnostic property is preserved in the consumer's SIGNATURE (a grade verdict + a category), which is what `GamificationBridge.cs:566-567` actually asserts |
| C7 | `RaiseQuizCompleted` | **closed** — the seam at `IntakeHostWindow.axaml.cs:538-543` stops discarding |
| C8 | `QuizCompletedEventArgs` | **not ported** — same reason as C6; its two members this path reads become the consumer's two parameters |
| C9 | `OnQuizCompleted` handler | **closed for the perfect branch** (`:598-609`); the pass/fail branch (`:584-596`) is C10/C12/C13 |
| C10 | `TeachersPetPasses = 25` | **left** — §1.4; census §6.2 residue |
| C11 | `HonorRollCategories = 3` | **closed** |
| C12 | `HeldBackFailStreak = 3` | **left** — §1.3; unreachable by construction here |
| C13 | `QuizzesPassed` / `QuizFailStreak` | **left** — the state C10 and C12 need |
| C14 | `PerfectedQuizCategories` | **closed, and deliberately NOT verbatim** — §2 |
| C15 | `TryUnlockExclusive` | **closed narrowly** — §3.1 (closed id set) and §3.2 (no entitlement gate) |
| C16 | `MarkDirty` | **closed as its outcome, not its mechanism.** Upstream's flag feeds a 30 s save timer; the port's store has no timer, so the outcome ("the change reaches disk") is a `Save()` on the runs that actually changed something. A run that changes nothing writes nothing — which upstream never has, because it always at least moves a counter |

**Five left, and all five are one subsystem: the pass/fail counters.** That is the honest shape
of this packet — it builds the perfect branch whole and leaves the passed branch entirely.

## 6. Divergences to record: **D226-D231**, inside the assigned D226-D239

`client/docs/wpf-surface-reachability.md`, new `## SP-128` section:

- **D226** — the early-firing `honor_roll`, and the port's deliberate correction (the headline).
- **D227** — the whitespace-only category upstream admits and the port rejects.
- **D228** — `TryUnlockExclusive`'s patron gate, dropped, with §3.2's reasoning.
- **D229** — the static event / event args, not ported as mechanism.
- **D230** — `MarkDirty` → save-on-change.
- **D231** — the passed branch (`teachers_pet`, `held_back`) left whole, with its cost.

## 7. Citation audit (census CLOSED — reported, never edited)

**Corrected at the plan gate. My first draft of this section accused the census of a
one-line error at `IntakeHostService.cs:418-420` and the accusation was WRONG — mine was the
wrong citation, not the census's.** Re-derived with `awk` on the numbered bytes: `:418` is
`// held_back is deliberately left unwired (product decision): an intake has no`, `:419-420`
finish the sentence, `:421` is the bare `//`. `trainer-card-census.md:180`'s `:418-420` is
**exact**. The claim is corrected in §1.3 as well as here, so nothing downstream inherits it.

That mistake was inherited rather than invented, and finding its source is item 2.

### 7.1 The census re-derives cleanly — 22 of 22

`:40`, `:41`, `:42`, `:55`, `:169`, `:29`, `:32-35`, `:71-74`, `:418-420`, `:426`, `:427-429`,
`:434`, `:540`, `:566-567`, `:571`, `:572-573`, `:578`, `:598`, `:600`, `:602-603`, `:605`,
`:609`. **No wrong citation found in `trainer-card-census.md`, including the one I accused.**

Two ranges are over-inclusive by exactly one line at the tail and neither is an error:
§6.1's `GamificationBridge.cs:578-611` stops one short of the method's closing brace at `:612`
(34 lines by its own range), and B10's `:574-576` includes `/// </list>` after the sentence at
`:574-575`. Both are defensible range choices, recorded for exactness only.

### 7.2 THE REAL DEFECT: seven stale citations inside `IntakeGraded` — the class this packet builds on

`client/src/CcpClient.Desktop/Features/Intake/IntakeQuizRun.cs:123-159`. Its class header fixes
the base file as `IntakeHostService.cs`, so every bare `:NNN` below resolves against it. Each
target re-derived at `71ab1bac2`:

I extracted SP-058's own stated baseline (`git show 0c9947a6:…IntakeHostService.cs`) and read the
numbered bytes THERE too, so each citation is classified by whether it drifted or was born wrong:

| Port line | Cites | Claims it is | At `0c9947a6` that is | Today | Class |
|---|---|---|---|---|---|
| `:127` | `:406-422` | the `RaiseQuizCompleted` block | `:406-411` is the **held\_back comment**; the emit is `:419-423` | `:431-435` | **BORN WRONG** (range head lands in the wrong block) + stale |
| `:128` | `:435-441` | the mantra loop | contains the loop (`:439-441`) plus 4 lines of comment above it | `:451-453` | drifted |
| `:136` | `:45-53` | the `TopMarksPercent` rationale | `:45-51` summary, `:52` const, `:53` blank | `:48-55` | drifted |
| `:141` | `:414` | the grade | `:414` **IS** `var pct = …` | `:426` | **was exact**, drifted by 12 |
| `:145` | `:417` | the perfect guard | `: run.Niche.Trim().ToLowerInvariant();` — the guard is `:422` | `:434` | **BORN WRONG by 5** (D223) + stale |
| `:150` | `:418-420` | the category normalisation | `:418` blank, `:419-420` the raise call — normalisation is `:415-417` | `:427-429` | **BORN WRONG by 3** (D223) + stale, **and actively misleading today** |
| `:156` | `:437-438` | the mantra credit | the two COMMENT lines above it — the credit is `:439` | `:451` | **BORN WRONG** (points at prose, not code) + stale |

**A finding beyond D223, which recorded two.** There are **four** born-wrong citations in this
block, not two: the census caught `:145` and `:150`; `:127` and `:156` were not examined by it
(census §4.1 checked three comments, not seven). Three more merely drifted. So the block is
**7 of 7 wrong today, 4 of 7 wrong the day it was written.**

`:150` is the one that costs. Follow it today and you land on the `held_back` comment —
semantically the OPPOSITE of what the port comment says sits there. That is where my `:419-421`
came from: I reasoned from a port comment that pointed at the wrong block and shifted the real
one by a line to make room for it. **A wrong citation propagated into a new packet within one
reading.** This is the exact SP-127/D223 class, and it reproduced itself in front of me.

**Cause, verified rather than assumed:** `git log 0c9947a6..HEAD -- Services/Quiz/IntakeHostService.cs`
returns **exactly one commit** — `f7b4c317c` *"feat(media): remote media app-wide - Scrolller as
an asset source beyond FYP"*, `+106/-1` on that file. SP-058's baseline was `0c9947a6` (v6.7.4),
so one unrelated upstream commit invalidated the whole block at once.

**Disposition: REPAIR all seven.** `Features/Intake/**` is in this packet's May-change list; the
census guard's only port-side pin is the verbatim normalisation EXPRESSION
(`trainer-card-census.md` §9.4, `port-normalisation-matches`), not these comments, so repairing
them cannot move the closed guard. Repaired in the same commit as the award path, and recorded in
`record.md` with this table.

### 7.3 Same drift, same method: the completion-loop citations I am editing through

`IntakeHostWindow.axaml.cs`'s `OnQuizResult` block carries the same block shift. Re-derived:

| Port line | Cites | Today, re-derived |
|---|---|---|
| `:504` section header | the completion loop `:373-508` | `OnQuizResult` is `:393-568` |
| `:508` order-pinned | XP `:389-397` | `:443-446` |
| `:508` order-pinned | spend `:406` | `:465` |
| `:508` order-pinned | draft `:421-427` | `:478-484` |
| `:509` order-pinned | punch `:459` | `:519` |
| `:509` order-pinned | reply `:496`/`:504` | `:556`/`:564` |
| `:534` | XP formula `:389-397` | `:443-446` |
| `:538` | `:45-53` const, `:406-422` emit, `:435-441` mantra credit | `:48-55`, `:431-435`, `:451-453` |
| `:545` | spend `:406` | `:465` |
| `:549` | draft `:421-427`, sink `:515-528` | `:478-484`, `UniqueSessionPath` `:575-588` |
| `:559` | reply `:496` | `:556` |
| `:564` | drafting-failed reply `:504` | `:564` — **the number happens to equal its own line; the target is `:564` upstream too, by coincidence** |
| `:569` boot contract | `:236-295` | `OnPageReady` was `:237-297` at baseline; `:236-295` maps to **`:239-304`** today (verified: old `:237` → `:240`, old `:295` → `:304`) |

**Repaired, all thirteen.** Twelve are inside the one method this packet edits; `:569` is the
line immediately after it and its target was re-derived by the same arithmetic, so leaving it
would be leaving a hole I had already measured. **Bound stated honestly: I did NOT audit the
transport, protocol or teardown blocks of `IntakeHostWindow.axaml.cs`**, and given the
single-commit cause above they are likely stale by the same shift. Named in `record.md` as
remaining debt, not silently absorbed.

### 7.4 Two more, in `Persistence/` — repaired (plan-gate finding 8)

`client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs:204` and
`client/tests/CcpClient.Tests/PersistenceStoreTests.cs:110` both cite
`IntakeHostContext.cs:126-127` for a "FlushAsync before StopAsync" that today lives at
`:172-175`. Already wrong at `71ab1bac2`; **this packet adds a fourth store to that Dispose and
pushes the target further still.** Both files are in the May-change list, so declining the
one-line repair would be a choice rather than a scope bar. **Repaired.**

## 8. What this plan does not promise

No headed evidence, no rendering, no interaction, no window behaviour. The award path is pure
logic over a file; G14 pins that the call site exists in the product source and pins **nothing
about the window running**. Linux is untouched and unproven, as is Windows at runtime: this
packet executes tests, not the app.
