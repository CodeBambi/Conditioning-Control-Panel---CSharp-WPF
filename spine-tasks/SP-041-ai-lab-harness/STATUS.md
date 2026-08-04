## STATUS: SP-041 — T-15: c2 AI lab harness hardening
**Current Step:** Step 1 — harness archaeology + fix design + pre-approach consult (in progress)
**Last Updated:** 2026-08-04 (worker, Step 1 started)
**Blockers:** none

### Step 1: archaeology + fix design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Listener lifecycle read + before-state failure shape captured
- [x] Design (teardown discipline, fresh-instance-per-bind, host-exit guarantee, leaked-listener self-check)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: harden the harness
- [ ] AiProviderLab.cs lifecycle + teardown + self-check
- [ ] Consumer files: host-exit discipline only (assertions untouched unless justified)
- [ ] Full matrix green with identical lab semantics

### Step 3: stability proof + evidence + pre-completion consult
- [ ] 5 consecutive full-suite runs green (transcripts; hosts reaped; no leaked dotnet test hosts)
- [ ] Self-check demonstrated (throwaway leak injection fails loud; committed suite leak-free)
- [ ] record.md (before-state, design, justifications, transcripts, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + counts EXACTLY 516/29)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
