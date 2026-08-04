## STATUS: SP-036 — Audit and admit bounded Avalonia MCP use
**Current Step:** Step 1 complete (pending plan review); Step 2 evidence gathered
**Last Updated:** 2026-08-04
**Blockers:** none

### Step 1: inventory + config audit + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Installation inventory (three seats probed live; version/commit/hash verified — DLL SHA256 42DAE31D…CC24, clean porcelain, HEAD==upstream 974ec59)
- [x] Config audit (registration, startup, Sentry/telemetry state; secrets presence+shape) — Sentry UNCONDITIONAL + LIVE socket empirically confirmed
- [x] Pre-approach solo consult (verdict + provenance in record.md; corrections executed: live-probe avalonia-docs, DLL hash, mechanical tool count, FN rephrasing)

### Step 2: runtime health + outbound + tool inventory
- [x] Startup health (gateway connect cycle, GetServerInfo, PerformHealthCheck DEGRADED-on-internal-TelemetryService only)
- [x] Outbound connections (PID 47796: exactly one ESTABLISHED TLS — o4509369388761088.ingest.us.sentry.io; no hosts block)
- [x] Tool inventory classified (avalonia-ui 53 via tools.search; avalonia-docs 8 live; avalonia-live UNVERIFIED provisional)

### Step 3: seeded probes + matrix + redaction
- [x] Valid AXAML probe (seed A: PASS, 0 false positives)
- [x] Invalid probes (seeds B-F: 4 FN defect classes missed, 1 true negative on malformed XML; seeds in evidence/)
- [x] Redaction behavior (seed G: no output echo of fake secret strings; transport risk recorded — live Sentry socket)

### Step 4: admission record + pre-completion consult
- [ ] Admission record (decree verbatim; per-item findings; admitted subset vs rejected; advisory boundary rule)
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; 466/29 exact, no drift)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
