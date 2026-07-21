# Task: SP-025 — DTRH host slice b3: native SFX/audio/video + freeze + rendered tint safety

## Mission

Execute slice **b3** of `client/docs/dtrh-admission.md` §7 for the `client/docs/task-board.md` row **"Implement web-only DTRH host"** (P0): native SFX cues + native audio/video playback + world freeze + rendered tint safety on top of SP-024's landed b2 (save slots, picker/quick start, protocol v1 full vocabulary with `Deferred(b3)` typed outcomes). Real product code in `client/src/CcpClient.Desktop/Features/Dtrh/`. b3 upgrades its owned protocol messages from `Deferred(b3)` to `Handled` with REAL native effects.

**Honesty framings (binding):** (a) SFX playback uses the **SP-017-SELECTED backend** (`client/docs/audio-backend-spike.md`: SoundFlow primary; explicit channel ownership — SFX = bounded pool, drop-on-overflow, max 8 simultaneous; finding disciplines F1/F3 — re-enumerate devices immediately before init, match devices by NAME, persist NAME never Id; generation/identity tokens from F2) — package admission is a Step-1 gate re-confirmed from the LIVE nuget feed (version/license/natives), never transcription from the spike; (b) freeze semantics come from WPF archaeology (`File.cs:line`): idempotent dedup, stale-freeze cleanup at run start AND run end, mid-freeze teardown unwedge — a video/voice must NEVER wedge paused; (c) **the §3.2 Linux layering divergence is DECIDED WITH EVIDENCE in this slice** (Linux = separate WebKitGTK TOPLEVEL window, no host compositing over the web surface): candidate uniform resolution = in-page tint/freeze via protocol v1 message (platform-identical, uses the §3 transport) vs host-side layering (Windows-only shape) — the decision + rationale + rejected alternative are recorded; (d) **OWNER DISPLAY CONVENTION: all headed evidence windows position on DISPLAY3 ((-2576,1091) 2560×1440), GetWindowRect-verified before captures;** plus the **SP-024 modal-drive rule** (UIA InvokePattern or timed drive for modal buttons; topmost raise only for canvas clicks on NON-modal windows; foreground runs for exit-code evidence); (e) Linux = WX session facts + mechanism evidence only — no timing/latency claims (WSLg jitter, SP-017 named limit), no input automation (SP-008), Wayland never claimed (§5); (f) real media for audio/video evidence comes from the owner dir **`Z:\CCP Vids`** (git-bash `/z/CCP Vids`; videos/ + images/ verified populated 2026-07-22) — COPY chosen files into packet evidence scratch; product code and committed files never reference `Z:\` in-place; (g) sensitive logging: presence+shape only; credentialed/media URLs carry `Cache-Control: no-store` discipline (SP-018 V5).

## Dependencies

- **Task:** SP-024 (b2 landed — slots, picker/quick start, protocol v1 vocabulary with Deferred(b3) outcomes)

## Context to Read First

- `client/docs/dtrh-admission.md` §7 (b3's exact scope + evidence classes) + §3.2 (the named layering divergence) + §5 (no classic fallback)
- `client/docs/audio-backend-spike.md` (SP-017) — backend SELECTION + channel ownership + findings F1–F5 (their disciplines are binding on product code)
- `client/docs/video-handoff-spike.md` (SP-018) — LibVLCSharp admission shape + finding V3 (dummy vout crashes at/after EndReached; vmem memory-callback vout is stable AND gives frame-level decode proof) + V5 (no-store)
- `spine-tasks/SP-024-dtrh-host-b2/record.md` — b2's landed shape (protocol dispatcher + Deferred outcomes to upgrade; harness entry is **`--dtrh-quick`** since b2 — plain `--dtrh-demo` now opens the picker; E-series forensics = the modal-drive rule)
- WPF DTRH host (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/Chaos/DtrhHostService.cs` — `_worldFrozen` (`:43`), VN portrait tint/mod URLs (`:186-189`), sfx scale default 0.6 (`:226`), sfx dispatch incl. `wave_clear`/`ripple_cast` special-cases (`:232-240`), `freeze-state` (`:246-247`), stale-freeze cleanup at run start (`:259`) and run end (`:513`), `freeze-caught` bark (`:639`), `ApplyWorldFreeze` idempotent dedup (`:671-698`), mid-freeze window-death unwedge (`:896`); `ChaosSfx` (SFX names, cooldowns, pool shape — locate via repo search); the native video/voice playback the freeze pauses
- The READ-ONLY DTRH payload (`ConditioningControlPanel/Resources/web/dtrh/`, tree `40be29df`) — `protocol.js` message shapes for sfx/freeze/tint/audio/video (b2's `DtrhProtocol.cs` already types them; verify the payload fields here)
- `client/docs/window-behavior-manifest.md` — the board row cites W15 (native-cue message path)
- `client/docs/port-lessons.md` — DISPLAY3 convention + modal-drive rule entries (2026-07-21)
- Required skills: load `wpf-parity`, `dashboard-design` before Step 1; `avalonia-research` before Step 4

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (native effects service, protocol upgrade, host wiring)
- `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` (admitted package pins ONLY)
- `client/tests/CcpClient.Tests/**` (effects/pool/freeze/protocol-upgrade tests)
- `client/tests/CcpClient.HeadlessTests/**` (tint/freeze surface draw-level tests where honest)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-025-dtrh-host-b3/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `spine-tasks/SP-025-dtrh-host-b3/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: SFX/freeze/tint/video archaeology + design + package admission + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): sfx message handling (names, scale default, special-cases, cooldowns/pool in `ChaosSfx`), freeze-state semantics (idempotency, stale cleanup at run start/end, teardown unwedge — the sites listed above), the tint/portrait path (`:186-189` and whatever it composes), and WHAT native audio/video the DTRH host actually plays (the video + voice the freeze pauses — `:43`, `:673-677`, `:896`)
- [ ] Payload `protocol.js` verification (READ-ONLY): exact field shapes for the b3-owned messages (sfx, freeze-state, tint/portrait, audio/video playback) against b2's `DtrhProtocol.cs` records
- [ ] **Package admission gate (solo Fable consult):** SoundFlow re-confirmed from the LIVE nuget feed (exact version, license expression, bundled natives, TFM) before any csproj pin; video backend decision with evidence (the SP-018-admitted LibVLCSharp shape incl. the V3 vmem discipline vs an honest narrower cut) — the cut MUST cover whatever freeze pauses, else freeze evidence is hollow
- [ ] Design: `DtrhNativeEffects` shape (channel ownership per the spike selection; bounded SFX pool; generation tokens; device disciplines); freeze service semantics; **the §3.2 divergence decision framing** (in-page vs host-side, with the evidence each alternative requires)
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the archaeology + admission + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Native effects core (SFX + freeze + tint mechanism)

- [ ] `Features/Dtrh/DtrhNativeEffects.cs` (contract-named): native SFX playback through the admitted SoundFlow pin (bounded pool, drop-on-overflow at 8, scale default 0.6, `wave_clear`/`ripple_cast` special-cases, generation tokens, device re-enumerate-before-init + match-by-NAME); world freeze (idempotent dedup; stale-freeze cleanup hooks for run start/end; teardown unwedge — never leave audio/video wedged paused); tint mechanism per the Step-1 divergence decision
- [ ] Unit tests: pool bounds + drop-on-overflow, freeze idempotency + stale cleanup + unwedge invariants, tint state transitions, tolerance preserved (unknown/forward-version still never crashes)

### Step 3: Protocol upgrade — b3-owned messages Deferred → Handled

- [ ] b3-owned messages in the host dispatcher (`DtrhProtocol` outcomes + `DtrhHostWindow` wiring): `sfx`, `freeze-state`, tint/portrait, audio/video playback per the verified vocabulary — real effects via `DtrhNativeEffects`, typed outcomes, presence+shape logging only
- [ ] Run lifecycle invariants wired: stale freeze cleared at run start AND run end (WPF `:259`/`:513` parity); window teardown mid-freeze un-wedges (`:896` parity)
- [ ] Unit tests: every upgraded message dispatches to the real effect seam (recorded fake — never the real backend in unit tests); ordering + idempotency; `Deferred` remains for b4/b5-only messages

### Step 4: Headed/WX evidence + divergence decision executed + board reconciliation + pre-completion consult

- [ ] **Windows headed evidence on DISPLAY3 (owner convention — GetWindowRect before captures; modal-drive rule):** SFX cues fired from the page protocol → backend-event-verified playback (SP-017 discipline: completion/interruption events, never call-returned); freeze visuals + tint **pixel-verified** (before/during/after captures); native audio/video playback using real media copied from `Z:\CCP Vids` into packet evidence scratch; freeze lifts → playback resumes (never wedged); run-end mid-freeze → clean teardown
- [ ] **WSL2 in-packet gate (`~/ccp-sp025`, never /mnt/e):** contract testCommand green; WX session facts for tint/freeze surfaces (XGetImage, no input automation); SFX mechanism evidence on the Linux backend (RDP Sink class facts per SP-017 — no timing/latency claims); the divergence decision executed on Linux with the result recorded
- [ ] Write `spine-tasks/SP-025-dtrh-host-b3/record.md` (archaeology, admission evidence, divergence decision + rationale, consult verdicts + ACTUAL answering models, engine-review presence, evidence transcripts, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` host row → `WIP` with slice-b3 evidence + named limits (Wayland; Linux timing; remaining slices b4/b5; the divergence decision's consequence) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild` per the xUnit1051 lesson; counts ≥ the b2 floor 292 unit + 27 headless)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Native SFX cues play through the admitted SoundFlow backend with bounded-pool/drop-on-overflow + device disciplines, backend-event-verified on Windows and mechanism-verified on Linux
- World freeze implemented with WPF-parity idempotency + stale cleanup + teardown unwedge; freeze/resume pixel/event-verified on DISPLAY3; never a wedged paused state
- Rendered tint delivered per the **evidence-decided §3.2 divergence resolution** (decision + rationale + rejected alternative recorded); tint safety pixel-verified
- b3-owned protocol messages upgraded `Deferred` → `Handled` with real effects; tolerance suite still green; counts ≥ 292/27 floor on both platforms
- Board row `WIP` with named limits (never `DONE`); both solo Fable consults persisted with actual answering models

## Do NOT

- Build past b3 (progression/payout/Loom/user-media = b4; watchdog/exit-done/pong/stale-profile = b5); bypass the SP-017 backend selection or the SP-018 vout discipline; add packages beyond the Step-1 admission gate; edit the DTRH payload in place (read-only evidence); reference `Z:\` paths from product code or committed files (copy into evidence scratch); claim Linux timing/latency; fake Linux input automation; claim Wayland; log sensitive values (presence+shape only); silently drop messages; modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-025): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/task-board.md` (row evidence), `spine-tasks/SP-025-dtrh-host-b3/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only — **UTF-8 only**)

## Amendments

- 2026-07-21 (authoring): **admission record §7 slice cut binding (b3: native SFX/audio/video + freeze + rendered tint safety); SP-024 landed `a842c639` provides slots/picker/protocol-v1-full-vocabulary with Deferred(b3) outcomes to upgrade.** DISPLAY3 convention + SP-024 modal-drive rule + `--dtrh-quick` harness entry encoded. Owner real-media dir `Z:\CCP Vids` admitted for evidence scratch (owner directive 2026-07-22). mustNotChange intersected against File Scope at authoring (SP-020 lesson — no overlap; csproj pin entry is the Desktop head's, the sln itself untouched). T-11 sizing: Step 4 is the headed step; orchestrator sets `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch.
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
