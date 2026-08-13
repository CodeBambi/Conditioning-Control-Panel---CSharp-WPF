# Task: SP-066 — Vacuous-shape sweep, name-anchored skip pin, and the shape guard

## Mission

Board row 49 part **(2)** landed last wave (SP-065): the floor is now machinery, and an unexpected skip or an off-floor count fails the CONTRACT. Part **(1)** has never been swept, and the machinery makes that gap worse rather than better: `check-floor.mjs` pins **898 passing facts** without being able to tell whether any of them *assert anything*. A test that conditionally `return`s before its only assertion reports as **Passed** and is now **pinned as a permanent green fixture**.

**Deliver the sweep, mechanically.** Enumerate every test in both projects whose assertion can be silenced — an early `return` in a fact body, a conditional guard around its only assertion, or an environment/platform predicate — **disposition every one**, and commit the enumeration together with **the exact detector surface that produced it**. Then make the class non-recurring: a guard test that fails with `file:line` when a **new** unclassified site appears.

This row's class has recurred **four times** in the timing-discipline lineage (SP-059 → SP-063) precisely because a sweep was one-time. `TestTimingGuardTests` and `FloorWrapperGuardTests` are the precedent for the fix shape; the row's own acceptance wording — *"commit the enumeration with its cleared entries and the exact grep/analyzer surface it covered"* — is satisfied by making that surface **executable and pinned**, not by pasting a grep into a document.

**This task writes ZERO product code.** Per `client/docs/port-workflow.md` item 11 it is infrastructure-only and closes **no product capability**. `client/src/**` is in `fileScopeMustNotChange` and that is a checkable claim, not a preference.

**Binding framings:**

(a) **Re-derive the inventory yourself; the orchestrator's numbers are a starting point, not input.** A crude authoring-time scan (brace-matched `[Fact]`/`[Theory]` bodies across 87 files, 724 facts) reported: **7** bare `return;` in fact bodies, **12** platform predicates (`OperatingSystem.Is*` / `RuntimeInformation.IsOSPlatform`), **3** environment predicates (`Environment.GetEnvironmentVariable`), **48** filesystem-existence predicates, **53** facts where **every** assertion sits at nesting depth > 1 (guarded or looped), **10** facts with no assertion token at all, and exactly **1** existing `Assert.Skip` site (`DataRootOverrideTests.cs:68`, the SP-062 pin). Treat these as an **expected order of magnitude to reconcile against** — if your detector finds materially fewer, your detector is wrong; if materially more, say why. Do not copy the numbers into the record as findings.

(b) **The detector is lexical, and lexical detection of vacuity is provably incomplete. Say so, in the ledger, permanently.** Two error directions, both real on this codebase: a fact whose assertions live in a **called helper** reads as *no assertion* (false positive), and a fact whose only assertions sit inside `foreach (var x in collection)` over a **possibly-empty collection** reads as *assertions present* while being **vacuous at runtime** (false negative — and that is the exact defect class this row names). The guard is a **shape** guard. It cannot see runtime vacuity, and the record must not imply it can.

(c) **The runtime-vacuity mitigation is additive, never subtractive: `Assert.NotEmpty` (or an explicit count assertion) on the enumerated source BEFORE a loop whose body carries the only assertions.** This adds a fact to a test, weakens nothing, and converts an invisible runtime false-negative into a compile-visible one. It does **not** add a `[Fact]`, so it does not move the count. Apply it wherever a loop-only assertion shape is dispositioned as legitimate.

(d) **ORDER IS LOAD-BEARING: the floor schema change lands BEFORE any `Assert.Skip` conversion.** Converting a silenced platform test into `Assert.Skip` makes it **REPORT**, and today's pin is `skipped: 0` — so the first honest conversion turns your own contract RED. Do Step 2 before Step 3. Never resolve this by widening a count.

(e) **Expected skips are pinned by NAME, never by count** (decomposition consult verdict, and it closes SP-065's own named limit that the floor tracks *counts, not identity*). `floor.json` per project becomes `{ "total": N, "allowedSkips": [ "<fully-qualified test name>", ... ] }` with the wrapper enforcing: **zero** bad outcomes, `passed + skipped == total`, and **every** `NotExecuted` result's `testName` present in `allowedSkips`. Semantics are **"may skip"**, not "must skip" — a listed test that runs and passes on a machine where its precondition holds is green. This is strictly stronger than the current count pin (a deleted test still reddens `total`) and it is machine-portable, which a platform-conditional pin is not. The TRX result list already carries `testName`; anchor on the result list, never on `Counters` arithmetic (SP-065's finding: the arithmetic does not close over skips).

(f) **`allowedSkips` is NOT a quarantine list, and this is the single most likely way this packet ships a regression.** A test may be listed **only** when its precondition is a property of the machine or OS that **cannot be satisfied by configuration during a contract run**, and the ledger must name the machine class where it *does* execute. Two concrete bans: the **SP-057 pin** (`DataRootOverrideTests.cs:68`) must **never** be listed — its skip means someone exported `CCP_DATA_ROOT` process-wide, which is exactly the vacuous `896/1` green SP-062 closed; and the **named flake** `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` must **never** be listed, weakened, or quarantined — it guards a privacy boundary (route classes only, never a filename or query) and its board row says reproduce and fix at the source. If that flake fires during any of your runs, **record it by name with the run number and the TRX path**; never retry it away.

(g) **A vacuous test is fixed or deleted — never "made to pass".** Fixed means it is given a real, failing-when-broken assertion (state what breaks it). Deleted means the fact carried no information and the record says which behavior is consequently unverified. Weakening an existing assertion, widening a tolerance, or converting a defect into a skip to clear an entry is **banned** by the row itself. If a site's correct disposition is "this needs real work beyond this packet", record it as a **named residual with a filing intent** — do not fake-clear it.

(h) **Every count change bumps `floor.json` in the SAME commit, with the reason in the message** (SP-065 framing e, already the file's own `bumpRule`). This packet both **adds** facts (the new guard tests, the auditor pin) and may **remove** them (deleted vacuous tests). Never widen, disable, or special-case the check to make one of your own steps pass — that is the failure this row exists to prevent, reproduced by its own fix.

(i) **T-17 rides as a bounded, doc-only final step and may not expand.** `client/tools/port-audit-prompt.md:12-13` still runs a bare `dotnet test`, so the independent auditor — the one check meant to catch a lying land — retains the exact detection path SP-065 replaced. Edit **only that file** under `client/tools/`, to invoke `node client/tests/floor/check-floor.mjs` and treat a non-zero exit as an audit **FAIL** naming the wrapper's reason. **The file is inside the `.gitignore:168` bare `tools/` rule but is force-added and therefore tracked** — editing it is safe, **creating any new file under `client/tools/` is not** (it would pass in-lane and be absent from the merged tree). Prove the edit is tracked with `git ls-files`. **Do not run, and do not claim, the full auditor proof** (an induced-skip audit run) — that is beyond this packet's budget; land the edit plus a mechanical pin and name the unproven half as a residual.

(j) **Never export `CCP_DATA_ROOT` process-wide for a suite run** (`client/docs/port-workflow.md:204`). It makes the SP-057 pin skip and the floor goes blind. Induce skips only in a **scoped child-process environment**, for demonstrations.

(k) **Exact-count floor is load-bearing.** Floor at authoring: **898 unit / 35 headless / 0 skipped, build 0W/0E** (SP-065, integrate `09b4b639`). State the new exact counts in `record.md` and `STATUS.md`; every run reports them. A red must be identified **BY NAME** before it is attributed, no other red may hide behind it, and an unexpected skip counts as a red.

(l) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — name intended filings in `record.md`; the orchestrator reconciles at land.

## Dependencies

- **Task:** SP-065 (landed `09b4b639`) — `client/tests/floor/check-floor.mjs`, `floor.json`, and `FloorWrapperGuardTests`; this packet changes that wrapper's pin semantics and must keep every existing fail-closed check intact.
- SP-062 (landed `7518c6a4`) — the one live `Assert.SkipWhen` site, `ProcessEnvCollection` co-location, and the vacuous-green class this row generalizes.
- SP-063 (landed `10c37650`) — `TestWait` and the pinned-allowlist token-guard discipline (`TestTimingGuardTests`) this packet's guard mirrors.

## Context to Read First

- `client/tests/floor/check-floor.mjs` and `client/tests/floor/floor.json` — the wrapper you are extending. **Preserve every existing fail-closed check** (missing / unparseable / stale / zero-result / bad-counter / result-list-vs-Counters consistency); this packet changes the *pin* semantics only
- `client/tests/CcpClient.Tests/DataRootOverrideTests.cs` — the SP-062 pin, its `Assert.SkipWhen` checkpoint (the suite's only live dynamic skip), and `[Collection(nameof(ProcessEnvCollection))]` membership. **Any test you add that mutates process env joins that collection** — `DisableParallelization` does NOT serialize on this runner
- `client/tests/CcpClient.Tests/TestTimingGuardTests.cs` — pinned-allowlist guard discipline (path + exact string + count), and its own honest statement of what the token surface cannot see
- `client/tests/CcpClient.Tests/DataRootChokePointGuardTests.cs`, `HarnessEntryPointGuardTests.cs`, `FloorWrapperGuardTests.cs` — the guard pattern: repo-root walk, `file:line` violations, **never skips** (a missing directory is a failure, not a pass)
- `client/tests/CcpClient.Tests/TestWait.cs` — the shared wait/budget helper; do not add waits outside it
- `client/tools/port-audit-prompt.md` — lines 12-13 are the T-17 edit target (framing i)
- `.gitignore` lines **90-91, 168** — `TestResults/`, `*.trx`, and the bare `tools/` rule
- `client/docs/port-workflow.md` §Verification floor, the `CCP_DATA_ROOT` rule at :204, and item 11 (infrastructure-only tasks state that they close no product capability)
- `client/docs/port-lessons.md` — 2026-08-12 and 2026-08-13 entries (cold = a NEW worktree; merge-time gitignored-dirty tolerates nothing)
- `docs/constitution.md` — standing orders

## File Scope

- `client/tests/**` (the detector, the ledger, the guard test, the wrapper + pin changes, dispositioned test files)
- `client/tools/port-audit-prompt.md` (**this single file only** — framing i)
- `spine-tasks/SP-066-vacuous-shape-sweep/**`
- **NOT in scope:** `client/src/**` (zero product code), any other path under `client/tools/**`, `client/spikes/**`, `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` |
| fileScopeMustChange | `client/tests/floor/check-floor.mjs` |
| fileScopeMustNotChange | `client/src/**`, `ConditioningControlPanel/**`, `client/spikes/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-066-vacuous-shape-sweep/record.md` |

The `testCommand` runs the suite **through the wrapper** (`FloorWrapperGuardTests` binds every packet with ID >= SP-065 and this packet is one of them). `check-floor.mjs` is `--no-build` by design, so the explicit `dotnet build` ahead of it is **not optional** — standalone the wrapper measures the last build, not the working tree, and fails closed naming the wrong cause (SP-065 land finding).

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` >= 2 before launch.** Review Level 2 applies to every step below.

## Steps

### Step 1: Build the detector, produce the raw inventory, design the ledger and the pin schema

- [ ] Update STATUS.md before starting work
- [ ] Implement the shape detector over **both** test projects' `.cs` sources. It must classify, per `[Fact]`/`[Theory]` body, at minimum: **early `return`** reachable before an assertion; **all assertions nested** under a conditional or loop; **no assertion token** present; **platform predicate**; **environment predicate**; **filesystem-existence predicate**. Emit `file:line` + method name + shape(s) per site
- [ ] Produce the **complete raw inventory** (every site, unfiltered) into `evidence/`. Reconcile its magnitude against framing (a) and state the reconciliation
- [ ] **State the detector's error directions explicitly** (framing b) with a concrete example of each found in this codebase: one false positive (assertions in a called helper) and one false-negative shape (loop over a possibly-empty collection)
- [ ] Design the ledger: schema, one entry per site, each carrying `file:line`, method, shape, **disposition verdict**, and a one-line reason. Decide and record how the ledger is keyed so that moving a test's line number does not silently un-cover it
- [ ] Design the `floor.json` schema change per framing (e), and write down the **admission rule for `allowedSkips`** per framing (f) — including the two named bans — as text that will live in the committed file
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback). Verdict + **ACTUAL answering model** in `record.md`. **The tool has repeatedly returned reasoning-only or mid-sentence-truncated verdicts (waves 17, 21, 22, and this packet's authoring) — ask narrowly, cap the reply length, record exactly what surfaced, and never stitch a verdict out of reasoning**

### Step 2: Name-anchored skip pin in the wrapper (BEFORE any conversion)

- [ ] Change `floor.json` to the framing-(e) schema (`total` + `allowedSkips`), carrying the admission rule and the existing `bumpRule` forward in the file itself
- [ ] Change `check-floor.mjs` to enforce it: zero bad outcomes, `passed + skipped == total`, and **every** `NotExecuted` `testName` present in `allowedSkips` — failing with the **offending test name** in the message. Anchor on the TRX result list, never on `Counters` arithmetic
- [ ] **Every pre-existing fail-closed check still present and still demonstrated** (missing / unparseable / stale / zero-results / bad-counter / result-list-vs-`Counters` consistency). A table with one row per mode, its induced condition, and the observed non-zero exit
- [ ] **Both new verdicts demonstrated with captured output:** (i) a skip whose name is **not** in `allowedSkips` → **RED naming that test**; (ii) the same skip **with the name listed** → **GREEN**. Use a scoped child-process environment to induce (framing j); remove every injection afterwards and prove removal
- [ ] **`total` drift both directions → RED**, each captured. A check that only catches deletions is half a check
- [ ] Schema change and wrapper change in **one commit** (they are one semantic unit); floor bumped in that same commit if the count moved

### Step 3: Disposition every site

- [ ] Disposition **every** entry in the raw inventory — no entry left unverdicted. Verdicts: `not-vacuous` (with the reason it cannot be silenced), `platform-skip-converted` (now `Assert.Skip`, so it REPORTS), `fixed` (real assertion added — state what breaks it), `deleted` (state which behavior is consequently unverified), or `residual` (framing g — named, with filing intent, never fake-cleared)
- [ ] Apply framing (c): `Assert.NotEmpty` (or an explicit count assertion) before every loop-only assertion body dispositioned `not-vacuous`
- [ ] Any `Assert.Skip` conversion whose skip fires on this machine is added to `allowedSkips` **under the framing-(f) admission rule**, with the machine class where it does execute named in the ledger. The SP-057 pin and the named flake are **never** listed
- [ ] **Zero assertions weakened, zero tolerances widened, zero tests quarantined** — prove it: `git diff` review of every touched test file, summarized per file in the record
- [ ] Commit the ledger with its cleared entries; bump `floor.json` in the same commit as any count change, reason in the message (framing h)

### Step 4: The shape guard, and the T-17 auditor edit

- [ ] Guard test: runs the detector's surface and fails with `file:line` for any site **not** present in the committed ledger. Mirror the existing guards — repo-root walk, `file:line` violations, **never skips** (missing directory = failure)
- [ ] **Captured RED**: introduce a probe fact with a silenced assertion → guard fails naming it; save the output under `evidence/`, then remove the probe and prove removal
- [ ] The guard's own honesty: it is a **shape** guard (framing b). State in the test file and the record what it cannot see
- [ ] **T-17 (bounded, framing i):** edit `client/tools/port-audit-prompt.md:12-13` so the auditor invokes `node client/tests/floor/check-floor.mjs` and treats a non-zero exit as an audit **FAIL** naming the wrapper's reason. Add the port-workflow:204 note that the wrapper needs no `CCP_DATA_ROOT` and must never be given one
- [ ] Pin it mechanically: a test asserting `client/tools/port-audit-prompt.md` invokes the wrapper and contains **no bare `dotnet test`** invocation
- [ ] `git ls-files client/tools/port-audit-prompt.md` output pasted into the record (framing i — the file lives under a gitignore rule and is force-added; prove the edit is in the tree). **Create no new file under `client/tools/`**

### Step 5: Record + pre-completion consult

- [ ] `record.md`: the detector and its exact surface; the raw inventory reconciliation against framing (a); the ledger with per-site verdicts; the wrapper/pin schema change with both new verdicts and the preserved fail-closed table; every deletion with the behavior it leaves unverified; every residual with its filing intent; the `git ls-files` proof; the 3-run suite table with **new exact counts**; consults + **ACTUAL answering models**; engine-review presence per step; intended board filings (state them, set no row state)
- [ ] **Honesty cell — required.** At minimum, all six: (1) the detector is **lexical** — assertions in called helpers read as absent, and a loop over an empty collection reads as asserting, so **runtime vacuity is not detected** (framing b); (2) the guard binds only the shapes it enumerates — a new way to silence an assertion is invisible until someone adds it to the surface; (3) `allowedSkips` records intent, and nothing mechanically verifies that a listed test *should* be allowed to skip; (4) the ledger's per-site *reasons* are human judgment that no test checks; (5) **T-17's auditor proof is NOT delivered** — the edit and its pin are, the induced-skip audit run is not (framing i); (6) **Linux unproven** (zero WSL distros on this machine — do not fake a Linux run)
- [ ] If the named flake fired in any run, it is recorded **by name** with run number and TRX path, and was **not** retried away or listed in `allowedSkips` (framing f)
- [ ] **Pre-completion solo consult**; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`

### Step 6: Testing & Verification

- [ ] Contract testCommand passes **through the wrapper** (`verify.mjs` exit 0, build 0W/0E, the new exact unit count / 35 headless, skip set exactly as pinned)
- [ ] **3 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** ("cold" is a NEW worktree, not a rebuild in place — port-lessons 2026-08-12). Per-run table: run, worktree, cold/warm, unit + headless counts, skipped names
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows **no new ignored artifact** produced by any run (the wrapper writes outside the worktree — SP-065 framing d; do not regress it)

## Completion Criteria

- Every `[Fact]`/`[Theory]` in **both** projects matching any enumerated silencing shape is in the committed ledger with a disposition verdict and a reason — none unverdicted
- The detector that produced the ledger is **committed and executable**, and the guard fails with `file:line` on a new unclassified site, its bite demonstrated with a captured RED
- Expected skips are pinned **by name** with an admission rule in the file; a non-allowlisted skip is RED naming the test, an allowlisted one is GREEN, and `total` drift in either direction is RED — all demonstrated with captured output
- Every pre-existing `check-floor.mjs` fail-closed behavior still holds and is re-demonstrated
- Zero assertions weakened, zero tolerances widened, zero tests quarantined; every deletion names the behavior it leaves unverified
- The auditor prompt invokes the floor wrapper and contains no bare `dotnet test`, proven tracked by `git ls-files` and pinned by a test
- Zero changes under `client/src/**` — the record states plainly that this closes no product capability
- 3 consecutive full-suite greens at the stated new exact counts, >= 1 fresh-checkout first-ever build
- The record names what the sweep does NOT close, including that runtime vacuity is undetected and that T-17's auditor-run proof is not delivered

## Do NOT

- List the SP-057 pin or the named flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` in `allowedSkips`, or use `allowedSkips` as a quarantine for any flaky or failing test (framing f)
- Convert a defect into a skip, or a vacuous test into a "passing" one by weakening what it checks (framing g)
- Weaken, widen, delete, or skip any test that is **not** dispositioned in the ledger with a stated reason
- Change `check-floor.mjs`'s pin semantics without demonstrating both new verdicts, or drop any existing fail-closed check while doing it
- Convert anything to `Assert.Skip` before the Step 2 schema change lands (framing d — you will redden your own contract and be tempted to widen the pin)
- Widen, disable, or special-case the floor to make one of your own steps pass; bump without stating the reason in the same commit (framing h)
- Create any new file under `client/tools/` (`.gitignore:168` — it would pass in-lane and vanish from the merged tree), or edit any file there other than `port-audit-prompt.md`
- Claim the T-17 auditor proof (induced-skip audit run) — land the edit and the pin, name the rest as residual
- Export `CCP_DATA_ROOT` process-wide for a suite run (framing j)
- Add waits outside `TestWait`, or a timeout literal that trips SP-063's `"Timeout = TimeSpan."` guard token without a marker + pin
- Touch `client/src/**` or any other out-of-scope path
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs and this packet's own ignored evidence; clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit build output (`bin/`, `obj/`) or any results file

## Git Commit Convention

- `feat(SP-066): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-066-vacuous-shape-sweep/record.md`, `STATUS.md`, `client/tools/port-audit-prompt.md`
**Check If Affected:** `client/tests/floor/floor.json` (the admission rule text lives in the file itself)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/patches/manifest.json`

## Amendments

- 2026-08-13 (authoring, orchestrator): **wave 23 runs this row ALONE.** The standing rule ("a row delivering a suite-wide pinned guard runs alone", waves 19-22) applies twice over: this packet changes the floor's **pin semantics**, so a parallel lane would carry a different definition of green or depend on an unmerged schema, and it edits test files across the whole suite, so any lane-mate that adds or removes a test collides on `floor.json` and the exact count — green alone, RED at merge (the SP-054/SP-058 class).
- 2026-08-13 (authoring, orchestrator): **decomposition consult (solo, Opus 5).** First call surfaced reasoning only (5th occurrence of the truncation class); a narrow re-ask capped at 150 words surfaced the verdict cleanly. Verdicts, all encoded: **(1)** wave 23 = row 49 part (1) alone, with T-17 riding as a bounded doc-only final step that "may not expand beyond editing `port-audit-prompt.md:12-13`" → framing (i); **(2)** ship the guard — *"enumeration + the scanner that produced it, wired as a pinned-ledger test... four recurrences of the timing class is the evidence; `TestTimingGuardTests`/`FloorWrapperGuardTests` are the precedent, so this is the row's 'exact analyzer surface', not scope creep"* → Step 4; **(3)** option **(A)** — allowlist by fully-qualified name, `failed==0`, `passed+skipped==pinned total`, because *"TRX already carries names; it fixes SP-065's named counts-not-identity limit and is machine-portable"*, with platform-conditional pins rejected (*"encodes machine facts into a committed pin"*) and forbidding `Assert.Skip` rejected (*"contradicts the row's own acceptance"*) → framing (e); **(4)** single lane. Its four binding constraints are framings (h), (e)+Step 2, (i)/Do-NOT, and (c) respectively, plus *"if the named flake fires, record it by name; never retry it away"* → framing (f). Nothing was stitched from reasoning.
- 2026-08-13 (authoring, orchestrator): **inventory in framing (a) is orchestrator-measured, not authoritative.** Produced by a crude brace-matched scan over `client/tests/**` at authoring time; it is given as a magnitude to reconcile against precisely because a lexical scan is unreliable in both directions (framing b). The worker re-derives.
- 2026-08-13 (authoring, orchestrator): floor at authoring is **898 unit / 35 headless / 0 skipped, 0W/0E** (SP-065, `09b4b639`). This packet both adds and may remove facts; the worker states the new exact counts and every run reports them.
- 2026-08-13 (authoring, orchestrator): **Size L, deliberately not split.** Each step is bounded under a 2h worker budget and the expensive step (3) is bulk disposition of a pre-enumerated list, not open-ended investigation. The orchestrator sets `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch for headroom. Splitting was rejected because the schema change (Step 2) and the conversions (Step 3) are one semantic unit — shipping them in separate waves would leave a schema with no consumer or conversions with no pin.
- 2026-08-13 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing named gate** (do not fake a Linux run); **MCP 0/3 connected** (`avalonia-docs` / `avalonia-live` cached only, `avalonia-ui` not connected) — this packet touches no AXAML, so the A-013 advisory step is not a gate for it and MCP unavailability is a named limit, never a blocker. **`## Review Level: 2` heading present + grep-verified >= 2 (SP-034 authoring rule).**
