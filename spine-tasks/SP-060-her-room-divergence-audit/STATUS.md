## STATUS: SP-060 — Her Room + Awareness divergence audit (zero product code)
**Current Step:** Step 4 — sizing, audit doc, pre-completion consult
**Last Updated:** 2026-08-12 (worker, in progress)
**Blockers:** none

### Step 1: upstream enumeration from the tree + pre-approach consult — COMPLETE
- [x] Update STATUS.md before starting work
- [x] Enumerate upstream Companion/Awareness/Views/asset semantics with `File.cs:line`
- [x] Privacy surface named with upstream defaults as cited facts
- [x] Pre-approach solo consult (verdict + ACTUAL model in record.md)

### Step 2: port-side inventory (what c1–c7 landed) — COMPLETE
- [x] Per-element port counterpart with contract section + `File.cs:line`, or explicit "no counterpart"
- [x] Port defaults + named limits recorded as facts (consent placeholder, cooldown families, memory read-gating)
- [x] Already-decided divergences flagged with their decision citation

### Step 3: divergence table + privacy verdicts — COMPLETE
- [x] One row per element with verdict ADOPT / KEEP / MERGE / BLOCKED-ON-OWNER
- [x] Data-boundary line per row (newly observed / retained / transmitted — "none" is an answer)
- [x] Owner decision list, each item a plain answerable question

### Step 4: sizing verdicts, audit doc + pre-completion consult — IN PROGRESS
- [ ] Sizing verdict per ADOPT/MERGE row (S/M/L, evidence class, deps, limit shape)
- [ ] `client/docs/her-room-divergence-audit.md` written (self-contained)
- [ ] `record.md` written (method, consults + ACTUAL models, UNKNOWNs, intended filings)
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes — EXACTLY 862 unit / 33 headless (zero product change), TRX
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
