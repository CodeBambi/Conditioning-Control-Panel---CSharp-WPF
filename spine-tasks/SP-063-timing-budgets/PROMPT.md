# Task: SP-063 — Raise the injected test timeout budgets (owner decree)

## Mission

**OWNER DECREE 2026-08-12 (chat, verbatim): "Just increase the amount of budgets by a lot! So it does not happen again."** This supersedes the SP-059-inherited standing order that raising a timeout budget is a banned fix, **for this row only**. The owner is authority order #1; the ban was a consult-derived rule, not an owner decision.

The defect: `LoopbackOllamaProviderTests` builds every provider from one factory with `RequestTimeout = TimeSpan.FromMilliseconds(800)` / `ProbeTimeout = 800 ms` (`LoopbackOllamaProviderTests.cs:20-27`), and `AiProviderLabIntegrationTests`' `Harness` does the same. On a fresh-checkout first-ever build, JIT + `HttpListener` warmup exceeds 800 ms, so the provider **correctly** returns `timeout` before it ever parses the body — and a test whose subject is *classification* fails on *wall time* (`Truncated_PrefixCut_NeverSurfaced_TypedUnavailable`, `:168`).

**The fix is the decree: give every such budget a LOT more headroom, from ONE shared constant.** Three populations, three treatments:

1. **Budgets that must not decide an outcome** (classification, round-trip, payload-shape, cancellation tests) → the new large shared constant.
2. **Budgets whose ELAPSING IS the subject** (the two timeout-classification tests) → **keep them short and deliberate.** Raising these would not fix anything: their expected outcome IS `timeout`, cold start only makes that outcome more certain, and a large budget would just make the suite sit there for a minute per test. Keep the literal, mark it with `// wallclock-allow: <reason>`, and pin it.
3. **Budgets that are inert** (pre-socket rejection, connection-refused probes — the deadline is never reached, and even if it fired the typed code is identical) → delete the assignment; the product default stands.

**Binding framings:**

(a) **Large but FINITE.** Use one shared constant — **`TimeSpan.FromSeconds(60)` unless you can justify a different single value in `record.md`** — which is ~75× the old budget and far beyond any plausible cold-start warmup. **Do NOT use `Timeout.InfiniteTimeSpan`:** an infinite deadline turns a genuinely wedged lab or product hang into a test host that hangs forever (this suite has no per-test timeout), which the project has already paid for twice as zombie test hosts and stall-killed workers. A bounded 60 s fails loudly in a minute; that is the whole difference. Name the constant once, use it everywhere in population 1.
(b) **This is a decree, not a discovery.** Do not re-litigate the ban, do not propose deterministic-classification surgery instead, and do not touch product code — `client/src/**` is entirely out of scope for this packet. If you believe the decree is wrong, say so in `record.md` in one paragraph and **implement it anyway**.
(c) **Do not weaken anything else.** No assertion changes, no widened tolerances, no deleted or skipped tests, no new `Task.Delay`/`Thread.Sleep` outside `TestWait`.
(d) **The exact-count floor is load-bearing** (SP-062, landed `7518c6a4`): **892 unit / 35 headless, 0 skipped**. This packet adds no facts, so the numbers must be *identical* in every run. An unexpected **skip** counts as a red here even though `dotnet test` exits 0.
(e) **A previous run of this packet was ABORTED mid-flight** when the decree arrived. Its completed Step-1 work is preserved at `spine-tasks/SP-063-timing-budgets/prior-step1/` (a full 13-row suite-wide sweep of every injected budget in both test projects, the mechanism read out of the provider's classification order, and two cold first-ever-build reproduction attempts that did NOT reproduce: 0 firings / 2 cold runs). **Verify it, do not trust it** — re-run the sweep's greps against the current tree and say in `record.md` which rows you confirmed, which you corrected, and what it missed. Its recommended treatment for population 1 was `Timeout.InfiniteTimeSpan`; framing (a) overrides that with a large finite constant.
(f) **Keep the guard small.** The decree's goal is "so it does not happen again", so extend `TestTimingGuardTests` with the **one** option-assignment token that catches this shape (`"Timeout = TimeSpan."` — it matches `RequestTimeout =`, `ProbeTimeout =`, and any future `*Timeout = TimeSpan.` initializer) under the existing marker+pin discipline, and **prove the guard bites with a captured RED** (inject an unpinned budget, capture the failure, remove the injection). No new fact, no new doctrine, no new guard file — the count must stay 892.
(g) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — name intended filings in `record.md`; the orchestrator reconciles at land.

## Dependencies

- SP-062 (landed `7518c6a4`) — the suite's count discipline is trustworthy again, which is what makes "10 consecutive greens at an exact count" mean something.

## Context to Read First

- `spine-tasks/SP-063-timing-budgets/prior-step1/aborted-run-record.md` — the preserved sweep + mechanism analysis from the aborted run (verify, do not trust)
- `client/tests/CcpClient.Tests/LoopbackOllamaProviderTests.cs` — the shared `Provider(...)` factory and the two timeout-subject tests
- `client/tests/CcpClient.Tests/AiProviderLabIntegrationTests.cs` — the `Harness` factory (request AND probe budgets; a cold probe timeout flips the capability state and every pipeline expectation with it)
- `client/tests/CcpClient.Tests/TestTimingGuardTests.cs` — `ForbiddenTokens`, the `// wallclock-allow:` marker contract, and the `Pins` shape (path + exact trimmed code + count)
- `client/tests/CcpClient.Tests/TestWait.cs` — the approved helper
- `client/docs/port-lessons.md` — the 2026-08-12 entries on budgets-vs-waits, cold-means-fresh-checkout, and TRX failure names
- `docs/constitution.md` — standing orders

## File Scope

- `client/tests/CcpClient.Tests/**`
- `client/tests/CcpClient.HeadlessTests/**` (the prior sweep found zero sites there — confirm)
- `spine-tasks/SP-063-timing-budgets/**`
- **NOT in scope:** `client/src/**` (all product code), `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/LoopbackOllamaProviderTests.cs` |
| fileScopeMustNotChange | `client/src/**`, `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**` |
| artifactsMustExist | `spine-tasks/SP-063-timing-budgets/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Verify the preserved sweep, classify every site, pick the constant

- [ ] Update STATUS.md before starting work
- [ ] Re-run the sweep's greps on the current tree; produce the site table yourself (file:line, literal, population 1/2/3, treatment). State per row: confirmed / corrected / newly found, and anything the prior sweep missed
- [ ] Name the shared constant and its value (framing a: 60 s default; a different single value needs a written justification). One definition, referenced everywhere in population 1
- [ ] State the expected wall-clock cost: population 2's short budgets are the only ones that can elapse on a green run, so suite duration must not materially change — say what you expect and check it in Step 3
- [ ] **Pre-approach solo consult** (`mode: "solo"` — bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback). **Tell the advisor the decree is settled and ask only about EXECUTION** (constant value, site classification, guard token shape, anything the sweep missed). Verdict + **ACTUAL answering model** in `record.md`

### Step 2: Apply the raise + the one guard token

- [ ] Population 1 → the shared constant. Population 2 → unchanged short literals, each with a `// wallclock-allow: <why elapsing is the subject>` marker and a pin. Population 3 → assignment deleted
- [ ] Extend `TestTimingGuardTests` with the single option-assignment token + pins for population 2 (framing f). No new fact; the floor stays 892
- [ ] **Capture the guard's RED:** inject an unpinned budget, run the guard, save the failure output as evidence, remove the injection
- [ ] Verify no stale pins after your edits (SP-062 touched `AiProviderLab.cs`); if one is stale, update it WITH the reason, never to silence the guard

### Step 3: Ten consecutive greens, at least one genuinely cold

- [ ] **10 consecutive full-suite runs, zero reds, zero unexpected skips**, TRX attached, output redirected to files (never tailed)
- [ ] **≥1 fresh-checkout, first-ever build** run ("cold" is a NEW worktree, not a rebuild; the first-ever-run property is consumed after run 1). Per-run table: run number, worktree, cold/warm, wall-clock, unit + headless counts, skipped count
- [ ] `Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` green in **every** run including the cold one
- [ ] Confirm the suite's wall-clock did not materially regress vs the Step-1 expectation (the budgets got bigger; on a green run they must not be reached)
- [ ] Run table + TRX under `spine-tasks/SP-063-timing-budgets/evidence/`

### Step 4: Record + pre-completion consult

- [ ] `record.md`: the decree quoted verbatim, the verified sweep table with confirmations/corrections, the constant + its justification, the guard token + its captured RED, the 10-run table, consults + **ACTUAL answering models**, engine-review presence per step, intended board filings (state them; set no row state)
- [ ] **Honesty cell — required, and this is the point of it:** a raised budget does not remove the time dependence, it lengthens the fuse. State plainly what remains true after this change: the cold-start class returns if any future machine/test is ~75× slower; population-2 tests still depend on wall time by design; the guard catches the option-assignment SHAPE only, not a budget expressed as a constant, a computed `TimeSpan`, or a method argument; and the deterministic alternative (removing the time dependence at the site) was set aside by owner decree, not refuted
- [ ] **Pre-completion solo consult**; verdict in `record.md`
- [ ] STATUS.md accurate before `.DONE`

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, **892 unit / 35 headless, 0 skipped**, TRX attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every injected budget that could decide a non-timing outcome now uses ONE large finite shared constant; timeout-subject budgets stay short, marked and pinned; inert assignments are gone
- `TestTimingGuardTests` carries the single option-assignment token with pins, and its bite is **demonstrated with a captured RED**
- 10 consecutive full-suite greens at 892 / 35 / 0 skipped, ≥1 fresh-checkout first-ever build, TRX committed; suite duration not materially regressed
- Zero product-code changes, zero assertion changes, zero skips
- The honesty cell states what a bigger number does not fix

## Do NOT

- Use `Timeout.InfiniteTimeSpan` (framing a — an unbounded deadline trades a rare cold flake for an unbounded hang)
- Touch `client/src/**` or any product file; change real-world timeout semantics
- Weaken, delete, or skip a test; widen a tolerance; add waits outside `TestWait`
- Raise the budgets of the timeout-subject tests (population 2) — that makes the suite slow and fixes nothing
- Re-litigate the decree (one paragraph of dissent in `record.md` is welcome; refusing to implement is not)
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs and this packet's own ignored evidence; clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes

## Git Commit Convention

- `feat(SP-063): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-063-timing-budgets/record.md`, `STATUS.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`

## Amendments

- 2026-08-12 (re-authoring, orchestrator): **owner decree received mid-batch; batch `20260812T221746` ABORTED** with the reason recorded, and this packet rewritten. The original SP-063 required a fourth timing class, a deterministic EOF/stream-close fix, and named raising the budget as the banned fix; the owner replaced that with "just increase the budgets a lot". Scope shrank accordingly: no product code, no new doctrine, one guard token instead of a class. **Two deliberate deviations from a literal reading of the decree, both defensible and both stated to the owner:** (1) the two tests whose expected outcome IS `timeout` keep short budgets — raising them fixes nothing and only makes the suite slower; (2) the raise is large but FINITE (60 s), not infinite, so a genuine hang still fails instead of wedging the test host. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-12 (re-authoring, orchestrator): the aborted run's Step-1 artifacts are preserved under `prior-step1/` — a completed 13-row sweep and two non-reproducing cold runs. They are INPUT to verify, never evidence to cite: no claim in `record.md` may rest on a run this packet did not make.
- 2026-08-12 (authoring, orchestrator): the board row's floor note says "863 / 33" — **stale**. Current floor is **892 unit / 35 headless, 0 skipped** (SP-062). A red must be identified BY NAME before it is attributed, no other red may hide behind it, and an unexpected SKIP counts as a red.
- 2026-08-12 (authoring, orchestrator): machine posture — avalonia-live MCP not connectable, avalonia-ui not connected, avalonia-docs connected; WSL zero distros (Linux is a standing named gate); no `MonitorCreate`/`LoopList` tools in the orchestrating session. This packet is headless/test-only, so none are gates for it.
