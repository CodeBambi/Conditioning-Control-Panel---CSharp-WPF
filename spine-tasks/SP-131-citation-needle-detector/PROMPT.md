# SP-131 — Give the citation-drift detector a line-level mode, using the mechanism SP-129 proved

## Mission

**The port's parity claims are `File.cs:line` citations into the read-only WPF tree, and nothing
checks that the line still says what the citation claims.**

`client/tools/citations/detect.mjs:91-93` states the limit in its own words, under
**WHAT THIS FILE DELIBERATELY DOES NOT DO**:

> It does not validate citation LINE NUMBERS. The `:NNN` suffix is matched and then discarded;
> there is no line-number field in the row shape.

So it answers a **file-level** question — *did an upstream file the port cites change?* — while the
rot is a **line-level** one: *does the cited line still say what the citation claims?* A file can be
untouched in a sync window and still have every citation into it silently shifted by an edit ten
lines above.

**Your outcome: a second mode that re-greps a stored needle and reports line-level drift, without
turning the tool into a red test.**

## THE MECHANISM ALREADY EXISTS AND WAS PROVEN LAST WAVE

SP-129 built exactly this and it works. `client/tests/CcpClient.Tests/GoonGameCensusTests.cs` reads a
`key | path | line | needle` table and asserts **the needle is ON that line** of the shipping file.
It caught a stale number **mechanically, before any reviewer did** — `census says 15, the bytes say
18` — and a reviewer reproduced it independently.

**Read that implementation before you design yours.** You are generalising a proven mechanism, not
inventing one.

## WHY THIS IS URGENT, with the evidence from wave 64

Wave 64 produced **four fresh instances in landed port code**:

- **Seven stale citations in one class** — `client/src/CcpClient.Desktop/Features/Intake/IntakeQuizRun.cs`'s
  `IntakeGraded`. Upstream `f7b4c317c` moved the file under them, and `:418-420` came to point at a
  comment saying **the opposite** of what the citation claimed. **That then propagated into a lane's
  plan**, which cited the wrong line because it trusted the landed comment.
- Two `IntakeHostContext.cs:126-127` citations whose code sits at `:172-175`.

SP-128 repaired **22**. **The existing detector would have flagged the file and could not have named
a single one of the citations.**

## THE DESIGN CONSTRAINTS, and they are the whole difficulty

### 1. It stays a REVIEW LIST, never a red test
`detect.mjs:13-14` says it by name: *"IT IS A REVIEW LIST, NOT A RED TEST. A changed upstream file is
not automatically a defect, and a guard that cries wolf gets disabled."* **A moved needle is a review
row, not a failure.** Preserve the exit contract exactly; read it at `detect.mjs:78-80` before you
touch anything.

### 2. Add a `needle`, do NOT add a `line`
The row shape's own comment says there is no line-number field, deliberately. **Adding an optional
per-citation `needle` to `client/docs/upstream-citation-inventory.json` keeps that true** while making
the check possible: you re-grep the needle and report where it moved to. A stored line number would
rot exactly as fast as the citation.

### 3. Do NOT widen it into blanket line-number validation
The T-19 board row forbids that **without evidence it can be specified** — and the needle technique
**is** that evidence, for citations that carry one. Citations with no needle stay file-level. Say so.

### 4. Coverage is opt-in and the gap must be stated
Not every one of the 297 cited files will get a needle in this packet, and that is fine. **What is not
fine is a report that reads as complete.** Print how many citations carry a needle and how many do
not, every run. An unstated coverage gap is how a review list becomes a false reassurance.

### 5. A needle is not a line — pick needles that survive reformatting
A needle that is a whole line will rot on an indent change. SP-129's needles are **short distinctive
substrings**. Say in the tool what makes a good needle, because whoever adds the next one will copy
what they see.

## THE TRAPS

### 1. Twelve guards this session were descriptions outrunning their mechanisms
Six holes were drilled into SP-129's guard across four review rounds — **positional, asymmetric,
lexical, overbroad, self-referential, and incomplete-by-sampling.** The last one is the one to carry:
its author claimed coverage after probing **one** case, and three more were leaking. **Enumerate what
your mode covers; never generalise from one sample.** And demonstrate every claim **at the committed
head**, with the SHA — a demonstration against an intermediate tree is worth nothing.

### 2. The self-test is run by no standing gate, and that is NOT yours to fix
`client/tools/citations/self-test.mjs` has 15 cases and nothing runs them; `floor.json`'s own
`lastMovedBy` records it. **Extend it for your new mode** so the cases exist, but **do not** wire it
into a gate in this packet — that is a separate board row with its own acceptance. Say in `record.md`
that your cases inherit the same named limit.

### 3. Open every line you cite
This is the citation packet. A wrong citation here is self-refuting.

### 4. Divergence ids: **D260 onward**
The sibling packet holds **D250-D259**. Stay above D259.

### 5. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Escape pipes in table cells.

## File Scope

| | |
|---|---|
| May change | `client/tools/citations/**`, `client/docs/upstream-citation-inventory.json`, `client/tests/CcpClient.Tests/CitationNeedleTests.cs` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D260 onward), and `spine-tasks/SP-131-citation-needle-detector/**` |
| Must not change | everything else, and specifically `client/src/**`, `client/tools/coverage/**`, `client/tools/verify/**`, `client/tools/gate/**`, `client/tools/wave/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/goon-game-census.md`, `client/tests/CcpClient.Tests/GoonGameCensusTests.cs`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-131-citation-needle-detector/floor-delta.json` |
| fileScopeMustChange | `client/tools/citations` |
| fileScopeMustNotChange | `client/src/**`, `client/tools/coverage/**`, `client/tools/verify/**`, `client/tools/gate/**`, `client/tools/wave/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/goon-game-census.md`, `client/tests/CcpClient.Tests/GoonGameCensusTests.cs`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-131-citation-needle-detector/record.md`, `spine-tasks/SP-131-citation-needle-detector/plan.md`, `spine-tasks/SP-131-citation-needle-detector/floor-delta.json` |

**Pin: 2457 unit / 144 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** the needle field's shape; how the second mode reports; how
   the exit contract stays unchanged; what makes a good needle; and **which edit each new assertion
   must red on**.
2. Read SP-129's implementation and say what you took and what you changed, and why.
3. Add the optional `needle` field and the second mode.
4. **Seed needles for the citations wave 64 proved rot** — the `IntakeGraded` block and the two
   `IntakeHostContext` ones are the known-good regression corpus, since their correct lines are
   recorded in D232.
5. Print coverage every run: how many citations carry a needle, how many do not.
6. Extend `self-test.mjs` for the new mode, and record that it inherits the no-standing-gate limit.
7. Divergences **D260 onward**.

## Completion Criteria

- A second mode re-greps stored needles and reports line-level drift as a **review row**.
- The exit contract is unchanged and the tool still cannot fail a build.
- Coverage is printed every run; the gap is stated, never implied away.
- The wave-64 rot corpus is seeded and demonstrably detected.
- Every new assertion watched red at the committed head, with the SHA.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Turn the detector into a red test.
- Store a line number where a needle belongs.
- Widen into blanket line-number validation.
- Wire `self-test.mjs` into a standing gate.
- Claim coverage you have not enumerated.
- Use a divergence id at or below D259.

## Git Commit Convention

Conventional commit, `feat(SP-131): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the needle design, what you took from SP-129, the coverage numbers, the red
demonstrations with the head SHA, and the inherited self-test limit; divergences in
`client/docs/wpf-surface-reachability.md`.
