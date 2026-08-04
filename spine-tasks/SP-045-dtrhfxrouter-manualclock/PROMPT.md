# Task: SP-045 — DtrhFxRouterTests ManualClock hygiene (SP-043 discovery)

## Mission

Close the latent-timer discovery recorded in `spine-tasks/SP-043-dtrh-captimer-tests/record.md` §7 item 4: `client/tests/CcpClient.Tests/DtrhFxRouterTests.cs:34` still constructs `DtrhNativeEffects` with the real-clock default while routing a fire-payload video message — a latent, non-observed 15s timer on a pool thread. Inject the proven `ManualClock` fake (the SP-043 class-wide pattern) so no test in the file arms a real timer. **Hygiene only — zero assertion changes, zero product change.**

**Honesty framings (binding):** (a) never touch an assertion — this is the SP-043 class-wide injection applied to one more file, nothing else; (b) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (c) **WSL2 named limit: laptop WSL zero distros — Windows-only evidence, never faked.**

## Dependencies

- **Task:** SP-043 (the seam + ManualClock pattern it landed)

## Context to Read First

- `spine-tasks/SP-043-dtrh-captimer-tests/record.md` §7 item 4 (the discovery) + §4 (the ManualClock pattern — due+fire capture, in-order Advance, dispose-cancels)
- `client/tests/CcpClient.Tests/DtrhFxRouterTests.cs` (the construction at :34 + every other construction in the file — class-wide injection, not just :34)
- `client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs` (the SP-043 ManualClock reference implementation)

## File Scope

- `client/tests/CcpClient.Tests/DtrhFxRouterTests.cs`
- `spine-tasks/SP-045-dtrhfxrouter-manualclock/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/DtrhFxRouterTests.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/src/**`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-045-dtrhfxrouter-manualclock/record.md` |

## Review Level: 1 (Plan Only)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Verify + inject + consult

- [ ] Update STATUS.md before starting work
- [ ] Verify every `new DtrhNativeEffects(` construction in the file (class-wide, not just :34); inject the SP-043 `ManualClock` pattern (shared or file-local per the existing convention — follow the SP-043 shape)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the one-paragraph change description; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Green + zero-wall-clock grep + evidence + pre-completion consult

- [ ] Full DTRH test classes green (`DtrhFxRouterTests` + `DtrhNativeEffectsTests`); zero assertions changed in the diff (grep-proven)
- [ ] Zero-wall-clock grep over the file (only deterministic fake-clock fields/comments match — the SP-043 proof shape)
- [ ] Write `spine-tasks/SP-045-dtrhfxrouter-manualclock/record.md` (constructions found + injected, consult verdicts + ACTUAL answering models, engine-review presence)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 3: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green; counts EXACTLY the 564/29 floor — zero new tests)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every construction in `DtrhFxRouterTests.cs` injects a ManualClock (no real timer armed anywhere in the file)
- Zero assertion changes (grep-proven); zero new tests; contract green (564/29 exact)
- record.md carries both solo consult verdicts with actual answering models + engine-review presence per call

## Do NOT

- Touch any assertion or product code (`client/src/**` is banned); add tests; widen anything; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-045): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-045-dtrhfxrouter-manualclock/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **S-size hygiene packet closing the SP-043 recorded discovery (§7 item 4).** Review Level 1 (single-file hygiene, zero behavior change — the SP-043 pattern applied verbatim). **`## Review Level: 1` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-044 + SP-045, 2 lanes — disjoint scopes) per owner cycle.
