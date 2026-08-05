# Task: SP-052 — DTRH run-setup ownership gates: Hourglass ceiling + Bottomless Fall endless knob (b4 parity defects)

## Mission

Execute the `client/docs/task-board.md` **P0** row **"DTRH run-setup ownership gates: Hourglass duration ceiling + Bottomless Fall endless knob (b4 parity defects, found by SP-050)"** (OPEN, filed 2026-08-05): fix two silent user-observable parity defects in the landed b4 port. **(1) Hourglass ceiling:** the greenfield clamps `durationSec` 60..1200 UNCONDITIONALLY at BOTH host points (persist `DtrhMeta.cs:878`, deal `DtrhRunConfig.cs:98`) — a `custom_duration` owner's 2h fall is silently re-clamped to 20min; main's shape = ownership-gated `Math.Clamp(..., 60, owned ? 7200 : 1200)` at persist AND deal (`DtrhHostService.cs:469-473`, `ChaosModels.cs:203`). **(2) Bottomless Fall endless knob:** no `Endless` anywhere in the greenfield host — an `endless_mode` owner's ∞ toggle persists nowhere, deals `rc.endless` absent; main's end-to-end shape: persisted setup (`if (setup["endless"] != null) s.ChaosEndless = ((bool?)setup["endless"] ?? false) && IsOwned("endless_mode")` — "a stale page can't arm it without the unlock", `DtrhHostService.cs:477-479`), init runSetup carries it (`:508`), run-config carries it (`:1042`), deal-time ownership re-check (`ChaosModels.cs:206`), ownedHabitIds excludes `endless_mode` (`:1073` — "effect -> keep them off the HUD rail"). ONE packet, TWO user-observable behaviors.

**Honesty framings (binding):** (a) **these are defects in landed work, not new features** — the b4 evidence stays (annotate-don't-rewrite), the fix restores WPF-observed behavior exactly as main implements it (measured drift lines cited above); (b) **b4's existing 1200-clamp tests must be UPDATED, never weakened** — the clamp change breaks them by design (they assert the unconditional clamp); each updated assertion cites the main line it now matches; (c) **no schema bump** — the index-doc `Endless` member is additive with the absent-member-flag discipline (b4 additive-only rule); (d) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (e) **WSL2 named limit: laptop WSL zero distros — headed round-trips are Windows-only (avalonia-live class); Linux owner-gated, never faked**; (f) no Wayland claims.

## Dependencies

- **Task:** SP-050 (the audit that measured these drifts — the obligation table + sources)

## Context to Read First

- `client/docs/task-board.md` row "DTRH run-setup ownership gates" (the acceptance text) + the DTRH host row's b4 annotation
- `spine-tasks/SP-050-v663-obligation-audit/record.md` (the measured drift table — main vs greenfield lines per point)
- `spine-tasks/SP-026-dtrh-host-b4/record.md` (the landed meta engine: ownership = `PurchasedUpgrades.Contains`; the persist/deal machinery; the 21-recipe whitelist; additive-only schema rule)
- Landed mechanics (consume): `client/src/CcpClient.Desktop/Features/Dtrh/DtrhMeta.cs` (:878 persist clamp; :66 recipe whitelist), `DtrhRunConfig.cs` (:98 deal clamp; :181-182 habit exclusion), `DtrhSaveSlots.cs` (:152 the index document)
- WPF (READ-ONLY, `File.cs:line` against git main): `DtrhHostService.cs:469-473` (persist gate), `:477-479` (endless persist + the stale-page comment), `:508` (runSetup), `:1042` (run-config), `:1073` (habit-rail exclusion), `ChaosModels.cs:134-135,203,206` (config knob + deal gates), `ChaosUpgrades.cs:323` (`IsOwned`)
- Payload (READ-ONLY): `game/warren.js:1013,108-109,134-145,170-171,690-734` (the page-side purchase/ownership/toggle/dial shapes), `game/chaosRun.js:144,147` (page-side clamp + `rc.endless` read)

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (the two gates + the endless member)
- `client/tests/CcpClient.Tests/Dtrh*` (clamp matrix + endless round-trips; the b4 1200-clamp test updates)
- `spine-tasks/SP-052-dtrh-ownership-gates/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**` |
| artifactsMustExist | `spine-tasks/SP-052-dtrh-ownership-gates/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Drift verification + fix design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Re-verify every drift line against git main (the SP-050 table — measured, not transcribed): the two clamp sites, the five endless points, the habit-rail exclusion, the page-side shapes
- [ ] Design: the ownership-gated ceiling at BOTH points (persist + deal — main's exact shape); the `Endless` additive member + the five-point end-to-end carry; the clamp-matrix + round-trip test plan; the b4 test updates (each updated assertion cites its main line)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the drift table + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: The two gates + tests

- [ ] Hourglass: ownership-gated `durMax` (1200 vs 7200) at persist AND deal (main's shape; clamp lower bound 60 unchanged)
- [ ] Bottomless Fall: additive `Endless` index-doc member (absent-member-flag discipline); ownership-gated persist (stale-page can't arm); init runSetup carry; run-config carry; deal-time re-check; habit-rail exclusion of `endless_mode`
- [ ] Unit tests: the clamp matrix (owner/non-owner × persist/deal × boundary values 1200/1201/7200); the five endless points + stale-page refusal; the b4 1200-clamp tests updated (each cites its main line; none weakened)

### Step 3: Headed round-trips + evidence + pre-completion consult

- [ ] **Headed round-trip per behavior (Windows, avalonia-live/harness class):** a `custom_duration` owner's >20min setup survives persist and deals ≥1201s `durationSec` in run-config (non-owner still clamps 1200); `endless:true` from a real owner reaches `rc.endless` (the page's own state line or the dealt-config transcript in evidence — never a full endless run)
- [ ] Write `spine-tasks/SP-052-dtrh-ownership-gates/record.md` (drift verification, design, consult verdicts + ACTUAL answering models, engine-review presence, round-trip transcripts, budgets, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 669/33 floor; TRX logger attached per the template amendment)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Ownership-gated duration ceiling at BOTH host points (persist + deal) — the clamp matrix green (owner 7200 / non-owner 1200, boundaries 1200/1201/7200); b4's 1200-clamp tests updated with main-line citations, none weakened
- The `endless` knob end-to-end (additive member, ownership-gated persist, init/runSetup/run-config carry, deal-time re-check, habit-rail exclusion)
- One headed round-trip per behavior (owner's setup deals ≥1201s; owner's endless reaches `rc.endless`)
- No schema bump; contract green (≥669/33 floor); both solo consults persisted with actual answering models

## Do NOT

- Weaken any existing assertion (the b4 clamp tests are UPDATED with main-line citations, never loosened); bump the schema version (additive member only); invent new upgrade IDs or ownership semantics (consume b4's `PurchasedUpgrades`); fake the headed round-trips (typed named limit instead); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**`; set any board row state; claim Wayland; fake Linux evidence
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-052): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-052-dtrh-ownership-gates/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-05 (authoring, orchestrator): **P0 defect row filed at the wave-12 land (land-consult framing: defects in landed work, not v6.6.3 deltas — silent user-observable data loss).** One packet, two user-observable behaviors. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-05 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-052 + SP-053, 2 lanes — disjoint scopes) per owner cycle.
