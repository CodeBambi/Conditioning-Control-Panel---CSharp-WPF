# Task: SP-048 — DTRH published-artifact payload location (b1 land condition)

## Mission

Discharge the DTRH host row's oldest open land condition: **the published-artifact payload location is UNDECIDED** — the WPF-tree read-only source the boot matrix serves from does not exist in a published artifact. Decide WITH EVIDENCE where the DTRH payload lives for distribution and prove it: the published client (win-x64 self-contained single-file, SP-010 strategy) must boot the DTRH host from the published artifact (engine live, payload served from the decided location) on Windows. Linux publish evidence = named limit (WSL zero distros — owner-gated, never faked).

**Honesty framings (binding):** (a) **decide from evidence, never assume:** the csproj linked glob ALREADY copies the legacy payload tree to output AND publish dirs (`CcpClient.Desktop.csproj:50-53`, `CopyToPublishDirectory PreserveNewest` — verify empirically what a real publish actually lays down, including whether the self-contained single-file bundle sees it); the candidates are: copy-beside-exe via the existing glob (status quo — ratify and prove), embedded resources through SP-009's manifest (1538+ files — measure the size cost), first-run materialization (extraction to the user-data root); (b) **the payload trust anchor matters:** the SP-011 hashes (tree `40be29df`) predate v6.6.3; the serving location must keep the read-only, hash-verifiable discipline (the decided shape names how integrity is asserted post-SP-037's re-derivation); (c) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (d) no Wayland claims; Linux publish = named limit; (e) the publish matrix rides SP-010's gates — the decided shape must not regress any existing publish gate (fresh-profile, quarantine, data-path, logs-absence, native-deps floor, `--verify-assets`).

## Dependencies

- **Task:** SP-037 (the manifest re-derivation this serving consumes)

## Context to Read First

- `client/docs/task-board.md` row "Implement web-only DTRH host" — the b1 land condition text (published-artifact payload location UNDECIDED, consolidated named limits)
- `client/docs/dtrh-admission.md` §4 (loopback security contract — the serving discipline the location must honor) + §6 (payload trust anchor)
- `client/docs/release-publish-gates.md` (SP-010: publish strategy, artifact matrix — the gates that must not regress)
- `spine-tasks/SP-023-dtrh-host-b1/record.md` (the payload serving mechanism + the land condition's origin) + `spine-tasks/SP-010-release-publish-gates/record.md` (the publish matrix machinery)
- `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj:45-58` (the linked glob — verify what publish ACTUALLY produces; `client/tools/verify/` + publish scripts)
- `client/src/CcpClient.Desktop/Features/Dtrh/` (the loopback server + how it resolves the payload root at runtime — where does it read from today?)

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (payload root resolution + the decided location's implementation)
- `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` (publish/glob wiring if the decision changes it — per-change justification)
- `client/src/CcpClient.Desktop/Assets/**` (manifest/copy wiring if the decided shape involves it — per-change justification)
- `client/tests/CcpClient.Tests/Dtrh*` + `client/tests/CcpClient.Tests/AssetManifestTests.cs` + `client/tests/CcpClient.Tests/*Publish*` (location/publish tests)
- `client/tests/CcpClient.HeadlessTests/Dtrh*` (headless location tests where honest)
- `client/tools/**` (publish scripts if the decided shape needs them)
- `spine-tasks/SP-048-dtrh-payload-location/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**` |
| artifactsMustExist | `spine-tasks/SP-048-dtrh-payload-location/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Evidence + decision + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Empirical inventory: what a real `dotnet publish` (win-x64, SP-010 strategy) ACTUALLY lays down today (payload files present? where? does the bundle see them?); how the DTRH loopback server resolves its payload root at runtime TODAY (read the resolution code); the size/integrity trade-offs of the three candidates
- [ ] Decision in record.md: the chosen location with evidence + rejected alternatives; the integrity discipline post-SP-037 (how the payload's hash-verifiability is asserted in the decided shape)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the inventory + decision; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Implement the decided shape

- [ ] Payload root resolution honors the decided location (Debug, Release, published — each named); publish wiring if the decision changes the glob/scripts
- [ ] Tests: resolution per mode; SP-010 publish gates do not regress (fresh-profile, quarantine, data-path, logs-absence, native-deps floor, `--verify-assets` exit 0 against the published artifact)

### Step 3: Published boot proof + evidence + pre-completion consult

- [ ] **Published win-x64 artifact boots the DTRH host:** engine live against the published binary (payload served from the decided location — the §4 loopback contract green; boot evidence via avalonia-live or the existing boot-matrix harness, honestly scoped)
- [ ] `--verify-assets` exit 0 against the published artifact in the decided shape
- [ ] Write `spine-tasks/SP-048-dtrh-payload-location/record.md` (inventory, decision + rejected alternatives, consult verdicts + ACTUAL answering models, engine-review presence, publish/boot transcripts, budgets, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 601/33 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Decision recorded with evidence + rejected alternatives (never assumed)
- Published win-x64 artifact boots the DTRH host from the decided location (engine live, §4 contract green)
- SP-010 publish gates do not regress; `--verify-assets` exit 0 on the published artifact
- Integrity discipline post-SP-037 named (hash-verifiability in the decided shape)
- Linux publish = named limit (owner-gated); contract green (≥601/33 floor); both solo consults persisted with actual answering models

## Do NOT

- Assume the glob's behavior (verify publish output empirically); regress any SP-010 publish gate; fake the published boot (the published binary must really serve the engine); fake Linux publish evidence (named limit); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state; claim Wayland
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-048): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-048-dtrh-payload-location/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-05 (authoring, orchestrator): **the DTRH host row's b1 land condition — the oldest open condition in the port (named at the SP-023 land 2026-07-21; phase re-derivation consult 2026-08-05 ordered it as this wave's lane-mate).** Evidence-first decision (glob behavior verified empirically, never assumed); SP-010 publish gates must not regress. Enabler 2 (no hot docs). 4h budget exported at launch. WSL zero-distros named limit (Linux publish owner-gated). Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-05 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-047 + SP-048, 2 lanes — disjoint scopes: memory/prompt vs publish/payload) per owner cycle.
