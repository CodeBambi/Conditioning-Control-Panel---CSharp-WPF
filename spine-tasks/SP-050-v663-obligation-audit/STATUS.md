## STATUS: SP-050 — Host-obligation audit (v6.6.3 remaining deltas)
**Current Step:** Step 1 — delta inventory + payload enumeration + pre-approach consult
**Last Updated:** 2026-08-05 (authored)
**Blockers:** none

### Step 1: inventory + payload enumeration + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Per-delta payload-side facts (modules, bridge messages, self-driven surfaces; payload file:line)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: WPF host-side enumeration + obligation table
- [ ] Per-delta WPF host provisions (File.cs:line; explicit nothing-records)
- [ ] Obligation table (messages/windows-stores/probes/NOTHING + sources + filter)

### Step 3: sizing verdicts + filings + pre-completion consult
- [ ] Per-delta packet-sizing verdicts (size, evidence class, deps, limit shape)
- [ ] Board-row filings named for obligation-carrying deltas
- [ ] record.md (audit, table, verdicts, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + counts EXACTLY 629/33; TRX logger)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
