# SP-022 record — admit DTRH browser and origin design

Worker session log. Design decisions, consult verdicts (provenance), transcripts, surprises, engine-review presence (T-2).

## Engine-review presence log (T-2)

Packet emits structured `## Review Level: 2` heading. Per-call record:

| Step | spine_review_step call | Result | Engine review fired? |
|------|------------------------|--------|----------------------|
| 1 | type=plan after step-1 commit | `skipped: true`, `spawnFailed: false` — SP-195 nested-spawn block, reviewLevel echoed 2 | NO in-worker (by design); engine reviews post-.DONE |

## Step 1 — pin re-verification, transport design, pre-approach consult

### Package pin re-confirmed (live feed, 2026-07-21)

- `https://api.nuget.org/v3-flatcontainer/avalonia.controls.webview/index.json` → 12 versions, latest = **12.0.1** (unchanged since SP-011's 2026-07-19 check; 12.0.0 → 12.0.1 is still the whole 12.0.x train). Raw feed saved: `evidence/nuget-webview-index.json`.
- Nuspec of 12.0.1 re-fetched (`evidence/nuget-webview-12.0.1.nuspec`): license **MIT** (expression), dependency `Avalonia@12.0.0` (minimum). TFMs per SP-011 (net10.0, net8.0, net10.0-android36.0, net10.0-browser1.0).
- Baseline feed re-checked: `Avalonia` latest = **12.1.0** — the product baseline pin is still current (`evidence/nuget-avalonia-index.json`).

### Restore/build re-run of the EXISTING quarantined spike (no new code)

- Windows: `dotnet build client/spikes/CcpSpike.WebView -c Debug` → restore clean, **0W/0E**, 5.56s.
- WSL2 (Ubuntu 26.04): rsync'd `client/` to `~/ccp-sp022/client` (bin/obj/scratch/.git excluded; never /mnt/e), `dotnet build` → **0W/0E**, 8.11s.

### Linux native deps — apt-source check on the WSL2 image (2026-07-21)

- `apt-cache policy` after `apt-get update`: `libwebkit2gtk-4.1-0` installed+candidate **2.52.3-0ubuntu0.26.04.2** (resolute-updates/resolute-security); `libgtk-3-0t64` installed+candidate **3.24.52-0ubuntu1** (resolute). SP-011's pinned versions still current.
- `libwpewebkit-2.0-1`: no policy entry (still unpackaged on Ubuntu 26.04) — SP-011 L1 stands; WPE remains a named limit.

### Transport design (pre-consult proposal)

Detection branch at the top of `bridge.js` only. Page→host: `window.chrome.webview` native postMessage where present, else JSON.stringify → `invokeCSharpAction`. Host→page: Windows unchanged (synthetic MessageEvent dispatch, SP-011 W4/W6 proven); Linux = page polls a host-controlled loopback endpoint on the page origin. Navigation-based rejected. Named risk: `invokeCSharpAction` page→host spike-proven only on the EMBEDDED GTK adapter, not on NativeWebDialog → b1's first gate.

### Pre-approach solo consult (2026-07-21; council unavailable per T-7 — solo only per packet)

Questions: (1) Linux host→page shape; (2) detection-branch shape; (3) b1…b5 cut soundness.

**VERDICT (received text): "Design approved on all three points, with corrections to fold in before you write the admission record."** Corrections received in full:

1. **Sequence-numbered retained delivery** — drain-on-GET has a message-loss window (renderer stall / teardown race); `init`/`manifest` must not be lost. Spec: monotonic seq per host→page message; `GET /bridge/inbox?after=N` returns all above N; host retains until the next poll's `after` acknowledges. Exactly-once at the page + preBuffer-replay equivalence for free.
2. **Long-poll flavor or a named cadence** — fixed-cadence polling either wastes requests or delays boot by up to one interval. Spec long-poll (GET hangs until a message exists or a bounded timeout, e.g. 25s, then returns empty) OR state the chosen cadence + boot-latency budget. Admission chooses long-poll (see dtrh-admission.md §3).
3. **Inbox endpoint is new attack surface** — loopback binding does not stop other LOCAL processes from draining/reading host→page traffic (init contains settings). Spec a per-session unguessable token in the bridge route path (host generates; in the navigated URL; bridge.js reads from `location`) or record token-absence as an accepted risk. Admission specs the token (§4).
4. **`isHosted` import-time precision fix** — SP-011 L5 failed because bridge.js computes `isHosted` at module-eval time from `window.chrome.webview` alone. The diff must extend the import-time detection to `window.chrome.webview || typeof invokeCSharpAction === 'function'` AND select the transport at the same moment — not bolt a branch onto send/receive only.
5. **`invokeCSharpAction(body)` takes a string** — the diff owns JSON.stringify page-side; host owns parse. Windows object-shaped postMessage path stays byte-identical to spike-proven behavior; do NOT unify Windows onto polling (would discard W3–W6 evidence).
6. **WebSocket rejected with the real reason** — managed `HttpListener.AcceptWebSocketAsync` throws PlatformNotSupportedException on Unix; WebSocket is not available cross-platform, record that as the rejection reason (not "could have").

**TRUNCATION (provenance):** the verdict text cut off mid-sentence during the Q2 tail ("…discarding W3–W6 evidence for symmet…"); Q3's elaboration (if any) did not arrive. The verdict's opening line explicitly approved all three points; the six corrections above were received complete. Per SP-011 truncation precedent the loss is recorded, not re-derived. **Actual answering model: solo Fable route (`anthropic/claude-fable-5` per the session's only working consult route — T-7).**

### Slice cut b1…b5 (pre-consult proposal, approved by the verdict's opening line)

b1 host shell + loopback origins + transport diff applied + boot matrix re-run in-product (NativeWebDialog page→host risk first); b2 three local slots + save picker/quick start + protocol v1; b3 native SFX/audio/video + freeze + rendered tint safety; b4 progression/payout + Loom + user/mod media; b5 watchdog recovery (W17 zombie; native ProcessFailed via platform handle) + graceful exit + failure injection.
