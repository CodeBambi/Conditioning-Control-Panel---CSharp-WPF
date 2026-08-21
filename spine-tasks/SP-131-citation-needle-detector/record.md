# SP-131 — record

Branch `lane/SP-131-citation-needle-detector`, base `e3aee3e21`. Review Level 3; the plan checkpoint
is `plan.md`, and the four factual errors the plan gate found in it are corrected **in place**, each
marked `CORRECTED after the plan gate`, rather than left standing.

## What shipped

`client/tools/citations/detect.mjs` gains a second mode, `--needles`. The default mode is unchanged
except that it now also prints the coverage block. Both modes still exit **only 0 or 1**, and no row
count of either kind is consulted anywhere in `main` — which is why neither outcome object escapes
its `try` block.

`client/docs/upstream-citation-inventory.json` gains an optional per-entry `needles: [{id, needle}]`
and one top-level `needleContract`. **There is no line-number field anywhere in the schema.** Both
endpoints of every comparison are derived — one from `git show <endpoint>:<path>`, one from the
working tree — so there is nothing stored that can go stale and nothing to re-record after a repair.

### The needle design

A needle is a **short distinctive substring of the cited line**, matched literally, case-sensitively
and Ordinally. The rules are stated twice, in the two places the next author will look: `detect.mjs`'s
header (`WHAT MAKES A GOOD NEEDLE`) and `needleContract` in the JSON. The 80-character bound is
stated **once**, as a number inside `needleContract`, and the test reads it out of the document rather
than duplicating it — so loosening the prose without the needles conforming reds.

Four row classes, and each states something **certainly true**, which is what keeps the mode out of
cry-wolf territory:

| Class | Fires when |
|---|---|
| `NEEDLE-GONE` | the needle matches **0** lines today |
| `NEEDLE-AMBIGUOUS` | it matches **>1** line today |
| `NEEDLE-MOVED` | exactly one match at both endpoints, at different lines; the row names `:old -> :new (Δ±n)` |
| `CITATION-OUT-OF-RANGE` | a file-qualified span into a **needled** path exceeds the file's line count — arithmetic, not inference |

The default comparison endpoint is `inventory.baseline.merge`, the state the inventory was recorded
against. That makes the mode **silent on this tree by construction** and makes it fire exactly when
upstream moves past the recorded baseline, which is when the wave-64 rot was created.

Every line-level check reads `entry.needles` first and does nothing without it. That is how the T-19
prohibition on blanket line-number validation is a **property of the code**, not a promise: a citation
into an entry with no needle stays file-level and the mode says nothing about it.

## What I took from SP-129, and what I changed

Read: `client/tests/CcpClient.Tests/GoonGameCensusTests.cs` (closed to this packet; read only).

**Taken unchanged** — a needle is a short distinctive substring, matched with a plain Ordinal
`contains` on the raw line (`CitationDrift`/`LineAt`); a needle that resolves nowhere is a finding and
never a silent skip; every disagreement is reported, not the first (`AssertAgrees`); fixtures drive
the mechanism so the real tree never decides whether a mechanism fact passes.

**Changed** — SP-129 stores `key | path | line | needle`; **I dropped the stored line.** SP-129's
table describes a document under its own authorship, where a pinned line is data it owns. The
inventory describes 297 read-only upstream files whose line numbers are not ours to hold, and a
stored line rots exactly as fast as the citation it describes. SP-129's verdict is an assertion that
may red; mine is a **review row**, because `detect.mjs:13-14` forbids the other. And SP-129 compares
against a stored expectation; I compare against a **git endpoint**.

## The class that was designed, measured, and CUT

A per-citation `CITATION-DRIFT` class — a span that contained a needle at the endpoint and contains
none now — was implemented and measured at the inventory's own previous baseline `42286638`. It
emitted **four rows and all four were false**: `GradedRunAwards.cs:95`, `trainer-card-census.md:180`
and `wpf-surface-reachability.md:1467`/`:1491` all cite `IntakeHostService.cs:418-420`, which today
is exactly the `held_back` comment. Three cite it for the quote *"an intake has no fail state to be
held back by"*; the fourth, at `:1467`, is **D223**, which records `:418-420` as a historical
mis-citation about the normalisation — a different reason for the same verdict. None is owed a
repair. A 100% false-positive rate is the shape the header forbids by name, so the class was **cut,
not softened**, and recorded as **D261** so it is not re-invented. The plan gate re-implemented it
from scratch, got exactly four rows at exactly those four sites, opened all four and confirmed the
verdict independently.

The cost is stated rather than hidden: the mode therefore issues a **per-subject** verdict and names
the shift; re-basing a citation is the reviewer's step.

## Coverage, printed every run in both modes

Measured at `474e9a803` from `node client/tools/citations/detect.mjs --needles`:

| | |
|---|---|
| inventory entries | 297 — **1** needled, **296** file-level only |
| needles | 9, all anchoring exactly one line; 0 gone, 0 ambiguous |
| against `db3e842f` | 9 unchanged, 0 moved, 0 absent there, 0 ambiguous there |
| file-qualified citations with a line | **3534** in the port, **49** into the needled path — **23** confirmed, **26** uncovered, **0** out of range |
| bare `:NNN` continuations | **2285** — NOT CHECKED, in either mode |

**Six enumerated blind spots**, each printed or stated, none generalised from a sample:

1. **Bare `:NNN` continuations are never resolved to a file.** The nearest-preceding-citation-token
   heuristic was measured at `e3aee3e21` and rejected: it credits **200** of them to
   `IntakeHostService.cs` alone and mis-binds on the first example examined (`DtrhUserMedia.cs`'s
   `FlashService.GetMediaFiles :2855-2867`, a FlashService citation). That 200 is a hand measurement
   no run re-derives, so it lives in `detect.mjs`'s header **with its SHA** and was deliberately
   removed from the derived coverage line.
2. The **296 un-needled entries** are untouched.
3. A citation **wrong the day it was written** cannot be caught — four of D232's seven were.
4. **No per-citation verdict** is ever issued (the cut class).
5. **Port-internal citations** are outside the inventory's key space (D263).
6. **An entry's `citedBy` is never re-derived.** `detect.mjs:564-574` skips any path already in the
   inventory and no class diffs an existing entry's citer list, so a citation ADDED to a known file is
   invisible to all six default classes. Measured instance: the `IntakeHostService.cs` entry's
   `citedBy` omits `GradedRunAwards.cs`, `trainer-card-census.md` and `wpf-surface-reachability.md`,
   all of which cite it. Recorded, not repaired.

**The coverage figures include these very rows.** Measured before the divergences were written the
same run reported 3526/46/22/24/2255; the difference is D261, D262 and D264, which quote citations
of their own. The row states that explicitly, because quoting the pre-edit numbers would have made it
wrong the moment it landed — the shape SP-129's hole five had, where the table documenting a defect
recreated it.

## The retrospective demonstration — the only evidence the mode does anything

The mode is silent today by construction, so this is what proves it works:

```
node client/tools/citations/detect.mjs --needles --since 42286638
```

reports all nine needles moved: `+3` for the two top-marks subjects (`:47`→`:50`, `:52`→`:55`) and
`+12` for the other seven (`:414`→`:426`, `:417`→`:429`, `:419`→`:431`, `:421`→`:433`, `:422`→`:434`,
`:439`→`:451`, `:441`→`:453`). **That is D232's hand-derived finding — "three merely DRIFTED, by 3
and 12 lines" — reproduced number-for-number, over a window the inventory already records.** SP-128
established the same shift by hand across 22 citations. Recorded as **D262**.

## Tests, and every red demonstration

**`client/tests/CcpClient.Tests/CitationNeedleTests.cs` — 7 facts, on the floor.** Nothing in it
asserts anything about the CONTENT of the review list. The one fact that reads real bytes,
`EveryNeedle_ResolvesAtExactlyOneLine_InTheFrozenBaselineSnapshot`, resolves each needle in
`git show <baseline.merge>:<path>` — **a frozen commit, never the working tree**, so it can never be
reddened by an upstream edit, which would be the cry-wolf shape in a test instead of a report.

**`client/tools/citations/self-test.mjs` — F15-F24, ten fixtured facts** for the mechanism, in the
existing temp-dir-repository style. **They inherit the same named limit as F1-F14: no standing gate
in this repository runs that file** (`self-test.mjs:5-12`). Wiring it into one is a separate board row
with its own acceptance and was explicitly out of this packet's scope, so the mechanism —
moved/gone/ambiguous/out-of-range classification and the coverage arithmetic — is **not on the floor**.
What is on the floor is the contract and the data.

**Every new assertion was watched red at the committed head `ae7168d8f`**, one mutation at a time,
each reverted from git afterwards. **21 of 21 reverts reddened the fact that names them**:

| Revert | Fact that red |
|---|---|
| a third key on a needle record | `EveryNeedleRecord_HasExactlyAnIdAndANeedle` |
| `"line": 434` on a needle record | `NoNeedleRecord_StoresALineNumber` |
| id `Perfect_Guard` (shape) | `EveryNeedle_ObeysTheShapeAndTheBoundTheInventoryItselfStates` |
| the whole source line pasted as the needle (bound) | same |
| a duplicated id within one entry | same |
| a needle matching 0 lines at the baseline | `EveryNeedle_ResolvesAtExactlyOneLine_InTheFrozenBaselineSnapshot` |
| a needle matching 2 lines at the baseline | same |
| `if (rows.length) return 1` on the needle path | `TheNeedleMode_ExitsZero_WithANonEmptyReviewList` |
| the coverage block deleted from the needle report | `TheNeedleMode_PrintsTheCoverageGapEveryRun` |
| `if (rows.length) return 1` on the default path | `TheDefaultMode_StillExitsZero_AndAlsoPrintsTheCoverageGap` |
| the coverage argument dropped from the default report | same |
| unmoved needles emit a row | F15 |
| the signed delta removed from the moved row | F16 |
| a vanished needle no longer classed GONE | F17 |
| a doubled needle no longer classed AMBIGUOUS | F18 |
| un-needled entries given an implicit needle | F19 |
| the out-of-range check disabled | F20 |
| the bare-reference lookbehind removed | F21 |
| absent-at-endpoint counted as unchanged | F22 |
| `--until` accepted silently in needle mode | F23 |
| the coverage block dropped from the default mode | F24 |

**The exit-contract fact is deliberately run at the inventory's recorded PREVIOUS baseline, not the
current one.** The plan gate caught this: at the current baseline the needle mode is silent, so
`return rows.length ? 1 : 0` would leave it at exit 0 and the fact would pass **with the revert
applied** — a guard that would not have guarded. It now reads `baseline.previous.merge` out of the
inventory (never hard-coded), asserts the list is non-empty, and says in its own failure message that
it is only a guard while that holds.

## Floor

Declared in `floor-delta.json`: **unit +7, headless 0**. Pin **2457 / 144**.

Observed: **2464 unit / 144 headless**, 0 failed, 2 allowed skips (the two Linux-precondition ones).
**2464 = 2457 + 7**, exactly the declared delta. `client/tests/floor/floor.json` was never opened.
`check-warnings.mjs` (forced non-incremental): **0 warnings, 0 errors across 4 projects.**

## Spec-versus-code discrepancies, and how each was resolved

1. **Packet Step 4 asks for the two `IntakeHostContext.cs:126-127` citations to be seeded. They
   cannot be.** `IntakeHostContext.cs` is a **port** file; no file of that name exists under
   `ConditioningControlPanel/`, and all 297 inventory entries are keyed there. Widening the key space
   would emit a spurious `CITATION-GONE` on every uncredited port entry plus a `DELTA-MISMATCH` on any
   carrying `changedAtSync`, since numstat is scoped `-- ConditioningControlPanel/`. **Resolution:**
   seed the `IntakeHostService.cs` half in full and record the port-internal half as **D263**. The
   plan gate confirmed this as an authoring error in the packet.
2. **"Seven bare `:NNN`" is five.** Enumerated at `3c38c3973`: five bare, two file-qualified. The
   landed D232 and the repaired comment at `IntakeQuizRun.cs:136-137` both say seven; both are outside
   this packet's file scope. **Resolution:** state the measured number, record it as **D264**, and
   leave the landed corrections to the orchestrator. This matters because the needle mode reaches the
   file-qualified form and never the bare one — the corpus that motivated this packet is
   **five-sevenths unreachable by it**, which is the honest size of the gap.
3. **The plan claimed the existing detector classes a stale `citedBy` as `NEW-CITATION`. It does not
   class it as anything.** Corrected in `plan.md`, added to `detect.mjs`'s do-not-do list, printed in
   the coverage block, and recorded as blind spot 6 in **D260**.
4. **The plan misnamed the failure mode of widening the key space** (UNRESOLVED rather than
   CITATION-GONE/DELTA-MISMATCH). The conclusion was unaffected; the mechanism is corrected in
   `plan.md` and stated correctly in D263.

## Divergences

**D260** coverage and the six blind spots; **D261** the cut class with its 4/4 false-positive
measurement; **D262** the retrospective demonstration; **D263** the port-internal half; **D264** the
five-versus-seven correction. All at or above D260, as required.

## What this does NOT prove

**Nothing was built as a product, run, or rendered.** `client/src/**` was closed to this packet and no
product code was written or changed. There is **no interaction, rendering, audio, focus, window or
animation evidence here of any kind**, no headless frame was produced and no headed capture taken;
`draw-verified` and `presentation-verified` are both untouched. Linux is unproven for everything here,
and so is Windows beyond the fact that these gates ran on it.

**Specifically about this tool:** the needle mode is **silent on this tree**, so no run in this packet
detected a live defect. Its correctness rests on ten fixtured self-test facts that **no standing gate
runs**, plus the retrospective run at `42286638` — which reproduces a finding already made by hand.
It has **never caught a fresh regression**, and it cannot until upstream moves past `db3e842f`.

**Coverage is one entry in 297.** The 296 others are file-level only, and 2285 bare continuations are
unreachable by design — including five of the seven citations that motivated the packet. A green run
of this tool is evidence about **nine subjects in one file**, and nothing else.

**The mode compares a needle's POSITION, not its meaning.** It cannot tell that a citation points at
the wrong thing, only that the thing it was pointed at has moved. A citation wrong from birth stays
invisible.

## Stamping note

`D260` and this file quote `474e9a803` for the commit the figures were measured at. That
placeholder is replaced in the immediately following commit, which changes only hexadecimal text and
therefore cannot move a citation count — verified by re-running the tool after the substitution.
