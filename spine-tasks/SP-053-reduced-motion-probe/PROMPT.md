# Task: SP-053 — Webview prefers-reduced-motion inheritance probe (DTRH host)

## Mission

Execute the `client/docs/task-board.md` P1 row **"Webview prefers-reduced-motion inheritance probe"** (OPEN, filed 2026-08-05 — a PRE-EXISTING DTRH host obligation surfaced by the SP-050 audit, range-proven NOT a v6.6.3 delta obligation): measure `matchMedia('(prefers-reduced-motion: reduce)')` inside the greenfield embedded WebView2 host with the OS animation setting toggled, and record what the embedded engine reports vs the OS/user state. If inheritance fails, the host owes a typed mechanism that honors the OS/user motion preference page-side (never silently betraying the page's own probe — `shared/capability.js:35,57` drives reduced → 2D mode off this). Windows evidence; **Linux half unproven (WSL zero-distros named limit; WebKitGTK inheritance unknown — recorded, never faked)**.

**Honesty framings (binding):** (a) **probe-first, never assume:** the answer is whatever the embedded engine actually reports — measure with the OS "Animation effects" setting OFF and ON (WPF's own OS cap reads `SystemParameters.ClientAreaAnimation`, `MotionFx.cs:37-54` — the OS can only remove motion); (b) **user-observable consequence drives the verdict:** if the engine ignores the OS state, a reduced-motion user silently gets the 3D descent — the host obligation is then a typed mechanism (a forced CSS/media override or a host-driven state the page reads), decided with the consult, recorded; (c) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (d) no Wayland claims; the Linux half is a named limit.

## Dependencies

- **Task:** SP-050 (the audit that surfaced this obligation + the range-proof)

## Context to Read First

- `client/docs/task-board.md` row "Webview prefers-reduced-motion inheritance probe" (the acceptance text)
- `spine-tasks/SP-050-v663-obligation-audit/record.md` (the obligation cell + range-proof + the consequence-if-wrong)
- `client/src/CcpClient.Desktop/Features/Dtrh/` (`DtrhHostWindow` — the embedded WebView2 surface; the drive/InvokeScript seam for page-side measurement)
- Payload (READ-ONLY): `ConditioningControlPanel/Resources/web/dtrh/shared/capability.js:35,57` (the page's own probe — `matchMedia('(prefers-reduced-motion: reduce)')` → reduced → 2D mode, `canTry3d:false`)
- WPF (READ-ONLY): `Services/UI/MotionFx.cs:37-54` (`SystemParameters.ClientAreaAnimation` — the OS can only remove motion; MotionLevel Full/Reduced/Off semantics)
- The avalonia-live usage map + the windowId silent-drop quirk (`client/memories/avalonia-mcp.md` — `target`/`handle` params; dimension validation)

## File Scope

- `client/src/CcpClient.Desktop/Features/Dtrh/**` (the probe mechanism + the typed mechanism IF inheritance fails — per-change justification)
- `client/tests/CcpClient.Tests/Dtrh*` (the probe-seam unit test)
- `spine-tasks/SP-053-reduced-motion-probe/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Dtrh/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**` |
| artifactsMustExist | `spine-tasks/SP-053-reduced-motion-probe/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Probe design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Design the measurement: a page-side probe script through the host's script interface reading `matchMedia('(prefers-reduced-motion: reduce)')` inside the embedded engine + the host-side OS read (`SystemParameters.ClientAreaAnimation` equivalent on this box) — BOTH recorded; the toggle mechanism for the OS setting (Settings app or registry; the probe run documents the exact toggle and restores it after)
- [ ] The failure-contingent mechanism design (only sketched, built only if inheritance fails): typed host-driven honoring of the OS/user preference (never a silent betrayal) — consult decides the shape if needed
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: The probe + measurement

- [ ] The probe seam (unit-testable: the seam asserts the measurement PATH, never the OS inheritance itself)
- [ ] **The headed measurement run (Windows):** the embedded engine's `matchMedia` answer recorded with the OS setting OFF and ON (each state verified OS-side before reading the engine); typed log lines in evidence/

### Step 3: Verdict + mechanism (if needed) + evidence + pre-completion consult

- [ ] The verdict recorded with the measurement transcripts (inheritance holds / fails — engine answers vs OS state)
- [ ] If inheritance fails: implement the typed honoring mechanism (consult-decided shape) + the page-side verification (the page's `capability.js` probe now reports reduced under OS-off)
- [ ] Write `spine-tasks/SP-053-reduced-motion-probe/record.md` (design, transcripts, verdict, mechanism if built, consult verdicts + ACTUAL answering models, engine-review presence, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green incl. new tests; warnings measured on `-t:Rebuild`; counts ≥ the 669/33 floor; TRX logger attached per the template amendment)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- The embedded engine's `matchMedia('(prefers-reduced-motion: reduce)')` answer measured against BOTH OS animation states (transcripts)
- Verdict recorded (inheritance holds / fails) with the user-observable consequence
- If failed: the typed honoring mechanism implemented + page-side verification (page probe reports reduced under OS-off)
- Linux half recorded as unproven (named limit); contract green (≥669/33 floor); both solo consults persisted with actual answering models

## Do NOT

- Assume the inheritance answer (measure); change the page's probe (`ConditioningControlPanel/**` READ-ONLY); fake the OS toggle (the state must be verified OS-side before each engine read); widen the scope (the FX chrome body is the BLOCKED inventory's); edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `client/CcpClient.sln`, `client/spikes/**`, `.spine/**`, `client/src/CcpClient.Desktop/Ai/**`, `client/src/CcpClient.Desktop/Features/Companion/**`; set any board row state; claim Wayland; fake Linux evidence
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-053): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-053-reduced-motion-probe/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-05 (authoring, orchestrator): **row filed at the wave-12 land (SP-050 audit's probe obligation; range-proven pre-existing, not a v6.6.3 delta).** Probe-first framing (the verdict is whatever the engine reports); the honoring mechanism is contingent on a failed verdict. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-05 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-052 + SP-053, 2 lanes — disjoint scopes) per owner cycle.
