## STATUS: SP-065 — Mechanical skip/count detection that fails the CONTRACT
**Current Step:** 5 complete — all steps done
**Last Updated:** 2026-08-13 (worker, all steps complete)
**Blockers:** none

### Step 1: probe the runner surface, choose the mechanism, design the guard — COMPLETE
- [x] Update STATUS.md before starting work
- [x] Probe `dotnet test --help`, xunit v3 `failSkips`, MTP `--minimum-expected-tests` — exact invocation + exact response each
- [x] Choose the mechanism; reject the other two on their real failure modes (exactly-N skips; zero-results/never-ran/filtered-out)
- [x] Wrapper design: invocation of both projects, results OUTSIDE the worktree, full fail-closed list, exit codes, success summary
- [x] Pin-file design: not-ignored location, schema, per-project passed/skipped, what a bump requires
- [x] Half-install guard design: walk, `>= SP-065` ID rule, `file:line` shape, why it cannot skip
- [x] Pre-approach solo consult (T-7: `mode: "solo"`) — verdict + ACTUAL answering model; record exactly what surfaced, never stitch

### Step 2: implement the wrapper and the pin, prove BOTH verdicts — COMPLETE
- [x] Wrapper at exactly `client/tests/floor/check-floor.mjs`; pin beside it; `git ls-files` tracking proof
- [x] Results outside the worktree; `git status --porcelain --ignored=matching -uall` gains no new entry
- [x] Every fail-closed behavior implemented AND demonstrated (one table row per failure mode)
- [x] Induced skip -> RED (captured); clean run -> GREEN
- [x] Induced count drift BOTH directions -> RED (captured)
- [x] Every injection removed and proven removed
- [x] Pin bumped in the same commit as any count change, reason stated

### Step 3: the half-install guard — COMPLETE
- [x] Guard walks `spine-tasks/*/PROMPT.md`, enforces IDs `>= SP-065`, `file:line` violations, never skips
- [x] Captured RED from a probe packet with a bare `dotnet test` testCommand; probe deleted and deletion proven
- [x] This packet's own PROMPT.md passes the guard (self-binding)
- [x] Confirm no false fire on a packet that legitimately runs no tests

### Step 4: record + pre-completion consult — COMPLETE
- [x] record.md complete (probes verbatim, mechanism + rejections, fail-closed table, new exact pin, both verdicts, drift, guard RED, ls-files proof, cleanliness proof, 3-run table, consults + actual models, intended filings)
- [x] Honesty cell — all six named limits
- [x] Pre-completion solo consult
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — COMPLETE
- [x] Contract testCommand passes through the wrapper (verify.mjs 0, 0W/0E, 898 unit / 35 headless, 0 skipped)
- [x] 3 consecutive full-suite greens, >= 1 fresh-checkout first-ever build (run 2 cold)
- [x] `git diff --check` clean
- [x] `git status --short` shows only File Scope paths
- [x] No new gitignored-dirty entry produced by the wrapper

### Discoveries
- xunit.runner.visualstudio 3.1.5: a dynamic skip (`Assert.SkipWhen`) yields `outcome="NotExecuted"` in the TRX result list while `Counters/@notExecuted` stays 0 and `@executed` excludes it — Counters arithmetic does NOT close over skips. Skip counts must be anchored on the result list.
- runsettings `<xunit><failSkips>` is silently unhonored on this stack; `xunit.runner.json` beside the test dll IS honored; MTP flags are silently swallowed (VSTest mode).
- Named flake: `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` — 1 red in 15 full-suite runs, mechanism non-obvious (per-instance fixtures), filed for the board.
