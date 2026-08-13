## STATUS: SP-066 — Vacuous-shape sweep, name-anchored skip pin, and the shape guard
**Current Step:** 0 — not started
**Last Updated:** 2026-08-13 (orchestrator, authored)
**Blockers:** none

**Floor at authoring:** 898 unit / 35 headless / 0 skipped, build 0W/0E (SP-065, integrate `09b4b639`).
This packet ADDS facts (guard tests, auditor pin) and MAY REMOVE facts (deleted vacuous tests) — state the new exact counts here and bump `client/tests/floor/floor.json` in the SAME commit as every count change.

### Step 1: build the detector, produce the raw inventory, design the ledger and pin schema — ⬜ Not Started
- [ ] Update STATUS.md before starting work
- [ ] Detector over BOTH test projects classifying: early `return`, all-assertions-nested, no-assertion-token, platform predicate, environment predicate, filesystem-existence predicate — `file:line` + method + shape per site
- [ ] Complete RAW inventory into `evidence/`, reconciled against the framing (a) magnitudes (7 / 12 / 3 / 48 / 53 / 10 / 1)
- [ ] Detector error directions stated with one concrete example of each from this codebase (helper-hoisted assertion = false positive; loop over possibly-empty collection = false negative)
- [ ] Ledger schema designed, incl. how entries are keyed so a moved line does not silently un-cover a site
- [ ] `floor.json` schema change designed (framing e) + the `allowedSkips` admission rule text (framing f, incl. the two named bans)
- [ ] Pre-approach solo consult (T-7: `mode: "solo"`, cap the reply) — verdict + ACTUAL answering model; record exactly what surfaced, never stitch from reasoning

### Step 2: name-anchored skip pin in the wrapper (BEFORE any conversion) — ⬜ Not Started
> ⚠️ Hydrate: expand from the Step 1 schema decision
- [ ] `floor.json` → `{ total, allowedSkips[] }` carrying the admission rule + existing `bumpRule` in-file
- [ ] `check-floor.mjs` enforces: zero bad outcomes, `passed + skipped == total`, every `NotExecuted` `testName` in `allowedSkips`; failure message NAMES the offending test; anchored on the TRX result list
- [ ] Every pre-existing fail-closed check preserved AND re-demonstrated (one table row per mode)
- [ ] Non-allowlisted skip → RED naming the test (captured); same skip allowlisted → GREEN (captured)
- [ ] `total` drift BOTH directions → RED (captured)
- [ ] Injections removed and removal proven
- [ ] Schema + wrapper change in ONE commit; floor bumped in that commit if the count moved

### Step 3: disposition every site — ⬜ Not Started
> ⚠️ Hydrate: one checkbox group per shape class once the Step 1 inventory exists
- [ ] EVERY inventory entry verdicted: `not-vacuous` / `platform-skip-converted` / `fixed` / `deleted` / `residual` — none left unverdicted
- [ ] `Assert.NotEmpty` (or explicit count assertion) before every loop-only assertion body dispositioned `not-vacuous` (framing c)
- [ ] Any conversion whose skip fires here added to `allowedSkips` under the admission rule, machine class named; SP-057 pin and the named flake NEVER listed
- [ ] Zero assertions weakened / tolerances widened / tests quarantined — proven by per-file `git diff` review summarized in the record
- [ ] Ledger committed with cleared entries; `floor.json` bumped in the same commit as any count change, reason in the message

### Step 4: the shape guard, and the T-17 auditor edit — ⬜ Not Started
- [ ] Guard test fails with `file:line` for any site absent from the ledger; repo-root walk; NEVER skips (missing directory = failure)
- [ ] Captured RED from a probe fact with a silenced assertion; probe removed and removal proven
- [ ] Guard's own honesty stated in the test file and the record (shape guard; runtime vacuity invisible)
- [ ] `client/tools/port-audit-prompt.md:12-13` invokes `node client/tests/floor/check-floor.mjs`; non-zero exit = audit FAIL naming the reason; port-workflow:204 `CCP_DATA_ROOT` note added
- [ ] Test pins that the auditor prompt invokes the wrapper and contains NO bare `dotnet test`
- [ ] `git ls-files client/tools/port-audit-prompt.md` pasted into the record; NO new file created under `client/tools/`

### Step 5: record + pre-completion consult — ⬜ Not Started
- [ ] `record.md` complete: detector + exact surface, inventory reconciliation, ledger verdicts, schema change + both new verdicts + preserved fail-closed table, deletions with the behavior left unverified, residuals with filing intent, `git ls-files` proof, 3-run table with NEW exact counts, consults + ACTUAL answering models, engine-review presence per step, intended board filings (no row state set)
- [ ] Honesty cell with all six required items (lexical detector / shape-only guard / `allowedSkips` records intent / ledger reasons are unchecked judgment / T-17 auditor proof NOT delivered / Linux unproven)
- [ ] Named flake, if it fired: recorded BY NAME with run number + TRX path; not retried away, not allowlisted
- [ ] Pre-completion solo consult; verdict + actual model recorded
- [ ] STATUS.md accurate before `.DONE`

### Step 6: Testing & Verification — ⬜ Not Started
- [ ] Contract testCommand green through the wrapper (`verify.mjs` 0, build 0W/0E, new exact counts, skip set exactly as pinned)
- [ ] 3 consecutive full-suite greens, ≥1 a FRESH-CHECKOUT first-ever build (cold = a NEW worktree); per-run table incl. skipped NAMES
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact from any run
