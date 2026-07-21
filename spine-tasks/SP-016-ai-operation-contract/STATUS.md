# STATUS: SP-016 — define provider-neutral AI operation contract

**Current Step:** DONE — all steps complete
**Last Updated:** 2026-07-21 (Step 5 green: Rebuild 0W/0E, 213/213 + 22/22, diff-check clean, scope clean)

### Step 1: AI archaeology + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] WPF + first-attempt archaeology (READ-ONLY, `File.cs:line`)
- [x] First-attempt strategy/parser ACCEPT/ADAPT/REJECT dispositions cited explicitly
- [x] Owner-question inventory recorded pending-owner
- [x] Pre-approach solo Fable 5 consult (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Contract document
**Status:** ✅ Complete

- [x] `client/docs/ai-operation-contract.md` — named section per acceptance item
- [x] Every section traces to archaeology evidence or marked greenfield-decision

### Step 3: Typed vocabulary + seam mechanics + tests
**Status:** ✅ Complete

- [x] `client/src/CcpClient.Desktop/Ai/AiOperationVocabulary.cs` + siblings
- [x] Memory/secret seams declared-only (no implementations)
- [x] Unit tests: envelope reject-by-default, per-command results, diagnostic content-freedom, generation-invalidation reuse, serialization round-trips

### Step 4: WSL2 gate + board reconciliation + pre-completion consult
**Status:** ✅ Complete

- [x] WSL2 in-packet gate (`~/ccp-sp016`, never /mnt/e): contract testCommand green
- [x] record.md complete
- [x] Pre-completion solo Fable 5 consult (verdict in record.md)
- [x] Board row → `WIP` with evidence + named limits (never `DONE`)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (build 0W/0E incl. `-t:Rebuild`; both test projects)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only
