# DTRH browser and origin admission (SP-022)

**Date:** 2026-07-21 · **Task:** SP-022 (task-board row "Admit DTRH browser and origin design") · **Authority:** owner decree 2026-07-21 lifted the approval gates; this document is the **engineering record the decree cannot write** — every value below is pinned with live evidence. Owner asynchronous review/veto remains.

**Feeds:** BLOCKED row "Implement web-only DTRH host" — executes as slices **b1…b5** (§7), first slice = SP-023.

---

## 1. Package pin — ADMITTED

| Fact | Value | Evidence |
|------|-------|----------|
| Package | `Avalonia.Controls.WebView` **12.0.1** | Live flat-container feed re-queried 2026-07-21: 12.0.1 is still the current latest (12.0.0 → 12.0.1 is the whole 12.0.x train). Raw feed: `spine-tasks/SP-022-dtrh-admit/evidence/nuget-webview-index.json` |
| License | **MIT** (expression) | Nuspec re-fetched 2026-07-21: `evidence/nuget-webview-12.0.1.nuspec` |
| Dependency | `Avalonia@12.0.0` (minimum) | Same nuspec. Product baseline pins `Avalonia` **12.1.0** (nearest-wins over the minimum — no conflict, no downgrade; SP-011 Step-1 restore analysis). Baseline feed re-checked 2026-07-21: 12.1.0 still current (`evidence/nuget-avalonia-index.json`) |
| TFMs | net10.0, net8.0, net10.0-android36.0, net10.0-browser1.0 | Nuspec (net10.0 covers the product baseline) |
| Native engines | None bundled — managed DLLs + `av-webview.mjs`; platform-native engines | SP-011 nupkg listing |
| Restore/build re-verified | **0W/0E on Windows AND WSL2** (2026-07-21) | Re-run of the EXISTING quarantined spike `client/spikes/CcpSpike.WebView/` (build only, no new code): Windows 5.56s; WSL2 8.11s at `~/ccp-sp022` (native ext4, never /mnt/e) |

**Integration consequence (SP-011 surprise, binding on the host slices):** Avalonia's Win32 NativeControlHost crashes without `supportedOS` entries in an app.manifest (`InvalidOperationException: Unable to create child window`). The product head MUST carry an app.manifest with `supportedOS` before any embedded WebView lands (b1).

## 2. Linux native dependencies — PINNED

WPE WebKit (the docs' embedded Linux engine, `libwpewebkit-2.0-1`) is **not packaged on Ubuntu 26.04** (SP-011 L1; re-confirmed 2026-07-21: no `apt-cache policy` entry on the WSL2 image). The admitted Linux path therefore uses the **WebKitGTK 4.1** stack:

| apt package | Version (pinned 2026-07-21) | Source |
|-------------|------------------------------|--------|
| `libwebkit2gtk-4.1-0` | `2.52.3-0ubuntu0.26.04.2` | resolute-updates / resolute-security (Ubuntu 26.04) |
| `libgtk-3-0t64` | `3.24.52-0ubuntu1` | resolute (Ubuntu 26.04) |

These are **distro system packages, not publish sidecars** — SP-010's natives-beside-exe strategy (`IncludeNativeLibrariesForSelfExtract` NOT set) ships only the .NET/Avalonia natives beside the exe; WebKitGTK/GTK load from the system via dlopen, exactly like the fontconfig/freetype/X11/ICU floor recorded in `release-publish-gates.md` gate 8. The Linux native-deps floor therefore grows by these two packages; the install documentation line is `apt install libwebkit2gtk-4.1-0 libgtk-3-0t64`. WPE remains a **named limit** (SP-011 owner question: WPE-SHM performance unmeasurable on WSLg; needs real-hardware Ubuntu 24.04 or another WPE source — §5.1-class, not settled here).

## 3. Transport selection — minimal transport-only diff (DECIDED)

**The unchanged `bridge.js` shape is empirically falsified** (SP-011 L5: `window.chrome.webview` absent on Linux → `isHosted=false` → no ready, no boot). The admitted transport is **one minimal transport-only diff inside `bridge.js`** (the capability-inventory's allowed host-only compatibility edit; blob `13af3f4d` remains the trust anchor for everything else — §6).

### 3.1 Diff shape (exact)

At the TOP of `bridge.js`, replacing the WebView2-only detection:

1. **Import-time hosted detection (load-bearing):** `isHosted` must become `!!(window.chrome && window.chrome.webview) || typeof invokeCSharpAction === 'function'`. SP-011 L5 failed precisely because `isHosted` is computed at module-eval time from `window.chrome.webview` alone; the transport selection must happen at the SAME moment, before any send/receive use.
2. **Page→host:** if `window.chrome.webview` exists → native `postMessage` (object-shaped, byte-identical to spike-proven W3). Else → `invokeCSharpAction(JSON.stringify(message))` — `invokeCSharpAction(body)` takes a **string**; the diff owns stringify page-side, the host owns parse.
3. **Host→page Windows:** unchanged — host dispatches a synthetic `MessageEvent` on `window.chrome.webview` (W4/W6 proven, incl. preBuffer replay).
4. **Host→page Linux: page long-polls a host-controlled loopback endpoint on the page origin** (DECIDED — see 3.3).

### 3.2 Per-direction transport matrix

| Direction | Windows (WebView2 embedded) | Linux (WebKitGTK NativeWebDialog) |
|-----------|------------------------------|-----------------------------------|
| Page→host | `window.chrome.webview.postMessage` (native; also `invokeCSharpAction` — W3) | `invokeCSharpAction(JSON.stringify(...))` → `WebMessageReceived` |
| Host→page | Synthetic `MessageEvent` dispatch on `window.chrome.webview` (W4) | Long-poll `GET /bridge/<token>/inbox?after=N` on the page origin (3.3) |
| Ordering/preBuffer | Native bridge.js preBuffer replay (W6) | Sequence-numbered retained delivery (3.3) — replay-equivalent |

Identical on both platforms: loopback origin serving (SP-011 L8 — shared assembly), the protocol v1 message vocabulary, the boot contract (ready → init+manifest → engine live). Per-platform divergence: host→page channel only. **Second divergence (pre-completion consult, named for b3):** the admitted Linux shape is a separate WebKitGTK TOPLEVEL window, not an embedded control — host-drawn layers (rendered tint, freeze visuals) CANNOT composite over the web surface on Linux the way they can over the embedded WebView2 child on Windows. Candidate uniform resolution: in-page tint/freeze via protocol v1 message (platform-identical, uses the §3 transport) vs host-side layering (Windows-only shape). b3 decides with evidence; the divergence is named here so b3 does not discover it mid-packet.

### 3.3 Linux host→page — long-poll inbox (the decided shape)

Rejected alternatives, with reasons:

- **Unchanged bridge.js** — falsified on Linux (L5).
- **Navigation-based host→page** — destroys page state, no ordering guarantee; rejected.
- **WebSocket** — managed `HttpListener.AcceptWebSocketAsync` throws `PlatformNotSupportedException` on Unix; not available cross-platform. Recorded so it is never revived as a "could have."
- **Unify Windows onto polling** — would discard W3–W6 spike-proven native evidence for symmetry; rejected.

Contract (consult-verified shape):

- **Route:** `GET /bridge/<token>/inbox?after=N` on the **page origin**. `<token>` is a per-session unguessable value generated by the host, present in the URL the host navigates to; `bridge.js` reads it from `location`. The token closes the local-process attack surface (loopback binding alone does not stop another local process from draining or reading host→page traffic; `init` contains settings).
- **Sequence-numbered retained delivery:** every host→page message carries a monotonic seq. The endpoint returns all messages with seq > N. The host RETAINS messages until the next poll's `after` acknowledges them — exactly-once at the page, immune to a lost response (renderer stall / teardown race). A late-registering handler re-reads from its last seq → preBuffer-replay equivalence.
- **Long-poll flavor:** the GET hangs until at least one message exists or a bounded timeout (~25s) elapses, then returns empty. Push-grade latency for `init`/`manifest` without cadence tuning; still a plain GET inside the GET-only contract.
- **Failure honesty:** inbox GETs outside the token route → 404; non-GET → 405; the endpoint serves JSON only, never payload bytes.

**Named risk (b1's first gate):** `invokeCSharpAction` page→host is spike-proven only on the EMBEDDED GTK adapter (L5), NOT on the NativeWebDialog path (L7 observed rendering + the no-InvokeScript constraint only). If page→host fails on the dialog, b1 stops and reports — it does not invent a new transport.

## 4. Loopback security contract — APPROVED BY DECREE, written as text

The decree lifts the approval; this is the contract it approves (SP-011 §3 evidence, consult corrections folded in):

1. **Two GET-only loopback origins on 127.0.0.1**, ephemeral ports (random 49152–65535 retry loop — HttpListener cannot bind port 0; no URL ACL needed on Windows).
   - **Page origin:** `GET /dtrh/*` → overlay-first over the READ-ONLY payload tree; `GET /dtrh/` → index.html; `GET /health` → 200; `GET /bridge/<token>/inbox` → long-poll inbox (§3.3); everything else → 404; non-GET → 405.
   - **Media origin:** `GET /media/*` → payload `assets/` (READ-ONLY) with `Access-Control-Allow-Origin: <page-origin>` and `Access-Control-Expose-Headers: Content-Range`; OPTIONS preflight → 204 `Access-Control-Allow-Headers: range` (Range is NOT a CORS-safelisted request header).
2. **Two origins, not one:** preserves the WPF `ccp.game`/`ccp.assets` cross-origin shape so CORS/taint checks stay meaningful. Single-origin would make them trivially same-origin and prove nothing. The payload is origin-agnostic (root-relative `/dtrh/...` importmap + host-supplied absolute media URLs) — proven with zero payload change.
3. **Range semantics:** `Range` → 206 + `Content-Range`; invalid → 416. Required by video seek.
4. **MIME allowlist:** fixed extension→type table, `X-Content-Type-Options: nosniff` on every response, unknown extension → **415 deny-by-default** (tightening over the spike's octet-stream fallback). **The allowlist is pinned from the trust-anchored tree, not aspirational:** extension sweep of `git ls-tree -r 40be29df…` (1536 files — count matches the tree-hash claim) yields exactly 9 extensions, ALL covered by the spike's table: `.css 1, .gif 6, .html 2, .js 74, .json 1, .mp3 1306, .png 129, .webm 1, .webp 16`. No fonts/models/wasm/shaders exist in the payload. Transcript: `spine-tasks/SP-022-dtrh-admit/evidence/dtrh-tree-files.txt`.
5. **CORS-on-errors:** every refusal on the media origin carries CORS headers — a CORS-less error surfaces to `fetch()` as an opaque TypeError and silently aborts probes (SP-011 W18 lesson). Error diagnosability is part of the contract.
6. **Traversal refusal (403):** encoded `..%2F`, `%2e%2e`, backslash, drive-colon, leading-slash, escape-under-root. (Literal `..` is normalized away by HTTP clients before reaching the server — recorded so nobody "proves" traversal wrong with a normalized URL.)
7. **Localhost binding only** — both origins bind 127.0.0.1, never a LAN interface; plus the per-session bridge token (§3.3) on the host→page channel.
8. **Sensitive-logging ban:** request logs record route classes, never media file contents, settings values, or the bridge token; the token never appears in logs (SP-018's sensitive-logging audit pattern applies to the host slices).

## 5. No classic fallback — commitment and honesty

- **Windows:** WebView2 (embedded `NativeWebView`), runtime preinstalled Win11 / installer on Win10. Proven by SP-011's Windows matrix.
- **Linux X11/XWayland:** WebKitGTK **NativeWebDialog** path (dedicated window) — the only shape observed rendering for real (L7). The embedded GTK adapter never presents on WSLg/X11 (L4, adapter declares NativeDialog-only scenarios) — embedded is NOT admitted on Linux until WPE evidence exists.
- **No classic fallback:** there is no external-browser, no "open in default browser," no degraded static-page substitute. Where the admitted engines are absent, the product reports **honest unsupported** (typed SP-006 capability state), never a silent substitute.
- **Wayland: named limit.** WSLg is X11 via XWayland; no Wayland-native session evidence exists. Owner question §5.1 is untouched; nothing in this record claims Wayland.

## 6. Payload trust anchor

The DTRH payload stays READ-ONLY evidence. Trust derives **only** from SP-011's recorded identity: git root tree `40be29df822bbfece639b435b0820419aed54c19` (1536 files, ~383MB), `bridge.js` blob `13af3f4d` (byte-unchanged in every spike run; §3's transport diff is the ONE admitted change, applied in-product by the host slices, reviewable as a diff against this blob). Overlay files are NEW paths that never shadow a payload file. Presence is never re-derived as trust.

## 7. Host slice cut b1…b5 (serial, one slice per packet)

Maps the "Implement web-only DTRH host" row's acceptance to five packets. Evidence classes: **WH** = Windows headed, **WX** = WSLg/X11 session facts (no input automation — SP-008), **U** = unit/headless both platforms. Serial order and one-slice-per-packet discipline stand; refinement is by rationale only.

| Slice | Scope | Host-row acceptance items covered | Key evidence (class) |
|-------|-------|-----------------------------------|----------------------|
| **b1** (SP-023) | Host shell (product window + app.manifest §1) + loopback origin serving (§4) + transport diff applied (§3) + boot matrix re-run **in-product** | Foundation for all; protocol v1 boot handshake (ready → init+manifest → engine live) | Boot matrix re-run: engine live, transport checks both directions, focus-claim at ready, autoplay flag — **WH**; Linux NativeWebDialog render + **page→host risk gate first** (§3.3) — **WX**; loopback contract tests — **U** |
| **b2** | Three local save slots + save picker / quick start + protocol v1 (full message vocabulary) | save picker/quick start, three local slots, protocol v1 | Slot create/select/persist via SP-005 machinery; picker + quick-start flows — **WH** + **WX** render; protocol round-trips — **U** + **WH** |
| **b3** | Native SFX/audio/video + freeze + rendered tint safety | native SFX/audio/video, freeze, rendered tint safety | SFX cues (native-cue message path per W15), audio/video playback, freeze behavior, tint safety — **WH** (pixel-verified); **named platform divergence: tint/freeze layering is NOT uniform** (Linux = separate dialog window, no host compositing over the web surface — §3.2; candidate uniform resolution = in-page tint/freeze via protocol v1 message) — Linux **WX** + divergence decision recorded |
| **b4** | Progression/payout + Loom + user/mod media | progression/payout, Loom, user/mod media | Payout round-trip (m2test.js-class harness), Loom integration, user/mod media served within the §4 contract — **WH** + **U**; Linux — **WX** |
| **b5** | Watchdog recovery + graceful exit + failure injection | watchdog recovery, graceful exit | W17 zombie class: heartbeat watchdog + **native `ProcessFailed` via `TryGetPlatformHandle`** (documented immediate signal; `AdapterDestroyed` does NOT fire — W17); bounded `exit-done` wait; renderer-kill / blocked-route / missing-media injection — **WH**; Linux equivalents where the dialog path allows — **WX** + honest named limits |

Cross-cutting for every slice: Wayland never claimed (§5); no classic fallback (§5); payload READ-ONLY with §6 hashes as the trust anchor; contract pollution guard (product build 0W/0E + both test projects) green on both platforms per packet.

## 8. Explicit non-claims

- No Wayland-native evidence (§5.1 untouched).
- No product code in this packet — design record only. The `bridge.js` transport diff lands in b1, not here.
- No WPE claim — unpackaged on Ubuntu 26.04; embedded Linux performance unmeasured (owner question stands).
- No claim that NativeWebDialog page→host works — it is b1's first gate (§3.3).
- No installer/packaging work — Linux native deps are documented system requirements (§2), not shipped bits.
