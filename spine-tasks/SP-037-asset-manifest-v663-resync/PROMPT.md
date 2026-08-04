# Task: SP-037 — Reconcile asset manifest with v6.6.3 DTRH payload delta (main-sync breakage)

## Mission

Execute the `client/docs/task-board.md` P0 row **"Reconcile asset manifest with v6.6.3 DTRH payload delta (main-sync breakage)"** (OPEN, filed 2026-08-04): re-derive `client/src/CcpClient.Desktop/Assets/assets.manifest.json` against the CURRENT read-only legacy payload tree (`ConditioningControlPanel/Resources/web/dtrh/`). Merge `56f156fc` (main v6.3→v6.6.3+, inventory in `client/docs/main-sync-2026-08-04.md`) gained `CLAUDE.md`, `LOOM_PRIMER.md`, `loom.html`, `loomBoot.js` (Loom studio promotion — copied to output by the linked glob but UNMANIFESTED) and deleted `assets/bubbles/effects/spirals/sp8.gif` (manifested but source-missing). Symptom on `feat/crossplatform`: 2 red `AssetManifestTests` (`CopiedDirection_RealManifest_AllCopiedEntriesPresentCaseExact_SweepClean`, `SelfCheck_RealAssembly_ExitZero_WithPerAssetLines`), floor 464/466 + 29/29 (measured 2026-08-04 post-12.1.1). **Restore the green floor: 466/466 + 29/29.**

**Honesty framings (binding):** (a) the delta set is DERIVED EMPIRICALLY (run the failing tests and the sweep; enumerate the tree vs the manifest both directions) — the board row's file list is the hypothesis, never trusted; (b) the legacy payload tree is READ-ONLY — the delta flows FROM it, never into it; (c) the v6.6.3 BEHAVIOR deltas (Loom studio, Brain Drain rework, FX overhaul) are separate port-plan rows — this packet reconciles the CATALOGUE only, no behavior porting, no payload-content evaluation; (d) the SP-011 trust-anchor hashes (tree `40be29df`, bridge.js blob `13af3f4d`, cited in the csproj glob comment and `dtrh-admission.md` §6) PREDATE the v6.6.3 merge — re-anchoring payload trust is NOT this packet; record the observed current tree state as a session fact in record.md; (e) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (f) **WSL2 named limit:** the board acceptance says "both tests green Windows AND WSL2" — THIS machine (owner's laptop) has WSL installed with ZERO distros (`wsl -l -q` empty, exit 0, probed 2026-08-04). Provisioning a distro is an owner decision (port.txt machine-specific rule). Record the probe verbatim in record.md as the named limit; Windows evidence only; NEVER fake Linux evidence.

## Dependencies

- **None** (SP-009 machinery + SP-023 first-copied-consumer landed long ago)

## Context to Read First

- `client/docs/task-board.md` row "Reconcile asset manifest with v6.6.3 DTRH payload delta (main-sync breakage)" (the exact acceptance text)
- `client/docs/main-sync-2026-08-04.md` (the v6.3→v6.6.3+ delta inventory + merge resolution rule)
- `client/docs/asset-manifest.md` (SP-009 schema authority: logical ID, source classes, optionality, **provenance/license fields**, heads, override/trust, two-direction validation rule)
- The failing tests: `client/tests/CcpClient.Tests/AssetManifestTests.cs` (`CopiedDirection_RealManifest_AllCopiedEntriesPresentCaseExact_SweepClean` — hard-coded copied-count assertion; `SelfCheck_RealAssembly_ExitZero_WithPerAssetLines`)
- `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj:45-53` (the linked glob: legacy tree → output `payload/dtrh/`, PreserveNewest — new legacy files flow to output automatically; the manifest is the only catalogue)
- Existing dtrh entries in `assets.manifest.json` (the ID/provenance/trust convention to match — e.g. `dtrh.payload/<relpath>`)

## File Scope

- `client/src/CcpClient.Desktop/Assets/assets.manifest.json` (the re-derived catalogue)
- `client/tests/CcpClient.Tests/AssetManifestTests.cs` (copied-count assertion + comment, ONLY as demanded by the derived delta)
- `spine-tasks/SP-037-asset-manifest-v663-resync/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Assets/assets.manifest.json` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-037-asset-manifest-v663-resync/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Empirical delta sweep + re-derivation plan + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Run the two failing tests; capture the named failures verbatim (the sweep names every unmanifested file; the forward check names every source-missing entry). Enumerate the legacy tree vs the manifest BOTH directions (file listing diff, not the board row's list)
- [ ] Re-derivation plan in record.md: every add/remove decided per the SP-009 schema (ID convention, source `copied`, required, heads, override/trust per existing dtrh entries; **provenance.origin names the v6.6.3 main-sync promotion + main commits `d64860d4`/`f0c093f4` where applicable; provenance.license matches the existing dtrh-entry convention**); the copied-count assertion's new value derived from the plan, never guessed; current legacy-tree state recorded as a session fact (per honesty framing (d))
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the derived delta + plan; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Apply the re-derivation

- [ ] `assets.manifest.json`: add the derived new entries (schema-complete fields), remove the derived dead entries (`sp8.gif` expected; the empirical sweep rules)
- [ ] `AssetManifestTests.cs`: copied-count assertion + comment updated to the derived count (nothing else in the test file changes)
- [ ] Both named tests green locally; full `AssetManifestTests` class green

### Step 3: Self-check binaries + full-suite floor + evidence + pre-completion consult

- [ ] `--verify-assets` exit 0 against Debug AND Release binaries (real builds, per the row acceptance — the SP-009 self-check invocation)
- [ ] Full contract testCommand green: 466/466 + 29/29 restored (zero drift beyond the two repaired tests; any other red = halt and record)
- [ ] Write `spine-tasks/SP-037-asset-manifest-v663-resync/record.md` (derived delta, per-entry decisions, consult verdicts + ACTUAL answering models, engine-review presence, the WSL2 named-limit probe verbatim, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green; counts EXACTLY 466/466 + 29/29 — drift = red flag)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Manifest re-derived against the CURRENT legacy tree: new v6.6.3 files catalogued with schema-complete provenance/license fields, dead entries removed, spiral-pool delta per the main-sync inventory
- Both named tests green; full suite restored to 466/466 + 29/29; `--verify-assets` exit 0 Debug + Release
- WSL2 absence recorded as the named limit with the verbatim probe; NO Linux evidence faked
- record.md carries the empirical delta derivation, both solo consult verdicts with actual answering models, and engine-review presence per call

## Do NOT

- Modify `ConditioningControlPanel/**` (legacy payload tree READ-ONLY — the re-derivation flows FROM it); port v6.6.3 behavior (Loom studio / Brain Drain / FX = separate port-plan rows); re-anchor the SP-011 trust hashes; invent exclusion rules beyond the SP-009 schema's stated rules (any new exclusion = consult first, recorded); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `AGENTS.md`, `CLAUDE.md`; set any board row state; fake WSL2/Linux evidence
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-037): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-037-asset-manifest-v663-resync/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **filed from the board row after reconciliation measured the red floor (464/466 + 29/29) and both wave-4 packets' full-suite testCommands were shown to hard-block on it — SP-037 lands FIRST; SP-035/SP-036 carry a sequencing dep on this task.** Consult route per the 2026-08-04 rewire (Opus 5 main; Fable 5 fallback) — supersedes the Fable-only norm in pre-rewire packets. WSL2-absence named limit recorded in-packet (honesty framing (f)). Engine pre-launch state: global pi-spine restored to the admitted 2.10.0 + all patches (verify.mjs green both roots) after the unpinned global floated to 2.12.2. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached single-lane batch per owner cycle; 4h budget exported at launch (standing rule).
