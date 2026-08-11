## STATUS: SP-055 — One active-pool definition (asset deselection parity)
**Current Step:** Step 1 — archaeology + seam design + pre-approach consult (design drafted, consult pending)
**Last Updated:** 2026-08-11 (Step 1 in progress)
**Blockers:** none

### Step 1: archaeology + seam design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (EnumerateActive/BuildDisabledSet/Scan/ScanItem, IsAssetActive, DisabledAssetPaths + UseAssetWhitelist, FlashService.GetMediaFiles normalization)
- [x] Consumer inventory (grep every user-media consumer in the client)
- [x] Seam design + fixture matrix (in record.md Step 1)
- [x] Pre-approach solo consult (verdict + actual model in record.md — APPROVED with 6 corrections, all adopted)

### Step 2: the seam + both consumers + tests
- [ ] Single active-pool definition with upstream's exact semantics
- [ ] DTRH pool routed through it (divergence comment updated)
- [ ] Intake provisioning routed through the SAME definition
- [ ] Persisted deselection set + whitelist flag (additive, SP-005 machinery)
- [ ] Fixture-matrix tests + both-consumers-agree + skip-vs-deselect + both-folders bound

### Step 3: headed proof + record + pre-completion consult
- [ ] Headed: deselected asset never reaches the page on BOTH consumers + empty-set control
- [ ] record.md (archaeology, consumer inventory, design, consults, review presence, evidence)
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (floor = whatever SP-054 leaves)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
