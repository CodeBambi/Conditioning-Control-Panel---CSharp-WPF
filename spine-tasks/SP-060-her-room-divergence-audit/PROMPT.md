# Task: SP-060 — Her Room + Awareness divergence audit (ZERO product code)

## Mission

While this port built the AI companion in seven slices (c1–c7: SP-033/035/038/040/042/044/046, plus SP-047's memory→prompt wiring), **upstream shipped its own companion redesign**: the Companion tab became **Her Room**, with a new brain, opt-in awareness that notices PC activity and reacts, a plain-words privacy dial (**nothing / app names / + page titles**), an app picker, incognito detection, a one-hour pause, and a **Mute Voice Lines** switch. Two designs now exist for one product surface.

Produce the decision input, not the decision: a **per-element divergence table** across upstream's shipped semantics and the port's landed contracts, each element carrying exactly one verdict — **ADOPT / KEEP / MERGE / BLOCKED-ON-OWNER** — with evidence, plus a packet-sizing verdict for everything that would become work. **Zero product code, zero tests, zero contract edits** (the SP-050 audit shape).

**Binding framings:**
(a) **This audit decides nothing that needs a decree.** Adopting upstream's redesign over landed, reviewed, tested c1–c7 behavior is an **OWNER decision**. `ADOPT` in this table means "the audit found no reason to keep the port's version and no boundary is broadened"; anything touching consent defaults, retention, moderation policy, or user-visible product identity is **BLOCKED-ON-OWNER** no matter how technically easy it looks.
(b) **Privacy is a hard filter, not a sizing input.** Awareness observes PC activity: app names, page titles, incognito state, media sessions. Any element that would broaden the port's webcam/capture/logging/consent/network boundaries is **BLOCKED-ON-OWNER** by rule (constitution: never broaden those boundaries). Say plainly, per element, what data it would newly observe, retain, or transmit — including "none".
(c) **Evidence or nothing.** Every upstream semantic cites `File.cs:line` (or the asset path); every port-side counterpart cites the landed contract section and `File.cs:line`. An element you cannot source gets an explicit UNKNOWN row, never an inferred one.
(d) **Compare user-observable outcomes, not class topology.** Upstream's file count is not a gap. The question per element is: what does the user experience, and does the port's landed behavior already deliver it? A port element that already delivers the outcome by different mechanics is `KEEP`, not `MERGE`.
(e) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/ai-operation-contract.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`.** This is a 2-lane parallel wave (constitution: parallel waves reconcile at land). The audit's own new doc is the deliverable; the orchestrator files rows and the contract owner applies contract changes after the decree.

## Dependencies

- none (wave-17 lane-2; disjoint from lane-1, which touches only tests)

## Context to Read First

- `client/docs/task-board.md` — the row "Her Room + Awareness: reconcile the port's AI companion against upstream's shipped design (v6.7)" (READ-ONLY; its acceptance is this task's acceptance)
- Upstream (READ-ONLY behavioral evidence): `ConditioningControlPanel/Services/Companion/**` (incl. `Brain/`), `ConditioningControlPanel/Services/Awareness/**` (23 files: observer/policy/privacy-rules/intensity + migration/pause/probes/projection/prompt-builder/reaction/cooldown-ledger/worthiness/SMTC watcher/activity ledger), `ConditioningControlPanel/Views/Controls/Companion/**`, `ConditioningControlPanel/Resources/sounds/companion_audio/awareness_apps.json`
- `client/docs/ai-operation-contract.md` — the port's landed contract (§4 awareness consent + cooldown semantics, §5 memory seam, §1 typed replies) — the port side of every comparison
- `client/docs/ai-companion-admission.md` — the admission that scoped c1–c7 (§8 slice definitions)
- The landed port implementation (`client/src/CcpClient.Desktop/**` AI/companion/awareness surfaces + `Features/**` composition) and the slice records `spine-tasks/SP-042-ai-companion-c5/record.md` (awareness), `SP-044-ai-companion-c6/record.md` (commands), `SP-046-ai-companion-c7/record.md` (the UI surface), `SP-047-memory-prompt-context/record.md`
- `spine-tasks/SP-050-v663-obligation-audit/record.md` — the audit shape this task follows (per-item verdict + sizing + orchestrator-side filings)
- `client/docs/upstream-sync.md` + `client/docs/main-sync-2026-08-04.md` — how the v6.7 delta was enumerated (READ-ONLY; note the standing lesson that the sync ledger has been incomplete three times — enumerate from the TREE, not the ledger)

## File Scope

- `spine-tasks/SP-060-her-room-divergence-audit/**` (STATUS.md, record.md, evidence, .DONE)
- `client/docs/her-room-divergence-audit.md` — **new file only**, the durable audit deliverable
- **NOT in scope:** `ConditioningControlPanel/**`, `client/src/**`, `client/tests/**`, `client/docs/ai-operation-contract.md`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `.spine/**`, `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/her-room-divergence-audit.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/src/**`, `client/tests/**`, `client/docs/ai-operation-contract.md`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-060-her-room-divergence-audit/record.md`, `client/docs/her-room-divergence-audit.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Upstream enumeration from the tree + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Enumerate upstream's shipped semantics **from the tree** (Companion services + Brain, Awareness services, Companion views, the awareness app-list asset): per element — what the user observes, what data it touches, what it persists, what it requires consent for — each with `File.cs:line`
- [ ] Name the privacy-relevant surface explicitly: the three privacy levels (nothing / app names / + page titles), incognito detection, the one-hour pause, app-picker scoping, SMTC media observation, Mute Voice Lines — with their upstream defaults as **facts** (`File.cs:line`), never as assumptions
- [ ] **Pre-approach solo consult** (`mode: "solo"` — bare `consult` hits the council-roster trap, T-7; Opus 5 main, Fable 5 fallback per the pause protocol) on the enumeration + the verdict rubric; verdict + **ACTUAL answering model** in record.md before checking this box

### Step 2: Port-side inventory (what c1–c7 actually landed)

- [ ] Per upstream element, the port's counterpart: landed behavior + its contract section + `File.cs:line`, or an explicit "no counterpart"
- [ ] Record the port's **defaults and named limits** as facts — in particular the awareness-consent placeholder (`Denied`) and the recorded WPF-true-vs-placeholder tension from SP-047, cooldown families from SP-042, the memory read-gating semantics, and the typed refusal/unavailable vocabulary
- [ ] Flag every place where the port's landed behavior is a **deliberate divergence already decided** (with the decision's citation) — those are `KEEP` unless new evidence overrides, and the audit must not silently reopen them

### Step 3: The divergence table + privacy verdicts

- [ ] One row per element: upstream semantic (cited) | port counterpart (cited) | user-observable difference | **verdict: ADOPT / KEEP / MERGE / BLOCKED-ON-OWNER** | reason
- [ ] Per row, the **data-boundary line**: what this element would newly observe / retain / transmit if adopted — "none" is an answer, silence is not. Anything broadening a boundary is BLOCKED-ON-OWNER regardless of verdict pressure
- [ ] Collect the BLOCKED-ON-OWNER rows into a single **owner decision list**, each stated as one plain question with the options and their consequences (the owner should be able to answer without reading the table)

### Step 4: Sizing verdicts, audit doc + pre-completion consult

- [ ] Per ADOPT/MERGE row: packet-sizing verdict (S/M/L + evidence class + dependencies + honest limit shape, incl. any Linux/headed gate the element would need)
- [ ] Write `client/docs/her-room-divergence-audit.md`: the enumeration, the table, the owner decision list, the sizing verdicts — durable, self-contained, readable by a future worker who was not here
- [ ] Write `record.md`: method, sources, consults + ACTUAL answering models, engine-review presence, UNKNOWN rows, surprises, intended board filings (orchestrator writes them at land), durable-lesson candidates
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the table + verdicts; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes — **zero product change, so counts are EXACTLY the floor: 862 unit / 33 headless** (TRX logger attached). A count that is not exactly the floor means something was touched: stop and explain it
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every enumerated upstream element has a sourced verdict (ADOPT / KEEP / MERGE / BLOCKED-ON-OWNER) with a port-side counterpart or an explicit "no counterpart"
- Every row states what data it would newly observe, retain, or transmit
- A single owner decision list, each item answerable as a plain question
- Sizing verdicts for every element that would become work
- `client/docs/her-room-divergence-audit.md` exists and stands alone; the contract file is untouched
- Zero product change (862/33 exact); both solo consults persisted with actual answering models

## Do NOT

- Write product code or tests; edit `client/docs/ai-operation-contract.md` (the contract changes after the owner decrees, not in the audit)
- Decide adoption of upstream's redesign over landed c1–c7 behavior, or reopen an already-decided divergence without new evidence
- Infer a semantic you cannot cite (use UNKNOWN); treat upstream file count as a parity gap
- Recommend anything that broadens a privacy/consent/logging/network boundary; propose consent defaults, retention values, or moderation policy values (all owner-supplied)
- Modify `ConditioningControlPanel/**`, `client/src/**`, `client/tests/**`, `.spine/**`, `.pi/**`, or the three hot docs; set any board row state
- Use `consult` council mode (T-7: solo only — a bare `consult` call errors with the stale synthesizer seat)

## Git Commit Convention

- `feat(SP-060): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `client/docs/her-room-divergence-audit.md`, `spine-tasks/SP-060-her-room-divergence-audit/record.md`
**Explicitly NOT updated by the worker:** `client/docs/ai-operation-contract.md`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md` (orchestrator/owner apply at land)

## Amendments

- 2026-08-12 (authoring, orchestrator): wave-17 lane-2, per the wave-16 decomposition consult (zero-product-code archaeology — a product packet now would invent scope) and confirmed by the wave-17 consult, which paired it with SP-059 precisely because it touches **no tests** (SP-059 lands a deadline-literal guard; a test-bearing lane-mate would be a merge-time landmine). Audit output is a NEW doc rather than an edit to `ai-operation-contract.md`, so the audit cannot be mistaken for the decree. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-12 (authoring, orchestrator): enumerate from the TREE, not the sync ledger — the ledger has been incomplete three consecutive times (SP-037/SP-055/SP-058, the last of which found `GamificationBridge.cs +157` only via a widened sweep).
