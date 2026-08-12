# Task: SP-058 — Graded Intake v6.7.x delta (accents + AI rework + `IsAssetActive` + `TopMarksPercent`)

## Mission

SP-054 landed the Graded Intake web-core host against the **v6.6.3** upstream baseline; it was in flight when the v6.7.4 merge landed and was deliberately not retargeted mid-wave (its baseline is internally consistent). Bring the landed host up to the **v6.7.4** baseline for exactly the delta the sync ledger names, and state the new baseline in the record so the next sync has an honest starting line.

Named delta (verify each against the merged tree — the ledger is a pointer, not the authority):
- `ConditioningControlPanel/Resources/web/intake/core/accents.js` — **new, ~+350 lines**: an accents system the host must serve and provision (whatever the payload asks the host for: files, settings, bridge messages, or nothing).
- `ConditioningControlPanel/Resources/web/intake/core/ai.js` — +79/−22: an AI rework whose **host obligations** must be derived, not assumed.
- `ConditioningControlPanel/Services/Quiz/IntakeHostService.cs` — +83: `IsAssetActive` (`:783`) now gates asset enumeration (`:815`), and `TopMarksPercent = 90.0` (`:52`) drives the `perfect` verdict (`:422`).

**Binding framings:**
(a) **Derive obligations, do not port files.** For each delta element the answer is one of: host serves new payload files / host provisions data or settings / host answers a new bridge message / host stores something / **NOTHING** (payload-internal). The SP-050 obligation-table precedent is the shape; a "NOTHING" verdict with evidence is a full-credit result and is cheaper than speculative wiring.
(b) **`IsAssetActive` is already ported — consume it, do not re-implement.** SP-055 landed the one active-pool definition in `DtrhUserMedia` with upstream's semantics verbatim (normalization, empty-set short-circuit, unrelatable-path `true`, `UseAssetWhitelist` gate, skip-vs-deselect distinction, both-folders bound) and the intake media manifest was one of its three grep-verified consumers. Verify what SP-055 already wired for intake before changing anything; a second definition of active-pool semantics is a defect, not a delta.
(c) **`TopMarksPercent` is a pinned threshold with a derivation test, not a magic number.** Pin the value AND the comparison (`pct >= 90.0`, `MaxScore > 0` guard) as upstream computes it; a test that only asserts the constant is not evidence the verdict matches.
(d) **Serving is proven by the host, not by file presence.** New payload files count as served only when the running host resolves them through the real serving contract (`IntakeServingRoots` + the loopback server) — a manifest entry or a copied file is not a served file.
(e) **Use SP-057's isolation seam for every headed run.** The real profile must be byte-identical after this task's evidence runs; SP-057 lands first in this wave precisely so this task can be the seam's first consumer. Record it as consumed.
(f) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — the orchestrator reconciles at land. `client/docs/upstream-payload-inventory.json` **may** be updated if this task changes a tree's disposition or file count (SP-056's guard owns that file's schema — keep it well-formed or the guard fails).

## Dependencies

- SP-057 (profile isolation seam — this task's headed evidence runs under it; also shares `Program.cs`)

## Context to Read First

- `client/docs/task-board.md` — the row "Graded Intake v6.7.x delta (post-SP-054 …)" (READ-ONLY; its acceptance is this task's acceptance) and the P0 asset-deselection row
- `spine-tasks/SP-054-graded-intake-host/record.md` — the v6.6.3 archaeology this task re-runs (bridge vocabulary, stores, profiler, drafting sink, degraded-delivery contract)
- `spine-tasks/SP-055-asset-active-pool/record.md` — what the active-pool seam already provides and which intake consumer it wired
- `ConditioningControlPanel/Services/Quiz/IntakeHostService.cs` (READ-ONLY WPF evidence — `:52`, `:422`, `:783`, `:815` plus the surrounding +83 delta)
- `ConditioningControlPanel/Resources/web/intake/core/accents.js` and `core/ai.js` (READ-ONLY payload evidence — read for **host obligations**, not to port)
- `client/src/CcpClient.Desktop/Features/Intake/**` — the landed host (`IntakeServingRoots`, `IntakeProtocol`, `IntakeMediaManifest`, `IntakeParticipant`, `IntakeQuizRun`, `IntakePassService`)
- `client/docs/asset-manifest.md` + `client/docs/upstream-payload-inventory.json` — the serving/manifest authorities

## File Scope

- `client/src/CcpClient.Desktop/Features/Intake/**`
- `client/src/CcpClient.Desktop/Program.cs` (only if a harness flag is genuinely required — prefer none)
- `client/tests/CcpClient.Tests/**`
- `client/docs/asset-manifest.md`, `client/docs/upstream-payload-inventory.json` (only if the delta changes them; keep the SP-056 guard schema valid)
- `spine-tasks/SP-058-graded-intake-v67-delta/**`
- **NOT in scope:** `ConditioningControlPanel/**`, `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/src/CcpClient.Desktop/Lifecycle/**`, `client/spikes/**`, `.spine/**`, `client/CcpClient.sln`, the three hot docs

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `spine-tasks/SP-058-graded-intake-v67-delta/record.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/src/CcpClient.Desktop/Lifecycle/**`, `client/spikes/**`, `.spine/**`, `client/CcpClient.sln`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md` |
| artifactsMustExist | `spine-tasks/SP-058-graded-intake-v67-delta/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Delta archaeology + obligation table + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] **Enumerate the real delta** between the v6.6.3 baseline SP-054 ported and the merged v6.7.4 tree for the intake surface (payload files added/changed/removed with counts; `IntakeHostService.cs` changed members) — the ledger's list is the hypothesis, the tree is the authority (SP-037/SP-055 precedent: the predicted list was wrong both times)
- [ ] Build the **obligation table**: per delta element → SERVE / PROVISION / MESSAGE / STORE / NOTHING / BLOCKED-ON, each with a File.cs:line or payload:line citation and a sizing verdict
- [ ] Verify what SP-055 already wired for intake `IsAssetActive` (do not re-derive the semantics) and state the residual gap, if any
- [ ] **Pre-approach solo consult** (`mode: "solo"`, Opus 5 main / Fable 5 fallback — bare `consult` hits the council-roster trap, T-7); verdict + ACTUAL answering model in record.md

### Step 2: Implement the obligations

- [ ] Serve the new payload files through the real serving contract (`IntakeServingRoots` + loopback), with a typed probe/failure for a missing file — never a silent 404 that the payload masks
- [ ] Provision whatever `accents.js` requires of the host (derived in Step 1); if the verdict is NOTHING, state it and ship no code for it
- [ ] Apply the `ai.js` host obligations only (the payload's own logic is not ported)
- [ ] Wire/verify `IsAssetActive` gating for intake asset enumeration through the SP-055 definition; no second definition
- [ ] Pin `TopMarksPercent = 90.0` with the verdict derivation (`MaxScore > 0 && pct >= 90.0`), including the boundary cases (exactly 90.0, 89.99, zero max score)
- [ ] Tests for each shipped obligation; a NOTHING verdict is recorded in record.md, not tested into existence

### Step 3: Host-proven evidence (headed, under the SP-057 seam)

- [ ] Real headed intake run (`--intake-demo` + the existing drive vocabulary) on DISPLAY3 `(-2576,1091) 2560×1440`, **with the SP-057 data-root override set** — record the override as consumed
- [ ] Prove each new payload file is resolved by the running host (request/response evidence from the loopback, not a directory listing)
- [ ] Prove the top-marks verdict end-to-end at the boundary in a driven run
- [ ] Post-run: the real user data directory is byte-identical (SP-057 manifest/diff method)
- [ ] Linux/WSLg disposition recorded honestly, or the exact gate named

### Step 4: Record + pre-completion consult

- [ ] Write `record.md`: enumerated delta with counts, the obligation table with verdicts, what shipped vs what was NOTHING, the SP-055 reuse statement, the **new baseline version (v6.7.4 + merge SHA) stated explicitly**, headed evidence + byte-identity result, consults + ACTUAL models, engine-review presence, budgets, surprises, durable-lesson candidates
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, both suites at or above the floor at branch tip, TRX logger attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- The intake surface's real v6.6.3 → v6.7.4 delta is enumerated from the tree (not the ledger) with counts
- Every delta element carries a typed obligation verdict with a citation; shipped obligations are implemented and tested, NOTHING verdicts are recorded with evidence
- New payload files are proven served by the running host through the real serving contract
- `IsAssetActive` gating for intake runs through SP-055's single definition — no second definition of the semantics
- `TopMarksPercent = 90.0` is pinned with its comparison and boundary cases
- The record states the new baseline version explicitly; the real profile is byte-identical after headed evidence (SP-057 seam consumed)
- Contract green; both consults persisted with actual answering models

## Do NOT

- Port payload logic into C# because it is easy to read; invent a second `IsAssetActive`; assert the threshold constant without the verdict derivation; claim a file is served from a manifest entry or directory listing
- Retarget or re-litigate SP-054's v6.6.3 archaeology beyond the delta; expand into Goon/FYP/Her Room/Trainer Card/Haptics scope (each has its own row)
- Run headed evidence without the SP-057 override
- Touch `Features/Dtrh/**`, `Lifecycle/**`, `ConditioningControlPanel/**`, `.spine/**`, the sln, `client/spikes/**`, or the three hot docs; set board row state
- Use `consult` council mode (T-7: solo only — a bare `consult` call errors with the stale synthesizer seat)

## Git Commit Convention

- `feat(SP-058): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-058-graded-intake-v67-delta/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`

## Amendments

- 2026-08-12 (authoring, orchestrator): wave-16 lane-2, serial after SP-057 (shared `Program.cs`; wave-15's serial-lane shape). Size M. Depends on SP-057 so the seam gets its first real consumer and this task's headed runs cannot touch the owner profile. Upstream citations grep-verified at authoring: `IntakeHostService.cs:52` (`TopMarksPercent = 90.0`), `:422` (perfect verdict), `:783` (`IsAssetActive`), `:815` (enumeration gate); `Resources/web/intake/core/accents.js` present in the merged tree. Headed step sized separately per T-11; `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch. **`## Review Level: 2` heading present.**
