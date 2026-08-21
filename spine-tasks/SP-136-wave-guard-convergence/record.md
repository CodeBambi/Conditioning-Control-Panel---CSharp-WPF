# SP-136 — record

Base `feat/crossplatform` at `766be7ac0`. Worktree `agent-ae859c22c33d424fe`, branch
`worktree-agent-ae859c22c33d424fe`. Plan checkpoint `2047f76c2`; implementation `26a9b2ec4`.

## 1. Premises — all four verified against the merged head, all four hold

`validate-wave.mjs:458` was `if (!mustNotChange.some((p) => patternCovers(p, FLOOR_PIN_PATH))) {`.
`FloorWrapperGuardTests.cs:224` was
`if (!row.Groups[1].Value.Replace('\\', '/').Contains(SharedFloorPin, StringComparison.OrdinalIgnoreCase))`.
`:47` was the constant. `:42` was the MIRROR note. No premise defect to report.

Corroborated independently in the tree: `b5f789de5 fix(wave-60): unred the base — both packets'
contract rows, and the mirror drift that let it through` states the same defect and closed it **by
editing the two packets**, which is why the cause was still live.

## 2. Blast radius — measured in both directions before choosing

128 packets carry a `PROMPT.md`. Both semantics were re-implemented byte-faithfully and run over
every one of them.

| | js PASS | js FAIL | js NOROW |
|---|---|---|---|
| **cs PASS** | 56 | **0** | 0 |
| **cs FAIL** | **12** | 59 | 0 |
| **cs NOROW** | 0 | 0 | 1 |

- **Glob in the C# guard: 0 bound packets change verdict** (0 newly red, 0 newly green). The 12
  disagreements are all below SP-073 and the guard's grandfather rule already skips every one.
- **Literal in the validator: 12 packets it accepts today would be newly REJECTED** — SP-011, 012,
  017, 018, 019, 020, 022, 030, 036, 039, 050, 060. Every one declares `client/tests/**`. The
  validator has no grandfather on check 4, so these are live rejections.

## 3. Semantics chosen: glob-aware coverage, and why against what the rule is FOR

The rule exists so a lane cannot edit `client/tests/floor/floor.json`. `client/tests/**` forbids
the pin at least as completely as naming it, so it satisfies the **property**; `Contains` tested
the **spelling**. Measured third finding, which settled it: the literal was wrong in **both**
directions, not merely narrower — unanchored to a backticked value, it **accepted** a cell
declaring `client/tests/floor/floor.json.bak`, a different file the lane may freely edit. Nothing
was loosened: the surviving rule is strictly stronger on that case and identical on all 56 bound
packets.

**Stated plainly, because the record must not imply more than it earns: on today's corpus this is
a FORWARD-looking hardening, not a repair of a live failure.** All 16 bound packets that declare
`client/tests/floor/**` (SP-120..SP-135) also carry the literal in the same cell, which is why the
tree is green. What it buys is that the next packet declaring only the glob cannot print `WAVE OK`
and red the suite. The live hardening is on the validator side and on the grandfather line, and
those were left as they are, named rather than changed.

## 4. The mechanism, stated precisely: what makes drift impossible rather than unlikely

**The C# guard stops implementing the decision.** `validate-wave.mjs` owns
`declarationCoversChokepoint` / `chokepointVerdict` and grew a read-only projection:

```
node client/tools/wave/validate-wave.mjs --emit-packet-scopes <spineTasksDir>
```

It emits, per packet-root `PROMPT.md`: the parsed `testCommand` / `floorDelta` /
`fileScopeMustNotChange` rows with line numbers, the wrapper-routing verdict, the chokepoint
coverage verdicts, the validator's own per-packet violations, and the pinned fixture with its
verdicts. `FloorWrapperGuardTests` consumes it. It now holds **no** coverage predicate, **no**
contract-row regex and **no** wrapper-token test — those three `[GeneratedRegex]` members were
deleted. Two implementations cannot disagree when there is one of them.

**Exit-code contract**, stated because the consumer's correctness rests on it: exit 0 whenever a
projection was produced, **including for a corpus riddled with violations** (violations travel as
data, because the consumer binds a different population); exit 2 when no projection exists. The
consumer treats non-zero exit, unstartable `node`, empty stdout, unparseable JSON and an unknown
schema as hard failures — never as an empty corpus, which would make every guard consuming it
vacuously green.

What the C# still owns, deliberately: its own packet walk (so it can refuse to go blind, and so
the two walks cross-check each other in both directions), its two grandfather IDs, and its message
text. `SharedFloorPin` survives only as a cross-check that both sides guard the same path.

**The second axis of divergence, which the plan review surfaced and which bites this mechanism.**
The two guards bind different **populations**: the validator applies check 4 with no packet-number
condition at all, so **60 of the 128 packets violate check 4 there**, while the C# guard
grandfathers everything below SP-073 and binds 56. Consuming the projection therefore *requires*
re-applying that grandfather afterwards, and that is pinned by fact 8 — dropping it would newly
red sixty packets and look like a corpus problem rather than a guard problem.

## 4b. THE FIRST CUT CLOSED CHECK 4 AND OPENED CHECK 2 — found at code review, reproduced, fixed

**This is the packet's own defect class arriving one check to the left, and it is recorded rather
than quietly repaired.** The first implementation also routed the SP-065 **wrapper-routing**
verdict through the projection and deleted the C# side's independent test (`TestCommandRow()` and
`DotnetTest().IsMatch(command) && !command.Contains(WrapperToken, ...)` at base `:53/:131/:139`).
The consumer then trusted two booleans — `InvokesDotnetTest`, `RoutesThroughWrapper` — computed at
exactly one site and pinned by **nothing**: no fixture case, no C#-side verdict, no synthetic
packet carrying a bare `dotnet test`, and no mutation in the M1-M7b watch. **The refusal branch was
executed by no test at all**, because every synthetic packet used
`node client/tests/floor/check-floor.mjs`.

Reproduced before fixing: the single substitution
`routesThroughWrapper: command.includes(WRAPPER_TOKEN),` -> `routesThroughWrapper: true,` silences
the wave gate's check 2 **and** `PacketsAtOrAboveSp065_RouteDotnetTestThroughTheFloorWrapper`
together. **Before this packet's convergence, that same edit was caught by the C# guard.** So the
first cut was a net loss on check 2 while being a net gain on check 4.

Fixed by giving check 2 the same treatment check 4 got: `wrapperRoutingVerdict` is now the single
implementation and the row parse goes through it; a **six-case fixture** lives in
`validate-wave.mjs` with the verdicts pinned **again in C#** (`PinnedWrapperVerdicts`); a synthetic
packet running a bare `dotnet test` drives `ComputeWrapperViolations` to a non-empty result; and a
one-sided mutation on the routing call site reds. Three new facts, watched red as M8/M9/M10 below.

**The general lesson, stated because it is the reusable part:** routing a decision through a shared
projection *removes* a guard unless the decision is pinned by a fixture on both sides. Consolidation
is not free. Every boolean the C# stopped computing needed a fixture, and I built one for the
decision the packet named and not for the one it did not.

## 5. The one-sided-update demonstration

`TheFloorGuard_RedsWhenONLYTheValidatorChanges` copies `validate-wave.mjs` to a temp directory,
substitutes its single coverage call site
(`declaredValues.some((p) => patternCovers(p, chokepointPath))`) for the literal-only semantics,
**asserts the substitution applied exactly once** (so a refactor that moves the call site reds the
fact instead of silently mutating nothing), and drives
`FloorWrapperGuardTests.ComputeChokepointViolations` with the mutated projection. The guard
accepts the glob packet with the clean projection and rejects it with the mutated one, in the same
test body. `TheWrapperGuard_RedsWhenONLYTheValidatorChanges` does the same for the SP-065 rule. No
shared constant can make that claim.

**MUTATION FIDELITY, corrected at review.** The first version substituted `p === chokepointPath`
and the record claimed it "restores the exact literal-only semantics `FloorWrapperGuardTests.cs:224`
used to carry". **It did not.** The historical predicate was whole-cell, substring, and
case-INsensitive; `p === chokepointPath` is per-declared-value, exact-equality and case-SENSITIVE,
so it is strictly stricter and would also have refused the `case-only-difference` and
`windows-separators` cases the historical predicate accepted. It still reddened, but a mutation that
misrepresents the defect it replays is worse evidence even when it reds. Now faithful:
`declaredValues.join(", ").replace(/\\/g, "/").toLowerCase().includes(chokepointPath.toLowerCase())`,
matching `DriftedLiteralCoverage` in the test file.

**THE EXACT BOUND ON THIS PROOF, stated because it is narrower than the obvious reading.** It
catches a **replacing** shadow — C# logic used *instead of* the projection's verdict. It does
**not** catch an **additive** one (`covered = emitted.FloorPin.Covered || localPredicate`), because
the clean run stays at zero violations and the mutated run still reaches exactly one, so the fact
stays green; nor a **population-gated** one whose branch the SP-072/SP-073 fixtures never reach.
The earlier class comment claimed this route was closed and that was **overstated**.

The residue is bounded, and the bound is the reassuring part: an OR-shadow makes the C# strictly
**more permissive** than the validator, so guard-rejects becomes a subset of validator-rejects, and
**the SP-136 incident direction — `WAVE OK` printed while the base is red — cannot recur through
it.** The inverse direction reds loudly at the pre-launch gate. That is a real gap, written down
rather than claimed away.

## 6. Every new guard watched RED at a committed head — six passes, ending at the tip

### Passes 1-4, at `26a9b2ec4c3140a0ee25a35f5c5e64450f9c4d45`

Eight targeted source mutations, each applied, built, run, then reverted; the tree was verified
clean by `git status --porcelain` after every one, and `HEAD` was unchanged throughout.

| mutation | facts it reds |
|---|---|
| **M1** the shared decision goes literal-only (the drifted semantics, restored) | `Failed: 4` — facts 1, 2, 3, 5 |
| **M2** the C# guard grows a SHADOW implementation and stops consuming the projection | `Failed: 3` — facts 3, 4, 8 |
| **M3** the literal predicate is reintroduced on the C# side | `Failed: 1` — fact 4 only |
| **M4** coverage consumption inverted | `Failed: 5` — facts 1, 3, 6, 8 + `PacketsAtOrAboveSp073_...` |
| **M5** the C# packet walk stops seeing some packet roots | `Failed: 4` — facts 6, 7 + both pre-existing FloorWrapper facts |
| **M6** the SP-073 grandfather dropped after consuming | `Failed: 2` — fact 8 + `PacketsAtOrAboveSp073_...` |
| **M7a** the projection stops exiting 0 on a corpus it could read | `Failed: 10` — fact 9 among them |
| **M7b** a failed projection reads as an EMPTY corpus instead of throwing | `Failed: 1` — fact 9 **only**, `Assert.Throws() Failure: No exception was thrown` |

### Pass 5, at the review-fix head `6896a046c3a74dc2ef98caa3be2cfcd183b5fb4d`

The three new wrapper-routing facts, the corrected coverage mutation, and the newly-tightened
grandfather VALUE pin. Same discipline: applied, built, run, reverted, tree verified clean.

| mutation | facts it reds |
|---|---|
| **M8** the shared routing verdict always says "routed" (the unpinned-boolean defect, exactly) | `Failed: 3` — all three wrapper facts |
| **M9** the shared routing verdict stops recognising `dotnet test` at all | `Failed: 3` — all three wrapper facts |
| **M10** the C# guard stops consuming the routing verdict (SP-065 refusal branch removed) | `Failed: 2` — `ABareDotnetTest_IsRejectedByBothGuards`, `TheWrapperGuard_RedsWhenONLYTheValidatorChanges` |
| **M11** coverage mutation CORRECTED to the faithful historical semantics (replaces M1) | `Failed: 4` — facts 1, 2, 3, 5, the same set M1 reddened |
| **M12** the delta bound RAISED to 100 | `Failed: 1` — the grandfather fact only |
| **M13** the delta bound LOWERED to 72 | `Failed: 2` — the grandfather fact + `PacketsAtOrAboveSp073_...` |

Coverage: fact 1 (M1/M11, M4), 2 (M1/M11), 3 (M1/M11, M2, M4), 4 (M2, M3), 5 (M1/M11),
6 (M4, M5), 7 (M5), 8 (M2, M4, M6, M12, M13), 9 (M7a, M7b), wrapper-fixture (M8, M9),
bare-`dotnet test` (M8, M9, M10), wrapper-mutation (M8, M9, M10). **All twelve watched red.**

### Pass 6, measured at `abce9e40c9a13b03de6da504b25ba0279fcc4938` — because the earlier claim was wrong

**The claim this section used to make did not follow.** It said M1-M7b were watched at
`26a9b2ec4`, that the commits between were docs-only, and therefore that evidence stood against the
current code. But `6896a046c` is itself a **code** commit landing after that watch, and it changed
`FloorWrapperGuardTests.cs` — including `LoadAsync`, which is exactly what M7a/M7b mutate — and
`validate-wave.mjs`. Facts 4, 6, 7 and 9 had not been re-watched against the code that now ships.

Rather than caveat it, the mutations were **re-run against the shipping code**:

| mutation | facts it reds at `abce9e40c` |
|---|---|
| **M2'** the C# guard grows a shadow and stops consuming the projection | `Failed: 3` — facts 3, 4, 8 |
| **M3'** the literal predicate reintroduced on the C# side | `Failed: 1` — fact 4 only |
| **M4'** coverage consumption inverted | `Failed: 5` — facts 1, 3, 6, 8 + `PacketsAtOrAboveSp073_...` |
| **M5'** the C# packet walk stops seeing some packet roots | `Failed: 4` — facts 6, 7 + both pre-existing FloorWrapper facts |
| **M7a'** the projection stops exiting 0 on a corpus it could read | `Failed: 13` of 15 — every convergence fact except the lexical one, fact 9 among them |
| **M7b'** a failed projection reads as an EMPTY corpus instead of throwing | `Failed: 1` — fact 9 **only** |

**Net position, stated exactly: all twelve convergence facts have been watched RED at
`abce9e40c`** — facts 1, 2, 3, 5, 6, 7, 8, 9 and all three wrapper facts via M7a', and fact 4 via
M2'/M3' (which M7a' correctly leaves green, since the lexical read needs no projection).

**And the SHA is stated exactly, because the earlier version of this section got the same shape
wrong.** `abce9e40c` is where the mutations were MEASURED; the landed head is later. The two differ
only by `.md` files — the durable-row corrections and this record — so the watched code is the
shipping code, but "the current tip" was false as written and is not the claim being made. The
earlier passes at `26a9b2ec4` and `6896a046c` are retained above as history, not as the
load-bearing evidence. The full commit trail, with the landed head, is §13.

**Recorded because it is evidence and not a nuisance:** a first attempt at M7 — disabling the
oracle's exit-code check alone — did **not** red fact 9. `LoadAsync` fails closed at four
independent points, so removing one leaves the next catching it. That is the correct behaviour for
a fail-closed reader and it means no single-line mutation defeats it; M7b removes the layers
together and reds the fact exactly and only. A second attempt (M7a) initially reported `anchor
occurrences: 0` because the working copy of the `.mjs` is CRLF while my anchor assumed LF — a
harness bug in my own mutation script, not a property of the guard, fixed and re-run.

## 7. Before/after floor, compared as FAILURE SETS

**BEFORE**, at `766be7ac0`, taken before any edit:
`Failed: 1, Passed: 2596, Skipped: 2, Total: 2599 — CcpClient.Tests.dll`.
Failure set = { `SoundArbitrationTests.Construction_AbandonedThenFaults_CountStillDrops_CapNeverRefusesForever` }
(`Assert.Equal() Failure: Expected: 1 / Actual: 0`). `CcpClient.HeadlessTests` did not report,
because the wrapper stops at the first failing project, so the headless half was **unobserved at
baseline**.

**AFTER**, at `26a9b2ec4`:
`Passed! Failed: 0, Passed: 2606, Skipped: 2, Total: 2608 — CcpClient.Tests.dll`.
Failure set = **{ }** — empty.

**FINAL VERIFICATION**, at `88bb3333a`, two consecutive full runs: the first carried
`Failed: 1` and the second `Failed: 0`, both `Total: 2608`. The one failure was identified from the
preserved TRX (`ccp-floor-Y2lLDG`) as the **same** `SoundArbitrationTests` strand already in the
BEFORE set, byte-identical message — **not** one of this packet's facts, and not a new name. It is
written up as observation B in §8 with its own isolation runs. So across every run in this packet
the failure set is a subset of { that one strand } and contains nothing else.
`node client/tests/floor/check-floor.mjs` reports `FLOOR VIOLATION — total drift: 2608 result(s)
(pin total 2599)`. **That is the expected and declared outcome, not a failure:** pin 2599 +
declared delta 9 = **2608 observed**. `sum-deltas.mjs` independently computes
`CcpClient.Tests: 2599 +9 = 2608`, `CcpClient.HeadlessTests: 152 +0 = 152`.
`client/tests/floor/floor.json` was never opened.

**AFTER THE REVIEW FIX**, at `6896a046c` (the state being reported):
`Passed! Failed: 0, Passed: 2609, Skipped: 2, Total: 2611 — CcpClient.Tests.dll`.
Failure set = **{ }** — empty. `FLOOR VIOLATION — total drift: 2611 result(s) (pin total 2599)` is
again the expected, declared arithmetic: **2599 + 12 = 2611**, and `sum-deltas.mjs` independently
computes the same.

Headless, observed separately because the wrapper stops at the unit-project drift:
`Passed! Failed: 0, Passed: 152, Skipped: 0, Total: 152` — exactly the pin, delta 0.

Warning gate at the same head: `WARNING GATE OK (SP-114): 0 warnings, 0 errors across 4 project(s)
[CcpClient.Desktop, CcpClient.HeadlessTests, CcpClient.Tests, CcpVerify] in Debug, forced
non-incremental.`

## 8. The baseline failure is a KNOWN STRAND — sightings four AND five, each a re-observation, neither a sign-off

Prior sightings: `SP-133/record.md:220-245` ("failed once in ten runs") and
`SP-135/record.md:248-262` (first full run after the ledger edit, 3/3 clean in isolation). Mine is
the third recorded and the fourth overall.

**I observed it TWICE in this packet, so these are the fourth and fifth sightings overall. Neither
is signed off as "a known flake"; each gets its own isolation run and its own line, per the
standing rule.**

- **Observation A (baseline, at `766be7ac0`, before any edit).**
  `Failed: 1, Passed: 2596, Skipped: 2, Total: 2599`. Isolation re-run through the same wrapper,
  `--filter FullyQualifiedName~SoundArbitrationTests`: **`Passed! Failed: 0, Passed: 52, Total: 52`**.
- **Observation B (final verification, at `88bb3333a`).** It RECURRED:
  `Failed: 1, Passed: 2605, Skipped: 2, Total: 2608`, TRX `ccp-floor-Y2lLDG`,
  `Assert.Equal() Failure: Values differ / Expected: 1 / Actual: 0` — byte-identical message and
  the same test name as observation A. **Identified from the preserved TRX rather than assumed**,
  precisely so it could be distinguished from a failure of my own. Isolation re-run **three times**:
  **52/52, 52/52, 52/52.** The immediately following full floor run at the same head was clean
  (`Failed: 0, Passed: 2606, Skipped: 2, Total: 2608`, TRX `ccp-floor-hHHyi0`).
- Across this packet: **4 full-suite runs of the unit project, 2 clean and 2 carrying this one
  failure**, all four with lane-mates building concurrently through the 3-slot gate. That is a
  markedly higher rate than SP-133's "once in ten runs" and is worth carrying into the escalation.

The assertion is
`SoundArbitrationTests.cs:1560`, `Assert.Equal(1, Volatile.Read(ref h.ConstructCount));` — a
thread-pool injection failure inside `ConstructionBudget = TimeSpan.FromMilliseconds(200)`
(`:1106`), which is a real wall-clock timeout in the PRODUCT's construction path and not a
`TestWait`. `task-board.md:341` already excludes desktop contention as its cause (the class carries
no collection attribute). **Not fixed — out of scope, and deliberately not touched.** Recorded as
an observation against the three prior ones; the row is being escalated separately.

## 9. Wave-gate behaviour proven unchanged, not reviewed

The refactor moved the validator's whole per-packet body into `inspectPacket`. Pre-change and
post-change `validateWave` were run side by side over **259 wave compositions** — each of the 128
packets alone, every adjacent pair, the whole corpus at once, a duplicated-packet wave, a missing
directory and an unparseable name — comparing the violation list, the packet list and the delta
summary as strings. **Zero differences.** The projection was separately proven to reproduce a
single-packet wave's per-packet violations for all 128 packets.

C#-consumer parity measured too: no packet on disk carries a `floorDelta` cell with anything but
one backticked value, so replacing that regex with the shared row parse moves no verdict; and every
bound packet keeps its scope-row verdict under the new coverage consumption.

Authoring re-checked live after the change:
- `SP-136 SP-137` -> `WAVE OK: 2 packet(s); scopes disjoint; declared floor delta unit +9, headless +0`
- `SP-136 SP-136` -> check 7 duplicate + check 8 ID reuse, both messages intact
- `SP-011-webview-dtrh-spike` -> check 3 and check 8 fire; **check 4 does NOT**, because
  `client/tests/**` covers the pin. The exact case the drift got wrong.

## 10. LAND OBLIGATION — a citation this packet rots and may not repair

`client/docs/port-workflow.md:15` cites `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:42-100`
for the claim that the file "asserts the directory exists, enumerates `PROMPT.md` at exactly one
level, requires the directory to parse as `SP-<n>-`, and requires a `| testCommand | ... |` row for
every packet at or above SP-065, failing closed on each". **That range is now wrong.** After this
packet those behaviours live at `FloorWrapperGuardTests.cs:99-131` (the walk) and `:180-241` (the
SP-065 rule, applied to the projection's verdicts), with the fact itself at `:315-325`. The claim
remains TRUE; only the line range rotted.

`client/docs/port-workflow.md` is **outside this packet's File Scope** and nothing mechanical binds
the citation (`detect.mjs:152-156`: the detector cannot see citations into `client/tools/**` or any
`.mjs`, and this one is into `client/tests/**`). Reported, not fixed.

## 10b. THE CONVERGENCE IS NOT UNIFORMLY PINNED, and here is exactly where it is thin

1. **`FirstDeltaBoundPacketNumber = 73` is held UNILATERALLY.** `FirstBoundPacketNumber = 65` is
   cross-checked against `projection.FirstBoundPacketNumber`; 73 appears at exactly one site and
   the validator carries no delta bound at all, so there is nothing to cross-check it against.
   Mitigated rather than closed: the grandfather fixture now brackets it at **SP-072 / SP-073**, so
   the **value** is pinned in both directions — watched red at 100 (M12) and at 72 (M13). The
   earlier SP-010 / SP-903 pair pinned only the *existence* of a bound and would have let any value
   from 11 to 903 pass unchanged. A cross-check remains impossible without teaching the validator a
   rule it does not apply, which would be inventing a rule to make a test possible.
2. **The figures 60 / 128 / 56 live only in COMMENTS** (`WaveGuardConvergenceTests.cs` and
   `FloorWrapperGuardTests.cs` doc comments, and this record). Nothing asserts them. Adding
   compliant packets raises 128 and 56 while 60 stays frozen, so those comments will drift and
   nothing will red. They are descriptive measurements taken at `766be7ac0`, not invariants, and
   they should be read as dated rather than current.
3. **The SP-065 wrapper rule was unpinned in the first cut** and is now pinned — see §4b. That gap
   existed in a committed state of this branch and is recorded rather than erased.
4. **The additive/population-gated shadow residue** on the one-sided-update proof — see §5.

## 10c. THE TRANSFERABLE LESSON: A CORRECTION LANDS IN THE DURABLE ROW, OR IT DOES NOT LAND

At final review I was caught having corrected an overstatement in **two** places and not the
**third**. The claim that the one-sided-update fact "closes the route the lexical guard cannot see"
was walked back in `record.md` and in the test's own doc comment, and left standing in
`wpf-surface-reachability.md` D302 — the divergence ledger. Same for the residue D304 was
supposed to enumerate: it named four limits and omitted the one the correction had just created.

**The durable row is the artifact that survives; the packet record is the one that gets read once.**
A packet record is read at land and effectively never again. The ledger row is what a reader two
waves from now actually consults, so it is simultaneously the place a correction is most likely to
be missed and the place where missing it costs most. **When a claim is walked back, the ledger row
is the FIRST place to change, not the last.** The reviewer notes this is the eighth instance of the
shape this wave and that they committed it themselves this session on D294 — which is the strongest
argument that it is structural rather than careless.

### The enforcement half, learned the hard way one round later

**The correction reached the row and not the reader.** Both corrected rows carried a literal
`||` inside a code span — the notation naming an additive shadow — and **GFM does not respect
backticks inside table cells**. Seven unescaped pipes where a four-column row needs five, so the
renderer dropped everything past column four: D302 truncated mid-sentence and **D304's entire
Reason cell, which is where the "different halves" retraction lives, was invisible**. The notation
for the defect destroyed the description of the defect.

Measured, not eyeballed, before and after — because that is the point:

| row | before | after |
|---|---|---|
| D302 | 7 unescaped pipes, renders 6 cells | 5, renders 4, reason cell 944 chars |
| D304 | 7 unescaped pipes, renders 6 cells | 5, renders 4, reason cell 430 chars |
| D296-D301, D303, D305 | 5 each | 5 each |

This is a repeat at repository scale: the wave-64 land found **27,859 characters across 17 board
rows** silently dropped by exactly this, and the reviewer committed it again this session in a row
that was *about* unescaped pipes. So the rule has an enforcement clause, and the clause is what
makes it usable:

- **Escape `|` inside table cells even within backticks**, and
- **verify by COUNTING the delimiters, never by reading.** A four-column row has exactly five
  unescaped pipes. The failure is invisible to review by eye — the source looks correct and only
  the rendered output is wrong — so the check has to be mechanical or it does not happen.

That sits alongside §4b's lesson as the trio worth carrying out of this packet:

- **Consolidation is not free.** Routing a decision through a shared projection REMOVES a guard
  unless the decision is pinned by a fixture on both sides.
- **A correction that does not reach the durable row has not been made.** Grep the ledger for the
  claim you just weakened, before declaring the fix done.
- **A correction that does not RENDER has not been made either.** Count the delimiters in every
  table row you touch; backticks do not protect a pipe.

## 10d. RESIDUALS CARRIED FORWARD, confirmed at review and not blocking

Collected in one place so none of them has to be reconstructed from prose:

1. **The one-sided-update facts catch a REPLACING shadow only.** An additive (`covered || local`)
   or population-gated C# shadow evades both them and the lexical guard. Bounded, not closed: an
   OR-shadow is strictly more permissive than the validator, so this packet's own incident
   direction cannot recur through it.
2. **`FirstDeltaBoundPacketNumber = 73` is cross-checked by nothing.** Its VALUE is pinned by the
   SP-072/SP-073 bracket (red at 100, red at 72), but the validator carries no delta bound, so
   there is no second opinion to compare against and inventing one would be adding a rule to make a
   test possible.
3. **The figures 60 / 128 / 56 live only in comments.** Nothing asserts them; adding compliant
   packets raises 128 and 56 while 60 stays frozen. Read them as measurements dated `766be7ac0`.
4. **`client/docs/port-workflow.md:15`'s range into `FloorWrapperGuardTests.cs` is rotted by this
   packet**, is outside File Scope, and has **no mechanical detector** — `detect.mjs:152-156` cannot
   see citations into `client/tools/**` or `.mjs`, and this one points into `client/tests/**`.
   See §10 for the exact replacement ranges.
5. **There is no wrapper-side equivalent of `BannedCoveragePredicates`.** A C#-side
   re-implementation of the SP-065 rule is guarded only by the replacing-shadow mutation fact, not
   by a lexical read.
6. **The wedged-child path in `WaveScopeOracle.RunAsync` is exercised by no fact.**

## 11. What this work does NOT prove

- **Nothing here renders, composites, focuses a window, plays audio, or animates.** These are file-
  and process-level facts about two guards. No headed gate is discharged, and no headless frame is
  claimed to discharge one.
- The **anti-shadow fact is lexical and incomplete**, and says so in its own doc comment: it closes
  the named routes (`Contains(SharedFloorPin`, a hand-rolled `patternCovers`) and cannot see a
  fresh predicate mentioning none of them. That route is only **PARTLY** closed by the one-sided
  mutation fact: a REPLACING shadow reds there, an ADDITIVE or population-gated one reds nowhere.
  See §5, §10b.4 and §10d.1 — this sentence was the fourth surviving copy of a claim corrected in
  three other places, which is the whole of why §10c exists.
- Only **validator-accepts implies guard-accepts** is asserted, never the converse. The validator
  legitimately raises more (row cardinality, ID reuse, File Scope disjointness) and binds a larger
  population. That asymmetry is declared, not accidental.
- **`node` is now a hard requirement of two floor facts.** A machine without it FAILS them rather
  than skipping — the same disposition `CitationNeedleTests.cs:344-374` takes. Nothing was added to
  `allowedSkips`; `floor.json` was never opened.
- The convergence is proven over the **live 128-packet corpus, 13 pinned coverage cases and 6
  pinned wrapper-routing cases**. It is not proven over inputs in none of those — which is
  precisely why the C# side keeps no second implementation to disagree *with*, rather than resting
  on corpus coverage alone.
- **The wedged-child path is unexercised.** `TheProjection_..._AndFailsClosedOnAFailedProjection`
  covers a failed projection (non-zero exit, empty stdout, bad JSON, wrong schema). It does NOT
  reach the `TestWait` window expiring and the child being killed in `WaveScopeOracle.RunAsync`;
  no fact here does, and the test was renamed so its name stops implying otherwise.
- The four `.mjs`-internal citations in the new divergence rows are invisible to the citation
  detector by its own statement, so they carry no mechanical protection against rot.

## 12. Floor delta declared

`spine-tasks/SP-136-wave-guard-convergence/floor-delta.json` — **unit +12, headless 0**.
Pin 2599 / 152; observed **2611 / 152**; `2599 + 12 = 2611`. (It was +9 before the review fix added
the three wrapper-routing facts.) `client/tests/floor/floor.json` was never opened.

## 13. Commit trail

| commit | what |
|---|---|
| `2047f76c2` | plan checkpoint (Review Level 3 stop, no product edit) |
| `26a9b2ec4` | the convergence — head for red-watch passes 1-4 (M1-M7b) |
| `88bb3333a` | record, and one divergence-row wording fix (docs only) |
| `90feaf2d8` | the strand's second sighting (docs only) |
| `6896a046c` | review fix: the SP-065 routing decision pinned, three accuracy corrections — head for red-watch pass 5 (M8-M13) |
| `abce9e40c` | record of the reopened check and the corrected claims — head for red-watch pass 6 (M2'-M7b'), where ALL TWELVE facts were watched red |
| final | the durable-row corrections D302/D304 and this trail; text-only, no code and no re-pin |
