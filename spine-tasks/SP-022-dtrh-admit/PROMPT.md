# Task: SP-022 — admit DTRH browser and origin design

## Mission

Execute `client/docs/task-board.md` row **"Admit DTRH browser and origin design"** (P0, FIRST row of Phase 5 in `spine-tasks/CONTEXT.md`; owner review discharged by decree 2026-07-21 — this packet writes the engineering record the decree cannot): package version and Linux dependencies pinned; unchanged `bridge.js` or one minimal transport-only diff selected; loopback security/range/MIME/CORS contract approved; no classic fallback. Deliverable: `client/docs/dtrh-admission.md` — the admission record + the **host slice cut (b1…b5)** that the following DTRH-host packets execute. **Design-record only: zero product code, zero new spike code.**

**Honesty framings (binding):** (a) the decree lifts the approval, it does not write the record — every value in the admission must be PINED with live evidence (package version re-confirmed from the live feed + restore/build re-run of the existing quarantined spike on Windows AND WSL2; Linux native deps from SP-011's recorded evidence); (b) the transport selection is SP-011's empirical answer — the unchanged WebView2-shaped `bridge.js` cannot transport on Linux (`window.chrome.webview` absent; `invokeCSharpAction` page→host works) — so the record specifies the **minimal transport-only diff**: a small transport-detection branch, with the host→page direction decided explicitly (NativeWebDialog has NO InvokeScript on Linux — candidate shapes: page polls a host-controlled loopback endpoint (works on both platforms per SP-011's proven loopback serving), or navigation-based; decide with the pre-approach consult and record); (c) **no classic fallback** (the row's own constraint — record what "no classic fallback" commits us to: WebView2 on Windows, WebKitGTK NativeWebDialog path on Linux X11, honest unsupported elsewhere); (d) Wayland §5.1 untouched — the admission is scoped Windows + Linux X11/XWayland with Wayland as a named limit; (e) the DTRH payload stays READ-ONLY evidence — the record references SP-011's tree/blob hashes, never re-derives trust from presence.

## Dependencies

- **Task:** SP-020 (Phase-5 serial chain)

## Context to Read First

- `client/docs/task-board.md` — the admit row + DTRH host row (acceptance the slice cut must map) + gate history (the decree)
- `client/docs/webview-dtrh-spike.md` (SP-011) — the spike's named observations: package verified (`Avalonia.Controls.WebView` 12.0.1, MIT, dep `Avalonia@12.0.0`, clean restore vs pinned 12.1.0), Windows boot/bridge matrix (postMessage + invokeCSharpAction + preBuffer replay proven), the THREE Linux findings (embedded never-presents; NativeWebDialog renders but NO InvokeScript; unchanged bridge.js cannot transport on Linux), WPE absent (WebKitGTK 2.52.3 / libgtk-3-0t64 3.24.52 / libwebkit2gtk-4.1-0 2.52.3 pinned), loopback shape (two GET-only origins, overlay-first, Range + CORS preflight + traversal refusal), payload hashes (tree `40be29df`, bridge.js blob `13af3f4d`)
- `spine-tasks/SP-011-webview-dtrh-spike/record.md` — budgets, failure-injection results, owner question filed (WPE-SHM unmeasurable on WSLg)
- `client/docs/release-publish-gates.md` (SP-010) — natives-beside-exe packaging strategy the Linux native deps list feeds
- Required skills: none beyond standing referenceDocs (admission/design-record work)

## File Scope

- `client/docs/dtrh-admission.md` (deliverable: admission record + transport diff spec + origin security contract + host slice cut)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-022-dtrh-admit/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/dtrh-admission.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/src/**`, `client/tests/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/**` |
| artifactsMustExist | `client/docs/dtrh-admission.md`, `spine-tasks/SP-022-dtrh-admit/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Pin re-verification + transport design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Package pin re-confirmed EMPIRICALLY: live nuget feed (exact current 12.0.x of `Avalonia.Controls.WebView` + its `Avalonia` dep) + restore/build re-run of the EXISTING quarantined spike (`client/spikes/CcpSpike.WebView/`, build only — no new code) on Windows AND WSL2 (`~/ccp-sp022`, never /mnt/e); Linux native deps restated from SP-011's pinned evidence (WebKitGTK stack versions) with the apt-source check on the WSL2 image
- [ ] Transport design: specify the minimal transport-only diff (detection branch in bridge.js) + DECIDE the Linux host→page shape (page-polls-loopback-endpoint vs navigation-based vs named-limit) with SP-011's evidence in hand; state what works identically on both platforms vs per-platform divergence
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the pin evidence + transport design + slice-cut proposal; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Admission record + host slice cut

- [ ] `client/docs/dtrh-admission.md`: package pin (exact versions, license, restore/build re-verified both platforms, dep-vs-baseline note); Linux native dependencies pinned (apt package names + versions, SP-010 natives-beside-exe implication); **transport selection: minimal transport-only diff SPEC** (exact branch shape, per-direction matrix Windows/Linux, host→page decision); **loopback security contract approved-by-decree and written as text** (two GET-only origins, overlay-first, Range semantics, MIME allowlist, CORS preflight handling, traversal refusal, localhost-binding, sensitive-logging ban); **no classic fallback** commitment + unsupported-elsewhere honesty; Wayland named limit (§5.1); payload trust = SP-011 hashes referenced
- [ ] **Host slice cut b1…b5** in the same doc: b1 host shell + loopback origin serving + transport diff applied and proven in-product (boot matrix re-run); b2 three local slots + save picker/quick start + protocol v1; b3 native SFX/audio/video + freeze + rendered tint safety; b4 progression/payout + Loom + user/mod media; b5 watchdog recovery + graceful exit + failure injection — each slice mapped to the host row's acceptance items with its evidence class per platform; the cut may be refined with rationale, but the serial order and one-slice-per-packet discipline stand (T-11)

### Step 3: Board reconciliation + record + pre-completion consult

- [ ] Write `spine-tasks/SP-022-dtrh-admit/record.md` (pin re-verification transcripts, transport decision rationale, consult verdicts + ACTUAL answering models, engine-review presence, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the admission record + slice cut; verdict text in record.md
- [ ] Update `client/docs/task-board.md` admit row → `WIP` with evidence + named limits (Wayland, host→page Linux shape if limited, owner async-veto noted) — row never `DONE` by worker; the DTRH host row's BLOCKED text annotated "admit landed, first slice = SP-023"
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (client build 0W/0E + both test projects green — pollution guard; spike re-build outputs recorded separately)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Package pin re-confirmed from the live feed + restore/build re-run on BOTH platforms; Linux native deps pinned with apt names/versions
- Transport = minimal transport-only diff SPEC with an explicit per-direction matrix and a DECIDED Linux host→page shape; loopback security contract written and approved-by-decree; no classic fallback; Wayland named limit
- Host slice cut b1…b5 recorded with per-slice acceptance mapping + evidence classes
- Board admit row `WIP` with named limits (never `DONE`); both solo Fable consults persisted with actual answering models

## Do NOT

- Write product code or new spike code (design-record only); re-derive payload trust from presence (SP-011 hashes are the trust anchor); choose the unchanged-bridge.js shape (empirically falsified on Linux); claim Wayland; make network calls beyond package research/restore; modify `ConditioningControlPanel/**`, `client/src/**`, `client/tests/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-022): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/dtrh-admission.md` (deliverable), `client/docs/task-board.md` (row evidence), `spine-tasks/SP-022-dtrh-admit/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only — **UTF-8 only**)

## Amendments

- 2026-07-21 (authoring): **Phase 5 decomposition consult verdicts applied (solo Fable 5):** admit record FIRST (design-record with the host slice cut); decree lifts approvals but doesn't write engineering records — all values pinned with live evidence; stay serial; host sliced b1…b5, one slice per packet. Preceded by e0 engine upgrade (2.8.0→2.10.0) + hygiene (18 rows ratified DONE; pause clause corrected: Sol fallback dead).
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
