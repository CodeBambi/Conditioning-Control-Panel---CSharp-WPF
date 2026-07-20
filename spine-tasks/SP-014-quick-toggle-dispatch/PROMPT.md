# Task: SP-014 — replace card-title quick-toggle dispatch

## Mission

Execute `client/docs/task-board.md` row **"Replace card-title quick-toggle dispatch"** (P0, Phase 2 of `spine-tasks/CONTEXT.md`), SCOPED to the demonstrator card with named limits (owner-approved Phase-2 decomposition: one card, one theme exist; full multi-card/multi-theme acceptance awaits real cards). The mechanism being REPLACED is **dispatch keyed on the card's title string** (localized, mutable — breaks on title change); the replacement is **stable-identity dispatch**: every unlocked toggleable card uses a stable card ID and one command path; plain right-click toggles immediately, live-starts/stops, saves, and updates its ring; locked/help/Visuals/System exceptions follow a documented contract; no plain-right-click context-menu substitution.

**Honesty framings (pre-authoring consult, binding):** (a) the client has NO localization system (A-014 honest absence, SP-009/SP-010 verified) — language-switch invariance is a NAMED LIMIT; the falsifiable proof with one card/one language is the **title-mutation negative test**: mutate the demonstrator card's displayed title (test-only), prove right-click dispatch still resolves via the stable ID — this test is the row's core claim; (b) no session concept exists — "live-starts/stops during a session" = re-cite SP-007's real SP-004 operation liveness + NEW cross-proof: right-click toggle WHILE the SP-013 modeless popup is open; the WPF-session sense stays a named limit; (c) exceptions (locked/help/Visuals/System) are CONTRACT-ONLY with WPF `File.cs:line` evidence per class — none are demonstrable with one card; never synthesize fake locked cards; (d) contract + demonstrator evidence only — NO multi-card framework or registry abstraction beyond what one card needs (A-005-style trap); (e) board row annotate-don't-rewrite with named limits (languages, themes, exceptions, session) spelled out; row never `DONE`.

## Dependencies

- **Task:** SP-013 (the modeless popup enables the toggle-while-popup-open cross-proof; demonstrator card from SP-007 is the subject)

## Context to Read First

- `client/docs/task-board.md` — the quick-toggle row + Decisions-needed + SP-007 gate history (demonstrator framing)
- `client/docs/capability-inventory.md` — Dashboard section (~line 93-118): "plain right-click anywhere on the body of every unlocked, toggleable card immediately reverses its enabled state; does not open the settings popup or require a context-menu choice"
- WPF sources (READ-ONLY): `ConditioningControlPanel/Controls/FeatureCard.xaml.cs:248-261` (right-click path) + the title-keyed dispatch mechanism being replaced (find it — card construction/registration sites) + exception-card behavior (locked/help/Visuals/System)
- First-attempt dispatch path under `ConditioningControlPanel/CCP.*/` (READ-ONLY lessons) + `client/docs/first-attempt-lessons.md` — ACCEPT/ADAPT/REJECT dispositions for this mechanism
- `spine-tasks/SP-007-first-visible-slice/record.md` — one-command-path toggle evidence; `spine-tasks/SP-013-popup-scrolling/record.md` — "dashboard interactions keep working" modeless evidence
- Required skills: load `wpf-parity`, `avalonia-research` before Step 1

## File Scope

- `client/src/CcpClient.Desktop/**` (stable-ID dispatch on the demonstrator card — minimal)
- `client/tests/CcpClient.Tests/**` (dispatch unit tests + title-mutation negative test)
- `client/tests/CcpClient.HeadlessTests/**` (draw-level dispatch interaction tests where honest)
- `client/docs/quick-toggle-dispatch.md` (dispatch contract deliverable)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-014-quick-toggle-dispatch/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/quick-toggle-dispatch.md`, `client/tests/CcpClient.Tests/QuickToggleDispatchTests.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `client/docs/quick-toggle-dispatch.md`, `spine-tasks/SP-014-quick-toggle-dispatch/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Archaeology of the title-keyed mechanism + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (READ-ONLY, `File.cs:line`): the ACTUAL title-keyed dispatch mechanism being replaced (card construction/registration — where title text keys toggle dispatch), `FeatureCard.xaml.cs:248-261` right-click path, exception-card behavior per class (locked/help/Visuals/System)
- [ ] First-attempt `CCP.*` dispatch path (READ-ONLY) + first-attempt-lessons dispositions for this mechanism (ACCEPT/ADAPT/REJECT — cite the lesson IDs)
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable) with the stable-identity design + title-mutation negative test plan; verdict text in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Dispatch contract + stable-identity implementation

- [ ] `client/docs/quick-toggle-dispatch.md`: the dispatch contract — stable card ID as the ONLY dispatch key (title is display text, never a key); ONE command path (right-click body region ≡ keyboard Enter ≡ same operation); immediate toggle; ring from operation liveness (SP-007 rule, never the flag); persistence (SP-005); exception taxonomy per class with WPF evidence; click-region parity (body, per capability-inventory); named limits section (languages, themes, exceptions, session)
- [ ] Minimal implementation on the demonstrator card: stable ID drives dispatch end-to-end (no title-text keying anywhere on the path); NO multi-card framework
- [ ] `client/tests/CcpClient.Tests/QuickToggleDispatchTests.cs`: stable-ID resolution, one-command-path convergence, **title-mutation negative test** (mutate displayed title → dispatch still resolves via ID), exception-contract guards where exercisable

### Step 3: Windows-headed evidence

- [ ] Right-click toggle evidence on the changed code: immediate toggle, ring update, persistence file-content proof — re-asserted (SP-007 claims re-verified, not assumed)
- [ ] **Cross-proof: right-click toggle WHILE the SP-013 modeless popup is open** (dashboard stays live; one operation through the same path)
- [ ] Negative proofs: plain right-click opens NO context menu; title-region vs body-region behavior matches the contract; locked/help/Visuals/System classes recorded as contract-only named limits (no fake cards)
- [ ] A-013 MCP advisory IF AXAML changed (ValidateXaml ONLY — AnalyzePerformance always reject, template rule); K3 visual only where pixels changed

### Step 4: WSL2 gate + board reconciliation + pre-completion consult

- [ ] WSL2 in-packet gate (native-dir copy, never /mnt/e): contract testCommand green (title-mutation test included); X11 session facts where honestly obtainable (no input automation — SP-008 limit)
- [ ] Write `spine-tasks/SP-014-quick-toggle-dispatch/record.md`: archaeology findings, lesson dispositions, consult verdicts (provenance — ACTUAL answering model), engine-review presence, evidence, surprises
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the contract + evidence + diff; verdict text in record.md
- [ ] Update `client/docs/task-board.md` row → `WIP` with evidence + named limits (languages, themes, exception classes, WPF-session sense, multi-card acceptance) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (build 0W/0E + both test projects green incl. new tests)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- The replaced mechanism (title-keyed dispatch) pinned with WPF `File.cs:line` + first-attempt path + lesson dispositions cited
- `client/docs/quick-toggle-dispatch.md` contract complete (stable-ID keying, one command path, immediate toggle, ring-from-liveness, persistence, exception taxonomy, click-region parity, named limits)
- Title-mutation negative test green on Windows AND WSL2 — the row's core claim falsifiably proven
- Windows-headed re-verification + toggle-while-popup-open cross-proof + no-context-menu negative proof
- Board row `WIP` (not `DONE`) with named limits; both solo Fable consults persisted; no multi-card framework; no fake exception cards

## Do NOT

- Build a multi-card registry/framework beyond one card's needs (A-005-style trap); synthesize locked/help/Visuals/System cards; claim language/theme invariance (no localization system, one theme — named limits)
- Key ANY dispatch decision on title/display text; open a context menu on plain right-click; weaken SP-004/SP-005/SP-007/SP-013 invariants
- Modify `ConditioningControlPanel/**` (READ-ONLY); claim the WPF-session sense of live-start/stop; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only); use A-013 `AnalyzePerformance` (self-contradictory — ValidateXaml only)

## Git Commit Convention

- `feat(SP-014): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/quick-toggle-dispatch.md` (deliverable), `client/docs/task-board.md` (row evidence), `spine-tasks/SP-014-quick-toggle-dispatch/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only)

## Amendments

- 2026-07-20 (authoring): **pre-authoring consult RAN — solo Fable 5 (requested `anthropic/claude-fable-5`; council unavailable per failed probe).** Verdicts applied: (a) the row's core target is IDENTITY-BY-TITLE-STRING replacement — stable-ID dispatch is the center of gravity, Step-1 pins the actual WPF + first-attempt mechanism with File.cs:line (never guess what's being replaced); (b) localization does NOT exist in the client (A-014) — language invariance is a named limit; the falsifiable proof is the title-mutation negative test; (c) live-start/stop = re-cite SP-007 liveness + toggle-while-popup-open cross-proof; WPF-session sense = named limit; (d) exceptions contract-only, no fake cards; (e) contract + demonstrator scope, no framework; (f) Size M endorsed; (g) concrete fileScopeMustChange deliverables (prelanded-warning avoidance); (h) A-013 ValidateXaml-only.
- 2026-07-20 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
