# Task: SP-065 — Mechanical skip/count detection that fails the CONTRACT

## Mission

`dotnet test` exits **0** on `891 passed / 1 skipped`. Every count discipline this port runs on — the exact-count floor, "an unexpected skip is a red", "state the new exact count" — is therefore enforced by a human reading two numbers off a console. That is the same detection path that let the **wave-18 land ship a RED base**, and the same one SP-062 had to repair after a pin started passing vacuously. Board row 49 says it plainly: *"Detection is discipline, not machinery."*

**Deliver the machinery.** A mechanical check that **fails the contract** — not a human read — when the suite reports an unexpected skip or an off-floor count, demonstrated **failing on an induced skip and passing on a clean run**.

This is **board row 49 part (2) ONLY.** Part (1) (the vacuous-SHAPE enumeration sweep across both test projects) stays on the board and is explicitly **out of scope** — the row itself says do not let (1) delay (2).

**This task writes ZERO product code.** Per `client/docs/port-workflow.md` item 11: it is infrastructure-only and closes no product capability. `client/src/**` is in `fileScopeMustNotChange` and that is a checkable claim, not a preference.

**Binding framings:**

(a) **Probe the runner-native options FIRST, empirically, and record what came back verbatim.** The row says *"a runner flag verified against the pinned packages (do not assume one exists)"* — that cuts both ways: do not assume one exists, and do not assume one does not. The pinned stack is `xunit.v3 3.2.2` + `Microsoft.NET.Test.Sdk 17.10.0` + `xunit.runner.visualstudio 3.1.5` in **both** projects, with no runsettings and no `xunit.runner.json` anywhere, i.e. VSTest mode. Probe at minimum: `dotnet test --help` on this stack, xunit v3's `failSkips` (configuration file and/or runsettings), and Microsoft.Testing.Platform's `--minimum-expected-tests` — **stating whether MTP is even reachable in this configuration.** Record the exact invocation and the exact response for each.

   **A runner flag can be a component, never the whole answer.** The general form this row needs is an **EXACT pin on the skipped count**, not "skips are failures": part (1) of this same row prescribes converting silent `return`s into `Assert.Skip` **so that they REPORT**, and a legitimate platform skip must not become a failure. `failSkips` cannot express *"exactly N expected skips"*. Whatever you pick must still be able to say 0 today and N later.

(b) **The check must catch the ZERO-RESULTS case.** This is why an assembly-teardown assertion is not sufficient on its own: it cannot fire when the assembly never runs, when a filter excludes everything, or when the run dies before teardown — the exact shapes where "no tests ran" reads as success. Results post-processing sees the absence. **Fail closed** on every one of: results file missing; results file unparseable; results file **stale from a previous run**; zero results; more or fewer result files than expected; a project that did not produce results at all.

(c) **Do NOT put the wrapper under `client/tools/`.** `.gitignore:168` is a bare `tools/` rule that ignores `client/tools/` — the 117 files already living there are **force-added**. A new script written there would pass the contract inside the lane (the file is on disk) and be **absent from the merged tree**: the mechanism would silently not exist. Verified not-ignored and therefore safe: `client/tests/**`. **Prove tracking with `git ls-files <path>` after the commit** — presence on disk is not presence in the tree.

(d) **Results must not land inside the worktree.** Both `*.trx` (`.gitignore:91`) and `[Tt]est[Rr]esult*/` (`.gitignore:90`) are ignored, and the **merge-time gitignored-dirty check tolerates NOTHING** (port-lessons 2026-08-13, SP-064 land: the lane must reach literally zero ignored-dirty entries or it is unmergeable). A wrapper that emits results into the worktree on every contract run would make **every future lane unmergeable-until-cleaned by construction** — converting SP-064's one-off recovery into a permanent tax on every land. Write results **outside the worktree** (a run-scoped temp directory) and print the path so evidence can hash them. Prove it: `git status --porcelain --ignored=matching -uall` gains **no new entry** from a wrapper run.

(e) **Bootstrap circularity — the most likely way a weakened check ships.** The moment an exact pin is wired into this packet's own `testCommand`, every later step that adds a test turns the contract RED. Handle it deliberately: commit the exact pin **last**, or make *"bump the pin and state why, in the same commit"* a mandatory sub-step of every count-changing step. **Never widen, disable, or special-case the check to make one of your own steps pass** — that is the failure this row exists to prevent, reproduced by its own fix.

(f) **Close the half-install with a GUARD TEST, not with discipline.** The mechanism binds only invocations that route through it. The packet TEMPLATE that would make future packets inherit it lives in `.spine/patches/manifest.json`, which stays **out of this packet's scope** (standing rule: `.spine/**` is not worker-writable; policy-touching text is applied by the orchestrator at land — SP-059 precedent). Holding that line is correct, so make the omission **impossible to miss at the point of harm**: a guard test that walks `spine-tasks/*/PROMPT.md` and, for every packet whose task ID is **>= SP-065**, fails with `file:line` when that packet's `testCommand` invokes `dotnet test` without routing through the wrapper. Grandfather older packets by that explicit **ID rule, never by a suppression list**. Mirror `DataRootChokePointGuardTests` / `HarnessEntryPointGuardTests`: repo-root walk, `file:line` violation messages, **never skips** (a missing `spine-tasks/` directory is a failure, not a pass). If the orchestrator forgets the template at land, the **next** packet's lane goes red on its own contract — the right place to catch it, mechanically.

(g) **Exact-count floor is load-bearing** (SP-062 `7518c6a4`, SP-063 `10c37650`, SP-064 `e8eab7c1`). Floor at authoring: **897 unit / 35 headless / 0 skipped, build 0W/0E**. This packet ADDS facts — state the new exact counts in `record.md` and `STATUS.md`, and every run must report them.

(h) **Never export `CCP_DATA_ROOT` process-wide for a suite run.** It makes the SP-057 pin skip, so the suite reports `896/1` instead of `897/0` and the floor goes blind — the exact vacuous-green class SP-062 closed (`client/docs/port-workflow.md:204`). **Note the interaction specific to this packet: that pin is the one live `Assert.SkipWhen` site in the suite, so it is also the cheapest honest way to INDUCE the skip your check must go red on.** Induce it in a scoped child-process environment for the demonstration only; never in the contract run.

(i) **Do not weaken anything.** No assertion changes to existing tests, no widened tolerances, no deleted or skipped tests, no new `Task.Delay`/`Thread.Sleep` outside `TestWait`, no new timeout literal that trips SP-063's `"Timeout = TimeSpan."` guard token without a marker + pin.

(j) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — name intended filings in `record.md`; the orchestrator reconciles at land.

## Dependencies

- SP-062 (landed `7518c6a4`) — the loud `Assert.SkipWhen` sites, `ProcessEnvCollection` co-location, and the `891 passed / 1 skipped` positive control this row generalizes.
- SP-063 (landed `10c37650`) — `TestWait` and the timing-guard token discipline.
- SP-064 (landed `e8eab7c1`) — the current floor (897/35/0) and the guard-test shape to mirror.

## Context to Read First

- `client/tests/CcpClient.Tests/DataRootChokePointGuardTests.cs` and `client/tests/CcpClient.Tests/HarnessEntryPointGuardTests.cs` — the guard pattern (repo-root walk, never-skip, `file:line` violations)
- `client/tests/CcpClient.Tests/DataRootOverrideTests.cs` — the SP-062 pin, its `Assert.SkipWhen` checkpoints, and `[Collection(nameof(ProcessEnvCollection))]` membership. **If any test you add mutates process env, it joins that collection** (SP-062's finding: `DisableParallelization` does NOT serialize on this runner)
- `client/tests/CcpClient.Tests/CcpClient.Tests.csproj` and `client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj` — the exact pinned package set you are probing against
- `.gitignore` lines **27-28, 90-91, 168** — the four traps in framings (c) and (d)
- `spine-tasks/SP-064-harness-refuse-unsealed/PROMPT.md` — the `## Contract` table shape your guard parses (`| testCommand | \`...\` |`)
- `client/docs/port-lessons.md` — the 2026-08-13 entries (merge-time gitignored-dirty tolerates nothing; `*.trx` never actually committed) and the 2026-08-12 entries
- `client/docs/port-workflow.md` §Verification floor, the `CCP_DATA_ROOT` rule at :204, and item 11 (infrastructure-only tasks state that they close no product capability)
- `docs/constitution.md` — standing orders

## File Scope

- `client/tests/**` (the wrapper, the pin file, the guard test, any supporting test)
- `spine-tasks/SP-065-test-floor-contract-check/**`
- **NOT in scope:** `client/src/**` (zero product code — this is a test-infrastructure row), `client/tools/**` (the `.gitignore:168` trap AND out of scope), `client/spikes/**`, `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` |
| fileScopeMustChange | `client/tests/floor/check-floor.mjs` |
| fileScopeMustNotChange | `client/src/**`, `client/tools/**`, `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**` |
| artifactsMustExist | `spine-tasks/SP-065-test-floor-contract-check/record.md` |

**The wrapper path is pinned by the contract**: it must be exactly `client/tests/floor/check-floor.mjs`, because the `testCommand` above names it and that command IS the gate. Node is already a hard dependency of every contract run (`node .spine/patches/verify.mjs`), and `node --version` on this machine is v24.5.0. The wrapper owns **both** `dotnet test` invocations — the contract no longer calls them directly.

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` >= 2 before launch.** Review Level 2 applies to every step below.

## Steps

### Step 1: Probe the runner surface, choose the mechanism, design the guard

- [ ] Update STATUS.md before starting work
- [ ] **Probe, do not assume** (framing a). For each of: `dotnet test --help` on this stack, xunit v3 `failSkips` (config file and/or runsettings), MTP `--minimum-expected-tests` (and whether MTP is reachable at all here) — record the **exact invocation and exact response**. A probe that errors is a result; write down what it said
- [ ] Choose the mechanism and justify it against the two rejected alternatives **on their real failure modes**, not on taste: what a runner flag cannot express (exactly-N skips, framing a) and what an assembly-teardown assertion cannot see (zero results / never ran / filtered out, framing b)
- [ ] Design the wrapper: how it invokes both projects, where results land (**outside the worktree**, framing d), the complete fail-closed list from framing (b), its exit codes, and the summary line it prints on success
- [ ] Design the pin file: location (not-ignored, framing c), schema, per-project `passed` / `skipped` expectations, and exactly what a bump requires of the person doing it
- [ ] Design the half-install guard (framing f): what it walks, the `>= SP-065` ID rule, the `file:line` violation shape, and why it cannot skip
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback). Verdict + **ACTUAL answering model** in `record.md`. **The tool has returned reasoning-only or mid-sentence-truncated verdicts on several recent calls (waves 17, 21, and this packet's own authoring consults) — record exactly what surfaced and never stitch a verdict from reasoning**

### Step 2: Implement the wrapper and the pin, and prove BOTH verdicts

- [ ] Wrapper at exactly `client/tests/floor/check-floor.mjs`; pin file beside it. Both **tracked**, proven by `git ls-files` output pasted into `record.md` (framing c — disk presence is not tree presence)
- [ ] Results written outside the worktree; prove `git status --porcelain --ignored=matching -uall` gains **no new entry** after a wrapper run (framing d)
- [ ] Every fail-closed behavior from framing (b) implemented, and **each one demonstrated** — a table with one row per failure mode, its induced condition, and the observed non-zero exit
- [ ] **Both verdicts demonstrated, which is the board's own acceptance wording:** (i) **induced skip -> RED**, output captured to `evidence/`; (ii) **clean run -> GREEN**. Use the SP-057 pin as the cheapest honest skip source (framing h), induced in a **scoped child-process environment only**
- [ ] **Induced count drift both directions -> RED**: one added fact, one removed fact, each captured. A check that only catches deletions is half a check
- [ ] Every injection removed afterwards, and **proven removed** (`git status` + a clean run)
- [ ] Pin bumped in the same commit as any count change, with the reason stated (framing e)

### Step 3: The half-install guard

- [ ] Guard test implemented per framing (f): walks `spine-tasks/*/PROMPT.md`, enforces for task IDs `>= SP-065`, fails with `file:line`, **never skips** (missing directory = failure)
- [ ] **Captured RED**: a probe packet directory whose `testCommand` calls `dotnet test` directly -> guard fails naming it; save the failure output under `evidence/`, then delete the probe and prove deletion
- [ ] This packet's own `PROMPT.md` passes the guard (self-binding — the mechanism proves itself on itself)
- [ ] Confirm the guard does **not** fire on a packet whose `testCommand` legitimately runs no tests at all, and say so in the record

### Step 4: Record + pre-completion consult

- [ ] `record.md`: probe results verbatim; mechanism choice + both rejections on their failure modes; wrapper design; the fail-closed demonstration table; the pin file with its **new exact numbers**; both-verdict demonstrations; count-drift demonstrations; the guard + its captured RED; the `git ls-files` tracking proof; the worktree-cleanliness proof; the 3-run suite table; consults + **ACTUAL answering models**; engine-review presence per step; intended board filings (state them, set no row state)
- [ ] **Honesty cell — required.** At minimum, all six: (1) it detects an off-floor count but **does not name which test vanished**; (2) it binds only invocations routed through the wrapper — a bare `dotnet test` run by a human still exits 0 on an unexpected skip; (3) **it cannot prove the pinned number is the RIGHT number** — a pin bumped in the same commit as a bad or vacuous test is blessed by the mechanism, so this replaces *"a human must compare numbers"* with *"a human must justify a bump"*, which is better but is **not verification**; (4) it does nothing for part (1)'s vacuous-SHAPE sweep — a test that asserts nothing and reports as passed stays invisible to it; (5) the template that makes future packets inherit the wrapper is an **orchestrator land action**, and the guard catches its omission only at the next packet's lane, not before; (6) **Linux unproven** (zero WSL distros on this machine — do not fake a Linux run)
- [ ] **Pre-completion solo consult**; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`

### Step 5: Testing & Verification

- [ ] Contract testCommand passes **through the wrapper** (verify.mjs exit 0, build 0W/0E, the new exact unit count / 35 headless, 0 skipped)
- [ ] **3 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** ("cold" is a NEW worktree, not a rebuild in place — port-lessons 2026-08-12). Per-run table: run, worktree, cold/warm, unit + headless counts, skipped count
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact produced by the wrapper (framing d)

## Completion Criteria

- The contract **fails** when the suite reports an unexpected skip, and **fails** when the passed count drifts in either direction — both demonstrated with captured output, and the clean run demonstrated green
- The check fails closed on missing, unparseable, stale, empty, and absent results — each demonstrated
- The wrapper and pin are **tracked in git** (`git ls-files` proof), not merely present on disk, and live outside `client/tools/`
- A wrapper run leaves **zero** new gitignored-dirty entries in the worktree
- A guard test fails with `file:line` when a packet with ID >= SP-065 invokes `dotnet test` outside the wrapper, its bite demonstrated with a captured RED, and it never skips
- Zero changes under `client/src/**` — the record states plainly that this closes no product capability
- 3 consecutive full-suite greens at the stated new exact counts, 0 skipped, >= 1 fresh-checkout first-ever build
- The record names what the mechanism does NOT close, including that it cannot judge whether the pinned number is the right one

## Do NOT

- Do part (1) of board row 49 (the vacuous-SHAPE enumeration sweep) — out of scope by the row's own sizing note
- Write the wrapper under `client/tools/` (gitignored by `.gitignore:168` — it would vanish from the merged tree), or claim it is committed without `git ls-files` proof
- Let the wrapper emit results **inside** the worktree (`*.trx` and `TestResults/` are ignored, and the merge-time check tolerates nothing — you would tax every future land)
- Weaken, widen, disable, or special-case the check to make one of your own steps pass (framing e)
- Turn legitimate skips into failures as the whole mechanism — the row's part (1) needs skips to REPORT; pin the skip count exactly instead (framing a)
- Weaken, delete, or skip any existing test; widen a tolerance; add waits outside `TestWait`
- Export `CCP_DATA_ROOT` process-wide for a suite run (framing h — it makes the SP-057 pin skip and reports a vacuous 896/1)
- Touch `client/src/**`, `client/tools/**`, or any other out-of-scope path
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs and this packet's own ignored evidence; clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit build output (`bin/`, `obj/`) or any results file

## Git Commit Convention

- `feat(SP-065): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-065-test-floor-contract-check/record.md`, `STATUS.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/patches/manifest.json`

## Amendments

- 2026-08-13 (authoring, orchestrator): **wave 22 runs this row ALONE.** The standing rule ("a row delivering a suite-wide pinned guard runs alone", waves 19-21) applies, and the decomposition consult added a sharper reason: **this row changes what "the contract passing" MEANS for every lane in the batch.** Two lanes would carry two different definitions of green, or lane-2's contract would depend on lane-1's not-yet-merged wrapper. Every candidate lane-mate (part (1)'s sweep, any product row) also moves the exact number this row pins — green alone, RED at merge (the SP-054/SP-058 class).
- 2026-08-13 (authoring, orchestrator): **consult provenance.** Three solo Opus-5 calls. Call 1 returned **reasoning only** (no verdict text surfaced). Call 2 returned **one line then truncated** mid-sentence — enough to fix Q-A: *"hold the scope line, and close the half-install with a guard test, not with discipline"*, which is framing (f). Call 3 surfaced the three corrections now encoded as framings **(c)** `client/tools/` is gitignored, **(d)** results inside the worktree tax every future land, **(e)** bootstrap circularity, plus the honesty-cell item that the mechanism cannot prove the pinned number is the right one. Nothing was stitched from reasoning. **Corrections (c) and (d) were re-verified empirically by the orchestrator before encoding** — `git check-ignore -v client/tools/newthing.ps1` returns `.gitignore:168:tools/`, `client/tests/**` is not ignored, and `TestResults/` matches `.gitignore:90`.
- 2026-08-13 (authoring, orchestrator): floor at authoring is **897 unit / 35 headless / 0 skipped, 0W/0E** (SP-064, `e8eab7c1`). This packet adds facts; the worker states the new exact counts and every run must report them. A red must be identified BY NAME before it is attributed, no other red may hide behind it, and an unexpected SKIP counts as a red.
- 2026-08-13 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing named gate** (do not fake a Linux run); **MCP 0/3 connected** (`avalonia-docs` and `avalonia-live` cached only, `avalonia-ui` not connected) — this packet touches no AXAML, so the A-013 advisory step is not a gate for it and MCP unavailability is a named limit, never a blocker. **`## Review Level: 2` heading present + grep-verified >= 2 (SP-034 authoring rule).**
