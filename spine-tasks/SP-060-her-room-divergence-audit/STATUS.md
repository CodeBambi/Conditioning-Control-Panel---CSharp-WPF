## STATUS: SP-060 — Her Room + Awareness divergence audit (zero product code)
**Current Step:** not started
**Last Updated:** 2026-08-12 (orchestrator, authored — wave-17 lane-2)
**Blockers:** none

### Step 1: upstream enumeration from the tree + pre-approach consult — NOT STARTED
- [ ] Update STATUS.md before starting work
- [ ] Enumerate upstream Companion/Awareness/Views/asset semantics with `File.cs:line`
- [ ] Privacy surface named with upstream defaults as cited facts
- [ ] Pre-approach solo consult (verdict + ACTUAL model in record.md)

### Step 2: port-side inventory (what c1–c7 landed) — NOT STARTED
- [ ] Per-element port counterpart with contract section + `File.cs:line`, or explicit "no counterpart"
- [ ] Port defaults + named limits recorded as facts (consent placeholder, cooldown families, memory read-gating)
- [ ] Already-decided divergences flagged with their decision citation

### Step 3: divergence table + privacy verdicts — NOT STARTED
- [ ] One row per element with verdict ADOPT / KEEP / MERGE / BLOCKED-ON-OWNER
- [ ] Data-boundary line per row (newly observed / retained / transmitted — "none" is an answer)
- [ ] Owner decision list, each item a plain answerable question

### Step 4: sizing verdicts, audit doc + pre-completion consult — NOT STARTED
- [ ] Sizing verdict per ADOPT/MERGE row (S/M/L, evidence class, deps, limit shape)
- [ ] `client/docs/her-room-divergence-audit.md` written (self-contained)
- [ ] `record.md` written (method, consults + ACTUAL models, UNKNOWNs, intended filings)
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes — EXACTLY 862 unit / 33 headless (zero product change), TRX
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
