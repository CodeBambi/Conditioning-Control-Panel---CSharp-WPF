# SP-124 — record

Branch `lane/SP-124-anchor-chokepoint`, worktree
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-ae4549e78574234be`, base
`b76856a7`. **Five commits:**

| commit | carries |
|---|---|
| `f9bfc93f5` | the anchor: `census.mjs --metadata-json` / `--check-stale`, the shared `metadataView`, and `MetadataReader_AndReflection_SeeTheSameShippedTypes` plus the three new facts replacing the stored-scalar comparison |
| `913eab0be` | the three clock guards: the dispose fact rewritten in all three files, the struck no-reporter claim, the corrected sound-clock cross-reference, and the three class-doc paragraphs |
| `8f617dd04` | `record.md` and `floor-delta.json` — the baseline, the mutation evidence and the drift trade |
| `b18ddaaaf` | code review's five items: the `--check-stale` write-time diagnostic and its pin, the wedged-`node` tree kill, the unclosed `<para>`, the `ScheduleClock.cs:65-67` off-by-one, and the timing-guard hand-off added to §7a |
| **HEAD** (this commit, `fix(SP-124): scope the method-body clause…`) | final review's two prose defects: this commit inventory, and the method-body clause scoped to the C2/C3-surviving subset with the excluded-row `hasIl` gap named |

The last row describes the commit that adds it, and is named rather than given a SHA because that
SHA cannot exist before the commit does. The previous version of this line said "two commits" and
then SURVIVED a commit that edited this very file — exactly the stale sentence this packet exists to
strike, in the packet's own required artifact.

## 1. THE CHOKEPOINT BASELINE — measured first, as required

Working-copy probe, `client/src/CcpClient.Desktop/Sp124ChokepointProbe.cs`, one ordinary
`public sealed class` with one member. Never committed.

```
[xUnit.net] CcpClient.Tests.ExecutionCensusTests.Census_DenominatorIsAnchoredToTheShippedAssembly [FAIL]
  Failed ... [542 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 885
Actual:   884
  ... ExecutionCensusTests.cs:line 254
Failed!  - Failed:     1, Passed:     9, Skipped:     0, Total:    10
```

Exactly one fact red, and it is the anchor. That is the chokepoint: reflection 885, document 884,
and the only remedy is regenerating a file closed to every lane. Reverted; `git status --short --
client/src` and `git diff --stat -- client/src` both empty afterwards.

## 2. THE REPLACEMENT — green with the SAME class present

Same probe file restored, replacement in place:

```
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13
```

`Failed: 1, Passed: 9` → `Failed: 0, Passed: 13` with the identical throwaway shipped type present.
**A packet adding one ordinary shipped type now passes.** Reverted again; `client/src` clean.

### Design

`census.mjs` gains two non-generating modes (`client/tools/coverage/**` is in scope):

- `--metadata-json [--dll <path>]` — the tool's OWN `readMetadataTypeDefs` and `classify` over a
  given assembly, printed as JSON. No coverage run, no test host, no document, ~150 ms.
- `--check-stale [--dll <path>] [--census <path>]` — the drift check, see §4.

Both go through a new shared `metadataView(rule, metadata)` that `render()` now also uses, so the
JSON a test cross-validates is literally the numbers the document prints rather than a second
derivation. The three metadata row labels are named once as constants and consumed by both
`render()` and `--check-stale`, so a renamed row surfaces as a loud `MISSING ROW`.

`Census_DenominatorIsAnchoredToTheShippedAssembly` becomes
`MetadataReader_AndReflection_SeeTheSameShippedTypes`. It asks `census.mjs` for its reading of
`typeof(HapticGate).Assembly.Location` — the same bytes this test process has loaded — and compares
four sorted **multisets**:

| # | compared | isolates |
|---|---|---|
| 1 | every TypeDef name minus `<Module>` vs `assembly.GetTypes()` names | the TypeDef table walk |
| 2 | the same, each with its kind, vs `IsInterface`/`IsEnum`/`IsValueType` | the `extends` coded-index decode and the interface flag |
| 3 | the C2/C3-surviving subset, both sides | the classified view (the old 884) |
| 4 | the no-IL subset vs "every declared method and ctor has a null body" | the MethodDef/`methodList` walk (the old 212) |

Multisets, not sets: 295 of the 1325 simple names repeat (1325 rows, 1030 distinct), so a set
comparison reads 1030 == 1030 and misses a dropped duplicate row. Names only, never
namespace-qualified: metadata's `TypeNamespace` is empty for a nested type while reflection returns
the enclosing namespace.

Plus `TheRuleClassifiesTheRealAssembly_IdenticallyInBothImplementations`: the JSON carries
`census.mjs`'s verdict for all 1325 names and the .NET `Classify` must agree on every one. Before
this the two implementations only ever met on the rule file's 20 fixtures.

**New precedent, named:** these facts spawn `node`. No test in `client/tests/**` did before.
Justified because both tier-1 gates and this tool are node scripts, so node is already a hard
requirement of this tree. If node is absent the fact FAILS with that message; it does not skip.

## 3. WHAT STILL FAILS IF THE READER STARTS MISCOUNTING

**If `census.mjs`'s ECMA-335 walk goes wrong in any way that changes which type definitions it
reports, what kind each one is, or which of the C2/C3-SURVIVING ones carry a method body, then its
own output stops matching what `Assembly.GetTypes()`, `Type.IsInterface` and
`MethodBase.GetMethodBody()` report for the identical file, and
`MetadataReader_AndReflection_SeeTheSameShippedTypes` fails naming the exact names that differ.**

That sentence claims the TypeDef walk, the kind decode and the MethodDef walk **over the
C2/C3-surviving subset** — and nothing else. Two gaps, both named, neither covered by the anchor
this replaces:

- **`ns` is compared by nothing.** It drives the census's namespace headings and its nested/top-level
  split.
- **`hasIl` on an EXCLUDED TypeDef is compared by nothing.** Comparison 4 reads the reader's
  `noMethodBody`, which `census.mjs:419` has already narrowed to `authored.filter(t => !t.hasIl)`,
  and `MetadataRow.HasIl` is carried into C# but never used. So a walk defect flipping `hasIl` on a
  compiler-generated row changes the emitted output and reds nothing. Consequence for the census is
  nil — `hasIl` has exactly one consumer, that filter — and closing it is a fifth multiset over
  every row. Named rather than closed; final review directed prose only, no mechanism change.

The method-body clause was scoped on this pass. It is the sentence's third: plan review caught it
over-claiming on `ns`/`kind`, `kind` was added and `ns` named, and this was the residue.

Proved by making the reader wrong three times (each mutation applied to the committed
`census.mjs`, run, then `git checkout --`; blob restored to `e690fed196fd9b40aff309bd578a20c16bbbdc1a`
each time):

| mutation | line | result |
|---|---|---|
| `i < rows[0x02]` → `i < rows[0x02] - 1` (drop the last TypeDef row) | 367 | **Failed: 2, Passed: 11.** `disagree about every type definition on 1 name(s)` → `NamespaceInfo:/Views/Pages/SystemPage.axaml: census.mjs 0, reflection 1` |
| `methodRva[m - 1]` → `methodRva[m]` (MethodDef index off by one) | 388 | **red on comparison 4 only:** `disagree about the no-method-body subset on 61 name(s)` |
| `t.flags & 0x20` → `t.flags & 0x40` (interface flag) | 391 | **red on comparison 2 only:** `disagree about every type definition, with its kind on 142 name(s)`, e.g. `IAiDiagnosticsSink [class]: census.mjs 1, reflection 0` |

Each mutation reds exactly the leg it should. The kind leg catches a defect that nothing in the port
could catch before this packet.

## 4. DRIFT — which of the two I chose, and why

**Relocated, not deleted — and the relocated check is itself executed and mutation-proved.**

`node client/tools/coverage/census.mjs --check-stale` recomputes the three metadata scalars from the
built assembly, diffs them against the census, and exits non-zero naming each drifted row. Two new
facts drive it against **synthetic** documents in the temp directory, never the committed one:
`StaleCheck_RedsWhenTheDocumentsScalarsStopDescribingTheAssembly` (one scalar bumped by one →
non-zero exit, `STALE ROW`, the row named, the live number quoted) and
`StaleCheck_IsQuietWhenTheDocumentDescribesTheAssembly` (live scalars → exit 0). So both outcomes
are pinned; the checker cannot be one that reds unconditionally or one that never reds.

**The trade, stated plainly: no per-lane fact notices any more that the committed census's scalars
have stopped describing the tree.** That comparison IS the chokepoint. It now runs at the land,
where regenerating is something the runner may actually do.

`--check-stale` always prints what it did NOT check, and the quiet-case fact pins that it does:

```
checked: the three scalars this tool reads from the shipped assembly's own metadata.
NOT CHECKED, and stale by construction the moment a test or a covered line moves:
  * the embedded suite run table (passed/failed/skipped per project) — it is a
    snapshot of one run and nothing here or in the floor binds it (task-board.md:34)
  * every coverage-derived row: the census universe, the executed/zero split, the
    zero-execution list itself and its per-type line counts
  * the platform marks, the namespace headings and the prose
A quiet exit therefore means the metadata scalars agree, and nothing more.
census metadata scalars agree with CcpClient.Desktop.dll: 1325 / 884 / 212
```

What survives in the suite at zero cost to lanes: `Census_IsInternallyConsistent` now also carries
the two relations the old anchor held, restated over STORED scalars only —
`invisible == authored − universe` and `invisible == noMethodBody + noSourceMapped`. Those are
arithmetic inside the document, so a lane adding a type does not move them.

### What the orchestrator must run at the land

- **Required: nothing.** This packet adds no shipped type. Verified independently:
  `1325 / 884 / 212` from the reader on a clean Debug build, matching `execution-census.md:60,61,64`.
  **`client/docs/execution-census.md` needs no regeneration for SP-124.**
- **Recommended, once, after the wave's product packets land:** build Debug FIRST, then
  `node client/tools/gate/with-slot.mjs -- node client/tools/coverage/census.mjs --check-stale`.
  It reads the BINARY, so it prints that binary's write time on every run — if that predates the
  wave's last source change, rebuild and re-run before believing either verdict. If it still reds,
  the remedy is `node client/tools/coverage/census.mjs`.
- **Verified the generation path still works** through the shared `metadataView`:
  `census.mjs --filter "FullyQualifiedName~HapticGateTests" --print` still renders
  `| type definitions ... | 1325 |`, `| ... authored name shape ... | 884 |`,
  `| — no method body at all ... | 212 |`, and `--self-check` still reports 20/20.
- **Discovery I could not act on:** the natural home for `--check-stale` is the land gate, but
  `client/tests/floor/**` is closed to this packet, so I could not wire it. Reported, not widened.

## 5. THE TWO LANDED GUARDS

### 5a. `ACallbackThatThrowsWithNoReporter_IsStillContained` — the claim was false, and now it is measured

Probe 5 (granted). `client/src/CcpClient.Desktop/Scheduling/ScheduleClock.cs`, pre-probe blob
`1fd63285297f4024ed558560182780f3ae4b5b1e`. One-line working-copy diff at line 78:

```
-        return new Timer(_ => Run(fire), null, ms, Timeout.Infinite);
+        return new Timer(_ => fire(), null, ms, Timeout.Infinite);
```

Verbatim counters for both facts in the class, with the containment bared:

```
ACallbackThatThrowsWithNoReporter_IsStillContained
[xUnit.net 00:00:03.22]     [FATAL ERROR] System.InvalidOperationException
[xUnit.net 00:00:03.22] Catastrophic failure: System.InvalidOperationException : no reporter
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
```

```
ACallbackThatThrows_IsContainedAndREPORTED_RatherThanKillingTheProcess
   TIMING-VERDICT:CONDITION-NEVER-TRUE — waited the full 20s for the faulting scheduled
   callback's exception to be reported and the deterministic signal never completed:
   treat as a REAL product/test failure.
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

So the fact passes with the containment reverted, and its old comment ("this is what says the
CONTAINMENT does not depend on the REPORTING") was false. The claim is struck and replaced with the
measured limit; the fact is kept, un-inflated, because a null reporter is a real product
configuration; the comment names the sibling that does redden and quotes both counters. Restored:
`git hash-object` → `1fd63285297f4024ed558560182780f3ae4b5b1e`, matching the pre-probe blob;
`git status --short -- client/src` and `git diff --stat -- client/src` both empty.

`SystemSoundClockTests.cs:107-110` said the sibling was "outside this packet's scope" to fix. It no
longer is, so that cross-reference is corrected too — the symmetric twin of the staleness this
packet was sent to remove.

### 5b. `DisposingTheHandleBeforeItIsDue_SuppressesTheCallback` — three files, and it now bites

Fixed in **`SystemSessionClockTests.cs`, `SystemScheduleClockTests.cs` AND `SystemSoundClockTests.cs`**;
the third carried the identical ten-minute shape and is in scope.

New shape: one delay `DoomedDue` (1000 ms) for a `control` and the `doomed` schedule, a `barrier` at
`BarrierDue` (2000 ms). `Timer` fixes its deadline at construction, so arming order is deadline
order.

1. **Disposed before it was due — a deduction, not a margin.** `control` is armed before `doomed` at
   the same delay. `Assert.False(control.Task.IsCompleted)` immediately after `Dispose()` returns
   proves less than `DoomedDue` elapsed since `control` was armed, hence less than `DoomedDue` since
   `doomed` was armed. If that ever trips, the machine stalled a full second between two adjacent
   statements, and the message says ENVIRONMENT rather than blaming the product.
2. **Its moment has passed, with room to spare.** `control` firing proves the queue ran the pass at
   `DoomedDue`; the barrier is due a whole `DoomedDue` later.
3. **The delay is observable at all** — `control` firing is the positive control.

`TaskCompletionSource` replaces `bool cancelledFired`, which was written on a pool thread and read on
the test thread with nothing ordering the two.

**Honest limit, in the code as well as here:** step 2 is an ordering-plus-settle argument, not a
happens-before edge. A pool starved for a whole second while still servicing two later timer
callbacks could mask a fired callback. That is the same class of argument the file already made; it
is named, not hidden.

Revert-red-restore, both directions, all three files (blobs `d0dee90a…`, `071e32ed…`, `39fd8ae4…`
restored exactly afterwards):

| mutation | result |
|---|---|
| `doomed.Dispose();` commented out | **Failed: 3, Passed: 0.** Each on its own suppression message: "a schedule whose handle was disposed still fired…", "a scheduled tick whose handle was disposed still fired…", "a sound schedule whose handle was disposed still fired…" |
| `DoomedDue` put back to `TimeSpan.FromMinutes(10)` (the old shape) | **Failed: 3, Passed: 0**, all three on `TIMING-VERDICT:CONDITION-NEVER-TRUE — waited the full 20s for a schedule armed before the cancelled one to fire` |

The second row is the point: the exact vacuity this packet was sent to fix is now itself a failing
assertion.

### 5c. The class doc paragraphs, corrected in the same pass

All three files claimed "never on an interval elapsing" / "nothing here asserts how long anything
took". After this change one fact per file **does** deliberately observe a due time elapsing — that
is what makes it a positive control. Left alone, this packet would have shipped three new sentences
of exactly the class it exists to remove. All three paragraphs now name the exception, say why it is
not a tolerance, and keep the "nothing asserts how LONG anything took" claim, which remains true.

## 6. THE SHAPE-GUARD DECISION: not affordable AND not landable — I did not half-build one

Two reasons, the second stronger:

1. **Undecidable, and here indistinguishable.** The three bad facts are syntactically identical to
   honest negative facts: `Assert.False(flag)` where a callback might have set `flag` is the correct
   shape for a real negative observation. Any grep-shaped approximation either misses all three or
   fires on every honest negative fact, and a guard that fires on honest facts gets suppressed.
2. **Unlandable inside this packet.** The machinery already exists —
   `VacuousShapeDetector`/`VacuousShapeGuardTests` — and extending its shape surface would force an
   edit to `client/tests/floor/vacuous-shape-ledger.json`, which this packet closes. So it is not
   merely a bad idea here, it is out of scope by construction.

What I did instead is mechanical, per fact, and costs three lines: the **positive control** — a leg
that reds when the negative leg's precondition stops being reachable. Proved in §5b, second row.
That is my answer to SP-123's "three instances argues for a guard": the guard is a required SHAPE
for negative facts, enforced at review, not a scanner pretending to decide undecidable questions.

## 7. FINDINGS I COULD NOT ACT ON

### 7a. The three new due-time literals are class-3 sites the timing guard cannot see

`TestTimingGuardTests` (`TestTimingGuardTests.cs:8-13`) defines a class-3 site as an elapsed-time
subject, requiring an inline `// wallclock-allow: <reason>` marker AND a pin in that file. The three
`DoomedDue`/`BarrierDue` literals I added are exactly that by the guard's own taxonomy, and they
carry neither.

**This is not a regression in the guard's reach.** Its `ForbiddenTokens` list
(`TestTimingGuardTests.cs:20-41`) covers `Thread.Sleep(`, `Task.Delay(`, `.WaitAsync(TimeSpan`,
`CancelAfter(`, `Timeout = TimeSpan.` and the clock-reading tokens — it never covered a bare
`TimeSpan.From*` handed to a PRODUCT API, so the ten-minute literals these replace were equally
unseen, in all three files, before this packet. The exception is named loudly in all three class doc
comments and again at each site, so it is disclosed rather than smuggled; and `TestTimingGuardTests.cs`
is outside this packet's File Scope, so adding the token and the three pins was not mine to do.

**Proposed board row:** *"`TestTimingGuardTests.ForbiddenTokens` does not cover a bare `TimeSpan.From*`
passed to a product scheduling API, so a due-time literal whose elapsing IS a fact's subject is
neither marked nor pinned. Three such sites exist after SP-124 (`DoomedDue`/`BarrierDue` in
`SystemSessionClockTests.cs`, `SystemScheduleClockTests.cs`, `SystemSoundClockTests.cs`), all
disclosed in their class docs. When that guard file is next open: add the token, add
`// wallclock-allow:` markers, pin the three sites. Guard file plus three test files, no product
code."*

Recording it here rather than leaving it in a review message, for the same reason as 7c: a finding
that lives only in a reviewer's message dies with the review.

### 7b. An intermittent I observed once, in a test outside my File Scope

On the FIRST floor run of the final-review pass, one fact I have never touched failed:

```
CcpClient.Tests.SoundArbitrationTests.Construction_LockUnavailableAtCompletion_AbandonsWithoutCounting_NothingWasParked
Assert.Equal() Failure: Values differ / Expected: 0 / Actual: 1   (SoundArbitrationTests.cs:1625)
Failed!  - Failed: 1, Passed: 2309, Skipped: 2, Total: 2312
```

**It is that test's own declared starvation mode**, written at `SoundArbitrationTests.cs:1616-1619`:
"The one scheduling assumption is that a trivial already-ungated construction returns inside the
200 ms budget; if a starved pool ever broke that, this fact REDS (it would take route (a) and count
1) — it can never pass vacuously." `Actual: 1` is exactly route (a). Its author chose red-on-starved
over pass-vacuously, so this is the fact behaving as designed on a loaded machine, not a defect it
failed to catch.

**Did my change cause it?** I checked rather than assumed, from that run's own TRX. The failing test
ran 06:36:30.188 → 06:36:31.150. All four of my node-spawning facts started AFTER it ended
(06:36:32.73, 06:36:33.47, 06:36:34.34, 06:36:34.66), so no external process of mine was concurrent
with it. Two of the three dispose facts did overlap in wall-clock (Sound 06:36:28.89 → 06:36:30.96,
Schedule 06:36:29.92 → 06:36:31.93) — but during those two seconds they hold three timers and await
signals through `TestWait`, which is `Task.WhenAny` over a delay: no poll loop, no spin, about six
trivial callbacks total. If anything they lower CPU pressure by occupying a parallel slot while
idle. **I cannot rule my change out entirely, and I am not claiming to.**

Runs at this head: **red once, then green three times** (2310 passed / 2 skipped / 2312 total, and
144/144 headless). `SoundArbitrationTests.cs` is not in this packet's File Scope, so I could not
have touched it in any case; this is reported, not acted on.

**Proposed board row:** *"`SoundArbitrationTests.Construction_LockUnavailableAtCompletion_AbandonsWithoutCounting_NothingWasParked`
reds on a starved pool by its own design (`:1616-1619`) and did so once during SP-124's final-review
floor runs (1 red in 4). Not a quarantine candidate — it is red-on-starved by deliberate choice, and
`allowedSkips` would be exactly the wrong instrument. Decide whether the 200 ms construction budget
should be `TestWait.InjectedBudget` instead."*

### 7c. `DtrhBarkRouting.Composition.cs` cites the anchor by its old name — `client/src/**` is closed

`client/src/CcpClient.Desktop/Features/Dtrh/DtrhBarkRouting.Composition.cs:24-31` explains why
SP-123's lift is a `partial` and not a type of its own, and its mechanical half is now **doubly
stale**:

> Mechanically: a new shipped type changes the authored-type count of `CcpClient.Desktop`, and that
> count is ANCHORED — `census.mjs` publishes it and
> `ExecutionCensusTests.Census_DenominatorIsAnchoredToTheShippedAssembly` recomputes it by reflection
> and requires exact agreement, so adding a type reds the floor until the census is regenerated.
> Measured, not assumed: as a standalone type this code failed that guard with 885 against the
> published 884.

The named fact no longer exists (renamed) and the constraint no longer holds (that is this packet's
outcome). Nothing enforces the string, so the floor stays green over a false paragraph. The
semantic half of that justification — "this IS the DTRH bark boundary" — is untouched and still
stands on its own, so the design does not change; only the citation is wrong.

**Proposed board row:** *"SP-124 removed the authored-type-count chokepoint that
`DtrhBarkRouting.Composition.cs:24-31` cites as the mechanical reason for its `partial` shape. Strike
the mechanical half, keep the semantic half, retarget the citation to
`ExecutionCensusTests.MetadataReader_AndReflection_SeeTheSameShippedTypes`. One doc comment,
`client/src/**`, no behaviour."*

## 8. FLOOR

| | |
|---|---|
| pin | 2309 unit / 144 headless |
| declared delta | `{ "packet": "SP-124-anchor-chokepoint", "unit": 3, "headless": 0, ... }` |
| **observed** | **2312 unit** (2310 passed + 2 skipped, 0 failed) / **144 headless** (144 passed, 0 skipped, 0 failed) |
| arithmetic | 2309 + 3 = 2312 ✓, 144 + 0 = 144 ✓ |

`check-floor.mjs` reports `FLOOR VIOLATION — total drift: 2312 result(s) (pin total 2309)`, which is
the declared-delta shape and not a failure: the orchestrator sums every packet's delta and applies
one bump at the land. The two skips are the pre-existing Linux-precondition pins
(`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`,
`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`); I added none and touched no `allowedSkips`.

`check-warnings.mjs`: `WARNING GATE OK (SP-114): 0 warnings, 0 errors across 4 project(s)`, forced
non-incremental.

The +3 is exactly: the two classifiers agreed on the real assembly's whole name population, and the
two `--check-stale` outcomes — minus nothing, since the anchor was replaced 1:1 by a rename and the
three dispose facts were rewritten in place.

## 9. WHAT THIS WORK DOES NOT PROVE

- **No product code changed.** Two working-copy probes touched `client/src` and both were reverted
  to their exact pre-probe blobs; `git status --short -- client/src` and
  `git diff --stat -- client/src` are empty at both commits.
- **Nothing here verifies interaction, rendering, audio, focus, window behaviour or animation.**
  These are pure-logic and timer facts in the unit project. No frame was drawn, no headed capture
  taken, and nothing in this packet discharges a `presentation-verified` gate.
- **The cross-validation does not prove the census's ANSWER is right.** It proves two independent
  mechanisms agree about the assembly's type definitions, their kinds and their method bodies. Which
  types are really dead remains a question for a reader.
- **`ns` is not compared** (§3), so a defect isolated to the namespace read would be invisible to it,
  as it was to the anchor this replaces.
- **`hasIl` is not compared on C2/C3-EXCLUDED rows** (§3). Comparison 4 reads `noMethodBody`, which
  the tool has already narrowed to the authored subset, so a walk defect flipping `hasIl` on a
  compiler-generated TypeDef changes the reader's output and reds nothing. Nil consequence for the
  census — one consumer — and one multiset away from closed, but not closed here.
- **`--check-stale` checks three scalars and says so.** It cannot see the coverage-derived rows or
  the embedded run table, and it prints that on every run rather than letting a quiet exit imply it.
- **`--check-stale` reads the BUILT ASSEMBLY, so its own input can be stale.** A leftover Debug
  binary makes it report `STALE ROW` over a census that describes the source tree perfectly — review
  hit exactly that with a leftover probe build. It cannot detect this, so it now prints the DLL's
  write time and the instruction to rebuild on BOTH outcomes, and
  `StaleCheck_IsQuietWhenTheDocumentDescribesTheAssembly` pins that the line is printed. Disclosed,
  not solved: reading a binary is what makes the check cheap enough to run at a land.
- **The suppression facts are ordering arguments, not happens-before edges** (§5b). A sufficiently
  starved pool could let one read green.
- **The stale-check facts run against synthetic documents.** They prove the checker bites; they do
  not prove anything about the committed census, by design.
