# STATUS: SP-010 — establish Release and publish gates

**Current Step:** 1
**Last Updated:** 2026-07-19 (Step 1 started)

## Steps

### Step 1: Pre-approach consult, current-docs research, contract draft
**Status:** 🔄 In Progress

- [x] Pre-approach solo Fable 5 consult (verdict in record.md BEFORE checkbox)
- [x] STATUS.md updated before starting work
- [x] Current-docs research: .NET 10 single-file native extraction semantics; AppContext.BaseDirectory/Assembly.Location under single-file; residual Linux deps (ldd method); Avalonia 12.1 publish guidance (URLs + freshness recorded)
- [x] `client/docs/release-publish-gates.md` written (strategy + revisit trigger, version rule, matrix, metadata shape, honest absences)

### Step 2: Version authority + derivation tests
**Status:** ⬜ Not Started

- [ ] `client/Directory.Build.props` single `<Version>`; SDK flow confirmed
- [ ] Runtime version surface reads InformationalVersion ATTRIBUTE (no path/Location reads)
- [ ] Publish script derives naming via `dotnet msbuild -getProperty:Version`
- [ ] Derivation tests green (attributes + artifact name; no blind equality)

### Step 3: Windows artifact evidence matrix
**Status:** ⬜ Not Started

- [ ] win-x64 self-contained single-file published; size + command recorded
- [ ] Matrix green on Debug/Release/published: startup+shutdown exit 0, --verify-assets (published = row-8 discharge), fresh-profile, corrupt-settings quarantine, data-path identity, logs-absence
- [ ] Windows native-deps expectation vs observed recorded

### Step 4: WSL2 gate — Linux matrix + WSLg headed smoke
**Status:** ⬜ Not Started

- [ ] Native-dir copy (~/ccp-sp010), full contract testCommand green on WSL2
- [ ] linux-x64 published; same matrix green incl. --verify-assets on all three modes
- [ ] ldd residual-deps floor recorded
- [ ] WSLg headed smoke: rendered window (XGetImage) + clean exit (rows 2/3 debt)
- [ ] Graceful close via WM_DELETE_WINDOW attempted (discharged or honestly renamed)
- [ ] Budgets measured both platforms (cold precondition verified)

### Step 5: Evidence, board reconciliation, pre-completion consult
**Status:** ⬜ Not Started

- [ ] record.md complete (incl. engine-review presence/absence — T-2)
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] task-board.md: row 9 → WIP with evidence; rows 2/3/8 discharge annotations
- [ ] STATUS.md accurate before .DONE

### Step 6: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (solution + both test projects)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only (client/tools check-ignore audit done)
