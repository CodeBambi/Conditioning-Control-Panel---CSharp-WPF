# Task: SP-011 — spike official WebView with the copied DTRH payload

## Mission

Execute `client/docs/task-board.md` row **"Spike official WebView with the copied DTRH payload"** (P0, Phase 2 of `spine-tasks/CONTEXT.md`). Produce EVIDENCE for the BLOCKED "Admit DTRH browser and origin design" row (owner reviews this spike): does `Avalonia.Controls.WebView 12.0.1` restore/build against the project's Avalonia 12.1.0/net10.0 baseline, and does the exact unchanged DTRH payload boot and behave on Windows and WSLg/X11? Deliver `client/docs/webview-dtrh-spike.md` with a **named observation per acceptance item** (SP-007 pattern — the list is long enough to rot into "works" claims otherwise): bridge ordering, loopback routes, WebGL, workers, WebAudio/autoplay, video seek, CORS-clean media upload, fullscreen, focus, exit, failure injection (bounded to THREE named cases: kill renderer/host process, blocked loopback route, missing media file), startup time, frame behavior.

**Honesty framings (pre-authoring consult, binding):** (a) the package ID/version is a CLAIM to verify first — a wrong ID or restore conflict is a spike FINDING, not a task failure; (b) WSLg/X11 is must-EVIDENCE, not must-pass — a precisely-diagnosed non-boot is acceptance-satisfying evidence for the owner's Linux decision; faking a boot is a contract violation; (c) Wayland stays the named owner question §5.1 — never faked; (d) packaged/bundled serving is the DTRH HOST row's acceptance, NOT this spike's — do not invent it.

## Dependencies

- **Task:** Phase 1 (all rows — harness, capability honesty, publish gates); the spike reads their contracts, changes none of them

## Context to Read First

- `client/docs/task-board.md` — the spike row + the BLOCKED "Admit DTRH browser and origin design" row (what this evidence feeds) + Decisions-needed (WebView/loopback owner questions)
- `client/docs/capability-inventory.md` — DTRH section (~line 231+): web-only host contract (slot choice, required roots, path-traversal ban, no native DTRH overlay recreation, audio/overlay safety)
- `client/docs/runtime-capability-contract.md` — honesty rule (probe-derived claims only; faked availability = violation)
- `ConditioningControlPanel/Resources/web/dtrh/` — READ-ONLY payload (index.html, boot.js, bridge.js, spike.html/spike.js, m2test.js, engine/, game/, shared/, assets/; 383MB — NEVER copy it, NEVER write into it)
- First-attempt DTRH code (`ConditioningControlPanel/CCP.Core/Services/Chaos/DtrhHostOrchestrator.cs`, `DtrhMetaBridge.cs`; `CCP.Avalonia.Desktop.Windows/Services/Chaos/DtrhGameHostService.cs`) — READ-ONLY lessons evidence (what a host does), NOT implementation architecture
- `spine-tasks/SP-007-first-visible-slice/record.md` — WSLg/X11 evidence pattern (XGetImage captures, honest scoping); SP-010 record for publish/native-sidecar facts
- Required skills: load `port-feature`, `avalonia-research` before Step 1

## File Scope

- `client/spikes/CcpSpike.WebView/**` (quarantined spike host project + tracked overlay dir + spike-local scratch; inherits `client/Directory.Build.props` — state this in the evidence doc)
- `client/docs/webview-dtrh-spike.md` (evidence deliverable — named observation per acceptance item)
- `client/docs/task-board.md` (spike-row evidence edit only)
- `spine-tasks/SP-011-webview-dtrh-spike/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo && dotnet build client/spikes/CcpSpike.WebView/CcpSpike.WebView.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/webview-dtrh-spike.md`, `client/spikes/CcpSpike.WebView/CcpSpike.WebView.csproj` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/src/**`, `client/tests/**`, `client/CcpClient.sln`, `.spine/**` |
| artifactsMustExist | `client/docs/webview-dtrh-spike.md`, `spine-tasks/SP-011-webview-dtrh-spike/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 fix validated by this packet:** prior packets wrote the level as bold prose (never parsed → ten zero-review batches); this packet emits the structured heading. Record engine-review presence/absence per call in record.md — if reviews STILL skip with the heading present, the second suspect is the windowsHide spawn mass-patch touching review-spawn (say so explicitly; do not stall).

## Steps

### Step 1: Package verification, payload archaeology, pre-approach consult

- [ ] **FIRST checkbox — verify the package claim:** `Avalonia.Controls.WebView` exists on NuGet at 12.0.1 (or the actual current 12.x); record ID/version/feed URL + license + native-dependency documentation (WebView2 on Windows; WebKitGTK/WPE on Linux — WHICH packages, from current official docs). Wrong ID/version/conflict = spike finding, record and CONTINUE the spike as far as honestly possible
- [ ] Update STATUS.md before starting work
- [ ] Payload archaeology: `git log` on `spike.html`/`spike.js`/`m2test.js` (who added them, for what host — likely the WPF era's own capability-probe surface); read them enough to know what they exercise; record source tree absolute path + file count/hash summary (the "exact payload" claim must be checkable)
- [ ] Restore/build the spike skeleton against the baseline: does 12.0.1 (or actual) resolve with Avalonia 12.1.0 — record the dependency-range outcome empirically
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable — probe failed, seats unproven) with the host design: GET-only loopback into the read-only WPF tree + tracked overlay dir served overlay-first + spike-local scratch dir for any write path + HTTP Range support for video seek; verdict text in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Quarantined spike host + loopback

- [ ] `client/spikes/CcpSpike.WebView/` — minimal Avalonia 12.1.0 app hosting the WebView control, NOT referenced by `CcpClient.sln`, build-only (no test project — acceptance is headed evidence)
- [ ] Loopback server: GET-only routes into the read-only payload tree; overlay-first over the tracked `overlay/` dir (every deviation from the payload is a reviewable tracked file — `bridge.js` stays BYTE-UNCHANGED, the admit row's decision hinges on visible diffs); any write the payload attempts (saves via bridge) is answered from the spike-local scratch dir, NEVER the tree; **HTTP Range support** (video seek depends on it); record the origin/port shape for the admit row's loopback-security decision
- [ ] Windows boot: `index.html` boots in the WebView — the row's literal "exact page boots" claim (never substitute spike.html for this); capture evidence via the SP-007/SP-008 harness patterns (headed capture; K3 image review where pixels matter)

### Step 3: Windows evidence matrix (named observation per item)

- [ ] `spike.html` drives the granular items IF archaeology supports it (reuse, don't reinvent): WebGL, workers, WebAudio/autoplay, video seek, CORS-clean media upload, fullscreen, focus — one named observation + evidence (capture/log/measurement) per item
- [ ] Bridge ordering: observe whether unchanged `bridge.js` initializes before dependent page scripts (console capture or overlay instrumentation) — named observation
- [ ] Loopback routes: enumerate required routes; path-traversal attempt returns refusal (capability-inventory's ban); named observation
- [ ] Exit: clean close + teardown (SP-003 discipline); named observation
- [ ] **Failure injection, bounded THREE:** kill renderer/host process; blocked loopback route; missing media file — observed behavior + recovery per case
- [ ] Startup time (cold time-to-first-frame) + frame behavior (steady-state rendering, no blanks/artifacts at named moments) — measured, not impressions

### Step 4: WSL2 gate — Linux build + WSLg/X11 evidence

- [ ] **WSL2 gate (in-packet pattern):** native-dir copy (`~/ccp-sp011`, NEVER /mnt/e); WebKitGTK/WPE natives installed via `wsl -u root` (no passwordless sudo — port-lessons); record exact packages + versions
- [ ] Restore/build the spike on Linux against the baseline; contract testCommand green on WSL2
- [ ] Boot attempt on WSLg/X11: evidence via XGetImage captures (xgetimage.py pattern); run as much of the Step-3 matrix as the backend honestly supports; **a precisely-diagnosed non-boot (missing backend, dlopen failure, protocol error) is acceptance-satisfying — record the diagnosis, never fake a boot**
- [ ] Budgets: spike build + boot times both platforms (cold precondition verified)

### Step 5: Evidence, board reconciliation, pre-completion consult

- [ ] Write `client/docs/webview-dtrh-spike.md`: named observation per acceptance item (pass/fail/diagnosed-blocked + evidence pointer), package facts (ID/version/license/native deps), restore/dependency-range outcome, origin/route shape for the admit row, Linux diagnosis, budgets, open questions routed to the board's Decisions-needed
- [ ] Write `spine-tasks/SP-011-webview-dtrh-spike/record.md`: design decisions, consult verdicts (provenance), research citations, transcripts, surprises; **record engine-review presence/absence per call (T-2 closure evidence — heading now emitted)**; **probe-row clause: attempt ONE bounded council-mode consult from this worker context (expected to fail per T-7 — the attempt + outcome IS the evidence, non-blocking), record what actually responded**
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence doc + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` spike row → `WIP` with evidence citing webview-dtrh-spike.md (the "Admit DTRH browser" row STAYS BLOCKED — owner reviews the spike; never `DONE`)
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification

- [ ] Contract testCommand passes (product suite green AND spike builds)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths (NO payload files, NO 383MB anything, NO scratch-dir content — overlay + project files only)

## Completion Criteria

- Package claim verified from the live feed (ID/version/license/native deps recorded); restore/build outcome against the 12.1.0 baseline recorded empirically
- `index.html` boot evidence on Windows; WSLg/X11 evidence (boot or precisely-diagnosed non-boot) — never faked, Wayland never claimed
- Named observation + evidence for EVERY acceptance item (bridge ordering, loopback routes, WebGL, workers, WebAudio/autoplay, video seek, CORS-clean media upload, fullscreen, focus, exit, three bounded failure-injection cases, startup time, frame behavior)
- Payload served read-only (GET-only + Range); `bridge.js` byte-unchanged; every deviation a tracked overlay file; source path + file count/hash recorded
- `client/docs/webview-dtrh-spike.md` complete and sufficient for the owner's admit-row review; board row `WIP` (not `DONE`); both solo Fable consults persisted; worker-child council attempt + engine-review presence recorded; no tracked changes outside File Scope

## Do NOT

- Modify `ConditioningControlPanel/**` (READ-ONLY — serve it, never write into it); copy the 383MB payload anywhere; commit payload content or scratch-dir output
- Add `Avalonia.Controls.WebView` (or any package) to the product solution/projects — the spike is quarantined; integration is the BLOCKED admit row's owner-reviewed decision
- Implement the DTRH host-isolation contract, slot logic, or any product feature (that's the host row, blocked on the admit row)
- Fake a Linux boot, claim Wayland, or substitute `spike.html` for the `index.html` boot claim
- Expand failure injection beyond the three bounded cases; create a test project for the spike; set any board row to `DONE`
- Use `consult` council mode EXCEPT the single bounded worker-child probe attempt in Step 5 (expected failure = evidence); all gates stay solo Fable 5
- Weaken SP-003…SP-010 invariants; broaden network/loopback scope beyond the payload's required roots

## Git Commit Convention

- `feat(SP-011): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/webview-dtrh-spike.md` (deliverable), `client/docs/task-board.md` (spike-row evidence), `spine-tasks/SP-011-webview-dtrh-spike/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only)

## Amendments

- 2026-07-19 (authoring): **pre-authoring consult RAN — solo Fable 5 (package-admission gate; council unavailable per failed probe, seats unproven, owner direction 2026-07-19).** Verdicts applied: (a) quarantined `client/spikes/` project endorsed (inherits Directory.Build.props — state it; build-only, no test project; product suite stays the contract base); (b) FIRST checkbox = verify the package claim on the live feed — wrong ID/conflict is a spike finding, not a task failure; (c) payload = serve the WPF tree READ-ONLY via GET-only loopback + tracked overlay-first dir + spike-local scratch writes + HTTP Range; do NOT copy 383MB ("copied payload" = byte fidelity, not disk-copy; packaged serving is the host row's scope); (d) `spike.html` archaeology first — reuse for granular items, never substitute for the `index.html` boot claim; (e) WSLg/X11 must-EVIDENCE not must-pass — precisely-diagnosed non-boot is acceptance-satisfying; (f) named observation per acceptance item (SP-007 pattern); failure injection bounded to three named cases.
- 2026-07-19 (authoring): **`## Review Level: 2` structured heading emitted (T-2 root-cause fix — prior packets' bold-prose level never parsed; regex `^##\s+Review Level:\s*(\d+)` requires the heading).** SP-011's land is the empirical check on whether engine reviews fire; second suspect if not = windowsHide spawn mass-patch. Launch: same-shape packet (6th), straight to real batch after validate/analyze/plan/preflight per owner cycle.
