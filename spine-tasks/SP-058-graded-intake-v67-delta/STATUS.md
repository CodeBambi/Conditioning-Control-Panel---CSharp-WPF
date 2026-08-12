## STATUS: SP-058 — Graded Intake v6.7.x delta
**Current Step:** complete (all 5 steps done; contract green)
**Last Updated:** 2026-08-12 (worker, Steps 4-5 complete — 862/862 + 33/33, TRX attached, diff clean, scope clean)
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
- [x] Headed intake run on DISPLAY3 with the data-root override set
- [x] Loopback request/response proof per new payload file
- [x] Top-marks boundary proven end-to-end in a driven run
- [x] Real profile byte-identical post-run
- [x] Linux/WSLg disposition or named gate

### Step 4: record + pre-completion consult
- [x] record.md (delta, obligation table, NEW BASELINE VERSION stated, evidence, consults)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
- [x] Contract testCommand passes (verify.mjs exit 0; build 0W/0E; 862/862 + 33/33 ≥ floor 847/33, TRX)
- [x] git diff --check clean
- [x] git status --short shows only File Scope paths
