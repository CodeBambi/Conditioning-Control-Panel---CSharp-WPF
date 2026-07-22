# Task: SP-027 — DTRH host slice b5: watchdog recovery, graceful exit, failure injection (FINAL slice)

## Mission

Execute slice **b5** of `client/docs/dtrh-admission.md` §7 for the `client/docs/task-board.md` row **"Implement web-only DTRH host"** (P0) — the FINAL host slice: heartbeat watchdog + native process-failure detection + relaunch-once recovery + graceful exit + failure injection, on top of SP-026's landed b4. This slice closes the routed limits from b1–b4 (renderer-kill/watchdog/exit-done bounded wait; `0x800700AA` stale-profile-lock; pong). Real product code in `client/src/CcpClient.Desktop/Features/Dtrh/`.

**Honesty framings (binding):** (a) the **W17 zombie class is the center of this slice** (`client/docs/window-behavior-manifest.md` W17 + SP-011 spike): killing the WebView2 renderer processes leaves a BLACK surface whose bridge keeps beating ~28s before going silent, and **`AdapterDestroyed` NEVER fires** — detection needs BOTH the heartbeat watchdog (catches the silence) AND the documented immediate signal (native `ProcessFailed` via the platform handle); where a platform cannot deliver a signal, the typed SP-006 capability state says so and the named limit is recorded — never faked; (b) recovery policy comes from WPF archaeology (`File.cs:line`): relaunch-once, never a restart loop; (c) graceful exit = page wind-down request → bounded `exit-done` wait → watchdog-forced close, with the WPF timeout constant cited; (d) failure-injection evidence must show DETECTION + TYPED OUTCOME (never a crash, never a silent wedge): renderer-kill, blocked-route, missing-media; (e) **DISPLAY3 convention + rect-persistence BINDING (GetWindowRect output in committed run logs) + modal-drive rule;** (f) Linux = WX equivalents where the dialog path allows + honest named limits — no timing claims, no input automation, Wayland never; (g) this is the FINAL slice — the board row's named limits from b1–b5 get CONSOLIDATED in the row text (the row stays WIP, never DONE).

## Dependencies

- **Task:** SP-026 (b4 landed — progression/payout/Loom/media; run lifecycle messages handled)

## Context to Read First

- `client/docs/dtrh-admission.md` §7 (b5's exact scope + evidence classes) + §5 (no classic fallback)
- `client/docs/window-behavior-manifest.md` **W17** (renderer-kill zombie diagnosis: black surface + beats ~28s → silence; `AdapterDestroyed` never fires; native `ProcessFailed` via `TryGetPlatformHandle` = the documented immediate route) + W21 (frame behavior — black ONLY in the W17 state)
- `client/docs/webview-dtrh-spike.md` (SP-011) — failure-injection observations (renderer-kill timeline, blocked-route, missing-media) + the host-row detection question
- `spine-tasks/SP-023-dtrh-host-b1/record.md` (routed: renderer-kill/watchdog/exit-done bounded wait; `0x800700AA` stale-profile-lock), `spine-tasks/SP-025-dtrh-host-b3/record.md` (routed: watchdog/exit-done/pong), `spine-tasks/SP-026-dtrh-host-b4/record.md` (run lifecycle the watchdog must not regress)
- WPF (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/Chaos/DtrhHostService.cs` — `:22` watchdog + ProcessFailed relaunch-once policy, `:33-35` `_exitWatchdog`/`_heartbeatWatch`/`_lastHeartbeatUtc`, `:123` `OnProcessFailed` wiring, `:127` StartHeartbeatWatch, `:149-160` graceful close (page wind-down → watchdog-force after the cited constant), `:176` heartbeat stamp, `:277/:312-320` heartbeat/exit/exit-done/pong dispatch, `:817-823` watch cadence; the stale-profile-lock path (locate the `0x800700AA` handling via repo search — UserDataFolder creation / WebView2 runtime errors)
- `client/docs/port-lessons.md` — DISPLAY3 + rect-persistence + modal-drive + E-series zero-HWND class (failure evidence must not confuse harness wedges with product wedges)
- Required skills: load `wpf-parity`, `dashboard-design` before Step 1; `avalonia-research` before Step 4

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (watchdog, recovery, exit policy, protocol wiring)
- `client/tests/CcpClient.Tests/**` (watchdog/recovery/exit tests)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest)
- `client/docs/task-board.md` (row evidence edit only — FINAL slice: consolidated named limits)
- `spine-tasks/SP-027-dtrh-host-b5/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhWatchdog.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `spine-tasks/SP-027-dtrh-host-b5/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Watchdog/exit/recovery archaeology + design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): heartbeat watch cadence + silence threshold, `OnProcessFailed` path + relaunch-once policy, graceful-close flow + the exit watchdog constant, exit/exit-done/pong handling, stale-profile-lock (`0x800700AA` class) detection + recovery
- [ ] Spike/manifest verification: W17 timeline (black-but-beating ~28s → silence; `AdapterDestroyed` never fires), W21 (black only in the zombie state), SP-011 blocked-route + missing-media observations
- [ ] Design: the detection STACK (heartbeat-silence vs native process-failure — what each catches, in what order); typed capability states for the native signal per platform (probed, SP-006 honesty — Unavailable where the handle cannot deliver it); relaunch-once state machine (never a restart loop); graceful-exit flow on top of the landed run lifecycle (no b3/b4 regressions); stale-profile-lock recovery shape; the injection-harness shape (HARNESS-ONLY flags per the b3/b4 norm)
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Watchdog core (detection stack + relaunch-once)

- [ ] `Features/Dtrh/DtrhWatchdog.cs` (contract-named): heartbeat watch (cadence + silence threshold from archaeology — the W17 black-but-beating window is the sizing case), native process-failure subscription where the platform delivers it (typed capability, probed at runtime), relaunch-once state machine with typed outcomes (relaunching / relaunched / exhausted→honest close)
- [ ] Unit tests: silence detection timing vs the W17 case, heartbeat resume resets, relaunch-once (exactly one relaunch, then typed exhaustion), capability-Unavailable paths never fake a signal, watchdog does not fire during a live session (b4 run-lifecycle regression guard)

### Step 3: Graceful exit + stale-profile recovery

- [ ] Graceful exit: page wind-down request → bounded `exit-done` wait (WPF constant cited) → watchdog-forced close; pong handling; exit path through the landed lifecycle (freeze unwedge + flush per b3/b4 — no regression)
- [ ] Stale-profile-lock recovery (`0x800700AA` class): detect the lock class, recover honestly (typed outcome + logged, never silent, never a crash loop)
- [ ] Unit tests: exit-done fast path, timeout force path, mid-freeze exit (b3 invariant), profile-lock classification + recovery outcome

### Step 4: Failure-injection evidence + consolidated limits + board reconciliation + pre-completion consult

- [ ] **Windows headed evidence on DISPLAY3 (rect-persistence BINDING; modal-drive rule):** (1) renderer-kill injection — watchdog/ProcessFailed detection proven (both signals where the platform delivers them; the black-but-beating window caught), relaunch-once exercised end-to-end, relaunch EXHAUSTION on a second kill → honest close; (2) blocked-route injection — diagnosable typed failure, not a hang; (3) missing-media injection — typed surface, not a crash; (4) graceful-exit matrix (page exit-done fast path, timeout force path, ESC-path regression check)
- [ ] **WSL2 in-packet gate (`~/ccp-sp027`, never /mnt/e):** contract testCommand green; WX equivalents where the dialog path allows (detection/recovery/exit facts); honest named limits where it does not (e.g., native process-failure signal on the WebKitGTK dialog — recorded, never faked); no timing claims; Wayland untouched
- [ ] Write `spine-tasks/SP-027-dtrh-host-b5/record.md` (archaeology, detection-stack design + capability states, consult verdicts + ACTUAL answering models, engine-review presence, injection transcripts WITH rect lines, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` host row → `WIP` with slice-b5 evidence + **CONSOLIDATED named limits from b1–b5** (the slice cut is complete; the row's remaining open items in one place) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild` per the xUnit1051 lesson; counts ≥ the b4 floor 366 unit + 29 headless)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Detection stack live: heartbeat watchdog (W17-sized) + native process-failure signal where the platform delivers it (typed capability, never faked where unavailable)
- Relaunch-once recovery proven end-to-end (incl. exhaustion → honest close); never a restart loop
- Graceful exit delivered (wind-down → bounded exit-done → force close) with no b3/b4 lifecycle regressions; stale-profile-lock recovery typed and proven
- Failure-injection matrix green on Windows DISPLAY3 (renderer-kill / blocked-route / missing-media — detection + typed outcome each) with WX equivalents + honest named limits
- Board row `WIP` with CONSOLIDATED b1–b5 named limits (never `DONE`); contract green both platforms (≥366/29 floor); both solo Fable consults persisted with actual answering models

## Do NOT

- Regress b1–b4 semantics (transports, slots, effects, progression/Loom/media — the contract suite is the guard); fake a process-failure signal where the platform cannot deliver it (typed capability + named limit instead); invent recovery policies beyond WPF + W17 evidence; restart-loop (relaunch-once only); claim Wayland or Linux timing; fake Linux input automation; silently swallow failures; modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-027): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/task-board.md` (row evidence — consolidated limits), `spine-tasks/SP-027-dtrh-host-b5/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only — **UTF-8 only**)

## Amendments

- 2026-07-22 (authoring): **admission record §7 slice cut binding (b5: watchdog recovery + graceful exit + failure injection — the FINAL host slice); SP-026 landed `d0e4a1d9` provides the run lifecycle + all b1–b4 surfaces.** W17 zombie class centered (`AdapterDestroyed` never fires; heartbeat silence + native ProcessFailed stack); b1–b4 routed limits collected (renderer-kill/watchdog/exit-done, `0x800700AA` stale-profile, pong). DISPLAY3 + rect-persistence binding + modal-drive rule + `--dtrh-quick`/fx-drive harness entries carried. FINAL-slice duty encoded: consolidated b1–b5 named limits in the board row. mustNotChange intersected against File Scope at authoring (SP-020 lesson — no overlap). T-11 sizing: Step 4 is the headed step; orchestrator sets `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch.
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
