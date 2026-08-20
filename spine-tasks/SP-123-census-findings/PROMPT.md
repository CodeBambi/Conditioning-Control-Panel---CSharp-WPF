# SP-123 — The three defects the census found, fixed rather than admired

## Mission

SP-121 built an instrument and it immediately found three things. **An instrument whose findings are
never acted on is a more expensive way of being wrong**, so this packet closes them.

**The strongest is the third instance of a shape that has already bitten twice.**
`client/src/CcpClient.Desktop/Audio/AudioSeams.cs:133-137` returns
`new Timer(_ => fire(), null, ms, Timeout.Infinite)` — the callback is **bare**. Both sibling clocks
route through a private `Run(fire)` with `try/catch` **and say why in their own words**: an escaping
exception on a pool thread is unhandled and **.NET terminates the process**
(`client/src/CcpClient.Desktop/Scheduling/ScheduleClock.cs:74-92`,
`client/src/CcpClient.Desktop/Session/SessionClock.cs:46-70`). It is the default on **three** product
paths — `Companion/BarkPipeline.cs:116`, `Features/Dtrh/DtrhHostWindow.axaml.cs:217`,
`Features/Dtrh/DtrhNativeEffects.cs:53` — and grep confirms those are the only constructions.

**Precedent, and it is why this is P0:** SP-101's twin **killed the test host**. SP-118's
`SystemScheduleClock` hid D188 — a tick that could start a conditioning session with `ShutdownAsync`
already past its drain. Both were found when somebody happened to write the first test. This one was
found mechanically, which is the entire return on the instrument.

Your outcome: **all three closed, each pinned by a fact that fails without the fix.**

## THE THREE

### 1. `SystemSoundClock` has no fault guard (P0)
Give it the same containment its siblings have. **Copy the shape, not the text** — and if you find the
siblings' guards differ from each other in a way that matters, that is a finding.

### 2. The only production `IBarkAudioResolver` is driven by no test (P1)
`client/src/CcpClient.Desktop/Companion/BarkPipeline.cs:69-78` documents *"missing file -> null, never
throws"* over `Path.Combine(root, audioFileName)`, **which throws on a null filename**. Exactly two
references tree-wide: the declaration and one wiring at `Features/Dtrh/DtrhHostWindow.axaml.cs:239`.
**Every bark test substitutes `RecordingResolver`** (`BarkPipelineTests.cs:593`), so the shipped
implementation has never run. **Decide which is true — the contract or the code — and make them
agree.** A doc comment that lies is worse than no comment.

### 3. The DTRH host composition block is a CLUSTER, not three rows (P1)
`client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:213-245` is the sole construction
site of four zero-executed seams, and the 833-line window is zero-executed too — the census's single
largest dead surface. **It carries `same-os-code(windows)`, which means the machine does NOT excuse
it**: the predicate selects the OS the census ran on, so those lines would be *more* dead elsewhere.
**One fact that drives the composition block is expected to move several rows at once.**

## THE CENTRAL TRAP: the first honest test on a never-executed type finds a defect

That is not a hope, it is this port's record — **twice, and both times the test was written to close a
coverage gap rather than to hunt a bug.** So write the test that DRIVES the thing, not the test that
mentions it. **If driving one of these finds a second defect, that is the expected outcome and it is
a finding, not scope creep** — record it and fix it if it is in scope, file it if it is not.

## THE OTHER TRAPS

### 1. Do not close a row by making the census stop reporting it
The census is regenerated from a real instrumented run. **A row leaves the zero list because a fact
now drives the type, never because a rule, a marker or an exclusion moved.** `client/tools/coverage/**`
and `client/docs/execution-census.md` are **CLOSED to you** — the other lane does not own them either,
and a census regeneration at the land is the orchestrator's.

### 2. `construction-invisible` means the census cannot see it, not that it is fine
13 of the 42 rows carry it: a member-less record reads ZERO even when a passing test constructs it.
**None of your three carry a weakening marker** — that is why they were chosen. Do not reach for the
markers as an excuse if a fix is harder than expected.

### 3. Prove the fix bites
For each of the three, the fact must **fail without the fix**. Show it: revert the product change,
show red, restore. That is the sweep discipline and it is not optional on a defect packet.

### 4. Standing rules
No wall-clock waits — `TestWait` only. Equivalence claims inadmissible until every consumer is
enumerated by `grep`. A tolerance is the size of the defect it hides. Both gates alone.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Audio/**`, `client/src/CcpClient.Desktop/Companion/**`, `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/tests/CcpClient.Tests/**` (new and existing bark/audio/DTRH facts), `client/tests/CcpClient.HeadlessTests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-123-census-findings/**` |
| Must not change | everything else, and specifically `client/tools/**`, `client/docs/execution-census.md`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/verification-harness.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-123-census-findings/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Audio` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/tools/**`, `client/docs/execution-census.md`, `client/docs/task-board.md`, `client/docs/verification-harness.md`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/src/CcpClient.Desktop/Views/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-123-census-findings/record.md`, `spine-tasks/SP-123-census-findings/floor-delta.json` |

**Pin: 2270 unit / 141 headless.** `sum-deltas` before deleting any delta file. **Keep every artifact
inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** the containment shape you will copy and whether the two siblings agree; your
   ruling on the bark contract-versus-code question; and how you will drive the DTRH composition
   block without changing what it constructs.
2. Fix #1 and pin it. **Revert-red-restore, shown.**
3. Fix #2 — contract or code, decided and justified — and pin it.
4. Drive #3. Report how many census rows it is expected to move, and say plainly that you did not
   regenerate the census to check.
5. Sweep every predicate you touched; discharge or withdraw every equivalence claim.
6. Divergences; **and report any second defect the first honest test uncovered.**

## Completion Criteria

- All three closed, each with a fact that fails without its fix, demonstrated.
- No census file, rule or marker touched.
- Any second defect found while driving them is recorded.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Close a finding by changing the census, its rule, or a marker.
- Write a test that references a type without driving it.
- Touch `Views/**` — the other lane in this wave owns it.
- Regenerate the census.

## Git Commit Convention

Conventional commit, `fix(SP-123): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the containment comparison, the bark ruling and its reasoning, the DTRH driving
approach, the revert-red-restore evidence for each fix, and any second defect found.
