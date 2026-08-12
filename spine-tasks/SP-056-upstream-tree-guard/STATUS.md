## STATUS: SP-056 — Upstream payload-tree guard
**Current Step:** Step 1 — inventory + guard design + pre-approach consult
**Last Updated:** 2026-08-11 (step 1 in progress)
**Blockers:** none

### Step 1: inventory + guard design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Enumerate upstream top-level payload trees + honest dispositions (served / not-ported + row ref)
- [ ] Design inventory schema + non-vacuous repo-root resolution + failure message
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: inventory + guard + tests
- [ ] client/docs/upstream-payload-inventory.json (baseline v6.7.4 / merge 42286638)
- [ ] Guard test (unknown tree fails, stale entry fails, unreachable branch asserts well-formedness + observable)
- [ ] Fixture tests pinning the guard's logic

### Step 3: record + pre-completion consult
- [ ] record.md (enumeration table, design, consults, review presence, vacuous-pass argument)
- [ ] Transcript: guard FAILS on an unlisted-tree fixture, passes on the real tree
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
