# Browser-to-native online-video handoff spike — evidence + supported/unsupported matrix

**Date:** 2026-07-21 · **Task:** SP-018 (task-board row "Spike browser-to-native online-video handoff") · **Status:** spike outcome, **matrix PENDING-OWNER ratification** (like every spike row; the board row stays `WIP`)

Quarantined spike host: `client/spikes/CcpSpike.VideoHandoff/` (console + Avalonia browser host, NOT in `client/CcpClient.sln`; inherits only `client/Directory.Build.props`). Raw observation logs: `spine-tasks/SP-018-video-handoff-spike/evidence/run-windows.jsonl` (decode matrix, 128 obs), `run-windows-browser.jsonl` (105), `run-wslg.jsonl` (128), `run-wslg-browser.jsonl` (102). Worker log, consult verdicts, research citations: `spine-tasks/SP-018-video-handoff-spike/record.md`.

**Honesty framings (packet, binding):** (a) independent of the owner-blocked DTRH admit row — the handoff mechanism (URL/cookie/header transfer to a native decoder) does not depend on the bridge.js transport choice; (b) **sensitive-logging ban enforced by self-check** — cookie/header/signed-token values are never logged (presence+shape only, e.g. `cookie:present(len=40)`); `--audit-logs` re-registers the accumulated secret registry and FAILS on any value hit — GREEN on every recorded run INCLUDING the WebView2 profile (finding V5); (c) **DRM = detect-and-report-limitation ONLY** — EME usage observed, typed limitation, no bypass/key-extraction/capture-mirroring attempted (asserted in logs); (d) **target sites = owned loopback pages only** — no commercial scraping, no ToS-gray sources; real-site shapes untested BY DESIGN (named limit); (e) transfer successes are native-decoder-event-verified (parse track metadata + vmem frame delivery + progression + EndReached vs PRE-DECLARED thresholds), never HTTP 200 or call returns; (f) no Wayland claim (§5.1 untouched); (g) presentation/fullscreen OUT of scope — this spike proves the SOURCE reaching the native decoder; presentation is the unified-video row.

## 1. Admitted packages (package admission gate, solo Fable 5 consult 2026-07-21)

| Package | Version | Published | License | Native deps | Notes |
|---|---|---|---|---|---|
| **LibVLCSharp** | 3.10.0 | 2026-06-17 | LGPL-2.1-or-later | none bundled | net10.0 TFM; WPF incumbent pins 3.8.5 (skew recorded; decoder SELECTION for the unified-video row stays pending-owner) |
| **VideoLAN.LibVLC.Windows** | 3.0.23.1 | 2026-04-16 | LGPL-2.1-or-later | bundled libvlc win-x64 | WPF pins 3.0.21 |
| Linux native | distro `libvlc` 3.0.23-1 via apt (Ubuntu 26.04) | — | LGPL-2.1-or-later | apt (`libvlc5`, `libvlccore9` — already installed) | NO official `VideoLAN.LibVLC.Linux.*` nuget exists (flatcontainer 404 verified 2026-07-21) |
| FFmpeg.AutoGen (doc-level alternative) | 8.1.0 | 2026-04-28 | license-file (bindings LGPL; natives LGPL/GPL build-dependent) | external FFmpeg builds (no official native nuget) | hand-rolled demux pipeline = spike-overkill vs one-call URL open; recorded, not admitted |
| `Avalonia.Controls.WebView` (browser layer) | 12.0.1 | SP-011-admitted | MIT | WebView2 (Windows) / WebKitGTK 2.52.3 (WSLg) | reuse, no new admission |

SP-017's LibVLCSharp rejection does not apply: that was backend-shape-for-AUDIO (per-instance player, no sample mixer); per-instance decode IS the video-handoff shape. LGPL sidecar/relink obligations = SP-010 packaging note, pending-owner.

**Fixtures (deterministic, committed):** lavfi-generated (`testsrc2` 96x96@10fps 2s + 440Hz sine; license-safe): clip.mp4 (h264+aac, +faststart, SHA-256 `eb14abd6…9fbc`), clip.webm (vp8+vorbis, `5b32afa0…cdfb`), HLS fMP4 v7 (EXT-X-MAP), HLS TS variant, DASH static MPD (SegmentTemplate fMP4). ffmpeg exists on the Windows box but NOT on WSL2 — fixtures generated once and committed so neither platform needs it at runtime.

## 2. Named observations — supported/unsupported matrix (PENDING-OWNER)

Pre-declared success thresholds (declared before runs): duration ∈ [1500, 2500] ms; vmem frames ≥ 5 (2s@10fps ⇒ 20 expected); TimeChanged ≥ 3; progression = span ≥ 1000 ms OR maxPosition ≥ 0.5 OR (frames ≥ 5 AND wall-clock-to-end ≥ 1500 ms — finding V8: adaptive demuxers FLAKILY report neither Time nor Position); EndReached ≤ 12 s. Evidence class per row: **decode** = native-decoder events; **browser** = live-DOM discovery → transfer → decode events; **typed** = limitation from the redaction-safe preflight classification layer (libvlc `EncounteredError` is generic and cannot distinguish 401/403/expired).

| # | Source form | Windows (WebView2 150.0.4078.83 + libvlc 3.0.23.1) | WSLg/Linux (WebKitGTK 2.52.3 + libvlc 3.0.23-1) | Evidence |
|---|---|---|---|---|
| M1 | Direct MP4 | **SUPPORTED** — h264 96x96, aac 44.1k, 37 frames, end | **SUPPORTED** — same shape, 14/14 decode matrix | decode + browser (B1) |
| M2 | Direct WebM | **SUPPORTED** — VP80 96x96, vorbis, 30 frames, end | **SUPPORTED** | decode |
| M3 | HLS manifest (fMP4 v7 EXT-X-MAP; TS variant) | **SUPPORTED (fixture scope: single-segment VOD playlist)** both variants — 36/34 frames, end; track metadata via decoder-format callback (V4). Multi-segment, multi-variant/master, and live HLS UNTESTED (named limit) | **SUPPORTED (same fixture scope)** | decode |
| M4 | DASH manifest (static MPD, single-chunk SegmentTemplate, fMP4) | **SUPPORTED (fixture scope: single-chunk static MPD)** — h264 96x96 (MPD parse exposes tracks directly), 36 frames, end. Multi-chunk/multi-period/live DASH UNTESTED (named limit) | **SUPPORTED (same fixture scope)** | decode |
| M5 | Target-site shape (owned loopback `<video>` page only) | **SUPPORTED** — live-DOM discovery of `src` → transfer → decode events (B1) | **SUPPORTED** — same through WebKitGTK InvokeScript (V6) | browser |
| M6 | Cookie-gated | **direct-decoder-auth: NOT SUPPORTED** (libvlc 3.x sends NO cookie; `:http-cookie` media option silently ignored — gate log `presented=absent` ×3). **proxy-mediated: SUPPORTED** (relay injects; gate `presented=valid` ×5; negative control no-inject → typed `auth-required`, no decode events). Relay = strategy evidence, PENDING-OWNER admission | same shape, both classes | decode + browser (B4) |
| M7 | Custom-header-gated | **direct: NOT SUPPORTED** (no arbitrary-header mechanism in libvlc 3.x). **proxy-mediated: SUPPORTED** with negative control | same shape | decode + browser (B5) |
| M8 | Expiring signed URL (HMAC TTL) | **SUPPORTED when opened before expiry** (valid → decode events; bad-sig → typed `auth-required`). **Expired → typed `source-expired` at PREFLIGHT — ZERO decoder opens, no retry-storm.** **Mid-stream expiry (re-open on seek after TTL) UNTESTED — named limit** | same shape | decode + browser (B2/B3) |
| M9 | `blob:` / MSE | **LIMITATION `blob-untransferable`** — protocol `blob:` read off the LIVE DOM (not asserted); page really created the object URL AND MSE append succeeded (`mse-append-ok`, WebM SourceBuffer). No decoder attempt; no browser-fullscreen/capture fallback (REJECT lessons honored) | **LIMITATION `blob-untransferable`** — same detection through WebKitGTK; `mse-append-ok` also on WebKitGTK | browser (B6) |
| M10 | DRM (EME signaling) | **LIMITATION `drm-detected`** — EME USAGE observed: `requestMediaKeySystemAccess('org.w3.clearkey')` GRANTED on the page; no bypass/key-extraction/capture attempted (asserted) | **LIMITATION `drm-detected`** — EME usage observed as DENIED (`TypeError` — WebKitGTK 2.52.3 ships no ClearKey); the ATTEMPT is the signaling (per admission consult) | browser (B7) |

**Verdict shape:** every matrix row above is a NAMED observation with evidence on BOTH platforms (no Windows-only claims except where marked). Decode matrix 14/14 PASS exit 0 (Windows AND WSLg); browser matrix 7/7 PASS exit 0 (Windows AND WSLg). Contract pollution guard: `CcpClient.sln` 0W/0E + 213/213 + 22/22 on BOTH platforms.

## 3. Findings (failures recorded, never patched over)

- **V1 — D3D11VA hw decode segfaults the probe environment** (libvlc 3.0.23.1 default) even parsing a LOCAL file (d3d11va decoder-setup path) → probe forces software decode. WPF incumbent plays hw decode + real vout; crash is specific to the headless-probe shape. Matrix risk for the unified-video row.
- **V2 — dummy-vout sw-decode pipeline crashes ~at/after EndReached on a BACKGROUND libvlc thread** (asynchronous; disposal orders, `--no-audio`, and the WPF-pinned 3.8.5+3.0.21 combo all crash; `--no-video` stable). **vmem memory-callback vout is STABLE (5/5 clean runs both platforms)** and yields frame-level decode proof (display-callback count) — strictly stronger than TimeChanged.
- **V3 — libvlc native teardown segfaults in this probe shape** (media/player release in ANY order incl. GC-then-release, both version combos, Windows) → spike hard-exits (`_exit`) after flushing line-durable evidence. **Clean teardown is owned by the unified-video row.**
- **V4 — HLS playlists expose no tracks at playlist level via LibVLCSharp 3.10 parse API** (sub-items absent for media playlists); track evidence for HLS = decoder-format-callback observed chroma/dims (`decoded:I420 96x98` — decoder-proposed dims include H.264 coded-height padding). Codec NAME for HLS not observable through this API (recorded, not faked). DASH parse exposes `h264 96x96` directly. Adaptive demuxers report Time=0 → position-based progression evidence pre-declared.
- **V5 — signed-token URLs persisted in the WebView2 HTTP cache** (`Cache_Data/data_1`, caught BY the audit on the first browser run). Mitigation: `Cache-Control: no-store` AT THE SOURCE on signed/gated endpoints + signed-embed pages; re-audit GREEN over the entire scratch INCLUDING the profile. Product implication: token-bearing media URLs need no-store at source and/or profile hygiene (owner decision for the host row).
- **V6 — WebKitGTK InvokeScript works and returns RAW results where WebView2 returns JSON-encoded** (SP-011 L6 said the API exists; this spike is the first empirical exercise — discovery pipeline runs identically on WSLg; both result shapes must be accepted).
- **V7 — WebKitGTK 2.52.3 denies ClearKey EME** (`TypeError`); EME-signaling DETECTION still works (the attempt is observed).
- **V8 — adaptive demuxer Time/Position reporting is FLAKY ACROSS RUNS** (HLS-TS and DASH: one run maxPos=0.63-0.73, the next maxPos=0.00 with identical frames+end). Progression evidence for those rows is the pre-declared frame-paced wall-clock prong (delivered frames + wall-time-to-end ≥ 1500 ms); Time/Position are reported but never required for adaptive sources.

## 4. WPF behavioral contract the matrix serves (archaeology, File.cs:line in record.md)

WPF hands bare URLs straight to libvlc (`VideoService.cs:1341`, `DualMonitorVideoService.cs:120` — `FromType.FromLocation`), sets NO headers/cookies ever (grep-verified), has NO expiry/refresh/retry (`VideoService.cs:1301-1320` EncounteredError → log+cleanup), and reports URL failures to the user NOWHERE (log-only; dialogs exist only for empty lists `:984` and missing codecs `:2019-2024`). The typed-limitation layer this spike demonstrates is therefore an UPGRADE over incumbent behavior, not a regression. First-attempt REJECT lessons honored (cited in record.md): no browser fullscreen, no capture mirroring, explicit supported-source matrix gating, limitations instead of silent degradation.

## 5. Named limits / explicit non-claims

1. **Real-site shapes untested BY DESIGN** — target sites = owned loopback pages + spike fixtures only (packet: no commercial scraping, no ToS-gray sources). Approved-site list is pending-owner.
2. **Mid-stream expiry untested** (decoder re-open on seek after TTL) — the unified-video row owns seek semantics.
3. **Relay = proxy-mediated-auth strategy evidence, PENDING-OWNER admission** — a local proxy holding authenticated media is a real architecture/security decision; direct-decoder-auth for cookies/headers is honestly NOT SUPPORTED by libvlc 3.x. The spike relay is UNAUTHENTICATED on 127.0.0.1 (any local process could have fetched through it during the run) — a product relay needs origin/auth binding as part of that owner decision.
4. **Codec NAME for HLS not observable** via LibVLCSharp 3.10 parse API (V4); decoder-format dims + frames are the track evidence.
5. **libvlc teardown crashes in the probe shape** (V3) — clean teardown unproven, owned by the unified-video row; hard-exit is a spike harness choice, not a product pattern.
6. **Software decode only in the probe** (V1); hw-decode viability with real presentation is the unified-video row's evidence to produce.
7. **No Wayland claim** (§5.1 untouched); WSLg = X11/XWayland session facts. Linux browser-discovery evidence is WebKitGTK-embedded (never presents visually — SP-011 L4 inherited; discovery/transport unaffected).
8. **Final matrix ratification, approved-site list, and native decoder SELECTION for the unified-video row are pending-owner** — this doc records a spike outcome, not a product decision.
9. Signed/cookie/header fixture secrets are per-run random and never logged (audit GREEN); the registry lives in gitignored scratch only.
10. WebM cannot be an HLS segment (spec); HLS evidence uses h264 fMP4/TS. Public test vectors (e.g. Apple bipbop) unnecessary — owned fixtures covered every row; none fetched.
11. **HLS/DASH coverage is fixture-scoped** — single-segment VOD HLS (fMP4 + TS) and single-chunk static DASH only; multi-segment, multi-variant/master playlists, live/EVENT playlists, multi-period DASH, and adaptive bitrate switching are UNTESTED.
12. **EME detection is FIXTURE-INSTRUMENTED** — the spike page logs its own `requestMediaKeySystemAccess` call; detecting EME on an UNCOOPERATIVE page requires host-injected observation (user script / InvokeScript probe / resource-hook), untested here. The `encrypted` media event variant is untested (fixture media is clear). WebKitGTK grants no key system (V7) — a product detector relying on granted-`keySystem` or `encrypted` would have NO Linux evidence yet.
