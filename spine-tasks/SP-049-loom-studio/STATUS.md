## STATUS: SP-049 — Loom studio promotion (v6.6.3 delta)
**Current Step:** Step 2 — studio driving + protocol + tests (DONE; Step 3 next)
**Last Updated:** 2026-08-05 (worker)
**Blockers:** none

### Step 1: dual archaeology + design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] v6.6.3 payload archaeology (studio needs, bridge messages, 3D gate, rack pane, gifenc)
- [x] b4 archaeology + delta list (what v6.6.3 adds; user-observable set)
- [x] Drive design (open path, typed messages, evidence plan)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: studio driving + protocol + tests
- [x] Open path + new typed messages (tolerance discipline) — `--loom-demo`/`--loom-drive`/`--loom-auto-close` + `LoomReveal` typed (22-type vocabulary)
- [x] Rack pane driven in-engine (or typed named limit) — `DtrhLoomWindow` (WPF LoomHostService sibling shape); discharge evidence = Step 3
- [x] GIF export through the serving contract — gifenc path verified end-to-end in Step 3 evidence (store/serving side tested here)
- [x] Unit tests (round-trips, tolerance, serve discipline) — 14 new (628/628 + 33/33 green; floor 614/33)

### Step 3: in-engine evidence + consolidation + pre-completion consult
- [ ] avalonia-live evidence (open/render/operate, save round-trip file proof, valid GIF)
- [ ] Named-limit escalation if a host surface is missing (never fake)
- [ ] record.md (dual archaeology, delta, design, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥614/33 floor; TRX logger on full-suite runs)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
