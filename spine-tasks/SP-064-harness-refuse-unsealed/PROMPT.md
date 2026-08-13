# Task: SP-064 — Harness entry points must REFUSE to run unsealed

## Mission

`CCP_DATA_ROOT` (SP-057, landed `c42d82ff`) isolates a headed evidence run **only when someone remembers to set it.** A packet that forgets writes the owner's real `%APPDATA%\CcpClient` profile exactly as before the seam existed. That is the same procedural-mitigation class as "back up before every headed run" — the rule that already failed at SP-052 and cost the owner slot-1's unrecoverable pre-run values. The seam is opt-in; this task makes it **mandatory for the launches whose only reason to exist is automated evidence capture.**

**Deliver:** harness-only entry points **fail loudly at startup when `CCP_DATA_ROOT` is unset**, with a message naming the variable; normal user launches are provably unaffected; both directions are pinned by tests; and a guard makes the classification impossible to forget when the next drive flag is added.

**Binding framings:**

(a) **Classify by resolved launch INTENT, not by flag spelling.** Build the disposition table from the tree, then decide per class:
   1. **HARNESS — must refuse.** The flag's only purpose is automated evidence capture: it scripts the app with no human at the keyboard, or fabricates/mutates persisted state for a test. Known members (**enumerate the current tree — do not assume this list is complete or correct**): `--dtrh-m2test`, `--dtrh-fx-drive`, `--intake-drive`, `--loom-drive`, `--tunnel-drive`, and the harness-only failure injectors `--dtrh-kill-renderers`, `--dtrh-block-route`, `--intake-kill-renderers` (Program.cs:110-116 already calls the first two "HARNESS-ONLY failure injection" in its own comment; nobody kills a renderer for fun, so their presence IS evidence intent).
   2. **DEMO / INSPECTION — must NOT refuse.** `--dtrh-demo`, `--loom-demo`, `--intake-demo`, `--tunnel-demo`, `--popup-demo`, `--avatartube-demo`, `--dtrh-quick`, and friends. A human may legitimately run these against their real profile; the board row explicitly bans extending the refusal to them.
   3. **PRE-PHASE SELF-CHECK — must NOT refuse.** `--verify-assets`, `--version`, `--generate-avatar-packs`, `--avatar-strip-decode`, `--avatar-sequence` return before any phase, never construct the composition root, never touch the profile (Program.cs:22-82).
   4. **MODIFIER — no independent verdict.** `--dtrh-auto-close`, `--dtrh-page`, `--dtrh-picker-timeout`, `--capture`, `--pack`, `--trace`, `--scan`, `--ai-ollama-host`, `--avatar-trace`, `--no-video-title-show`, the other `*-auto-close` flags. They cannot launch anything alone; the verdict comes from the rest of the command line.

   **Name the residual hole in `record.md` rather than silently closing it:** `--dtrh-demo --dtrh-auto-close 30` is an unattended evidence-shaped run that this gate still permits, because the row's decree protects demo flags. State it as a named limit and let the owner decide; do NOT unilaterally extend the refusal to a demo flag.

(b) **The gate must ride the REAL entry point, and it must fire BEFORE anything can write.** The highest-risk way to ship this defective is a refusal that lives in a helper the tests call but the real launch does not (or one that fires after the composition root is built, after a window opens, or after the first profile write) — a green pin over a still-broken product. Place the check in `Program.Main` immediately after the SP-057 override validation block (Program.cs:88-108) and **before** `new CompositionRoot { ... }` (Program.cs:131). On refusal: write the message to stderr naming `CompositionRoot.DataRootOverrideVariable`, return non-zero, construct no host, open no window. A **real process run** proves this, not a unit test alone (Step 3).

(c) **One registry, one guard.** Put the classification in ONE place in product code (a small static registry — suggested `Lifecycle/HarnessEntryPoints.cs`, name it as you like) that the gate consumes. Then add a guard test that enumerates every `"--..."` string literal reachable from the startup dispatch surface and **fails with the offending file:line when a literal is not classified in the registry.** Mirror `DataRootChokePointGuardTests` (same repo-root walk, same never-skip discipline, same violation-message shape). This is what makes the fix survive the next feature slice; **demonstrate the guard's bite with a captured RED** (add an unclassified flag literal, capture the failure output as evidence, remove it).

(d) **Exact-count floor is load-bearing** (SP-062, `7518c6a4`; SP-063, `10c37650`). Current floor: **892 unit / 35 headless, 0 skipped.** This packet ADDS facts, so state the new exact count in `record.md` and STATUS.md, and every run must report that same number. **An unexpected skip is a red here even though `dotnet test` exits 0.**

(e) **Never export `CCP_DATA_ROOT` process-wide for a plain suite run.** It makes the SP-057 pin skip, so the suite reports 891/1 instead of 892/0 and the count floor goes blind — the exact vacuous-green class SP-062 closed (`client/docs/port-workflow.md:204`). Set it per evidence run only, in that run's own process environment.

(f) **Do not weaken anything.** No assertion changes to existing tests, no widened tolerances, no deleted or skipped tests, no new `Task.Delay`/`Thread.Sleep` outside `TestWait`, no new timeout literal that would trip SP-063's `"Timeout = TimeSpan."` guard token without a marker + pin.

(g) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — name intended filings in `record.md`; the orchestrator reconciles at land.

## Dependencies

- SP-057 (landed `c42d82ff`) — the `CCP_DATA_ROOT` seam, `CompositionRoot.ActiveDataRootOverride()` / `ResolveDataRoot(string?)` / `DataRootOverrideVariable`, and the choke-point guard this task's guard mirrors.
- SP-063 (landed `10c37650`) — current floor and the timing-guard token discipline.

## Context to Read First

- `client/src/CcpClient.Desktop/Program.cs` — the whole 335-line dispatch: pre-phase self-checks (22-82), the SP-057 override block (88-108), harness failure injectors (110-116), the flag parsing region (157-230), composition-root construction (131+)
- `client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs` — `DataRootOverrideVariable` (:59), `ActiveDataRootOverride()`, `ResolveDataRoot`, `DefaultSettingsPath()` (the single data-root authority)
- `client/tests/CcpClient.Tests/DataRootChokePointGuardTests.cs` — the guard pattern to mirror (repo-root walk, never-skip, file:line violations)
- `client/tests/CcpClient.Tests/DataRootOverrideTests.cs` — the SP-057 pin, its `Assert.SkipWhen` checkpoints and `[Collection(nameof(ProcessEnvCollection))]` membership. **If your tests mutate process env, they belong in `ProcessEnvCollection` too** (SP-062's co-location finding: `DisableParallelization` does NOT serialize on this runner)
- `spine-tasks/SP-057-profile-isolation-seam/evidence/run.ps1` — the reusable path-hashed pre/post profile manifest + both-directions diff + positive controls
- `client/docs/port-workflow.md` §Verification floor and the `CCP_DATA_ROOT` rule at :204
- `client/docs/port-lessons.md` — 2026-08-12 entries (headed runs must set `CCP_DATA_ROOT`; cold means fresh checkout; TRX failure names)
- `docs/constitution.md` — standing orders

## File Scope

- `client/src/CcpClient.Desktop/Program.cs`
- `client/src/CcpClient.Desktop/Lifecycle/**` (the new registry)
- `client/tests/CcpClient.Tests/**`
- `spine-tasks/SP-064-harness-refuse-unsealed/**`
- **NOT in scope:** every other `client/src/**` path (feature code — the gate is a startup concern, not a per-feature one), `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**`, `client/tools/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Program.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `client/spikes/**`, `client/tools/**` |
| artifactsMustExist | `spine-tasks/SP-064-harness-refuse-unsealed/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Enumerate every entry point, classify it, design the gate

- [ ] Update STATUS.md before starting work
- [ ] Enumerate **every** `"--..."` literal reachable from startup (grep `client/src/**`, do not trust the Mission's list — it was built by the orchestrator from one grep and may be stale or incomplete). Produce the disposition table: flag, file:line, class 1-4 per framing (a), one-line reason
- [ ] For every class-1 (HARNESS) entry, say what it writes to the real profile if run unsealed. For every class-2 (DEMO) entry, say why a human running it unsealed is legitimate. Vague dispositions are the defect this table exists to prevent
- [ ] Design the gate: exact insertion point in `Program.Main`, exact refusal message text, exit code, and the registry's shape. State explicitly what happens for `--verify-assets --dtrh-m2test` (a self-check flag returns first — decide, then name the consequence in the record)
- [ ] Design the guard test: what literal surface it scans, how a new unclassified flag fails it, why it cannot skip
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback). Ask about the disposition table's boundary calls and the gate's ordering. Verdict + **ACTUAL answering model** in `record.md`

### Step 2: Implement the gate, the registry, and the guard

- [ ] One registry, consumed by the gate — no second copy of the classification anywhere
- [ ] Gate in `Program.Main` after the SP-057 override block and before composition-root construction: stderr message naming `CCP_DATA_ROOT`, non-zero exit, no host, no window
- [ ] Refusal pinned by a test that is **table-driven over the registry** (a new class-1 flag inherits the pin automatically), and the allow direction pinned for class 2/3/4
- [ ] Guard test added, mirroring `DataRootChokePointGuardTests`; **capture its RED** with an unclassified literal, save the failure output under `evidence/`, remove the injection
- [ ] If any test mutates process env, it joins `ProcessEnvCollection` (SP-062)

### Step 3: Real-process evidence — the pin is not the proof

- [ ] **(a) Refusal, real process, unsealed:** launch the built binary with a class-1 flag and `CCP_DATA_ROOT` **unset** → non-zero exit, stderr names the variable, no window appeared. Capture stdout/stderr to files
- [ ] **(b) The real profile is untouched by (a):** path-hashed manifest of `%APPDATA%\CcpClient` before and after, both-directions diff, **with the positive controls SP-057 established** (byte-identity without controls is vacuous). Commit manifests path-hashed only
- [ ] **(c) The harness path still works sealed:** same flag WITH `CCP_DATA_ROOT` set to a scratch root → not refused, run proceeds far enough to prove it (the `data-root override active:` line plus whatever the flag's own success signal is), bounded by an auto-close
- [ ] **(d) Normal launch non-regression, real process, unsealed:** a plain launch with no class-1 flag and no `CCP_DATA_ROOT` → **not refused**, window observed (rect-verified), exit 0. Report the real-profile manifest delta **honestly**: SP-010 observed a fresh profile creates no `settings.json`, so byte-identity is the expectation — if something did change, name it, never suppress it
- [ ] **3 consecutive full-suite greens**, ≥1 a fresh-checkout first-ever build ("cold" is a NEW worktree, not a rebuild in place), TRX attached, output redirected to files (never tailed). Per-run table: run, worktree, cold/warm, unit + headless counts, skipped count
- [ ] Keep this step bounded (T-11): four process runs and three suite runs, no capture matrices

### Step 4: Record + pre-completion consult

- [ ] `record.md`: the full disposition table, the gate's insertion point and message, the registry, the guard + its captured RED, all four process runs with their exit codes and profile manifests, the 3-run suite table with the **new exact floor**, consults + **ACTUAL answering models**, engine-review presence per step, intended board filings (state them; set no row state)
- [ ] **Honesty cell — required:** state what this does NOT close. At minimum: the `--dtrh-demo --dtrh-auto-close` residual hole the row's own decree leaves open; the gate protects the data root, not `%LOCALAPPDATA%`/`%TEMP%` writes by WebView2/LibVLC (outside SP-057's claim scope too); the guard binds the literal surface it scans and nothing else; a harness path invoked by something other than `Program.Main` is unprotected; Linux is unproven here (zero WSL distros on this machine — do not fake it)
- [ ] **Pre-completion solo consult**; verdict in `record.md`
- [ ] STATUS.md accurate before `.DONE`

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, **the new exact unit count / 35 headless, 0 skipped**, TRX attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every class-1 harness entry point refuses to start when `CCP_DATA_ROOT` is unset, with a message naming the variable, exiting non-zero before the composition root exists
- Normal launches, demo/inspection flags, and pre-phase self-checks are provably unaffected — pinned by tests AND by a real unsealed launch that opened a window and exited 0
- ONE registry holds the classification; a guard test fails when any startup flag literal is unclassified, and its bite is demonstrated with a captured RED
- The real profile is byte-identical across the refusal run, proven with path-hashed manifests and positive controls
- 3 consecutive full-suite greens at the stated new exact count, 0 skipped, ≥1 fresh-checkout first-ever build
- The record names the residual holes instead of implying the class is closed

## Do NOT

- Extend the refusal to demo/inspection flags or to pre-phase self-checks (the row's explicit ban) — name the residual hole instead
- Implement the gate anywhere but the real `Program.Main` startup path, or let it fire after the composition root, a window, or any profile write
- Prove the refusal with unit tests alone — a real process run is required (framing b)
- Export `CCP_DATA_ROOT` process-wide for the suite runs (framing e — it makes the SP-057 pin skip and reports a vacuous 891/1)
- Weaken, delete, or skip a test; widen a tolerance; add waits outside `TestWait`
- Touch feature code under `client/src/CcpClient.Desktop/Features/**`, `client/tools/**`, or any out-of-scope path
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`, `.pi/**`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs and this packet's own ignored evidence; clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit anything under `client/src/CcpClient.Desktop/bin/**` or other build output

## Git Commit Convention

- `feat(SP-064): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-064-harness-refuse-unsealed/record.md`, `STATUS.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `docs/constitution.md`

## Amendments

- 2026-08-13 (authoring, orchestrator): **wave 21 runs this row ALONE.** The deliverable is a suite-wide pinned enumeration of startup entry points, and every product slice in this port's history has added its own `--x-demo` / `--x-drive` flag — a parallel lane adding a flag would be green alone and RED at merge against this guard (the SP-054/SP-058 class, and the wave-19 standing rule "a row delivering a suite-wide pinned guard runs alone"). Board row 49's mechanical skip/count check is the natural successor and must land AFTER this row, since this row moves the count it would pin.
- 2026-08-13 (authoring, orchestrator): the decomposition consult (solo, Opus 5) returned **reasoning only — the final verdict text was not surfaced by the tool.** Recorded, never stitched. Its substantive guidance is carried in framings (a) intent-not-spelling + the named residual hole, (b) real-entry-point ordering + real-process proof, and (c) registry + unclassified-literal guard mirroring `DataRootChokePointGuardTests`.
- 2026-08-13 (authoring, orchestrator): floor at authoring time is **892 unit / 35 headless, 0 skipped** (SP-063, `10c37650`). This packet adds facts; the worker states the new exact count and every run must report it. A red must be identified BY NAME before it is attributed, no other red may hide behind it, and an unexpected SKIP counts as a red.
- 2026-08-13 (authoring, orchestrator): preflight raised `prelanded-file-scope` for `fileScopeMustChange = client/src/CcpClient.Desktop/Program.cs` ("already changed on main"). **Verified structural noise, not a real risk:** `client/` does not exist on `main` at all (`git ls-tree origin/main -- client` is empty — main is the still-shipping WPF product), so every client path in this repo reads as pre-landed against that baseline. The same shape shipped in waves 16-20 (SP-057 `CompositionRoot.cs`, SP-061 `Program.cs`, SP-063 `LoopbackOllamaProviderTests.cs`) with full review chains. The path is kept because the gate genuinely must land in `Program.Main`; **`fileScopeMustChange` is therefore NOT the proof of work here — Step 3's real-process evidence is.**
- 2026-08-13 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing named gate** (do not fake a Linux run); DISPLAY3 may be absent (SP-057's loud-fallback amendment applies: verify the rect and say which display); MCP seats are advisory only and this packet touches no AXAML, so the A-013 advisory step is not a gate for it. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
