## STATUS: SP-027 — DTRH host slice b5: watchdog recovery, graceful exit, failure injection (FINAL)
**Current Step:** Step 4 — evidence + consult complete; committing, then plan review + .DONE
**Last Updated:** 2026-07-22 (Step 4 verification green)
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

### Step 3: graceful exit + stale-profile recovery — COMPLETE (plan review: engine-skipped, T-2; 390/390 + 29/29)
- [x] Wind-down → bounded exit-done → force close; pong
- [x] 0x800700AA-class stale-profile-lock detection + recovery
- [x] Unit tests (fast path, timeout force, mid-freeze exit, lock classification)

### Step 4: failure-injection evidence + consolidated limits + board + pre-completion consult
- [x] DISPLAY3 headed injection matrix (runA renderer-kill + relaunch-once + exhaustion; runB1/B2/B3 exit triptych; runC 403; runD 404; rect lines PERSISTED in committed transcripts)
- [x] WSL2 gate (~/ccp-sp027 native ext4: contract green 391/391 + 29/29 0W/0E; wx1 typed Unavailable + graceful fast-path EXIT=0; wx2 renderer-kill → heartbeat-silent 24s → relaunch-ONCE on Linux)
- [x] record.md complete (salvage provenance, ESC-drive forensics, consult verdicts)
- [x] Pre-completion solo consult
- [x] Board row WIP with CONSOLIDATED b1–b5 named limits
- [x] STATUS.md accurate

### Step 5: verification
- [x] testCommand green 0W/0E (Rebuild-measured) both platforms — Windows 391/391 + 29/29, WSL2 391/391 + 29/29 (≥366/29 floor)
- [x] git diff --check clean
- [x] git status shows File Scope only
