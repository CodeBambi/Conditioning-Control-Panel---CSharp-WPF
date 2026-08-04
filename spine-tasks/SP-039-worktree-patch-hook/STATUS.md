## STATUS: SP-039 — T-14: lane-local patch application at worktree creation
**Current Step:** Step 1 — engine archaeology + mechanism decision + pre-approach consult
**Last Updated:** 2026-08-04 (authored)
**Blockers:** none

### Step 1: engine archaeology + mechanism decision + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Worktree-setup-hook contract (resolution, invocation, args/env, timeout, exit semantics)
- [ ] Lane .pi/npm install timing (engine source evidence)
- [ ] Mechanism decision in record.md (chosen seam + rejected alternatives + fail-safe exit semantics)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: implement + scratch verification
- [ ] Hook script + config wiring (idempotent, engine exit contract, presence+shape logging)
- [ ] Scratch verification through the engine's provisioning path (stub batch per T-6; patches present pre-worker; verify.mjs exit 0 in-lane)
- [ ] Negative control (hook disabled/absent = old unpatched state)

### Step 3: evidence + pre-completion consult
- [ ] record.md (archaeology, decision + alternatives, transcripts, named post-land gate)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs exit 0 after the one recorded manual remediation; build 0W/0E; counts EXACTLY 492/29)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
