## STATUS: SP-055 — One active-pool definition (asset deselection parity)
**Current Step:** COMPLETE — all 4 steps done, contract green
**Last Updated:** 2026-08-11 (Step 4 complete: verify.mjs 0, build 0W/0E, 814/814 + 33/33 TRX-attached, diff clean, scope clean)
**Blockers:** none

### Step 1: archaeology + seam design + pre-approach consult — COMPLETE
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (EnumerateActive/BuildDisabledSet/Scan/ScanItem, IsAssetActive, DisabledAssetPaths + UseAssetWhitelist, FlashService.GetMediaFiles normalization)
- [x] Consumer inventory (grep every user-media consumer in the client)
- [x] Seam design + fixture matrix (in record.md Step 1)
- [x] Pre-approach solo consult (verdict + actual model in record.md — APPROVED with 6 corrections, all adopted)

### Step 2: the seam + both consumers + tests — COMPLETE
- [x] Single active-pool definition with upstream's exact semantics
- [x] DTRH pool routed through it (divergence comment updated)
- [x] Intake provisioning routed through the SAME definition
- [x] Persisted deselection set + whitelist flag (additive, SP-005 machinery)
- [x] Fixture-matrix tests + both-consumers-agree + skip-vs-deselect + both-folders bound (+ fire-pool consumer found + routed)

### Step 3: headed proof + record + pre-completion consult — COMPLETE
- [x] Headed: deselected asset never reaches the page on BOTH consumers + empty-set control (+ whitelist-off cell; captures dimension-validated)
- [x] record.md (archaeology, consumer inventory, design, consults, review presence, evidence, budgets, surprises, lessons)
- [x] Pre-completion solo consult (SOUND; 5 discharges executed)
- [x] STATUS.md accurate before .DONE

### Step 4: Testing & Verification — COMPLETE
- [x] Contract testCommand passes (814/814 + 33/33 ≥ the 795/33 floor SP-054 leaves; TRX attached; one recorded flake in an untouched heartbeat test, re-run green)
- [x] git diff --check clean
- [x] git status --short shows only File Scope paths
