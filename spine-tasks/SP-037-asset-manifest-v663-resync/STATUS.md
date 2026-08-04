## STATUS: SP-037 — Reconcile asset manifest with v6.6.3 DTRH payload delta
**Current Step:** complete — all steps done, .DONE next
**Last Updated:** 2026-08-04 (worker, Step 1 in progress)
**Blockers:** none

### Step 1: empirical delta sweep + re-derivation plan + pre-approach consult — COMPLETE (plan review: engine-skipped SP-195, recorded)
- [x] Update STATUS.md before starting work
- [x] Run the two failing tests; capture named failures verbatim; enumerate tree vs manifest both directions
- [x] Re-derivation plan in record.md (per-entry SP-009 schema decisions; derived copied-count; legacy-tree session fact)
- [x] Pre-approach solo consult (Opus 5 main / Fable 5 fallback; verdict + actual model in record.md)

### Step 2: apply the re-derivation — COMPLETE (23/23 AssetManifestTests green; plan review: engine-skipped SP-195, recorded)
- [x] assets.manifest.json: add derived new entries, remove derived dead entries
- [x] AssetManifestTests.cs: copied-count assertion + comment to the derived count
- [x] Both named tests + full manifest class green locally

### Step 3: self-check binaries + full-suite floor + evidence + pre-completion consult
- [x] --verify-assets exit 0 Debug AND Release binaries
- [x] Full contract testCommand green (466/466 + 29/29 restored)
- [x] record.md written (delta derivation, consults, review presence, WSL2 named-limit probe verbatim)
- [x] Pre-completion solo consult (verdict + actual model in record.md)
- [x] STATUS.md accurate before .DONE

### Step 4: Testing & Verification — COMPLETE
- [x] Contract testCommand passes (verify.mjs + build 0W/0E + 466/466 + 29/29 exact)
- [x] git diff --check clean
- [x] git status --short shows only File Scope paths
