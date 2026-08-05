# Task: SP-051 — ChaosSfx cue→fallback-chain audit + typed resolution

## Mission

Execute the `client/docs/task-board.md` P1 row **"Audit the WPF ChaosSfx cue→fallback-chain map against greenfield sfx resolution"** (OPEN, filed 2026-08-05): enumerate the COMPLETE cue→fallback-chain map from the WPF sources; per cue, make the greenfield resolution either resolve per the chain (file present in payload/sound content) or record it as a named content gap with its WPF chain cited; the resolution layer (`DtrhNativeEffects` + future sfx consumers) handles the audited chains typed; tests pin each resolved chain and each named gap. **User-observable hearing parity is the invariant** (owner decree 2026-08-04).

**Honesty framings (binding):** (a) **the map is complete or it's not evidence:** enumerate EVERY cue with a chain from `Services/Chaos/ChaosSfx.cs` (+ the chaos sound library content in the WPF tree) — no sampling; the table is the deliverable; (b) **named gaps are typed, never silent:** a chain whose target file doesn't exist in the greenfield content = a named content gap recorded with its WPF chain (WPF's chaos sound library is WPF-tree content — a future content row; never copied ad hoc into this slice); (c) the `boon_pick` precedent stands (SP-049: chain → `chime2.mp3`, page-supplied scale kept, test-pinned); (d) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (e) **WSL2 named limit: laptop WSL zero distros — Windows-only evidence, never faked**; (f) presence+shape logging only (cue names are stable tokens; file paths are path-class content).

## Dependencies

- **Task:** SP-049 (the boon_pick precedent + the resolution layer this audit extends)

## Context to Read First

- `client/docs/task-board.md` row "Audit the WPF ChaosSfx cue→fallback-chain map" (the acceptance text) + the corrected b3 text
- `spine-tasks/SP-049-loom-studio/record.md` (the boon_pick precedent + `Sfx_BoonPick_ChainFallsBackToChime2_KeepingPageScale`)
- WPF (READ-ONLY, `File.cs:line`): `Services/Chaos/ChaosSfx.cs` (the complete chain map + resolution logic) + the chaos sound library content in the WPF tree (file inventory)
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs` (the landed sfx pool/resolution layer — 8 payload files + the boon_pick chain)
- The payload sfx content (READ-ONLY): `ConditioningControlPanel/Resources/web/dtrh/assets/bubbles/sfx/`

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (the typed chain-resolution layer)
- `client/tests/CcpClient.Tests/Dtrh*` (chain + gap tests)
- `spine-tasks/SP-051-chaossfx-chain-audit/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**` |
| artifactsMustExist | `spine-tasks/SP-051-chaossfx-chain-audit/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Complete cue→chain map + gap classification + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Enumerate EVERY cue with a chain from `Services/Chaos/ChaosSfx.cs` (+ the WPF sound library file inventory) — the complete table with `File.cs:line` per entry
- [ ] Classify per cue: resolvable in the greenfield content (file present in payload/sound content) vs named content gap (target absent — WPF-tree content, future row)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the map + classification + resolution design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Typed chain resolution + tests

- [ ] The resolution layer handles every audited chain typed (resolvable chains resolve per WPF — the boon_pick shape: chain target + page-supplied scale kept; named gaps = typed silent no-op WITH the gap recorded, never an unrecorded drop)
- [ ] Tests pin EVERY resolved chain (exact target + scale behavior) and EVERY named gap (typed + recorded)
- [ ] The complete cue→chain table lands in record.md (the audit deliverable)

### Step 3: Evidence consolidation + pre-completion consult

- [ ] Write `spine-tasks/SP-051-chaossfx-chain-audit/record.md` (the complete map, classification, resolution design, consult verdicts + ACTUAL answering models, engine-review presence, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 629/33 floor; TRX logger attached per the template amendment)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Complete cue→chain map enumerated (every WPF chain, `File.cs:line`) and landed in record.md
- Per cue: resolved per the chain OR named content gap with its WPF chain cited (typed, never silent)
- Resolution layer handles the audited chains typed (page-supplied scale kept per the boon_pick shape)
- Tests pin every resolved chain + every named gap
- Contract green (≥629/33 floor); both solo consults persisted with actual answering models

## Do NOT

- Sample the map (complete or it's not evidence); copy WPF sound-library content into the greenfield (named gaps instead — a future content row); silently drop any cue (typed + recorded); weaken the boon_pick test; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**`; set any board row state; fake Linux evidence
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-051): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-051-chaossfx-chain-audit/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-05 (authoring, orchestrator): **board row filed at the wave-11 land (boon_pick discovery + land-consult retrospective).** Complete-map-or-not-evidence + named-gaps-typed encoded. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-05 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-050 + SP-051, 2 lanes — disjoint scopes) per owner cycle.
