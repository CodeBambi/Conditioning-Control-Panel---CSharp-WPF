# STATUS: SP-018 — spike browser-to-native online-video handoff

**Current Step:** Step 3 — Windows browser→native handoff evidence
**Last Updated:** 2026-07-21 (Step 2 complete: 14/14 matrix PASS exit 0, audit GREEN)

### Step 1: Video archaeology + source-matrix definition + package admission pre-approach consult
**Status:** ✅ Complete (2026-07-21; in-worker plan review skipped by design SP-195)

- [x] STATUS.md updated before starting work
- [x] WPF + first-attempt video archaeology (READ-ONLY, `File.cs:line`); handoff REJECT lessons cited
- [x] Source-matrix defined (every acceptance row → loopback fixture design + public test vectors w/ licenses)
- [x] Native decoder candidates from live feeds (exact versions/licenses/natives per OS)
- [x] **Package admission solo Fable 5 consult** (verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Loopback source lab + native decode handoff core
**Status:** ✅ Complete (2026-07-21; in-worker plan review skipped by design SP-195)

- [x] `client/spikes/CcpSpike.VideoHandoff/` (out of solution; loopback lab: MP4/WebM synthetic, cookie/header-gated, signed-URL TTL, HLS, DASH, blob fixture, fake-DRM/EME)
- [x] Native decode probe: track metadata + time progression + end events per row; typed limitation reports
- [x] Redaction discipline + `--audit-logs` self-check implemented (GREEN on evidence run)

### Step 3: Windows browser→native handoff evidence
**Status:** 🔄 In Progress

- [ ] WebView2 host: per-row discovery → transfer → decode-verified playback (or typed limitation), success + failure shapes
- [ ] Expiring-URL valid+expired; cookie/header negative controls; blob:/MSE outcome; DRM detect-report asserted in logs
- [ ] **Sensitive-logging audit run green — recorded as evidence**

### Step 4: WSLg/Linux gate + record + pre-completion consult + board reconciliation
**Status:** ⬜ Not Started

- [ ] WSL2 in-packet gate (`~/ccp-sp018`, never /mnt/e): native decode side REAL; browser-discovery limits named; contract green (pollution guard)
- [ ] `client/docs/video-handoff-spike.md` — named observation per row + supported/unsupported matrix pending-owner
- [ ] record.md complete
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] Board row → `WIP` with evidence + named limits (never `DONE`)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (client 0W/0E + both test projects; spike host builds clean separately)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only
