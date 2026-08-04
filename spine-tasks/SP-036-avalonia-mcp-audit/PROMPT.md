# Task: SP-036 — Audit and admit bounded Avalonia MCP use (A-013 evidence packet)

## Mission

Execute the `client/docs/task-board.md` P0 row **"Audit and admit bounded Avalonia MCP use"** (OPEN): inspect the exact Pi-installed Avalonia MCP server/config; verify commit/version/hash, tool inventory, startup health, outbound connections, Sentry/telemetry disablement, and behavior on seeded valid/invalid Avalonia v12 AXAML; document false positives/negatives and safe redaction; and record the **bounded admission** per the owner's 2026-07-21 decree ("Avalonia MCP admission (Sentry-mitigation decision made — proceed per the conditional recommendation)"). Audit + admission evidence — the advisory boundary is explicit: **no MCP-generated output bypasses official docs, real compilation, K3 screenshots, or headed Windows/Linux gates.**

**Honesty framings (binding):** (a) the owner decree is recorded verbatim with its source (2026-07-21, all-gates-lifted decision — Sentry-mitigation decided; proceed per the conditional recommendation); this packet produces the audit evidence + the bounded admission record, NOT a re-decision; (b) every audit claim is empirical (probed on the live box: processes, configs, versions, connections, seeded probes) — never documentation transcription; (c) the advisory boundary is structural: MCP output may ADVISE (a second opinion to accept/reject with reasons recorded) but may never SUBSTITUTE for the run's verification layers (docs research, compilation, pixel evidence, headed gates) — the row's own acceptance text; the two recorded A-013 rejections (AnalyzePerformance self-contradictory score ×2, SP-013/SP-014 records) are the standing examples; (d) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — record in record.md; the orchestrator reconciles at land; (e) the audit must not exfiltrate anything: seeded probe payloads are synthetic by construction, and any MCP-server config files inspected are read-only; sensitive values found in configs are recorded as presence+shape only.

## Dependencies

- **Task:** SP-030 (wave-1 landed — sequencing; the A-013 advisory usage record spans SP-013/SP-014/SP-015 records)
- **Task:** SP-037 (sequencing — the v6.6.3 manifest-drift repair must land first; this packet's full-suite contract demands counts EXACTLY the 466/29 floor, red until restored)

## Context to Read First

- `client/docs/task-board.md` row "Audit and admit bounded Avalonia MCP use" (the exact acceptance text) + the 2026-07-21 decree entry (owner-decision ledger)
- The recorded A-013 usages: `spine-tasks/SP-013-popup-scrolling/record.md` (ValidateXaml PASS; AnalyzePerformance REJECTED self-contradictory — 1st), `spine-tasks/SP-014-quick-toggle-dispatch/record.md` (2nd rejection), `spine-tasks/SP-015-avatartube-animation/record.md` (advisory usage notes)
- The `avalonia-research` skill's MCP-advisory posture (`.pi/skills/avalonia-research/SKILL.md` if present — how the run's rules use advisory output)
- **2026-08-04 reality (supersedes the packet's original single-server premise):** the THREE-seat registration on this laptop — `avalonia-docs` (official Build MCP, http `https://docs-mcp.avaloniaui.net/mcp`, free), `avalonia-live` (Keincheck 0.11.0 embedded, MIT, http `127.0.0.1:3001`, app-side `CCP_MCP=1` env-gated `UseMcpServer()` seam in `client/src/CcpClient.Desktop/Program.cs`), `avalonia-ui` (decriptor/AvaloniaUI.MCP local build, stdio `dotnet C:\Code\AvaloniaUI.MCP\src\AvaloniaUI.MCP\bin\Release\net9.0\AvaloniaUI.MCP.dll`, 46 tools verified 2026-08-04) — registered in `C:\Users\Micha\.pi\agent\mcp.json`; the committed deep-research report `.spine/mcp-avalonia-deepresearch.md` (identity, Sentry analysis: UNCONDITIONAL with hardcoded US DSN in Program.cs:22, patch-and-rebuild noted as the actionable mitigation — whether THE LAPTOP BUILD carries Sentry is an empirical audit question, not a transcription); the 2026-07-18 pilot admission record `client/docs/avalonia-mcp-admission.md`; the official DevTools MCP was REJECTED 2026-08-04 (paid Avalonia Plus feature — violates the free-OSS constraint)
- The Pi MCP configuration surfaces on this box (`.pi/settings.json` / `C:\Users\Micha\.pi\agent\mcp.json` — locate via the `mcp` gateway tool's server status + the Pi docs `docs/sdk.md` if needed)

## File Scope

- `spine-tasks/SP-036-avalonia-mcp-audit/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `spine-tasks/SP-036-avalonia-mcp-audit/record.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/runtime/**`, `client/src/**`, `client/tests/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-036-avalonia-mcp-audit/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Installation inventory + config audit + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Locate and inventory ALL THREE registered seats on the live box (`avalonia-docs`, `avalonia-live`, `avalonia-ui` — registration config, package/pin/version/commit-hash where applicable, process cmdlines); verify the `avalonia-ui` build's version + upstream-HEAD equivalence against the registry/remote (live evidence); the deep-research report is INPUT to verify, never evidence to transcribe
- [ ] Config audit (READ-ONLY): per-seat registration, startup command, environment, any Sentry/telemetry settings + their state (the owner's Sentry-mitigation decision verified in the actual config/build — the deep research found Sentry UNCONDITIONAL with a hardcoded DSN and no disable path; whether THIS laptop's build carries or strips it is answered empirically here); sensitive values presence+shape only
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the inventory + the audit plan; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Runtime health + outbound connections + tool inventory

- [ ] Startup health: server start/probe cycle (via the Pi MCP gateway tools — `mcp({server:"avalonia"})` metadata refresh), error/log surface
- [ ] Outbound connections: live connection inventory for the server process (netstat-class evidence: remote endpoints, ports, TLS); Sentry/telemetry endpoints specifically sought and recorded (presence/absence — the decree's mitigation verified empirically); DNS-class lookups if observable
- [ ] Tool inventory: the full exposed tool list with one-line purposes (from the gateway metadata), classified against the row's advisory-only criterion

### Step 3: Seeded probes (valid/invalid AXAML) + false-positive/negative matrix + redaction

- [ ] Seeded VALID Avalonia v12 AXAML probe: the advisory tool(s) accept/analyze without false positives (record verdicts vs the known-good source)
- [ ] Seeded INVALID probes (deliberate v12 violations: bad compiled-binding usage, v11-isms, invalid property, broken style selector): the tool(s) flag them — record hits/misses/false-positives/false-negatives in a matrix with the exact seeds (reproducible, in evidence/)
- [ ] Redaction behavior: a seed with a fake secret-shaped string — does the tool transmit/echo it? Record the redaction posture (safe/unsafe, evidence)
- [ ] **WSL2 note:** the audit is host-runtime evidence, platform-neutral; recorded as such

### Step 4: The bounded admission record + pre-completion consult

- [ ] Write the admission record section in record.md: decree verbatim; the audit findings per acceptance item (installation verified / startup health / outbound + Sentry posture / tool inventory / probe matrix / redaction); the **admitted tool subset** (which tools are admitted for advisory use, e.g. ValidateXaml-class) vs **rejected/not-admitted** (e.g. AnalyzePerformance-class, per the two recorded rejections + the probe matrix); the **advisory boundary rule** (no MCP output bypasses docs/compilation/K3 screenshots/headed gates; every advisory accepted-or-rejected WITH reasons recorded in the using packet's record.md)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the audit + admission record; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green — zero product/test change, counts EXACTLY the 466/29 floor, any drift = red flag)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Installation inventory with verified version/hash; config audit with the Sentry/telemetry posture verified empirically
- Startup health + outbound connection inventory (telemetry endpoints specifically answered) + full tool inventory classified
- Seeded valid/invalid probe matrix with false-positive/negative counts + redaction posture evidence
- The bounded admission record (decree verbatim + admitted subset + rejected set + the advisory boundary rule) in record.md
- Contract green (466/29 exact, no drift); both solo Fable consults persisted with actual answering models

## Do NOT

- Exfiltrate anything (synthetic seeds only; configs read-only; sensitive values presence+shape); re-decide the owner's Sentry admission (record it — the Sentry-live/patched state on THIS box + the owner question stay surfaced, never silently resolved); use MCP output AS verification (advisory only — the boundary); make product-code or test changes (audit only); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/runtime/**`; set any board row state
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-036): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-036-avalonia-mcp-audit/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-07-22 (authoring): **row OPEN with the owner's Sentry-mitigation decision already made (2026-07-21 decree — recorded verbatim in the admission record).** The two recorded A-013 rejections (AnalyzePerformance self-contradictory ×2) fold into the probe matrix. Advisory boundary = the row's own text (no bypass of docs/compilation/screenshots/headed gates). Enabler 2 encoded. Headless audit; 4h budget exported at launch. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-07-22 (authoring): Launch: validate → analyze → plan → preflight → detached wave batch (SP-035 + SP-036, 2 lanes) per owner cycle.
- 2026-08-04 (orchestrator, pre-launch reconciliation): **premise corrections applied in-place.** (1) The audit subject is now the THREE-seat 2026-08-04 registration (avalonia-docs / avalonia-live / avalonia-ui), not a single Pi-installed server — inventory + classification covers all three; the admission record admits a bounded subset per seat. (2) The committed deep-research report + the 2026-08-04 registration session are INPUTS to verify empirically on this box (Sentry-carrying vs patched build answered HERE), never transcribed as evidence. (3) Consult route rewired 2026-08-04: solo = Opus 5 main, Fable 5 fallback; council stays unproven (T-7; kimi-api unregistered on this laptop). (4) Floor measured 464/466 + 29/29 (2 pre-existing red AssetManifestTests from merge `56f156fc`) — SP-037 repairs the floor and LANDS FIRST (sequencing dep added); the `counts EXACTLY the 466/29 floor` clause reads against the restored floor, any drift = red flag stands. (5) Wave-4 resume moot: the desktop's parked batch `20260722T152755` lane commits never reached this repo — this run is a FRESH execution of the packet. (6) Baseline is now Avalonia 12.1.1 (bccbabf3) — probe seeds use current v12 AXAML.
