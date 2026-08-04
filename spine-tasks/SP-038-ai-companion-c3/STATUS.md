## STATUS: SP-038 — AI companion slice c3: moderation boundary
**Current Step:** Step 1 — archaeology + surface inventory + design + pre-approach consult (work complete, plan review pending)
**Last Updated:** 2026-08-04 (worker, Step 1 started)
**Blockers:** none

### Step 1: archaeology + inventory + design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (4 call sites, ModerationCounter mechanism, sentinel REJECTED, removed keyword pre-check)
- [x] Greenfield surface/command-field inventory (wired vs reserved, coverage-honesty table)
- [x] Design (policy seam, taxonomy, boundary positions, escalation mechanism, state location)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: boundary mechanism + pipeline wiring
- [ ] AiModerationBoundary.cs + policy seam + verdict types on c1's pipeline
- [ ] Escalation counter mechanism (typed, placeholder thresholds)
- [ ] Unit tests (taxonomy, injected-policy posture, surfacing classes, guard-outside-model)

### Step 3: coverage-honesty tests + escalation behavior + offline/diagnostics
- [ ] Boundary-coverage inventory tests (wired/reserved assertions + completeness tripwire)
- [ ] Escalation transitions (warning/cooldown typed, consulted at admission)
- [ ] Offline zero-network + content-free diagnostics + redaction registry

### Step 4: evidence consolidation + pre-completion consult
- [ ] record.md (archaeology, inventory table, design, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥492/29 floor)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
