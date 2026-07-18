# Task: SP-001 — avalonia template pilot

## Mission

Prove the pi-spine pipeline end-to-end on this repository with a bounded throwaway spike: create an official Avalonia 12 template project in an ignored scratch directory, restore and build it on Windows with a real `dotnet` contract testCommand, and record the exact SDK/template/package versions. This is the pilot for the `client/docs/task-board.md` row **"Pilot pinned spine batch workflow"** — it proves packet discipline, review, contract verify, gate, and integrate before any milestone-1 packet is authored. It produces **no** product code and closes **no** product capability.

## Dependencies

- **None**

## Context to Read First

- `docs/constitution.md` — standing orders (authority order, read-only zones, board reconciliation)
- `client/docs/task-board.md` — the pilot row and the probe row (consult council is unproven; use **solo** consults only)
- `client/docs/port-workflow.md` §Pilot — the owner judges pass/fail against that section, not this packet

## File Scope

- `spine-tasks/SP-001-avalonia-template-pilot/**` (STATUS.md, record.md, .DONE)
- `client/docs/task-board.md` (pilot row evidence only)
- `.spine-scratch/**` (untracked throwaway project — never committed)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build .spine-scratch/CcpPilotApp/CcpPilotApp.csproj -c Debug --nologo` |
| fileScopeMustChange | `spine-tasks/SP-001-avalonia-template-pilot/record.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**` |
| artifactsMustExist | `spine-tasks/SP-001-avalonia-template-pilot/record.md` |

**Review Level 1 (plan only)** — blast radius and reversibility are near zero, but plan review is deliberately enabled to exercise the review machinery as part of the pilot. Call `spine_review_step` after each step.

## Steps

### Step 1: Resolve current Avalonia 12 facts

Run a **pre-approach solo consult** (`consult` tool, mode solo) with the planned spike shape. Then resolve from the real feeds — never guess:

- [ ] Installed .NET SDK version (`dotnet --list-sdks`; pick the SDK the template will use)
- [ ] Current `Avalonia.Templates` package version from the actual NuGet feed (`dotnet new search avalonia` or `dotnet new install Avalonia.Templates` output)
- [ ] The Avalonia package version the template pins (read the generated `.csproj` in Step 2 and reconcile)

### Step 2: Create and build the throwaway template project

- [ ] `dotnet new install Avalonia.Templates` (record the exact installed template version)
- [ ] `dotnet new avalonia.app -o .spine-scratch/CcpPilotApp` (path is ignored by git — verify with `git status --short`; it must NOT appear)
- [ ] `dotnet restore .spine-scratch/CcpPilotApp/CcpPilotApp.csproj`
- [ ] `dotnet build .spine-scratch/CcpPilotApp/CcpPilotApp.csproj -c Debug --nologo` succeeds

### Step 3: Record evidence and reconcile the board

- [ ] Write `spine-tasks/SP-001-avalonia-template-pilot/record.md`: exact SDK version, template package version, Avalonia package version(s) from the generated csproj, the build command and its final output lines, consult verdicts (pre-approach and pre-completion), and any surprises
- [ ] Update the `client/docs/task-board.md` row **"Pilot pinned spine batch workflow"** to `WIP` with evidence text "pilot executed via SP-001, owner admission decision pending" — **never** set it `DONE`; that flip is the owner's
- [ ] Run a **pre-completion solo consult** on the diff and record the verdict in record.md

### Step 4: Testing & Verification

- [ ] Contract testCommand passes: `dotnet build .spine-scratch/CcpPilotApp/CcpPilotApp.csproj -c Debug --nologo`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths (no `.spine-scratch/` entries — it is gitignored)

## Completion Criteria

- record.md exists with all versions resolved from real feeds and both consult verdicts
- Board pilot row is `WIP` with evidence, not `DONE`
- No tracked changes outside File Scope; no product code created; `ConditioningControlPanel/` and `client/` (except the board row) untouched

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (WPF + first attempt are read-only evidence)
- Create anything under `client/` except the single board-row edit
- Guess any Avalonia version or API — resolve from the actual template feed
- Use `consult` council mode (seats unproven — solo only)
- Set any board row to `DONE`
- Commit `.spine-scratch/**`

## Git Commit Convention

- `chore(SP-001): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/task-board.md` (pilot row evidence)
**Check If Affected:** none

## Amendments

- 2026-07-18 (authoring): `client/docs/task-board.md` removed from `fileScopeMustChange` — it pre-landed on the base branch during the engine switch and would false-satisfy contract verify. The board update remains required via Step 3 and Documentation Requirements; the contract deliverable is `record.md`.
