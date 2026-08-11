## STATUS: SP-053 — Webview prefers-reduced-motion inheritance probe
**Current Step:** Step 1 — probe design + pre-approach consult (IN PROGRESS)
**Last Updated:** 2026-08-05 (worker)
**Blockers:** none

### Step 1: probe design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Measurement design (page-side probe + host OS read + toggle mechanism + restore)
- [x] Failure-contingent mechanism sketch (built only if inheritance fails)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: the probe + measurement
- [ ] Probe seam (unit-testable; asserts the measurement PATH)
- [ ] Headed measurement run: engine matchMedia vs OS setting OFF and ON (transcripts)

### Step 3: verdict + mechanism (if needed) + evidence + pre-completion consult
- [ ] Verdict recorded with transcripts
- [ ] Typed honoring mechanism if failed + page-side verification
- [ ] record.md (design, transcripts, verdict, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥669/33 floor; TRX logger)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
