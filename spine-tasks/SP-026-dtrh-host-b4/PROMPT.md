# Task: SP-026 — DTRH host slice b4: progression/payout, Loom, user/mod media

## Mission

Execute slice **b4** of `client/docs/dtrh-admission.md` §7 for the `client/docs/task-board.md` row **"Implement web-only DTRH host"** (P0): progression/meta-command ops + XP payout round-trip + the Loom (saved-spiral store) + user/mod media serving on top of SP-025's landed b3 (native effects, protocol upgrades, in-page tint/freeze). Real product code in `client/src/CcpClient.Desktop/Features/Dtrh/`. b4 upgrades its owned protocol messages (`meta-command`, `request-run`, `asset-stats`, `loom-save`, `loom-delete`, payout path) from `Deferred(b4)` to `Handled` with REAL effects — and **unlocks the page-internal visuals b3 named as b4-gated** (VN portrait tint via the meta message, freeze bubbles via request-run).

**Honesty framings (binding):** (a) **meta progression rides the b2 slot documents on SP-005 machinery** — `DtrhSlotDocument` (schema v1 + `[JsonExtensionData]` unknown-member preserve) gains progression members WITHOUT a schema bump (b2's recorded design intent); a parallel `chaos_meta.json` or any second save format is a HARD STOP; (b) WPF semantics come from archaeology (`File.cs:line`), never invention — meta ops, payout math, rank/lesson/boon state, request-run setup, asset-stats, Loom slug/save/delete/list discipline; (c) **media filename logging is presence+shape ONLY (SP-018 V5 class, b3 named limit → b4 requirement)** — no media filenames/URLs in logs; (d) Loom spirals are GIF FILES (not JSON docs) — the store design (folder + slug index + atomic writes vs SP-005 adaptation) is a Step-1 consult decision with the rejected alternative recorded; (e) user/mod media serving stays inside the §4 loopback contract (GET-only, MIME allowlist, traversal refusal, localhost, no-store for credentialed URLs); the user-media folder contract is decided in Step 1 (WPF `App.EffectiveAssetsPath` parity vs the b3 overlay-staging shape) and recorded; (f) **OWNER DISPLAY CONVENTION + SP-025 land-consult BINDING: headed windows position on DISPLAY3 ((-2576,1091) 2560×1440) and the drive scripts MUST redirect the GetWindowRect output line into the committed run logs** (SP-025 executed placement+verification but never persisted the rect printout); plus the modal-drive rule (UIA InvokePattern/timed drive for modals); (g) Linux = WX session facts + mechanism evidence only — no timing claims, no input automation, Wayland never; (h) real user media comes from `Z:\CCP Vids` — COPIES into packet evidence scratch only; product code and committed files never reference `Z:\`; (i) SoundFlow discipline from SP-025 (port-lessons 2026-07-22): any audio player construction happens OFF the SynchronizationContext.

## Dependencies

- **Task:** SP-025 (b3 landed — native effects, protocol Deferred(b4) outcomes, in-page tint/freeze)

## Context to Read First

- `client/docs/dtrh-admission.md` §7 (b4's exact scope + evidence classes) + §4 (loopback contract — media serving stays inside it) + §5 (no classic fallback)
- `spine-tasks/SP-024-dtrh-host-b2/record.md` (slot document shape + "b4 adds progression members WITHOUT a schema bump" design intent) and `spine-tasks/SP-025-dtrh-host-b3/record.md` (Deferred outcomes to upgrade; b4-gated page visuals — `cheshireGuide.js:82-84` needs the meta message, freeze bubbles need request-run; media-logging requirement; rect-persistence gap this packet must close)
- WPF (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/Chaos/DtrhHostService.cs` (`:19-20` meta-command/payout routing, `:83/:97` Loom folder + `ccp.spirals` virtual host, `:193/:209` init-carried saved run setup + PostLoomList seed, `:249-300` meta-command/request-run/asset-stats/loom-save/loom-delete dispatch, `:327-342` PostLoomList shape incl. `loom_{slug}.gif` URLs, `:390-395` OnRequestRun persistence); `ChaosMetaState.cs` + `ChaosMetaStore.cs` (slot-aware meta doc, legacy migration idempotency, additive-only rule); `DtrhMetaBridge` (meta-command ops — locate via repo search); `DtrhLoomStore` (slug discipline, GIF validation, folder layout); `ChaosRanks.cs`/payout math (XP, sparks/gold, payout-result shape)
- The READ-ONLY DTRH payload (`ConditioningControlPanel/Resources/web/dtrh/`, tree `40be29df`) — protocol field shapes for b4-owned messages + the `m2test.js`-class harness + `cheshireGuide.js` meta-message consumption + `modContent.js` user/mod media paths
- `client/docs/port-lessons.md` — DISPLAY3 convention + rect-persistence binding + modal-drive rule + SoundFlow off-sync-context discipline (all 2026-07-21/22 entries)
- Required skills: load `wpf-parity`, `dashboard-design` before Step 1; `avalonia-research` before Step 4

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (meta/payout, Loom store, media serving, protocol upgrade)
- `client/tests/CcpClient.Tests/**` (meta/payout/Loom/media tests)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-026-dtrh-host-b4/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLoom.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `spine-tasks/SP-026-dtrh-host-b4/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Meta/payout/Loom/media archaeology + design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): meta-command op set (DtrhMetaBridge), ChaosMetaState fields + additive-only rule + legacy-migration idempotency, payout math + payout-result shape, request-run setup persistence, asset-stats shape, Loom store (slug discipline, GIF validation, folder layout, list/result shapes), user/mod media paths (modContent.js, ccp.mod, `App.EffectiveAssetsPath` concept)
- [ ] Payload verification (READ-ONLY): exact field shapes for b4-owned messages against b2's `DtrhProtocol.cs` records; the `m2test.js`-class harness; `cheshireGuide.js:82-84` meta-message consumption (what unlocks the VN portrait tint); request-run → freeze-bubble path
- [ ] Design: progression members mapped onto the b2 slot document (explicit member list, no schema bump, unknown-member preserve honored); Loom store shape decision (folder + slug index + atomic writes vs SP-005 adaptation — rejected alternative recorded); user-media folder contract decision (recorded); media-logging presence+shape rule implemented at the log sites
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Meta progression + payout on the b2 slot machinery

- [ ] Progression members on `DtrhSlotDocument` (no schema bump); meta-command ops mutating through the slot store (atomic, journaled, quarantine-honest); legacy/absent-member defaults flagged not silent
- [ ] Payout: run-ended → payout computation → `payout-result` reply (WPF shape); run-started/run-ended classification upgraded Deferred→Handled (the b3 freeze run-boundary hygiene rides this — verify no regression); request-run setup persistence (hub Descent-tab choices, init-carried); asset-stats reply
- [ ] Unit tests: meta op matrix (each op's mutation + clamp/validation), payout math vs WPF cases, payout-result round-trip, request-run persist→init round-trip, tolerance preserved (unknown/malformed never throws), media-logging rule test (no filenames at the log sites)

### Step 3: The Loom + user/mod media serving

- [ ] `Features/Dtrh/DtrhLoom.cs` (contract-named): save/delete/list/result per the Step-1 design; GIF validation (WPF discipline); `ccp.spirals`-class serving through the §4 loopback (virtual host or overlay route — decided in Step 1); `loom_{slug}.gif` URL shape; PostLoomList-equivalent seeding at ready
- [ ] User/mod media serving inside §4: the decided folder contract wired into the loopback (MIME allowlist, traversal refusal, no-store discipline for credentialed URLs); modContent.js path honored
- [ ] Unit tests: Loom save→list→serve→delete lifecycle (incl. slug collision + invalid-GIF refusal), traversal/MIME refusal tests at the serving layer, seed-at-ready shape

### Step 4: Headed/WX evidence + page-visual unlocks + board reconciliation + pre-completion consult

- [ ] **Windows headed evidence on DISPLAY3 (owner convention; rect-persistence BINDING — the GetWindowRect output line MUST be redirected into the committed run logs; modal-drive rule):** payout round-trip end-to-end (descend → run events → payout → payout-result → slot-doc file-content proof); Loom save → serve → display pixel-verified; user media from `Z:\CCP Vids` COPIES served + rendered (real files, presence+shape logs); **the b3-gated page visuals now exercised where their gating message exists** (VN portrait tint via meta message; freeze bubble via request-run) — pixel-verified, never faked
- [ ] **WSL2 in-packet gate (`~/ccp-sp026`, never /mnt/e):** contract testCommand green; WX session facts (XGetImage; Loom/media serving on Linux; page-visual facts where honest); no timing claims; Wayland untouched
- [ ] Write `spine-tasks/SP-026-dtrh-host-b4/record.md` (archaeology, design decisions incl. Loom store + user-media contract with rejected alternatives, consult verdicts + ACTUAL answering models, engine-review presence, evidence transcripts WITH the rect lines, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` host row → `WIP` with slice-b4 evidence + named limits (Wayland; Linux facts; remaining b5; published-artifact payload location still UNDECIDED) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild` per the xUnit1051 lesson; counts ≥ the b3 floor 313 unit + 29 headless)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Meta progression + payout implemented on the b2 slot documents (no parallel file, no schema bump) with WPF-parity ops/math and payout-result round-trip evidence; request-run + asset-stats handled
- The Loom delivered (save/delete/list/result + GIF validation + serving through §4) with lifecycle tests and DISPLAY3 pixel-verified display (rect lines persisted in committed logs)
- User/mod media served inside the §4 contract with presence+shape-only logging; real-media evidence from `Z:\CCP Vids` copies
- b4-gated page visuals (VN portrait tint, freeze bubbles) exercised through their real gating messages — never faked
- Contract green both platforms (≥313/29 floor); board row `WIP` with named limits (never `DONE`); both solo Fable consults persisted with actual answering models

## Do NOT

- Build past b4 (watchdog/exit-done/pong/stale-profile = b5); create a parallel meta/save file or bump the slot schema; log media filenames/URLs (presence+shape only); fake page-internal visuals whose gating message does not exist; serve media outside the §4 contract; reference `Z:\` from product code or committed files; claim Wayland or Linux timing; fake Linux input automation; silently drop messages; modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-026): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/task-board.md` (row evidence), `spine-tasks/SP-026-dtrh-host-b4/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only — **UTF-8 only**)

## Amendments

- 2026-07-22 (authoring): **admission record §7 slice cut binding (b4: progression/payout + Loom + user/mod media); SP-025 landed `50b61312` provides native effects + protocol Deferred(b4) outcomes + in-page tint/freeze.** b3-landed constraints encoded: meta progression rides the b2 slot docs (no schema bump, no parallel file); media filename logging = presence+shape (b3 named limit → requirement); b4-gated page visuals now exercised through their real messages; **rect-persistence BINDING (SP-025 land consult): GetWindowRect output MUST be redirected into committed run logs**; SoundFlow off-sync-context discipline. DISPLAY3 convention + modal-drive rule + `--dtrh-quick` harness entry carried. Owner media dir `Z:\CCP Vids` (copies only). mustNotChange intersected against File Scope at authoring (SP-020 lesson — no overlap). T-11 sizing: Step 4 is the headed step; orchestrator sets `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch.
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
