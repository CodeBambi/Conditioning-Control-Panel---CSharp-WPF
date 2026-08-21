# SP-133 — record

**Outcome: `client/tools/citations/self-test.mjs`'s 25 facts run from the unit suite on every floor
run, and a regression in `detect.mjs`'s classification reddens the suite. Demonstrated twice, at the
committed head, against two different classes.**

Base `79d08a27b`. Bridge commit `8beb0b679`. Branch `lane/SP-133-citation-selftest-gate`.

---

## 1. The bridge's shape

`client/tests/CcpClient.Tests/CitationSelfTestGateTests.cs`, built on the reviewed spawn pattern at
`ExecutionCensusTests.cs:620-685` (also `CitationNeedleTests.cs:349-406`): `ProcessStartInfo` with
`ArgumentList`, both streams read concurrently with the wait (the transcript is larger than a pipe
buffer), `TestWait.Until` on `WaitForExitAsync()`, kill-tree on window expiry with the original
exception winning, hard failure on `Win32Exception` from `Process.Start`.

**Invocation:** `node --test-reporter=tap <repoRoot>/client/tools/citations/self-test.mjs`, cwd repo
root, `StandardOutputEncoding`/`StandardErrorEncoding` pinned to UTF-8 so the redirected readers do
not use the Windows console code page and mangle the fact titles the failure messages quote.

**Why TAP.** It is a machine format with a `1..N` plan line, one column-0 `ok N - name` per fact, and
a per-line `# tests` / `# pass` / `# fail` summary. The default spec reporter emits no `ok N -` line
at all, which is why an ignored `--test-reporter` flag lands in the parser's named "no TAP result
lines" failure instead of reading as an empty green run.

**One process, eight facts.** The run is cached in a `static readonly Lazy<Task<ToolRun>>` and is
deliberately not given a per-test cancellation token, so a shared run is never tied to whichever
fact touched it first. Measured at this commit from the TRX: the shared run cost **28.08 s** inside
`EveryAnchoredFact_IsPresent_AndPassing` (the first fact to touch it); every other fact ran in
1-6 ms.

**Window.** `TestWait.DefaultWindow` is 20 s and this subject honestly takes 23.6 s, so the signal
wait takes an explicit 3-minute window. That is not the widening `TestWait` forbids: the banned move
is widening to paper over an INTERMITTENT pass, whereas 20 s here would be a **deterministic** false
red on a healthy tree. The measurement and that distinction are written into the constant's doc
comment. `TimeSpan.FromMinutes` is not in `TestTimingGuardTests.ForbiddenTokens`, and the guard runs
green.

**Parse and verdict are pure and separate from the spawn.** `Transcript.Parse(exit, stdout, stderr)`
then `Problems()`, which reds on: non-zero exit; zero result lines; any `not ok`; any `# SKIP`/
`# TODO` directive; non-zero `fail`/`cancelled`/`skipped`/`todo`; a missing summary counter;
`pass + fail != tests`; results listed != `tests`; plan != `tests`; no plan line. That separation is
what lets the negative-path facts exercise the verdict without a process.

### The eight facts

| Fact | Subject |
|---|---|
| `TheCitationSelfTest_RunsClean_EveryFactPassing` | the real run is clean; the failure message carries every failing fact with node's own YAML error block and the stderr tail |
| `EveryAnchoredFact_IsPresent_AndPassing` | the NAME anchor over all 25 ids, matched on the `Fn:` PREFIX only (not the title) so a reword survives and a deletion or rename reds |
| `TheTranscriptTotals_ComeFromTheScript_AndAgreeWithItsOwnResults` | plan == `# tests` == results parsed == `# pass` + `# fail`, non-empty, and the derived total is written to test output |
| `AFailingFact_IsReadAsRed_EvenAtExitZero` | the real transcript mutated to node's exact red form, judged at exit **0**: still red, naming F16 |
| `ANonZeroExit_IsReadAsRed_EvenWithACleanTranscript` | exit 1 with the real clean stdout: red |
| `ATruncatedTranscript_IsReadAsRed_TheSummaryIsNeverTrustedAlone` | one result line deleted, summary untouched: red on the arithmetic |
| `AnAbsentInterpreter_FailsNamingIt_NeverSkips` | `InvalidOperationException` naming the interpreter and "refuses to skip" |
| `AnAbsentScript_FailsNamingIt_NeverSkips` | same, naming the path, without spawning |

Both mutation helpers **throw** rather than return the input unchanged when they match nothing, so
neither negative-path fact can pass by silently doing nothing.

## 2. Where the fact count came from

The literal 25 is written nowhere as an expected total. The number is taken from the script's own
`1..N` plan line and its own `# tests N` summary and cross-checked against the result lines parsed.
Observed at this commit, quoted from the test's own output in the TRX:

```
self-test.mjs: 25 result line(s), plan 1..25, # tests 25, # pass 25, # fail 0, # skipped 0, exit 0
```

The vacuous corner (a transcript with zero facts satisfies every equality) is closed three ways: the
name anchor over 25 ids, an explicit non-empty assertion, and `Problems()` reddening on zero result
lines. The anchored ID list is the deliberate review friction if a fact is ever legitimately retired.

## 3. The red demonstration, at the committed head `8beb0b679`

Both mutations were applied to the **tracked** `client/tools/citations/detect.mjs`, run through
`node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs`, then
reverted with `git checkout --`.

**The red had to be distinguishable from pin arithmetic**, because this lane's ordinary run is
already red: with +8 tests, `check-floor.mjs` reports
`FLOOR VIOLATION — total drift: 2555 result(s) (pin total 2547)`. A broken classifier fails
**earlier and differently**, at `dotnet test exited 1 for CcpClient.Tests — runner-level failure`,
with named failures from the TRX.

### Mutation A — the moved class's payload

`detect.mjs:966`, `const delta = now[0] - then[0];` to `const delta = then[0] - now[0];`. The bucket
assignment is untouched (`moved += 1` at `:965`, `cls: NEEDLE_CLASS.MOVED` at `:968`); what it
corrupts is the SIGNED shift in the reason at `:972`, which `:975-976` makes the entire actionable
output of the class ("names the SHIFT, never which citation is wrong").

```
FLOOR CHECK FAILED (SP-065):
  dotnet test exited 1 for CcpClient.Tests — runner-level failure (see tail below)
  --- CcpClient.Tests: 3 named failure(s) from the TRX ---
    CcpClient.Tests.CitationSelfTestGateTests.EveryAnchoredFact_IsPresent_AndPassing
      anchored fact(s) present but not passing: F16
      error: The input did not match the regular expression /:4 -> :11 \(\+7\)/. Input:
        ':4 -> :11 (-7) since 983e633f64de0caff0c01ed4c26e1a3b7b854765'
    CcpClient.Tests.CitationSelfTestGateTests.TheCitationSelfTest_RunsClean_EveryFactPassing
      the self-test exited 1; ... self-test fact F16 (...) did not pass
Failed!  - Failed: 3, Passed: 2550, Skipped: 2, Total: 2555
```

The third failure is `AFailingFact_IsReadAsRed_EvenAtExitZero`, throwing
`no passing TAP result line for F16 to mutate; the negative-path fact would have proven nothing`.
That is the mutation helper's own anti-vacuity guard firing correctly: when the real run has already
broken F16, the negative-path fact refuses to pretend it proved something. Loud, not silent.

### Mutation B — a bucket boundary in a different class

`detect.mjs:957-958`, the `absentAtEndpoint` / `ambiguousAtEndpoint` counters swapped: one token, and
it corrupts a classification BOUNDARY rather than a row's payload.

```
FLOOR CHECK FAILED (SP-065):
  dotnet test exited 1 for CcpClient.Tests — runner-level failure (see tail below)
  --- CcpClient.Tests: 2 named failure(s) from the TRX ---
    CcpClient.Tests.CitationSelfTestGateTests.TheCitationSelfTest_RunsClean_EveryFactPassing
      self-test fact F22 (F22: a needle absent at the ENDPOINT is counted as uncomparable, never
      reported as unmoved) did not pass
      error: Expected values to be strictly equal: 0 !== 1
    CcpClient.Tests.CitationSelfTestGateTests.EveryAnchoredFact_IsPresent_AndPassing
      anchored fact(s) present but not passing: F22
```

Here `AFailingFact_IsReadAsRed_EvenAtExitZero` **passed**, because F16 was still green and mutatable:
the negative path works while a different class is broken.

### The revert is proven, not asserted

```
git checkout -- client/tools/citations/detect.mjs
git status --porcelain                                        -> (empty)
git diff --quiet 79d08a27b -- client/tools/citations/detect.mjs -> exit 0 (byte-identical to base)
```

`detect.mjs` was never widened and ends this packet identical to the wave base.

## 4. The absent-tool paths fail rather than skip

Both are on the floor as facts, not as prose:

- absent interpreter: `RunSelfTestAsync("ccp-node-that-is-not-installed", <real script>)` throws
  `InvalidOperationException` naming the interpreter and stating that node is a hard requirement of
  this tree and that the guard refuses to skip. Asserted on both substrings.
- absent script: `RunSelfTestAsync("node", <path that does not exist>)` throws naming the path,
  without spawning anything. Asserted on both substrings.

There is no `Assert.Skip`, no new `allowedSkips` entry, and no conditional in any fact body. The two
skips in the floor run are the pre-existing OS-gated ones.

## 5. Cost, measured rather than assumed

The plan predicted "~23.5 s added to the unit suite". **That prediction was wrong by an order of
magnitude**, and the first correction of it was wrong too: on one pair I wrote "not observable at
the runner's resolution", which is a categorical claim from a single sample. Corrected here and in
D277 as a band with its sample count.

Six pairs, same command shape (`--no-build`, one filtered to exclude the class, one not),
alternating, at the runner's whole-second resolution:

| Source | without the class (2547) | with the class (2555) |
|---|---|---|
| this lane, three pairs | 46, 50, 47 s | 46, 45, 48 s |
| independent, at review, three pairs | 47, 48, 47 s | 50, 50, 47 s |

Two of the three review pairs are +2 to +3 s, the third (taken at final review) is 47/47, and the
three taken here go both ways (-5, +1, 0). No pair separates the two populations on its own.
**What the pairs settle is the order of magnitude: 24 s of work inside one fact costs the suite
single-digit seconds, not 24. What they do not settle is the exact tax.**

**And the band is a fact about THIS MACHINE, not about the bridge.** Every pair was measured on a
16-core box, where xunit's collection parallelism is what absorbs the run. On a 4-core runner the
24 s would not be absorbable and the tax would approach its full size. The mechanism is the same
either way: the shared run (24.19 s to 28.08 s across observed TRX) sits in one collection, and
xunit runs collections in parallel, so it overlaps the rest of the suite instead of extending it
end to end.

Parallelising the self-test itself was rejected on a fact rather than a preference: all 25 fixture
bodies are synchronous `execFileSync`-driven git repositories, so node's in-file `concurrency` buys
nothing without rewriting all 25 to async, which is a tool rewrite rather than a bridge. (The plan
gave scope as the reason; the reviewer's correction is right - `self-test.mjs` is open to this
packet, `detect.mjs` is the closed one, and synchronicity is the real obstacle.)

## 6. `self-test.mjs`, comment-only and line-for-line

Two blocks said "NO STANDING GATE IN THIS REPOSITORY RUNS THIS FILE" and are now false: the header
(`:4-11`) and the F15-F24 section header (`:631-637`). Both are corrected in place, 862 lines before
and after, `15 added / 15 deleted`, so `self-test.mjs:5-12` - cited from `CitationNeedleTests.cs:31`,
a file this packet may not edit - still points at the same block.

**That preservation is a courtesy to human readers, not a mechanical requirement**: D260's seventh
blind spot puts `client/tools/**` and every `.mjs` outside the detector's corpus, so no tool checks
that span. Nobody should later mistake it for a guard.

**A rotted citation was found in the same block and repaired.** `self-test.mjs:6` credited
`check-floor.mjs:253` for running the discovered projects. `:253` is a comment line inside the
build-staleness note; the run is at `:364` (`runProject`, defined at `:321`). Discovery at `:80-107`
was correct. This is a live instance of the blind spot above: no tool in this repository could have
caught it, and it surfaced only because this packet had to restate the claim.

## 7. Floor numbers

Pin: **2547 unit / 152 headless**. Declared delta:
`spine-tasks/SP-133-citation-selftest-gate/floor-delta.json` = `+8 unit / 0 headless`.

Observed on the final green run: **2555 unit / 152 headless** = pin + declared delta, exactly. The
unit project reports `Failed: 0, Passed: 2553, Skipped: 2, Total: 2555`; the headless project is
`152/152` and untouched. `check-floor.mjs` therefore exits 1 on the pin arithmetic alone
(`FLOOR VIOLATION — total drift: 2555 result(s) (pin total 2547)`), which is the expected lane state
and is resolved by the orchestrator's `sum-deltas --apply` at land. `client/tests/floor/floor.json`
was never opened.

Warning gate: `WARNING GATE OK (SP-114): 0 warnings, 0 errors across 4 project(s)`, forced
non-incremental.

## 7b. THE FLAKE: a second sighting on board row 48, and my first analysis of it was wrong

`CcpClient.Tests.SoundArbitrationTests.Construction_AbandonedThenFaults_CountStillDrops_CapNeverRefusesForever`
failed **once in ten runs** of the unit project during the cost measurement, with
`Assert.Equal() Failure: Values differ / Expected: 1 / Actual: 0` (227 ms), in a filtered run. It
passed in the other nine, including both full floor runs and both red demonstrations.

**My first draft of this section said it "shares nothing with the citation facts" and pointed at "a
finalizer/GC-timed subject". Both clauses are false, and my own TRX disproves them.** Corrected here
because the correction makes this packet look WORSE, which is exactly why it has to land.

**What the evidence actually says** (measured A/B on this head, at code review):

- The node run's window **fully contains** the SoundArbitration block: all **52** of its facts run
  inside it. That inverts the exoneration board row 48 records for SP-124, whose *"TRX shows all
  four of its node spawns started after that test ended"*.
- **16 tests in flight on 16 CPUs** at the target fact's midpoint.
- Child-process test-seconds overlapping that block: **4.04 s without this class, 11.30 s with it**,
  a **2.8x increase in concurrent out-of-process load**. This is not a light child: 25 temp git
  repositories, each running `git` several times plus `detect.mjs` as a further child.
- `Expected: 1 / Actual: 0` is `SoundArbitrationTests.cs:1560`,
  `Assert.Equal(1, Volatile.Read(ref h.ConstructCount)); // the refusal started no pool thread`.
  **That is a thread-pool injection failure inside the 200 ms construction budget. Nothing
  GC-timed is involved.**

**It is a DIFFERENT FACT from the one board row 48 names, with OPPOSITE POLARITY.** The row names
`Construction_LockUnavailableAtCompletion_AbandonsWithoutCounting_NothingWasParked` at
`Expected: 0 / Actual: 1` (`:1625`), once in eight runs; this is
`Construction_AbandonedThenFaults_...` at `Expected: 1 / Actual: 0`. **The row's own acceptance
says a second sighting is escalation, not noise - this is that sighting, on a new fact.** And the
row's second named mechanism, *"lengthening any neighbouring test changes which facts run
concurrently with it"*, is now **demonstrated rather than merely unexcluded**.

**The RATE is unresolvable and no verdict is offered.** One in ten here against one in eight there
is statistically indistinguishable. **I do not claim this packet caused it, and I do not claim it is
unrelated.** What is established is the mechanism and the containment; what is not established is
whether the added load changes the frequency.

**Reported rather than repaired, and deliberately not re-run until it went green**: an intermittent
failure looked at once and dismissed is how a real defect becomes folklore. `SoundArbitrationTests`
is outside this packet's file scope, and `allowedSkips` would be exactly the wrong instrument -
board row 48 already says so, because the test is red-on-starved by deliberate authorial choice.

## 8. What this does NOT prove

- **Not that the classifier is correct.** Only that its 25 fixtured facts execute and hold, and that
  breaking the classification reddens the suite. Every fixture is a temp-dir repository; nothing in
  the file reads the real inventory, the real WPF tree or the real port sources.
- **No product code, no UI, no rendering.** Nothing here proves anything about interaction,
  rendering, audio, focus, window behaviour or animation. Neither `draw-verified` nor
  `presentation-verified` is touched, and no headed gate is discharged.
- **Observed on Windows only.** The floor runs there. Nothing is Windows-specific by construction
  (`ProcessStartInfo("node")` resolves through PATH; `self-test.mjs:100` passes `-c user.name` /
  `-c user.email` per commit so no global git identity is assumed), but that is an argument, not an
  observation, and only a Linux run settles it.
- **`detect.mjs` gained no reach.** Its corpus and token class are unchanged and remain a separate
  board row; every limit at D260 stands.

## 9. Divergences

D275 (the gate, and what it pins), D276 (the two red demonstrations with the head SHA), D277 (the
measured cost), D278 (five residuals: the stale `CitationNeedleTests.cs:30-31` prose, `floor.json`'s
`lastMovedBy`, board row `:330` CLOSED while `:49` is AMENDED NOT CLOSED, the un-widened detector, and the repaired
`check-floor.mjs:253` citation), plus a "What SP-133 does NOT establish" section. All in
`client/docs/wpf-surface-reachability.md`.
