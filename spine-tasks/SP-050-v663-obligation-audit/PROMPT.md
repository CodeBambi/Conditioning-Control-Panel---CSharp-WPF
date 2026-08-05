# Task: SP-050 — Host-obligation audit across the remaining v6.6.3 deltas (decomposition record)

## Mission

Execute the land-consult directive (wave-11): a bounded **host-obligation audit** across the remaining v6.6.3 behavior deltas, instead of blind feature packets. For EACH delta — **Brain Drain rework + Brain Melt; FX overhaul (AmbientFxCanvas, tiers, reduced motion); Hourglass; Bottomless Fall; NUX first-run; Weekly Intake Pass** — enumerate from the PAYLOAD sources (what new pages/modules/bridge messages exist) AND the WPF HOST sources (what the WPF host provides per delta) what the greenfield client must provide: **new bridge messages / new windows or stores / new capability probes / NOTHING**. Produce a per-delta packet-sizing verdict (S/M/L + evidence class + dependencies + the honest limit shape). Output = ONE audit record (`record.md`); the orchestrator files board rows at land for deltas with real obligations. **Zero product code** (audit/design-record — the SP-030 admission-record shape).

**Honesty framings (binding):** (a) **evidence-first, never assume:** every obligation cites its source (`payload file:line` / `File.cs:line`); a delta whose payload self-drives everything gets an explicit NOTHING verdict with proof (the SP-049 lesson: one message + one window was the whole Loom obligation); (b) **user-observable parity is the filter:** obligations are what the client must provide for the delta's user-observable behavior — not a re-port of payload-internal machinery; (c) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — board-row filings are named in record.md; the orchestrator writes them at land; (d) no Wayland claims; WSL zero-distros named limit applies to any future Linux evidence, not this audit.

## Dependencies

- **Task:** SP-049 (the first delta port — the archaeology discipline this audit applies)

## Context to Read First

- `client/docs/main-sync-2026-08-04.md` (the full v6.6.3 delta inventory — release trains 6.4.0→6.6.3 verbatim)
- The v6.6.3 payload tree (READ-ONLY): `ConditioningControlPanel/Resources/web/dtrh/` — the new/changed modules per delta (Brain Drain, FX, Hourglass, Bottomless Fall, NUX, intake)
- `spine-tasks/SP-049-loom-studio/record.md` (the delta-port precedent: dual archaeology; what a host obligation actually looks like)
- `spine-tasks/SP-026-dtrh-host-b4/record.md` + `spine-tasks/SP-027-dtrh-host-b5/record.md` (the landed host surface + consolidated limits the deltas interact with)
- WPF host sources (READ-ONLY): the per-delta host services (`Services/Chaos/**`, the FX pipeline, Brain Drain services, NUX/intake services) — locate via the main-sync inventory's service list

## File Scope

- `spine-tasks/SP-050-v663-obligation-audit/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `spine-tasks/SP-050-v663-obligation-audit/record.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/**`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-050-v663-obligation-audit/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Delta inventory + payload-side enumeration + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Per delta (Brain Drain rework + Brain Melt; FX overhaul; Hourglass; Bottomless Fall; NUX first-run; Weekly Intake Pass): the payload-side facts — new/changed modules, bridge messages emitted/consumed, self-driven surfaces (each fact `payload file:line`)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the payload enumeration + the audit plan; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: WPF host-side enumeration + the obligation table

- [ ] Per delta: the WPF host's provisions (services, windows, stores, probes — each `File.cs:line`; where a delta has no host provisions, the explicit record of that)
- [ ] **The obligation table:** per delta — client must provide {new bridge messages | new windows/stores | new capability probes | NOTHING} with sources; user-observable filter applied (payload-internal machinery excluded with reason)

### Step 3: Sizing verdicts + board-row filings + pre-completion consult

- [ ] Per-delta packet-sizing verdict (S/M/L + evidence class + dependencies + honest limit shape — Linux/Wayland/painted-surface classes inherited from the wave-11 record where relevant)
- [ ] Board-row filings NAMED in record.md (only for deltas with real obligations — the orchestrator writes them at land)
- [ ] Write `spine-tasks/SP-050-v663-obligation-audit/record.md` (the audit: payload + host enumerations, the obligation table, sizing verdicts, consult verdicts + ACTUAL answering models, engine-review presence)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the audit + verdicts; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green — zero product change, counts EXACTLY the 629/33 floor; TRX logger attached per the template amendment)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every named v6.6.3 delta has a sourced obligation verdict (messages/windows-stores/probes/NOTHING — `payload file:line` + `File.cs:line` citations)
- Per-delta packet-sizing verdicts (size, evidence class, deps, limit shape)
- Board-row filings named for obligation-carrying deltas (orchestrator writes at land)
- Zero product change (629/33 exact); both solo consults persisted with actual answering models

## Do NOT

- Write product code (audit only); assume obligations (cite or NOTHING); re-port payload-internal machinery into the verdicts (user-observable filter); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `client/**`, `ConditioningControlPanel/**`, `.spine/**`, `.pi/**`; set any board row state; claim Wayland
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-050): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-050-v663-obligation-audit/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-05 (authoring, orchestrator): **the wave-11 land consult's directive — audit-first decomposition across the remaining v6.6.3 deltas instead of blind feature packets.** Zero-product-code audit (SP-030 shape); board filings are orchestrator-side at land. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-05 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-050 + SP-051, 2 lanes — disjoint scopes: audit is packet-dir-only; ChaosSfx audit touches Dtrh/** + tests) per owner cycle.
