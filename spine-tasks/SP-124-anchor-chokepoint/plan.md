# SP-124 — plan (Review Level 3, step 1 checkpoint)

Branch `lane/SP-124-anchor-chokepoint`, worktree
`C:\Code\Conditioning-Control-Panel---CSharp-WPF\.claude\worktrees\agent-ae4549e78574234be`, base
`b76856a7`. **No product or test file changed yet.** This document is the checkpoint artifact.

## 0. What I read, and the one measurement I already took

`ExecutionCensusTests.cs` (all 423 lines), `census.mjs` (config, `classify`,
`readMetadataTypeDefs` 256-385, `render`'s metadata block 436-517, `main`), `shipped-type-rule.json`,
`SystemScheduleClockTests.cs`, `SystemSessionClockTests.cs`, `SystemSoundClockTests.cs`,
`Scheduling/ScheduleClock.cs`, `TestWait.cs`, `TestTimingGuardTests.cs`, `check-floor.mjs`,
`with-slot.mjs`.

Measurement (read-only; scratchpad driver importing the tool's own exported reader, no repo file
touched): on this worktree's Debug build the reader reports **typeDefs 1325 / authored 884 /
no-IL 212**, identical to the committed census rows at `execution-census.md:60,61,64`. So the
document is NOT stale today, and nothing in this packet requires a regeneration.

Two facts that shape the design:

- **295 of the 1325 TypeDef simple names are duplicates** (nested types reuse simple names), so any
  reader-vs-reflection comparison must be a **sorted multiset**, never a set and never a count.
- `floor.json` name-anchors only the **class** `ExecutionCensusTests`, never the individual fact
  names (verified with `git grep -h -o` for the four names, without reading the file). Renaming a
  fact inside the class is therefore safe; adding facts moves only `total`, which is my declared
  delta.

## 1. The chokepoint, stated exactly

`Census_DenominatorIsAnchoredToTheShippedAssembly` computes `authored.Count` and `noMethodBody` **by
live reflection** and compares them to `ScalarRow(census, ...)` — numbers **frozen in a document no
lane may regenerate**. The cross-validation is real and valuable; the coupling to a *stored* number
is what makes every new `public sealed class` a red suite.

## 2. Replacement: same cross-validation, both sides live

Add a non-generating mode to `census.mjs` (in scope: `client/tools/coverage/**`):

```
node client/tools/coverage/census.mjs --metadata-json [--dll <path>]
```

It runs the tool's own `readMetadataTypeDefs` and its own `classify` over the given assembly
(default `PRODUCT_DLL`) and prints one JSON object on stdout — no coverage run, no test host, no
document, ~150 ms:

```json
{ "dll": "...", "typeDefNames": ["<Module>", ...], "authored": [...], "noMethodBody": [...],
  "verdicts": { "<name>": "kept|excluded-C2|excluded-C3|flagged-V1" } }
```

Then in `ExecutionCensusTests.cs`, `Census_DenominatorIsAnchoredToTheShippedAssembly` becomes
**`MetadataReader_AndReflection_SeeTheSameShippedTypes`**: it spawns that mode against
`typeof(HapticGate).Assembly.Location` — the *same bytes* reflection has loaded — and compares three
sorted multisets:

| compared | catches |
|---|---|
| all TypeDef names minus `<Module>` vs `assembly.GetTypes()` names | any row-count, row-width, heap-index or string-heap error in the table walk |
| the C2/C3-surviving subset, both sides | a miscount that hides inside the classified view |
| the no-IL subset vs "every declared method and ctor has a null body" | a MethodDef/`methodList` range error (the `hasIl` walk) |

Fallback if full-set equality does not hold empirically (generic mangling, `<Module>`): drop to the
second and third rows only, which are exactly the two scalars the old anchor pinned (884/212).
I will measure before choosing, and record which.

Adding a `public sealed class` moves **both** sides by one, so it stays green. That is the whole
point and I will demonstrate it with the throwaway class still present.

**Third fact — the classifiers cross-validated on real names, not 20 fixtures.** The same JSON
carries `census.mjs`'s verdict for every one of the 1325 names;
`TheRuleClassifiesTheRealAssembly_IdenticallyInBothImplementations` asserts the existing .NET
`Classify` agrees on all of them. Today the two implementations only ever meet on the rule file's 20
fixtures.

### The sentence the record must answer

**After this change, if `census.mjs`'s ECMA-335 reader starts miscounting — a wrong row width, a
wrong heap-index size, a dropped or duplicated TypeDef row, a `methodList` range off by one — the
reader's own output stops matching what `Assembly.GetTypes()` and `MethodBase.GetMethodBody()`
report for the identical file, and `MetadataReader_AndReflection_SeeTheSameShippedTypes` fails
naming the exact names that differ.** Nothing about that sentence mentions the committed document,
which is precisely why it no longer chokepoints.

Mutation proof (step 3): flip `simple(0x04)`/`simple(0x06)` widths, or `end = i+1 < ... : rows[0x06]`,
in a working-copy edit; show red; restore.

### Rejected alternatives

- Reimplementing the ECMA-335 reader in C#: a third reader would not cross-validate the shipped one.
- Comparing reflection against a freshly regenerated census: a full coverage run (minutes, a test
  host inside a test host) for a number that needs no coverage.
- Letting lanes regenerate the census: forbidden by the packet, and it is the vacuous-green class.
- Deleting or loosening: forbidden, and it would throw away the only reader check in the port.

### The new dependency, named plainly

The fact **spawns `node`**. No test in `client/tests/**` spawns a process today, so this is a new
precedent. Justification: both tier-1 gates (`check-warnings.mjs`, `check-floor.mjs`) and the census
tool are node scripts, so node is already a hard requirement of this tree, not an optional extra.
If node is missing the fact **fails with that message** — it does not skip (`allowedSkips` is not a
quarantine list, and it is closed to me anyway).

## 3. Drift: what notices when the committed census stops describing the tree

I chose **relocation, not deletion** — and the relocated check is itself executed and
mutation-proved inside the suite.

Second new mode, also in `census.mjs`:

```
node client/tools/coverage/census.mjs --check-stale [--dll <path>] [--census <path>]
```

It recomputes the three metadata scalars from the built assembly and diffs them against the census
document's rows, exiting non-zero and naming each drifted row. It cannot check the coverage-derived
rows (universe, the zero list) — those need a real run — and it will say so in its own output.

Two new facts drive it against **synthetic** documents in a temp dir, never the committed one:

- `StaleCheck_RedsWhenTheDocumentsScalarsStopDescribingTheAssembly` — one scalar bumped by one →
  exit non-zero, and the message names that row.
- `StaleCheck_IsQuietWhenTheDocumentDescribesTheAssembly` — scalars taken from the live assembly →
  exit zero.

So the drift detector is not a claim; it is exercised and both of its outcomes are pinned. What
changes is **when** it runs: at the land, by the orchestrator, instead of on every lane. That is the
trade, stated plainly: **no per-lane fact will notice the committed census going stale**, by design,
because that is exactly the chokepoint I was sent to remove.

I also keep the document honest where it costs no lane anything: `Census_IsInternallyConsistent`
gains the two relations the old anchor carried, restated over **stored** scalars only —
`invisible == authored − universe` and `invisible == noMethodBody + noSourceMapped`. Those are
arithmetic inside the document and do not move when a lane adds a type.

### What the orchestrator must run at the land

- **Required: nothing.** This packet adds no shipped type, and the document matches the tree
  (measured above, 1325/884/212).
- **Recommended, once, after the wave's product packets land:**
  `node client/tools/gate/with-slot.mjs -- node client/tools/coverage/census.mjs --check-stale`
  (needs a Debug build). If it reds, the remedy is the full regeneration
  `node client/tools/coverage/census.mjs`, which is the orchestrator's, not a lane's.
- **Discovery I could not act on:** the natural home for `--check-stale` is the land gate, but
  `client/tests/floor/**` is closed to this packet, so I cannot wire it. Reporting it rather than
  widening scope.

## 4. The two landed guards

### 4a. `ACallbackThatThrowsWithNoReporter_IsStillContained` (SystemScheduleClockTests.cs:74-78)

Correct the claim, keep the fact. The comment currently says the fact "is what says the CONTAINMENT
does not depend on the REPORTING" — it does not; its only assertion is that a *second, unrelated*
schedule ran. Replacement comment states the measured limit in the same words SP-123 used in its own
copy, names the sibling fact that does pin the mechanism
(`ACallbackThatThrows_IsContainedAndREPORTED_...`), and says what the fact does pin: the
**null-reporter configuration**, which is a real product configuration.

Also in scope and also now false: `SystemSoundClockTests.cs:96-98` says the sibling's limit "is named
here rather than fixed because that file is outside this packet's scope". Once I fix the sibling that
sentence is stale, so I update it. Leaving a stale cross-reference would be the same defect class.

**Probe request (needs your approval).** The claim "it passes with the containment reverted" is
SP-123's measurement on the *sound* clock. To write it as measured for the *schedule* clock I would
temporarily bare `SystemScheduleClock.Run` to `new Timer(_ => fire(), ...)`, run the two facts, and
revert — a working-copy probe of a CLOSED file, exactly like the sanctioned throwaway class, never
committed. Say the word and I do it; otherwise I will write the comment as "inherited shape,
measured by SP-123 on the identical sibling" and cite that rather than claim my own measurement.

### 4b. `DisposingTheHandleBeforeItIsDue_SuppressesTheCallback` — three files, not two

`SystemSessionClockTests.cs:53`, `SystemScheduleClockTests.cs:122`, **and
`SystemSoundClockTests.cs:154`**, which has the identical ten-minute shape and is in my scope.
Fixing two and leaving the third is the packet's own "do not fix one and leave the sibling".

New shape (same in all three), fully deterministic — no probabilistic margin, no wall-clock wait,
`TestWait` only, and no token `TestTimingGuardTests` forbids (that file is out of my scope, so I add
no pin):

```
D = TimeSpan.FromMilliseconds(1000)          // the due time IS this fact's subject

control = Schedule(D, -> controlTcs)          armed 1st  => earliest deadline
doomed  = Schedule(D, -> doomedTcs)           armed 2nd  => middle deadline
doomed.Dispose()
Assert.False(controlTcs.Task.IsCompleted)     // RACE DETECTOR, see below
barrier = Schedule(D, -> barrierTcs)          armed 3rd  => latest deadline
await TestWait.Until(controlTcs.Task, ...)    // POSITIVE CONTROL
await TestWait.Until(barrierTcs.Task, ...)    // ORDERING BARRIER
Assert.True(controlTcs.Task.IsCompletedSuccessfully, ...)
Assert.False(doomedTcs.Task.IsCompleted, ...) // THE SUPPRESSION
```

Why each line is load-bearing:

- `Timer`'s deadline is fixed at construction, so arming order is deadline order:
  `control < doomed < barrier`.
- **Race detector.** If `control` (armed *before* `doomed`, same delay) has not fired at the moment
  `Dispose` returns, then strictly less than D elapsed since `control` was armed, hence strictly
  less than D since `doomed` was armed — so `doomed` was disposed **before its own due moment**,
  deterministically, not probably. If this trips it means the machine stalled >1 s between two
  adjacent statements; the message will say that is an environment stall, not a product failure.
- **Positive control.** When `barrier` has fired, now ≥ barrier's deadline > doomed's deadline, so
  doomed's moment has passed and the queue demonstrably ran at it. `control` firing proves D is a
  delay this fact actually observes — **which is the property the ten-minute version lacked**. Move
  D back to ten minutes and this leg times out and reds. That is the "assertion cannot fail" defect
  turned into a failing assertion.
- `TaskCompletionSource` instead of the current `bool cancelledFired`: the bool is written on a pool
  thread and read on the test thread with no barrier ordering the two. Not the defect I was sent for,
  but I am rewriting the line anyway and will not re-lay a data race.

Cost ~1 s per fact, three classes, xunit runs classes in parallel. `TestWait.DefaultWindow` is 20 s,
so the window is 20× the delay.

Revert-red-restore for this one: replace `doomed.Dispose()` with a no-op in the working copy; all
three must red on the suppression assertion. Restore.

## 5. The mechanical "assertion cannot fail" shape check: NOT affordable — and I will not half-build one

"This assertion cannot fail" is undecidable in general, and specifically here the three bad facts are
**syntactically indistinguishable** from honest ones: `Assert.False(flag)` where a callback might have
set `flag` is the correct shape for a genuine negative observation. Any grep-shaped approximation
either misses all three (they look right) or fires on every honest negative fact in the suite, and a
guard that fires on honest facts gets suppressed, which is worse than no guard.

What *is* affordable, mechanical, and I am doing it: the **positive control inside the fact** — a leg
that reds when the negative leg's precondition stops being reachable. It is not a scanner; it is per
fact; it costs three lines; and it is checkable by reading. That is my proposal for the general
answer to SP-123's "three instances argues for a guard": the guard is a required *shape* for negative
facts, enforced by review, not a text scanner pretending to decide undecidable questions.

## 6. Probes (all reverted, none committed)

1. **Chokepoint baseline (sanctioned).** Add `public sealed class Sp124ChokepointProbe` under
   `client/src/CcpClient.Desktop/`, build, run `ExecutionCensusTests` → expect exactly
   `Census_DenominatorIsAnchoredToTheShippedAssembly` red (885 vs 884). Record verbatim. Revert.
2. **Replacement green with the same class present** (re-add, run, revert).
3. **Reader made wrong** → replacement red.
4. **Dispose fix**: `Dispose()` no-op'd → red in all three files.
5. **(needs approval, §4a)** schedule clock's containment bared → no-reporter fact still green.

`git status` will be clean of `client/src/**` at every commit; I will verify with
`git status --short -- client/src` before each.

## 7. Floor delta

| | |
|---|---|
| rename `Census_Denominator...` → `MetadataReader_AndReflection_SeeTheSameShippedTypes` | 0 |
| `TheRuleClassifiesTheRealAssembly_IdenticallyInBothImplementations` | +1 |
| `StaleCheck_RedsWhenTheDocumentsScalarsStopDescribingTheAssembly` | +1 |
| `StaleCheck_IsQuietWhenTheDocumentDescribesTheAssembly` | +1 |
| the three dispose facts and the two comments | 0 |

`spine-tasks/SP-124-anchor-chokepoint/floor-delta.json` = `{ unit: +3, headless: 0 }`.
Pin 2309/144 → **expected observed floor 2312 unit / 144 headless**.

## 8. Files I will touch

`client/tests/CcpClient.Tests/ExecutionCensusTests.cs`,
`client/tests/CcpClient.Tests/SystemScheduleClockTests.cs`,
`client/tests/CcpClient.Tests/SystemSessionClockTests.cs`,
`client/tests/CcpClient.Tests/SystemSoundClockTests.cs`,
`client/tools/coverage/census.mjs`, `spine-tasks/SP-124-anchor-chokepoint/**`. All inside File Scope.
Nothing else — in particular no `client/src/**`, no `execution-census.md`, no `floor.json`, no
`TestTimingGuardTests.cs`, no board.

## 9. Open question for you

Only §4a's probe 5. Everything else I can execute as written.
