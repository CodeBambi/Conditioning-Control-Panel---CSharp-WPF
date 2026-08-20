# SP-121 — Which shipped types have ZERO executed lines, answered mechanically

## Mission

**This blind spot has produced a real defect twice, and both times the first honest test found it.**
SP-101's clock **killed the test host**. SP-118's `SystemScheduleClock` was the default clock on every
product path, carried a doc comment asserting fault containment, and was executed by no test and
mutated by no sweep entry — delete its `catch` and the whole suite stayed green. Closing it took one
test file and found **D188: a tick could start a conditioning session with `ShutdownAsync` already
past its drain** (`client/docs/task-board.md:31`).

**The floor counts test results. The warning gate counts warnings. Neither can see a type whose only
coverage is a reference or a property read.**

Your outcome: **a committed, reproducible census naming every shipped type with zero executed lines,
and a tool that regenerates it.**

## THE PREMISE IS MEASURED, NOT ASSUMED — start from these numbers

I ran this on the tree you are branching from. **Do not re-litigate it; verify it and move.**

```
dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo \
  --filter "FullyQualifiedName~HapticGateTests" \
  --collect:"Code Coverage;Format=cobertura" --results-directory <scratch>
```

- **It works on a net10.0 test host and needs NO new package.** `Microsoft.NET.Test.Sdk` 17.10.0
  already brings `Microsoft.CodeCoverage` 17.10.0 transitively — resolved at
  `client/tests/CcpClient.Tests/obj/project.assets.json:598`. **The csproj files are CLOSED to you:
  if you find yourself needing a `PackageReference`, STOP — that is a dependency-admission
  checkpoint and it is not yours.**
- **`;Format=cobertura` is load-bearing.** Without it the output is a binary `.coverage` that needs a
  converter, and *that* would be a dependency admission.
- Ten tests produced **2661 `<class>` entries** and a **15 MB** XML. **A full-suite artifact is
  larger and NONE of it may be committed.**
- With `obj/**` paths (87) and synthetic name shapes (1361) excluded, **1213 candidate shipped types**
  remain, of which that 10-test probe executed **21** — and the 21 are exactly the gate and
  entitlement types those tests drive. **The instrument is sharp. The exclusion rule is the risk.**

## THE CENTRAL TRAP: the exclusion rule is BIGGER than the answer

**1361 synthetic shapes against 1213 shipped types.** More than half of what the report contains is
not a type anybody wrote. So the census is only as honest as its "shipped type" rule — and the
tempting failure is to widen the exclusions until the list looks clean, which is
**`allowedSkips`-as-quarantine wearing a new hat** (`client/tests/floor/floor.json`, `admissionRule`).

**Write the rule down, defend each clause, and make it fail LOUD rather than quiet.** An exclusion
you cannot justify in one sentence is a defect you are hiding. State the count each clause removes.

**And do not drift into a coverage percentage.** The row asks **ZERO**, not a target. A record whose
synthesized `Equals`/`ToString` ran reports `line-rate="0.5"` while no behaviour was driven — so a
percentage would be a worse answer than the one asked for. **This packet sets NO threshold and NO
gate.** A tolerance sized to a number you just observed is exactly the size of the defect it will
next hide (`client/docs/port-workflow.md`, the tolerance rule).

## THE OTHER TRAPS

### 1. PROVE IT BITES — the whole packet turns on this
A census that cannot demonstrate it would catch the thing it exists to catch is decoration. **Both
known instances are now covered**, so history will not prove it for you. Construct the proof:
introduce a type with zero executed lines **as a temporary, uncommitted mutation** (the sweep
pattern), regenerate, and show it named — then show it gone when a fact drives it. **`client/src/**`
is CLOSED to your commits**; a probe you revert is not a product change, and committing one is.

### 2. The instrumented run may go RED, and that is DATA, not your problem to chase
The suite is intermittently red at base: **3 in 60 (5.0%)** before SP-116's fence, bounded at
**0.20% fenced / 9.5% suite** after — never zero (`task-board.md:34`). Instrumentation changes
timing. **A coverage run is DIAGNOSTIC and is never a gate.** Record a red run with its name and
move on; do not re-run to get a prettier census, and do not touch a test to make one pass.

### 3. Nested types, and what "a type" even means
The report emits `Outer.Inner` as its own entry (`HapticGateDecision.Allow` appears beside
`HapticGateDecision`). **Decide and state** whether a nested type is counted separately, and what a
partial class spanning two files counts as. Either answer is defensible; an unstated one is not.

### 4. Both gates, alone
`check-warnings.mjs` then `check-floor.mjs`, never concurrently, and **never in parallel with your
coverage run** — the machine-wide real-desktop lease is what keeps the floor honest.

### 5. Standing rules
Equivalence claims inadmissible until every consumer is enumerated by `grep`. **Anything compiled but
never executed is UNEXECUTED** — the rule this packet exists to make mechanical, and it applies to
your own tool.

## File Scope

| | |
|---|---|
| May change | `client/tools/coverage/**` (new), `client/docs/execution-census.md` (new), `client/tests/CcpClient.Tests/ExecutionCensusTests.cs` (new), and `spine-tasks/SP-121-zero-execution-census/**` |
| Must not change | everything else, and specifically **`client/src/**` (this packet touches NO product file)**, `client/tests/CcpClient.Tests/CcpClient.Tests.csproj`, `client/tests/CcpClient.HeadlessTests/**`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/tools/{verify,gate,wave,citations,publish}/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**If the census needs a product file changed, that is a FINDING and a board row — not a licence.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-121-zero-execution-census/floor-delta.json` |
| fileScopeMustChange | `client/tools/coverage` |
| fileScopeMustNotChange | `client/src/**`, `client/tests/floor/**`, `client/tests/CcpClient.HeadlessTests/**`, `client/tests/CcpClient.Tests/CcpClient.Tests.csproj`, `client/docs/task-board.md`, `client/tools/verify/**`, `client/tools/gate/**`, `client/tools/wave/**`, `client/tools/citations/**`, `client/tools/publish/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-121-zero-execution-census/record.md`, `spine-tasks/SP-121-zero-execution-census/floor-delta.json` |

**Pin: 2247 unit / 141 headless.** `sum-deltas` before deleting any delta file. **Keep every artifact
inside your worktree, and commit NO coverage output.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** your "shipped type" rule clause by clause with the count each removes; your
   nested-type and partial-class decisions; **how you will prove it bites**; and what you will do
   when the instrumented run reds.
2. Build the tool. Deterministic output, stable ordering, no timestamps or machine names in the
   committed census — it must diff cleanly next wave.
3. Regenerate against the **full** suite and commit the census. **Report the real number**, however
   large, and do not editorialise it down.
4. **Prove it bites** with a reverted probe, both directions.
5. Read the census yourself and report **the three findings you consider most likely to be real
   defects**, with a citation each. Do not fix them — file them.
6. Record the rule, the number, and what the census CANNOT see.

## Completion Criteria

- A committed census naming every shipped type with zero executed lines, regenerable by one command.
- The exclusion rule written down, every clause defended, with its removed count.
- Proof that it bites, in both directions.
- No new package, no product file changed, no threshold set, no coverage artifact committed.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Add a `PackageReference` or edit any csproj.
- Set a threshold, a target, or a failing gate on the number.
- Widen an exclusion to make the list shorter.
- Chase a red instrumented run, or re-run for a prettier census.
- Commit a `.coverage` or `.cobertura.xml` artifact.

## Git Commit Convention

Conventional commit, `feat(SP-121): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the measured premise, the exclusion rule and its defence, the bite proof, the real
number and the three findings; the census itself and what it cannot see in
`client/docs/execution-census.md`.
