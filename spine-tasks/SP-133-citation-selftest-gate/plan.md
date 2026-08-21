# SP-133 — plan (Review Level 3 checkpoint, written before any product edit)

Worktree `C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-ae79e5a291fa8630b`,
branch `lane/SP-133-citation-selftest-gate`, base `79d08a27b`. Nothing outside this file has been
edited.

---

## 1. Premise, verified rather than inherited

| Claim | How I checked | Result |
|---|---|---|
| `self-test.mjs` holds 25 facts, F1-F24 plus F2b | `grep -n '^test(' client/tools/citations/self-test.mjs` | 25 `test(` calls; F2b is the "plus one" |
| Nothing runs it | `grep -rn "citations/self-test" client/tests client/tools client/docs` | only `client/docs/task-board.md:49`, `:330` (the two board rows) and `CitationNeedleTests.cs:30` — all of which say it is unrun. No `.cs`, no `.mjs`, no gate invokes it |
| It is green today | `node client/tools/citations/self-test.mjs` | `tests 25 / pass 25 / fail 0`, exit 0, 23.6 s |
| The floor cannot see it | `check-floor.mjs` discovers csproj entries under `tests/` in `client/CcpClient.sln` and runs them | a node file under `client/tools/` is invisible; the bridge must be a `.cs` fact |

## 2. The bridge's shape

One new file, `client/tests/CcpClient.Tests/CitationSelfTestGateTests.cs`, built on the reviewed
spawn pattern at `ExecutionCensusTests.cs:625-684` (and its sibling `CitationNeedleTests.cs:349-406`):
`Process.Start` with `ArgumentList`, both streams read concurrently with the wait, `TestWait.Until`
on `WaitForExitAsync()`, kill-tree on window expiry with the original exception winning, hard failure
on a `Win32Exception` from `Process.Start`.

**Invocation:** `node --test-reporter=tap <repoRoot>/client/tools/citations/self-test.mjs`, cwd =
repo root. Verified working on node v24.5.0 when the file is run directly (not via `node --test`);
captured transcript in the scratchpad. TAP rather than the default spec reporter because TAP is a
machine format with a plan line and a summary block, so the bridge parses a contract instead of
console decoration.

**Transcript contract** (measured from the real run, both green and red):

```
ok 17 - F16: a needle that MOVED emits ...           <- one per fact, column 0
not ok 17 - F16: a needle that MOVED emits ...       <- the red form, with a YAML block after it
1..25                                                 <- plan
# tests 25 / # suites 0 / # pass 25 / # fail 0 / # cancelled 0 / # skipped 0 / # todo 0
```

**One run, many facts.** The self-test costs ~23.5 s, so the process runs ONCE per assembly behind a
`static readonly Lazy<Task<ToolRun>>` and every fact asserts over that one transcript. It is not
given `TestContext.Current.CancellationToken`: a shared run must not be tied to whichever test
happened to touch it first.

**Window.** `TestWait.DefaultWindow` is 20 s and the self-test takes 23.5 s, so the signal overload
gets an explicit `window:` — a named constant of 3 minutes, ~7.6x the measured 23.5 s, sized for a
lane running through the 3-slot gate limiter. It carries the measurement and the date in its doc
comment. `TimeSpan.FromMinutes` is not in `TestTimingGuardTests.ForbiddenTokens`, and
`AudioCapabilityTests.cs:559` is the precedent for an explicit unmarked `window:`.

**Parse and verdict are pure and separate from the spawn**, so the failure paths can be exercised
without a process: `Parse(exitCode, stdout, stderr) -> Transcript`, and `Transcript.Problems()`
returning a list of named reasons (empty = clean). `Problems()` reds on: non-zero exit; zero result
lines; any `not ok`; any `# SKIP`/`# TODO` directive on a result; `fail`/`cancelled`/`skipped`/`todo`
non-zero; `pass + fail != tests`; results counted != `tests`; plan != `tests`. A parser that trusted
only the exit code, or only the summary counters, fails two of the facts below.

## 3. The eight facts

| # | Fact | Subject |
|---|---|---|
| 1 | `TheCitationSelfTest_RunsClean_EveryFactPassing` | the real run: exit 0 and `Problems()` empty; the message names every failing fact id with its TAP error block and the stderr tail |
| 2 | `EveryAnchoredFact_IsPresent_AndPassing` | the NAME anchor: all 25 ids (F1, F2, F2b, F3..F24) present and `ok`. Deleting or renaming a fact reds; ADDING one does not. The board's acceptance is "putting F1-F24 on the floor" (`task-board.md:330`), so the anchor covers the whole file, not only the classification ten |
| 3 | `TheTranscriptTotals_ComeFromTheScript_AndAgreeWithItsOwnResults` | the count is DERIVED: plan == `# tests` == results listed == `# pass` + `# fail`, non-empty, and the derived total is written to `ITestOutputHelper` so it is reported rather than typed |
| 4 | `AFailingFact_IsReadAsRed_EvenAtExitZero` | the real stdout mutated to the exact red form node emits (`ok N - F16` -> `not ok N - F16`, `# pass 25` -> `24`, `# fail 0` -> `1`), fed to `Parse` with exit code **0**: still red, and names F16. Proves the bridge does not lean on the exit code |
| 5 | `ATruncatedTranscript_IsReadAsRed_TheSummaryIsNeverTrustedAlone` | one `ok` line deleted, summary untouched: red on the arithmetic. Proves the bridge does not lean on the summary |
| 6 | `ANonZeroExit_IsReadAsRed_EvenWithACleanTranscript` | exit 1 + the real clean stdout: red. The other half of fact 4 |
| 7 | `AnAbsentInterpreter_FailsNamingIt_NeverSkips` | the runner called with an interpreter that does not exist: `InvalidOperationException` whose message names it and says the guard refuses to skip |
| 8 | `AnAbsentScript_FailsNamingIt_NeverSkips` | the runner called with a script path that does not exist: same, naming the path. Never spawns |

Facts 4 and 5 mutate the transcript captured from the REAL run, so they cannot drift from the
reporter's format the way a hand-written fixture would. Each mutation asserts it actually applied
before asserting the verdict, so neither can pass by silently doing nothing.

No fact body uses `File.Exists`, `Directory.Exists`, `OperatingSystem.Is*`,
`Environment.GetEnvironmentVariable`, `Assert.Skip*`, or an early `return;`, and every fact has an
`Assert.` at guarding-depth 0 — so the new file introduces no `VacuousShapeDetector` site and needs
no entry in `client/tests/floor/vacuous-shape-ledger.json`, which is out of scope.

## 4. Fail, never skip — the three routes, all closed

| Route | Behaviour |
|---|---|
| `node` absent / unlaunchable | `Win32Exception` from `Process.Start` is rethrown as `InvalidOperationException` naming the interpreter: node is a hard requirement of this tree (both tier-1 gates are node scripts). Pinned by fact 7 |
| `self-test.mjs` absent | existence check in the runner (a helper, never a fact body) throws naming the path. Pinned by fact 8 |
| Runs but exits non-zero, or exits 0 with a dirty transcript, or prints a transcript the parser does not recognise | `Problems()` names the reason and the fact fails. "No TAP result lines" is itself a named problem, mentioning `--test-reporter=tap`, so a node build that ignored the flag fails loudly instead of passing on an empty result set |

There is no `Assert.Skip`, no `allowedSkips` entry, and no conditional in any fact body.

## 5. Where the count comes from

Nowhere in the `.cs` file is the literal 25 written as an expected total. Fact 3 takes the number
from the script's own `1..N` plan line and its own `# tests N` summary, checks those two against the
number of result lines it parsed and against `# pass + # fail`, and prints the result. Vacuity (a
transcript with zero facts satisfies every one of those equalities) is closed by fact 2's name
anchor and by an explicit non-empty assertion. The anchor is a list of NAMES, not a count: it reds on
removal and stays quiet on addition, which is the opposite failure direction from a pinned total and
is the same idiom as `TestTimingGuardTests`' pins.

## 6. The red demonstration — which edit, and why that one

`client/tools/citations/detect.mjs:966`, the needle-mode delta:

```js
const delta = now[0] - then[0];      // ship
const delta = then[0] - now[0];      // the mutation: sign flipped
```

It is a genuine CLASSIFICATION regression (a moved subject reported with the wrong direction of
shift), it is one character of surface, and it hits exactly one subject: F16's
`assert.match(moved[0].reason, /:4 -> :11 \(\+7\)/)`.

Already measured against untracked COPIES of both tools in the scratchpad, so the repository was not
touched: mutated copy -> exit 1, `not ok 17 - F16: ...`,
`error: The input did not match the regular expression /:4 -> :11 \(\+7\)/. Input: ':4 -> :11 (-7) since e7b8014...'`,
`# pass 24 / # fail 1`, and F15 and F17-F24 all still `ok`.

At implementation time this gets repeated **in the worktree at the committed head**: apply the flip
to the tracked `detect.mjs`, run `node client/tests/floor/check-floor.mjs` through the slot limiter,
record the red and the head SHA in `record.md`, then `git checkout -- client/tools/citations/detect.mjs`
and prove the revert with `git status --porcelain` and a re-run. `detect.mjs` is CLOSED: it ends the
packet byte-identical to `79d08a27b`, and I will show that with `git diff --stat` against the base.

## 7. Files this packet touches

| File | Change | Why |
|---|---|---|
| `client/tests/CcpClient.Tests/CitationSelfTestGateTests.cs` | new | the bridge (`fileScopeMustChange`) |
| `client/tools/citations/self-test.mjs` | comment-only, **line count preserved** | its header (`:4-11`) and its F15-F24 section header (`:631-637`) both state "NO STANDING GATE IN THIS REPOSITORY RUNS THIS FILE". That becomes false with this packet, and this is the file anyone touching the classifier reads first. Replacement blocks occupy exactly the same lines so `self-test.mjs:5-12`, cited from a file I may not edit, still points at the same block |
| `client/docs/wpf-surface-reachability.md` | append `## SP-133` section, divergence rows **D275 onward** | divergences only, as scoped |
| `spine-tasks/SP-133-citation-selftest-gate/{plan.md,record.md,floor-delta.json}` | new | packet artifacts |

Nothing else. `detect.mjs`, `upstream-citation-inventory.json`, `CitationNeedleTests.cs`,
`floor.json`, `client/tests/floor/**`, `client/src/**`, `task-board.md` are untouched. No csproj edit
is needed: the test project globs `*.cs`.

## 8. Floor delta

`spine-tasks/SP-133-citation-selftest-gate/floor-delta.json` = `{ "packet": "SP-133-citation-selftest-gate",
"unit": 8, "headless": 0, "reason": ... }`.

Pin 2547 unit / 152 headless; expected observation **2555 unit / 152 headless**. I will state both
numbers and the arithmetic in the report. `floor.json` is never opened.

## 9. Divergences, D275 onward

Planned rows (final wording at write time):

- **D275** — the gate exists: `self-test.mjs`'s 25 facts run from the unit suite on every floor run;
  what that does and does not put on the floor (it pins the classifier's fixtured behaviour; it does
  not widen the detector's corpus or token class, and `detect.mjs` was not edited).
- **D276** — the red demonstration, with the mutation, the observed red and the head SHA.
- **D277** — the cost, measured: ~23.5 s of wall clock added to a check-floor run, in one fact, and
  why that was accepted rather than parallelised (changing the self-test's execution model is a tool
  change, not a bridge).
- **D278** — the residual: `CitationNeedleTests.cs:30-31` still says no standing gate runs the file
  and cites `self-test.mjs:5-12`. That file is `fileScopeMustNotChange` for this packet, so the
  prose is left stale and named here; the line citation still resolves because the replacement block
  preserves the line span. Also names the two board rows (`task-board.md:49`, `:330`) that the land
  should close, which are the orchestrator's to edit.

## 10. Risks and open findings, stated before the work

1. **`--test-reporter=tap` on a directly-run file** is verified on node v24.5.0 only. If a future
   node ignores it, the bridge fails loudly with a message naming the flag — never silently green.
2. **Runtime.** ~23.5 s added to the unit suite (one process, shared across eight facts). Measured
   again after the change and reported.
3. **`CitationNeedleTests.cs` goes stale** (see D278). Out of scope by contract; reported, not
   improvised.
4. **The anchor pins fact IDs.** Renaming F16 reds fact 2. That is intended review friction and is
   documented in the file so the next author knows the anchor is the thing to update.

## 11. What this will not prove

A green bridge proves the self-test's 25 facts execute and pass inside the floor run. It proves
nothing about interaction, rendering, audio, focus, window behaviour or animation — no UI is
involved. It does not widen what `detect.mjs` can see: the corpus/token-class limits recorded at
D260 (blind spot 7 — citations into `client/tools/**` and any `.mjs` are invisible to the tool) are
untouched and remain a separate board row. It does not make the detector a red test on the real
tree; the review-list contract at `detect.mjs:13-14` is unchanged.

---

## 12. Amendments after the plan gate (APPROVE + seven refinements)

Kept for honesty: this section records where the plan above was corrected, so nobody reads a
superseded prediction as a finding. See `record.md` for the executed result.

1. **A second mutation was taken.** `detect.mjs:957-958` swaps the `absentAtEndpoint` /
   `ambiguousAtEndpoint` counters, corrupting a bucket BOUNDARY (reds F22) where `:966` corrupts the
   moved row's PAYLOAD (reds F16). Both were watched red at `8beb0b679`.
2. **The window's doc comment names why it is not the banned widening.** `AudioCapabilityTests.cs:559`
   is 15 s, NARROWER than the default, so it is precedent for the syntax only; the justification
   written into the constant is that 23.6 s against a 20 s window is a deterministic false red, not a
   flake being silenced.
3. **The anchor matches the `Fn:` prefix, never the title** (`^(?<id>F\d+[a-z]?):`), stated in the
   code comment.
4. **The summary is parsed per token**, one counter per line; §2's slash-joined display was
   presentation in this document only. No single-line summary regex exists.
5. **§6's cited reason for declining parallelisation was wrong.** `self-test.mjs` is open to this
   packet; `detect.mjs` is the closed one. The real obstacle is that all 25 fixture bodies are
   synchronous, so node's in-file `concurrency` buys nothing without rewriting them.
6. **The cost prediction was wrong and is replaced by a measurement.** "~23.5 s added" did not
   happen: same command shape, back to back, 2547 tests in 46 s without the class and 2555 in 46 s
   with it. The 28.08 s shared run overlaps other collections.
7. **A stale citation was found and repaired in passing.** `self-test.mjs:6` credited
   `check-floor.mjs:253` for running the discovered projects; the run is at `:364`. Recorded as D278
   item 5.

## 13. Amendments after the code gate (APPROVE + three items)

1. **D277's first correction overstated itself.** "Not observable at the runner's resolution" was one
   pair reported as categorical. Restated as a band with its sample count: five pairs, three here
   and two independent at review, `without` 46/50/47/47/48 s and `with` 46/45/48/50/50 s. The
   order-of-magnitude correction holds; the exact tax does not resolve at n=5 and 1 s resolution.
2. **Both `Assert.True(mutated != run.StdOut, ...)` sites now say what they are.** The line-ending
   rejoin makes them near-tautological; the real anti-vacuity guard is the helper throwing, and the
   comment says so at the assertion so a later reader does not trust the wrong line.
3. **Filed, not built:** four `Problems()` branches have no fact behind them (the SKIP/TODO directive
   path, the missing-counter path, `pass + fail != tests`, and the missing-plan-line path). Cheap to
   pin because `Parse`/`Problems` are pure. A board row, not this packet.
