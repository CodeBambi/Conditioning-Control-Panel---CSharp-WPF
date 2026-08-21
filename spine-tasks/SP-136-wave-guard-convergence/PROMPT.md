# SP-136 — Two guards that were supposed to be one implementation: make them agree BY CONSTRUCTION

## Mission

**A packet can print `WAVE OK` at the pre-launch gate and RED the suite from the authoring commit
onward. It happened to BOTH packets of wave 60 and cost a lane a blocked gate.**

`client/tools/wave/validate-wave.mjs` carries a MIRROR note in its own header (`:42`) saying its
packet enumeration and contract-row checks are ports of
`client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs`, and *"do not let these two drift"*.
**They have drifted, on check 4.** I verified all four citations against the merged head before
writing this packet:

| where | what it actually asks |
|---|---|
| `validate-wave.mjs:458` | `if (!mustNotChange.some((p) => patternCovers(p, FLOOR_PIN_PATH)))` — **glob-aware coverage** |
| `FloorWrapperGuardTests.cs:224` | `row.Groups[1].Value.Replace('\\', '/').Contains(SharedFloorPin, StringComparison.OrdinalIgnoreCase)` — **literal substring** |
| `FloorWrapperGuardTests.cs:47` | `SharedFloorPin = "client/tests/floor/floor.json"` |
| `validate-wave.mjs:42` | the MIRROR note itself |

So `client/tests/floor/**` **satisfies the validator and fails the test.** The glob demonstrably
covers the pin, which is the property the rule actually cares about, and the test still reds.

Your outcome: **the two checks agree by construction — one implementation consulted by both, or a
fixture both must satisfy — so that this class cannot recur.**

## THE NOTE IS THE EVIDENCE THAT NOTES DO NOT WORK

`validate-wave.mjs:42` already says *"do not let these two drift"*. **It is documentation, and
documentation does not enforce.** That sentence was present the whole time they drifted. **Do not
close this by improving the note**, and do not close it by adding a second note to the C# side.

## THE CENTRAL TRAP: THE BLAST RADIUS IS EVERY PACKET EVER WRITTEN

`FloorWrapperGuardTests` parses **every** `spine-tasks/*/PROMPT.md` in the repository and fails
closed. There are ~136 packet folders. **Whichever semantics you choose, you are re-judging all of
them at once.**

- Make the C# test glob-aware -> packets that used the literal still pass; packets that used a glob
  start passing. Strictly widening, so the blast radius is probably zero. **Measure it, do not
  assume it.**
- Make the validator literal-only -> **every existing packet that declared a covering glob would
  newly fail.** That may be none, or it may be many.

**Count both directions before choosing, and put the counts in your plan.** A choice made without
that measurement is the same class of error as the drift itself.

## DECIDE THE SEMANTICS DELIBERATELY, AND SAY WHY

Row 32's acceptance names the trade: **glob-covers is the more honest test of the underlying rule**
(the rule cares that the lane cannot edit the pin, and a covering glob achieves that), while **the
literal is the easier thing to grep for**. Pick one, implement it once, and justify the pick against
what the rule is FOR — not against which was easier to keep.

**DO NOT fix this by editing packets to please the stricter of two rules that were supposed to be
one.** That is explicitly ruled out by the row and it inverts cause and effect.

## THE OTHER TRAPS

### 1. "By construction" has a testable meaning
A shared constant is not shared logic. If the two remain two implementations that merely agree
today, you have rebuilt the defect with a fresh coat. **State precisely what makes a future drift
impossible rather than merely unlikely**, and pin it. If a single implementation genuinely cannot be
shared across a `.mjs` tool and a C# test, then the fixture both must satisfy is the answer — build
it, and make it fail when only one side is updated.

### 2. Pin the case that started this
A covering **GLOB** and a **literal** must be treated identically by whichever rule survives. That
case is the row's stated acceptance. It must be a fact, not a comment.

### 3. Your own packet must not trip the bug it fixes
This packet declares the literal `client/tests/floor/floor.json` in its File Scope precisely so it
passes both rules today. **Do not "simplify" it to a glob mid-flight.**

### 4. This is the gate that launches every wave
A defect here blocks work that is not yours, in both directions: a validator that wrongly rejects
stops a wave being authored, and one that wrongly accepts reds the base. **Run the full floor before
and after and compare FAILURE SETS, not just counts**, and run `validate-wave.mjs` against several
existing packet folders to prove you did not break authoring.

### 5. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Every new guard watched red **at the committed
head**, with the SHA.

### 6. Divergence ids: **D296-D305**

## File Scope

| | |
|---|---|
| May change | `client/tools/wave/validate-wave.mjs`, `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs`, `client/tests/CcpClient.Tests/WaveGuardConvergenceTests.cs` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D296-D305), and `spine-tasks/SP-136-wave-guard-convergence/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/tests/floor/check-floor.mjs`, `client/tests/floor/sum-deltas.mjs`, `client/tools/citations/**`, `client/src/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**`, and **the `PROMPT.md` of any other packet** |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-136-wave-guard-convergence/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/WaveGuardConvergenceTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/tests/floor/check-floor.mjs`, `client/tools/citations/**`, `client/src/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-136-wave-guard-convergence/record.md`, `spine-tasks/SP-136-wave-guard-convergence/plan.md`, `spine-tasks/SP-136-wave-guard-convergence/floor-delta.json` |

**Pin: 2599 unit / 152 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** the two blast-radius counts (how many existing packets change
   verdict under each semantics), which semantics you pick and why **against what the rule is for**,
   what makes future drift impossible rather than unlikely, and which edit each new guard reds on.
2. Implement the convergence. One implementation consulted twice, or a fixture both must satisfy.
3. **Pin the glob-versus-literal equivalence** as a fact.
4. **Prove a one-sided update FAILS.** That is the whole claim of "by construction".
5. Run the full floor before and after; compare failure sets. Run `validate-wave.mjs` on existing
   packets to prove authoring still works.
6. Divergences **D296-D305**.

## Completion Criteria

- The two checks agree by construction, with the mechanism named precisely.
- A covering glob and a literal are treated identically, pinned by a fact.
- A one-sided change is demonstrated to fail.
- Blast radius measured in both directions and reported as counts.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Close this by editing the MIRROR note, or by adding another note.
- Edit any other packet's `PROMPT.md` to satisfy a rule.
- Loosen either check to make them meet.
- Ship a shared CONSTANT and call it shared LOGIC.
- Use a divergence id outside D296-D305.

## Git Commit Convention

Conventional commit, `feat(SP-136): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the chosen semantics and its justification, the two blast-radius counts, the exact
mechanism that prevents future drift, the one-sided-update demonstration, and the before/after
failure sets with the head SHA.
