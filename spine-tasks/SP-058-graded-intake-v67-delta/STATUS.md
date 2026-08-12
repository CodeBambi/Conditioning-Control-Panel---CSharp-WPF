## STATUS: SP-058 — Graded Intake v6.7.x delta
**Current Step:** Step 3 — host-proven evidence (headed, under the SP-057 seam)
**Last Updated:** 2026-08-12 (worker, Step 2 complete 862/862+33/33 — plan review skipped:in-worker SP-195 class; Step 3 in progress)
**Blockers:** none (SP-057 landed c42d82ff — seam available)

### Step 1: delta archaeology + obligation table + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Enumerate the real v6.6.3 → v6.7.4 intake delta from the tree (counts, changed members)
- [x] Obligation table (SERVE/PROVISION/MESSAGE/STORE/NOTHING/BLOCKED-ON + citation + sizing)
- [x] Verify SP-055's existing intake `IsAssetActive` wiring; state the residual gap
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: implement the obligations
- [x] Serve new payload files through the real serving contract + typed probe
- [x] Provision accents.js host requirements (or record NOTHING)
- [x] Apply ai.js host obligations only
- [x] IsAssetActive gating through SP-055's single definition
- [x] TopMarksPercent 90.0 pinned with verdict derivation + boundaries
- [x] Tests per shipped obligation

### Step 3: host-proven evidence (headed, under the SP-057 seam)
- [ ] Headed intake run on DISPLAY3 with the data-root override set
- [ ] Loopback request/response proof per new payload file
- [ ] Top-marks boundary proven end-to-end in a driven run
- [ ] Real profile byte-identical post-run
- [ ] Linux/WSLg disposition or named gate

### Step 4: record + pre-completion consult
- [ ] record.md (delta, obligation table, NEW BASELINE VERSION stated, evidence, consults)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs exit 0; build 0W/0E; suites ≥ floor at branch tip, TRX)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
