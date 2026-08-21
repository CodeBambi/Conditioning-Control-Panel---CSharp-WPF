# SP-131 — plan (Review Level 3, checkpoint before any product edit)

Base `e3aee3e21`, branch `lane/SP-131-citation-needle-detector`, worktree
`.claude/worktrees/agent-a1f20d56c7425fa55`. Nothing outside this file has been edited.

Everything numeric below was **measured on this checkout before it was written**, with throwaway
scripts under the session scratchpad. No number here is an estimate.

---

## 0. What I read

| Source | What I took from it |
|---|---|
| `client/tools/citations/detect.mjs:1-103` | the header contract: review-list-not-red-test (`:13-14`), the exit contract (`:74-82`), the stated limit (`:91-93`), the two universes, the ordering invariant |
| `detect.mjs:104-822` | `CITATION_TOKEN` (`:171`), `walkFiles`, `indexByBasename`, `runGit`/`verifyEndpoint` (`:281-312`), `runDetector` (`:362-671`), `formatReport`, `parseArgs`/`main` (`:736-816`) |
| `client/tools/citations/self-test.mjs:1-615` | fixture-repo shape (`withFixtureRepo`, `makeFixture`, `fx.cli`), facts F1-F14, and the NO-STANDING-GATE limit stated at `:5-12` |
| `client/tests/CcpClient.Tests/GoonGameCensusTests.cs` (CLOSED to me; read only) | the proven needle mechanism — see §2 |
| `client/docs/upstream-citation-inventory.json` | entry shape; the `IntakeHostService.cs` entry at `:176` |
| `client/docs/wpf-surface-reachability.md:1492` (D232) | the wave-64 rot corpus and its causes |
| `client/docs/task-board.md:325` | the P1 board row this packet executes |
| `client/tests/CcpClient.Tests/ExecutionCensusTests.cs:625-684` | the precedent for spawning `node` from a floor fact, with `TestWait.Until` and a kill-on-timeout, refusing to skip |

---

## 1. Measurements that drove the design

All against the working tree at `e3aee3e21`.

| Fact | Value |
|---|---|
| `ConditioningControlPanel/Services/Quiz/IntakeHostService.cs` | 1264 lines; inventory entry records `+106/-1` at the 2026-08-14 sync — exactly `f7b4c317c` |
| The nine subjects `IntakeGraded` cites | every one currently correct; all nine candidate needles occur **exactly once** in the file |
| Needle line at `db3e842f` (inventory `baseline.merge`) vs working tree | **all nine identical** — the corpus is repaired, so the mode must be SILENT today |
| Needle line at `42286638` (inventory `baseline.previous.merge`) vs working tree | **all nine moved**, by **+3** (the two top-marks subjects) and **+12** (the seven emit/mantra subjects) — which reproduces D232's *"three merely DRIFTED, by 3 and 12 lines"* mechanically |
| Explicit `IntakeHostService.cs:NNN` citations across `client/src/**` + `client/docs/**` | **46** |
| Bare `:NNN` continuation refs across the same trees | **2255**, of which 24 have no preceding citation token in their own file |
| Bare refs that a nearest-preceding-token heuristic would credit to `IntakeHostService.cs` | **200** — and the very first is a **mis-binding** (`DtrhUserMedia.cs`'s `FlashService.GetMediaFiles :2855-2867`, which is a FlashService citation) |

### The class I designed, measured, and CUT

I first designed a per-citation `CITATION-DRIFT` class: an explicit span `[a,b]` that contained a
needle at the comparison endpoint and contains none now. Measured at the `42286638` window it emits
**4 rows, and all 4 are false**:

- `client/src/CcpClient.Desktop/Features/Progression/GradedRunAwards.cs:95`
- `client/docs/trainer-card-census.md:180`
- `client/docs/wpf-surface-reachability.md:1467` and `:1491`

all four cite `IntakeHostService.cs:418-420`. **CORRECTED after the plan gate:** three of them cite it
for the quote *"an intake has no fail state to be held back by"*, and the fourth,
`wpf-surface-reachability.md:1467`, is **D223**, which records `:418-420` as a *historical
mis-citation about the normalisation* and already states today's lines — a different reason for the
same verdict. Lines 418-420 today are exactly that `held_back` comment, so **all four are correct**
and none is owed a repair; the 4/4 measurement and the cut both stand unchanged.
They fire only because the *emit* needle happened to sit at `:419` at the old baseline. A 100%
false-positive rate is the cry-wolf shape `detect.mjs:13-14` forbids by name, so **the class is cut,
not softened**, and it is recorded as a divergence so the next author does not re-invent it.

**Consequence, stated rather than hidden:** the mode therefore never issues a per-citation verdict.
It issues a per-SUBJECT verdict and names the shift; re-basing the citations is the reviewer's step.

---

## 2. What I took from SP-129, and what I changed

| Taken unchanged | Why |
|---|---|
| A needle is a **short distinctive substring**, matched with a plain **Ordinal `contains`** on the raw line | `GoonGameCensusTests.CitationDrift`/`LineAt` do exactly this. A substring match is already immune to indentation changes, which is the reformatting case that matters; normalising whitespace would be an unproven deviation from a proven mechanism |
| A needle that resolves nowhere is a **finding, never a silent skip** | `LineAt` returns `""` out of range and `""` never contains a needle |
| Markdown/JSON escaping is the DOCUMENT's syntax, unescaped before matching real bytes | `ParseCitationTable` unescapes `\|` and `\*`; my needles live in JSON, so JSON string escaping does the same job and no second unescape layer is invented |
| Report **every** disagreement, never the first | `AssertAgrees` |
| Fixtures drive the mechanism; the real tree never decides whether a mechanism fact passes | `self-test.mjs:21-25` and SP-129's fixture block |

| Changed | Why |
|---|---|
| **The stored line is dropped.** SP-129 stores `key \| path \| line \| needle`; I store `id \| needle` only | Trap 2, and packet §2. SP-129's table describes a document under its own authorship; the inventory describes 297 read-only upstream files whose line numbers are not ours to hold. Both endpoints of my comparison are DERIVED: one from `git show <sha>:<path>`, one from the working tree |
| **The verdict is a review row, not an assertion** | SP-129 pins a document it owns and may red; `detect.mjs:13-14` may not |
| **Per-citation verdicts are refused** (see §1) | measured false-positive rate of 100% |
| **The comparison is against a git endpoint, not a stored expectation** | so the data cannot rot; nothing to re-record after a repair |

---

## 3. The needle field's shape

Added to an inventory entry, optional, absent on 296 of 297 entries after this packet:

```json
{
  "path": "ConditioningControlPanel/Services/Quiz/IntakeHostService.cs",
  "tier": 1,
  "citedBy": [ "..." ],
  "changedAtSync": { "...": "unchanged" },
  "verdict": "unchanged",
  "needles": [
    { "id": "quiz-completed-emit", "needle": "QuizService.RaiseQuizCompleted(" }
  ]
}
```

- **Exactly two keys per record**, `id` and `needle`. A structural fact rejects any third key, and
  rejects `line`/`lines`/`lineNumber`/`at` by name.
- `id`: `^[a-z0-9][a-z0-9-]*$`, unique within its entry. Report legibility only; it is never matched
  against source.
- `needle`: a non-empty, trimmed, single-line literal, `<=` the bound stated in the inventory's own
  new top-level `needleContract` string. It may not itself look like a citation (`Foo.cs:12`, `:12`).
- One new top-level field `needleContract`: one sentence saying what a good needle is, **with the
  length bound as a number inside it**, so the bound is stated once and enforced from the test
  rather than duplicated in code. Document is the data, test is the logic — SP-129's own rule.

### The nine seeded needles (the wave-64 corpus), each verified unique in the file

| id | needle | line today | line at `42286638` |
|---|---|---|---|
| `top-marks-rationale` | `Deliberately NOT full marks` | 50 | 47 |
| `top-marks-constant` | `TopMarksPercent = 90.0` | 55 | 52 |
| `score-percent` | `var pct = run.MaxScore` | 426 | 414 |
| `category-normalise` | `run.Niche.Trim().ToLowerInvariant()` | 429 | 417 |
| `quiz-completed-emit` | `QuizService.RaiseQuizCompleted(` | 431 | 419 |
| `passed-always-true` | `passed: true` | 433 | 421 |
| `perfect-guard` | `perfect: run.MaxScore > 0` | 434 | 422 |
| `mantra-credit-cap` | `var affirmed = Math.Min(run.AffirmedMantras` | 451 | 439 |
| `mantra-credit-loop` | `TrackMantraCompleted` | 453 | 441 |

`Math.Min(run.AffirmedMantras` alone was rejected during needle selection: it matches **two** lines
(445, the XP sum, and 451, the credit cap), so the `var affirmed = ` prefix is load-bearing. That is
the "what makes a good needle" rule, discovered by measurement rather than asserted.

### What makes a good needle (stated in `detect.mjs`'s header and in `needleContract`)

1. A **substring of the cited line**, never the whole line — a whole line rots on an indent change.
2. It must occur **exactly once** in the file. The mode reports `NEEDLE-AMBIGUOUS` when it does not.
3. Prefer a code token or a distinctive phrase; avoid punctuation-only and whitespace-run-sensitive text.
4. Never a line number, never a regex. The match is a literal, case-sensitive, Ordinal `contains`.

---

## 4. How the second mode reports

`node client/tools/citations/detect.mjs --needles [--since <sha>]`. It reads the inventory, the
working tree and `git show <endpoint>:<path>`. It compares against the **working tree**, so `--until`
is rejected with a named error rather than silently ignored. The default endpoint is
`inventory.baseline.merge` — the state the inventory was recorded against — which makes the mode
**silent today by construction** and makes it fire exactly when upstream moves past the recorded
baseline, i.e. at the next sync, which is when wave 64's rot was created.

Four row classes, each of which states something **certainly true**, which is what keeps it out of
cry-wolf territory:

| Class | Fires when | Why it is not a wolf |
|---|---|---|
| `NEEDLE-GONE` | the needle matches **0** lines in the file today | the subject a landed claim names is not in the file; every citation into it is suspect |
| `NEEDLE-AMBIGUOUS` | it matches **>1** line today | the needle can no longer anchor a line; the action is "lengthen the needle" |
| `NEEDLE-MOVED` | exactly 1 match at both endpoints, at different lines | upstream moved the subject; the row names `:old -> :new (Δ±n)` and every citer of the path |
| `CITATION-OUT-OF-RANGE` | an explicit citation span of a **needled** path exceeds the file's current line count | arithmetic, not inference; certainly wrong |

Scoping every line-level check to **needled entries only** is how packet constraint 3 is satisfied
by construction: a citation with no needle stays file-level and this mode says nothing about it.

### Coverage, printed on EVERY run (both modes)

A `## NEEDLE COVERAGE` block carrying, as numbers: entries with needles / without; needles total;
explicit citation spans into needled paths, split **confirmed** (a needle's current line is inside
the span) / **uncovered** (no needle at either endpoint) / **out of range**; needles that could not
be compared because they were absent or ambiguous at the endpoint; and the **bare `:NNN`
continuation count with the reason it is not checked**. Today that block reads, at the default
window: 1 of 297 entries needled, 9 needles, 46 explicit spans (22 confirmed, 24 uncovered), 2255
bare continuations not checked.

### Enumerated blind spots, printed and recorded — never generalised from a sample

1. **Bare `:NNN` continuations are not checked at all.** 2255 of them exist. The nearest-preceding-
   token heuristic was measured and **rejected**: it credits **200** of them to `IntakeHostService.cs`
   and mis-binds on the first example examined. **CORRECTED after the plan gate: FIVE, not six**, of
   `IntakeGraded`'s seven rotted citations are of this form — enumerated at `3c38c3973`, bare =
   `:435-441`, `:414`, `:417`, `:418-420`, `:437-438`; file-qualified = `IntakeHostService.cs:406-422`
   and `IntakeHostService.cs:45-53`. The landed D232 and the repaired comment at
   `IntakeQuizRun.cs:136-137` both say "seven bare", which is also wrong; both are outside this
   packet's file scope, so the measured number is stated here and recorded as D264 rather than
   inherited from either. This is still the single largest gap, and it is printed in the report.
2. **Citations into the 296 entries with no needle** are untouched.
3. **A citation that was wrong the day it was written** cannot be caught: no needle knows what a
   citation intended. Four of D232's seven were of this class.
4. **Per-citation verdicts are refused** (§1's cut class).
5. **Port-internal citations** (`client/src` citing `client/src`) are outside the inventory's key
   space entirely — see §7.

---

## 5. How the exit contract stays unchanged

`main()` keeps returning **only 0 or 1**, and `outcome.rows.length` is still never consulted.
`--needles` adds no new return path: `DetectorError` -> stderr + 1 (unparseable inventory, missing
`ConditioningControlPanel/`, unresolvable endpoint, no repo root, `--until` in needle mode), and
everything else -> report on stdout + 0. `verifyEndpoint` is **reused, not re-implemented**, so a
bad `--since` keeps naming the SHA. The needle mode never writes the inventory; `--out` remains the
only write and remains opt-in.

Two floor facts (§6) pin this from the .NET suite: the real tree, both modes, exit 0.

---

## 6. Tests, and the exact edit each must red on

**`client/tests/CcpClient.Tests/CitationNeedleTests.cs`** (new, pure logic + child process; the
headless project is untouched).

| # | Fact | Reds on this exact edit |
|---|---|---|
| 1 | `EveryNeedleRecord_HasExactlyAnIdAndANeedle` | adding a third key to a needle record, or a needle record that is not an object; also `Assert.NotEmpty` so it cannot pass on an empty file |
| 2 | `NoNeedleRecord_StoresALineNumber` | adding `"line": 431` (or `lines`/`lineNumber`/`at`) to a record, or writing a needle that is itself a citation (`IntakeHostService.cs:431` / `:431`) — **the anti-trap-2 guard** |
| 3 | `EveryNeedle_ObeysTheBoundTheInventoryItselfStates` | pasting a whole line in as a needle (exceeds the bound), a needle with leading/trailing whitespace or a newline, a duplicate `id` within an entry, or loosening the bound in `needleContract` prose without the needles conforming |
| 4 | `TheNeedleMode_ExitsZero_SoItCanNeverFailABuild` | `return outcome.rows.length ? 1 : 0` anywhere in `main`, i.e. turning the review list into a red test |
| 5 | `TheNeedleMode_PrintsTheCoverageGapEveryRun` | deleting the coverage block, or dropping the not-checked bare-continuation count from it |
| 6 | `TheDefaultMode_StillExitsZero_AndAlsoPrintsCoverage` | the same two edits on the default path, which is the one that already had users |

Facts 4-6 spawn `node` exactly as `ExecutionCensusTests.cs:625-684` does: `ProcessStartInfo` with an
argument list, `TestWait.Until(process.WaitForExitAsync(), ...)` (**no `Thread.Sleep`, no bare
`Task.Delay`, no clock poll**), kill-the-tree on timeout, and a **hard failure — never a skip — if
node is absent**, because both tier-1 gates are node scripts. The three runs share one lazily
created result so the tool is spawned twice, not six times.

**Deliberately NOT asserted from the floor:** that the nine seeded needles are still present and
unique in upstream. That would red the suite the day upstream edits `IntakeHostService.cs`, which is
the day the tool is most needed — cry-wolf, in a test instead of a report. Needle health is a
`NEEDLE-GONE`/`NEEDLE-AMBIGUOUS` **row**.

**`client/tools/citations/self-test.mjs`**, extended with F15-onward for the new mode, in the
existing temp-fixture-repo style: silence when a needle has not moved; exactly one `NEEDLE-MOVED`
row with the signed delta when it has; `NEEDLE-GONE` and no spurious moved row; `NEEDLE-AMBIGUOUS`
excluded from the comparison; an un-needled entry contributing nothing (the opt-in property);
`CITATION-OUT-OF-RANGE`; a bare `:NNN` counted-but-unchecked; "absent at the endpoint" counted as
uncomparable rather than reported as moved; a non-empty needle review list still exiting 0; the
coverage numbers themselves. **These cases inherit the same named limit as F1-F14 — `self-test.mjs`
is run by NO standing gate (`self-test.mjs:5-12`), and this packet does not wire it into one**;
that is a separate board row with its own acceptance, and `record.md` will say so.

**Floor delta:** `{ "unit": 6, "headless": 0 }` in
`spine-tasks/SP-131-citation-needle-detector/floor-delta.json`. Pin 2457/144, so the expected
observed totals are **2463 unit / 144 headless**. I will not open `client/tests/floor/floor.json`.

---

## 7. Spec-versus-code discrepancies found, and how I resolve them

1. **Packet Step 4 and the board row name "the two `IntakeHostContext.cs:126-127` citations" as part
   of the corpus to seed. They cannot be seeded here.** `IntakeHostContext.cs` is a **port** file
   (`client/src/CcpClient.Desktop/Features/Intake/IntakeHostContext.cs`); there is no such file in
   `ConditioningControlPanel/`. `upstream-citation-inventory.json` is keyed on WPF paths only, and
   the detector's whole universe is `ConditioningControlPanel/**` (`detect.mjs:463-467`,
   `:482-487`). The surviving citation of it, `PersistenceStore.cs:204` -> `IntakeHostContext.cs:212-214`,
   is a port file citing a port file. **Resolution:** seed the `IntakeHostService.cs` half (nine
   needles covering all seven rotted citations' subjects), and record the port-internal half as a
   divergence naming the exact structural reason plus where that class already lives (SP-129's §10.5
   port-anchor table). I will not widen the inventory's key space to port paths. **CORRECTED after
   the plan gate — the conclusion holds, the mechanism was misstated.** For a port path that
   *exists*, the UNRESOLVED loop skips it at `detect.mjs:586`; the spurious row would be
   `CITATION-GONE` (`:653-670`), plus `DELTA-MISMATCH` on any entry carrying `changedAtSync`, because
   numstat is scoped `-- ConditioningControlPanel/` at `:396`.
2. **The packet says SP-129's table is `key | path | line | needle` and tells me to store a needle
   and NOT a line.** Both are true and they are not in conflict once the endpoints are derived; §2
   records exactly which half I took and which I dropped.
3. **A live finding, reported not fixed:** the inventory's `citedBy` for `IntakeHostService.cs` is
   missing `src:src/CcpClient.Desktop/Features/Progression/GradedRunAwards.cs`,
   `docs:trainer-card-census.md` and `docs:wpf-surface-reachability.md`, which all cite it today.
   **CORRECTED after the plan gate: the existing detector does NOT class that as `NEW-CITATION`, or
   as anything at all.** `detect.mjs:640-651` skips any path already in `entryByPath`, and no class
   in `runDetector` diffs an existing entry's citer list — the recorded `citedBy` is only copied into
   rows for display. **A stale `citedBy` on an entry that still exists is invisible to all six
   classes.** The resolution is unchanged and is scope-correct either way — regenerating the
   inventory is not this packet's job — but this is now a **sixth enumerated blind spot**, printed in
   the coverage block, stated in `detect.mjs`'s do-not-do list and recorded in D260.

---

## 8. Divergences (D260 onward; the sibling packet holds D250-D259)

- **D260** — what the needle mode covers and what it provably does not, with today's numbers
  (1/297 entries, 9 needles, 46 explicit spans: 22 confirmed / 24 uncovered, 2255 bare continuations
  unchecked) and the four enumerated blind spots.
- **D261** — the per-citation `CITATION-DRIFT` class that was designed, measured at 4/4 false rows
  on `:418-420`, and cut. Recorded so it is not re-invented.
- **D262** — the retrospective demonstration: `--needles --since 42286638` reproduces D232's +3/+12
  shift for all nine needles mechanically, at the committed head SHA.
- **D263** — the port-internal `IntakeHostContext` half of the board row, and why it is outside the
  upstream inventory's key space.

## 9. Order of work after the verdict

1. `detect.mjs`: needle mode + coverage + header sections (good-needle rules, the new stated limit,
   the cut class). 2. inventory: `needleContract` + the nine needles. 3. `CitationNeedleTests.cs`.
4. `self-test.mjs` F15-onward. 5. divergences. 6. `floor-delta.json`, `record.md`.
7. `node client/tests/floor/check-warnings.mjs` and `check-floor.mjs` through
   `client/tools/gate/with-slot.mjs --slots 3`. 8. Red demonstrations re-run at the committed head
   and quoted with that SHA in `record.md`.

## 10. What this plan does not prove

Nothing has been built, run or rendered. No product code is in scope. The measurements above are
`node` scripts reading bytes; they are not the implementation and do not prove the implementation
will reproduce them — that is what step 8 is for. No headless frame, no headed capture, no
interaction, rendering, audio, focus, window or animation behaviour is touched by any of this.
