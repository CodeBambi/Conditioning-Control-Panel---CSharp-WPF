# Status: SP-002 — bootstrap discovery and architecture proposal

**Overall:** 🔄 In Progress — Step 3

## Steps

### Step 1: Pre-approach consult and architecture proposal
**Status:** ✅ Complete

- [x] Pre-approach solo consult (Fable 5) run and verdict recorded in record.md
- [x] STATUS.md updated before starting work
- [x] `client/docs/architecture-proposal.md` written (topology, package baseline, composition root, A-### citations, lesson dispositions)
- [x] Flagged owner questions section complete
- [ ] Proposed spine testing commands recorded in record.md

### Step 2: Minimal client scaffolding
**Status:** ✅ Complete

- [x] `client/CcpClient.sln` + desktop app project (Avalonia 12.1.0, net10.0) + `client/tests/CcpClient.Tests` created
- [x] Restore + Debug build succeed with 0 warnings
- [x] Composition-root shape instantiated minimally (no features)

### Step 3: WSL2 Ubuntu 26.04 build attempt
**Status:** 🔄 In Progress

- [ ] WSL2 dotnet SDK verified/installed; scaffold build attempted from WSL
- [ ] Outcome or named manual gate recorded in record.md

### Step 4: Evidence, board reconciliation, pre-completion consult
**Status:** ⬜ Not Started

- [ ] record.md written (proposal summary, consult verdicts, build outputs, surprises)
- [ ] Pre-completion solo consult (Fable 5) run and recorded
- [ ] Board row 1 → `WIP` with evidence (not `DONE`)
- [ ] STATUS.md accurate

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand passes
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
