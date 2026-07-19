# Task: SP-009 — define asset and packaged-output manifest

## Mission

Execute `client/docs/task-board.md` row 8 (**"Define asset and packaged-output manifest"**, P0, Phase 1 of `spine-tasks/CONTEXT.md`). Deliver `client/docs/asset-manifest.md` (contract) plus ONE JSON asset catalogue with a **`--verify-assets` diagnostic self-check mode** on the real binary and two-direction validation tests. The catalogue schema covers: logical IDs, case-sensitive paths, source kinds (embedded/copied/user/mod), override policy + trust, optionality, provenance/license, target heads. **Schema covers user/mod/override/trust; instances do NOT** — no mod loader, no runtime override resolution, no copied WPF localization JSONs (no consumer; A-014). The first slice has exactly ONE required asset (`demo-status-ticker.png`); a manifest with one real entry and a validated schema is honest. Debug AND Release packaged asset-open runs on Windows AND WSL2 close the decidable acceptance; **publish-mode is a named deferred hook for row 9** (publish strategy is row 9's decision — a throwaway publish against a mode row 9 may reject is misleading evidence, same class as the Wayland gate; annotate-don't-rewrite).

## Dependencies

- **Task:** SP-007 (the one real asset + its consumer), SP-008 (harness discipline: measured budgets, named checks, throwaway-edit pattern)

## Context to Read First

- `client/docs/task-board.md` row 8 + gate history
- `client/docs/architecture.md` — A-014 (Release rule: Debug/Release/published artifacts are separate gates; YAGNI), A-012
- `client/docs/architecture-proposal.md` — §6 (asset/localization manifest = this row; publish strategy = row 9's decision)
- `client/docs/first-attempt-systemic-lessons.md` — asset/packaging lessons (missing packaged assets, assets-present-means-supported)
- `spine-tasks/SP-008-verification-harness/record.md` — budget-measurement discipline (verify cold precondition), named-check pattern
- `client/docs/runtime-capability-contract.md` — honesty/evidence-class discipline
- Required skills: load `port-feature`, `avalonia-research` before Step 1

## File Scope

- `client/src/CcpClient.Desktop/**` (`--verify-assets` self-check mode; manifest resource wiring)
- `client/src/CcpClient.Desktop/Assets/assets.manifest.json` (the catalogue)
- `client/tests/CcpClient.Tests/**` (two-direction validation tests)
- `client/docs/asset-manifest.md` (contract deliverable)
- `client/docs/task-board.md` (row-8 evidence edit only)
- `spine-tasks/SP-009-asset-manifest/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/asset-manifest.md`, `client/src/CcpClient.Desktop/Assets/assets.manifest.json` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/asset-manifest.md`, `spine-tasks/SP-009-asset-manifest/record.md` |

**Review Level 2 (plan + code).** Call `spine_review_step` after each step. Engine reviews are empirically dead (zero in SP-001…SP-008 — T-2 open); if skipped, record it and rely on the mandatory Fable consults. Do not stall.

## Steps

### Step 1: Pre-approach consult, v12 research, contract draft

- [ ] **Pre-approach solo consult** (Fable 5, solo; council is owner-sanctioned but its route is broken — T-7) with the planned schema, two-direction validation, and self-check design; verdict text in record.md BEFORE checkbox. Keep questions few/pointed
- [ ] Update STATUS.md before starting work
- [ ] v12 research (avalonia-research): **verify how `AssetLoader` behaves in Avalonia 12.1** — whether it needs platform/app initialization to open `avares://` resources (decides whether the self-check runs inside the app's lifetime or standalone), asset embedding mechanics in the csproj, and how single-file publish changes asset resolution (for the row-9 hook writeup — research only, NOT implementation). Record URL + freshness
- [ ] Write `client/docs/asset-manifest.md`: schema (per-asset: logical ID, source-kind embedded/copied/user/mod, case-sensitive path, required/optional, provenance/license, target heads); override policy + trust rule for non-embedded sources (STATED POLICY, unimplemented — no consumer); the two-direction validation rule; the `--verify-assets` self-check contract (open every required manifest asset in the real app context, exit 0, one diagnostic line per failure, non-zero exit); the row-9 publish hook (same self-check against the published artifact — one new invocation, zero new test logic); evidence-class note (Debug/Release runs here; publish evidence is row 9's named gate)

### Step 2: Catalogue and validation tests

- [ ] `Assets/assets.manifest.json`: schema-valid catalogue with the ONE real entry (`demo-status-ticker.png`, embedded, required, provenance recorded); ship it as an embedded `avares://` resource so the self-check reads it through the same mechanism it validates (decide self-listing honestly — the manifest itself is an asset)
- [ ] **Two-direction validation tests** (in `CcpClient.Tests`): (a) every required manifest entry opens via `AssetLoader`; (b) **completeness sweep** — enumerate actual embedded `avares` resources + copied output asset files and FAIL on any asset not in the manifest (drift protection); (c) **case-exactness named check** — manifest path matches the actual resource/file name case-EXACTLY (ordinal comparison) even on Windows (this is the row's highest-value assertion: works-on-Windows/breaks-on-ext4)
- [ ] Schema-validation tests: user/mod/copied entries validate against the schema (instances optional/absent is fine — fields validated, loading unimplemented, recorded)

### Step 3: `--verify-assets` self-check mode

- [ ] Implement the diagnostic flag on the real binary (single bounded path, not a framework): opens every required manifest asset in the REAL app context, prints one diagnostic line per failure, exit 0/non-zero; no side effects (no window, no startup of participants beyond what asset opening needs — respect the SP-003 phase discipline)
- [ ] Tests: the self-check path is exercised from unit tests where possible (manifest parse + per-asset open outcome mapping); the real-binary runs are Step 4
- [ ] **Never** let the flag weaken startup invariants (teardown, phases, no constructor-started work)

### Step 4: Debug+Release output runs, WSL2 gate, budgets

- [ ] Run `--verify-assets` against **Debug and Release build output on Windows** — real binaries, exit 0 recorded
- [ ] **WSL2 gate (in-packet, SP-005/007/008 pattern):** native-dir copy, full contract testCommand green, AND `--verify-assets` against Debug AND Release Linux binaries — the case-exactness check is meaningful here (ext4); record results + session facts
- [ ] **Measured budgets** (SP-008 discipline; verify the cold precondition): time the self-check + validation tests on both platforms, cold vs incremental; record actuals in the contract doc
- [ ] Integration proof: the harness's tier-2 can invoke the self-check (document the command in `verification-harness.md` — one-line addition IF it belongs there; otherwise keep in asset-manifest.md, say which and why in record.md)

### Step 5: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-009-asset-manifest/record.md`: design decisions, consult verdicts (provenance noted), v12 citations, two-direction test evidence, Debug/Release × Windows/WSL2 self-check transcripts, budgets, surprises; **record engine-review presence/absence** (T-2)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the diff + contract; verdict text in record.md
- [ ] Update `client/docs/task-board.md` row **"Define asset and packaged-output manifest"** to `WIP` with evidence citing record.md; the evidence text MUST name the **publish-mode gate** (deferred to row 9 — annotate, never rewrite acceptance) — never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification

- [ ] Contract testCommand passes (solution + BOTH test projects)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `asset-manifest.md` defines the schema (all accepted fields), override/trust policy (stated, unimplemented), two-direction validation rule, self-check contract, row-9 publish hook, and measured budgets
- Catalogue exists with the one real entry; two-direction tests green (open + completeness sweep + case-exact ordinal); schema-validation tests green
- `--verify-assets` exits 0 on Debug AND Release binaries on Windows AND WSL2 (transcripts recorded); case-exactness meaningful on ext4
- Publish-mode named as row 9's gate in the board evidence text; no throwaway publish run
- Both solo Fable consults persisted; STATUS.md accurate; board row `WIP` (not `DONE`); no tracked changes outside File Scope; no new packages

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (read-only evidence) — in particular do NOT copy the WPF localization JSONs "to have entries" (no consumer; localization entries arrive with the localization row)
- Implement a mod loader, runtime override resolution, or user-content directories (schema + stated policy only — no consumer, A-014)
- Run a throwaway publish or claim publish-mode evidence (row 9's named gate)
- Admit any package; add native interop
- Use `System.Drawing`; make presentation claims from headless frames
- Weaken SP-003…SP-008 invariants
- Use `consult` council mode (route broken — T-7; solo Fable 5 only this packet)
- Set any board row to `DONE`; fake STATUS.md/review notes

## Git Commit Convention

- `feat(SP-009): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/asset-manifest.md` (deliverable), `client/docs/task-board.md` (row-8 evidence), `spine-tasks/SP-009-asset-manifest/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only), `client/docs/verification-harness.md` (only if the self-check invocation belongs there — one line, justify in record.md)

## Amendments

- 2026-07-19 (authoring): **pre-authoring consult RAN — council attempted first (owner-sanctioned 2026-07-19) but its route is BROKEN (`kimi-openai-completions` provider unregistered; filed as T-7); fell back to solo Fable 5 per the standing rule, verdict received complete.** Verdicts applied: (a) JSON catalogue over typed registry (readable by pack/verify tooling, SP-008 checks.json pattern) with THREE pin-downs — two-direction validation (completeness sweep fails on unmanifested assets), case-exactness as a named ordinal check (the row's highest-value assertion), schema-covers-policy-not-instances (no mod loader, no copied localization JSONs — invented scope); (b) NO throwaway publish — publish changes asset resolution and is row 9's open decision; evidence against a rejectable default mode is misleading (Wayland-gate class); deliver Debug+Release on Windows+WSL2, publish = named deferred hook; (c) the asset-open test must run against REAL OUTPUT, not a unit-test assembly context — bounded `--verify-assets` diagnostic self-check mode on the real binary (row 9 = same invocation against the published artifact, zero new test logic); (d) worker must verify AssetLoader platform-init behavior in 12.1 via research, not memory.
- 2026-07-19 (authoring): engine reviews assumed absent (T-2); Review Level 2 retained. Launch follows the T-6 playbook (stub batch → abort+clean → separate fresh real batch).
