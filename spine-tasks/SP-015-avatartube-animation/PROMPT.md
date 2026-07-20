# Task: SP-015 — prove AvatarTube rendered animation

## Mission

Execute `client/docs/task-board.md` row **"Prove AvatarTube rendered animation"** (P0, FINAL row of Phase 2 in `spine-tasks/CONTEXT.md`): static pose fades, looping animation, idle/talk/reaction emote crossfades, click reaction, gentle float, pause/resume, mod switching, attach/detach, owner transitions, and cleanup produce **changing rendered-frame evidence** on Windows/Linux with no blanks, duplicates, multiplied speed, or growing timers/subscriptions. Build an explicitly-labeled DEMONSTRATOR AvatarTube surface (SP-007/SP-013 pattern: really-functioning, superseded-by-first-real, owner may async-veto) rendering real animated **synthetically-generated** asset packs routed through SP-009's manifest pipeline.

**Honesty framings (pre-authoring consult, binding):** (a) **animated-GIF decode in Avalonia 12 is an UNVERIFIED CLAIM — the packet's FIRST checkbox** (SP-011 pattern): verify the pinned 12.1.0 surface (pinned-package XML/source, NEVER the docs site — WindowDecorations lesson); a missing decoder is a FINDING, and the fallback demonstrator shape is own-frame composition (decode stills, drive frames via an SP-004-owned timer) — which makes the acceptance's timing properties directly testable; route the decision through the in-packet pre-approach consult with research in hand; (b) **"mod switching" = switching between TWO synthetic packs routed through the same SP-009 manifest path** (embedded entries, case-exact IDs, `--verify-assets` green) — there is NO mod loader (SP-009: schema covers mod, instances do not); real mod-loader semantics = named limit; (c) the owner questions stay UNANSWERED: transition/liveness constants = demonstrator values pending-owner; undecodable-asset fallback = surfaced as a typed SP-006 capability state (static fallback + bounded diagnostics as MECHANISM); the warning-vs-diagnostics UX choice is recorded pending-owner, never implemented; (d) synthetic frames are **machine-checkable by construction** — pixel counter strip encoding frame index + pack ID, NON-UNIFORM frame delays (a uniform-delay asset cannot falsify multiplied-speed), deterministic generation hashed in record.md; (e) cadence/multiplied-speed claims are **Windows-headed only** — WSLg capture jitter supports frame-deltas + no-blanks as session facts, never timing assertions; (f) WSLg: no input automation (SP-008 limit); Wayland §5.1 untouched.

## Dependencies

- **Task:** SP-014 (final Phase-2 chain link; dashboard/dispatch surface is the demonstrator host)

## Context to Read First

- `client/docs/task-board.md` — the AvatarTube row + Decisions-needed (transition/liveness constants; undecodable-asset fallback UX) + SP-009 gate history (manifest: schema covers user/mod, instances do NOT)
- `client/docs/capability-inventory.md` — AvatarTube/avatar sections
- `client/docs/asset-manifest.md` — SP-009 catalogue schema (embedded entries, case-exact IDs, `--verify-assets`)
- WPF sources (READ-ONLY): `ConditioningControlPanel/AvatarTubeWindow.xaml(.cs)` + avatar/animation services — the behavior evidence for fades/crossfades/click/float/pause/attach/detach/owner transitions
- First-attempt `CCP.*` AvatarTube code (READ-ONLY) + `client/docs/first-attempt-lessons.md` — cite AvatarTube timer/subscription-leak REJECT lessons (the acceptance's "no growing timers/subscriptions" targets exactly this scar tissue)
- `spine-tasks/SP-008-verification-harness/record.md` — CcpVerify named-check pattern; `spine-tasks/SP-013-popup-scrolling/record.md` — X11 settled-tree lesson; SP-004 contract — owned operations/timer ownership
- Required skills: load `wpf-parity`, `avalonia-research`, `dashboard-design` before Step 1

## File Scope

- `client/src/CcpClient.Desktop/**` (demonstrator AvatarTube surface + animation engine + synthetic pack generator)
- `client/src/CcpClient.Desktop/Assets/**` + `client/src/CcpClient.Desktop/Assets/assets.manifest.json` (synthetic pack entries through the SP-009 pipeline)
- `client/tests/CcpClient.Tests/**` (engine/pipeline/cadence/leak-count tests)
- `client/tests/CcpClient.HeadlessTests/**` (draw-level tests where honest)
- `client/docs/avatartube-demonstrator.md` (evidence deliverable)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-015-avatartube-animation/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/AvatarTube/SyntheticAvatarPacks.cs`, `client/docs/avatartube-demonstrator.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `client/docs/avatartube-demonstrator.md`, `spine-tasks/SP-015-avatartube-animation/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Decoder claim verification + AvatarTube archaeology + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] **FIRST checkbox — verify the decode/animate claim:** what does the pinned Avalonia 12.1.0 surface actually decode/animate (pinned-package XML/source, never the docs site)? Record the finding; a missing built-in animated-GIF decoder is a FINDING, not a failure — own-frame composition is then the demonstrator shape
- [ ] WPF + first-attempt AvatarTube archaeology (READ-ONLY, `File.cs:line`): fade/crossfade/click/float/pause-resume/attach-detach/owner-transition/cleanup behaviors; **first-attempt timer/subscription-leak REJECT lessons cited explicitly**
- [ ] Owner-transition archaeology decides: which owner/attach/detach transitions are demonstrable with the dashboard as owner vs contract-only named limits
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable) with the decoder finding + engine design (own-frame composition vs verified decoder — SP-004-owned timer either way) + evidence plan; verdict text in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Synthetic asset pipeline + animation engine

- [ ] `client/src/CcpClient.Desktop/Features/AvatarTube/SyntheticAvatarPacks.cs` (contract-named): deterministic generator of TWO packs — frames machine-checkable by construction (pixel counter strip encoding frame index + pack ID), NON-UNIFORM frame delays; generation hashed in record.md
- [ ] Both packs routed through SP-009's manifest (embedded entries, case-exact IDs); `--verify-assets` green with the new entries on Debug + Release
- [ ] Animation engine: frame progression driven by an SP-004-owned operation/timer (generation-invalidated on detach/close); typed undecodable-asset path → SP-006 capability state (static fallback + bounded diagnostics mechanism; UX choice pending-owner)
- [ ] Unit tests: cadence math (non-uniform delays honored, multiplied-speed detection), pause/resume successor-frame + unchanged-cadence, pack-switch cleanliness, timer/subscription counts from REAL registries stable across N attach/detach/pack-switch cycles, undecodable-asset typed state

### Step 3: Demonstrator surface + behaviors

- [ ] Demonstrator AvatarTube surface (explicitly labeled): static pose fade-in, looping, idle/talk/reaction emote crossfades, click reaction, gentle float, pause/resume, pack switching, attach/detach, owner transitions per Step-1 archaeology, cleanup
- [ ] Behaviors implemented through the ONE engine (no parallel timers); transition/liveness constants = demonstrator values recorded pending-owner

### Step 4: Windows-headed evidence matrix

- [ ] Rendered-frame DELTAS via headed capture + CcpVerify named checks on the frame-indexed strip: frames advance (N vs N+k differ), no blank frames, no duplicate-run beyond hold count, cadence vs asset delays (multiplied-speed detection) — per behavior
- [ ] **Resume-fast-forward check:** after pause/resume the next frame is the SUCCESSOR of the paused frame and cadence is unchanged — not just "deltas resume"
- [ ] Leak long-run: one headed run with many attach/detach/pack-switch cycles — registry counts observed stable
- [ ] Click reaction + crossfade sequences captured as named sequences; K3 visual where pixels matter; A-013 ValidateXaml-only advisory if AXAML changed

### Step 5: WSLg/X11 gate + board reconciliation + pre-completion consult

- [ ] WSL2 in-packet gate (native-dir copy, never /mnt/e): contract testCommand green; WSLg render + frame-delta + no-blanks session facts via XGetImage sequences (settled-tree reads per the id-churn lesson); cadence/timing NOT claimed on Linux (jitter); click/owner-transition evidence stays Windows-headed named gates
- [ ] Write `client/docs/avatartube-demonstrator.md` (evidence per behavior) + `spine-tasks/SP-015-avatartube-animation/record.md` (decoder finding, archaeology, lesson citations, consult verdicts + ACTUAL answering models, engine-review presence, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` row → `WIP` with evidence + named limits (mod-loader semantics, owner-constant values, undecodable-asset UX choice, Linux cadence/click/owner gates, Wayland §5.1) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Decoder claim verified from the pinned 12.1.0 surface; engine shape decided with research + pre-approach consult recorded
- Two synthetic packs generated deterministically (frame-indexed, non-uniform delays, hashed) and routed through SP-009's manifest; `--verify-assets` green Debug + Release
- Every acceptance behavior delivered with changing rendered-frame evidence: fades, looping, crossfades, click reaction, float, pause/resume (successor-frame + unchanged cadence), pack switching, attach/detach, owner transitions (real or contract-only per archaeology), cleanup; no blanks/duplicates/multiplied speed/growing timers-subscriptions PROVEN (CcpVerify named checks + real-registry counts)
- Windows-headed cadence/timing evidence; WSLg frame-delta/no-blanks session facts; Wayland untouched; named gates recorded
- Undecodable-asset path = typed SP-006 capability state (mechanism only, UX pending-owner); first-attempt leak lessons cited; board row `WIP` (not `DONE`); both solo Fable consults persisted

## Do NOT

- Assume a built-in animated-GIF decoder exists (verify first — the claim is unverified); build a mod loader or claim mod semantics; implement the owner-question UX choices (constants, warning-vs-diagnostics)
- Copy WPF assets into the client (read-only evidence); use uniform-delay synthetic frames (cannot falsify multiplied-speed); claim cadence/timing from WSLg captures; automate input on WSLg; claim Wayland
- Create timers/subscriptions outside SP-004 ownership; leave growing registries; modify `ConditioningControlPanel/**`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only); use A-013 `AnalyzePerformance` (ValidateXaml only)

## Git Commit Convention

- `feat(SP-015): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/avatartube-demonstrator.md` (deliverable), `client/docs/task-board.md` (row evidence), `spine-tasks/SP-015-avatartube-animation/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only)

## Amendments

- 2026-07-20 (authoring): **pre-authoring consult RAN — solo Fable 5 (requested `anthropic/claude-fable-5`; council unavailable per failed probe).** Verdicts applied: (a) animated-GIF decode = UNVERIFIED CLAIM, first checkbox (SP-011 pattern), own-frame composition as the likely-correct shape (timing machinery becomes testable), decision routed through the in-packet pre-approach consult; (b) mod switching = two synthetic packs through SP-009's manifest path, real mod-loader semantics = named limit; (c) undecodable-asset = typed SP-006 capability state mechanism, UX choice pending-owner; (d) synthetic frames machine-checkable by construction (pixel counter strip: frame index + pack ID; NON-UNIFORM delays; hashed); (e) cadence claims Windows-headed only, WSLg = deltas/no-blanks session facts; (f) resume-fast-forward = successor-frame + unchanged-cadence assertion; (g) leak counts from REAL registries across N cycles in tests + one headed long-run; first-attempt leak REJECTs cited; (h) Size L endorsed (headed-evidence-heavy; SP-013 timeout lesson).
- 2026-07-20 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
