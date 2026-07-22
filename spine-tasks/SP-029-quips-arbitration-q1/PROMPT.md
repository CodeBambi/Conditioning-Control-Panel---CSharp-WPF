# Task: SP-029 — Quips/sound arbitration slice q1: arbitration core (channels, queueing, ducking, panic cleanup)

## Mission

Execute the FIRST slice of the `client/docs/task-board.md` row **"Implement reliable quips and sound arbitration"** (P0 — blockers discharged: SP-017 spike landed, owner ratified the SoundFlow selection 2026-07-21): the **arbitration core** — explicit channel ownership per the SP-017 selection, ordinary/priority queueing with freshness, reference-counted ducking, and panic/error cleanup, on the admitted SoundFlow backend. Real product code in `client/src/CcpClient.Desktop/Audio/` (new home). Content-pipeline items (bark text/audio/emotion payload integrity, mute text-only, disabled phrase persistence, rapid click cues, stale-device fallback UX) = **q2** (not this task — the core must make them possible: the stale-device RE-PROBE discipline lands in this slice's device layer).

**Honesty framings (binding):** (a) the backend is the SP-017-SELECTED one (SoundFlow primary; channel ownership: voice = exclusive stop-replace + generation token; whisper = exclusive with real-event busy; SFX = bounded pool, drop-on-overflow, max 8; ONE generic player REJECTED) with the SP-025 port-lessons discipline: **any SoundFlow player/provider construction OFF the SynchronizationContext** (dump-proven deadlock class) and the spike's device rules (re-enumerate immediately before init, match devices by NAME, persist NAME never Id — F1/F2/F3); (b) WPF semantics come from archaeology (`File.cs:line`) — freshness windows, priority/preemption, ducking reference-counting, panic cleanup — never invented; (c) **ENABLER 2 (first packet): the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — row evidence and durable lessons are recorded in record.md; the orchestrator reconciles at land; (d) Linux = WSLg session facts + mechanism evidence (RDP Sink class), never timing/latency claims (WSLg jitter — SP-017 named limit); Wayland never claimed; (e) audio evidence is **backend-event-verified** (PlaybackEnded/state transitions), never call-returned and never audibility-claimed (volume = mechanism-only).

## Dependencies

- **Task:** SP-028 (T-5 patch landed — 2-lane-era gate)

## Context to Read First

- `client/docs/audio-backend-spike.md` (SP-017) — the SELECTION + channel ownership + findings F1–F5 disciplines + A11 coexistence probe (voice+whisper+3 SFX concurrent) + stale-device TOCTOU named limit (this slice owns the re-probe mechanism)
- `client/docs/port-lessons.md` 2026-07-22 SoundFlow sync-over-async entry (off-sync-context construction — binding) + the SP-025 `DtrhNativeEffects` shape (the DTRH-specific consumer pattern this slice must NOT duplicate — arbitration is APP-WIDE; DtrhNativeEffects stays the DTRH-local owner and a future refactor may route it through arbitration — record the boundary, do NOT refactor DTRH in this task)
- WPF (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/Companion/BarkService.cs` (queueing, freshness, priority, panic cleanup), `ConditioningControlPanel/Services/Bark/` (BarkRule/Loader/Set/State/Variant/Context — the rules engine surface the core must serve), `CompanionPhraseService.cs` (phrase/audio pairing), the audio ducking service (locate via repo search — reference-counted ducking of media audio under barks), `ChaosSfx` (cap-6 pool precedent vs the SP-017-selected 8)
- `client/docs/task-board.md` row "Implement reliable quips and sound arbitration" (acceptance text; q1/q2 split is this packet's slice cut, recorded in record.md)
- Required skills: load `wpf-parity` before Step 1; `avalonia-research` before any UI-surface work (q1 is service-core — load only if a surface appears)

## File Scope

- `client/src/CcpClient.Desktop/Audio/**` (new arbitration core home)
- `client/tests/CcpClient.Tests/**` (arbitration/queueing/ducking/device tests)
- `client/tests/CcpClient.HeadlessTests/**` (surface tests where honest — likely none for q1; recorded if absent)
- `spine-tasks/SP-029-quips-arbitration-q1/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-029-quips-arbitration-q1/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: WPF arbitration archaeology + design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): BarkService queue/freshness/priority/panic semantics; the ducking service's reference-counting (who holds ducks, release discipline, overlapping ducks); channel/preemption rules; device selection/fallback paths; the rapid-click-cue path's demands on SFX latency (q2's consumer — recorded, not built)
- [ ] Design: `Audio/SoundArbitration.cs` (contract-named) — channel ownership state machine per the SP-017 selection (voice exclusive stop-replace + generation token; whisper exclusive + real-event busy; SFX bounded pool 8 drop-on-overflow), ordinary/priority queue model with freshness windows (WPF-cited), reference-counted ducking (acquire/release symmetry, overlapping holders, panic release-all), device layer with the re-probe discipline (re-enumerate-before-init, match-by-NAME, stale→fallback, never a process-fatal Id), all player construction off-sync-context; the q1/q2 boundary (what the core exposes to the content pipeline)
- [ ] **Pre-approach solo consult** (Fable 5, solo) with the archaeology + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Arbitration core implementation

- [ ] `Audio/SoundArbitration.cs` + supporting types: channel ownership state machine, queue + freshness, ducking refcount, device re-probe layer, off-sync-context player construction; typed outcomes everywhere (never silent drops — drop-on-overflow is a TYPED, logged outcome per SP-017)
- [ ] Unit tests: ownership transitions (stop-replace generations; stale-generation discard), queue ordering + freshness expiry, ducking refcount symmetry (incl. overlapping + panic release-all), device re-probe (stale NAME → fallback; missing → typed outcome; never an unvalidated Id), off-sync-context construction proof (a sync-context thread does not deadlock — the SP-025 regression test)

### Step 3: Backend-event evidence + panic cleanup + WSL gate

- [ ] Windows backend-event evidence (console harness, no pixels needed): voice completion/interruption (PlaybackEnded + generation semantics), whisper busy set/cleared by real events, SFX 8/8 overlap + drop-on-overflow at 9th (typed + logged), ducking acquire/release under real playback (volume state transitions), panic cleanup (error mid-play → typed outcome + all channels released + no wedged players)
- [ ] **WSL2 in-packet gate (`~/ccp-sp029`, never /mnt/e):** contract testCommand green; Linux mechanism session facts (RDP Sink class enumeration, PlaybackEnded events — NO timing claims); teardown leak counts (handles/threads bounded, SP-017 discipline)
- [ ] Write `spine-tasks/SP-029-quips-arbitration-q1/record.md` (archaeology, design, q1/q2 boundary, consult verdicts + ACTUAL answering models, engine-review presence, evidence transcripts, budgets, surprises, durable-lesson candidates for the orchestrator's land reconcile)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 391/29 floor)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Arbitration core live with the SP-017 channel-ownership semantics (voice exclusive stop-replace + generation, whisper exclusive real-event busy, SFX pool 8 drop-on-overflow typed) — backend-event-verified on Windows, mechanism facts on Linux
- Ordinary/priority queueing + freshness windows (WPF-cited) + reference-counted ducking (symmetry proven incl. panic release-all)
- Device re-probe discipline implemented (re-enumerate-before-init, match-by-NAME, stale→typed fallback — the SP-017 TOCTOU named limit's mechanism owned)
- Panic/error cleanup proven (no wedged channels/players; teardown leak counts bounded both platforms)
- Contract green both platforms (≥391/29 floor); both solo Fable consults persisted with actual answering models; the q1/q2 boundary recorded

## Do NOT

- Refactor `Features/Dtrh/**` (DtrhNativeEffects stays the DTRH-local owner — boundary recorded, refactor = future row); build bark content/payload/persistence/rapid-cue UX (q2); claim Linux timing/latency; claim audibility (mechanism-only); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); add packages beyond the SP-017-admitted set; silently drop anything (typed + logged outcomes); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`; set any board row state
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-029): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-029-quips-arbitration-q1/record.md` (evidence + durable-lesson candidates)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-07-22 (authoring): **FIRST 2-LANE-ERA PACKET (owner plan #215).** Enabler 2 encoded: worker File Scope excludes task-board.md + port-lessons.md (orchestrator reconciles at land); mustNotChange carries both hot docs. Slice cut q1 (arbitration core) vs q2 (content pipeline: payload integrity, mute text-only, disabled phrase persistence, rapid click cues, stale-device UX) recorded by the orchestrator; row blockers discharged (SP-017 landed + owner ratification 2026-07-21). Waved with SP-030 (AI companion admission — disjoint scope, both non-headed). mustNotChange intersected against File Scope at authoring (SP-020 lesson). T-11 sizing: no DISPLAY3 step (audio evidence is console/backend-event); 4h budget exported at launch for consistency.
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached wave batch (SP-029 + SP-030, 2 lanes) per owner cycle.
