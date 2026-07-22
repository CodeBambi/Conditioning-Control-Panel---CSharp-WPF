## STATUS: SP-031 — T-5 anchor re-base (provenance-faithful fixture)
**Current Step:** Step 4 — docs + pre-completion consult
**Last Updated:** 2026-07-22 (forensics complete — packet premise FALSIFIED by primary evidence; corrected design)
**Blockers:** none

### Step 1: forensics + design + pre-approach consult — COMPLETE (plan review: engine-skipped, SP-195)
- [x] Update STATUS.md before starting work
- [x] Call-site forensics — **PIVOT FINDING:** taskFolder = LANE path at all 4 callers in BOTH engine versions (`.DONE`-check + `lane.committed` event order prove it live); the packet's base-path theory is falsified. REAL root cause: engine CLI runs from GLOBAL install `C:\Users\Micha\.pi\agent\npm\node_modules\pi-spine` (2.8.0, process-cmdline-proven on the live batch); SP-028 patched the repo-local 2.10.0 tree the engine never loads. Existing patch line is CORRECT.
- [x] Design (keep path expression; add engineRoot to manifest + engine-flagged patches; normalize global hand-variants; dotnet replacement gains "dotnet.exe"; fixture v2 = two-tree provenance repro; post-land gate re-point)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: manifest re-base + migration + apply/verify — COMPLETE (plan review: engine-skipped, SP-195)
- [x] Manifest re-based (two-root; t5 comment/rationale corrected; dotnet +dotnet.exe)
- [x] Live-tree migration executed (atomic single writes) + apply/verify exit 0 both roots + idempotence + loud-failure + missing-root proofs

### Step 3: provenance-faithful fixture + regression proof — COMPLETE (plan review: engine-skipped, SP-195)
- [x] Fixture v2 GREEN 13/13, 5 cells (two-tree wave-1 repro; base-shaped cells w/ true semantics; negative control preserved; caller-shape regression pass)
- [x] Consumer census re-confirmed (4 callers, both versions, all taskFolderInWorktree)
- [x] Boundary re-recorded (named post-land gate re-pointed at next wave; this-wave ESM-cache caveat)

### Step 4: docs + pre-completion consult
- [ ] README row
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥412/29 floor, no drift)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
