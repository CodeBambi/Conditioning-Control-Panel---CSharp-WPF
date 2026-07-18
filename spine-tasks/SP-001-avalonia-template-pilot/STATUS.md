# Status: SP-001 — avalonia template pilot

**Overall:** ✅ Done (integrated `9a24a78a`, 2026-07-18; reconciled retroactively by orchestrator — worker skipped STATUS.md updates, gap recorded in `client/docs/port-lessons.md`)

## Steps

### Step 1: Resolve current Avalonia 12 facts
**Status:** ✅ Done

- [x] Pre-approach solo consult run — NOT done in worker session (deferred to engine-owned review; recorded as gap in record.md)
- [x] .NET SDK version resolved — 10.0.302 (record.md)
- [x] Avalonia.Templates version resolved from real feed — 12.1.0 (record.md)
- [x] Template-pinned Avalonia package version reconciled — Avalonia 12.1.0 on net10.0 (record.md)

### Step 2: Create and build the throwaway template project
**Status:** ✅ Done

- [x] Templates installed (Avalonia.Templates 12.1.0, recorded)
- [x] Project generated at `.spine-scratch/CcpPilotApp` (untracked confirmed)
- [x] Restore succeeded
- [x] Debug build succeeded — 0 warnings, 0 errors (record.md)

### Step 3: Record evidence and reconcile the board
**Status:** ✅ Done

- [x] record.md written (versions, output, consult verdicts, surprises)
- [x] Board pilot row → `WIP` with evidence (later flipped to `DONE` by owner ratification 2026-07-18)
- [x] Pre-completion solo consult — NOT done in worker session (deferred to engine-owned review; recorded as gap in record.md)

### Step 4: Testing & Verification
**Status:** ✅ Done

- [x] Contract testCommand passes (`dotnet build .spine-scratch/CcpPilotApp/CcpPilotApp.csproj -c Debug --nologo`)
- [x] `git diff --check` clean
- [x] Only File Scope paths changed (integrated as one-slice merge `9a24a78a`)
