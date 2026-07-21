# STATUS: SP-018 — spike browser-to-native online-video handoff

**Current Step:** DONE — all steps complete
**Last Updated:** 2026-07-21 (Step 5 green: Windows contract 0W/0E + 213/213 + 22/22, diff-check clean, scope audited)

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
**Status:** ✅ Complete (2026-07-21; in-worker plan review skipped by design SP-195)

- [x] WebView2 host: per-row discovery → transfer → decode-verified playback (or typed limitation), success + failure shapes (7/7)
- [x] Expiring-URL valid+expired; cookie/header negative controls; blob:/MSE outcome; DRM detect-report asserted in logs
- [x] **Sensitive-logging audit run green — recorded as evidence** (V5 cache-leak found+fixed, re-audit GREEN incl. profile)

### Step 4: WSLg/Linux gate + record + pre-completion consult + board reconciliation
**Status:** ✅ Complete (2026-07-21; in-worker plan review skipped by design SP-195)

- [x] WSL2 in-packet gate (`~/ccp-sp018`, never /mnt/e): native decode side REAL (14/14); browser-discovery EXCEEDS SP-011 inheritance (7/7 real via WebKitGTK InvokeScript); contract green (213/213 + 22/22, 0W/0E)
- [x] `client/docs/video-handoff-spike.md` — named observation per row + supported/unsupported matrix pending-owner
- [x] record.md complete
- [x] Pre-completion solo Fable 5 consult (verdict in record.md; 5 corrections applied incl. V8)
- [x] Board row → `WIP` with evidence + named limits (never `DONE`)
- [x] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ✅ Complete (2026-07-21)

- [x] Contract testCommand green (client 0W/0E + 213/213 + 22/22 Windows AND WSL2; spike host builds clean separately 0W/0E)
- [x] `git diff --check` clean
- [x] `git status --short` = File Scope only (audited lane diff: spike + 3 docs + task folder only)
