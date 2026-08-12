# Task: SP-062 — Loud skip + real process-env isolation for the SP-057 pin

## Mission

The SP-057 profile-isolation pin (`DataRootOverrideTests.DefaultSettingsPath_EnvUnset_IsThePlatformDefault`) can now pass **vacuously**. SP-061 converted a race-produced false RED into a silent `return` (`DataRootOverrideTests.cs:77-87`), so the same race now produces a **false GREEN** reported as `892 passed / 0 skipped` — invisible to the worker's suite, the engine's `contract.verified` gate, and the orchestrator's merged-state verification.

Two defects, both in scope:

1. **Signal quality.** The silent `return` must become a loud skip, so a vacuous run reports `891 passed / 1 skipped` and this project's exact-count floor discipline catches it with zero new machinery.
2. **The underlying isolation.** `DataRootOverrideEnvTests` mutates **process-wide** environment state and leaks it into siblings between the pin's guard call and its `DefaultSettingsPath()` read. `[CollectionDefinition(DisableParallelization = true)]` — the serialization SP-057's design relied on — **does not serialize** under xUnit.v3 3.2.2 + the Microsoft.Testing.Platform runner (SP-061 base-reproduced, 1 in 14).

**Why this is worth a wave slot:** this pin is the SP-057 invariant whose absence let SP-052 silently overwrite the owner's real `%APPDATA%\CcpClient` profile. A silently-skipping pin is the same failure mode with better optics. It is also a **measurement instrument** — until it is trustworthy, every "10 consecutive greens" acceptance in the queue (rows 38, 49) is measuring something whose semantics are unknown.

**Binding framings:**

(a) **The two fixes are complementary, not redundant.** After the isolation fix, the skip path should be **unreachable in a normal run** (a clean run stays `892 passed / 0 skipped`). It stays as a tripwire for any future leak. Do not remove the guard and assert unconditionally — that reintroduces the false RED. Do not leave the skip as the whole fix — that leaves the isolation broken.
(b) **A skip nobody has ever seen fire is dead code.** A **positive control** is required: deliberately induce the leaked-override state and observe the run report `891 passed / 1 skipped` with the skip reason naming the variable. Record the exact command and the counts.
(c) **Enumerate, do not predict** (SP-055/SP-056 standing lesson). The sweep for other correctness dependencies on `DisableParallelization` / process-wide mutable state (environment variables, current directory, static mutable singletons, `AppContext` switches, culture) is a **discovery instrument**: commit the enumeration and its verified false positives to `record.md`, including the sites you cleared and why.
(d) **Verify the API against the pinned package, not against docs or memory** (SP-061 lesson). `Assert.Skip` / dynamic-skip support is an xUnit v3 claim — confirm it compiles and behaves against the exact pinned `xunit.v3` version in this repo, and record the version. If the pinned version cannot express a runtime skip, say so with evidence and deliver the loudest mechanism it *can* express — never a silent one.
(e) **"Cold" means a FRESH CHECKOUT, not a rebuild in place** (SP-059 standing lesson). At least one of the 10 consecutive greens must be a brand-new worktree's **first-ever build**. A rebuild-in-place run does not satisfy that cell; the first-ever-run property is consumed after run 1, so a second cold run needs a second new worktree.
(f) **A known cold-start red is NOT yours to fix.** `LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` fails on fresh-checkout first-ever-build runs (~1 in 6 measured) because its provider options carry an 800 ms `RequestTimeout` budget. That is board row "Timing discipline part 2" — a **separate row**, not this packet. If it fires: record the occurrence (run number, cold/warm, TRX name) so the row inherits real hit-rate data, re-run, and **do not touch it**. **Raising the 800 ms budget is a banned fix** (standing order inherited from SP-059). No other red may hide behind this name.
(g) **Timing discipline is standing law** (`docs/constitution.md`, SP-059): no new hard-coded deadline literals, no `Task.Delay` waits outside `TestWait`, no new injected timeout **budgets** handed to product code. If a repro harness genuinely needs a bounded wait, use the existing helper and name it in `record.md`.
(h) **Assertions are never weakened to buy a green.** If a test must change, the change must make it stricter or more precise, and `record.md` must say which and why.
(i) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — name intended filings in `record.md`; the orchestrator reconciles at land.

## Dependencies

- SP-061 (landed) — this row was filed at its land. Wave 19 runs this task **alone**: it changes what "passing" means for the whole suite, and a concurrent lane would both contaminate the 10-green scheduling-pressure measurement and race the floor count (wave-19 decomposition consult, solo).

## Context to Read First

- `client/docs/task-board.md` — the row "SP-057 pin test can pass vacuously — make the skip LOUD and fix the fixture's process-env isolation" (READ-ONLY; its four acceptance clauses are this task's acceptance)
- `client/tests/CcpClient.Tests/DataRootOverrideTests.cs` — both halves: the pin with its two-checkpoint guard (`:77-87`) and the env-mutating `DataRootOverrideEnvTests` in `ProcessEnvCollection`
- `spine-tasks/SP-057-profile-isolation-seam/record.md` — why the seam exists (SP-052 wrote the owner's real profile), the consult A1/A2 reasoning that chose `DisableParallelization`, and the positive-control discipline
- `spine-tasks/SP-061-chaos-tunnel-backdrop/record.md` §5 — the flake's root-cause diagnosis, the base reproduction (1 in 14 on the wave-18 base commit), and the inverted-direction finding
- `client/docs/port-lessons.md` — the 2026-08-12 entries on `DisableParallelization`, failure-direction, cold-means-fresh-checkout, and TRX failure-name capture
- `spine-tasks/SP-059-timing-discipline/record.md` §4 — `TestWait` and the guard's pinned-allowlist discipline any new test must satisfy
- `docs/constitution.md` — standing orders (timing discipline, no assertion weakening, honest non-claims)
- The pinned xunit packages: `client/tests/CcpClient.Tests/CcpClient.Tests.csproj` (+ `Directory.Packages.props` if present) — the exact `xunit.v3` / runner versions your API claims must be verified against

## File Scope

- `client/tests/CcpClient.Tests/**` — the pin, the env fixture, any new isolation mechanism or repro harness
- `client/tests/CcpClient.HeadlessTests/**` — only if the sweep finds a real dependency there
- `spine-tasks/SP-062-pin-skip-env-isolation/**`
- **Product code (`client/src/**`) is NOT in scope by default.** If the isolation fix genuinely requires a product seam, state the justification in `record.md` **before** touching it, keep the change additive/conditional (never a behavior change on the user path), and pin the non-regression with a test.
- **NOT in scope:** `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**`, and the row-49 site `LoopbackOllamaProviderTests.cs`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/DataRootOverrideTests.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**`, `client/tests/CcpClient.Tests/LoopbackOllamaProviderTests.cs` |
| artifactsMustExist | `spine-tasks/SP-062-pin-skip-env-isolation/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Reproduce, verify the API, design the isolation + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] **Reproduce the leak deterministically.** Build a repro harness (a scratch test or a targeted run) that makes `ActiveDataRootOverride()` non-null between the pin's two checkpoints on demand. Prove the current code's behavior in BOTH directions: with the guard, the run reports a silent pass; the leak really does cross collections on this runner. Cite the observed counts, not the theory
- [ ] **Verify the skip API against the pinned packages** (framing d): record the exact `xunit.v3` + runner versions and how you confirmed the runtime-skip mechanism exists and reports as `skipped` (compile + observed run output, not documentation)
- [ ] **Enumerate the process-wide-state dependencies** (framing c): every test/fixture whose correctness depends on `DisableParallelization`, or that mutates env vars, current directory, static mutable singletons, `AppContext` switches, or culture. Record the full enumeration with dispositions (real dependency / cleared, and why cleared)
- [ ] **Design the isolation fix** and its rejected alternatives with reasons. Whatever you choose (per-test scoping of the mutation, co-locating readers with mutators in one serialized collection, an assembly-level serialization, or a product-side seam under the Step-scope rule) must be **proven by the repro harness**, never by the runner's documented claim — that claim is already known false here. State the suite-runtime cost of the choice if it serializes anything broad
- [ ] **Pre-approach solo consult** (`mode: "solo"` — bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback); verdict + **ACTUAL answering model** in `record.md`

### Step 2: Implement — loud skip + isolation

- [ ] Replace the silent `return` at both checkpoints with a loud runtime skip whose reason names `CCP_DATA_ROOT` and the leak class. The pin's assertion when it DOES bind stays exactly as strict as it is now
- [ ] Implement the designed isolation fix so a normal run's skip path is unreachable
- [ ] Apply the sweep's findings: fix every site the enumeration marked a real dependency, or record why a site is safe left alone (a bare "looks fine" is not a disposition)
- [ ] **Positive control** (framing b): induce the leaked-override state deliberately and capture a run reporting `891 passed / 1 skipped` with the skip reason visible. Command + output excerpt in `record.md`. This proves the tripwire is live code
- [ ] No new deadline literals, no `Task.Delay` outside `TestWait`, no new injected timeout budgets; `TestTimingGuardTests` stays green with its allowlist unchanged (or the allowlist change is named with path + exact string + count)

### Step 3: Ten consecutive greens, one genuinely cold

- [ ] **10 consecutive full-suite runs, zero reds**, TRX logger attached to every run (`--logger "trx"`), output redirected to files — never tailed (SP-058 lesson: a lost failure name is folklore)
- [ ] **At least one run is a fresh checkout, first-ever build** (framing e). State per run: run number, worktree (fresh/in-place), cold/warm, wall-clock, unit + headless counts, skipped count
- [ ] Every clean run reports **892 passed / 0 skipped** unit and **35 passed** headless (the wave-18 floor). A count that is not 892/0 must be explained by name before anything else is claimed
- [ ] If the row-49 site fires (framing f): record run number + cold/warm + TRX name, re-run, do not touch it. Do not attribute any other red to it
- [ ] Attach the run table and the TRX artifacts under `spine-tasks/SP-062-pin-skip-env-isolation/evidence/`

### Step 4: Record + pre-completion consult

- [ ] Write `record.md`: the reproduction (both directions, with counts), the pinned-package API verification, the full process-wide-state enumeration with dispositions, the isolation design + rejected alternatives + measured cost, the positive control transcript, the 10-run table, named limits, consults + **ACTUAL answering models**, engine-review presence per step, and intended board filings (state them; do not set any row state)
- [ ] **Honesty cell:** state explicitly what this task does NOT prove — e.g. whether the sweep is exhaustive beyond the enumerated surface, and whether the isolation holds under a different runner/parallelism setting than the one measured
- [ ] **Pre-completion solo consult** (same route discipline); verdict text in `record.md`
- [ ] STATUS.md accurate before `.DONE`

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, **892 unit / 35 headless, 0 skipped**, TRX attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- The SP-057 pin can no longer pass vacuously: the vacuous path reports a **skip** whose reason names the variable, and the exact-count floor discipline catches it for free
- The underlying process-env leak is fixed by a mechanism **proven with a repro harness on this runner**, not by a documented guarantee already known false here
- A committed enumeration of every correctness dependency on `DisableParallelization` / process-wide mutable state, with per-site dispositions
- A positive control showing `891 passed / 1 skipped` — the skip is live code, not decoration
- 10 consecutive full-suite greens at 892/0 + 35, including at least one fresh-checkout first-ever build, with TRX artifacts committed
- Zero assertions weakened; zero new deadline literals or injected budgets; the row-49 site untouched

## Do NOT

- Delete the two-checkpoint guard and assert unconditionally (reintroduces the false RED), or leave the loud skip as the whole fix (leaves the leak)
- Weaken any assertion, delete a test, or relax the timing guard's allowlist to buy a green
- Touch `LoopbackOllamaProviderTests.cs` or raise its 800 ms budget — that is a different row, and raising it is a banned fix
- Trust `DisableParallelization`, xUnit docs, or an old comment as proof of runner behavior — reproduce it
- Claim the sweep is exhaustive without saying what surface it covered
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `ConditioningControlPanel/**`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs and this packet's own ignored evidence; clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes

## Git Commit Convention

- `feat(SP-062): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-062-pin-skip-env-isolation/record.md`, `STATUS.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`

## Amendments

- 2026-08-12 (authoring, orchestrator): wave 19, **single lane by consult decision** (solo, wave-19 decomposition gate). Rationale recorded: this row changes what "passing" means for the entire suite, so a second lane would (i) contaminate the 10-consecutive-green measurement whose whole subject is scheduling pressure, and (ii) race the exact floor count that the acceptance depends on. Rows 38 (harness refuse-unsealed) and 49 (timeout budgets) are its natural successors and both benefit from landing **after** the suite's signal is trustworthy. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-12 (authoring, orchestrator): machine posture at authoring — avalonia-live MCP **not connectable** (`fetch failed`); avalonia-docs connected (8 tools); avalonia-ui not connected; WSL zero distros (Linux gates are named limits); no `MonitorCreate`/`LoopList` tools in the orchestrating session (the batch monitor is a background pi Agent on `spine wait`). This packet is headless/test-only, so none of these are gates for it.
