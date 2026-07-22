## STATUS: SP-036 — Audit and admit bounded Avalonia MCP use
**Current Step:** Step 1 — installation inventory + config audit + pre-approach consult
**Last Updated:** 2026-07-22 (authored)
**Blockers:** none

### Step 1: inventory + config audit + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Installation inventory (package/pin/version/hash probed + registry-verified)
- [ ] Config audit (registration, startup, Sentry/telemetry state; secrets presence+shape)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: runtime health + outbound + tool inventory
- [ ] Startup health (gateway probe cycle, error surface)
- [ ] Outbound connections (telemetry endpoints specifically answered)
- [ ] Tool inventory classified (advisory-only criterion)

### Step 3: seeded probes + matrix + redaction
- [ ] Valid AXAML probe (no false positives)
- [ ] Invalid probes (violation matrix: hits/misses/FP/FN with exact seeds)
- [ ] Redaction behavior (fake secret-shaped seed)

### Step 4: admission record + pre-completion consult
- [ ] Admission record (decree verbatim; per-item findings; admitted subset vs rejected; advisory boundary rule)
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; 466/29 exact, no drift)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
