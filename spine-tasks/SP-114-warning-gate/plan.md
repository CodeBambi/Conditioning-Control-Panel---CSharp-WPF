# SP-114 — plan checkpoint

Branch `lane/SP-114-warning-gate`, base `4332b8b9`, worktree
`.claude/worktrees/agent-a47b44268bce8d064`. Written BEFORE the first gate edit.

## 0. The measurement that decides the design (taken first, on the unmodified base tree)

Four builds, every one read unfiltered, logs committed under `evidence/`:

| # | command | tree | result |
|---|---|---|---|
| 1 | `dotnet build client/CcpClient.sln -c Debug --nologo` (cold, no `bin/`) | base `4332b8b9` | **0 Warning(s), 0 Errors** |
| 2 | same | base + one induced `CS0219` | **1 Warning(s)** |
| 3 | same, run again, **source unchanged** | base + the SAME induced `CS0219` | **0 Warning(s)** |
| 4 | same + `--no-incremental` | base + the SAME induced `CS0219` | **1 Warning(s)** |

**Run 3 is the finding.** A real, live `warning CS0219` was sitting in the working tree, and the
project's own mandated build command reported `0 Warning(s)`. MSBuild skipped `CoreCompile` for an
up-to-date project, so the compiler never re-emitted the diagnostic. Warnings are a property of the
**compilation**, not of the assembly, and an incremental build that compiles nothing reports none.

This is a second, independent mechanism for the same class of false claim SP-113 found in its grep:
a lane that builds, then builds again (or whose build was already current from a prior step) reads
`0 Warning(s)` off a compilation that did not happen. **A warning gate that does not force
compilation is vacuous**, and would have signed off exactly the tree runs 2-4 describe.

So the gate forces `--no-incremental`, and a test pins that flag with run 3 as its reason.

## 1. Where the gate lives

`client/tests/floor/check-warnings.mjs`, run as `node client/tests/floor/check-warnings.mjs`.

Beside `check-floor.mjs` because that directory is where this port keeps its pre-land mechanical
gates and where a reader already looks; and because the packet's `fileScopeMustChange` is
`client/tests/floor`.

It is a **separate script and a separate process**. `check-floor.mjs` gains nothing.

## 2. Why this does not compromise the stale-build guard

`client/docs/port-lessons.md:204` and the `assertBuildIsFresh` block in `check-floor.mjs`:
the floor runs `dotnet test --no-build`, so run standalone it measures the LAST BUILD, not the
tree. It once reported 1022 against a source tree containing 1018 (wave-30). The guard that now
fires on that stays exactly as it is.

- `check-floor.mjs` is **not edited by this packet at all**. It still passes `--no-build`, still
  calls `assertBuildIsFresh` before every project, and still contains no `dotnet build`.
- The warning gate builds because **building is what it observes**. It builds the same solution,
  the same configuration, into the same `bin/obj` the floor then reads. So the correct order is
  `check-warnings.mjs` then `check-floor.mjs`, and in that order the warning gate *satisfies* the
  floor's freshness precondition instead of eroding it: it IS the "always build immediately before
  the gate" step port-workflow.md already requires, with the output finally read by something other
  than a human eye.
- A regression test (`WarningGateGuardTests`) pins all three properties of `check-floor.mjs` above,
  so a later lane cannot quietly turn the floor into a builder and cite this packet as precedent.
- Hazard named, not hidden: the warning gate REBUILDS shared `bin/obj`, so it must never run
  concurrently with a floor run **in the same worktree** (the floor's `--no-build` runner would be
  reading assemblies as they are replaced). Across worktrees each lane has its own `bin/obj` and
  `with-slot.mjs` already bounds machine-wide build concurrency. This goes in the gate's header and
  in the docs.

## 3. What the gate reads, and how it refuses to go blind

It spawns the build with `stdio: pipe`, captures stdout+stderr **whole**, writes the complete
unfiltered log to a file, and then makes **two independent readings that must agree**:

1. every canonical MSBuild diagnostic line `…: warning <CODE>: <text> [<project>]`, deduplicated by
   exact line (MSBuild prints each warning inline and again in the summary block, byte-identically);
2. the summary counters `N Warning(s)` / `N Error(s)`.

Disagreement is a FAILURE, not a reconciliation. The gate also fails closed when:

- the build exits non-zero;
- either summary counter line is missing (it will not report a count it did not read);
- any project listed in `client/CcpClient.sln` did not report `Name -> …dll` in the log — a project
  that silently left the build is the same defect class as SP-065's "a suite outside the floor";
- warnings > 0, obviously, naming every one with file, line, code and project.

The regex is the direct answer to SP-113: its filter was `grep -E "error|warning CS|Build succ"`,
which **cannot match `warning xUnit2013`**. This gate keys on `warning <CODE>:` with the code as a
wildcard, and a `--self-test` mode runs the parser over a fixture corpus that contains
`xUnit2013`, `CS0219`, `NU1701`, `MSB3277`, `AVLN3001`, plus negatives that must NOT match
(`0 Warning(s)`, a test name containing the word "warning"). **The known-matching case SP-113's
filter missed is in the corpus by name.**

Build spawn is pinned to `DOTNET_CLI_UI_LANGUAGE=en` and `-tl:off` (both probed and accepted on
SDK 10.0.303 / MSBuild 18.6.14, evidence `measure-04-tloff.log`) so the parse does not depend on the
machine's UI locale or on whether MSBuild chose its terminal logger.

## 4. The boundary — what the gate cannot see (drafted here, final version in `record.md`)

- **Only `client/CcpClient.sln`, only `Debug|Any CPU`, only `net10.0`.** The Release/RID publish
  path (`client/tools/publish/publish.ps1`) and both other trees (`ConditioningControlPanel/`,
  `CCP.*`) are outside it.
- **A suppressed warning is invisible, by construction.** `NoWarn`, `#pragma warning disable`,
  `[SuppressMessage]`, a `.editorconfig` severity of `none`, or a lowered `WarningLevel` all act
  before MSBuild prints anything, so no output-reading gate can ever see them. Mitigation is a
  second, lexical instrument, not a claim: `WarningSuppressionCensusTests` pins the CURRENT
  suppression sites under `client/` by file and code, so a new one fails a test that names it.
  Measured census at base: **1 `NoWarn` (`CcpClient.Desktop.csproj:14` → `AVLN3001`, deliberate per
  CLAUDE.md), 9 `#pragma warning disable CS0067` sites, 0 `[SuppressMessage]`, 0
  `GlobalSuppressions`, 0 `.editorconfig` anywhere in the repo, 0 `TreatWarningsAsErrors`, 0
  `WarningLevel`.**
- **Analyzer-set dependent.** The warning set is whatever this SDK, these packages and these
  analyzer versions emit. A different SDK can emit more or fewer; the gate reports what its own
  build said and nothing about what another machine's would say.
- **Restore-time (`NU*`) warnings are only re-evaluated on a run where restore actually runs.**
  `--no-incremental` forces compilation, not restore; NuGet no-ops with "All projects are up-to-date
  for restore". The gate therefore REPORTS whether restore no-opped, so the reader knows which
  reading they are holding.
- **It is not a test.** It observes a build. It says nothing about behaviour, rendering, timing,
  interaction or any headed claim.

## 5. How the bite will be proven

Not asserted — executed, with the output kept:

1. **Compiler warning, test project.** Induce `CS0219` in `client/tests/CcpClient.Tests`, run the
   gate, capture the red.
2. **Analyzer warning, the exact SP-113 shape.** Induce a real `xUnit2013`
   (`Assert.Equal(1, collection.Count)`), run the gate, capture the red — and confirm SP-113's
   filter would have passed the same stream, which is the whole reason this packet exists.
3. **A different project.** Induce a warning in `client/tools/verify/CcpVerify` to prove
   `--no-incremental` really re-analyses projects the change did not touch.
4. **The vacuity check.** With a warning live in the tree, run a plain `dotnet build` (0 warnings,
   already measured as run 3) and the gate (red) back to back. That is the gate earning its keep.
5. **Restore byte-identically** and prove it: `git status --porcelain` empty for tracked files and
   `git diff HEAD` empty, then re-run the gate green.

## 6. Tests and floor delta

New file `client/tests/CcpClient.Tests/WarningGateGuardTests.cs` (pure logic, no Avalonia — nothing
here needs a visual tree, so nothing goes in the headless project). Planned facts:

1. `check-floor.mjs` still runs `--no-build`, still calls `assertBuildIsFresh`, and contains no
   `dotnet build` — the central trap, pinned.
2. `check-warnings.mjs` exists, forces `--no-incremental`, and carries the run-3 reason in its own
   source.
3. The gate discovers projects from the solution rather than a hardcoded list, and every project in
   `client/CcpClient.sln` is therefore covered.
4. The gate passes no warning-weakening switch (`NoWarn`, `-warnaserror:none`, `WarningLevel=0`,
   `-p:RunAnalyzers=false`).
5. The self-test corpus contains the `xUnit2013` case by name and at least one negative case.
6. `client/docs/port-workflow.md` and `client/docs/verification-harness.md` name the gate.
7. Suppression census: the exact `NoWarn` / `#pragma warning disable` sites under `client/`, pinned
   by file and code, failing with file:line on any addition.

Expected delta **+7 unit / +0 headless** against pin **1930 / 117**, i.e. an observed **1937 / 117**;
the exact number is declared in `floor-delta.json` once written and re-derived from the committed
file before the record is finalised. `client/tests/floor/floor.json` is never opened.

## 7. Explicitly out of scope

`client/src/**` is closed. The base tree measures 0 warnings (run 1), so no product change is
needed; if one ever were, that is a finding and a board row, not a licence. No warning is silenced,
no `#pragma` added, no `NoWarn` added, no `WarningLevel` raised, and `allowedSkips` is not opened.
