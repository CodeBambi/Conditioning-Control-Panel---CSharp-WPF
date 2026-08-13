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

### Engine review, Step 1 (T-2 heading format)

`spine_review_step step=1 type=plan` → **engine review ABSENT**: nested reviewer spawn
blocked inside worker session (`skipped: true, spawnFailed: false`, artifact
`.reviews/1-20260813T034352.md`). The batch engine runs reviews after `.DONE` (SP-195).

## Step 2 — wrapper + pin, both verdicts, every fail-closed mode

### Tracking proof (framing c — tree presence, not disk presence)

```
$ git ls-files client/tests/floor/
client/tests/floor/check-floor.mjs
client/tests/floor/floor.json
```

### Worktree cleanliness (framing d)

`git status --porcelain --ignored=matching -uall` snapshotted before and after a wrapper
run: **diff empty — zero new ignored entries**; `grep -icE 'trx|testresult'` over the
ignored listing = 0. Results land in `os.tmpdir()/ccp-floor-*` and the path is printed
every run; sha256 of both trx files captured in `evidence/worktree-cleanliness.txt`.

### Fail-closed demonstration table (framing b) — `evidence/fail-closed-table.txt`

The harness `evidence/demo-fail-closed.mjs` imports the REAL exported
`verifyProjectResults` from the wrapper and feeds it sabotaged fixtures. **14 PASS /
0 FAIL** (13 fail-closed cases + 1 positive control): results dir missing; no .trx; two
.trx files; garbage (not XML); truncated mid-write (no `</TestRun>`); stale mtime + stale
creation; stale creation with fresh mtime (copy-tool shape); zero results; failed category
nonzero; ResultSummary outcome != Completed; inconsistent arithmetic; off-floor count;
unexpected skip. Positive control: valid on-floor fixture accepted.

(One fixture in the first harness run was itself inconsistent — a skip fixture with
`executed=897` — and the wrapper correctly rejected it on arithmetic before the pin check.
The fixture was fixed, not the wrapper: executed 896 when 1 of 897 skips.)

### Both verdicts (the board's acceptance wording)

- **Induced skip → RED**: `CCP_DATA_ROOT` set on the wrapper's child process only (framing
  h — parent shell confirmed unset after), SP-057 pin skips, suite reports 896/1,
  `dotnet test` itself exits 0, **wrapper exits 1** (FLOOR VIOLATION).
  `evidence/red-induced-skip.txt`.
- **Clean run → GREEN**: `FLOOR OK: CcpClient.Tests: 897/897 passed, 0/0 skipped;
  CcpClient.HeadlessTests: 35/35 passed, 0/0 skipped`, exit 0.
  `evidence/green-clean.txt`.

### Count drift, both directions

- **+1**: temporary `FloorDriftProbe.cs` (one fact) → 898 vs pin 897 → exit 1.
  `evidence/red-drift-plus-one.txt`. File deleted afterwards.
- **-1**: one existing fact (`ResolveDataRoot_RelativePath_ThrowsTyped`) temporarily
  removed → 896 vs pin 897 → exit 1. `evidence/red-drift-minus-one.txt`. Restored with
  `git checkout` afterwards.
- **Injections proven removed**: `git status` after reverts showed only intended files;
  the cleanliness-run wrapper pass above (897/35/0, exit 0) is the post-injection clean run.

### Pin bump discipline (framing e)

No count change in Step 2 (injections reverted), pin committed at the real 897/35/0. The
Step 3 guard test adds exactly one fact; the pin bump 897 → 898 lands in the SAME commit
as the guard test with the reason in the message.

### Named red during Step 2 (amendment: identify BY NAME, n=1 is not "pre-existing")

The first-ever wrapper run went RED on
`CcpClient.Tests.ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`
(`ChaosTunnelLoopbackTests.cs:143` = `Assert.DoesNotContain("index.html", logs)`): the
collecting log contained a filename the route-class logging contract forbids. **Not
reproduced**: passes standalone, and the immediate full-suite re-run plus every subsequent
wrapper run (5+ full-suite runs this session) are green on it. Hit rate **unquantified**
(1 failure in 1 run, then 0 in 5+). The class uses a per-test-instance log and server (no
intra-class sharing), so the mechanism is not obvious — candidate classes: async log-write
race, or `LoopbackListenerRegistry` cross-talk. **Not fixed here**: `client/src/**` is out
of scope and weakening the assertion is forbidden; filed as an intended board filing
(Step 4). This is exactly the failure class this row exists to make loud — and it surfaced
on the mechanism's first run.

### Engine review, Step 2 (T-2 heading format)

`spine_review_step step=2 type=plan` → **engine review ABSENT**: nested reviewer spawn
blocked inside worker session (`skipped: true, spawnFailed: false`, artifact
`.reviews/2-20260813T035932.md`). Reviews run on the engine after `.DONE`.

## Step 3 — the half-install guard

### Guard implementation

`client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` — one fact
(`PacketsAtOrAboveSp065_RouteDotnetTestThroughTheFloorWrapper`), mirroring the
choke-point/harness guards: FindRepoRoot walk (anchor `client/CcpClient.sln`),
**never skips** (missing `spine-tasks/` → hard failure), `file:line` violations.
Walks `spine-tasks/*/PROMPT.md` (packet-root only), parses the packet number from the
directory name, binds IDs **>= 65**; grandfathering is the ID rule alone. For bound
packets: the `| testCommand | \`...\` |` row must exist and parse (missing/unparseable =
violation — the guard refuses to go blind), and a command matching `\bdotnet\s+test\b`
without `check-floor.mjs` is a violation.

### Captured RED (probe packet)

Probe `spine-tasks/SP-099-floor-guard-probe/PROMPT.md` with a bare-`dotnet test`
testCommand → guard **FAILED** naming
`spine-tasks/SP-099-floor-guard-probe/PROMPT.md:7` verbatim (`evidence/red-guard-probe.txt`).
Probe deleted afterwards (`ls | grep -c SP-099` = 0, no git entries), guard re-run green.

### Self-binding and no-false-fire

- This packet's own PROMPT.md (SP-065 >= 65, bound): testCommand is `node verify.mjs &&
  dotnet build ... && node client/tests/floor/check-floor.mjs` — `dotnet build` is not
  `dotnet test`, and the wrapper token is present. **Passes.**
- Live tree classification (grep sweep, all 65 packets): every packet below 65 with a bare
  `dotnet test` testCommand (SP-002..SP-064) is **grandfathered by the ID rule** — the guard
  does not flag them (verified green on the real tree).
- **No-false-fire on legitimately test-less packets**: SP-001 and SP-021 have testCommand
  rows with no `dotnet test` at all; the guard passes them by construction, and the
  whole-tree green run confirms it in fact.

### Pin bump (framing e)

The guard adds exactly one fact: unit floor 897 → **898** (headless stays 35, skipped
stays 0). `floor.json` bumped in THIS commit — the same commit as the guard test — with
the reason in the commit message and in `lastMovedBy` (documentation-only field).
**New exact floor: 898 unit / 35 headless / 0 skipped.**

### Engine review, Step 3 (T-2 heading format)

`spine_review_step step=3 type=plan` → **engine review ABSENT**: nested reviewer spawn
blocked inside worker session (`skipped: true, spawnFailed: false`, artifact
`.reviews/3-20260813T040324.md`). Reviews run on the engine after `.DONE`.

## Step 4 — honesty cell, filings, consults

### Honesty cell — what this mechanism does NOT close

1. **It detects an off-floor count but does not name which test vanished.** The TRX has
   the names; the wrapper deliberately checks counts, not identity. Diagnosis still starts
   from the trx in the printed results directory.
2. **It binds only invocations routed through the wrapper.** A bare `dotnet test` run by a
   human still exits 0 on an unexpected skip. The Step 3 guard binds future PACKETS
   (>= SP-65 testCommands); it cannot bind a keyboard.
3. **It cannot prove the pinned number is the RIGHT number.** A pin bumped in the same
   commit as a bad or vacuous test is blessed by the mechanism. This replaces "a human
   must compare numbers" with "a human must justify a bump" — better, and **not
   verification**. (`lastMovedBy` is documentation; nothing checks it.)
4. **It does nothing for part (1)'s vacuous-SHAPE sweep.** A test that asserts nothing and
   reports passed stays invisible to it.
5. **The template that makes future packets inherit the wrapper is an orchestrator land
   action** (`.spine/patches/manifest.json` — not worker-writable). The guard catches its
   omission only at the NEXT packet's lane, not before.
6. **Linux unproven.** Zero WSL distros on this machine; no Linux run was attempted and
   none is faked. The wrapper is portable node + TRX parsing, but "portable by
   construction" is not "proven on Linux".
7. **Test name-swap blind spot** (from the Step 1 consult reasoning, kept deliberately):
   a commit that removes one fact and adds another keeps the count and passes. Closing it
   means pinning a hash of the sorted test-name list — more friction than this row sized
   for; named here so nobody believes count = identity.

### Intended filings (named only — the worker sets no row state, ENABLER 2)

- **task-board.md**: row 49 part (2) evidence — wrapper (`client/tests/floor/check-floor.mjs`),
  pin (`floor.json` 898/35/0), half-install guard (`FloorWrapperGuardTests`); part (1)
  remains open on the board.
- **task-board.md (new row candidate)**: named flake —
  `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` went red once
  in this lane (1 in 6+ full-suite runs, unquantified), per-instance fixtures so the
  mechanism is non-obvious; named so nobody "fixes" it by weakening the assertion.
- **port-lessons.md (candidate)**: `cmd | tail; echo $?` reports TAIL's exit code — measure
  with `${PIPESTATUS[0]}`. Hit during this packet's own probes (first-pass readings were
  all wrong and were re-measured).
- **orchestrator land action**: `.spine/patches/manifest.json` packet-template change
  routing future testCommands through `node client/tests/floor/check-floor.mjs`. Until it
  lands, the Step 3 guard turns the NEXT bound packet's lane red — the designed catch point.

### Product capability statement (port-workflow item 11)

This task writes **zero product code** and closes **no product capability**.
`client/src/**` untouched (verified in Step 5 status sweep).

### Consults

- **Pre-approach (Step 1), mode solo**: reasoning only, NO verdict text surfaced; actual
  answering model not surfaced. Recorded verbatim in Step 1; adopted reasoning observations
  are marked "consult-reasoning point" there. Nothing stitched.
- **Pre-completion**: below, appended after the call.

### Engine-review presence summary (T-2)

| Step | Call | Engine review |
|---|---|---|
| 1 | `spine_review_step step=1 type=plan` | ABSENT (nested-spawn block, skipped=true, spawnFailed=false) |
| 2 | `spine_review_step step=2 type=plan` | ABSENT (same) |
| 3 | `spine_review_step step=3 type=plan` | ABSENT (same) |

## Step 5 — verification

(filled in during Step 5)
