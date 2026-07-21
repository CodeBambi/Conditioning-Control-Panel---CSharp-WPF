# Task: SP-023 — DTRH host slice b1: shell, origins, transport, boot matrix

## Mission

Execute slice **b1** of `client/docs/dtrh-admission.md` §7 for the `client/docs/task-board.md` row **"Implement web-only DTRH host"** (P0): host shell (product window + app.manifest) + loopback origin serving (§4 security contract) + the minimal transport-only diff applied (§3) + boot matrix re-run **in-product**. This is the FIRST product-implementation slice of the DTRH host — real code in `client/src/CcpClient.Desktop/`, not a spike. **The admission record is the binding spec; where this packet and the record disagree, the record wins (fix the smallest document per the authority order).**

**Honesty framings (binding):** (a) **LITERAL FIRST CHECKBOX — the named risk: `invokeCSharpAction` page→host is spike-proven only on the EMBEDDED GTK adapter, NOT on NativeWebDialog (admission §3.3/§8).** Prove it on WSLg via NativeWebDialog BEFORE building anything atop it (SP-011-pattern falsifiable-first-claim). **If it FAILS: the Linux page→host direction falls back to the poll-endpoint shape too (both directions polled) — decided with the pre-approach consult and recorded as a named limit; never shipped silently.** (b) **Windows host→page stays byte-identical to the SP-011-proven path** (synthetic MessageEvent dispatch; never unified onto polling); Linux host→page = long-poll inbox per §3.3 (sequence-numbered retained delivery + per-session unguessable token in the bridge route path). (c) **Payload provenance is the trust anchor:** the DTRH payload stays read-only evidence (SP-011 tree `40be29df`, bridge.js blob `13af3f4d`); the product's bridge.js is a PRODUCT-OWNED DERIVATIVE — original bytes + the minimal documented transport diff, with the original hash and the diff both recorded; the DTRH payload files themselves are served per the §4 contract through SP-009's manifest pipeline (case-exact IDs, `--verify-assets` green — the manifest gains the payload entries by whatever asset class the archaeology justifies; 1536 files is a real packaging decision, record it with rationale). (d) **Capability honesty (SP-006):** embedded WebView = Windows-only capability (probed), NativeWebDialog = the Linux path — typed states, never a fake-embedded claim on Linux; WPE absent (owner question stands). (e) **no classic fallback; Wayland never claimed (§5).** (f) boot-matrix claims come from rendered/received evidence (engine live, message round-trips, pixel-checked render on Windows), never from process-exists or no-exception.

## Dependencies

- **Task:** SP-022 (admission record is the binding spec; Phase-5 serial chain)

## Context to Read First

- `client/docs/dtrh-admission.md` — THE BINDING SPEC: §1 package pin (12.0.1 exact + dep note + app.manifest supportedOS requirement), §2 Linux natives, §3 transport diff spec (3.1 diff shape exact, 3.2 per-direction matrix, 3.3 long-poll inbox contract with seq/ack/token), §4 loopback security contract, §5 no-classic-fallback, §6 payload trust anchor, §7 slice cut (b1's exact scope + evidence classes), §8 non-claims
- `client/docs/webview-dtrh-spike.md` (SP-011) — the boot-matrix observations to re-run in-product (engine live t=1502ms cold, 360fps, Warren rendered, transports 1–3, bridge ordering incl. preBuffer replay, M0 probes, autoplay flag, focus-claim at ready, failure ×3) + the Linux findings
- `client/docs/asset-manifest.md` (SP-009) — the manifest pipeline the payload serving must pass through
- `client/docs/runtime-capability-contract.md` (SP-006) — typed capability states for the embedded/dialog split
- `client/docs/task-board.md` — the DTRH host row (acceptance) + admit row
- `client/spikes/CcpSpike.WebView/` (READ-ONLY) — the proven boot-matrix harness shapes (LoopbackServer, SpikeLog, MainWindow) to adapt INTO product code — port the idea, not the spike classes
- WPF DTRH host (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/` DTRH/Deeper-related windows and services (the original host's quick start/save/protocol behavior — archaeology for the boot handshake semantics)
- Required skills: load `wpf-parity`, `avalonia-research`, `dashboard-design` before Step 2

## File Scope

- `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` (package pin + app.manifest only)
- `client/src/CcpClient.Desktop/Features/Dtrh/**` (host shell, loopback origin server, transport bridge, inbox endpoint)
- `client/src/CcpClient.Desktop/Assets/**` + `client/src/CcpClient.Desktop/Assets/assets.manifest.json` (payload/bridge serving entries through SP-009's pipeline)
- `client/tests/CcpClient.Tests/**` (loopback contract tests, inbox seq/ack/long-poll/token tests, transport diff shape tests)
- `client/tests/CcpClient.HeadlessTests/**` (draw-level where honest)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-023-dtrh-host-b1/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs`, `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `spine-tasks/SP-023-dtrh-host-b1/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: FIRST GATE — NativeWebDialog `invokeCSharpAction` proof + host design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] **FIRST CHECKBOX (falsifiable-first-claim):** on WSLg, prove `invokeCSharpAction` page→host works on **NativeWebDialog** (minimal throwaway probe in the lane, recorded; not product code). Verdict binary: PROVEN (Linux page→host = invokeCSharpAction per §3.2) or FAILED (**fallback: Linux page→host ALSO via the poll endpoint — both directions polled; recorded as a named limit and driven into the pre-approach consult**)
- [ ] WPF DTRH host archaeology (READ-ONLY, `File.cs:line`): boot handshake semantics (ready → init+manifest → engine live), focus-claim at ready, autoplay flag, origin serving
- [ ] Host design from the admission spec: product window shape per platform (embedded Windows / NativeWebDialog Linux), capability-typed availability, payload serving class through SP-009's manifest (1536 files — class + rationale recorded), bridge.js derivative provenance plan
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the FIRST-GATE VERDICT + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Host shell + package integration

- [ ] `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`: `Avalonia.Controls.WebView` pinned per admission §1 (+ app.manifest supportedOS per SP-011 finding); restore/build 0W/0E both platforms
- [ ] `Features/Dtrh/DtrhHostWindow.axaml(.cs)` (contract-named): Windows embedded WebView surface; Linux NativeWebDialog path; typed capability states per SP-006 (probed, honest); registered through the existing composition root with SP-004 owned lifecycle (generation-cancelled teardown)
- [ ] Payload serving through SP-009's manifest pipeline (chosen class, case-exact IDs, `--verify-assets` green Debug + Release); bridge.js PRODUCT DERIVATIVE with the §3.1 transport diff applied — original blob hash + the diff both recorded in record.md

### Step 3: Loopback origin serving + inbox endpoint (§4/§3.3)

- [ ] Loopback origin server per §4: two GET-only localhost origins (overlay-first), Range semantics, MIME allowlist, CORS preflight handling, traversal refusal, localhost binding, sensitive-logging ban (presence+shape only)
- [ ] **Inbox endpoint per §3.3:** long-poll `GET /bridge/inbox?after=N` (hangs until message or bounded timeout), monotonic seq per host→page message, host retains until the next poll's `after` acknowledges, **per-session unguessable token in the bridge route path** (host-generated, delivered in the navigated URL, bridge.js reads from `location`)
- [ ] Unit tests: contract tests (GET-only, 404/405 shapes, Range, traversal refusal, CORS preflight), inbox seq/ack/long-poll-timeout/token-required tests, transport diff shape tests (detection branch + stringify ownership)

### Step 4: Boot matrix re-run (WH) + WSLg gate (WX) + board reconciliation + pre-completion consult

- [ ] **Windows headed boot matrix in-product** (adapted from SP-011's harness shapes, real evidence): engine live, rendered content pixel-checked, transport checks BOTH directions (postMessage/invokeCSharpAction page→host; synthetic dispatch host→page), bridge ordering incl. preBuffer replay, autoplay flag, focus-claim at ready, graceful exit 0
- [ ] **WSL2 in-packet gate (`~/ccp-sp023`, never /mnt/e):** contract testCommand green; NativeWebDialog render session facts (XGetImage); the FIRST-GATE verdict's transport path exercised (invokeCSharpAction round-trip if PROVEN, or poll-both-ways session facts if FAILED); no input automation (SP-008); no timing claims on Linux; Wayland untouched
- [ ] Write `spine-tasks/SP-023-dtrh-host-b1/record.md` (FIRST-GATE verdict + transcript, archaeology, design decisions, consult verdicts + ACTUAL answering models, engine-review presence, boot matrix transcripts, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` host row → `WIP` with slice-b1 evidence + named limits (Wayland; FIRST-GATE outcome; embedded = Windows-only capability; b2…b5 pending) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild` per the xUnit1051 lesson)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- FIRST-GATE verdict recorded with transcript: `invokeCSharpAction` on NativeWebDialog PROVEN or the poll-both-ways fallback recorded as a named limit (consult-driven)
- Host shell live: Windows embedded + Linux NativeWebDialog, typed capability states, SP-004 owned lifecycle; package pinned per admission §1
- Loopback origin server + inbox endpoint implement §4/§3.3 exactly (token required, seq/ack, long-poll, traversal refusal, GET-only); payload served through SP-009's pipeline (`--verify-assets` green); bridge.js derivative provenance recorded
- Windows headed boot matrix re-run green (real rendered/transport evidence); WSLg session facts per the FIRST-GATE verdict; unit tests green both platforms; board row `WIP` (not `DONE`); both solo Fable consults persisted with actual answering models

## Do NOT

- Build past b1 (slots/picker/protocol-v1-full/SFX/freeze/tint/progression/Loom/media/watchdog are b2…b5); edit the DTRH payload in place (read-only trust anchor — product derivative only for bridge.js, with hashes recorded); unify Windows host→page onto polling (W3–W6 evidence stands); fake an embedded WebView capability on Linux; claim Wayland; add a classic fallback; log sensitive values (token = presence+shape only); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-023): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/task-board.md` (row evidence), `spine-tasks/SP-023-dtrh-host-b1/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only — **UTF-8 only**)

## Amendments

- 2026-07-21 (authoring): **admission record §7 slice cut binding (SP-022 landed `451ac55e`):** b1 = host shell + loopback origins + transport diff + boot matrix re-run in-product; **the named risk is the LITERAL FIRST CHECKBOX (land-consult binding): `invokeCSharpAction` page→host on NativeWebDialog — falsifiable-first-claim with the poll-both-ways fallback pre-decided**; Windows host→page never unified onto polling; payload read-only with bridge.js as a provenance-recorded product derivative; **mustNotChange intersected against File Scope at authoring (SP-020 lesson)**. T-11 sizing: Step 4 is the headed step; orchestrator sets `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch.
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
