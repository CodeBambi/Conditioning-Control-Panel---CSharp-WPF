# SP-136 — plan (Review Level 3 checkpoint, written BEFORE any product edit)

Base: `feat/crossplatform` at `766be7ac0`. Worktree `agent-ae859c22c33d424fe`, branch
`worktree-agent-ae859c22c33d424fe`.

## 0. All four packet premises verified against the merged head — all four hold

| packet claim | verified |
|---|---|
| `validate-wave.mjs:458` = `if (!mustNotChange.some((p) => patternCovers(p, FLOOR_PIN_PATH))) {` | YES, exact line 458 |
| `FloorWrapperGuardTests.cs:224` = `if (!row.Groups[1].Value.Replace('\\', '/').Contains(SharedFloorPin, StringComparison.OrdinalIgnoreCase))` | YES, exact line 224 |
| `FloorWrapperGuardTests.cs:47` = `private const string SharedFloorPin = "client/tests/floor/floor.json";` | YES, exact line 47 |
| `validate-wave.mjs:42` = the MIRROR note | YES, exact line 42: `// MIRROR note (do not let these two drift): packet enumeration and the testCommand row` |

No premise defect to report. The incident is independently corroborated in the tree: commit
`b5f789de5 fix(wave-60): unred the base — both packets' contract rows, and the mirror drift that
let it through` describes exactly this and closed it *by editing the two packets* — which is the
resolution row 32 and this packet both forbid as the fix. That is why the drift is still live.

## 1. BLAST RADIUS — measured in both directions, not assumed

Method: both semantics re-implemented byte-faithfully in one node script and run over every
`spine-tasks/*/PROMPT.md` on disk (128 packets with a PROMPT.md; `spine-tasks/CONTEXT.md` and the
one packet folder without a PROMPT.md are not packets). C# side = `[^|]*` cell capture +
`Contains(SharedFloorPin, OrdinalIgnoreCase)`, every matching row must satisfy. JS side =
`(.*)$` capture, backtick-extracted values, `patternCovers` over the union, any value may satisfy.

Cross-tab, all 128 packets:

| | js PASS | js FAIL | js NOROW |
|---|---|---|---|
| **cs PASS** | 56 | **0** | 0 |
| **cs FAIL** | **12** | 59 | 0 |
| **cs NOROW** | 0 | 0 | 1 |

**Direction A — make the C# guard glob-aware (adopt `patternCovers`):**
- Bound packets (>= SP-073, the delta-bound population the C# guard actually judges): **56**.
- Bound packets that change verdict: **0**. FAIL->PASS: 0. PASS->FAIL: 0.
- The 12 packets where the two semantics disagree are **all below SP-073** (SP-011, SP-012,
  SP-017, SP-018, SP-019, SP-020, SP-022, SP-030, SP-036, SP-039, SP-050, SP-060), so the C#
  guard's explicit grandfather rule already skips every one of them. Blast radius on the suite is
  **exactly zero**, measured rather than assumed.

**Direction B — make the validator literal-only:**
- Packets on disk that the validator accepts today and would newly REJECT: **12** — the same
  twelve. Every one of them declares `client/tests/**` (or `client/**`-shaped equivalents), which
  demonstrably covers the pin. Restricted to bound packets (>= SP-073): **0**.
- The validator has **no grandfather rule** on check 4, so all 12 are live rejections the moment
  any of them is named in a wave (re-runs, re-issues, audits). Direction B is the strictly worse
  measurement.

**Also measured, and it matters for the choice:** the literal check is not merely narrower, it is
*differently* wrong. `Contains` is unanchored to a backticked value, so a cell declaring
`client/tests/floor/floor.json.bak` — a different file that the lane may freely edit — SATISFIES
the literal check and does NOT satisfy `patternCovers`. The literal rule is therefore both
under- and over-inclusive with respect to the property the rule is for.

## 2. SEMANTICS CHOSEN: glob-aware coverage (`patternCovers`). Why, against what the rule is FOR

The rule exists so **a lane cannot edit `client/tests/floor/floor.json`**, because every
test-adding packet would otherwise bump one line of one file and collide with every lane-mate
(SP-072 amendment; green alone, RED at merge). A packet that declares `client/tests/**` in
`fileScopeMustNotChange` forbids the lane from touching the pin *at least as completely* as one
that declares the literal — it forbids strictly more. The property is **"is the pin inside the
declared no-go set"**, and `patternCovers` asks exactly that. `Contains` asks whether the author
**spelled** a particular string, which is a proxy for the property and, per §1, a leaky one in
both directions.

The trade named in row 32 is "the literal is the easier thing to grep for". That is a property of
*reading the packets*, not of *the rule holding*, and it is already served: the shared decision
reports the declared values it accepted, so every violation message still prints what was
declared. Choosing the proxy over the property to keep grep convenient is the same inversion as
fixing the packets to please the stricter rule.

Nothing is loosened. The surviving check is strictly stronger than today's C# check on the point
that matters (the `.bak` case now fails where it used to pass) and identical on the 56 bound
packets.

## 3. WHAT MAKES FUTURE DRIFT IMPOSSIBLE RATHER THAN UNLIKELY

Not a shared constant, and not a note. **The C# guard stops implementing the decision at all.**

`client/tools/wave/validate-wave.mjs` becomes the single owner of everything the two guards must
agree on, and grows a read-only projection mode:

```
node client/tools/wave/validate-wave.mjs --emit-packet-scopes <spineTasksDir>
```

which prints JSON: for every packet-root `PROMPT.md` under the named directory, the packet dir and
number, the parsed `testCommand` / `floorDelta` / `fileScopeMustNotChange` rows with line numbers,
the wrapper-routing verdict, the **chokepoint coverage verdicts** for the floor pin and the task
board, the validator's own per-packet violations, and the pinned fixture cases with the
validator's verdict on each. It guesses no tree: the directory is required, so the mode is a pure
projection.

`FloorWrapperGuardTests` then **consumes** those verdicts. Its `SharedFloorPin` constant stops
being a predicate and becomes a cross-check against the value the validator reports. There is no
coverage predicate left on the C# side, so there is nothing for a future edit to drift *from*:
the two cannot disagree because there is only one of them. `patternsIntersect`/`patternCovers`
are exported once and called once.

What the C# keeps, deliberately: its own packet enumeration (so it can refuse to go blind), its
own grandfather IDs (65 / 73), and its own violation text. The enumeration is then **cross-checked
for set equality** against the validator's — which closes the other half of the MIRROR note
(`:42-48`) for free, and fails closed on any asymmetry.

Residual, stated rather than hidden: the C# still runs `node`. A machine without node fails these
guards rather than skipping them — the same disposition `CitationNeedleTests.cs:344-374` already
takes ("a missing `node` or `git` is a hard FAILURE and never a skip: both tier-1 gates are node
scripts"), and correct here because the floor gate *is* a node script.

## 4. THE ONE-SIDED-UPDATE DEMONSTRATION, AND WHICH EDIT EACH NEW GUARD REDS ON

New file `client/tests/CcpClient.Tests/WaveGuardConvergenceTests.cs`, **7 facts**:

| # | fact | reds on |
|---|---|---|
| 1 | `AGlobAndALiteralDeclaration_AreAcceptedIdentically_ByBothGuards` — a synthetic `spine-tasks/` holding two well-formed bound packets identical but for `client/tests/floor/**` vs the literal; **both** the validator's per-packet violations and `FloorWrapperGuardTests`' chokepoint violations are empty for **both**. The wave-60 packet shape, end to end. | either guard treating a covering glob differently from the literal — i.e. the exact defect |
| 2 | `TheSharedDecision_AgreesWithItsPinnedFixture_OnEveryCase` — the fixture (literal / `client/tests/floor/**` / `client/tests/**` / `client/**` = covered; `client/docs/**` / `client/tests/floorX/**` / **`client/tests/floor/floor.json.bak`** / un-backticked prose mention = not covered; no row = rowFound false; backslashes normalise; second row may satisfy) | any change to the coverage semantics on either side, including a lockstep change that breaks glob==literal |
| 3 | `TheFloorGuard_RedsWhenONLYTheValidatorChanges` — `validate-wave.mjs` is copied to temp, its single coverage call site textually mutated to literal-only (the mutation is asserted to have applied exactly once, so a refactor that moves it reds here rather than silently no-opping), and the C# guard's own violation computation is run against the mutated oracle over the synthetic tree from fact 1: it now REPORTS the glob packet. | a JS-side-only edit. This is the whole claim of "by construction" |
| 4 | `TheFloorGuard_HoldsNoChokepointCoverageLogicOfItsOwn` — reads `FloorWrapperGuardTests.cs` as text and forbids a re-introduced predicate (`Contains(SharedFloorPin`, `IndexOf(SharedFloorPin`, a local `patternCovers`). Precedent: `ExecutionCensusTests.CensusGenerator_HoldsNoShapeLiteralOfItsOwn`. **Lexical and therefore incomplete — it closes the literal-reuse route and names it, it does not prove no shadow exists.** | someone re-adding a second implementation on the C# side |
| 5 | `TheHistoricalLiteralPredicate_IsDetectedAsDrift` — the exact `:224` predicate, quoted, run against the fixture: it must DISAGREE with the shared decision on `client/tests/floor/**` and on `client/tests/floor/floor.json.bak`. The replayed defect as a fact. | the drifted C#-side semantics being reintroduced anywhere |
| 6 | `EveryPacketTheValidatorAccepts_TheFloorGuardAlsoAccepts` — over the **live** 128-packet corpus, validator-clean implies floor-guard-clean. The direct statement of the incident (`WAVE OK` printed, base red). The converse is deliberately not asserted: the validator legitimately raises MORE (row cardinality, ID reuse), and that difference is declared. | any future rule that lets `WAVE OK` coexist with a red suite |
| 7 | `TheValidatorAndTheFloorGuard_EnumerateTheSamePackets` — set equality over the live corpus. | the other half of the MIRROR note |

Facts 1, 3, 6 are the load-bearing ones. Fact 3 is the literal demonstration that a one-sided
update fails; fact 1 pins the glob/literal equivalence as a fact; fact 6 pins the outcome.

## 5. FILE SCOPE — every edit stays inside it

- `client/tools/wave/validate-wave.mjs` — extract the shared decision, add the fixture, add
  `--emit-packet-scopes`, keep the existing wave CLI byte-compatible (`port-wave.workflow.mjs:86`
  invokes it positionally and must keep working).
- `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` — consume the oracle; delete the three
  now-duplicated row regexes; keep `PacketNumber()` (enumeration cross-check) and `DotnetTest()`
  (auditor fact).
- `client/tests/CcpClient.Tests/WaveGuardConvergenceTests.cs` — new.
- `client/docs/wpf-surface-reachability.md` — divergences **D296-D305 only** (D295 is the last id
  on disk; the lane-mate SP-137 is allocated D306 onward, so the ranges are disjoint).
- `spine-tasks/SP-136-wave-guard-convergence/**` — `plan.md`, `record.md`, `floor-delta.json`.

`client/tests/floor/floor.json` is never opened.

## 6. CONSTRAINTS VERIFIED BEFORE COMMITTING TO THIS DESIGN

- **`client/tests/floor/vacuous-shape-ledger.json` is out of scope** and this blocked the previous
  packet (D295). Checked: `VacuousShapeGuardTests` anchors on `path::method` **key** and **shape
  set**, not on the ledger's `line` field, so editing `FloorWrapperGuardTests.cs` is safe provided
  its three `[Fact]` names and their `["fs-predicate"]` shape sets are preserved — the
  `Assert.True(Directory.Exists(...))` never-skip check stays in each body, exactly as the ledger
  reason at `vacuous-shape-ledger.json:620-624` demands. The **new** file's `[Fact]` bodies must
  carry NO shape at all: no `File.Exists(`/`Directory.Exists(` (moved to helpers), no
  `Environment.GetEnvironmentVariable`, no `OperatingSystem.Is`, no `Assert.Skip`, no bare
  `return` before the first assertion, and at least one depth-0 assertion each.
- **Timing guard**: process waits use `TestWait.Until(process.WaitForExitAsync(), ...)`, the
  pattern `CitationNeedleTests.cs:382` already uses. No `Thread.Sleep`, `Task.Delay`, `Stopwatch`,
  `SpinWait`, `TickCount`, `DateTime` poll, or `*Timeout = TimeSpan.` — so
  `TestTimingGuardTests` (out of scope) needs no new pin.
- **Citation detector**: `detect.mjs:152-156` states it cannot see citations into
  `client/tools/**` or into any `.mjs`; `client/tests/**` `.cs:line` citations already exist in
  `wpf-surface-reachability.md` (D283 `InputWindowProbe.cs:295-318`, D282 `:286-289`) and are
  green, so the new divergence rows use forms already proven not to move a pinned count.
- Nothing else in `client/tests/**` or `client/tools/**` binds `validate-wave.mjs`.

## 6b. BASELINE, TAKEN AT `766be7ac0` BEFORE ANY EDIT — AND THE BASE IS NOT GREEN

`dotnet build client/CcpClient.sln -c Debug` -> **Build succeeded, 0 Warning(s), 0 Error(s).**

`node client/tests/floor/check-floor.mjs` (through `with-slot --slots 3`, with another lane
building concurrently):

```
Failed: 1, Passed: 2596, Skipped: 2, Total: 2599 - CcpClient.Tests.dll (net10.0)
FLOOR CHECK FAILED (SP-065):
  dotnet test exited 1 for CcpClient.Tests — runner-level failure
    CcpClient.Tests.SoundArbitrationTests.Construction_AbandonedThenFaults_CountStillDrops_CapNeverRefusesForever
      Assert.Equal() Failure: Values differ  Expected: 1  Actual: 0
```

**BASELINE FAILURE SET (before) = { `SoundArbitrationTests.Construction_AbandonedThenFaults_CountStillDrops_CapNeverRefusesForever` }.**
Total 2599 matches the pin exactly, so the count arithmetic is fine; this is a test failure, not a
pin mismatch. `CcpClient.HeadlessTests` did not report — the wrapper stops at the first failing
project — so the 152 headless half of the pin is **unobserved at baseline** and will be observed
only in the after-run.

Characterised, not assumed: re-run in isolation through the same slot wrapper,
`--filter FullyQualifiedName~SoundArbitrationTests` -> **Passed! Failed: 0, Passed: 52, Total: 52.**
So it is a **load-induced flake in the orphan-construction budget path, pre-existing at the base
and untouched by this packet** (zero product edits existed when it was observed). It is recorded
here as the BEFORE set precisely so the after-run is compared against a failure SET and not a
count; if it recurs in the after-run it is this, and if a different name appears it is mine.

## 7. FLOOR DELTA

`spine-tasks/SP-136-wave-guard-convergence/floor-delta.json` = **unit +7, headless 0**.
Pin 2599 / 152, so the observed run must be **2606 unit / 152 headless**. `floor.json` is not
touched; the orchestrator sums the deltas at land.

## 8. WHAT THIS PLAN WILL NOT PROVE

Nothing here renders, composites, focuses a window, plays audio, or animates. These are file- and
process-level facts about two guards. A green floor run proves the guards agree on the corpus and
on the pinned cases; it does not prove they agree on an input neither the live corpus nor the
fixture contains — which is why the C# side keeps no second implementation to disagree *with*,
rather than relying on the corpus alone.
