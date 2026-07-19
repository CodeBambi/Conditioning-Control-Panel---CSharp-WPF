# Status: SP-008 — build tiered targeted verification harness

**Overall:** 🔄 In progress

**Current Step:** Step 4

## Steps

### Step 1: Pre-approach consult, admission spike design, harness doc skeleton
**Status:** ✅ Complete

- [x] Pre-approach solo consult (Fable 5) run; verdict text persisted BEFORE checkbox
- [x] STATUS.md updated before starting work
- [x] v12 headless research (official sources; URL + freshness recorded)
- [x] `client/docs/verification-harness.md` skeleton (tiers, evidence-class rule, affected=csproj-path, budget table, manifest schema, self-test usage)

### Step 2: Headless admission spike (evidence-gated)
**Status:** ✅ Complete

- [x] `Avalonia.Headless.XUnit@12.1.0` in NEW CcpClient.HeadlessTests project (NOT in CcpClient.Tests)
- [x] Real [AvaloniaFact] interaction tests (3) green on Windows AND WSL2 (recorded); draw-level assertions only
- [x] Contract testCommand builds solution + runs BOTH test projects; 85 landed tests untouched/green

### Step 3: Harness implementation (tiers 2–3)
**Status:** ✅ Complete

- [x] client/tools/verify/ thin scripts: launch/raise/capture one surface+state by name, Windows + WSLg XGetImage paths
- [x] Cross-platform .NET console assertion tool (Avalonia Bitmap, zero packages, no System.Drawing)
- [x] Check manifest scoped to real consumers (dashboard card lit/unlit, capability surface)
- [x] Assertion-logic unit tests in CcpClient.Tests (synthetic bitmaps: pass/fail/tolerance)

### Step 4: Seeded-regression self-test, budgets, K3 integration
**Status:** ⬜ Pending

- [ ] Re-runnable self-test mode: throwaway-edit real AXAML → specific NAMED check fails → restore → green (run recorded; no injection flags)
- [ ] Measured budgets: each tier Windows + WSL2, cold + incremental separate, recorded with headroom
- [ ] Manifest-driven K3 review of card lit+unlit (verdict recorded); tier-4 milestone hook documented (not run)
- [ ] WSL2 gate: full testCommand green native-dir + tier-2 WSLg capture exercised (X11 facts, no Wayland claim)

### Step 5: Evidence, board reconciliation, pre-completion consult
**Status:** ⬜ Pending

- [ ] record.md written (incl. engine-review presence/absence)
- [ ] Pre-completion solo consult (Fable 5) run; verdict text persisted
- [ ] Board row 7 → `WIP` with evidence (not `DONE`)
- [ ] STATUS.md accurate

### Step 6: Testing & Verification
**Status:** ⬜ Pending

- [ ] Contract testCommand passes (solution + BOTH test projects)
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
