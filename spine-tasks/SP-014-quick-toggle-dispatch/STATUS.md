# STATUS: SP-014 — replace card-title quick-toggle dispatch

**Current Step:** Complete (all 5 steps; .DONE pending)
**Last Updated:** 2026-07-20 (Step 5 verified: contract green 0W/0E, 144/144 + 15/15; engine review: in-worker skip by design SP-195 x4, reviewLevel=2)

### Step 1: Archaeology of the title-keyed mechanism + pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] WPF title-keyed dispatch mechanism + FeatureCard.xaml.cs:248-261 + exception cards (File.cs:line)
- [x] First-attempt CCP.* dispatch path + lesson dispositions (ACCEPT/ADAPT/REJECT)
- [x] Pre-approach solo Fable 5 consult (verdict in record.md BEFORE checkbox)

### Step 2: Dispatch contract + stable-identity implementation
**Status:** ✅ Complete

- [x] `client/docs/quick-toggle-dispatch.md` — full contract + named limits
- [x] Minimal stable-ID implementation on the demonstrator card (no framework)
- [x] `QuickToggleDispatchTests.cs` — stable-ID resolution, one-path convergence, title-mutation negative test

### Step 3: Windows-headed evidence
**Status:** ✅ Complete

- [x] Right-click toggle re-verified on changed code (immediate, ring, persistence file-proof)
- [x] Toggle WHILE SP-013 popup open (cross-proof)
- [x] Negative proofs: no context menu; title-vs-body region; exceptions contract-only
- [x] A-013 ValidateXaml-only advisory if AXAML touched; K3 only where pixels changed

### Step 4: WSL2 gate + board reconciliation + pre-completion consult
**Status:** ✅ Complete

- [x] WSL2 in-packet gate: contract green incl. title-mutation test; X11 session facts honest
- [x] record.md complete (archaeology, dispositions, consult provenance, engine-review presence, evidence)
- [x] Pre-completion solo Fable 5 consult (verdict in record.md)
- [x] task-board.md row → WIP with named limits (never DONE)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ✅ Complete

- [x] Contract testCommand green (build 0W/0E + both test projects incl. new tests)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only

Final verification (2026-07-20): build 0W/0E; CcpClient.Tests 144/144; CcpClient.HeadlessTests 15/15; `git diff --check` clean; `git status --short` File Scope only. WSL2 gate: identical counts green (144+15) on Ubuntu 26.04 / SDK 10.0.110, native-dir copy.
