## STATUS: SP-048 — DTRH published-artifact payload location
**Current Step:** Step 1 — evidence + decision + pre-approach consult
**Last Updated:** 2026-08-05 (authored)
**Blockers:** none

### Step 1: evidence + decision + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Empirical inventory (real publish output; runtime payload-root resolution; size/integrity trade-offs)
- [ ] Decision in record.md (chosen location + rejected alternatives + integrity discipline)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: implement the decided shape
- [ ] Payload root resolution per mode (Debug/Release/published)
- [ ] Publish wiring if the decision changes glob/scripts
- [ ] Tests (resolution per mode; SP-010 publish gates non-regression)

### Step 3: published boot proof + evidence + pre-completion consult
- [ ] Published win-x64 boots the DTRH host (engine live, §4 green)
- [ ] --verify-assets exit 0 on the published artifact
- [ ] record.md (inventory, decision, consults, review presence, transcripts)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥601/33 floor)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
