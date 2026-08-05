# SP-049 record — Loom studio promotion (v6.6.3 behavior delta — drive the studio surface)

**Task:** spine-tasks/SP-049-loom-studio · **Review Level:** 2 · **Target:** the b4 named limit "Loom rack pane render not driven (pane + 3D gate; display proof = served URL in-engine)"
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

---

## Engine-review log (T-2)

| Step | Type | Result | Artifact |
|------|------|--------|----------|
| 1 | plan | **SKIPPED BY DESIGN** (nested reviewer spawn blocked in worker session; `skipped=true, spawnFailed=false` — engine runs reviews after `.DONE`, SP-195) | `.reviews/2-20260805T052612.md` (called at the Step-2 boundary, covering Steps 1-2) |
| 3 | plan | SKIPPED BY DESIGN (same) | `.reviews/3-20260805T074125.md` (called at the Step-3/4 boundary, covering Steps 3-4) |

---

## Step 4 — verification (final tree, post consult fixes)

- `node .spine/patches/verify.mjs` → **OK exit 0** (8 project + 5 engine patches applied).
- `dotnet build client/CcpClient.sln -c Debug -t:Rebuild` → **0W/0E** (warnings measured on Rebuild per the xUnit1051 lesson).
- CcpClient.Tests **629/629** (≥ the 614 floor; +15 this slice) with `--logger trx` (sp049-unit.trx); CcpClient.HeadlessTests **33/33** (≥ 33 floor) with TRX (sp049-headless.trx) — every failure would yield a name (skill-trx-failure-names amendment).
- `git diff --check` clean; `git status --short` = File Scope paths only (Dtrh/** sources+tests + the documented 2-file wiring amendment Program.cs / App.axaml.cs + the task folder).

---

## Step 1 — dual archaeology + delta list + drive design

### v6.6.3 payload archaeology (READ-ONLY; `ConditioningControlPanel/Resources/web/dtrh/`)

**The studio's homes and the promotion.** The studio module `game/loomStudio.js` (schema v2, 833 lines) mounts in TWO homes (loomStudio.js:14-27 header): (a) the Warren's Boudoir pane inside the game (crafting-gated behind `the_loom` + 3D station navigation — the b4 limit's gate), and (b) **the standalone main-app window `loom.html`** ("outside the rabbit hole… always available" — loom.html:2-5). v6.6.3 (main commits `f0c093f4` + `d64860d4` per the assets.manifest origin notes) promoted the studio OUT of the game gate into a main-app window hosted by WPF `LoomHostService` (Services/Chaos/LoomHostService.cs). **No three.js, no crafting gate** in the standalone home: "the loom is 2D canvas + one WebGL quad" (loom.html:6).

**What the studio page needs from the host** (loomBoot.js + loomStudio.js + bridge.js, all verified):

| Direction | Message | Shape / semantics |
|---|---|---|
| page→host | `ready` | `{type:'ready', protocol:1}` — host flushes its queued loom-list on receipt (loomBoot.js:22, bridge.js:44) |
| page→host | `loom-save` | `{name, overwrite, params, gifBase64}` (loomStudio.js:251-259) — the gifenc-encoded GIF rides base64 |
| page→host | `loom-delete` | `{slug}` (loomStudio.js:769) — two-click armed page-side |
| page→host | **`loom-reveal`** | `{slug}` (loomStudio.js:749) — the rack tile 📂 button. **NEW vs b4's vocabulary** — not in `DtrhProtocol.cs` |
| page→host | `sfx` | `{name, scale=0.45}` (loomBoot.js:11; save-success `boon_pick` 0.4 at loomStudio.js:209) |
| page→host | `log` | diagnostic (bridge.js:41) |
| host→page | `loom-list` | `{spirals:[{slug, url, params|null}]}` — at ready AND after each successful mutation (loomBoot.js:17-18); `url` renders as rack `<img>` (loomStudio.js:729-733) |
| host→page | `loom-result` | `{op:'save'|'delete', ok, slug, error}` — error vocabulary bad-name/too-big/exists/cap-reached/bad-gif (loomStudio.js:204-224; arms overwrite on `exists`) |

**The "3D gate" resolved.** b4's named limit named "pane + 3D gate" because the ONLY in-game home of the rack sits behind the Warren's 3D station navigation + the `the_loom` crafting unlock. The v6.6.3 standalone window **removes both gates**: no three.js importmap (loom.html:6), no unlock ("always available"). The **WebGL field renderer** (`shared/loomField.js` — WebGL field + 2D centerpiece composite, ONE pipeline shared by preview and encoder) still has an honest no-WebGL fallback (the v1 wedge renderer, loomStudio.js:8-10) — a page-side concern, never a host gate.

**The rack pane's composition** (loomStudio.js:714-786): a filmstrip of tiles — served-GIF thumbnail (`s.url`), slug, ✎ re-edit (loads `s.params` into the dials), 📂 reveal (emits `loom-reveal`), 🗑 two-click delete (emits `loom-delete`). Empty state note when the library is empty. Cap display `n/12`.

**The GIF export path (gifenc).** Fully page-side: SAVE → `engine/loomWorker.js` (module Worker at the ABSOLUTE path `/dtrh/engine/loomWorker.js`, loomStudio.js:155 — the page origin must serve it, already true via the §4 `/dtrh/*` route) → renders frames through the same `shared/loomField.js` pipeline → encodes with vendored `vendor/gifenc/gifenc.esm.js` (quantizer + ordered Bayer dither, loomWorker.js:5-22) → `{id, gif:ArrayBuffer}` → base64 → `loom-save`. Host authority is only the store (validate + write + serve back). **The serving contract proof IS the round trip:** the saved GIF's rack thumbnail renders from the media-origin `/spirals/loom_<slug>.gif` URL.

**Publishing status (SP-037/SP-048 — already landed, verified):** `payload/dtrh/loom.html`, `loomBoot.js`, `shared/loomField.js`, `game/loomStudio.js`, `engine/loomWorker.js`, `vendor/gifenc/gifenc.esm.js` are all in `assets.manifest.json` and copy to output via the linked glob; the loopback page origin already serves `/dtrh/*` overlay-first (LoopbackServer.cs:205-211) and the media origin serves `/spirals/*` with the full §4 discipline (LoopbackServer.cs:346-366). **Nothing in the serving layer needs to change.**

### b4 archaeology (the landed base — never re-ported)

`DtrhLoom` (client, 217 lines): the WPF DtrhLoomStore discipline — `loom_<slug>.gif` + `.json` sidecar in `<dataDir>/Spirals`, temp-then-move writes, slug whitelist `^[a-z0-9_-]{1,24}$`, 12-cap, 8MB ceilings, GIF87a/89a magic+trailer validation, error-code vocabulary verbatim, presence+shape-only logging (slug never logged). `DtrhHostWindow` handles `LoomSave`/`LoomDelete` (Handled, real dispatch, :1055-1095), posts loom-list at ready (WPF :209 order) + after mutations (:1097-1130). loom-list URLs = `{MediaOrigin}/spirals/loom_<slug>.gif`. **b4's named limit (record.md Surprise 0, verbatim target):** the rack PANE was never driven in-engine — the display proof was the probe page rendering the served URL, because the in-game pane sits behind the crafting + 3D gates.

### The delta list (what v6.6.3 adds ON TOP of b4; what is user-observable)

1. **A standalone, always-available Loom studio window** (user-observable: THE LOOM opens from the main app without entering the game or crafting anything). WPF host shape: `LoomHostService` — a STRIPPED sibling of the game host (one windowed web host on `loom.html`, own browser profile, plain-titled window closed by X — NO exit protocol, NO meta/slots/bark/watchdog; LoomHostService.cs:22-96).
2. **The `loom-reveal` bridge message** (user-observable: 📂 shows the saved GIF in the OS file manager). New typed protocol message; WPF handler = `explorer.exe /select,"<path>"` with the path from the slug-whitelisted store, never from the page (LoomHostService.cs:108-117 + DtrhLoomStore.GifPathFor :114-123).
3. **The v2 studio surface itself** (six weaves, presets, undo, hotkeys, fullscreen preview, format shapes, the filmstrip rack) — **entirely payload-self-driven**; the host's only new obligation is the window + the message subset above.
4. **gifenc GIF export** (user-observable: SAVE produces a real animated GIF in the Spirals library). Page-side encoder; host side already landed in b4 (store + serving). What remains unproven until this slice: the encoder actually runs IN the greenfield engine (module-worker + ESM + OffscreenCanvas-class APIs on the embedded WebView2) and the file round-trips to the rack.

**Explicitly NOT re-ported (dual-archaeology guard):** the store, the serving routes, loom-save/loom-delete handling, loom-list/loom-result builders, the probe-page display proof — all b4-landed and reused as-is.

### Drive design (pre-consult)

1. **`DtrhLoomWindow`** (new, `Features/Dtrh/`, slim sibling of `DtrhHostWindow` — the WPF LoomHostService shape): an Avalonia `Window` titled "The Loom" embedding `NativeWebView` on Windows (embedded capability) / `NativeWebDialog` on Linux (dialog capability), navigating to `_dtrh.PageUrl("loom.html")` (bridge token in the query, §3.3). Dispatch surface: `ready` → focus claim + PostLoomList ONLY (no init/manifest/meta — loomBoot.js consumes nothing else; unknown host→page messages would just preBuffer page-side, but WPF parity is the stripped subset); `log` → diagnostics; `loom-save`/`loom-delete` → the SAME `DtrhLoom` store + `BuildLoomResult`/loom-list (shared static helper so the game host and the loom window never drift); `loom-reveal` → OS reveal; `sfx` → the b3 native sfx path; unknown/forward-version/malformed → b2's typed tolerance. Plain X close (WPF: "the user closes it with X, not a page exit protocol" — LoomHostService.cs:78-83). Idempotent single-instance: refocus if already open (WPF `IsActive`/`FocusWeb` parity) — owned by the launcher.
2. **Protocol:** add `LoomReveal(string? Slug)` to `DtrhProtocol` (parser + `Classify` → Handled — the game pane's rack emits it too, shared loomStudio.js:749, so the game host window handles it as well). No version bump (v1 tolerance absorbs it: older pages never send it; a newer page sending it to THIS host parses typed).
3. **`DtrhLoom.GifPathFor(slug)`** (DtrhLoomStore.cs:114-123 parity: regex-validated slug → existing file path or null) + **`DtrhLoomReveal`** static (typed outcome; injectable opener so tests never spawn a shell): Windows `explorer.exe /select,"<path>"` (WPF verbatim); Linux `xdg-open <folder>` (WPF is Windows-only — the greenfield's honest Linux equivalent; Linux evidence stays the WSL zero-distros named limit). Refused/missing → typed log, never a crash, presence-only logging (never the slug/path in logs — path-class content).
4. **sfx in the loom window:** reuse `DtrhNativeEffects` with the SAME sfx roots as the game host (payload bubbles/sfx, overlay-first) but WITHOUT the LibVlc video backend if the ctor admits a null/disabled video (checked at implementation; if not, a minimal `IDtrhVideoBackend` no-op — the loom page never fires video/freeze/whisper; `boon_pick` + UI blips are the whole surface). VN-gate/pool discipline inherited, never duplicated.
5. **Open path (user reachability):** `--loom-demo` CLI demonstrator flag, mirroring the `--dtrh-demo` precedent (Program.cs + App.axaml.cs wiring amendment per the SP-023 norm — those files are outside File Scope's `Dtrh/**` but NOT in `fileScopeMustNotChange`; per-file necessity documented here + STATUS). **WPF parity note:** v6.6.3 reaches the studio from the Spiral Overlay feature card (`SpiralFeatureControl.BtnOpenLoom_Click` :422-431); the greenfield dashboard has NO Spiral Overlay card yet (that card is a future dashboard row — recorded, never invented here). The CLI demonstrator is the honest current seam, same class as every DTRH slice before it.
6. **Evidence plan (Step 3, headed Windows, `CCP_MCP=1`, avalonia-live):** launch `--loom-demo` with a HARNESS `--loom-drive` step that drives the REAL page through the engine's own `InvokeScript` (the SP-011 W14 focus-check precedent — script sets the name input + clicks SAVE; the gifenc worker, the loom-save message, the store, the serving, and the rack re-render are ALL real; only the pointer is scripted — WSLg no-input class, honestly labeled). Captures: studio opened + rendered (screenshot + semantic tree, dimension-validated per the windowId quirk rule — `target`/`handle` params, PNG dimensions checked BEFORE the pass), rack tile visible after save, save→list→delete round-trip with FILE-CONTENT proof (GIF on disk: magic + trailer + size; served 200 through `/spirals/*`), GIF validity through the serving contract (the rack `<img>` renders the served URL — pixel proof). sfx fires on save-success (`boon_pick`) through the real audio path (log line evidence). Delete through the real page (two-click 🗑 via the same scripted drive) → file gone.
7. **Tests (Step 2, unit + headless where honest):** loom-reveal parse round-trip + classification Handled; unknown/forward-version/malformed tolerance unchanged; `GifPathFor` (valid+exists → path; valid+missing → null; traversal-shaped/bad slug → null); `DtrhLoomReveal` typed outcomes (opener injected — records the invocation, never spawns a process); the shared loom-dispatch helper (save→result+list, delete→result+list, reveal, sfx routing) against a recording send/log. Floor: ≥614/33.

---

## Step 2 — studio driving + protocol + tests (committed; Windows build 0W/0E, 628/628 + 33/33 green ≥ 614/33 floor)

- **`DtrhProtocol.cs`:** `LoomReveal(string? Slug)` added — parser + `Classify` → Handled (22-type vocabulary; the shared loomStudio.js emits it from BOTH homes — consult binding 4). No version bump: v1 tolerance absorbs it.
- **`DtrhLoom.cs`:** `GifPathFor(slug)` (DtrhLoomStore.cs:114-123 parity) — the ONLY path source for reveal.
- **`DtrhLoomReveal.cs` (new):** typed outcome (Revealed/Refused/LaunchFailed), never throws; Windows `explorer.exe /select,"<path>"` (WPF verbatim), Linux `xdg-open <folder>` (no /select equivalent — recorded divergence); injectable OS seam (tests never spawn a process); presence-only logging (path-class content never logged).
- **`DtrhLoomDispatch.cs` (new):** the shared loom subset (save/delete/reveal in, result/list out) — ONE write path for both hosts (consult binding 1a). `DtrhHostWindow` refactored to delegate (its inline loom handling + PostLoomList replaced; LoomReveal arm added — the game pane's rack emits it too). `Describe` → internal `DescribeState` shared.
- **`DtrhLoomWindow.axaml(.cs)` (new):** the WPF LoomHostService sibling shape — plain titled window "The Loom", 1200x800 (the studio-split grid needs ≥980px — consult 5d), capability-driven surface (embedded NativeWebView / NativeWebDialog / honest unsupported — same discipline, no OS guess). Stripped boot: ready → focus claim + loom-list ONLY (LoomHostService.cs:66 OnReady parity — no init/manifest/meta). sfx via the b3 DtrhNativeEffects path with a `NullDtrhVideo` seam (the studio has no video surface; TryPlay refuses honestly). OWN WebView2 profile `wv2-profile-loom` (LoomHostService.cs:64 parity + the b5 stale-profile-lock class stays single-surface; the game host's profile dir untouched — `WebView2ProfileDir()` legacy path preserved). Plain X close — no exit protocol. Store folder created BEFORE navigate (LoomHostService.cs:37-39).
- **Open path:** `--loom-demo` demonstrator (+ HARNESS `--loom-drive` `save:<name>@t;delete-first@t;reveal-first@t` — ONE atomic InvokeScript per step, scripted pointer / real everything else, the SP-023 timed-drive labeling class; `--loom-auto-close` for no-input exit). **Wiring amendment (SP-023 norm, 2 files — per-file necessity):** `Program.cs` (flag parse + thread — the only CLI entry) + `App.axaml.cs` (the demonstrator block — the only lifetime UI seam). `fileScopeMustNotChange` untouched. **Typed named limit (recorded):** WPF parity reachability = the Spiral Overlay feature card (`SpiralFeatureControl.BtnOpenLoom_Click`); the greenfield dashboard has no such card yet (future dashboard row) — the CLI demonstrator is the current seam, never claimed as UI parity.
- **Tests (14 new):** `DtrhLoomStudioTests.cs` (loom-reveal parse/classify/tolerance, GifPathFor matrix, reveal outcomes via injected seam incl. OS-shaped args, dispatch save→result+list with file-content proof, bad-gif no-list, delete round-trip, reveal fire-and-forget, non-subset false, slug-never-logged) + the vocabulary theory row (21→22). **628/628 unit + 33/33 headless green; sln 0W/0E.**

### Budgets

Step 1 archaeology ≈ 45 min (payload files, WPF LoomHostService/DtrhLoomStore, b4 record + client Dtrh tree read directly). Step 2 implementation + tests ≈ 50 min (build clean first pass; 628/628 + 33/33).

---

## Step 3 — in-engine evidence (Windows headed, avalonia-live + harness) + consolidation

### Evidence index (`evidence/`)

| Artifact | What it proves |
|---|---|
| `loom-run*.log` (16 headed runs, 1-16 incl. 9b) | The full message-level story per run: ready → loom-list at ready (never before), per-tile `GET /spirals/loom_<slug>.gif -> 200 (spirals)` served lines, gifenc saves (`dtrh-loom: saved spiral (1072927 bytes)`), loom-result-driven list refreshes, deletes, sfx lines. **Exit honesty:** the auto-close runs (1, 2, 10, 13, 14, 15, 16) end in a clean graceful teardown with `EXIT=0`; runs 4-9b/11/12 were ended by taskkill or the shell timeout AFTER their evidence was captured (their transcripts end mid-session — recorded, never claimed as clean exits) |
| `run5-studio-4tiles.png` (OS screen capture, dimension-validated 1214x837) | The studio SURFACE painted in-engine: THE LOOM title, stage card, the live WebGL spiral preview mid-animation, name input `run4-weave`, SAVE button, and the loom-result status line "kept as run4-weave. the tube knows it now." |
| `run8-rack-stacked.png` (OS screen capture, stacked narrow layout) | The dial cards painted in-engine: the weave (arms/turns/body/style/spin), the threads (swatches/gradient/backing), the motion, the effects, the centerpiece; the preset chips with their WOVEN thumbnails (classic ccp / bambi haze / sissy swirl / locked in / hypno teal / candy tunnel — the shared field pipeline rendering in-engine) |
| `run14-rack-ready.png` (in-engine raster, 3 tiles) | **Leg (a), unscripted:** the rack mounts at ready from pre-existing spirals — aaa-seeded + bbb-seeded + run13-weave tiles. **What the raster is and isn't (pre-completion consult binding):** a HARNESS COMPOSITE, not an OS screenshot of the painted pane — but each tile's pixels are gated on `img.complete && img.naturalWidth > 0` read from the RACK'S OWN `<img>` element, so a drawn tile PROVES the rack's own thumbnail decoded in-engine (an undecoded tile draws nothing and is marked NOT-DECODED). Combined with the per-tile served-200 log lines and the real 🗑 click → real loom-delete round trips, the discharge claim is exactly: **the rack pane is DRIVEN — it mounts, builds rows from the real loom-list, decodes the served thumbnails in-engine, and its controls round-trip real messages** — never "the painted rack was screenshotted" (the residual named limit below) |
| `run14-rack-4tiles.png` (in-engine raster, 4 tiles) | **Leg (b):** the gifenc SAVE round trip lands on the rack — run14-weave's tile (the page's own encoder output, 1,072,927 bytes) beside the pre-existing three |
| `run14-rack-after-delete.png` (in-engine raster, 3 tiles) | **Leg (c):** the real loom-delete removes aaa-seeded's tile and the list refresh re-renders the rack |
| `run15-proof-weave.gif` + `.json` (file-content proof) | The gifenc artifact on disk: **640x640, 60 FRAMES (framesFor2 speed table), GIF89a magic + 0x3B trailer, 1,072,927 bytes** (System.Drawing frame count) + the params sidecar |
| `run15-semantic-tree.json` (avalonia-live, dimension-validated) | The Loom window node 1200x800 (= the axaml size — the correct window, not the 520x680 dashboard), NativeWebView interactive+focused 1200x774, status "loom: studio live (loom-list posted)" |
| `loom-run16.log` (headed reveal run) | **loom-reveal end-to-end:** the drive clicks the first tile's 📂 → the REAL loom-reveal through the real dispatch → `dtrh-loom: reveal launched` → a new explorer.exe observed with window title "Spirals - File Explorer" (the store folder, gif selected — WPF `explorer /select` parity) |
| `capture-loom.ps1` / `wheel-loom.ps1` / `list-children.ps1` | The capture tooling (SetWindowPos topmost + GetWindowRect before every capture, the SP-026 norm) |

**avalonia-live usage log (SP-036 binding — accept/reject + reasons per call):** `list_windows` ACCEPTED (window inventory + bounds; found The Loom 1200x800). `screenshot_window` **REJECTED for page-content evidence** (it renders the Avalonia visual tree only — the native WebView2 child is a black hole; the one exploratory call produced a 1200x800 black PNG with only the status bar, which is what EXPOSED the mismatch — kept honest, not used as evidence). `get_semantic_tree` ACCEPTED (dimension-validated shell + focused NativeWebView — run15-semantic-tree.json). OS-level CopyFromScreen captures (SP-026 norm) carried the painted-pixel evidence instead; the in-engine raster carried the rack.

**Scripted-pointer labeling (binding 3):** the SAVE/DELETE drives run ONE atomic InvokeScript per step through the engine's own script interface (the SP-011 W14 precedent) — the gifenc worker, the bridge messages, the store, the serving, and the rack re-render are ALL real; only the pointer is scripted. Leg (a) needed NO scripting at all. The `exists` refusal + overwrite-arm path was also observed live (run 9b: `dtrh-loom: save refused (exists — page arms overwrite)`).

### Surprises (evidence-pass ledger)

1. **The SP-023 ping-pong class recurred:** the demonstrator's `Closed → Shutdown()` re-fired Closed 31× (teardown loop). Fixed with the same one-shot Interlocked latch the dtrh-demo uses; run 2+ show exactly one shutdown.
2. **sfx `boon_pick` was unresolved** (silent no-op): the WPF chain is `chaos/boon_pick.mp3` → fallback `chime2.mp3` (ChaosSfx.cs:33); the dedicated drop lives in the WPF sound library, not the DTRH payload pool. Added the chain (page-supplied scale kept) — run 2+: `dtrh-fx: sfx 'boon_pick' playing (chime2.mp3)`. This fix serves the GAME host too (chaosRun/biomeMech send boon_pick).
3. **The laptop WebView2 raster-scale mismatch (environmental, never faked around):** the embedded child rasters at the CREATION monitor's scale (1.75) while the window sits on a 1.0-scale display — only the page's top-left ~1/1.75 ever paints on screen, and the rack (pinned at the page bottom in grid mode, last element in stacked) is NEVER in the painted band. Measured, not guessed: `innerWidth=1200 innerHeight=775 dpr=1.75` (drive `report`); the body is the stacked-mode scroller (`scroll-probe`: body 691/1630, all other candidates 0); a 937-CSS-px-wide window trips the page's own <980px stacked media query. style.zoom hacks break canvas compositing (run 7). **Mitigation: the in-engine raster harness** (`shot-rack`/`shot-fetch` drive steps) — the REAL engine re-fetches each tile's served URL CORS-clean (the §4 route's own `Access-Control-Allow-Origin: page origin`), decodes via createImageBitmap, and composites the rack to a PNG staged under the overlay (HARNESS-ONLY, honestly labeled — never claimed as an OS screenshot).
4. **WebView2 ExecuteScriptAsync does NOT await returned promises** (run 13: the async IIFE came back `{}`) — async page results need the two-step arm (`shot-rack` sets `window.__loomShot`) / fetch (`shot-fetch` returns it synchronously).
5. **The encoder is deterministic:** identical default params produced byte-identical 1,072,927-byte GIFs across 8 independent saves (runs 1-15) — the cross-run byte count in the logs is the sameness proof.
6. **Background shells in this environment block the tool call until process exit or the timeout** (detach happens AT the timeout, process survives) — evidence runs must be scheduled around it (run 3's 900s block recorded honestly).
7. The stderr transcript renders `—` as `�` (console codepage, the SP-026 culture class — cosmetic, display-only).

### Linux

**Named limit (owner-gated, never faked):** WSL has zero distros on this laptop — no Linux evidence. The dialog surface + `xdg-open` reveal path are code-reviewed but unproven; no Wayland claims.

### Residual named limits (for the board row at land)

1. **The painted rack was never OS-screenshotted on THIS laptop** — the WebView2 child rasters at the creation monitor's 1.75 scale while the window displays at 1.0, so the page's bottom-pinned rack never paints on screen here (measured: dpr=1.75, viewport 1200x775; surprise 3). The discharge above is the DRIVEN rack (mount/list/decode/message round trips + harness composite). A single-monitor or matched-scale machine can take the plain screenshot with zero code changes.
2. **WPF parity reachability = the Spiral Overlay feature card** (`SpiralFeatureControl.BtnOpenLoom_Click`); the greenfield dashboard has no such card yet (future dashboard row). The `--loom-demo` demonstrator is the current seam, never claimed as UI parity.
3. **Landed-slice behavior change to call out on the board:** the `boon_pick` sfx chain (DtrhNativeEffects) now falls back to chime2.mp3 per ChaosSfx.cs:33 (was: typed silent no-op) — changes GAME-host behavior from b3 as well; pinned by `Sfx_BoonPick_ChainFallsBackToChime2_KeepingPageScale`.

### Consults

#### Pre-completion consult (Step 3)

**Mode:** solo (T-7). **Requested route:** Opus 5 main (2026-08-04 rewire). **Actual answering model:** NOT surfaced by the consult tool response (recorded honestly, same discipline as the pre-approach).

**Verdict: the chain is closeable after four fix-first items — ALL CLOSED:**
1. **Raster honesty reframed (CLOSED):** the raster is a harness composite, not a painted-pane screenshot — but the `img.complete && img.naturalWidth > 0` guard on the RACK'S OWN `<img>` makes each drawn tile proof the rack's own thumbnail decoded in-engine. The discharge sentence is scoped to exactly "the rack pane is DRIVEN" (mount/list/decode/round-trips); the painted-rack screenshot is a residual named limit (above). The transient per-tile decode-info claim was corrected (the guard logic IS the persisted proof).
2. **loom-reveal was never fired in a real run (CLOSED):** run 16 drove `reveal-first` — the real page click → real message → real dispatch → `reveal launched` → explorer.exe observed titled "Spirals - File Explorer".
3. **boon_pick fix needed a test (CLOSED):** `Sfx_BoonPick_ChainFallsBackToChime2_KeepingPageScale` pins the chain + the page-supplied scale (18/18 DtrhNativeEffects tests green). Recorded as a landed-slice behavior change for the board (residual 3).
4. **Overclaims corrected (CLOSED):** the "15 runs all EXIT=0" line replaced with the honest split (7 auto-close clean exits; the rest taskkilled/timeout after capture); the avalonia-live accept/reject usage log added per the SP-036 binding.

#### Pre-approach consult (Step 1)

**Mode:** solo (T-7: council unproven on this laptop). **Requested route:** Opus 5 main (2026-08-04 rewire). **Actual answering model:** NOT surfaced by the consult tool response (no model identity header — recorded honestly, same provenance discipline as SP-022…026).

**Verdict: the design stands; five bindings folded in.**
1. **DtrhLoomWindow (new window) APPROVED** over a loom-mode branch — DtrhHostWindow's ctor binds slots/meta/bark/watchdog, all wrong for the studio. TWO traps: (a) don't copy-paste the embedding/SendToPage/environment code blindly — share where clean; (b) **own WebView2 profile parity** — WPF uses `browser_data_loom` so the game's WebView2 state stays untouched (LoomHostService.cs:64); give the loom window its own loom-suffixed profile dir (the b5 stale-profile-lock class came from exactly this seam) — check DtrhProfileLock for a name seam or add one.
2. **--loom-demo APPROVED** — the dashboard Spiral Overlay card is a future row; inventing it would creep Views/ outside the Dtrh/** scope. MUST be recorded as a typed named limit (WPF parity reachability = the SpiralFeatureControl card; greenfield seam = the demonstrator flag), never claimed as UI parity. Wiring amendment (Program.cs + App.axaml.cs) documented per the SP-023 norm.
3. **InvokeScript page drive APPROVED — structured as TWO legs:** (a) **the rack pane renders in-engine at ready with a pre-existing saved spiral — NO scripting at all** (seed the store first, open the window, loom-list at ready, rack tile + served thumbnail render — this is the b4 limit's letter, unimpeachable); (b) the gifenc SAVE round trip via the scripted pointer (honestly labeled "scripted pointer, real everything else" — the SP-023 picker-timeout labeling class). Traps: the studio REBUILDS its DOM on every redraw (loomStudio.js render()) — one ATOMIC script that sets the name input, dispatches an `input` event (plain `.value=` never fires the handler), and clicks SAVE (element refs die across redraws); SAVE is disabled while encoding — poll for the result. Verify the module-worker fetch (`/dtrh/engine/loomWorker.js`, absolute, token-less) passes the loopback token discipline (subresources carry no `?bridge=` — confirm the token gates only the inbox, else the game's own subresources would already fail).
4. **loom-reveal scope = BOTH windows** — WPF handles it in the game host too (DtrhHostService.cs:336), and the shared loomStudio.js emits it from the game pane's rack. Strict discipline: the path comes ONLY from GifPathFor (slug whitelist), never from page strings; typed failure logging; never log the path (path-class content); Linux = `xdg-open <folder>` (no /select equivalent — recorded divergence; Linux evidence stays the WSL named limit).
5. **Delta-list corrections (all adopted):** (a) the loom page's fullscreen uses the DOM Fullscreen API INSIDE the webview with a page-side fixed-overlay fallback — NO `fullscreen-set` message, no host obligation (removed from the host-driven list); (b) sfx scale default mismatch (loomBoot 0.45 vs parser 0.6) is harmless — the page always sends scale explicitly; (c) the pre-ready queue discipline: loom-list posts ONLY at/after `ready` (design already does this); (d) **window size is evidence-relevant** — below 980px wide the page stacks to one column (loom.html media query); the evidence window must be ≥ ~1000x700 to show the studio-split grid + rack filmstrip; (e) check DtrhNativeEffects' ctor for a null-video seam rather than dragging LibVlc into the loom window.
