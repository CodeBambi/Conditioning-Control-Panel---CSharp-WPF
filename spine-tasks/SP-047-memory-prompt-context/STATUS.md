## STATUS: SP-047 — Wire companion memory into prompt context
**Current Step:** Step 4 — Testing & Verification
**Last Updated:** 2026-08-05 (Step 3 complete — line flipped, record.md + both consults persisted)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (assembly shape, load-gating :113, ambient stateless, enrichment exclusion)
- [x] Design (assembly seam, read-gating behavior port, tension recorded, surface line update)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: assembly implementation + tests
- [x] Assembly seam (interactive requests carry the store's pairs)
- [x] Unit tests (round-trip INTO prompt; read-gating; ambient stateless; enrichment exclusion; trimming)

### Step 3: surface honesty + evidence + pre-completion consult
- [x] Non-claim line updated to the true state (typed-default-honest)
- [x] Offline zero-network + content-free diagnostics
- [x] record.md (archaeology, design, consults, review presence)
- [x] Pre-completion solo consult (verdict + actual model in record.md)
- [x] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥601/33 floor)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
