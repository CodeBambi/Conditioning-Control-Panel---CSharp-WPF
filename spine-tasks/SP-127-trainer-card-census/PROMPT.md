# SP-127 — Trainer Card, censused so the next packet can build it

## Mission

**Three big v6.7 product surfaces remain undecomposed** after SP-125 censused For You Feed and
refused it with an inventory. Trainer Card is the next one to scope, and it differs from FYP in a way
that matters: **its consumer side is already partly in this port.**

`client/docs/task-board.md`'s row records the cross-reference: the v6.7 delta's
`GamificationBridge.cs +157` is the **consumer** side of `RaiseQuizCompleted`, the "Quiz" section
renamed "Graded runs", a source-agnostic `OnQuizCompleted` handler, `top_of_the_class` at the 90%
bar, `honor_roll` over **DISTINCT** categories, `held_back` deliberately fail-streak-only. **SP-058's
verbatim `Trim().ToLowerInvariant()` category normalisation is load-bearing here — case or padding
splitting a category would silently corrupt `honor_roll`.**

Your outcome: **a census ending in a build/refuse verdict with an inventory, sized so the next packet
can be authored against it.**

## THE MODEL, AND THE TRAP IT EXISTS TO AVOID

**SP-125 is the model and it is three days old.** It refused with an inventory: nine files where the
row said three, `Online/` at 42% of the surface and not part of the feature at all, three owner
decisions surfaced, and a separable unit named. **That refusal is worth more than a build would have
been**, because it told the owner exactly what to decide.

**THE CENTRAL TRAP is this port's worst habit, and it has bitten five times: 8 → 13 → 14 → 18 on the
haptic sites, and 3 → 9 on FYP.** Every correction came from **widening the universe**, never from
reading harder — each search was a file list somebody assembled by hand. **Enumerate by DIRECTORY,
recursively, state your universe before you count, and verify the board row's own evidence.** SP-125's
plan forbade hand-assembled lists and then contained one; make the walk structural so the rule cannot
be forgotten.

## THE OTHER TRAPS

### 1. The consumer side may already be here, and that changes the verdict shape
Unlike FYP, part of this subsystem's consumer may already exist in `client/src`. **Find out.** A
surface whose consumer is half-ported is a different object from one that is wholly missing —
`buildable-in-part` may be the honest verdict, and if so, **name the part**.

### 2. `honor_roll` over DISTINCT categories is a correctness trap, not a feature
If category normalisation is not verbatim, two spellings of one category count twice and the award
fires early. **Cite the upstream normalisation exactly and say whether this port already has it.**

### 3. Map every behaviour to a named capability or a named gap
Seven landed capabilities: overlay, input, audio, video, pointer, glyph, haptics. **Name the one that
covers each behaviour, or state precisely what is missing.** A gap is a finding, not a blocker.

### 4. Platform cells are required, not optional
Every row carries `Windows: proven|unproven` / `Linux: proven|unproven` with the manual gate named
when unproven. **`docs/constitution.md` is explicit that a Windows-only test never proves
cross-platform support**, and there is no WSL distro on this machine, so **every Linux claim here is a
named gate**.

### 5. If a behaviour is governed by an owner decision, it gets its own flagged section
Anything touching consent, sensors, networking, persistence or entitlement goes in an
**owner-flagged** section and is **never folded into a size**. SP-125 found three that way.

### 6. Open every line you cite
SP-113 found citations wrong by ~530 lines and in the wrong path. SP-120 found four in its own packet.
SP-125 caught one of its own. **`sed -n` the exact path before writing any `File.cs:line`.**

### 7. `client/src/**` is CLOSED
This packet writes no product code.

## File Scope

| | |
|---|---|
| May change | `client/docs/trainer-card-census.md` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/tests/CcpClient.Tests/TrainerCardCensusTests.cs` (new), and `spine-tasks/SP-127-trainer-card-census/**` |
| Must not change | everything else, and specifically `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/fyp-census.md`, `client/docs/haptic-limb-census.md`, `client/docs/verification-harness.md`, `client/tests/CcpClient.Tests/{ExecutionCensusTests,RackPresentationTests,HapticSiteCensusTests,FypCensusTests}.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-127-trainer-card-census/floor-delta.json` |
| fileScopeMustChange | `client/docs/trainer-card-census.md` |
| fileScopeMustNotChange | `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `client/docs/fyp-census.md`, `client/docs/haptic-limb-census.md`, `client/docs/verification-harness.md`, `client/tests/CcpClient.Tests/ExecutionCensusTests.cs`, `client/tests/CcpClient.Tests/RackPresentationTests.cs`, `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs`, `client/tests/CcpClient.Tests/FypCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-127-trainer-card-census/record.md`, `spine-tasks/SP-127-trainer-card-census/floor-delta.json` |

**Pin: 2332 unit / 144 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Commit `plan.md` BEFORE mapping**: your universe as directories, your method, and how a
   capability verdict is decided so it is not a judgement call.
2. **Verify the board row's own evidence.** If it is wrong, that is your headline.
3. Map every behaviour to a capability or a named gap, citing both sides, with platform cells.
4. **Say what the port already has of the consumer side**, precisely.
5. **Pin `honor_roll`'s category normalisation** — the exact upstream call, and whether this port
   matches it.
6. **Verdict with an inventory**, and a size the next packet can be authored against.
7. **Pin the enumeration against the shipping bytes** — the roots are directories in the TEST, the
   counts re-derive on every run, and a missing reference tree FAILS rather than skips. A guard that
   checks the document against itself is the vacuity `HapticSiteCensusTests.cs:11-25` already names.
8. Divergences from D210 onward. **Escape pipes in table cells** — a bare `|` inside a code span
   silently drops the rest of the row, which is how D197 and D209 lost their decisions.

## Completion Criteria

- Every behaviour mapped with verified citations on both sides and platform cells.
- The board row's evidence checked; the consumer side's current state stated precisely.
- `honor_roll`'s normalisation pinned.
- A verdict with an inventory, not an estimate.
- The enumeration re-derives from the shipping bytes.
- No product code; both gates green; build 0 warnings / 0 errors.

## Do NOT

- Estimate where you can enumerate.
- Inherit the board row's counts.
- Write a guard that checks the document against itself.
- Fold an owner-gated behaviour into a size.

## Git Commit Convention

Conventional commit, `docs(SP-127): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the method, the verified inventory, the capability mapping with platform cells, the
consumer-side finding, the normalisation pin and the verdict; the census in
`client/docs/trainer-card-census.md`; divergences in `client/docs/wpf-surface-reachability.md`.
