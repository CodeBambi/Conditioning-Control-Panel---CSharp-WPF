## STATUS: SP-048 — DTRH published-artifact payload location
**Current Step:** Step 4 — Testing & Verification (in progress)
**Last Updated:** 2026-08-05 (worker run 1)
**Blockers:** none

### Step 1: evidence + decision + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Empirical inventory (real publish output; runtime payload-root resolution; size/integrity trade-offs)
- [x] Decision in record.md (chosen location + rejected alternatives + integrity discipline)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: implement the decided shape
- [x] Payload root resolution per mode (Debug/Release/published)
- [x] Publish wiring if the decision changes glob/scripts
- [x] Tests (resolution per mode; SP-010 publish gates non-regression)

### Step 3: published boot proof + evidence + pre-completion consult
- [x] Published win-x64 boots the DTRH host (engine live, §4 green)
- [x] --verify-assets exit 0 on the published artifact
- [x] record.md (inventory, decision, consults, review presence, transcripts)
- [x] Pre-completion solo consult (verdict + actual model in record.md)
- [x] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥601/33 floor)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
