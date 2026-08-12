# Task: SP-057 — Profile isolation seam for headed evidence runs (APPDATA trap + m2test fixture discipline)

## Mission

Close a standing false-evidence hazard the board has already been bitten by twice. `APPDATA=` does **not** redirect .NET's `Environment.GetFolderPath(SpecialFolder.ApplicationData)` on Windows, so every headed evidence run that believed it was sandboxed has been reading and writing the **owner's real profile** — SP-052 Run A overwrote the real slot-1 index (remediated in-session, pre-run values unrecoverable). The sibling defect: `--dtrh-m2test` deep-clones the **live** slot document, so a test clone inherits whatever the real profile happens to hold — SP-052 Run B produced a confidently-wrong `dealt 7200/True` for a non-owner cell, caught only because the writer knew the domain.

Ship a **real isolation seam honored by the product's own path resolution** (harness-only), and make m2test-class clones start from a **declared fixture** instead of the live document. The interim "back up before every headed run" rule is exactly the procedural mitigation that already failed once; encode it in code instead.

**Binding framings:**
(a) **One choke point, not per-caller patches.** Grep-verified before authoring: every data-root consumer already routes through `CompositionRoot.DefaultSettingsPath()` — `DtrhParticipant.cs:57`, `DtrhProfileLock.cs:33`, `IntakeParticipant.cs:42`, and `CompositionRoot`'s own SaveSlots/loom/user-media wiring (`CompositionRoot.cs:115,122,131,185`). Re-verify the consumer set yourself (the SP-055 lesson: the inventory said two consumers, reality was three); the override belongs at the single function every caller funnels through, never sprinkled at call sites.
(b) **The proof is a byte-identical real profile, not an argument.** A headed run under the override must leave the real user data directory provably untouched — hash/manifest the real profile directory before and after a real headed run and diff it. A passing unit test is not this proof.
(c) **Harness-only, never a product feature.** The override is an environment/test seam for evidence runs; it is not user-configurable UI, not a settings field, not a migration, and it must not change default behavior on either platform (Windows `%APPDATA%\CcpClient`, Linux `$XDG_CONFIG_HOME`/`~/.config/CcpClient` — SP-010 verified the quarantine lands under `XDG_CONFIG_HOME`; do not regress it).
(d) **A seam that can silently do nothing is not a seam.** An override value that cannot be honored (relative path, unusable directory) must fail loudly and typed at startup, never fall back silently to the real profile — silent fallback reproduces the exact hazard being closed.
(e) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — the orchestrator reconciles at land.

## Dependencies

- none (runs first in wave 16; SP-058 depends on this task's seam for its headed evidence)

## Context to Read First

- `client/docs/task-board.md` — the row: "Headed DTRH evidence runs are not profile-isolated (APPDATA trap + m2test fixture discipline)" (READ-ONLY; its acceptance is this task's acceptance)
- `client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs:69-93` — `SettingsPathFactory` + `DefaultSettingsPath()`, the choke point
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhParticipant.cs:50-62`, `Features/Dtrh/DtrhProfileLock.cs`, `Features/Intake/IntakeParticipant.cs:38-60` — the `?? DefaultSettingsPath()` consumers and what they place under the data directory (`Spirals`, `assets`, `dtrh`, slot docs)
- `client/src/CcpClient.Desktop/Program.cs:88-205` — harness flag conventions, including `--dtrh-m2test` (`:166-169`) and the existing `CCP_MCP` environment precedent (`:285`)
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhMeta.cs:45,191` + `Features/Dtrh/DtrhHostWindow.axaml.cs:42` — the m2test in-memory clone path (what it clones today, and where a declared fixture must enter)
- `client/docs/persistence-migration-contract.md` — the persistence authority this seam must not violate (atomic write, quarantine, serialized writer)
- `client/docs/verification-harness.md` — the evidence tiers this seam serves

## File Scope

- `client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs`
- `client/src/CcpClient.Desktop/Program.cs`
- `client/src/CcpClient.Desktop/Features/Dtrh/**` (only what the m2test declared-fixture change requires)
- `client/tests/CcpClient.Tests/**` (new + amended tests)
- `client/tools/verify/**` (only if the named-check manifest gains an isolation check)
- `spine-tasks/SP-057-profile-isolation-seam/**`
- **NOT in scope:** `ConditioningControlPanel/**`, `client/src/CcpClient.Desktop/Features/Intake/**` (SP-058 owns it this wave), `client/spikes/**`, `.spine/**`, `client/CcpClient.sln`, the three hot docs

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/src/CcpClient.Desktop/Features/Intake/**`, `client/spikes/**`, `.spine/**`, `client/CcpClient.sln`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md` |
| artifactsMustExist | `spine-tasks/SP-057-profile-isolation-seam/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Consumer census + seam design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] **Census, do not predict:** grep the real consumer set of `DefaultSettingsPath()` / `SettingsPathFactory` / any other direct `SpecialFolder` use in `client/src/**` and record it as a table (file:line → what it places under the data root). Name anything that bypasses the choke point — a bypass is the whole failure mode
- [ ] Establish the trap as a **fact, not a quote**: a minimal proof that `APPDATA=` does not move `GetFolderPath(ApplicationData)` for this runtime on Windows (and record what it DOES do on Linux), so the row's premise is evidence in this repo
- [ ] Design the seam: environment variable name (`CCP_DATA_ROOT` unless the census argues otherwise — record the reason either way), absolute-path requirement, typed loud failure for an unusable/relative value, and where it enters so that **every** consumer inherits it. State explicitly how a future consumer that bypasses the choke point gets caught
- [ ] Design the m2test fixture discipline: what the declared fixture is (a committed fixture document, not a copy of the live doc), how `--dtrh-m2test` sources it, and what happens if it is missing (loud, never live-doc fallback)
- [ ] **Pre-approach solo consult** (`mode: "solo"`, Opus 5 main / Fable 5 fallback — bare `consult` hits the council-roster trap, T-7); verdict + ACTUAL answering model in record.md

### Step 2: Implement the seam + the fixture discipline

- [ ] Implement the data-root override at the choke point; defaults unchanged on both platforms (Windows `%APPDATA%\CcpClient`, Linux `XDG_CONFIG_HOME`/`~/.config`)
- [ ] Implement typed loud failure for an unusable override (relative path / uncreatable directory) — it must be impossible for a bad override to degrade into the real profile
- [ ] Convert the m2test clone source to the declared fixture; missing/malformed fixture = loud typed failure, never the live document
- [ ] Tests: default path unchanged per platform; override honored by **each** censused consumer (slot docs, `dtrh`, `Spirals`, `assets`, intake data dir — assert through the real composition, not a re-derived string); relative/unusable override fails typed; m2test sources the fixture and never the live doc; a guard test that fails if a new `SpecialFolder.ApplicationData` use appears outside the choke point

### Step 3: Real-profile byte-identity evidence (headed)

- [ ] Capture a pre-run manifest of the real user data directory (relative path + size + content hash per file, plus the directory's file set)
- [ ] Run a **real headed run** under the override on the owner display convention (DISPLAY3 `(-2576,1091) 2560×1440`) exercising the paths that previously wrote the real profile — at minimum a DTRH host run including the m2test mode
- [ ] Capture the post-run manifest and diff: **byte-identical** real profile, and the override root demonstrably populated (the run really did persist somewhere)
- [ ] Record the WSLg/Linux disposition honestly (run it if the environment allows; if not, name the exact gate — never fake it)
- [ ] Transcript both directions: with the override (real profile untouched) and, on a **copy** of the fixture profile only, the negative demonstration that without the override the write lands in the real location (do NOT run the negative case against the owner's live profile — reason about it from the census/trap proof if a safe demonstration is not possible, and say so)

### Step 4: Record + pre-completion consult

- [ ] Write `record.md`: consumer census table, the trap proof, seam design + rejected alternatives (backup/restore was rejected at authoring — procedural mitigation already failed once), fixture design, the byte-identity manifests/diff, consults + ACTUAL models, engine-review presence, budgets, surprises, durable-lesson candidates (this seam is now the standing rule for every headed packet)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, both suites at or above the current floor — 833 unit / 33 headless — TRX logger attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- A single data-root override, honored by the product's own path resolution, inherited by every censused consumer; defaults unchanged on Windows and Linux
- An unusable override fails loudly and typed; silent fallback to the real profile is impossible and is pinned by a test
- m2test-class clones start from a declared fixture; the live document can no longer seed a test clone
- A real headed run under the override leaves the real user data directory **byte-identical**, proven by pre/post manifests, with the override root demonstrably populated
- A guard test catches a future data-root consumer that bypasses the choke point
- Contract green; both consults persisted with actual answering models

## Do NOT

- Patch call sites individually instead of the choke point; add a user-facing setting/UI/migration for the override; change default paths on either platform; ship backup/restore as the mechanism (rejected at authoring)
- Let a bad override, a missing fixture, or an unreachable directory degrade silently
- Run the negative demonstration against the owner's live profile
- Touch `Features/Intake/**` (SP-058 owns it this wave), `ConditioningControlPanel/**`, `.spine/**`, the sln, `client/spikes/**`, or the three hot docs; set board row state
- Use `consult` council mode (T-7: solo only — a bare `consult` call errors with the stale synthesizer seat)

## Git Commit Convention

- `feat(SP-057): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-057-profile-isolation-seam/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`

## Amendments

- 2026-08-12 (authoring, orchestrator): wave-16 lane-1, filed from the wave-13 land consult's standing-hazard row. Size M. Serial with SP-058 (shared `Program.cs`; T-9/T-12 tax avoided by ordering, not parallelism). Product-side override chosen over backup/restore per the wave-16 decomposition consult (procedural mitigation already failed once at SP-052). Consumer choke point grep-verified at authoring: `CompositionRoot.cs:85` with consumers at `DtrhParticipant.cs:57`, `DtrhProfileLock.cs:33`, `IntakeParticipant.cs:42`, `CompositionRoot.cs:115,122,131,185`. Headed steps sized separately per T-11; `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch. **`## Review Level: 2` heading present.**
