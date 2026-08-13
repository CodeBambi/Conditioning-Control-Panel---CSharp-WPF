## STATUS: SP-064 — Harness entry points must REFUSE to run unsealed
**Current Step:** 1 (not started)
**Last Updated:** 2026-08-13 (orchestrator, authored — wave 21, single lane)
**Blockers:** none

### Step 1: enumerate every entry point, classify it, design the gate — NOT STARTED
- [ ] Update STATUS.md before starting work
- [ ] Disposition table built from the tree (flag, file:line, class 1-4, reason) — the Mission's list is a starting point, not the answer
- [ ] Per class-1 entry: what it writes unsealed. Per class-2 entry: why a human running it unsealed is legitimate
- [ ] Gate design: insertion point, message text, exit code, registry shape, `--verify-assets --dtrh-m2test` consequence named
- [ ] Guard design: literal surface scanned, failure mode, why it cannot skip
- [ ] Pre-approach solo consult (T-7: `mode: "solo"`) — verdict + ACTUAL answering model

### Step 2: implement the gate, the registry, and the guard — NOT STARTED
- [ ] One registry consumed by the gate; no second copy of the classification
- [ ] Gate after the SP-057 override block, before composition-root construction; stderr names `CCP_DATA_ROOT`; non-zero exit; no host, no window
- [ ] Refusal pin table-driven over the registry; allow direction pinned for classes 2/3/4
- [ ] Guard test mirroring `DataRootChokePointGuardTests`; RED captured with an unclassified literal, then removed
- [ ] Any process-env-mutating test joins `ProcessEnvCollection` (SP-062)

### Step 3: real-process evidence — the pin is not the proof — NOT STARTED
- [ ] (a) class-1 flag, `CCP_DATA_ROOT` unset → non-zero exit, stderr names the variable, no window
- [ ] (b) real profile path-hashed manifest before/after (a), both directions, with SP-057's positive controls
- [ ] (c) same flag sealed → not refused, override line observed, bounded by auto-close
- [ ] (d) plain launch unsealed → not refused, window rect-verified, exit 0, profile delta reported honestly
- [ ] 3 consecutive full-suite greens, ≥1 fresh-checkout first-ever build, TRX attached, per-run table incl. skipped column

### Step 4: record + pre-completion consult — NOT STARTED
- [ ] record.md complete (table, gate, registry, guard RED, four process runs, 3-run suite table with the new exact floor, intended filings)
- [ ] Honesty cell: the demo+auto-close residual hole, non-data-root writes, guard surface bounds, non-`Main` harness paths, Linux unproven
- [ ] Pre-completion solo consult (verdict + ACTUAL model)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification — NOT STARTED
- [ ] Contract testCommand passes (verify.mjs 0, 0W/0E, new exact unit count / 35 headless, 0 skipped, TRX)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

### Discoveries
- (none yet)
