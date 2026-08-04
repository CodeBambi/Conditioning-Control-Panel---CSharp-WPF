# Task: SP-041 — T-15: c2 AI lab harness hardening (HttpListener lifecycle + leaked test hosts)

## Mission

Execute the `client/docs/task-board.md` P2 tooling row **"T-15: c2 AI lab harness — HttpListener lifecycle under parallel load + leaked test hosts"** (OPEN, filed 2026-08-04): harden the c2 deterministic loopback lab (`client/tests/CcpClient.Tests/AiProviderLab.cs`) so the full suite is stable under parallel load and repeated runs. The flake mechanism is ROOT-CAUSED and harness-side: (1) the lab's `HttpListener` can be disposed while a request is in flight (the SP-023 fragility class — a disposed listener throws `ObjectDisposedException` on the request path, observed in `Refusal_ThroughPipeline_TypedCarrier_ExactlyOneHit`); (2) leaked `dotnet.exe` test-host processes from earlier runs held loopback ports and poisoned later runs (progressive 1→2→3 red on identical content; zombie kill → immediate green). **Harden the harness, NEVER the assertions** — the product refusal path is proven (in-lane greens ×4 consecutively; the post-kill 516/516).

**Honesty framings (binding):** (a) **never weaken, time out-loose, or delete an assertion to buy green** — the fix lives in the lab's listener lifecycle and test-host discipline; any assertion that must change gets a per-change justification in record.md; (b) the lab remains deterministic and loopback-only (127.0.0.1, zero external network) — no timing-sleep band-aids as the primary mechanism (bounded settle waits are acceptable where the SP-019 shapes already use them); (c) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (d) acceptance is EMPIRICAL: 5 consecutive full-suite runs green on one box with the suite's own hosts reaped, plus a lab self-check that fails loud on a leaked listener; (e) **WSL2 named limit: laptop WSL zero distros — Windows-only evidence, never faked.**

## Dependencies

- **Task:** SP-035 (c2 landed — the lab being hardened)

## Context to Read First

- `client/docs/task-board.md` row T-15 (acceptance text) + the wave-5 gate-history entry (the T-3 forensics: failing test name, exception, zombie mechanism)
- `client/tests/CcpClient.Tests/AiProviderLab.cs` (the lab: listener lifecycle, modes, per-request records) + `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs` + `client/tests/CcpClient.Tests/LoopbackOllamaProviderTests.cs` (consumers)
- `spine-tasks/SP-035-ai-companion-c2/record.md` (the lab's design — failure-injection shapes, per-request records, SlowOk decorator)
- `spine-tasks/SP-038-ai-companion-c3/record.md` §7 item 8 (the in-lane transient, cause-then-unknown — now root-caused)
- The SP-023 HttpListener lesson (`client/docs/port-lessons.md` 2026-07-21: a FAILED `Start()` DISPOSES the instance; retry needs a FRESH instance per attempt) + the SP-027 orphan-guard rule (bounded WaitForExit + loud FAIL + Kill, never a leaked process)

## File Scope

- `client/tests/CcpClient.Tests/AiProviderLab.cs` (the harness: listener lifecycle, teardown, self-check)
- `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs` + `client/tests/CcpClient.Tests/LoopbackOllamaProviderTests.cs` (host-exit discipline only — no assertion changes without per-change justification)
- `spine-tasks/SP-041-ai-lab-harness/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/AiProviderLab.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/src/**`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-041-ai-lab-harness/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Harness archaeology + fix design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Read the lab's listener lifecycle (bind, request dispatch, teardown) and reproduce the failure shape under load (a stress run that flaked before the fix — capture the exact exception/test names as the before-state; if it won't flake on demand, record the wave-5 T-3 forensics as the before-state and say so)
- [ ] Design: listener teardown tolerates in-flight disposal (drain-or-abandon discipline; FRESH instance per bind attempt per the SP-023 rule; `ObjectDisposedException` on the request path classified as harness-teardown, never a product failure); host-process exit guaranteed per test (the orphan-guard rule: every lab test's hosts reaped at disposal, loud on leak); the leaked-listener self-check (fails loud with the port/prefix named)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Harden the harness

- [ ] `AiProviderLab.cs`: listener lifecycle hardening + teardown discipline + self-check
- [ ] Consumer files: host-exit discipline only (assertions untouched unless justified per change)
- [ ] The full matrix still passes (all SP-019 failure shapes behave identically — the lab's OBSERVED semantics never change: hit counts, client-gone/completed records, Retry-After gaps, SlowOk arrival)

### Step 3: Stability proof + evidence + pre-completion consult

- [ ] **5 consecutive full-suite runs green** (the acceptance's empirical bar; record each run's counts; the suite's own hosts reaped between runs — prove no leaked `dotnet.exe` test hosts after the final run)
- [ ] Self-check demonstrated: inject a deliberate leaked-listener shape in a THROWAWAY test run (never committed) and show the self-check fails loud with the port named; the committed suite contains no such leak
- [ ] Write `spine-tasks/SP-041-ai-lab-harness/record.md` (archaeology, before-state evidence, design, per-change justifications, stability transcripts, consult verdicts + ACTUAL answering models, engine-review presence, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green; counts EXACTLY the 516/29 floor — zero product change)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Lab listener lifecycle hardened (in-flight disposal tolerated; fresh instance per bind; teardown-class exceptions classified as harness, never product)
- Host-exit discipline: no leaked test hosts after any lab test (orphan-guard; loud on leak); leaked-listener self-check fails loud with the port named
- 5 consecutive full-suite runs green with the suite's own hosts reaped (transcripts)
- Lab semantics unchanged (all SP-019 failure shapes observed identically); zero product change; contract green (516/29 exact)
- record.md carries the before-state evidence, per-change justifications, both solo consult verdicts with actual answering models, engine-review presence per call

## Do NOT

- Weaken/loosen/delete assertions to buy green (honesty framing (a)); add timing sleeps as the primary mechanism; change lab semantics (hit counts, record outcomes, Retry-After discipline, SlowOk arrival); touch product code (`client/src/**` — harness-only); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state; fake the 5-run transcript (each run's real counts recorded)
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-041): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-041-ai-lab-harness/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **T-15 row filed at the wave-5 land (T-3 flake forensics root-caused the zombie test-host + listener-disposal classes).** Harness-only scope; assertions protected by honesty framing (a); the 5-consecutive-runs bar is the row's own acceptance. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-040 + SP-041, 2 lanes — disjoint file scopes by filename) per owner cycle.
