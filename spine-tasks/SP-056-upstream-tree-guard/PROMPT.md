# Task: SP-056 — Guard: fail when upstream gains a payload tree the client has never heard of

## Mission

Close the guard gap the v6.6.3 → v6.7.4 sync exposed (`client/docs/upstream-sync.md` §D): **an entire 184-file upstream payload tree (`ConditioningControlPanel/Resources/web/goon/`) appeared and the client suite stayed 683/683 green.** The client's parity tests only cover trees the client already ships, so a brand-new product surface upstream produces *zero* signal — exactly the "silently left behind" failure the sync ritual exists to prevent.

Ship the smallest guard that makes that impossible: a **committed inventory** of the upstream payload trees known at the last sync, each with a typed disposition, plus a test that fails when the WPF tree contains a top-level payload tree the inventory has never heard of.

**Binding framings:** (a) **a guard that can silently skip is not a guard** — when the WPF reference tree is unreachable (published/CI contexts), the test must still assert the inventory is well-formed and non-empty, and it must be impossible for both branches to pass vacuously; (b) **dispositions are honest, not aspirational** — every tree is either `served` (the client ships/serves it today, naming where), or `not-ported` with a **board-row reference** (the row that owns it); an unlisted tree is a failure, not a warning; (c) **no port work is implied** — this task ships a guard and an inventory, not a Goon/FYP host; (d) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, or `client/docs/upstream-sync.md`** — orchestrator reconciles at land.

## Dependencies

- none (tests + one data file; runs in parallel with SP-055, disjoint file scope)

## Context to Read First

- `client/docs/upstream-sync.md` §A/§C/§D (the trees this sync introduced and the gap being closed)
- `ConditioningControlPanel/Resources/web/` (READ-ONLY — the actual top-level trees: `dtrh`, `intake`, `fyp`, `player`, `goon`, and whatever else is there; **enumerate, do not trust this list**)
- `client/tests/CcpClient.Tests/DtrhPayloadRootTests.cs` (how existing payload tests are shaped — note they use temp dirs, so repo-root resolution is a NEW concern this task must solve honestly)
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhParticipant.cs` (`ProbePayloadRoot` — the typed-probe pattern to imitate: Missing/Incomplete/Present, never a silent pass)

## File Scope

- `client/docs/upstream-payload-inventory.json` (new — the committed inventory)
- `client/tests/CcpClient.Tests/UpstreamPayloadInventoryTests.cs` (new)
- `spine-tasks/SP-056-upstream-tree-guard/**`
- **NOT in scope:** any `client/src/**` change, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/UpstreamPayloadInventoryTests.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/src/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md` |
| artifactsMustExist | `spine-tasks/SP-056-upstream-tree-guard/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Inventory + guard design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Enumerate the real top-level trees under `ConditioningControlPanel/Resources/web/` (with file counts) and, for each, determine the honest disposition by reading the client: `served` (name the serving code path) or `not-ported` (name the owning board row from `client/docs/task-board.md`, read-only)
- [ ] Design: the inventory schema (tree name, disposition, evidence/row ref, file-count-at-sync, sync baseline version), repo-root resolution that cannot pass vacuously, and the failure message a future sync will actually read
- [ ] **Pre-approach solo consult** (Opus 5 main, Fable 5 fallback); verdict + ACTUAL answering model in record.md

### Step 2: Inventory + guard + tests

- [ ] Write `client/docs/upstream-payload-inventory.json` from the enumeration (every tree, honest disposition, baseline `v6.7.4` / merge `42286638`)
- [ ] Implement the guard test: unknown upstream tree → **fail** naming the tree, its file count, and what to do (file a row, add the entry, cite it in the sync ledger); inventory entry for a tree that no longer exists upstream → fail (stale entry); when the reference tree is unreachable → assert the inventory parses, is non-empty, and every entry is well-formed, and make the reachable/unreachable branch **observable in the test output** so a permanently-skipping guard is visible
- [ ] Tests over the parser/comparer with fixtures (unknown tree, stale entry, matching set, malformed inventory, missing reference tree) — the guard's own logic is what must be pinned, not just today's tree list

### Step 3: Record + pre-completion consult

- [ ] Write `spine-tasks/SP-056-upstream-tree-guard/record.md` (enumeration table with counts + dispositions, design, consults + ACTUAL models, engine-review presence, the vacuous-pass argument stated explicitly, budgets, surprises, durable-lesson candidates)
- [ ] Demonstrate the guard bites: a transcript showing the test FAILING against a fixture with an unlisted tree, and passing against the real tree
- [ ] **Pre-completion solo consult**; verdict in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0, build 0W/0E, both suites at or above the current floor, TRX logger attached)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `client/docs/upstream-payload-inventory.json` lists every top-level upstream payload tree with an honest disposition (`served` with a code path, or `not-ported` with a board-row reference) and its file count at the v6.7.4 baseline
- The guard fails on an unknown tree and on a stale entry, with a message naming the tree and the required action
- The unreachable-reference branch cannot pass vacuously (inventory well-formedness asserted; branch observable in output)
- Guard logic pinned by fixture tests; contract green; both consults persisted with actual answering models

## Do NOT

- Port any upstream tree (Goon/FYP hosts have their own rows); change `client/src/**`; make the guard a warning; let both branches pass vacuously; hard-code today's tree list inside the test (the inventory is the data, the test is the logic); edit the three hot docs (enabler 2); modify `ConditioningControlPanel/**`, `.spine/**`, the sln, or `client/spikes/**`; set board row state
- Use `consult` council mode (T-7: solo only)

## Git Commit Convention

- `feat(SP-056): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-056-upstream-tree-guard/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`

## Amendments

- 2026-08-11 (authoring, orchestrator): filed from the v6.6.3 → v6.7.4 sync ledger §D. Size S; parallel-safe with SP-055 (disjoint scope: tests + one data file, zero `client/src/**`). Enabler 2. **`## Review Level: 2` heading present + grep-verified ≥ 2.**
