# Task: SP-049 — Loom studio promotion (v6.6.3 behavior delta — drive the studio surface)

## Mission

Port the v6.6.3 **Loom studio promotion** behavior delta for the `client/docs/task-board.md` row "Implement web-only DTRH host" (WIP): the main-sync merge `56f156fc` promoted the Loom studio INTO the DTRH payload (new files `loom.html`, `loomBoot.js`, `shared/loomField.js`, `vendor/gifenc/gifenc.esm.js` — served by the client's loopback server since SP-037/SP-048 but never DRIVEN). The b4 named limit is the target: **"Loom rack pane render not driven (pane + 3D gate; display proof = served URL in-engine)"**. Discharge it: the studio surface opens, renders, and operates in-engine on the REAL host (Windows, avalonia-live evidence); every host-side message the studio needs is handled (never Deferred-silent); GIF export (gifenc) works through the serving contract. Linux = named limit (WSL zero distros — owner-gated, never faked). No Wayland claims.

**Honesty framings (binding):** (a) **DUAL ARCHAEOLOGY (land-consult constraint, load-bearing):** read BOTH the v6.6.3 payload changes (loom.html / loomBoot.js / loomField.js / gifenc — what the studio page needs from the host, which bridge messages it emits/consumes, what the 3D gate and rack pane actually are) AND the landed b4 implementation (`DtrhLoom` — save/delete/list lifecycle, `/spirals/*` serving, sidecar discipline, loom-list at ready + after mutations). Frame the delta as: what v6.6.3 adds ON TOP of b4's landed Loom, and what is genuinely user-observable — never re-port machinery that already landed; (b) **user-observable parity is the contract; implementation is free** (owner decree 2026-08-04); the payload is READ-ONLY (never edit the payload tree — the studio page is WPF-shared content); (c) any NEW protocol message the studio needs lands typed (unknown/forward-version/malformed tolerance per b2's vocabulary discipline); no new origin without the §4 contract's discipline (GET-only, overlay-first, Range, MIME, CORS, traversal refusal, token); (d) media filename logging stays presence+shape ONLY (b4's binding); (e) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (f) WSL2 named limit; (g) if the studio needs something the host genuinely lacks (a backend, a 3D surface), record it as a typed named limit — never fake the surface.

## Dependencies

- **Task:** SP-048 (the published-artifact payload location + self-evidencing serving diagnostics this slice rides)

## Context to Read First

- `client/docs/main-sync-2026-08-04.md` (the v6.6.3 delta inventory — Loom studio promotion context) + the SP-037 board row (the manifest delta)
- The payload studio files (READ-ONLY): `ConditioningControlPanel/Resources/web/dtrh/loom.html`, `loomBoot.js`, `shared/loomField.js`, `vendor/gifenc/gifenc.esm.js` + the bridge/protocol usage they make
- `spine-tasks/SP-026-dtrh-host-b4/record.md` (the landed Loom implementation + the rack-pane named limit verbatim) + the board row's consolidated limits
- `client/src/CcpClient.Desktop/Features/Dtrh/` (`DtrhLoom`, `DtrhHostWindow`, the protocol router — b4 messages Handled/Deferred states)
- `client/docs/dtrh-admission.md` §3/§4 (transport + loopback contract) + `client/docs/webview-dtrh-spike.md` (engine behavior facts)
- The avalonia-live usage map (SP-046 record + `client/memories/avalonia-mcp.md` — 27 verified tools; `windowId` silent-drop quirk: use `target`/`handle`, validate capture dimensions BEFORE the evidence pass)

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (studio driving: open path, protocol handlers, any host-side glue)
- `client/tests/CcpClient.Tests/Dtrh*` (protocol/studio tests)
- `client/tests/CcpClient.HeadlessTests/Dtrh*` (where honest)
- `spine-tasks/SP-049-loom-studio/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**` |
| artifactsMustExist | `spine-tasks/SP-049-loom-studio/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Dual archaeology + drive design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] v6.6.3 payload archaeology (READ-ONLY): what loom.html/loomBoot.js/loomField.js/gifenc need from the host (bridge messages consumed/emitted, the 3D gate's real shape, the rack pane's composition, the GIF export path)
- [ ] b4 archaeology: the landed `DtrhLoom` surface (what already works — save/delete/list/serve; the rack-pane limit verbatim) → **the delta list: what v6.6.3 adds on top, what is user-observable, what the client must drive vs what the payload self-drives**
- [ ] Drive design: the open path (how a user reaches the studio from the host — WPF v6.6.3 parity shape per the payload), any new typed protocol messages (per b2's tolerance discipline), the rendering surface (in-engine WebView2 embedded on Windows), evidence plan (avalonia-live captures + semantic trees; dimension-validated)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + delta list + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Studio driving + protocol + tests

- [ ] The studio open path + any new typed protocol messages (unknown/forward-version/malformed tolerance; presence+shape logging only)
- [ ] The rack pane driven in-engine (the b4 limit's discharge shape decided from the archaeology — pane render or honest typed limit if the host genuinely lacks a surface)
- [ ] GIF export through the serving contract (gifenc path works against the §4 disciplines)
- [ ] Unit tests (protocol round-trips, new-message tolerance, serve/probe discipline)

### Step 3: In-engine evidence (avalonia-live) + evidence consolidation + pre-completion consult

- [ ] **In-engine evidence on Windows (avalonia-live, `CCP_MCP=1`):** the studio OPENS from the host, RENDERS (screenshot + semantic tree, dimension-validated per the windowId quirk rule), operates (rack pane content visible; a loom save/delete/list round-trip through the REAL messages with file-content proof; GIF export produces a valid GIF file through the serving contract)
- [ ] Escalate to a typed named limit (never fake) if a needed host surface is missing — recorded with the exact gap
- [ ] Write `spine-tasks/SP-049-loom-studio/record.md` (dual archaeology, delta list, design, consult verdicts + ACTUAL answering models, engine-review presence, evidence index, budgets, surprises, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 614/33 floor; **full-suite runs attach a TRX logger or lossless output so every failure yields a name** — the `skill-trx-failure-names` template amendment)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- The Loom studio opens from the host and renders in-engine (avalonia-live screenshot + semantic tree, dimension-validated)
- The b4 rack-pane limit discharged OR honestly converted to a typed named limit with the exact gap
- Every studio-needed bridge message handled typed (never Deferred-silent); GIF export valid through the serving contract
- Loom save/delete/list round-trip through real messages with file-content proof
- Payload tree READ-ONLY throughout; presence+shape logging; Linux named limit; contract green (≥614/33 floor); both solo consults persisted with actual answering models

## Do NOT

- Edit the payload tree (`ConditioningControlPanel/**` READ-ONLY); re-port b4's landed Loom machinery (dual archaeology — build on it); invent host surfaces (typed named limit instead); log media filenames beyond presence+shape; fake the in-engine render (dimension-validated captures or it isn't evidence); fake Linux evidence; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**`; set any board row state; claim Wayland
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-049): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-049-loom-studio/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-05 (authoring, orchestrator): **first v6.6.3 behavior-delta packet per the phase re-derivation order (land-consult constraint: dual archaeology — v6.6.3 payload changes AND b4's landed DtrhLoom).** Target = the b4 rack-pane named limit. Enabler 2 (no hot docs). avalonia-live is the headed instrument (windowId quirk rule encoded). **T-11 sizing: each headed step <2h; 4h budget exported at launch.** WSL zero-distros named limit. Consult route per the 2026-08-04 rewire. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-05 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached single-lane batch (feature slice runs alone — headed focus) per owner cycle.
