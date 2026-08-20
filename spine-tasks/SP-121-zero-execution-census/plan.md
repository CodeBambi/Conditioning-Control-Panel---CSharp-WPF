# SP-121 — plan checkpoint (Review Level 3, step 1)

Branch `lane/SP-121-zero-execution-census`, worktree
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-a83c52ca28f44816b`, base `4276249e`.
No product file touched. No csproj touched. Nothing built yet beyond the premise probe.

## 0. The premise, re-measured on this tree

Command exactly as the packet gives it (10 tests, `HapticGateTests`, `--collect:"Code Coverage;Format=cobertura"`):

| Packet claim | Measured here | Verdict |
|---|---|---|
| works on net10.0, no new package | ran clean, `Passed: 10` | confirmed |
| 15 MB XML | 15,156,801 bytes | confirmed |
| 2661 `<class>` entries | 2661 | confirmed |
| `obj/**` paths 87 | 87 | confirmed |
| synthetic shapes 1361 | 1438 total; 1361 once the 77 synthetic entries already inside `obj/**` are not double-counted | confirmed, reconciled |
| remainder 1213 | 2661 − 87 − 1361 = 1213 | confirmed arithmetic |
| the 21 executed are the gate/entitlement types | I get **18** under my rule, and they are exactly `HapticGate*`, `Entitlement*`, `Capability*` | confirmed in substance, count differs — see §1.6 |

The instrument is sharp: with 10 haptic-gate tests running, the only types with a covered line are
`HapticGate`, `HapticGateDecision` + its 3 cases, `EntitlementOutcome` + its 3 cases,
`EntitlementReason`, `CapabilityState` + its 6 cases, `CapabilityReason`.

## 1. The "shipped type" rule, clause by clause

Universe = every `<class>` entry in the cobertura report(s). Probe universe: **2661**.
Each clause below states what it removes and why in one sentence. Counts are from the probe; they are
static properties of the assembly and will be restated verbatim from the full run.

### C1 — assembly: keep only `<package name="CcpClient.Desktop">` — removes **1633**, leaving 1028
The row asks about *shipped* types. `CcpClient.Desktop` is the assembly that gets published;
`CcpClient.Tests` is not shipped, and a test class that did not execute is the floor's question
(a test count), not this census's.
**Counterfactual I will publish rather than assume:** the full run will state how many
`CcpClient.Tests` types have zero executed lines. If that number is ~0 the clause removed nothing
real; if it is large, that is a finding I report, not one I bury. (On the 10-test probe it is
550 of 553, which is meaningless because only 10 tests ran — hence measuring it on the full suite.)

### C2 — compiler-generated name shape: drop an entry if ANY dot-segment of its fully-qualified name begins with `<` — removes **364**, leaving 664
No C# identifier may begin with `<`, so a segment that does was emitted by the compiler and written
by nobody. This is one clause, not five, and it covers every shape present: `<>c`,
`<>c__DisplayClassN_M`, `<Method>d__N`, `<<Method>b__71_0>d`, `<>c<TModel>`,
`<RegexGenerator_g>FE8F…__SlugRegex_0` and its nested `RunnerFactory`/`Runner`, and would cover
`<Module>` / `<PrivateImplementationDetails>` if they appeared.

### C3 — XamlIl deferred-content closure: drop an entry whose FINAL segment matches `^XamlClosure_[0-9]+$` — removes **2**, leaving 662
Avalonia's XAML compiler emits these nested classes for deferred content and no source file declares
them; they are the only compiler-generated shape in this assembly that does not begin with `<`.
The two are `Features.Companion.CompanionWindow.XamlClosure_1` and
`Views.MainWindow.XamlClosure_2`. I found them by reading the 17 `.axaml`-backed entries, not by
pattern-guessing.

### That is the whole rule. Three clauses. Two clauses I deliberately did NOT write:

**Rejected: exclude `obj/**` file paths (would remove 5 in this assembly).** The premise used it and
it is wrong here: `CcpClient.Desktop.Features.Dtrh.DtrhLoom` has an entry in
`obj\…\RegexGenerator.g.cs` because `[GeneratedRegex]` puts half of that partial class there.
Dropping by path would discard a real type's real coverage. Path is not evidence of authorship;
name shape is. C2 already removes the 4 genuinely generated regex types by name.

**Rejected: exclude any name containing `<` (would remove 367 instead of 364).** It would eat three
types somebody wrote — `Audio.OrphanSafePlayerFactory<TPlayer>`, `Persistence.PersistenceStore<TModel>`,
`Session.PacedSessionEffect<TFiring>`. This is the exact shape of the trap: a rule that looks tidier
and hides three real answers. The per-segment form of C2 is strictly narrower and keeps all three.

### 1.5 — the loud-failure valve (not an exclusion)
Any entry that survives C1–C3 whose name does not start with `CcpClient.Desktop.` is **kept and
printed under `UNCLASSIFIED — the rule is incomplete`** in the census. It is not dropped. Today the
set is empty (0). A future source generator that invents a new shape therefore appears in the report
as an unexplained row rather than silently joining the zero list or silently vanishing.

### 1.6 — why 18 and not the packet's 21
The packet's rough cut counted class *entries* across both packages and did not merge partials.
Mine counts *types* in the product assembly after the merge in §2. Same executed set, different
denominator: 662 entries merge to **646 types**, of which 18 executed and **628 had zero executed
lines** under the 10-test probe. Recorded as a spec-versus-measurement discrepancy, resolved in
favour of the measurement; the packet's own sentence ("exactly the gate and entitlement types") is
what the 18 are.

## 2. Nested types and partial classes — the two decisions the packet demands

**A nested type is counted SEPARATELY, as its own row.** `HapticGateDecision.Allow` stands beside
`HapticGateDecision`. A discriminated-union case that no test ever constructs is precisely the dead
behaviour the row asks about, and folding it into its parent would hide it behind a live parent.
This is the choice that makes the census BIGGER; the opposite choice would be quarantine by
arithmetic. I will additionally report the top-level/nested split so the number is readable, because
reporting a breakdown is not the same as excluding one.

**A partial class is ONE type: entries are merged by fully-qualified name, coverage is the UNION.**
`CcpClient.Desktop.App` appears twice — `App.axaml` and `App.axaml.cs`; `DtrhLoom` appears twice —
its source file and the generated regex file. They are one type at runtime and one type in the head
of whoever wrote it, so a type is zero-execution only when EVERY entry bearing its name has zero
covered lines. 16 of the 662 probe entries are multi-entry types.
**Stated limit:** a completely dead HALF of a live partial class is invisible to a type-level
census. That is a method-level question and this census does not answer it; it goes in "what this
cannot see".

## 3. How I will PROVE IT BITES — both directions, nothing committed

Both known instances (SP-101, SP-118 `SystemScheduleClock`) are covered now, so history proves
nothing. Construct it:

1. **Direction A (it names the dead type).** Add, uncommitted, one file under
   `client/src/CcpClient.Desktop/` declaring a type with executable lines that nothing calls
   (the SP-118 shape: a class with a method body and a `catch`). Regenerate the census over a small
   filtered instrumented run. **Expect: the new type appears in the zero-execution list by name.**
2. **Direction B (it stops naming it once a fact drives it).** Add, uncommitted, one xunit fact in
   `client/tests/CcpClient.Tests/` that calls it. Regenerate. **Expect: the type is gone from the
   zero list and the total drops by exactly one.**
3. `git checkout`/`rm` both files, then `git status --porcelain` must show neither, before any gate
   runs and before any commit. `client/src/**` is closed to my commits; a reverted probe is not a
   product change and a committed one would be.

Both directions run against a filtered instrumented run, not the full suite: the collector
instruments the whole assembly regardless of filter, so the *universe* is complete either way and
only the executed set is smaller — which is all the probe needs. The transcript of both directions
goes in `record.md`.

## 4. What I do when the instrumented run reds

The suite is bounded at 0.20% fenced / 9.5% suite and is never zero (`task-board.md:34`), and
instrumentation moves timing. A coverage run is DIAGNOSTIC and is never a gate. So:

- Record the run as red **with the failing test's fully-qualified name and the counters** in
  `record.md`, and carry a one-line caveat into the census itself.
- **Use that run's artifact anyway.** A test that failed still executed the lines it reached, so the
  executed set is real; the only distortion is that a red test may have stopped short and left a
  type in the zero list that a green run would have driven. That is stated, not chased.
- **No re-run for a prettier census. No touching a test to make one pass.** If a red repeats I file
  it as a finding.
- The two gates (`check-warnings.mjs`, then `check-floor.mjs`) run ALONE, after the probes are
  reverted, and never concurrently with a coverage run — another lane is live this wave.

## 5. What gets built (step 2), and the no-threshold guarantee

| Path | What |
|---|---|
| `client/tools/coverage/shipped-type-rule.json` | the rule as data: clause id, kind, pattern, defence, so the tool and the test read the SAME rule |
| `client/tools/coverage/census.mjs` | one command: runs both suites instrumented into a temp dir OUTSIDE the worktree, unions the cobertura reports, applies the rule, writes the census, deletes the artifacts |
| `client/docs/execution-census.md` | the committed answer + the rule + what it cannot see |
| `client/tests/CcpClient.Tests/ExecutionCensusTests.cs` | pure-logic guards (below) |

Node `.mjs`, matching `client/tests/floor/*.mjs` and `client/tools/{gate,wave,citations}/*.mjs`.
**No new csproj, no new project, no `PackageReference`** — a new project would need the solution
edited and that is not mine either.

**Both suites, unioned.** Running only `CcpClient.Tests` would put every View and Window in the zero
list falsely, because the headless project is what drives them. The census is the union of
`CcpClient.Tests` and `CcpClient.HeadlessTests` coverage.

**Determinism:** output carries type names, repo-relative source paths (re-rooted at `client/`),
and counts. No timestamp, no machine name, no absolute path, no duration, ordinal sort. It must diff
cleanly next wave. Artifacts go to `os.tmpdir()`; nothing `.coverage`/`.cobertura.xml` is ever
written inside the worktree, let alone committed.

**NO threshold, NO target, NO failing gate on the number.** The tests I add assert none of:
- `ExecutionCensusRule_ClassifiesEveryKnownShape` — the JSON rule, applied by .NET `Regex`, over a
  fixture table of real shapes: the synthetic ones classify as excluded AND the three real generic
  types (`PersistenceStore<TModel>`, `OrphanSafePlayerFactory<TPlayer>`, `PacedSessionEffect<TFiring>`)
  classify as KEPT. **This is the anti-widening guard**: the tempting "contains `<`" rule reds here.
- `XamlClosureClause_DoesNotEatARealTypeNamedLikeOne` — boundary of C3.
- `Census_IsInternallyConsistent` — universe = removed + kept; kept entries merge to N types;
  N = executed + zero-execution. Arithmetic, not tolerance.
- `Census_IsDeterministicallyOrdered` — sorted ordinal, no duplicates.
- `Census_CarriesNoMachineIdentity` — no timestamp, drive letter, or machine name in the committed file.
- `Census_And_Rule_DeclareTheSameClauses` — the doc's clause table and the JSON agree, so a clause
  cannot be widened in code without the written defence moving with it.

Estimated **+6 unit / 0 headless**; declared in `spine-tasks/SP-121-zero-execution-census/floor-delta.json`
once the file count is final. Pin is 2247/141, so my observed floor total will be 2253/141 and that
is correct, not a failure.

## 6. Known risks I am carrying into step 2

1. **Full-suite instrumented runtime is unmeasured.** 2247 + 141 tests under the collector may take
   many minutes and produce a much larger XML than 15 MB. Mitigation: stream-parse, temp dir outside
   the repo, delete after. If it is unworkable I report that rather than silently censusing a subset.
2. **The headless project under the collector is unproven** (Avalonia headless + datacollector).
   If it cannot produce cobertura I do NOT ship a unit-only census pretending to be complete — I
   report the gap and name what it makes unanswerable.
3. **The number will be large** (probe floor: 628 of 646 with 10 tests). I report it as measured.

## 7. Stopping here

This is the plan checkpoint. Written before any tool, doc, or test file exists, per Review Level 3.
Awaiting review before step 2.
