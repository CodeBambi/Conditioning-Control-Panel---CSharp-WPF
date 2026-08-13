# SP-065 — record: mechanical skip/count detection that fails the CONTRACT

Board row 49 part (2). Infrastructure-only; closes no product capability (port-workflow item 11).
Part (1) (vacuous-SHAPE sweep) stays on the board, untouched.

## Step 1 — probes, mechanism, design

### Probe results (verbatim log: `step1-probes.md`, same folder)

Stack: xunit.v3 3.2.2 + Microsoft.NET.Test.Sdk 17.10.0 + xunit.runner.visualstudio 3.1.5 in
both projects; .NET SDK 10.0.303; no global.json, no runsettings, no xunit.runner.json, no
TestingPlatform* MSBuild property anywhere under client/ (grep-verified) → VSTest mode.

| Probe | Invocation | Response (exact) | Verdict |
|---|---|---|---|
| A | `dotnet test --help` | VSTest options only (`--logger`, `--results-directory`, `--collect`, `--blame*`); no `--minimum-expected-tests`, no MTP options | MTP surface absent from the CLI |
| B | skip induced (`CCP_DATA_ROOT` child env, filter to the SP-057 pin) | `Skipped! - Failed: 0, Passed: 0, Skipped: 1, Total: 1` | **exit 0** — the defect, verbatim |
| C | B + `--settings probe.runsettings` with `<xunit><failSkips>true</failSkips></xunit>` | test still `[SKIP]`, `Skipped: 1` | **exit 0 — NOT honored** (silently) |
| D | B + `xunit.runner.json {"failSkips": true}` beside the test dll | `FAIL_SKIP : CCP_DATA_ROOT override is active...`, `Failed: 1` | **exit 1 — honored** (file config only) |
| E | `-- --minimum-expected-tests 9999` with 1 test selected | `Passed! ... Total: 1` | **exit 0 — flag silently swallowed; MTP unreachable** in this configuration |
| F | `--results-directory "$TMPD" --logger "trx;LogFileName=results.trx"` | exactly one trx outside the worktree; `<Counters total executed passed failed error timeout aborted inconclusive passedButRunAborted notRunnable notExecuted disconnected warning completed inProgress pending>`; skips surface as `notExecuted`; `<Times creation="...">` present | post-processing substrate confirmed |

All exit codes measured with `${PIPESTATUS[0]}` after catching a first-pass measurement bug
(`cmd | tail; echo $?` reports tail's exit). Probe D's first attempt raced a parallel call
on the shared json file and was re-run serially; the clean re-run is what is recorded.

### Mechanism choice: results post-processing + exact pin

The wrapper runs both projects, captures TRX to a per-run temp dir outside the worktree, and
fails the contract on any deviation from an exact per-project pin (passed / skipped).

**Rejected: runner flag (`failSkips`) as the mechanism.** Its real failure modes: (1) it
cannot express *exactly N expected skips* — it is all-or-nothing, so the legitimate platform
skips that row 49 part (1) wants REPORTING would become failures, and it pins nothing about
counts; (2) the runsettings form is **silently unhonored** on this stack (probe C) — a flag
that looks wired and does nothing is worse than no flag; (3) the honored form (probe D)
requires shipping config into build output and changes runner behavior for every invocation,
including the developer's local ones. A component at most, never the answer.

**Rejected: MTP `--minimum-expected-tests`.** Not reachable at all here (probe E): the flag
is swallowed as a runsettings argument and the run passes with 1 test against a floor of
9999. False confidence by construction.

**Rejected: assembly-teardown assertion.** Cannot fire when the assembly never runs, when a
filter excludes everything, or when the run dies before teardown — exactly the shapes where
"no tests ran" reads as success. Results post-processing sees the absence.

### Wrapper design (`client/tests/floor/check-floor.mjs`, node stdlib only)

- Runs each of `client/tests/CcpClient.Tests/...csproj` and
  `client/tests/CcpClient.HeadlessTests/...csproj`:
  `dotnet test <csproj> -c Debug --nologo --no-build --results-directory <mkdtemp>/<project> --logger "trx;LogFileName=results.trx"`.
  `--no-build` is deliberate: the contract builds the sln first (both test projects verified
  in `client/CcpClient.sln` via `dotnet sln list`); a standalone run without a build fails
  closed with a message naming the build command (consult-reasoning point: fail closed is
  safe, friction gets a loud hint).
- Results dir: `fs.mkdtempSync(join(os.tmpdir(), "ccp-floor-"))` — **outside the worktree**
  (framing d). The path is printed on every run so evidence can hash the trx files.
- **Green requires ALL THREE: dotnet exit 0, every TRX check, and the pin match** (ANDed,
  never substituted — consult-reasoning point).
- Fail-closed checks per project (framing b), each independently demonstrable:
  1. results directory missing / **no trx** (project produced nothing);
  2. **more than one trx** (unexpected extra result files);
  3. **unparseable**: must start with `<?xml`, must END with `</TestRun>` (truncation
     detector), exactly one `<ResultSummary`, exactly one `<Counters`, all counter
     attributes present and non-negative integers, `ResultSummary outcome="Completed"`;
  4. **stale**: file mtime AND `<Times creation="...">` must both be >= wrapper start
     minus 15 s skew (mtime can be preserved by copy tools; creation is what the runner
     stamped — check both, consult-reasoning point);
  5. **zero results**: `total == 0` (filter-matched-nothing / never-ran shape);
  6. bad categories nonzero: `failed error timeout aborted notRunnable inconclusive
     passedButRunAborted` must all be 0;
  7. arithmetic: `passed + notExecuted == total` (self-consistency);
  8. **pin**: `passed` and `notExecuted` (skipped) exactly equal the pin for that project.
- Exit codes: 0 = green; 1 = any violation (each prints a loud named reason).
- Success summary per project: `<project>: <passed>/<pin-passed> passed, <skipped>/<pin-skipped> skipped`, plus the results path.
- The wrapper never sets `CCP_DATA_ROOT` (framing h). It inherits the child env, which is
  exactly the scoped induction seam used for the RED demonstration.

### Pin design (`client/tests/floor/floor.json`, beside the wrapper)

```json
{
  "projects": {
    "CcpClient.Tests": { "passed": <N>, "skipped": 0 },
    "CcpClient.HeadlessTests": { "passed": 35, "skipped": 0 }
  },
  "bumpRule": "Bump only in the SAME commit as the test change that moves the count; the commit message states the reason. Never widen or special-case to make a step pass.",
  "lastMovedBy": "SP-0xx (documentation only — the mechanism cannot verify this field)"
}
```

`client/tests/**` verified not-ignored (orchestrator re-verified `.gitignore:168 tools/`
does not cover it at authoring). Tracking proven with `git ls-files` in Step 2, not assumed.

### Guard design (`FloorWrapperGuardTests.cs`, framing f)

- Mirrors `DataRootChokePointGuardTests` / `HarnessEntryPointGuardTests`: FindRepoRoot walk
  (anchor `client/CcpClient.sln`), never skips, `file:line` violations.
- `spine-tasks/` missing at repo root → **failure**, not a pass.
- Walks `spine-tasks/*/PROMPT.md`; parses the packet number from `SP-(\d+)-...` dir names.
- For every packet with number **>= 65**: the `| testCommand | \`...\` |` row must exist and
  parse (a missing/unparseable row for an in-scope packet is a violation — the guard refuses
  to go blind, consult-reasoning point); if the command matches `\bdotnet\s+test\b` AND does
  not contain `check-floor.mjs` → violation with `file:line`.
- Packets below 65 are grandfathered by the ID rule alone — no suppression list.
- Packets whose testCommand runs no `dotnet test` at all pass by construction (verified on
  the real tree in Step 3).
- One `[Fact]` (violation enumeration), keeping the pin movement minimal: unit floor moves
  897 → 898, bumped in the SAME commit as the guard test (framing e).

### Pre-approach consult (mode: solo)

- **Actual answering model: not surfaced.** The call returned **reasoning only — no verdict
  text surfaced** (same failure class as waves 17/21 and this packet's authoring call 1).
  Nothing was stitched into a verdict; there is none.
- The returned reasoning contained concrete design observations; the ones adopted are marked
  "consult-reasoning point" above: sln membership check (verified), green = exit AND trx AND
  pin, staleness via mtime + `Times/@creation`, truncation-aware unparseable detection,
  guard must fail on a missing/unparseable testCommand row, pin set to the final number in
  the same commit as the fact-adding guard test, test name-swap stays a named blind spot.
