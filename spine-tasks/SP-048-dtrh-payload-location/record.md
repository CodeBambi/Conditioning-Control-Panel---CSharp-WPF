# SP-048 record — DTRH published-artifact payload location (b1 land condition)

## Step 1: empirical inventory + decision + pre-approach consult

### 1.1 What a real publish ACTUALLY lays down (empirical, never assumed)

Command: `pwsh client/tools/publish/publish.ps1` (win-x64, self-contained, PublishSingleFile=true,
clean output dir — transcript: `publish-transcript.txt`, exit 0).

Artifact: `client/artifacts/publish/CcpClient.Desktop-0.1.0-win-x64/` (899 MB total):

| Observation | Measured |
|---|---|
| Single-file exe | 117.5 MB (payload NOT bundled into it) |
| `payload/dtrh/` beside the exe | 1542 files, 380 MB — present via `CopyToPublishDirectory PreserveNewest` |
| `payload-overlay/` beside the exe | 2 files (bridge.js derivative + 1) |
| File-set vs source tree `ConditioningControlPanel/Resources/web/dtrh` | SET-IDENTICAL (`diff` of sorted relative paths, zero lines) |
| Byte-identity (sha256, all files) | payload 1542/1542 match, overlay 2/2 match, 0 mismatches |
| Native sidecars | libvlc/, miniaudio.dll, libSkiaSharp/libHarfBuzzSharp/av_libglesv2 (SP-010 floor intact) |

**Does the single-file bundle see the beside-exe payload? YES — empirically:**
published exe `--verify-assets` → exit 0, line `asset OK copied: 1544 entries present,
case-exact, sweep clean` + `verify-assets: PASS (1551 manifest entries, ...)`.
`AssetSelfCheck` roots the copied-direction check at `AppContext.BaseDirectory`
(AssetManifest.cs:446-451, comment "content sits BESIDE the exe ... single-file-safe by
design"). The green run on the REAL published binary proves `AppContext.BaseDirectory`
resolves to the exe's own directory in the shipped single-file shape.

### 1.2 How the payload root is resolved at runtime TODAY

One code path, all modes (`DtrhParticipant.cs:51-56`):

```
PayloadRoot  = AppContext.BaseDirectory + "payload/dtrh"   (READ-ONLY at runtime)
OverlayRoot  = AppContext.BaseDirectory + "payload-overlay"
MediaRoot    = PayloadRoot + "/assets"
```

`LoopbackServer` (LoopbackServer.cs:53-55,78) full-paths these roots and serves
overlay-first over the read-only payload (§4 contract). Per-mode resolution:

- **Debug:** `bin/Debug/net10.0/payload/dtrh` via `CopyToOutputDirectory PreserveNewest`
  (verified after the Step-2 Debug build — this worktree had no Debug output at task start).
- **Release:** same glob, `bin/Release/net10.0/`.
- **Published:** beside the exe via `CopyToPublishDirectory PreserveNewest` — VERIFIED
  above (set-identical, byte-identical, `--verify-assets` exit 0).

The serving location is observable at runtime: route-class log lines carry
`(page:payload)` / `(page:overlay)` provenance (SP-023 wh/index-run.log).

### 1.3 The three candidates — measured trade-offs

**(a) copy-beside-exe via the existing glob (status quo).** Proven above: publish lays it
down, the bundle sees it, byte-identity to the trust-anchored tree holds end-to-end,
`--verify-assets` copied sweep green. Zero new machinery.

**(b) embedded resources through SP-009's manifest.** The payload is 380 MB vs a 117.5 MB
exe — embedding grows the bundle ~4.2x (~500 MB single-file), and every Debug/Release
build pays the avares embed/compress cost on 1542 files. It would FLIP the asset class
(copied→embedded), contradicting SP-009's copied classification for exactly this consumer;
the §4 server reads from the filesystem regardless, so embedded bytes buy nothing
behaviorally. Size cost measured (380 MB vs 117.5 MB), benefit zero.

**(c) first-run materialization to the user-data root.** The 380 MB must ship somewhere:
embedded (same cost as (b)) or a beside-exe archive (still beside-exe, PLUS an extraction
failure mode, PLUS a second WRITABLE copy in the user-data root — weakening the read-only
trust discipline §6 requires, and doubling the disk footprint). Adds a first-run phase for
zero user-observable gain.

### 1.4 DECISION

**Ratify (a): copy-beside-exe via the existing linked glob. The published-artifact payload
location is `<publish-dir>/payload/dtrh/` beside the single-file exe, served read-only by
the §4 loopback server through `AppContext.BaseDirectory`.**

Rejected: (b) embedded (measured ~4.2x bundle growth, asset-class flip, zero behavioral
gain); (c) first-run materialization (bytes must still ship somewhere; adds extraction
failure modes and a writable second copy that weakens the read-only anchor).

**Integrity discipline post-SP-037 (how hash-verifiability is asserted in this shape):**
1. **Source anchor:** git tree `40be29df…` covers the pre-v6.6.3 bytes; SP-037's
   re-derivation carries the v6.6.3 delta as manifest entries whose provenance names the
   ADDING COMMITS (never citing the stale anchor for new bytes). 1544 copied entries =
   1542 payload + 2 overlay.
2. **Artifact assertion:** `--verify-assets` copied direction (presence + case-exact +
   completeness sweep) rooted at `AppContext.BaseDirectory` — exit 0 on the published
   artifact TODAY (transcript above).
3. **Byte-verifiability:** beside-exe bytes are DIRECTLY hashable against the anchor —
   proven end-to-end today (1542/1542 sha256 match source tree; 2/2 overlay). No
   repackaging/transformation exists between anchor and served bytes.
4. **Runtime read-only:** GET-only server, traversal-refused, never writes to the payload
   root (§4 contract unchanged).

### 1.5 Pre-approach solo consult

**Route:** `consult` mode solo (per the 2026-08-04 rewire; council forbidden by the packet).
**Actual answering model:** unknown to the worker (the tool does not name the model; the
call succeeded on the solo route — recorded honestly, never invented).
**Truncation:** the verdict text truncated mid-sentence (known precedent, SP-023/SP-044);
the received items are complete and actionable and were honored in full.

**Verdict (received text, verbatim substance):** "your decision is right. Ratify
copy-beside-exe. But the evidence set as it stands does NOT yet discharge the condition,
and your planned Features/Dtrh change needs one constraint added (non-fatal) plus one job
it must earn (falsifiability)."

Applied corrections:
1. **Falsifiability hole (Q2):** all prior evidence is consistent with the app quietly
   reading the REPO tree (it exists at a stable path on this box). `--verify-assets`
   passing proves the files are beside the exe, NOT that the server served from there.
   Correction applied: the Step-3 boot proof runs the published artifact from the MOVED
   copy (`%TEMP%\ccp-sp010-portable`, the matrix.ps1 gate-6 location), and the guard's
   startup diagnostic prints the RESOLVED payload root path so the boot transcript itself
   names where the server served from.
2. **Guard constraint:** the payload-presence guard must be NON-FATAL (typed honest
   diagnostic; boot continues; §4's 404 discipline stays the refusal path — never a hard
   crash) and must earn its keep via falsifiability (tests that fail if resolution or the
   probe points at the wrong root).
3. **Anti-invented-work:** "Do not manufacture a product change to feel like you did
   something" — the guard is the minimal change because the contract's
   `fileScopeMustChange` names `Features/Dtrh/`; nothing larger is justified.
