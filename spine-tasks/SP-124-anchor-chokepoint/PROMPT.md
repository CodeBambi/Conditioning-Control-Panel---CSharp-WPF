# SP-124 — The anchor that blocks every future product packet, and two guards that lie in landed files

## Mission

**This packet is on the critical path for all product work.** `ExecutionCensusTests.Census_DenominatorIsAnchoredToTheShippedAssembly`
(`client/tests/CcpClient.Tests/ExecutionCensusTests.cs:254`) computes the authored-type count **by
reflection over the live assembly** and asserts it equals a **scalar read out of the committed
`client/docs/execution-census.md`**.

**So adding one ordinary `public sealed class` reds the suite** — reflection says 885, the document
says 884 — **and the only remedy, regenerating the census, is a document closed to every lane.**
Reproduced independently three times: by SP-123's lane, by its code reviewer dropping a bare class
into `Audio/`, and by its final reviewer. SP-123 escaped only because hosting its lift on an existing
type as a `partial` adds no TypeDef — sound design, and also luck.

**The next packet that needs a genuinely new shipped type has no such escape, and the next four
product surfaces on the board all need many.**

Your outcome: **the chokepoint removed WITHOUT losing what the anchor proves, plus the two landed
guards that lie corrected.**

## THE CENTRAL TRAP: the anchor is VALUABLE and the obvious fix throws that away

**Do not delete it and do not loosen it.** What it does is genuinely good and rare here: it
cross-validates `census.mjs`'s **hand-rolled ECMA-335 metadata reader** against **ordinary
reflection** — two independent mechanisms, agreeing at 884/212. That is the only thing in this port
that could catch the census's reader being wrong, and a final reviewer confirmed it by a **third**
mechanism.

**The defect is not the cross-validation. It is binding a LIVE measurement to a STORED number that
the packet tripping it may not update.** The board's acceptance says it: the two counting mechanisms
validated **against each other at runtime**, not against a committed scalar.

**And do not solve it by opening the census to lanes.** That file is a shared chokepoint for the same
reason `floor.json` is: two lanes regenerating it in one wave collide, and "set it to whatever the
merged tree observes" is the vacuous-green class the pin exists to catch.

**Whatever you build, answer this in the record: after your change, what still fails if
`census.mjs`'s metadata reader starts miscounting?** If the answer is "nothing", you have deleted the
anchor while appearing to fix it.

## THE SECOND JOB: two guards in ALREADY-LANDED files assert less than their names claim

Both were measured, not argued, and SP-123 corrected its own copies while its siblings stayed wrong.

1. **`client/tests/CcpClient.Tests/SystemScheduleClockTests.cs:74-78`** — `ACallbackThatThrowsWithNoReporter_IsStillContained`
   **passes with the containment reverted.** Its only assertion is that a *second, unrelated* timer
   fired, which is true either way. Its comment makes the same false claim SP-123 struck from its own
   copy. **Correct the comment and the claim; do not delete the fact and do not inflate it** — it
   exercises a real product configuration, and the mechanism is pinned by its sibling fact.
2. **`SystemSessionClockTests.cs:53` and `SystemScheduleClockTests.cs:122`** —
   `DisposingTheHandleBeforeItIsDue_SuppressesTheCallback` asserts a condition that is **trivially
   true**: the doomed schedule is ten minutes out, so the flag is false whether or not `Dispose`
   suppresses anything. **This one CAN be made to bite** — a schedule due within the fact's own wait
   window would actually observe suppression. Fix it properly in both files, or record precisely why
   it cannot be.

**SP-123's own conclusion is the brief: three instances in one packet argues for a GUARD, not for
three more careful readers.** If a mechanical shape check for "assertion cannot fail" is affordable
inside this packet, propose it in the plan. If it is not, say so — do not half-build one.

## THE OTHER TRAPS

### 1. Prove every fix bites, including the anchor's replacement
Revert-red-restore for each. For the anchor, that means: **make the metadata reader wrong on purpose
and show your replacement reddens.** A fix to a guard that is not itself mutation-proved is the exact
class this packet exists to close.

### 2. Do not let the census document drift silently instead
If your design stops pinning the document's scalars, then **something else must notice when the
committed census stops describing the tree** — or you have traded a chokepoint for a blind spot, and
`client/docs/task-board.md` already carries a row saying the census cannot see its own drift. Say in
the record which of the two you chose and why.

### 3. `client/docs/execution-census.md` is CLOSED to you
You may not regenerate it. If your change requires a regeneration, that is the orchestrator's at the
land — state exactly what it must run and what should change.

### 4. Standing rules
No wall-clock waits — `TestWait` only. Equivalence claims inadmissible until every consumer is
enumerated by `grep`. A tolerance is the size of the defect it hides. Both gates alone.

## File Scope

| | |
|---|---|
| May change | `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/SystemScheduleClockTests.cs`, `client/tests/CcpClient.Tests/SystemSessionClockTests.cs`, `client/tests/CcpClient.Tests/SystemSoundClockTests.cs`, `client/tools/coverage/**`, and `spine-tasks/SP-124-anchor-chokepoint/**` |
| Must not change | everything else, and specifically `client/src/**`, `client/docs/execution-census.md`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/tools/verify/**`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**`client/src/**` is CLOSED. This packet changes no product code.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-124-anchor-chokepoint/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/ExecutionCensusTests.cs` |
| fileScopeMustNotChange | `client/src/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/execution-census.md`, `client/docs/task-board.md`, `client/tools/verify/**`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-124-anchor-chokepoint/record.md`, `spine-tasks/SP-124-anchor-chokepoint/floor-delta.json` |

**Pin: 2309 unit / 144 headless.** `sum-deltas` before deleting any delta file. **Keep every artifact
inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** your replacement for the anchor and **what still fails if the metadata reader
   miscounts**; what notices if the committed census stops describing the tree; your fix for the
   dispose shape in both landed files; and whether a mechanical "assertion cannot fail" check is
   affordable here.
2. **PROVE THE CHOKEPOINT FIRST.** Add a throwaway `public sealed class` under `client/src`, show the
   anchor reds today, revert it. That is your baseline and it must be in the record.
3. Build the replacement. **Show it green with that same throwaway class present** — that is the
   whole point — and **red when the reader is made wrong.**
4. Correct the two landed guards. Revert-red-restore the dispose fix.
5. Sweep every predicate you touched.
6. Record what the anchor still proves, in one sentence a reader can check.

## Completion Criteria

- A packet adding one ordinary shipped type passes the floor, demonstrated with a real throwaway class.
- The reader-versus-reflection cross-validation still bites, demonstrated by making the reader wrong.
- Something still notices if the committed census stops describing the tree, or the record says
  plainly that nothing does and why that is acceptable.
- Both landed clock guards corrected or their impossibility recorded.
- No product code; both gates green; build 0 warnings / 0 errors.

## Do NOT

- Delete or loosen the cross-validation.
- Open `client/docs/execution-census.md` to lanes, or regenerate it.
- Fix the dispose shape in one file and leave the sibling.
- Ship a guard you have not mutation-proved.

## Git Commit Convention

Conventional commit, `fix(SP-124): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the chokepoint baseline, the replacement's design and what it still proves, the
drift answer, the revert-red-restore evidence per fix, and the shape-guard decision.
