# Task: SP-055 — One active-pool definition: honor asset deselection in DTRH pools + Graded Intake provisioning (v6.7.x parity)

## Mission

Execute the `client/docs/task-board.md` **P0** row *"Asset deselection not honored by the port's DTRH pools + Graded Intake provisioning (v6.7.x parity drift)"*. Upstream turned asset deselection into a **shipped contract** (#762 #798 #619, "Assets: deselection honored everywhere, chaos overlays and Graded Intake included"): `Services/Chaos/DtrhAssetManifest.cs` was rewritten around a single `EnumerateActive()` "active pool" definition, and `Services/Quiz/IntakeHostService.cs` gained `IsAssetActive(disabled, root, fullPath)` for the intake media manifest. The port walks `images/` + `videos/` **raw** at `client/src/CcpClient.Desktop/Features/Dtrh/DtrhUserMedia.cs` and records the gap as a deliberate divergence (`:13` — "DisabledAssetPaths = empty (no assets tree in the greenfield)").

**Ship the SEAM, not the UI.** No Assets-tree work, no picker, no speculative settings screen. The deliverable is: **one shared active-pool definition** that every media consumer routes through, honoring a persisted deselection set that is simply *empty* until a future Assets-tree row populates it. That converts a latent parity defect into a wired contract — the day the tree lands, deselection works everywhere at once instead of being re-litigated per consumer.

**Binding framings:** (a) **the normalization is load-bearing** — upstream matches `FlashService.GetMediaFiles` exactly (relative to the assets root, `\` → `/`, `StringComparer.OrdinalIgnoreCase`) so the same uncheck that hides a flash hides it in DTRH; port that normalization verbatim and pin it with tests; (b) **never silently drop content over a path quirk** — upstream's `IsAssetActive` returns `true` when the path cannot be made relative (`Path.GetRelativePath` throws) and when the disabled set is empty; (c) **`UseAssetWhitelist` gates the whole mechanism** (`CCP.Core/Models/AppSettings.cs:1637` — "When true, files in `DisabledAssetPaths` are excluded from use; when false, all files are active"); (d) **skip semantics are distinct from deselection** — upstream's `ScanItem.Skipped` means "media-looking but not usable" (counted), while a deselected file is skipped *silently* (not counted); keep both meanings separate; (e) **the accepted-count bound spans BOTH folders** (upstream comment: it used to be a shared running total across the two collect calls — "keep it that way, the manifest's downsampling assumes a bounded but unbiased-by-folder input"); (f) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — orchestrator reconciles at land.

## Dependencies

- **Task:** SP-054 (the Graded Intake host — its media provisioning is the second consumer this row wires)

## Context to Read First

- `client/docs/upstream-sync.md` §B (this row's evidence, baseline pair v6.6.3 → v6.7.4, merge `42286638`)
- WPF (READ-ONLY, current `main`): `ConditioningControlPanel/Services/Chaos/DtrhAssetManifest.cs` — `EnumerateActive()` (`:85`), `BuildDisabledSet()` (`:116`, incl. the `FlashService.GetMediaFiles` normalization note at `:110-115`), `Scan(root, disabled)` (`:125`, the both-folders bound), `ScanItem` (`:41`); `ConditioningControlPanel/Services/Quiz/IntakeHostService.cs` — `IsAssetActive` (`:783-790`) and the MediaManifest walk it guards (`:792+`, incl. the verbatim #762/#798/#619 comment); `ConditioningControlPanel/CCP.Core/Models/AppSettings.cs` — `DisabledAssetPaths` (`:1613`), `UseAssetWhitelist` (`:1637`)
- Port (the code being fixed): `client/src/CcpClient.Desktop/Features/Dtrh/DtrhUserMedia.cs` (the raw walk + the recorded divergence at `:13`), its caller `Features/Dtrh/DtrhHostWindow.axaml.cs`, and **SP-054's intake media provisioning** (read its record + the `Features/Intake/` host it landed)
- `client/src/CcpClient.Desktop/Persistence/` (SP-005 machinery — where the persisted set belongs; own named owner, absent-member-flag discipline, no schema bump if additive)

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhUserMedia.cs` + the shared pool type's home (`Features/Dtrh/**` or a shared `Media/**` file — justify the choice in the record)
- `client/src/CcpClient.Desktop/Features/Intake/**` (routing intake provisioning through the seam only)
- `client/src/CcpClient.Desktop/Persistence/**` (the persisted deselection set + whitelist flag)
- `client/tests/CcpClient.Tests/**` (new + updated tests)
- `spine-tasks/SP-055-asset-active-pool/**`
- **NOT in scope:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md` (enabler 2)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhUserMedia.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md` |
| artifactsMustExist | `spine-tasks/SP-055-asset-active-pool/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Archaeology + seam design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology against current `main` (`File.cs:line`): the exact active-pool contract — normalization, empty-set short-circuit, unrelatable-path tolerance, whitelist gate, `Skipped` vs silently-deselected, the both-folders accepted bound, images-then-videos walk order, per-type extension lists and size caps
- [ ] Enumerate **every** current consumer of user media in the client (grep, don't guess): the DTRH pool walk, SP-054's intake media manifest, and anything else that touches `images/`/`videos/` — the row's whole point is that one definition serves all of them
- [ ] Design the seam (one definition, both consumers routed, persisted set + whitelist flag as additive members with an owner; the set stays empty until a future Assets-tree row) and the fixture matrix
- [ ] **Pre-approach solo consult** (Opus 5 main route, Fable 5 fallback); verdict + ACTUAL answering model in record.md before the checkbox

### Step 2: The seam + both consumers + tests

- [ ] Implement the single active-pool definition with upstream's exact semantics (normalization verbatim, empty-set and unrelatable-path tolerance, whitelist gate, distinct skip semantics, both-folders bound)
- [ ] Route the DTRH pool walk through it (`DtrhUserMedia`), replacing the raw enumeration; update the `:13` divergence comment to state the new truth
- [ ] Route SP-054's intake media provisioning through the SAME definition (never a second scan that can disagree)
- [ ] Persisted deselection set + whitelist flag on SP-005 machinery (additive, absent-member-flag discipline, own named owner)
- [ ] Tests: deselection fixture matrix (empty set, exact match, case difference, `\` vs `/`, nested path, unrelatable path, whitelist off vs on), both consumers proven to agree on the same fixture, skip-vs-deselect counting kept distinct, both-folders bound preserved

### Step 3: Headed proof + record + pre-completion consult

- [ ] **Headed evidence (Windows):** with a non-empty deselection set seeded in the persisted store, a deselected asset **never reaches the page** — prove it on BOTH consumers (DTRH pool + intake provisioning) with file-content/transcript proof, plus a control run with the set empty showing the asset present. Captures dimension-validated per the `windowId` quirk rule
- [ ] Write `spine-tasks/SP-055-asset-active-pool/record.md` (archaeology with citations, consumer inventory, design, consults + ACTUAL models, engine-review presence, evidence index, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult**; verdict in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E on `-t:Rebuild`, both suites at or above the floor SP-054 leaves, TRX logger attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Exactly ONE active-pool definition exists in the client; both the DTRH pool and intake provisioning consume it (grep-proven — no second raw walk survives)
- Upstream's semantics ported verbatim and pinned by tests: normalization, empty-set short-circuit, unrelatable-path tolerance (`true`, never a silent drop), whitelist gate, skip-vs-deselect distinction, both-folders accepted bound
- The deselection set is persisted (additive, no schema bump) and empty by default; no Assets-tree UI was built
- Headed proof on both consumers (deselected asset absent; control run present)
- Contract green; both consults persisted with actual answering models

## Do NOT

- Build an Assets-tree UI, picker, or settings screen (a separate row owns it); widen scope to flash/other WPF media consumers that the client has not ported yet; keep two scans that can disagree; drop content when a path cannot be relativized; conflate "unusable media" (counted `Skipped`) with "deselected" (silent); edit the three hot docs (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`; set any board row state; claim Wayland or fake Linux evidence
- Use `consult` council mode (T-7: solo only, Opus 5 main / Fable 5 fallback)

## Git Commit Convention

- `feat(SP-055): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-055-asset-active-pool/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`

## Amendments

- 2026-08-11 (authoring, orchestrator): row filed by the v6.6.3 → v6.7.4 upstream sync (merge `42286638`, ledger §B). Sequenced AFTER SP-054 because the intake consumer must exist before it can be routed through the seam. Enabler 2. T-11 sizing: each step < 2h; 4h budget exported at launch. WSL zero-distros named limit. **`## Review Level: 2` heading present + grep-verified ≥ 2.**
