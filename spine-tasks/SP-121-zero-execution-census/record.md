# SP-121 — record

Branch `lane/SP-121-zero-execution-census`. Base `4276249e`, merged `b5f789de` (the coordinator's
base unred) at `048eafb9`. Census row: `client/docs/task-board.md:32`.

**The answer: 42 shipped types have zero executed lines, out of a census universe of 649.**
No threshold was set, no gate was added on that number, and nothing was excluded to shorten it.

## The premise, re-measured before anything was built

Ran the packet's command verbatim (10 `HapticGateTests`, `--collect:"Code Coverage;Format=cobertura"`).

| Packet claim | Measured | Verdict |
|---|---|---|
| net10.0 host, no new package | ran clean, `Passed: 10` | confirmed |
| 15 MB XML | 15,156,801 bytes | confirmed |
| 2661 `<class>` entries | 2661 | confirmed |
| `obj/**` paths 87 | 87 | confirmed |
| synthetic shapes 1361 | 1438 total, of which 77 sit inside `obj/**`; 1361 after obj is removed first | confirmed, reconciled |
| remainder 1213 | 2661 − 87 − 1361 | confirmed |
| the ~21 executed are the gate/entitlement types | 18 under this packet's rule, and they are exactly `HapticGate*`, `Entitlement*`, `Capability*` | confirmed in substance |

**Discrepancy, resolved toward measurement.** The packet's 21-of-1213 counted class *entries* across
both packages without merging partials. This rule counts *types* in the shipped assembly after the
merge, giving 18-of-646 on that probe. Same executed set, different denominator. The coordinator has
corrected the record.

## The shipped-type rule, clause by clause, on the committed run

Universe: **3946** `<class>` entries across both cobertura reports.

| clause | removes | count | one-sentence defence |
|---|---|---|---|
| **C1** | any entry outside package `CcpClient.Desktop` | **1890** | the row asks about SHIPPED types, and an unexecuted test class is the floor's question, not this census's |
| **C2** | any entry with a dot-segment matching `^<` | **728** | no C# identifier may begin with `<`, so such a segment was emitted by the compiler and written by nobody |
| **C3** | any entry whose FINAL dot-segment matches `^XamlClosure_[0-9]+$` | **4** | Avalonia's XAML compiler emits these for deferred content; they are the only generated shape here that does not begin with `<`, and the anchors keep an authored `XamlClosure_Registry` |
| kept | merged by fully-qualified name | 1324 entries → **649** types | a partial class is ONE type; coverage unioned |

### Two clauses deliberately NOT written

- **`obj/**` path exclusion (the premise used it; refused).** `DtrhLoom` has an entry under
  `obj/.../RegexGenerator.g.cs` because `[GeneratedRegex]` (`DtrhLoom.cs:32`) puts half of that
  partial class there. Path is not evidence of authorship; name shape is.
- **"exclude any name containing `<`" (refused).** It removes 367 instead of 364 and eats three
  authored types: `OrphanSafePlayerFactory<TPlayer>` (`AudioSeams.cs:202`),
  `PersistenceStore<TModel>` (`PersistenceStore.cs:83`), `PacedSessionEffect<TFiring>`
  (`PacedSessionEffect.cs:50`). **This is the packet's central trap and it is now an executable
  fact**: `ExecutionCensusTests.TheTemptingWiderRule_WouldEatThreeAuthoredGenericTypes` reds if
  anyone widens C2 that way.

### The valve, and the prediction it settled

Anything surviving C1–C3 outside `CcpClient.Desktop.*` is **kept and flagged**, never dropped.
Observed: **0**.

Review predicted the valve would be non-empty because ~25 `CompiledAvaloniaXaml.*` types exist in
the shipped assembly. **I verified rather than trusting either party, and the prediction is refuted —
but the coordinator's stated reason is also not quite right.** Those types *do* exist and *do* carry
IL; they never reach the valve because the collector reports coverage **by source line**, and XamlIl
types are generated straight to IL from `.axaml` with no C# source behind them. So an empty valve
means *nothing unexplained reached the report*, not *nothing unexplained exists*. That distinction is
now stated in the census itself. The valve is executed code, not an unreached branch: two fixtures
(`CompiledAvaloniaXaml.XamlIlContext` and `!AvaloniaResources`) drive it in both the JS self-check
and the C# guard.

### Nested types and partial classes

- **A nested type is its own row.** A DU case nobody constructs is exactly the dead behaviour the row
  asks about; folding it into a live parent would hide it. This is the choice that makes the census
  bigger. 26 of the 42 are nested.
- **A partial class is ONE type, coverage unioned.** `App` (`App.axaml` + `App.axaml.cs`) and
  `DtrhLoom` (source + generated regex half) are single types, zero only if no half ran.

### Attribution — a correctness fix found mid-build, not in the plan

`StartupPhaseRunner` is a static class of async methods driven by 20+ tests and it appears in **no
`<class>` entry of its own**: the compiler moved every source line into `<RunAsync>d__N`, which C2
removes. Discarding those lines gives the census two failure modes it exists to prevent — a type
whose async body ran reading ZERO, and a type whose async body never ran being MISSED. So generated
entries' lines are now **attributed back to their declaring type** (724 entries re-attributed, 0
dropped). Measured impact on this tree: **0 false zeros, 0 missed zeros**, universe +3. Kept because
the next tree is not this tree.

## Proof that it bites, both directions

Probe: `client/src/CcpClient.Desktop/Sp121BiteProbe.cs`, shaped like SP-118's `SystemScheduleClock`
(a method body with a `catch` nothing drives). Both directions used the **identical** filter, so the
only difference between runs was the presence of the test file:

```
--filter "FullyQualifiedName~HapticGateTests|FullyQualifiedName~Sp121BiteProbeTests"
```

| direction | tests | zero count | probe named? |
|---|---|---|---|
| A — probe present, nothing drives it | 10 | **632** | **yes**, `Sp121BiteProbe`, 7 lines |
| B — one fact drives it | 11 | **631** | **no** |

The row-level diff between the two censuses is exactly one line and it is the probe. The count
dropped by exactly one; had it dropped by more I would have reported that rather than re-picking a
filter. Both probe files were deleted and `git status --porcelain` showed no residue before any gate
ran; the warnings gate's forced `--no-incremental` rebuild is what made the revert real in the
assembly, and it ran before the floor.

## What the census cannot see — including one I got wrong first

1. **235 authored-shape types are INVISIBLE rather than zero.** From the assembly's own TypeDef and
   MethodDef tables: 1324 type definitions, 884 of authored shape, 649 reaching the census. Of the
   235 absent, **212 have no method body at all** (70 interfaces, 62 enums, 45 structs, 35 classes)
   and **23 have a body with no source line mapped to it**.
2. **I initially wrote that fields-only nested types "have no method body". That is wrong and the
   census now says so precisely.** `OrphanSafePlayerFactory<TPlayer>.ConstructionSlot`
   (`AudioSeams.cs:228-233`) *does* get a compiler-supplied constructor; what it lacks is a sequence
   point. Corrected by measurement, not by argument.
3. **A member-less record reads ZERO even when a passing test constructs it.** Discovered by chasing
   `DtrhProtocol.DtrhPageMessage.Exit`, which `DtrhProtocolTests.cs:24` parses from `{"type":"exit"}`
   on a green theory row and which the census still called zero. **Settled with a controlled probe**
   rather than reasoning: a member-less record and an otherwise identical one-member record were
   constructed by the same passing fact; the member-less one landed in the zero list, the one-member
   control did not. The raw report shows `Exit: .ctor[0]` against `Pong: .ctor[1]`. Cause: for
   `sealed record Exit : Base;` the only line-mapped member is the copy constructor nothing calls.
   **13 of the 42 rows carry a `construction-invisible` marker.** They are marked, not excluded —
   excluding them would be widening a rule to shorten a list, which is the thing this packet exists
   to refuse. Treat such a row as unproven in both directions.
4. **The census is OS-conditioned.** Generated on Windows, so Linux legs never execute. Rows are
   marked `platform-conditional` when the type's **own source file** holds a platform predicate
   (`OperatingSystem.Is*`, `RuntimeInformation.IsOSPlatform`, `[SupportedOSPlatform]`) — a checkable
   property, deliberately not a guess from matching type names against OS-gated test names, because
   a wrong `os-gated` marker excuses a real defect as a machine artifact. **17 of 42** are marked;
   `SecretToolSecretStore` (116 lines, the Linux `secret-tool` store) is the clearest.
5. Executed is not tested; a dead half of a live partial class is invisible; zero is a fact about the
   suite, not a verdict on the code.

## The three findings most likely to be real defects

**1 — `SystemSoundClock` is the third clock in a family of three and the only one without the fault
guard.** `client/src/CcpClient.Desktop/Audio/AudioSeams.cs:127-138` schedules
`new Timer(_ => fire(), ...)` with **no try/catch**. Its two siblings both wrap the callback and both
say why: `SystemScheduleClock` (`Scheduling/ScheduleClock.cs:52-64`, :81-92) — *"a timer callback runs
on a thread-pool thread with no caller above it, so an exception escaping it is an UNHANDLED
exception and .NET terminates the process"* — and `SystemSessionClock` (`Session/SessionClock.cs:46-68`).
SP-101 fixed one, SP-118 fixed the other and found D188 doing it. **This is the same shape, unfixed,
and it is the default on three product paths**: `Companion/BarkPipeline.cs:116`,
`Features/Dtrh/DtrhHostWindow.axaml.cs:217`, `Features/Dtrh/DtrhNativeEffects.cs:53`. Zero executed
lines, so no test has ever thrown from a sound callback. Not marked `construction-invisible` and not
`platform-conditional` — this row is strong evidence.

**2 — the only production `IBarkAudioResolver` is driven by no test, and its stated contract is
unverified.** `DirectoryBarkAudioResolver` (`Companion/BarkPipeline.cs:70-78`) documents *"missing
file → null, never throws"*. It is the sole non-test implementation, wired once at
`Features/Dtrh/DtrhHostWindow.axaml.cs:239`; every bark test substitutes `RecordingResolver`
(`client/tests/CcpClient.Tests/BarkPipelineTests.cs:593`). `Path.Combine(root, audioFileName)` throws
`ArgumentNullException` on a null filename, so "never throws" is a claim no fact holds — the SP-118
shape exactly (a doc-comment contract on a product default that no test drives).

**3 — the DTRH host's composition block is the common cause, and it is itself zero-executed.**
`Features/Dtrh/DtrhHostWindow.axaml.cs:213-245` is the single construction site for the real seams:
`UnavailableDuckSink` (:216), `SystemSoundClock` (:217), `LogSinkAdapter` (:225),
`DirectoryBarkAudioResolver` (:239) — **all four are in the zero list, as is `DtrhHostWindow` itself
(833 lines)**. Nothing drives that block, so every real audio/clock/resolver seam enters the product
through a path no test executes; findings 1 and 2 are symptoms of it. (`UnavailableDuckSink` is a
documented named limit and is *not* itself a defect; it is listed here as evidence about the block.)

Filed, not fixed: this packet changed no product file.

## Gates and floor numbers

| gate | result |
|---|---|
| `check-warnings.mjs` | **OK** — 0 warnings, 0 errors, 4 projects, forced non-incremental |
| `check-floor.mjs` | **total drift 2256 vs pin 2247 — expected and correct**: 0 failures, 0 bad outcomes, only the 2 pinned OS skips |

- Observed: **2256 unit / 141 headless**. Pin **2247 / 141**. Declared delta **+9 / 0**
  (`floor-delta.json`). 2247 + 9 = 2256 and 141 + 0 = 141, both exact.
- The census's own provenance run was fully green after the base merge: `CcpClient.Tests` 2254
  passed / 2 skipped / 0 failed, `CcpClient.HeadlessTests` 141 / 0 / 0.

### Reds seen along the way, recorded rather than chased

- **`FloorWrapperGuardTests.PacketsAtOrAboveSp073_...` — red at base**, on both SP-120's and
  SP-121's `fileScopeMustNotChange` rows. Proven pre-existing: both PROMPT.md files entered at
  `9af75cdf`, and `git show 4276249e:...SP-120.../PROMPT.md` contains the required literal zero
  times. I first repaired my own packet's row, then reverted that on instruction and merged the
  coordinator's `b5f789de` instead; SP-120's row was never mine to touch.
- **`PointerCapabilityTests.TheDeliveryOracle_CanSeeAClickArriveAtAWindowItBuiltItself_...`** failed
  in exactly one instrumented run and passed in the three after it. Instrumentation changes timing
  and the suite is bounded at 0.20% fenced / 9.5% suite, never zero (`task-board.md:35`). Recorded,
  not re-run for, not touched.
- **`VacuousShapeGuardTests`** correctly caught my own `Census_AndRule_DeclareTheSameClauses` with
  every assertion loop-nested. **Fixed the vacuity** (non-nested `Assert.Equal(3, clauses.Count)`),
  not dispositioned in the ledger.

### Mirror drift worth a board row (coordinator's note, confirmed here)

`validate-wave.mjs` check 4 uses glob-aware `patternCovers` (`validate-wave.mjs:458`) while
`FloorWrapperGuardTests` uses a literal substring match (`FloorWrapperGuardTests.cs:47`, asserted at
`:224`). The validator passed this wave on packets the suite guard reds. That is the same class of
defect this packet is about — two owners of one rule, disagreeing, with a green in between.

## Files

| path | why |
|---|---|
| `client/tools/coverage/shipped-type-rule.json` | the rule as data: clauses, refusals, valve, and 20 fixtures, read by both the tool and the C# guard |
| `client/tools/coverage/census.mjs` | the tool: runs both suites instrumented into `os.tmpdir()`, unions per line, attributes generated entries back, reads the assembly's ECMA-335 TypeDef/MethodDef tables for the invisible denominator, writes the census, deletes the artifacts |
| `client/docs/execution-census.md` | the committed census |
| `client/tests/CcpClient.Tests/ExecutionCensusTests.cs` | 9 pure-logic guards (+9 unit) |
| `spine-tasks/SP-121-zero-execution-census/{plan.md,record.md,floor-delta.json}` | checkpoint, record, delta |

No csproj touched, no `PackageReference` added, no product file changed, no threshold set, no
`.coverage`/`.cobertura.xml` committed.

## What this does NOT prove

Compile-and-headless only. Nothing here verifies interaction, rendering, audio, focus, window
behaviour or animation, and no headless frame discharges a headed gate. The census proves a type was
untouched by these two suites on **one OS**; it never proves a type is verified, never proves a named
row is a defect, and — for the 13 `construction-invisible` rows — does not reliably prove the type was
untouched at all. The three findings above are read from the census by hand and are unfixed.
