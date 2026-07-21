# Task: SP-024 — DTRH host slice b2: save slots, picker/quick start, protocol v1

## Mission

Execute slice **b2** of `client/docs/dtrh-admission.md` §7 for the `client/docs/task-board.md` row **"Implement web-only DTRH host"** (P0): three local save slots + save picker / quick start + protocol v1 (full message vocabulary) on top of SP-023's landed b1 (host shell, §4 loopback origins, §3.3/§3.1 transport, boot matrix). Real product code in `client/src/CcpClient.Desktop/Features/Dtrh/`.

**Honesty framings (binding):** (a) slot persistence REUSES SP-005's machinery (schema-versioned store, atomic temp+rename+flush, corruption quarantine, migration journal) — no parallel save format, no second persistence path; slot identity/ordering semantics come from WPF archaeology (`File.cs:line`), not invention; (b) protocol v1 vocabulary comes from the READ-ONLY payload's protocol sources + WPF host behavior — the full message set with per-direction mapping through b1's transport (Windows postMessage / Linux invokeCSharpAction + long-poll inbox); unknown/forward-version messages get a typed tolerance decision (never silent drops, never crashes); (c) picker/quick-start UX ports the WPF interaction OUTCOME (wpf-parity: what the user can do and see), not WPF mechanics; dashboard-design skill governs the surface; (d) **OWNER DISPLAY CONVENTION (2026-07-21): all headed evidence windows position on DISPLAY3 ((-2576,1091) 2560×1440) — SetWindowPos/Window.Position verified by GetWindowRect before captures;** (e) capability honesty: picker flows are Windows-headed evidence; Linux = WX render session facts, no input automation (SP-008), no timing claims; Wayland never claimed (§5); (f) no classic fallback.

## Dependencies

- **Task:** SP-023 (b1 landed — host shell, transport, boot matrix)

## Context to Read First

- `client/docs/dtrh-admission.md` §7 (b2's exact scope + evidence classes) + §3 (transport) + §4 (loopback contract)
- `client/docs/persistence-migration-contract.md` (SP-005) — the machinery slots must reuse
- `client/docs/webview-dtrh-spike.md` (SP-011) — boot/protocol observations
- `spine-tasks/SP-023-dtrh-host-b1/record.md` — b1's landed shape (host window, inbox, bridge derivative, boot matrix harness)
- The READ-ONLY DTRH payload (`ConditioningControlPanel/Resources/web/dtrh/`, tree `40be29df`) — `protocol.js`/message sources for the v1 vocabulary
- WPF DTRH host (READ-ONLY, `File.cs:line`): save slot management, save picker, quick start, protocol handling — locate via repo search under `ConditioningControlPanel/` (DTRH/Deeper host windows + services)
- Required skills: load `wpf-parity`, `dashboard-design` before Step 1; `avalonia-research` before Step 4

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (slots, picker/quick-start, protocol v1)
- `client/tests/CcpClient.Tests/**` (slot persistence, protocol vocabulary/round-trip tests)
- `client/tests/CcpClient.HeadlessTests/**` (draw-level picker/slot-surface tests where honest)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-024-dtrh-host-b2/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhSaveSlots.cs`, `client/src/CcpClient.Desktop/Features/Dtrh/DtrhProtocol.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `spine-tasks/SP-024-dtrh-host-b2/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Slots/picker/protocol archaeology + design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): three-slot model (identity, ordering, create/select/delete semantics, empty-slot display), save picker flow (open, list, select, confirm/cancel, error states), quick start flow (entry point, what it creates/launches), protocol v1 usage in the WPF host
- [ ] Payload `protocol.js` archaeology (READ-ONLY): the full v1 message vocabulary (types, directions, required fields, versioning markers); map each message to the b1 transport direction
- [ ] Design: slot store on SP-005 machinery (schema, slot document shape, migration position); protocol dispatcher with typed outcomes + unknown-message tolerance decision; picker/quick-start surface per wpf-parity outcomes + dashboard-design
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Three local save slots (SP-005 machinery)

- [ ] `Features/Dtrh/DtrhSaveSlots.cs` (contract-named): three named slots — create/select/persist/delete per the WPF semantics; schema-versioned through SP-005 (atomic write, quarantine on corruption with typed Degraded, migration journal entry, unknown-member preserve)
- [ ] Unit tests: slot lifecycle (create/select/persist across store reloads), corruption → quarantine + flagged defaults (never silent), ordering stability, empty-slot semantics

### Step 3: Protocol v1 full vocabulary

- [ ] `Features/Dtrh/DtrhProtocol.cs` (contract-named): the full v1 message set from the payload's protocol sources — typed messages, per-direction dispatch through the b1 bridge (Windows postMessage / Linux invokeCSharpAction + inbox), typed outcomes per message, unknown/forward-version tolerance as decided in Step 1
- [ ] Unit tests: every vocabulary message round-trips through the in-memory bridge seam (serialized shapes match the payload's protocol sources), unknown-message tolerance proven (typed, logged presence-only, no crash, no silent drop)

### Step 4: Picker + quick start + headed/WX evidence + board reconciliation + pre-completion consult

- [ ] Save picker + quick start in the host window (per Step-1 outcomes; dashboard-design grammar; SP-004 owned operations for async flows)
- [ ] **Windows headed evidence on DISPLAY3 (owner convention — verify GetWindowRect before captures):** picker open/list/select/confirm + cancel flows, quick start end-to-end (slot created + game boots into it), slot persistence across app restart (file-content proof); K3 visual where pixels matter
- [ ] **WSL2 in-packet gate (`~/ccp-sp024`, never /mnt/e):** contract testCommand green; WX render session facts for picker/slot surfaces (XGetImage, no input automation); protocol round-trips over the Linux transport (invokeCSharpAction + inbox); no timing claims; Wayland untouched
- [ ] Write `spine-tasks/SP-024-dtrh-host-b2/record.md` (archaeology, design decisions, consult verdicts + ACTUAL answering models, engine-review presence, evidence transcripts, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` host row → `WIP` with slice-b2 evidence + named limits (Wayland; Linux picker interaction; remaining slices b3…b5) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild` per the xUnit1051 lesson)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Three local slots implemented on SP-005 machinery with corruption quarantine + ordering + lifecycle tests green
- Protocol v1 full vocabulary typed + dispatched through the b1 transport with unknown-message tolerance proven; round-trip tests green both platforms
- Save picker + quick start delivered with Windows headed evidence on DISPLAY3 (verified placement) + WSLg render facts; slot persistence proven across restart
- Board row `WIP` with named limits (never `DONE`); both solo Fable consults persisted with actual answering models

## Do NOT

- Build past b2 (SFX/freeze/tint = b3; progression/Loom/media = b4; watchdog/exit = b5); create a parallel save format or bypass SP-005; edit the DTRH payload in place (read-only evidence); invent protocol messages beyond the payload's sources + WPF host behavior; claim Wayland; fake Linux input automation; log sensitive values (presence+shape only); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-024): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/task-board.md` (row evidence), `spine-tasks/SP-024-dtrh-host-b2/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only — **UTF-8 only**)

## Amendments

- 2026-07-21 (authoring): **admission record §7 slice cut binding (b2: three local slots + save picker/quick start + protocol v1 full vocabulary); SP-023 landed `31e31d2d` provides the transport/shell/inbox.** **OWNER DISPLAY CONVENTION ENCODED (first headed packet since the 2026-07-21 directive): all headed evidence windows position on DISPLAY3 ((-2576,1091) 2560×1440), GetWindowRect-verified before captures.** mustNotChange intersected against File Scope at authoring (SP-020 lesson); T-11 sizing: Step 4 is the headed step; orchestrator sets `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch.
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
