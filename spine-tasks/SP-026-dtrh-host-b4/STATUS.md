## STATUS: SP-026 — DTRH host slice b4: progression/payout, Loom, user/mod media
**Current Step:** Step 3 — Loom + user/mod media serving (IN PROGRESS)
**Last Updated:** 2026-07-22 (Step 2 complete — 347/347 + 29/29 green; plan review SKIPPED BY DESIGN, SP-195)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult — COMPLETE
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (meta ops/meta state/payout/request-run/asset-stats/Loom/user-media, `File.cs:line`)
- [x] Payload verification (b4 message fields, m2test harness, cheshireGuide meta consumption)
- [x] Design (slot-doc progression mapping, Loom store decision, user-media folder contract, media-logging rule)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: meta progression + payout on b2 slot machinery — COMPLETE
- [x] Progression members on DtrhSlotDocument (no schema bump) + meta ops
- [x] Payout + payout-result, run-started/ended upgrade, request-run, asset-stats
- [x] Unit tests (op matrix, payout math, round-trips, tolerance, media-logging rule)

### Step 3: Loom + user/mod media serving
- [ ] DtrhLoom.cs (save/delete/list/result, GIF validation, §4 serving, seed-at-ready)
- [ ] User/mod media serving inside §4
- [ ] Unit tests (Loom lifecycle, traversal/MIME refusal, seed shape)

### Step 4: headed/WX evidence + page-visual unlocks + board + pre-completion consult
- [ ] DISPLAY3 headed evidence (payout round-trip, Loom display, user media, page-visual unlocks; rect lines PERSISTED)
- [ ] WSL2 gate (contract green, WX facts)
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] Board row WIP with named limits
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green 0W/0E both platforms (≥313/29 floor)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
