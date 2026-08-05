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
| Byte-identity (sha256, all files) | payload 1542/1542 + overlay 2/2 match the IN-REPO source tree at HEAD (`ConditioningControlPanel/Resources/web/dtrh`) — zero transformation in the publish step |
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
3. **Byte-verifiability:** beside-exe bytes are DIRECTLY hashable against the source —
   proven end-to-end today: 1542/1542 sha256 match the IN-REPO source tree at HEAD
   (the anchor tree `40be29df` covers the 1536 pre-v6.6.3 files; the v6.6.3 delta rides
   SP-037's per-entry commit provenance — the claim is faithful-copy-from-source-at-HEAD,
   never identity-to-anchor). No repackaging/transformation exists between source and
   served bytes.
4. **Runtime read-only:** GET-only server, traversal-refused, never writes to the payload
   root (§4 contract unchanged).

### Engine-review presence (T-2)

| Call | Step / type | Result |
|---|---|---|
| 1 | Step 1 / plan | **SKIPPED by runtime** (SP-195: nested reviewer spawn blocked inside pi worker session; `skipped:true, spawnFailed:false`; artifact `.reviews/1-20260805T032127.md`) — engine runs reviews post-worker |
| 2 | Step 2 / plan | **SKIPPED by runtime** (same SP-195 shape; artifact `.reviews/2-*.md`) |

## Step 2: implement the decided shape

**Product change (the minimal change the consult endorsed — non-fatal guard that earns
its keep via falsifiability):** `DtrhParticipant.cs` —
`ProbePayloadRoot(string)` (typed `DtrhPayloadState` Present/Missing/Incomplete +
recursive file count) + one startup diagnostic in `StartAsync` naming the RESOLVED root
and state: `dtrh: payload root '<full path>' -> <State> (<n> files)`. Non-fatal by
construction: the participant starts regardless; a missing/incomplete payload is refused
by the §4 404 discipline, never a crash, never a silent substitute. The log line makes
every boot transcript self-evidencing about WHERE the payload is served from (the
consult's falsifiability correction).

**Publish wiring: UNCHANGED** (decision ratified the existing glob — zero csproj/script
delta, so zero per-change justification owed).

**Resolution per mode (empirical):**
- Debug: `bin/Debug/net10.0/payload/dtrh` — 1542 payload + 2 overlay files after the
  contract Debug build (CopyToOutputDirectory arm of the glob).
- Release: `bin/Release/net10.0/` — EMPIRICAL: matrix GATE2 release PASS (`--verify-assets`
  exit 0 on the Release binary — copied entries present beside it, sweep clean).
- Published: beside the exe via CopyToPublishDirectory — set-identical, byte-identical
  to the in-repo source tree at HEAD, `--verify-assets` exit 0 (Step 1 transcripts).

**Tests:** `DtrhPayloadRootTests.cs` (5): probe Missing/Incomplete/Present shapes with
temp trees; the one-code-path root shape (`PayloadRoot`/`OverlayRoot`/`MediaRoot` vs
`AppContext.BaseDirectory`); participant start logs the resolved root + typed state and
stays Running (non-fatal). 5/5 green.

**Suite state:** unit 606/606 (floor 601 → +5 new), headless 33/33, build 0W/0E.
Honesty note: the FIRST full unit run after the change reported 1 transient failure
(test name not captured — tail-truncated output); unreproduced across 3 subsequent full
runs (606/606 each). Watched; if it recurs it gets named and fixed.

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

## Step 3: published boot proof + evidence + pre-completion consult

### 3.1 Published win-x64 boots the DTRH host from the decided location (falsifiable)

The published artifact (re-published AFTER the Step-2 guard, clean output dir) was copied
to a MOVED location (`%TEMP%\ccp-sp048-portable` — the consult's falsifiability
correction: proves the server does NOT read the repo tree) and booted:

`CcpClient.Desktop.exe --dtrh-demo --dtrh-quick --dtrh-auto-close 30` → **exit 0**
(transcript: `published-boot.log`, 165 lines). Needles:

- **Line 1 (the guard, self-evidencing):** `dtrh: payload root
  'C:\Users\Micha\AppData\Local\Temp\ccp-sp048-portable\payload\dtrh' -> Present (1542 files)`
  — the served root is the MOVED publish copy, named in the transcript. Repo-tree serving
  is disproven by the path itself.
- `dtrh-loopback: page+media origins bound on 127.0.0.1 (ephemeral)` — §4 origins live.
- WebView2 embedded surface AdapterCreated (Blink 151.0.4129.59) — the real engine.
- `dtrh: ready received — flushing init+manifest` — **engine live** on the published binary.
- **105 payload GETs → 200 `(page:payload)` + 1 `(page:overlay)`** (bridge.js shadow —
  overlay-first provenance visible in the route-class logs). §4 serving exercised on the
  published binary: GET-only 200s + overlay-first + route 404 discipline (favicon).
  Range/MIME-415/traversal/CORS arms are covered by `DtrhLoopbackContractTests` (green)
  — honestly NOT re-proven against the published binary.
- `dtrh: exit-done received — closing (graceful fast path)` + process exit 0.
- Noise honestly named: favicon.ico 404 (route discipline), the post-exit WebView2
  `Chrome_WidgetWin_0` unregister stderr line (runtime teardown noise after exit-done,
  not a product failure).

Scope honesty: boot evidence via the existing `--dtrh-demo` boot-matrix harness (headed
laptop run) — the same evidence class SP-023's Windows half used, now against the
PUBLISHED single-file binary from a moved directory. Linux publish = named limit
(WSL zero distros — owner-gated, never faked).

### 3.2 `--verify-assets` on the published artifact in the decided shape

Run against the MOVED publish copy: `asset OK copied: 1544 entries present, case-exact,
sweep clean` + `verify-assets: PASS (1551 manifest entries)` — **exit 0**.

### 3.3 SP-010 publish gates — non-regression (full matrix)

`pwsh client/tools/publish/matrix.ps1 -Mode all` → **MATRIX PASS (windows), exit 0**
(transcript: `matrix-transcript.txt`, 18 PASS lines, zero FAIL): gates 2 (--verify-assets),
3 (--version authority), 4 (fresh-profile), 5 (corrupt-settings quarantine, original
bytes preserved), 6 (data-path identity across modes, published from MOVED dir), 7
(logs-absence), 8 (native-deps floor) — debug + release + published all green.

### 3.4 Pre-completion solo consult

**Route:** `consult` mode solo (per the 2026-08-04 rewire; council forbidden by the packet).
Verdict received (FULL, untruncated). **Actual answering model: unknown to the worker**
(the consult tool does not surface the model name; recorded honestly, never invented).
**Sufficient to discharge WITH the following corrections — all applied:**

1. **FIX-FIRST — sweep-class check (SP-023's 3rd-engine-incident class):** verified
   `git check-ignore`: `client/artifacts/publish/` (899MB) and the TestResults trx are
   BOTH gitignored (.gitignore:94, :89) — the engine auto-commit at .DONE cannot sweep
   them. Clean.
2. **Flake hunt:** 2 further full unit runs post-consult → 606/606 each (flake now
   unreproduced across 5 consecutive full runs). Hypothesis recorded honestly per the
   consult: the failure appeared on the first run WITH the new tests, so the suspect
   class is the new `ParticipantStart_*` test's real loopback bind under xunit parallel
   collection — mitigated by construction (finally-StopAsync, ephemeral-port retry loop
   in LoopbackServer). If it ever recurs it gets named and fixed.
3. **§4 claim narrowed (overclaim caught):** the published boot exercises GET-only 200s,
   overlay-first provenance, and route-404 discipline; Range/MIME-415/traversal/CORS are
   covered by the green `DtrhLoopbackContractTests`, NOT re-proven on the published
   binary — record wording corrected (§3.1).
4. **Byte-identity framing corrected:** the hash proof is faithful-copy-from-IN-REPO-
   SOURCE-AT-HEAD (1542 files); the anchor `40be29df` covers the 1536 pre-v6.6.3 files
   and the v6.6.3 delta rides SP-037's per-entry commit provenance — never
   "byte-identical to the anchor" (§1.1, §1.4 corrected).
5. **Release resolution now empirical:** matrix GATE2 release PASS cited (was
   mechanism-identical phrasing).
6. **SP-018 sensitive-logging disposition:** no code-level log-site registry tripwire
   exists (grep-verified); the discipline is per-site tests (token ban etc., all green).
   The new log line records an install PATH — an allowed class (route-class logs and the
   matrix already print paths; never token/settings/media contents).
7. **Cleanup:** `%TEMP%\ccp-sp048-portable` scratch copy removed; `client/artifacts/` is
   gitignored working output (left in place, standard).

### 3.5 Discharge statement + enabler 2

**The b1 land condition "published-artifact payload location UNDECIDED" is DISCHARGED:**
the location is DECIDED (beside the exe via the existing linked glob — ratified with
empirical evidence, alternatives measured and rejected), PROVEN on the real published
win-x64 artifact (engine live from a MOVED directory, served root named in the
transcript, `--verify-assets` exit 0, full SP-010 matrix PASS). Per enabler 2 the worker
does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md` —
`git status --short` shows neither touched; the orchestrator reconciles the DTRH host
row at land. Linux publish evidence = named limit (WSL zero distros — owner-gated,
never faked). No Wayland claims.

### 3.6 Budgets + durable-lesson candidates

- **Budget:** 4h exported at launch; consumed ~35 min wall (inventory → decision →
  guard → tests → publish/matrix/boot evidence → 2 consults). Well inside.
- **Lesson candidate 1 (tool-quirk):** `PublishSingleFile=true` does NOT bundle
  `Content` items — `CopyToPublishDirectory` content lands beside the exe and
  `AppContext.BaseDirectory` resolves to the exe dir, so beside-exe payload roots are
  single-file-safe by construction (now empirically pinned for this repo).
- **Lesson candidate 2 (insight):** an evidence set whose artifact sits at a stable
  repo-adjacent path can be consistent with the WRONG serving root — boot from a MOVED
  copy and have the product NAME its resolved root at startup (the guard pattern:
  non-fatal typed probe + one diagnostic line) so transcripts are self-evidencing.
- **Lesson candidate 3 (convention):** "byte-identical" claims must name the exact
  comparand (in-repo source at HEAD vs git-tree anchor) — anchor coverage and live-tree
  coverage diverge after a content delta lands (SP-037 class).

## Step 4: contract verification

- `node .spine/patches/verify.mjs` → OK (all patches applied on all roots), exit 0.
- `dotnet build client/CcpClient.sln -c Debug` → 0W/0E; warnings measured on
  `-t:Rebuild` → 0W/0E.
- Unit `CcpClient.Tests` → **606/606** (floor 601; +5 new `DtrhPayloadRootTests`).
  Headless `CcpClient.HeadlessTests` → **33/33** (floor 33). Contract exit 0.
- `git diff --check` clean; branch delta touches only File Scope paths
  (DtrhParticipant.cs, DtrhPayloadRootTests.cs, spine-tasks/SP-048-*). Enabler 2
  honored: task-board.md / port-lessons.md untouched.
- Engine-review presence (T-2): all three in-worker plan-review calls (steps 1/2/3)
  SKIPPED by runtime per SP-195 (`skipped:true, spawnFailed:false`; artifacts under
  `.reviews/`). Code/final reviews run on the engine after .DONE.
