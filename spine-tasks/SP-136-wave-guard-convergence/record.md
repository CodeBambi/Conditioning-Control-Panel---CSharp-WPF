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

## 5. The one-sided-update demonstration

`TheFloorGuard_RedsWhenONLYTheValidatorChanges` copies `validate-wave.mjs` to a temp directory,
substitutes its single coverage call site
(`declaredValues.some((p) => patternCovers(p, chokepointPath))`) for the literal-only semantics,
**asserts the substitution applied exactly once** (so a refactor that moves the call site reds the
fact instead of silently mutating nothing), and drives
`FloorWrapperGuardTests.ComputeChokepointViolations` with the mutated projection. The guard
accepts the glob packet with the clean projection and rejects it with the mutated one, in the same
test body. No shared constant can make that claim.

## 6. Every new guard watched RED at the committed head `26a9b2ec4c3140a0ee25a35f5c5e64450f9c4d45`

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

Coverage: fact 1 (M1, M4), 2 (M1), 3 (M1, M2, M4), 4 (M2, M3), 5 (M1), 6 (M4, M5), 7 (M5),
8 (M2, M4, M6), 9 (M7a, M7b). **All nine watched red.**

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

Headless, observed separately because the wrapper stopped at the unit-project drift:
`Passed! Failed: 0, Passed: 152, Skipped: 0, Total: 152` — exactly the pin, delta 0.

Warning gate: `WARNING GATE OK (SP-114): 0 warnings, 0 errors across 4 project(s), forced
non-incremental.`

## 8. The baseline failure is a KNOWN STRAND and this is its fourth sighting — a re-observation, not a sign-off

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

## 11. What this work does NOT prove

- **Nothing here renders, composites, focuses a window, plays audio, or animates.** These are file-
  and process-level facts about two guards. No headed gate is discharged, and no headless frame is
  claimed to discharge one.
- The **anti-shadow fact is lexical and incomplete**, and says so in its own doc comment: it closes
  the named routes (`Contains(SharedFloorPin`, a hand-rolled `patternCovers`) and cannot see a
  fresh predicate mentioning none of them. That route is closed by a *different* fact (the
  one-sided mutation), and neither is claimed to close both.
- Only **validator-accepts implies guard-accepts** is asserted, never the converse. The validator
  legitimately raises more (row cardinality, ID reuse, File Scope disjointness) and binds a larger
  population. That asymmetry is declared, not accidental.
- **`node` is now a hard requirement of two floor facts.** A machine without it FAILS them rather
  than skipping — the same disposition `CitationNeedleTests.cs:344-374` takes. Nothing was added to
  `allowedSkips`; `floor.json` was never opened.
- The convergence is proven over the **live 128-packet corpus and 13 pinned fixture cases**. It is
  not proven over inputs in neither set — which is precisely why the C# side keeps no second
  implementation to disagree *with*, rather than resting on corpus coverage alone.
- The four `.mjs`-internal citations in the new divergence rows are invisible to the citation
  detector by its own statement, so they carry no mechanical protection against rot.

## 12. Floor delta declared

`spine-tasks/SP-136-wave-guard-convergence/floor-delta.json` — **unit +9, headless 0**.
Pin 2599 / 152; observed 2608 / 152; `2599 + 9 = 2608`.
