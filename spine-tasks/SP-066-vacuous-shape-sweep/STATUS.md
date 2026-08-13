## STATUS: SP-066 — Vacuous-shape sweep, name-anchored skip pin, and the shape guard
**Current Step:** DONE — all steps complete
**Last Updated:** 2026-08-13 (worker, step 1 started)
**Blockers:** none

**Floor at authoring:** 898 unit / 35 headless / 0 skipped, build 0W/0E (SP-065, integrate `09b4b639`).
**NEW EXACT COUNTS (this packet, HEAD f5f5d03b):** 900 unit / 35 headless / 2 skipped — the 2 skips pinned BY NAME in `allowedSkips` (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`, `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`; both Linux-machine-class), build 0W/0E. Bumps: Step 4 commit (+2 guard facts), allowedSkips in the Step 3 commit (conversions).
This packet ADDS facts (guard tests, auditor pin) and MAY REMOVE facts (deleted vacuous tests) — state the new exact counts here and bump `client/tests/floor/floor.json` in the SAME commit as every count change.

### Step 1: build the detector, produce the raw inventory, design the ledger and pin schema — ✅ Complete (plan review engine-skipped SP-195)
- [x] Update STATUS.md before starting work
- [x] Detector over BOTH test projects classifying: early `return`, all-assertions-nested, no-assertion-token, platform predicate, environment predicate, filesystem-existence predicate — `file:line` + method + shape per site
- [x] Complete RAW inventory into `evidence/`, reconciled against the framing (a) magnitudes (7 / 12 / 3 / 48 / 53 / 10 / 1)
- [x] Detector error directions stated with one concrete example of each from this codebase (helper-hoisted assertion = false positive; loop over possibly-empty collection = false negative)
- [x] Ledger schema designed, incl. how entries are keyed so a moved line does not silently un-cover a site
- [x] `floor.json` schema change designed (framing e) + the `allowedSkips` admission rule text (framing f, incl. the two named bans)
- [x] Pre-approach solo consult (T-7: `mode: "solo"`, cap the reply) — verdict + ACTUAL answering model; record exactly what surfaced, never stitch from reasoning

### Step 2: name-anchored skip pin in the wrapper (BEFORE any conversion) — ✅ Complete (plan review engine-skipped SP-195)
> ⚠️ Hydrate: expand from the Step 1 schema decision
- [x] `floor.json` → `{ total, allowedSkips[] }` carrying the admission rule + existing `bumpRule` in-file
- [x] `check-floor.mjs` enforces: zero bad outcomes, `passed + skipped == total`, every `NotExecuted` `testName` in `allowedSkips`; failure message NAMES the offending test; anchored on the TRX result list
- [x] Every pre-existing fail-closed check preserved AND re-demonstrated (one table row per mode)
- [x] Non-allowlisted skip → RED naming the test (captured); same skip allowlisted → GREEN (captured)
- [x] `total` drift BOTH directions → RED (captured)
- [x] Injections removed and removal proven
- [x] Schema + wrapper change in ONE commit; floor bumped in that commit if the count moved

### Step 3: disposition every site — ✅ Complete (plan review engine-skipped SP-195)
> ⚠️ Hydrate: one checkbox group per shape class once the Step 1 inventory exists
- [x] EVERY inventory entry verdicted: `not-vacuous` / `platform-skip-converted` / `fixed` / `deleted` / `residual` — none left unverdicted
- [x] `Assert.NotEmpty` (or explicit count assertion) before every loop-only assertion body dispositioned `not-vacuous` (framing c)
- [x] Any conversion whose skip fires here added to `allowedSkips` under the admission rule, machine class named; SP-057 pin and the named flake NEVER listed
- [x] Zero assertions weakened / tolerances widened / tests quarantined — proven by per-file `git diff` review summarized in the record
- [x] Ledger committed with cleared entries; `floor.json` bumped in the same commit as any count change, reason in the message

### Step 4: the shape guard, and the T-17 auditor edit — ✅ Complete (plan review engine-skipped SP-195)
- [x] Guard test fails with `file:line` for any site absent from the ledger; repo-root walk; NEVER skips (missing directory = failure)
- [x] Captured RED from a probe fact with a silenced assertion; probe removed and removal proven
- [x] Guard's own honesty stated in the test file and the record (shape guard; runtime vacuity invisible)
- [x] `client/tools/port-audit-prompt.md:12-13` invokes `node client/tests/floor/check-floor.mjs`; non-zero exit = audit FAIL naming the reason; port-workflow:204 `CCP_DATA_ROOT` note added
- [x] Test pins that the auditor prompt invokes the wrapper and contains NO bare `dotnet test`
- [x] `git ls-files client/tools/port-audit-prompt.md` pasted into the record; NO new file created under `client/tools/`

### Step 5: record + pre-completion consult — ✅ Complete
- [x] `record.md` complete: detector + exact surface, inventory reconciliation, ledger verdicts, schema change + both new verdicts + preserved fail-closed table, deletions with the behavior left unverified, residuals with filing intent, `git ls-files` proof, 3-run table with NEW exact counts, consults + ACTUAL answering models, engine-review presence per step, intended board filings (no row state set)
- [x] Honesty cell with all six required items (lexical detector / shape-only guard / `allowedSkips` records intent / ledger reasons are unchecked judgment / T-17 auditor proof NOT delivered / Linux unproven)
- [x] Named flake, if it fired: recorded BY NAME with run number + TRX path; not retried away, not allowlisted
- [x] Pre-completion solo consult; verdict + actual model recorded
- [x] STATUS.md accurate before `.DONE`

### Step 6: Testing & Verification — ✅ Complete
- [x] Contract testCommand green through the wrapper (`verify.mjs` 0, build 0W/0E, new exact counts, skip set exactly as pinned)
- [x] 3 consecutive full-suite greens, ≥1 a FRESH-CHECKOUT first-ever build (cold = a NEW worktree); per-run table incl. skipped NAMES
- [x] `git diff --check` clean
- [x] `git status --short` shows only File Scope paths
- [x] `git status --porcelain --ignored=matching -uall` shows no new ignored artifact from any run
