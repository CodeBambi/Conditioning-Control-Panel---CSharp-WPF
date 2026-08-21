# SP-133 — Put the citation self-test on the floor

## Mission

SP-131 landed a line-level citation-drift mode. **Its final review named the largest residual in the
packet, and this is it:**

> The seven facts on the floor pin the CONTRACT (both modes exit 0, both print coverage) and the DATA
> (schema shape, needle bound, one-line resolution at a frozen SHA). **They do not pin the
> CLASSIFICATION.** Moved / gone / ambiguous / out-of-range, and the coverage arithmetic, live in ten
> fixtured facts in `client/tools/citations/self-test.mjs` (F15-F24) **that nothing runs.**

So **the half of the tool that decides WHAT DRIFTED is unguarded**, and its green holds only as of the
last time a human ran it by hand. The same limit already applied to F1-F14 and is recorded in
`client/tests/floor/floor.json`'s own `lastMovedBy`.

**Your outcome: `self-test.mjs`'s facts run on every floor run, and a regression in the classification
logic reddens the suite.**

SP-131 was **explicitly forbidden** from doing this so the fix would get its own review. That review
is yours.

## THE SHAPE, AND IT IS SMALLER THAN IT LOOKS

The precedent is already in the repository. `client/tests/CcpClient.Tests/ExecutionCensusTests.cs:625-684`
spawns `node`, waits with `TestWait.Until`, kills the tree on timeout, and **fails rather than skips**
when the tool is absent. **SP-131's own facts use that pattern**, so you are copying a shape this
repo has already reviewed twice.

## THE TRAPS

### 1. A bridge that cannot fail is worse than no bridge
The whole point is that a broken classifier reddens the suite. **Prove it: break one of F15-F24's
subjects in `detect.mjs`, watch the floor go red, revert.** A bridge that passes because it never
really ran the fixtures is this project's signature defect, found fourteen times in two waves — and it
would be especially galling here, in the packet that exists to stop exactly that.

### 2. Fail, never skip
If `node` is missing, if the script is absent, if it exits non-zero for a reason you did not expect —
**fail with the reason in the message.** `VacuousShapeDetector.Scan` "refuses to skip" and
`TrainerCardCensusTests`' `RequireReference` is the shape. **A skip here is a silent hole in the only
guard the classifier will have.**

### 3. The self-test's own count is a number, and numbers rot
It reports 25 facts today (F1-F24 plus one). **Do not hard-code 25 anywhere without deriving it**, or
you have built the thing this wave has spent two packets fixing. If you pin a count, pin it from the
script's own output and say where it came from.

### 4. Do not widen the tool
`detect.mjs`'s corpus and token class are a **separate board row**, sized as a specification task
because a `.mjs` token class must tell a citation from a pasted stack trace. **Not yours.** If your
bridge needs the tool changed to be testable, that is a finding you report, not a widening you make.

### 5. Standing rules
No wall-clock waits — `TestWait` only, and the spawn pattern above. No TODOs. Every new guard watched
red **at the committed head**, with the SHA.

### 6. Divergence ids: **D275 onward**

## File Scope

| | |
|---|---|
| May change | `client/tools/citations/self-test.mjs`, `client/tests/CcpClient.Tests/CitationSelfTestGateTests.cs` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D275 onward), and `spine-tasks/SP-133-citation-selftest-gate/**` |
| Must not change | everything else, and specifically `client/tools/citations/detect.mjs`, `client/docs/upstream-citation-inventory.json`, `client/tests/CcpClient.Tests/CitationNeedleTests.cs`, `client/tools/verify/**`, `client/tools/coverage/**`, `client/src/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-133-citation-selftest-gate/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/CitationSelfTestGateTests.cs` |
| fileScopeMustNotChange | `client/tools/citations/detect.mjs`, `client/docs/upstream-citation-inventory.json`, `client/tests/CcpClient.Tests/CitationNeedleTests.cs`, `client/tools/verify/**`, `client/tools/coverage/**`, `client/src/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-133-citation-selftest-gate/record.md`, `spine-tasks/SP-133-citation-selftest-gate/plan.md`, `spine-tasks/SP-133-citation-selftest-gate/floor-delta.json` |

**Pin: 2547 unit / 152 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** the bridge's shape; how it fails rather than skips; how the
   fact count is derived rather than typed; and **which edit in `detect.mjs` your bridge must red on**.
2. Build the bridge on the `ExecutionCensusTests.cs:625-684` pattern.
3. **Break a classifier subject, watch the floor red, revert** — at the committed head, with the SHA.
4. Prove the absent-tool path fails rather than skips.
5. Divergences **D275 onward**.

## Completion Criteria

- `self-test.mjs`'s facts run from the unit suite on every floor run.
- A regression in the classification logic reddens the suite, demonstrated.
- The absent-tool path fails with a reason, never skips.
- Any count is derived from the script's own output.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Ship a bridge you have not watched red.
- Skip on a missing tool.
- Hard-code the fact count.
- Widen `detect.mjs`'s corpus or token class.
- Use a divergence id at or below D274.

## Git Commit Convention

Conventional commit, `feat(SP-133): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the bridge's shape, the red demonstration with the head SHA, the absent-tool
behaviour, and where the fact count came from.
