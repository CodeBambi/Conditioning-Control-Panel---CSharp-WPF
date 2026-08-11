# SP-052 record — DTRH run-setup ownership gates (b4 parity defects)

**Scope:** two user-observable parity defects in landed b4 (SP-026) work, measured by SP-050:
(1) Hourglass duration ceiling clamped unconditionally at both host points;
(2) Bottomless Fall `endless` knob absent end-to-end. Fixes restore WPF-observed behavior
exactly as main implements it. No schema bump (additive member, absent-member-flag
discipline). Docs (task-board / port-lessons) NOT edited by the worker (enabler 2).

## Step 1 — drift verification (re-measured against git main, 2026-08-05)

Line numbers below are this lane's `git grep -n` against the READ-ONLY WPF tree; PROMPT's
cited lines are within ±5 of these (the file drifted slightly since SP-050 measured).

### Defect 1 — Hourglass ceiling

| Point | main (WPF) | greenfield (b4, landed) | drift |
|---|---|---|---|
| persist | `DtrhHostService.cs:474-475` — `int durMax = ChaosMeta.IsOwned("custom_duration") ? 7200 : 1200; s.ChaosRunDurationSec = Math.Clamp(..., 60, durMax);` | `DtrhMeta.cs:878` — `Math.Clamp(GetInt(setup,"durationSec") ?? 960, 60, 1200)` unconditional | owner's 2h fall silently re-clamped to 20min |
| deal | `ChaosModels.cs:203-204` — same ownership-gated `durMax` in `FromSettings` | `DtrhRunConfig.cs:98` — `Math.Clamp(setup.DurationSec, 60, 1200)` unconditional in `FromSetup` | same silent re-clamp at deal |

Page-side confirmation (READ-ONLY payload): `warren.js:108-109` (`ownsCustomDur()`),
`warren.js:134-145` (owner keeps saved free value clamped 120..7200; non-owner snaps to a
preset), `warren.js:690-734` (the 2min..2h dial, DUR_MIN=120/DUR_MAX=7200),
`chaosRun.js:144` (page clamps dealt `rc.durationSec` 60..7200 — "The Hourglass: up to 2h").

### Defect 2 — Bottomless Fall endless knob

| Point | main (WPF) | greenfield (b4, landed) | drift |
|---|---|---|---|
| config knob | `ChaosModels.cs:134-135` — `public bool Endless { get; set; }` | no `Endless` anywhere | knob absent |
| persist | `DtrhHostService.cs:478-480` — `if (setup["endless"] != null) s.ChaosEndless = ((bool?)setup["endless"] ?? false) && ChaosMeta.IsOwned("endless_mode");` ("a stale page can't arm it without the unlock") | no `endless` handling in `PersistRunSetup` | owner's ∞ toggle persists nowhere |
| init runSetup | `DtrhHostService.cs:509` — `endless = s?.ChaosEndless ?? false` in `BuildRunSetup` | `DtrhRunSetup` record + `BuildRunSetupPayload` carry no `Endless` | page never sees saved toggle |
| run-config | `DtrhHostService.cs:1043` — `endless = cfg.Endless` in `BuildRunConfig` | `BuildRunConfigPayload` ships no `endless` | `rc.endless` absent at the page |
| deal re-check | `ChaosModels.cs:206` — `cfg.Endless = s.ChaosEndless && ChaosMeta.IsOwned("endless_mode");` | n/a (no knob) | no deal-time ownership re-check |
| habit rail | `DtrhHostService.cs:1073-1074` — `ownedHabitIds` excludes `custom_duration` + `endless_mode` ("setup-shape unlocks, no in-run effect -> keep them off the HUD rail") | `DtrhRunConfig.cs:181-182` — exclusion only implicit via `IsCatalogUpgrade` (six-row effects catalog lacks both ids) | correct by construction today, but not pinned to main's explicit shape |

Page-side confirmation: `warren.js:109` (`ownsEndless()`), `warren.js:134` (setup.endless
gated on ownership), `warren.js:170-171` (`buildSetup` sends `endless: !!setup.endless &&
ownsEndless()`), `warren.js:694-701` (the ∞ toggle chip), `chaosRun.js:147`
(`endless: !!rc.endless` — the page reads the dealt knob).

Ownership primitive (b4, consumed as-is): `PurchasedUpgrades.Contains(id)` —
main `ChaosUpgrades.cs:323` (`IsOwned` = `State.PurchasedUpgrades.Contains(id)`);
greenfield active slot doc via `DtrhMeta.S` / `meta.PurchasedUpgrades`. No new upgrade ids,
no new ownership semantics.

## Step 1 — design (main's exact shape, both defects)

1. **Persist gate** (`DtrhMeta.PersistRunSetup`, index-doc mutation): compute
   `durMax = S.PurchasedUpgrades.Contains("custom_duration") ? 7200 : 1200` and clamp
   `durationSec` 60..durMax (main `DtrhHostService.cs:474-475`). Lower bound 60 unchanged.
2. **Deal gate** (`DtrhRunConfig.FromSetup`): same `durMax` from `meta.PurchasedUpgrades`
   before the initializer (main `ChaosModels.cs:203-204`).
3. **Additive member**: `DtrhSlotIndex.Endless` (bool, default false) next to `DurationSec`
   — additive-only, `JsonExtensionData` + neutral default cover pre-SP-052 index docs
   (b4 absent-member-flag discipline; no schema bump).
4. **Endless persist**: in `PersistRunSetup` after `waveCount` (main's field order),
   `idx.Endless = (GetBool(setup,"endless") ?? false) && S.PurchasedUpgrades.Contains("endless_mode")`
   (main `DtrhHostService.cs:478-480`).
5. **Init carry**: `DtrhProtocol.DtrhRunSetup` gains `bool Endless` (after `DurationSec`,
   main :509 order); `BuildRunSetupPayload` passes `idx.Endless`.
6. **Run-config carry**: `DtrhRunConfig.Values.Endless`; `BuildRunConfigPayload` ships
   `endless = cfg.Endless` (main :1043).
7. **Deal re-check**: `FromSetup` sets `Endless = setup.Endless && meta.PurchasedUpgrades.Contains("endless_mode")`
   (main `ChaosModels.cs:206`).
8. **Habit rail**: make the `custom_duration`/`endless_mode` exclusions explicit in
   `ownedHabitIds` with main's comments (`DtrhHostService.cs:1073-1074`) — behavior
   unchanged today (IsCatalogUpgrade already filters them), pinned so a future
   catalog row can't leak them onto the HUD rail.

### Test plan

- Clamp matrix (owner/non-owner × persist/deal × 1200/1201/7200 + 99999):
  non-owner persist 99999 → 1200; owner persist 99999 → 7200; owner persist 1201 → 1201;
  owner persist 7200 → 7200; owner persist 7201 → 7200; deal: owner idx 1500 → cfg 1500;
  non-owner stale idx 7200 → cfg 1200 (deal re-clamp). Lower bound 60 both.
- Endless: owner persist `endless:true` → idx.Endless true; non-owner persist
  `endless:true` → idx.Endless **false** (stale-page refusal, main :478-480); init
  runSetup carries `Endless`; dealt `rc.endless` true for owner; deal re-check —
  idx.Endless true but ownership removed → dealt `endless` false; ownedHabitIds excludes
  `endless_mode`/`custom_duration` for an owner who also holds `slow_fuses`.
- b4 test updates (UPDATED, never weakened):
  `RequestRun_PersistsSetup_ToIndexDoc_WithClamps_AndInitRoundTrips` — non-owner 1200
  assertions stay (harness owns nothing); cite comments updated to main
  `DtrhHostService.cs:474-475` / `ChaosModels.cs:203-204`; endless round-trip assertions
  added to the same round-trip test.

## Consults

### Pre-approach solo consult (Step 1)

**Route:** `consult` solo (bpx-consult.json: solo = `anthropic/claude-opus-5`, thinking
high — the 2026-08-04 rewire main route). The solo reply truncated mid-answer; a
follow-up `gut-check` (configured = `zai/glm-5.2`, the pause-protocol fallback class)
carried the remainder. **Actual answering models: claude-opus-5 (solo, truncated) +
glm-5.2 (gut-check completion).**

**Verdict (combined): "design is sound — proceed", with four concrete additions and one
honesty correction:**

- (a) Active-slot `PurchasedUpgrades` at persist time: no flaw — safe *because* the
  deal-time re-check gates the run by the current owner; persist-time gating is a
  helper, not the final gate. Must name + test the cross-slot case.
- (b) Explicit habit-rail exclusion is the RIGHT call: relying on the six-row catalog is
  correct today but fragile (a later slice adding setup-shape unlocks to UpgradeEffects
  would silently leak them onto the HUD rail). Pin explicitly with main's comment.
- (c) No observable drift from main if the exact shapes are copied.
- Additions: (1) cross-slot test — owner persists 7200, non-owner deal clamps 1200;
  (2) lower-bound 60 still clamps for owner AND non-owner; (3) absent-`endless`-key
  discipline — a setup WITHOUT the key must not clear a saved `idx.Endless=true`
  (main only mutates when `setup["endless"] != null`); (4) owner's 7200 round-trips
  through init runSetup untouched (extend the existing round-trip test).
- **Honesty correction (binding):** do NOT claim the b4 1200-clamp tests "stay correct
  as-is" — their comments/intent assert the wrong invariant (the unconditional clamp IS
  the bug). Update explicitly: non-owner test renamed/split to assert the non-owner
  ceiling with the main-line cite; add a separate owner test. Same numeric assertion
  without updated name/comment = a weasel; the task says updated, never weakened.

Applied: the Step-2 test plan below folds in all four additions + the rename.

### Engine plan-review presence (T-2 heading format)

- Step 1 (`spine_review_step --step 1 --type plan`): **engine review ABSENT** — nested
  reviewer spawn blocked inside pi worker session (SP-195); engine runs reviews after
  `.DONE`. `spawnFailed=false` (not a fail-closed case). Artifact:
  `.reviews/1-20260811T141523.md`.

### Pre-completion solo consult (Step 3)

**Route:** `consult` solo (`anthropic/claude-opus-5`, truncated) + `gut-check`
(`zai/glm-5.2`) completion. **Actual answering models: claude-opus-5 + glm-5.2.**

**Verdict: "Not done yet — five cheap fixes, two of them honesty-blocking. The code and
the gate design are sound; I'd sign off on the implementation."** All five applied:

1. (honesty) Run A `run-ended` check: grep-verified ABSENT in `wh-runA-owner.log` — no
   banking; post-run slot-1 showed runsCompleted/sparks/totalRunSeconds all 0. Record
   reworded: no "verified fresh doc" claim — only the observable-delta statement.
2. (honesty) record.md remediation text now says "no claim is made about any pre-existing
   baseline beyond what the post-run file showed".
3. Non-owner lower-bound assertion added (durationSec:10 → 60) in the non-owner test —
   both branches now assert the shared floor.
4. `wh-runB-nonowner.INVALID.log` promoted to a dedicated "Invalid / superseded runs"
   subsection.
5. `--dtrh-m2test` spelled out on both Run A2 and Run B2 evidence lines.

Re-verified after the fixes: DtrhMeta suite 37/37 green; full contract re-run below.

## Headed round-trip transcripts (Step 3)

**Class:** Windows headed (avalonia-live/harness class — the SP-025/SP-026 `--dtrh-fx-drive`
harness: raw page JSON through the REAL parse+dispatch path; auto-close ends every session,
never a full run). WSL2 named limit stands: laptop WSL zero distros — Linux owner-gated,
never faked; no Wayland claims. Evidence: `evidence/wh/`.

**Harness additions (File Scope `Features/Dtrh/**`, SP-023-norm harness-only):**
`buy:<upgrade-id>` fx-drive step (REAL purchase-upgrade meta-command at cost 0 — headed
evidence can't grind the economy; validation is integrity not anti-cheat,
DtrhMetaBridge.cs:13-21), `request-run-hd` fx-drive step (REAL request-run whose setup
exceeds the non-owner ceiling: durationSec 99999 + endless:true), and the dealt run-config
transcript line in `DtrhMeta.OnRequestRun` (diff/dur/endless/scripted).

### Behavior 1 — Hourglass ceiling (owner deals ≥1201s; non-owner stays 1200)

- **Run A** (`wh-runA-owner.log`, real mode, purchases real on real slot 1):
  `buy:custom_duration` → `request-run-hd` → `descent setup persisted`; the real index doc
  held **`durationSec: 7200`** (verbatim capture: `wh-runA-owner-index-proof.json`) —
  owner's >20min setup survives persist (main DtrhHostService.cs:474-475 owner branch).
  (The dealt config in this run was the scripted classroom — fresh slot, RunsCompleted 0;
  the deal cell was re-run as A2 below.)
- **Run A2** (`wh-runA2-owner-deal.log`, **`--dtrh-m2test` test mode**: clone owns
  custom_duration + endless_mode via real purchase-upgrade dispatch; test mode never deals
  the classroom and never banks): `dealt run config (diff=Easy, dur=7200s, endless=True, scripted=False)` —
  the owner's setup deals **7200s ≥ 1201s** (main ChaosModels.cs:203-204 owner branch).
- **Run B2** (`wh-runB2-nonowner.log`, **`--dtrh-m2test` test mode**, clone owns neither
  unlock): same 99999s setup →
  persist clamped the real index to **1200** and **`dealt run config (diff=Easy, dur=1200s,
  endless=False, scripted=False)`** — non-owner ceiling intact at BOTH points.

### Behavior 2 — Bottomless Fall endless knob (owner's endless reaches rc.endless)

- Run A / A2 (owner): persist wrote **`endless: true`** to the real index doc
  (`wh-runA-owner-index-proof.json`); the dealt run-config transcript carries
  **`endless=True`** — `rc.endless` is the payload's own read (chaosRun.js:147). No full
  endless run driven (auto-close; PROMPT-named limit).
- Run B2 (non-owner): `endless:true` from a non-owner persists **false** (stale-page
  refusal, main DtrhHostService.cs:478-480) and deals **`endless=False`** (deal re-check,
  ChaosModels.cs:206).

### Incidents (honest)

- **Run A wrote the real `%APPDATA%/CcpClient` profile** — the `APPDATA=` env override does
  NOT redirect .NET's `GetFolderPath(ApplicationData)` on Windows (SHGetKnownFolderPath).
  Observable delta after Run A: two cost-0 purchase ids on slot 1 + the 7200/endless index
  values. Run A logged `run started` (the page began the dealt scripted classroom) but NO
  `run-ended` (grep-verified, `wh-runA-owner.log`) — auto-close at 20s ended the session,
  and the post-run slot-1 read showed `runsCompleted: 0, sparks: 0, totalRunSeconds: 0`,
  so no banking occurred. Remediated in-session: slot-1's `purchasedUpgrades` restored to
  `[]` (the only observable slot delta were the two added ids; no claim is made about any
  pre-existing baseline beyond what the post-run file showed); the index restored to
  neutral defaults (180s/5 waves, `endless` key removed — the pre-run index values were
  unrecoverable after Run A overwrote them; defaults are the WPF fallbacks).

### Invalid / superseded runs

- **`wh-runB-nonowner.INVALID.log`** — INVALID CELL, superseded by Run B2: the m2test clone
  deep-clones the real slot doc, so it inherited Run A's not-yet-remediated purchases and
  "owned" both unlocks (dealt 7200/True). Kept for audit, named INVALID, excluded from
  evidence. Lesson recorded below.

### Engine plan-review presence — Step 2

- Step 2 (`spine_review_step --step 2 --type plan`): **engine review ABSENT** — nested
  reviewer spawn blocked inside pi worker session (SP-195); `spawnFailed=false`.
  Artifact: `.reviews/2-20260811T142022.md`.

## Budgets

- Consults: 4 calls (2 solo — both truncated mid-answer by the transport, each completed
  by a gut-check follow-up; verdicts above name both answering models per the rewire's
  actual-model rule).
- Headed runs: 4 app sessions (A, A2, B-invalid, B2) ≈ 90s wall total, auto-closed.
- Contract: verify.mjs ~5s; Rebuild ~18s; unit 672 tests ~34s; headless 33 tests ~16s.

## Durable-lesson candidates

1. **`APPDATA=` does NOT isolate a .NET Windows process's `ApplicationData`**
   (SHGetKnownFolderPath ignores the env var) — headed harness runs that write real
   stores hit the real profile unless a data-dir override exists. Snapshot the target
   files BEFORE the run, or add a harness data-dir flag when the scope allows.
2. **The m2test clone deep-clones the REAL slot doc** — a test-mode "non-owner" cell is
   only as clean as the real profile. Sequence: restore/clean the profile FIRST, then run
   the non-owner cell.
3. **A fresh slot's request-run deals the scripted classroom, not FromSetup** — deal-path
   evidence needs RunsCompleted ≥ 1 or test mode (which never deals the classroom).
4. Solo-consult replies truncated twice this task; the gut-check follow-up pattern
   ("give the remainder compactly") recovered the full verdict both times.
