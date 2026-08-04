## STATUS: SP-040 — AI companion slice c4: memory
**Current Step:** Step 1 — archaeology + schema + consent design + pre-approach consult
**Last Updated:** 2026-08-04 (worker)
**Blockers:** none

### Step 1: archaeology + schema + consent design + pre-approach consult (COMPLETE)
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (lifecycle, clear, ambient-stateless, persist-gated precedent, consent default)
- [x] AiMemoryDocument schema + store design + consent seam (placeholder default recorded)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: store implementation + schema machinery
- [ ] AiMemoryStore.cs on SP-005 machinery (round-trips, quarantine→Degraded, journal, preserve)
- [ ] Consent gating at write admission (typed no-op on denial)
- [ ] Unit tests (round-trips, Degraded, journal, consent, both-answers schema shape)

### Step 3: persist wiring + explicit clear + file-content proofs
- [ ] Moderation-gated persist (blocked turn rolled back, never persisted — file proof)
- [ ] Explicit-clear (in-memory emptied + document deleted — file-content proof)
- [ ] Offline zero-network + content-free diagnostics + redaction registry

### Step 4: evidence consolidation + pre-completion consult
- [ ] record.md (archaeology, design, proofs, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥516/29 floor)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
