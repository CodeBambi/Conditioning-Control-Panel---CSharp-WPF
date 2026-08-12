# Task: SP-063 — Timing discipline part 2: injected timeout BUDGETS, not waits

## Mission

Close the **fourth occurrence** of the timing-discipline class. SP-059 converted wall-clock *waits in test bodies* and pinned them behind `TestTimingGuardTests`. It did not cover the other shape: a **budget handed to product code under test**. `LoopbackOllamaProviderTests` builds every provider with `RequestTimeout = TimeSpan.FromMilliseconds(800)` / `ProbeTimeout = 800 ms` (`LoopbackOllamaProviderTests.cs:20-27`). On a fresh-checkout first-ever build, JIT + `HttpListener` warmup exceeds 800 ms, so the provider **correctly** returns `timeout` before it ever parses the truncated body — and `Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` fails expecting `malformed-output` (`:168`). The test's subject is *classification*, but its outcome depends on *wall time*.

Three deliverables, from the board row's four acceptance clauses:

1. **A fourth class + a suite-wide sweep:** budget/deadline literals passed as options or config INTO product code under test.
2. **Guard extension:** `TestTimingGuardTests`' token alphabet grows to option-assignment timeouts under the SAME pinned-allowlist discipline (repo-relative path + exact trimmed code + expected count; unmarked → fail, marked-but-unpinned → fail, count mismatch → fail, stale pin → fail).
3. **A deterministic fix at the named site:** remove its time dependence (deterministic EOF/stream-close classification). **Raising the 800 ms budget is the banned fix** — standing order inherited from SP-059; a bigger number is the same bug with a longer fuse.

**Binding framings:**

(a) **A budget is not automatically a defect.** Where elapsed time IS the subject — the timeout-classification test, a terminal-hang tripwire — a bounded budget is correct and stays, *pinned with its reason*, exactly like SP-059's class-3 markers. The defect is a budget silently deciding the outcome of a test whose subject is something else. Your sweep must separate those two populations and say which is which per site.
(b) **Do not fix by loosening.** Raising a budget, widening a tolerance, or weakening an assertion are all banned. The fix direction is removing the time dependence.
(c) **Product code is out of scope by default.** If deterministic classification genuinely requires a product change in `Ai/LoopbackOllamaProvider*` (e.g. distinguishing "stream closed mid-document" from "deadline elapsed" at the classification seam), justify it in `record.md` BEFORE touching it, keep it additive and behavior-preserving on the user path, cite the WPF/contract behavior it must not change, and pin the non-regression with a test. A product change that alters real-world timeout semantics is a **stop-and-escalate**, not a judgement call.
(d) **The exact-count floor is now load-bearing (SP-062, landed `7518c6a4`).** The floor is **892 unit / 35 headless, 0 skipped**. If your work adds or removes facts, state the new exact numbers in `record.md` and in STATUS.md, and prove them in every run. A run reporting an unexpected **skip** is a red for this packet's purposes even though `dotnet test` exits 0 — that is precisely the signal SP-062 restored.
(e) **This row runs ALONE and that is deliberate** (wave-20 consult): it edits a suite-wide pinned allowlist keyed on path + exact string + count. A parallel lane adding tests would introduce tokens invisible to this sweep and stale this guard's counts at merge — each lane green alone, merged state red (the SP-054/SP-058 class). Do not create work that assumes a sibling lane.
(f) **Timing discipline binds you too** (`docs/constitution.md`, SP-059): no new hard-coded deadline literals, no `Task.Delay`/`Thread.Sleep` outside `TestWait`, and — the point of this packet — no new injected budgets except ones you pin with a reason.
(g) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — name intended filings in `record.md`; the orchestrator reconciles at land.

## Dependencies

- SP-062 (landed `7518c6a4`) — the suite's count discipline is trustworthy again (the SP-057 pin can no longer pass vacuously), which is what makes this packet's "10 consecutive greens at an exact count" acceptance mean something.

## Context to Read First

- `client/docs/task-board.md` — the row "Timing discipline part 2: injected timeout BUDGETS, not waits — FOURTH occurrence of the class" (READ-ONLY; its four acceptance clauses are this task's acceptance, including its floor note — but the floor has since moved to 892/35 per SP-062)
- `client/tests/CcpClient.Tests/TestTimingGuardTests.cs` — the landed guard: `ForbiddenTokens`, the `// wallclock-allow:` marker contract, the `Pins` allowlist shape, and the exemptions
- `client/tests/CcpClient.Tests/TestWait.cs` — the approved helper and its loud classifier
- `client/tests/CcpClient.Tests/LoopbackOllamaProviderTests.cs` — the named site and the shared `Provider(...)` factory that hands the 800 ms budgets to the whole class
- `client/src/CcpClient.Desktop/Ai/LoopbackOllamaProvider.cs` + its options type — where classification happens today (timeout vs malformed vs truncated), READ-ONLY unless framing (c) is satisfied
- `spine-tasks/SP-059-timing-discipline/record.md` — the three-class scheme, the sweep method, and why the guard is friction-by-design; §4 for `TestWait`
- `spine-tasks/SP-062-pin-skip-env-isolation/record.md` — the immediately preceding wave: enumeration-as-instrument, positive controls, the cold-run measurement method, and the AI lab record-ordering fix that touched `AiProviderLab.cs` (a file this sweep will revisit)
- `client/docs/port-lessons.md` — the 2026-08-12 entries on budgets-vs-waits, cold-means-fresh-checkout, TRX failure names, and record-before-observable ordering
- `docs/constitution.md` — standing orders

## File Scope

- `client/tests/CcpClient.Tests/**` (the guard, the named site, any swept site)
- `client/tests/CcpClient.HeadlessTests/**` (swept sites only)
- `client/src/CcpClient.Desktop/Ai/**` — **only** under framing (c): documented justification first, additive, non-regression pinned
- `spine-tasks/SP-063-timing-budgets/**`
- **NOT in scope:** `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**`, and any product file outside `Ai/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/TestTimingGuardTests.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**` |
| artifactsMustExist | `spine-tasks/SP-063-timing-budgets/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Reproduce cold, define the fourth class, sweep, design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] **Reproduce the named failure** on a fresh checkout, first-ever build (SP-059 standing lesson: "cold" is a new worktree, not a rebuild; the first-ever-run property is consumed after run 1). If it does not reproduce in the attempts you can afford, say so with the exact attempt count — SP-062 measured **0 firings in 23 runs incl. 3 cold builds**, so treat non-reproduction as likely and design from the MECHANISM (verified by reading the provider's classification order), never from a lucky red
- [ ] **Define the fourth class precisely** in one paragraph: what distinguishes "a budget that decides an outcome" from "a budget whose elapsing IS the subject" (framing a). This definition is the sweep's admission rule and the guard's marker vocabulary
- [ ] **Sweep suite-wide** for option/config-assigned budgets handed to product code — enumerate, do not predict (SP-055/SP-056/SP-062 discipline). Per site: file + line, the literal, which population it belongs to, and the disposition. Include the sites you CLEAR and why
- [ ] **Design** (i) the guard's token/marker extension under the existing pinned-allowlist discipline, and (ii) the deterministic fix at the named site, with rejected alternatives and reasons. If (ii) needs product code, framing (c) applies and the justification goes in `record.md` before any edit
- [ ] **Pre-approach solo consult** (`mode: "solo"` — bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback); verdict + **ACTUAL answering model** in `record.md`

### Step 2: Implement — guard extension, sweep dispositions, deterministic fix

- [ ] Extend `TestTimingGuardTests` to the fourth class: new tokens, the marker requirement, and pins carrying path + exact code + count. **Prove the guard BITES:** a deliberately-introduced unpinned budget makes the guard fail; capture that RED and then remove the injection (SP-056's red-demo discipline — a guard that has never failed on purpose is a guess)
- [ ] Apply the sweep dispositions: legitimate budgets get a marker naming WHY elapsing is the subject, plus a pin; illegitimate ones lose their time dependence
- [ ] Fix the named site deterministically. **Banned:** raising 800 ms, widening a tolerance, weakening an assertion, deleting the test
- [ ] If a stale pin exists after your edits (SP-062 touched `AiProviderLab.cs`), update it WITH the reason — never to silence the guard
- [ ] The exact-count floor: state the resulting unit/headless numbers and keep them stable across runs

### Step 3: Ten consecutive greens, at least one genuinely cold

- [ ] **10 consecutive full-suite runs, zero reds, zero unexpected skips**, TRX logger attached to every run, output redirected to files (never tailed)
- [ ] **≥1 fresh-checkout, first-ever build** run; per-run table: run number, worktree (fresh/in-place), cold/warm, wall-clock, unit + headless counts, skipped count
- [ ] The named site must be green in **every** run including the cold one — that is the fix's actual proof. If it ever fires, the fix is incomplete: say so, do not re-run until lucky
- [ ] Attach the run table and TRX artifacts under `spine-tasks/SP-063-timing-budgets/evidence/`

### Step 4: Record + pre-completion consult

- [ ] Write `record.md`: the reproduction attempt (with honest counts) and the mechanism read from the provider's classification order, the fourth-class definition, the full sweep table with dispositions and cleared sites, the guard extension + its captured RED demo, the deterministic fix + rejected alternatives, any product-code justification, the 10-run table, consults + **ACTUAL answering models**, engine-review presence per step, and intended board filings (state them; set no row state)
- [ ] **Honesty cell:** what this does NOT prove — e.g. whether the sweep's token surface can miss a budget expressed indirectly (a computed `TimeSpan`, a constant, a config file), and what a future guard would need to catch that
- [ ] **Pre-completion solo consult**; verdict text in `record.md`
- [ ] STATUS.md accurate before `.DONE`

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, the stated exact counts with **0 skipped**, TRX attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- A named fourth class with a written admission rule, and a committed suite-wide enumeration of injected budgets with per-site dispositions including cleared sites
- `TestTimingGuardTests` covers option-assignment budgets under the same pinned-allowlist discipline, and its bite is **demonstrated with a captured RED**, not asserted
- `LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` no longer depends on wall time; the budget was not raised, the assertion not weakened
- 10 consecutive full-suite greens at the stated exact counts with 0 skipped, ≥1 fresh-checkout first-ever build, TRX committed
- Any product-code change justified before the edit, additive, and non-regression-pinned — or none at all

## Do NOT

- Raise the 800 ms budget, widen a tolerance, weaken or delete an assertion, or mark the test skipped to make it stop failing
- Add new deadline literals or unpinned budgets; edit the guard's allowlist to silence a site instead of dispositioning it
- Claim the sweep is exhaustive without naming the surface it covered (framing on indirection belongs in the honesty cell)
- Touch product code outside `Ai/**`, or change real-world timeout semantics on the user path (stop and escalate instead)
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `ConditioningControlPanel/**`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs and this packet's own ignored evidence; clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes

## Git Commit Convention

- `feat(SP-063): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-063-timing-budgets/record.md`, `STATUS.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`

## Amendments

- 2026-08-12 (authoring, orchestrator): wave 20, **single lane by consult decision** (solo, wave-19 land consult). Rationale: this row edits a suite-wide pinned allowlist keyed on path + exact string + count, and it moves the floor; any parallel lane adding tests would both introduce tokens this sweep never saw and stale the pins at merge time — each lane green alone, merged state red. Standing rule extracted: **a row delivering a suite-wide pinned guard runs alone.** Board row 38 (harness entry points refuse-unsealed) is the wave-21 successor and benefits from being written UNDER this guard, the same sequencing that put SP-061 after SP-059. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-12 (authoring, orchestrator): the board row's floor note says "863 / 33" — **stale**. Current floor is **892 unit / 35 headless, 0 skipped** (SP-062, landed `7518c6a4`). The row's rule still stands in its stronger form: a red must be identified BY NAME before it is attributed, no other red may hide behind it, and an unexpected SKIP now counts as a red for this packet.
- 2026-08-12 (authoring, orchestrator): machine posture — avalonia-live MCP not connectable, avalonia-ui not connected, avalonia-docs connected; WSL zero distros (Linux is a standing named gate); no `MonitorCreate`/`LoopList` tools in the orchestrating session. This packet is headless/test-only, so none are gates for it.
