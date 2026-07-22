## STATUS: SP-027 — DTRH host slice b5: watchdog recovery, graceful exit, failure injection (FINAL)
**Current Step:** Step 3 — graceful exit + stale-profile recovery
**Last Updated:** 2026-07-22 (Step 1 in progress)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult — COMPLETE (plan review: engine-skipped, T-2)
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (watchdog/ProcessFailed/relaunch-once/exit/profile-lock, `File.cs:line`)
- [x] Spike/manifest verification (W17 timeline, W21, SP-011 injections)
- [x] Design (detection stack, capability states, relaunch-once machine, exit flow, injection harness)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: watchdog core — COMPLETE (plan review: engine-skipped, T-2; 378/378 + 29/29)
- [x] DtrhWatchdog.cs (heartbeat watch, native signal where available, relaunch-once)
- [x] Unit tests (silence timing, resume reset, once-then-exhaustion, no-live-session-fire)

### Step 3: graceful exit + stale-profile recovery
- [ ] Wind-down → bounded exit-done → force close; pong
- [ ] 0x800700AA-class stale-profile-lock detection + recovery
- [ ] Unit tests (fast path, timeout force, mid-freeze exit, lock classification)

### Step 4: failure-injection evidence + consolidated limits + board + pre-completion consult
- [ ] DISPLAY3 headed injection matrix (renderer-kill, blocked-route, missing-media, exit matrix; rect lines PERSISTED)
- [ ] WSL2 gate (contract green, WX equivalents + named limits)
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] Board row WIP with CONSOLIDATED b1–b5 named limits
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green 0W/0E both platforms (≥366/29 floor)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
