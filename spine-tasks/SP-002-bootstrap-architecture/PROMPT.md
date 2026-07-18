# Task: SP-002 — bootstrap discovery and architecture proposal

## Mission

Execute `client/docs/task-board.md` row 1 (**"Bootstrap discovery and architecture proposal"**, P0, Phase 1 of `spine-tasks/CONTEXT.md`): produce `client/docs/architecture-proposal.md` — a Windows/Linux solution proposal that **instantiates** the owner decisions A-001…A-014 (already in `client/docs/architecture.md`) into concrete project topology, package baseline, and composition-root shape — then create the minimal `client/` scaffolding proving that topology compiles on Windows, and attempt the same build under WSL2 Ubuntu 26.04. The proposal is reviewed asynchronously by the owner and by a solo Fable 5 consult before Phase 1 rows 2–9 are authored; it **decides nothing new** — it maps existing owner decisions to structure and flags gaps as owner questions.

## Dependencies

- **None** (SP-001 landed; this is the first Phase 1 row)

## Context to Read First

- `client/docs/architecture.md` — owner decisions A-001…A-014 (highest authority; the proposal cites the A-### it implements per section)
- `client/docs/capability-inventory.md` — WPF behavior inventory (skim; full archaeology is rows 2+)
- `client/docs/first-attempt-lessons.md` and `client/docs/first-attempt-systemic-lessons.md` — ACCEPT/ADAPT/REJECT lessons the proposal must disposition
- `client/docs/row-1-research-inputs.md` — current Avalonia 12.1.0 facts (versions, breaking changes, lifetime/dispatcher, Linux backends) resolved from official sources 2026-07-18
- `spine-tasks/CONTEXT.md` — Phase 1 scope and execution policy
- Required skills: load `port-plan` and `avalonia-research` before Step 1; `wpf-parity` when citing WPF behavior

## File Scope

- `client/**` (new solution scaffolding + `client/docs/architecture-proposal.md`)
- `client/docs/task-board.md` (row-1 evidence edit only)
- `spine-tasks/SP-002-bootstrap-architecture/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/architecture-proposal.md`, `client/CcpClient.sln` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/architecture-proposal.md`, `spine-tasks/SP-002-bootstrap-architecture/record.md` |

**Review Level 2 (plan + code)** — architecture proposal gates 8 downstream Phase 1 rows. Call `spine_review_step` after each step. If reviewer spawn stalls (unproven Level 2+ environment, SP-190/#150), the orchestrator will amend to Level 1 and rely on the land-time Fable consult.

## Steps

### Step 1: Pre-approach consult and architecture proposal

- [ ] Run a **pre-approach solo consult** (Fable 5 via `consult` tool, mode solo) with the planned proposal outline; record the verdict in record.md
- [ ] Update STATUS.md Step 1 checkboxes before starting work (SP-001's recorded gap — do not repeat it)
- [ ] Write `client/docs/architecture-proposal.md`: project topology under `client/` (heads, core library, test project placement), package baseline (Avalonia 12.1.0 / net10.0 per row-1-research-inputs; `Avalonia.Controls.WebView`, LibVLCSharp, Wayland package only as flagged candidates, not admissions), DI/composition-root shape, and a section per relevant A-### decision citing which lessons are ACCEPT/ADAPT/REJECT
- [ ] Include a **flagged owner questions** section for every gap the proposal cannot resolve from A-001…A-014 alone
- [ ] Record the proposed real `testing.build` / `testing.test` commands for `.spine/spine-config.json` in record.md (orchestrator applies them at land time — workers never edit `.spine/`)

### Step 2: Minimal client scaffolding

- [ ] Create `client/CcpClient.sln` with a desktop Avalonia app project (Avalonia 12.1.0, net10.0) and `client/tests/CcpClient.Tests/CcpClient.Tests.csproj` (xunit v3 per repo convention) with one trivial passing test proving the harness runs
- [ ] `dotnet restore` and `dotnet build client/CcpClient.sln -c Debug --nologo` succeed with 0 warnings
- [ ] The app project instantiates the proposal's composition-root shape minimally (entry point + App builder) — no features, no UI beyond a placeholder window

### Step 3: WSL2 Ubuntu 26.04 build attempt

- [ ] Attempt: `wsl -d Ubuntu-26.04` (or the installed 26.04 distro name) — verify/install dotnet SDK 10 in WSL, then `dotnet build` the scaffold from the WSL filesystem path
- [ ] Record outcome in record.md. If WSL2/dotnet setup cannot be driven from the worker lane context, record the exact blocker as the named manual gate — the row stays WIP, which is a legitimate outcome

### Step 4: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-002-bootstrap-architecture/record.md`: proposal summary, consult verdicts (pre-approach + pre-completion), build outputs (Windows and WSL2-attempt), scaffold file list, surprises
- [ ] Run a **pre-completion solo consult** (Fable 5, solo) on the diff and proposal; record the verdict
- [ ] Update `client/docs/task-board.md` row **"Bootstrap discovery and architecture proposal"** to `WIP` with evidence text citing record.md — **never** `DONE`; that flip follows owner review of the proposal
- [ ] Update STATUS.md — all checkboxes reflect reality before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes: `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `architecture-proposal.md` exists, cites A-### per section, dispositions first-attempt lessons, lists flagged owner questions
- `client/CcpClient.sln` builds green on Windows with 0 warnings; test project test passes
- WSL2 build attempted with outcome or named manual gate recorded
- Both solo Fable consults run and recorded; STATUS.md accurate; board row `WIP` with evidence (not `DONE`)
- No tracked changes outside File Scope; `.spine/` untouched

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (WPF + first attempt are read-only evidence)
- Make new architecture decisions — instantiate A-001…A-014 only; gaps become flagged owner questions
- Invent structure beyond A-014's YAGNI constraint (no generic frameworks, platform seams, or abstractions without a real consumer)
- Admit any dependency beyond Avalonia 12.1.0 + xunit for the scaffold; WebView/LibVLC/Wayland packages are flagged candidates only
- Use `consult` council mode (seats unproven — solo Fable 5 only)
- Set any board row to `DONE`
- Skip or fake STATUS.md updates or consult checkboxes (reviewers check them)

## Git Commit Convention

- `feat(SP-002): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/task-board.md` (row-1 evidence), `spine-tasks/SP-002-bootstrap-architecture/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (append only if a durable surprise emerges)

## Amendments

- 2026-07-18 (authoring, per pre-approach Fable consult): worker does NOT edit `.spine/spine-config.json` (issue #149 — Do NOT zone); proposed testing commands are recorded in record.md and applied by the orchestrator at land time. WSL2 build attempt added as explicit Step 3 (ratified as part of row 1). Stub run skipped — packet shape matches SP-001's proven shape; stubs cannot pass `fileScopeMustChange` contracts (port-lessons 2026-07-18).
