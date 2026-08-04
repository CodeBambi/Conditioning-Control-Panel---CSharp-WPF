## STATUS: SP-037 — Reconcile asset manifest with v6.6.3 DTRH payload delta
**Current Step:** Step 1 — empirical delta sweep + re-derivation plan + pre-approach consult
**Last Updated:** 2026-08-04 (authored)
**Blockers:** none

### Step 1: empirical delta sweep + re-derivation plan + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Run the two failing tests; capture named failures verbatim; enumerate tree vs manifest both directions
- [ ] Re-derivation plan in record.md (per-entry SP-009 schema decisions; derived copied-count; legacy-tree session fact)
- [ ] Pre-approach solo consult (Opus 5 main / Fable 5 fallback; verdict + actual model in record.md)

### Step 2: apply the re-derivation
- [ ] assets.manifest.json: add derived new entries, remove derived dead entries
- [ ] AssetManifestTests.cs: copied-count assertion + comment to the derived count
- [ ] Both named tests + full manifest class green locally

### Step 3: self-check binaries + full-suite floor + evidence + pre-completion consult
- [ ] --verify-assets exit 0 Debug AND Release binaries
- [ ] Full contract testCommand green (466/466 + 29/29 restored)
- [ ] record.md written (delta derivation, consults, review presence, WSL2 named-limit probe verbatim)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + 466/466 + 29/29 exact)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
