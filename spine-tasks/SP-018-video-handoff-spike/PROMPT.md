# Task: SP-018 — spike browser-to-native online-video handoff

## Mission

Execute `client/docs/task-board.md` row **"Spike browser-to-native online-video handoff"** (P0, THIRD/FINAL row of Phase 3 in `spine-tasks/CONTEXT.md`): define and exercise the supported matrix for direct MP4/WebM, approved HLS/DASH manifests, target sites, cookies/headers, expiring URLs, `blob:`/MSE, and DRM; prove safe transfer where possible; unsupported sources report a limitation without browser fullscreen, capture mirroring, DRM bypass, or sensitive logging. Deliverable: a SECOND quarantined spike host (`client/spikes/CcpSpike.VideoHandoff/`, OUT of the solution — SP-011/SP-017 pattern) + `client/docs/video-handoff-spike.md` with a named observation per matrix row and the supported/unsupported matrix recorded pending-owner. **Zero product-code change.**

**Honesty framings (Phase 3 decomposition consult, binding):** (a) the spike does NOT depend on the owner-blocked DTRH admit row — the handoff mechanism (URL/cookie/header transfer to a native decoder) is independent of the bridge.js transport choice; (b) **SENSITIVE LOGGING BAN IS A CHECKBOX:** cookie/header/signed-token values are NEVER logged — log presence + redacted shape (e.g. `cookie:present(len=31)`), never the value; the spike's own logs are audited for this before .DONE; (c) **DRM = detect-and-report-limitation ONLY** — detect EME/DRM signaling, report, never attempt bypass, key extraction, or capture mirroring; (d) **"target sites" = owned/public test content only** (the spike's loopback lab + well-known public test vectors) — no commercial-site scraping, no ToS-gray sources; (e) **Linux half inherits SP-011's findings** (embedded WebView never-presents on WSLg; NativeWebDialog renders but has no host→page InvokeScript; page→host `invokeCSharpAction` works) — browser-discovery evidence on Linux is mostly named limits; the native decode side on Linux IS real evidence; (f) transfer success claims come from native-decoder events (track metadata + time progression + end events), never from "the request returned"; (g) no Wayland claim (§5.1); (h) presentation/fullscreen is OUT of scope — this spike proves the SOURCE reaching the native decoder; presentation is the unified-video row.

## Dependencies

- **Task:** SP-017 (Phase-3 serial chain)

## Context to Read First

- `client/docs/task-board.md` — the handoff spike row + Decisions-needed + gate history (Phase 3 decomposition verdict)
- `client/docs/webview-dtrh-spike.md` (SP-011) — WebView2 Windows proof + the THREE Linux findings the Linux half inherits (embedded never-presents; NativeWebDialog renders/no-InvokeScript; bridge transport absent); loopback-server pattern (`client/spikes/CcpSpike.WebView/LoopbackServer.cs`)
- `client/docs/audio-backend-spike.md` (SP-017) — quarantined-spike + named-observation + pending-owner-selection pattern (most recent exemplar)
- WPF sources (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/Video/`, `ConditioningControlPanel/Services/Media/` — the incumbent video behavior (LibVLCSharp.WPF 3.8.5 + VideoLAN.LibVLC.Windows 3.0.21)
- First attempt (READ-ONLY, lessons-only): `ConditioningControlPanel/CCP.Core/Services/Video/` (`IVideoService.cs`, `VideoMetadataCache.cs`), `CCP.Core/Services/AIService/KnownVideoLinks.cs` + `client/docs/first-attempt-lessons.md` — cite video/handoff REJECT lessons explicitly
- Required skills: load `wpf-parity` before Step 1

## File Scope

- `client/spikes/CcpSpike.VideoHandoff/**` (quarantined spike host — NOT added to `client/CcpClient.sln`)
- `client/docs/video-handoff-spike.md` (evidence deliverable + supported/unsupported matrix)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-018-video-handoff-spike/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/video-handoff-spike.md`, `client/spikes/CcpSpike.VideoHandoff/CcpSpike.VideoHandoff.csproj` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/src/**`, `client/tests/**`, `.spine/**` |
| artifactsMustExist | `client/docs/video-handoff-spike.md`, `spine-tasks/SP-018-video-handoff-spike/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Video archaeology + source-matrix definition + package admission pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF + first-attempt video archaeology (READ-ONLY, `File.cs:line`): how online/video sources are discovered and handed to the native player today (URL flow, headers/cookies if any, expiry handling, limitation reporting); cite handoff REJECT lessons
- [ ] Source-matrix definition: every acceptance row (direct MP4, direct WebM, HLS, DASH, target-site shape, cookies, custom headers, expiring signed URLs, `blob:`/MSE, DRM) → spike-owned loopback fixture design + any public test vector used (URL + license noted)
- [ ] Native decoder candidates FROM LIVE FEEDS (exact versions): LibVLCSharp (+ VideoLAN.LibVLC.Windows native package; Linux `libvlc` via apt) and any maintained alternative the research surfaces — per candidate: exact version, license (+ packaging implication), native deps per OS
- [ ] **Pre-approach solo consult = PACKAGE ADMISSION GATE** (Fable 5, solo; council unavailable T-7): matrix + decoder candidates + spike design (loopback lab, redaction discipline, decode-event verification, Linux inherited-limits split). Verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Loopback source lab + native decode handoff core

- [ ] `client/spikes/CcpSpike.VideoHandoff/` (console/host, NOT in the solution; loopback lab endpoints: plain MP4/WebM with generated tiny synthetic media (generated locally, no copyrighted fixtures), cookie-gated endpoint, custom-header-gated endpoint, signed-URL endpoint with TTL (valid + expired shapes), segmented HLS playlist endpoint, DASH MPD endpoint, `blob:` fixture page, fake-DRM/EME-signaling endpoint)
- [ ] Native decode probe (admitted decoder): open URL (+optional headers/cookies) → assert track metadata (codec/resolution/duration), time progression, end event — per matrix row; limitation reports typed (expired/unsupported/DRM-detected/blob-untransferable-strategy)
- [ ] **Redaction discipline implemented:** every log line carries presence/shape only; a `--audit-logs` self-check scans emitted logs for known secret values and FAILS on any hit

### Step 3: Windows browser→native handoff evidence

- [ ] WebView2 host per SP-011 pattern (Windows): page-side discovery of each matrix source → transfer to native decoder → decode-event-verified playback (or typed limitation) — per matrix row, both success and failure shapes
- [ ] Expiring-URL: valid transfer + expired → typed limitation (no retry-storm); cookies/headers: gated endpoints pass ONLY when values transfer (negative control: without transfer → 401/403); `blob:`/MSE: detect → strategy outcome or typed limitation; DRM: EME signaling detected → limitation report (no bypass attempt, asserted in logs)
- [ ] **Sensitive-logging audit run:** `--audit-logs` over the full evidence run — zero secret values in any log; record the audit itself as evidence

### Step 4: WSLg/Linux gate + record + pre-completion consult + board reconciliation

- [ ] WSL2 in-packet gate (`~/ccp-sp018`, never /mnt/e): native decode side REAL (libvlc via apt: loopback matrix decode-level outcomes incl. cookies/headers/expiry); browser-discovery side per SP-011 inherited findings — exercise what's real (e.g. page→host transfer shape if demonstrable via NativeWebDialog), name the rest as limits; contract testCommand ALSO green on WSL2 (pollution guard)
- [ ] `client/docs/video-handoff-spike.md` — named observation per matrix row (supported/limitation + evidence class per platform) + the supported/unsupported matrix recorded pending-owner
- [ ] Write `spine-tasks/SP-018-video-handoff-spike/record.md` (archaeology, admission verdict, matrix outcomes, redaction audit, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + matrix; verdict text in record.md
- [ ] Update `client/docs/task-board.md` row → `WIP` with evidence + named limits (Linux browser-discovery, Wayland §5.1, matrix pending-owner, real-site shapes untested by design) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (client build 0W/0E + both test projects green — pollution guard; spike host builds clean separately)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every matrix row has a named observation with evidence class per platform: direct MP4/WebM, HLS, DASH, target-site shape (owned/public only), cookies, custom headers, expiring URLs (valid+expired), `blob:`/MSE, DRM detect-report
- Transfer successes are native-decoder-event-verified; failures are typed limitations (never browser fullscreen, capture mirroring, DRM bypass); negative controls present (ungated vs gated)
- **Sensitive-logging audit green and recorded**; no cookie/header/token value appears in any artifact
- WSLg: native decode side real; browser-discovery limits named per SP-011 inheritance; Wayland untouched
- Quarantine holds: zero `client/src/**`/`client/tests/**`/`client/CcpClient.sln` changes; contract green both platforms; both solo Fable consults persisted with actual answering models; board row `WIP` (not `DONE`)

## Do NOT

- Add the spike to `client/CcpClient.sln` or touch product code/tests; scrape commercial/ToS-gray sites; attempt DRM bypass/key extraction/capture mirroring; log cookie/header/token VALUES (presence+shape only); claim Wayland; make network calls beyond package research/restore, the loopback lab, and declared public test vectors
- Answer owner questions (final matrix ratification, approved-site list, native decoder selection for the unified-video row — record pending-owner); modify `ConditioningControlPanel/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-018): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/video-handoff-spike.md` (deliverable), `client/docs/task-board.md` (row evidence), `spine-tasks/SP-018-video-handoff-spike/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only)

## Amendments

- 2026-07-21 (authoring): **Phase 3 decomposition consult verdicts applied (solo Fable 5):** handoff spike LAST (Linux half inherits SP-011's embedded-never-presents finding → mostly named limits; cheap certainty first); spike does NOT depend on the owner-blocked DTRH admit row; **two binding cautions encoded as checkboxes — (i) no sensitive logging (cookie/header/token values; audit-the-logs self-check), (ii) DRM detect-and-report-limitation only, never bypass.** T-11 sizing: each evidence step <2h; orchestrator sets `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch (headed WebView2 work on Windows).
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
