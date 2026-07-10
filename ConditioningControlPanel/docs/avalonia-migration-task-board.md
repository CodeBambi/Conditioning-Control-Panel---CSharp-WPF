# Avalonia Migration Task Board

> **This is the ONE live work tracker for the WPF → Avalonia v12 port.** It serves the spirit and
> acceptance gate of [`docs/skia-rebuild-goal.md`](skia-rebuild-goal.md): every WPF feature must work
> end-to-end in the Avalonia heads on Windows AND Linux; a ported feature is accepted only when it is
> at least as fast and smooth as the WPF head — preferably measurably improved. Detail docs are
> referenced from a row, never duplicated. Nothing is currently in flight (post-crash reconciliation
> 2026-07-09); any "claimed / WIP / co-agent / do-not-touch" note found elsewhere is historical debris.

Read order for a fresh session: [`docs-index.md`](docs-index.md) → [`skia-rebuild-goal.md`](skia-rebuild-goal.md)
→ **claim ONE row below** → that row's detail doc. Driven by the pi-dynamic-workflows `workflow` tool
(agent()/parallel()/pipeline()/phase(), journaled resume, git-worktree isolation, verify()/judgePanel()).

---

## How to use this board

**Claim discipline (non-negotiable).** Append-only claim ledger at the foot of your chosen row — ONE row
per session, one task per commit, tracker row updated in the same session, never leave a red tree. If a
precondition fails or a step is ambiguous and you are on the MECHANICAL tier, STOP with a `BLOCKED:` note
instead of improvising. Re-run gates live before claiming them; evidence (hashes, dates, counts) is copied
from trackers, never invented.

**Tier tags — route every row to the right model** (Avalonia v12 is 2026-new; skills are mandatory, not
optional — `avalonia-research` before any v12 API/dependency, `port-plan` at claim, `wpf-parity` for
behavior contracts, `port-feature` for implementation, `mechanical-port-work` for small-tier discipline,
`unified-compositor-engine` for all layer/video work, `overlay-clickthrough` for all input/ex-style/hook
work, `dashboard-design` for all user-facing surfaces, `port-audit` at workstream close-out):

- **MECHANICAL** — `kimi-for-coding`: literal, list-driven execution of pre-sliced turnkey
  edits WITH WPF file:line citations, deletions, sweeps, tracker updates. Rows must be turnkey (steps
  pre-sliced + cites + gates). Dumb but fast; STOPS with `BLOCKED:` on any failed precondition.
- **STANDARD** — `zai/glm-5.2`: bounded implementation, research digestion, reference reconciliation,
  routine reviews, inventories.
- **JUDGMENT** — `anthropic/claude-fable-5`: architecture, slicing, adversarial review; anything touching
  state, economy, security, input hooks, or compositor internals.
- **VERIFY** — human-eyes / headed verification of already-implemented behavior.
- **BLOCKED** — cannot start until a named precondition lands.
- **DEFER** — deliberately scheduled for later; each carries a pointer.

Driver session model: `anthropic/claude-opus-4-8` (orchestration only — claims, dispatch, gates, commits,
tracker updates; never burn the JUDGMENT model on the long-lived driver context). Routing caveats:
`zai/glm-5.2` has NO vision — screenshot/visual verification (`--smoke-screenshots`, dashboard-design
5-theme checks, img-state comparisons) never routes to STANDARD; send it to JUDGMENT or the driver
(`zai/glm-5v-turbo` is an acceptable cheap vision fallback). `kimi-for-coding` has a 256k window —
MECHANICAL prompts stay self-contained (slice spec + cites only, never whole docs or the 100KB+ WPF files).

**Workflow & token economy (cost rules — pi-dynamic-workflows v2.12+):**
- **Escalation ladder, never start-big:** route every unit to the cheapest tier that can clear the bar and
  exit at the first tier that does; escalate only on failure, low confidence, or a high-stakes trigger
  (state/economy/security/input-hook/compositor goes to JUDGMENT directly). Target shape: ~80% of units on
  MECHANICAL/STANDARD; JUDGMENT reserved for slicing, adversarial review, and final synthesis only.
- **Resume, never re-run:** runs are journaled — a resumed run replays finished agents at zero token cost.
  Diagnose a null/empty agent via `/workflows` drill-down (error code + compact history, e.g.
  `AGENT_EMPTY_OUTPUT`) BEFORE dispatching any fresh run.
- **Budget-gate expensive phases:** `phase('Name', {budget: N})` on JUDGMENT-heavy phases (wrap in
  try/catch so later phases proceed); branch on `budget.remaining()` to skip optional review rounds;
  bounded `retry()`/`gate()` only — no unbounded loops.
- **Intermediate results stay in workflow variables**, never in the driver chat — the driver receives one
  synthesized result per row. Recurring orchestrations (e.g. the gate suite) get `/workflows save <name>`
  instead of being regenerated each session.
- **`/ultracode` stays OFF for port sessions** — the standing exhaustive fan-out fights the one-row loop;
  dispatch one targeted workflow per row instead. `verify()`/`judgePanel()` only on high-stakes outputs;
  the commit gates catch mechanical errors for free.

Project agentTypes available inside workflows: `wpf-archaeologist` (read-only WPF behavior contracts with
File.cs:line cites — nobody opens the 100KB+ WPF files raw), `port-slice-executor` (one pre-planned slice
under the iron rules), `port-parity-auditor` (adversarial working-tree diff vs WPF ground truth before
commit — mandatory for state/economy/lifecycle diffs).

**Gates before every commit (all must pass):** slnf build 0 errors; WPF sln build 0 errors; Core tests all
pass and the count NEVER decreases (floor **542** — re-run LIVE 2026-07-10: Core **542/542** Release 0
failed · slnf **0 errors**/384 warnings · WPF sln **0 errors** — read the live count); `--smoke-test`
→ 44 tabs + 0 unhandled + findings ⊆ the recorded benign drift set (smoke-drift row below; logged-out
baseline = Findings 5, count-equality NOT the signal while the env is authed — owner-waved 2026-07-10);
`--verify-layers`/`--verify-video` when touching the compositor/video;
`--benchmark` before/after on hot paths — not worse than [`docs/benchmark-optimized.json`](benchmark-optimized.json)
(re-baseline caveat: open row #2). Re-verify live before signing off.

---

## OPEN rows

### #1 — Per-region UCE input mask + `AvaloniaMouseHook` click-swallow · **JUDGMENT (HUMAN+SMART)**

Team review 2026-07-09 (deliberate divergence from WPF, recorded as a product decision): only the
theme-color-filter and spiral regions are ambient "tinted glass" the user works through; **every other
active layer** (video, flash, subliminal, brain-drain, bouncing text, keyword highlight, bubbles, all
chaos FX) **captures input over its painted region**. The global hook MUST swallow clicks inside the
per-frame capture mask (including the WPF hold-to-defuse no-swallow exception). This supersedes the old
"swallow: fix or accept" question — fixing the hook is now REQUIRED scope, not optional.

- **Detail spec:** [`unified-compositor-engine-plan.md`](unified-compositor-engine-plan.md) per-region mask
  section + `overlay-clickthrough` / `unified-compositor-engine` skills +
  [`crossplatform-rebuild-plan.md`](crossplatform-rebuild-plan.md) §7.4.
- **Mechanism:** each non-ambient layer exposes its active painted region; the engine unions them per frame
  into an immutable per-monitor capture-mask snapshot (hook-thread-safe per `overlay-clickthrough` rules);
  `AvaloniaMouseHook` gains a WPF-style swallow path (swallow inside the mask, pass outside).
  `CompositorWindow` stays `WS_EX_TRANSPARENT|LAYERED`. **Status check 2026-07-10:** none of this is
  implemented yet — the compositor window is still globally `WS_EX_TRANSPARENT` (all-or-nothing
  click-through, `CompositorWindow.axaml.cs:107`); the hook explicitly passes clicks
  (`AvaloniaMouseHook.cs:156-159`). **Sub-goal (folded 2026-07-10, improvement scan I-6):** the same
  per-layer painted-region data should also drive per-MONITOR dirty invalidation — today one dirty
  layer invalidates every monitor's window (`CompositorEngine.OnFrameTick` global `anyDirty`);
  intersecting painted regions with `screen.Bounds` skips untouched monitors (up to ~50% GPU on
  dual-monitor rigs with localized effects).
- **Open questions to resolve before/while implementing:** (a) does per-region capture apply mid-chaos-run,
  where the whole screen is already a game surface (likely the whole run captures — confirm)? (b) the
  `WH_MOUSE_LL` hook swallows POINTER only — is pointer-blocking sufficient for "blocks the desktop", or
  must keyboard also be gated over capturing regions? (c) keyword-highlight would capture over the user's
  OWN text they are reading — intended, or should highlight be treated as ambient/pass-through?
- **Acceptance:** behavior parity with the 2026-07-09 team decision AND the perf gate (idle/active FPS not
  worse than baseline; no new per-effect windows; human visual-verify of the mask edges).

### #2 — FPS re-baseline @240s + `MinFps=0` LibVLC decode-stall investigation · **JUDGMENT**

The 2026-07-05 chaos FPS-floor benchmark (Release `--max-benchmark`) held the floor: AvgFps 138.7 ≫ 30
floor across a full run incl. a 60s Chaos phase. BUT `MinFps=0` is a ≥1s render stall correlated with
LibVLC web-video decode failures — a **video-path stall, NOT a Skia/UCE regression**. The 180s→240s
duration drift and the Phase-2 web-video decode-retry loop (≈4× CPU, half the run failed to decode)
environmentally invalidate the "not-worse than benchmark-optimized.json" comparison on that machine.

- **Evidence:** [`benchmark-2026-07-05-analysis.md`](benchmark-2026-07-05-analysis.md) +
  [`benchmark-optimized.json`](benchmark-optimized.json).
- **Work:** re-baseline cleanly at 240s; investigate the video-failure→render-stall correlation.
- **Acceptance:** a clean benchmark where `MinFps` reflects real UCE frame cost, not LibVLC decode
  starvation; the "not-worse than benchmark-optimized.json" gate becomes re-comparable.
- **Evidence gap (2026-07-10):** the primary 2026-07-05 run log has been overwritten — `ccp-run.log`
  (mtime 2026-07-09) has **0** matches for `Failed to create video converter`/`mjpeg demux`; the
  decode-stall interpretation now stands on `benchmark-2026-07-05-analysis.md` +
  `benchmark-report-2026-07-05.json` (`MaxIntensityMinFps: 0`) only. Re-capture a fresh log during
  the re-baseline.
- **Fresh hypothesis (improvement scan 2026-07-10) — CONFIRMED + FIXED (IMP-2, this session):** the engine
  tick was a parameterless `DispatcherTimer` = **Background** dispatcher priority (VERIFIED against
  Avalonia 12.0.5 `Avalonia.Base.xml`: `DispatcherTimer.#ctor` “process the timer event at background
  priority”) — starvable under UI-thread load. Changed to `DispatcherPriority.Render`; a clean before/after
  `--benchmark` measured **Idle 122.6→141.6 fps (min 118→136), Active 123.2→144.2 fps (min 117→129)** —
  ~+15–17% avg AND higher minimums, i.e. the scheduling-starvation component of the render gap is real and
  now removed. The LibVLC decode-retry component of MinFps=0 is SEPARATE and still owned by this row's
  re-baseline (the two were always additive, not exclusive).
- **MinFps=0 ROOT-CAUSED (this session) — benchmark-input defect, not a UCE/product bug:** the MaxIntensity
  video-stress segments all feed `video.PlayUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ")`
  (`BenchmarkContext.cs:387,411,445`) — a YouTube **watch-page** URL. `PlayUrl`→`PlayUrlCore`→
  `VideoLayer.PlayVideo` hands the URL straight to LibVLC (WPF-parity contract, `AvaloniaVideoService.cs:1080-1103`;
  WPF `VideoService.cs:900-903`), which cannot stream-extract a YouTube page → a decode-retry loop (≈4× CPU)
  → the ≥1s render stalls behind `MaxIntensityMinFps: 0`. This is a benchmark HARNESS artifact; production
  `PlayUrl` callers (`AvaloniaAutonomyService.cs:682`, hypnotube remote `AvaloniaRemoteCommandExecutor.cs:270`,
  `_lastBrowserUrl` `MainWindowViewModel.cs:786`) pass DIRECT-STREAM URLs LibVLC decodes fine. Prescribed fix:
  swap the three `youtube.com/watch` URLs for a directly-decodable source so MinFps reflects real render cost
  — bundled into the SIGSEGV row below (unverifiable in isolation until the harness can run).
- **Clean 240s re-baseline BLOCKED (this session):** Release `--max-benchmark` reproducibly **SIGSEGVs at
  window-show** (right after `MandatoryVideoLayer` registration, before `[BENCH] MainWindow shown`) — 2/2 on
  `feat/crossplatform` @ `00ea03ad` AND 1/1 at the pre-code baseline `c9475bdd`, so it is **PRE-EXISTING**, not
  a session regression (IMP-2/BubbleLayer/economy all exonerated by the baseline repro). The Debug `--benchmark`
  Idle/Active path runs clean (that is where the IMP-2 141.6/144.2 numbers came from); only the Release
  max-intensity full-session window-show path crashes. A fresh MaxIntensity re-baseline cannot be captured on
  this machine until that crash is fixed — see the new row directly below.

### Release Windows head SIGSEGV at window-show (flag-independent) · **JUDGMENT (native crash) — BLOCKS row #2 re-baseline + all Release verification** (filed 2026-07-10)

The **Release** build of the Windows Avalonia head crashes with SIGSEGV (exit 139) during **window-show**,
immediately after the compositor layers register (`VideoLayer`/`MandatoryVideoLayer`), **before** any
`[BENCH]` / smoke tab-visit marker. No managed exception is logged (global handlers don't fire → native
fault). **Flag-independent:** reproduces with BOTH `--max-benchmark` (2/2) AND `--smoke-test` (1/1) — it is
NOT a benchmark artifact but a generic Release launch/window-show crash. **Pre-existing:** also reproduces at
the pre-code baseline `c9475bdd` (1/1), so not a session regression (candidate origin: the main merge
`a06509eb` or earlier). **Debug is fully UNAFFECTED** — every Debug gate passed this session (`--smoke-test`
44 tabs, `--verify-layers`, `--benchmark` Idle/Active). So this is a **Release-only** (optimized/JIT or
native-interop-timing) fault, observed on THIS machine (run via `dotnet run -c Release --no-build`).
- **SEVERITY UNKNOWN — needs owner/CI input:** is this reproducible in CI's Release single-file publish and
  in the shipped installer build (→ shipping-critical, users can't launch), or is it machine/GPU-driver/
  LibVLC-native-state specific to this dev box? A published-exe run + a CI Release artifact check should settle
  it before deep forensics.
- **Prescribed first step:** capture the faulting module — set `DOTNET_DbgEnableMiniDump=1` (+ `DbgMiniDumpType=4`)
  and re-run, then triage the dump with `dotnet-dump`; prime suspects are LibVLC video-layer native init and the
  Skia/GPU context at first show under the heavy session. Do NOT guess a fix without the module.
- **Then (bundled from row #2):** replace the three un-decodable `youtube.com/watch` URLs in `BenchmarkContext.cs`
  with a directly-decodable source, run a clean 240s `--max-benchmark`, and confirm `MaxIntensityMinFps` > 0
  reflecting real UCE frame cost — which re-arms row #2's "not-worse than `benchmark-optimized.json`" gate.
- **Tier:** JUDGMENT (native-crash forensics + measurement-methodology decision); not a mechanical row.

### #3 — WP2b optional libmpv render-API engine-swap spike · **JUDGMENT (spike, benchmark-gated)**

Optional, AFTER WS1 Phase E. Primary candidate `HanumanInstitute.LibMpv.Avalonia` (LGPL build,
near-zero-copy GL). Decision record lives in [`skia-rebuild-goal.md`](skia-rebuild-goal.md) (media-engine
decision record — owner-authorized, benchmark-gated).

- **Acceptance:** ≥20% CPU reduction OR smoother pacing at 1080p, zero behavior regressions, behind the
  same `IVideoService`/`VideoLayer` seams. Revert-not-patch if it fails the gate.

### #4 — WP4/WS3 Windows completion sweep · **MECHANICAL**

Run the `port-audit` skill over the whole Windows app; every remaining effect-window candidate becomes
a layer / is justified interactive / gets a row; re-verify parity-matrix rows invalidated by WS1/WS2
(feeding [`avalonia-ui-parity-matrix.md`](avalonia-ui-parity-matrix.md) "Re-verify queue");
`--benchmark`/`--max-benchmark` not worse than benchmark-optimized.json.

- **Acceptance:** Windows head reaches DoD item 5; parity matrix Windows column clean; perf gate held.

### #5 — WP5/WS4 Linux bring-up epic · **JUDGMENT (input/click-through) + MECHANICAL (sweep)**

`SupportsClickThrough = IsWindows` — there is **ZERO click-through code** on Linux (no XShape/XFixes input
region), no input hooks, no verified feature sweep. The head builds and launches in a VM, but features
are not swept. This is essentially the whole remaining Linux gap (head overall ~45%).

- **Detail:** [`crossplatform-rebuild-plan.md`](crossplatform-rebuild-plan.md) WS4 +
  [`linux-vm-testing.md`](linux-vm-testing.md).
- **Mechanism catalogue (each work-or-degrade-with-recorded-gap per the spirit; Windows never degrades):**
  - X11 click-through via `IOverlaySurface.SetClickThrough` (XShape/XFixes input regions); Wayland best-effort.
  - Global-mouse alternatives: evdev / XInput2 / XRecord.
  - System libvlc packages; wallpaper / WebView / audio-ducking Linux-native equivalents or graceful
    degrade with a recorded gap (PipeWire/PulseAudio ducking, layer-shell wallpaper, WebKitGTK/system
    browser flow).
  - Full feature sweep over the launched head (feeds parity matrix Linux column, currently all `[ ]`).
- **Acceptance:** Linux head feature-swept with per-feature status recorded; click-through works on X11;
  genuine platform gaps recorded (never silently absent).

### #6 — DTRH web roguelite port epic (dollhouse rewrite) · **JUDGMENT** (NOT seam-blocked; re-inventoried after merge `a06509eb` 2026-07-10)

**SUPERSEDED / RE-INVENTORIED 2026-07-10 (merge `a06509eb`, main `6e55bcc3`):** the earlier "The Fall"
snapshot this row was written against has been replaced upstream by the **dollhouse rewrite** — an
in-ambient 3D hub, gold economy (SchemaVersion 3), Four Chambers identity, journey rooms + 16 biomes,
junction v6, and a duo-boon wave. **The old plan is scrapped; the current merged bundle is the version to
implement**; the inventory below is corrected to the post-merge tree. NOTE the game is a **WebView2 web
app (HTML/JS + Three.js), NOT Skia** — it ports through the `IBrowserHost` seam (copy the web bundle + port
the JS↔C# bridge), not as native Avalonia/Skia layers.

**Web bundle advanced to v6.3.0 (merge `a539a7c7`, 2026-07-10):** the DTRH web game (`Resources/web/dtrh/**`,
14 files — chaosRun/spawner/biomeMech/warren/junctions/vnPortrait/…) got the v6.3.0 "Deeper Down" play-test
batch (consumable actives in the run-pick ribbon, native-stinger audio panel, VN-portrait gating, Warren
options overlay, charges-3/biome-clarity). **The Windows Avalonia head already auto-bundles this** —
`CCP.Avalonia.Desktop.Windows.csproj:61` links `..\Resources\web\**\*` as Content, so the port inherits the
current assets with no copy step; **implement the web port against the live `Resources/web/dtrh/` tree, not a
snapshot.** GAP: the Linux/macOS heads do NOT link `Resources/web/**` (see the dedicated row below) — they
will have no DTRH assets until that + a real browser host land (row #5). Native chaos-run confirm-then-delete
ordering is unchanged.

**OWNER RULING 2026-07-10 (direction):** the Avalonia head goes **web-only** for DTRH. The native/Skia
chaos-run game already ported (WS2 S1–S9, hub/HUD/boon-bar windows, run-specific Core services) is now
**dead code slated for deletion** — see the decommission phase in the appendix. WPF keeps its native run as
a Lab toggle + WebGL-boot-failure fallback (`MainWindow.Lab.cs:107-126`, `ChaosWebGameEnabled` default ON
since M6); the Avalonia head deliberately does **NOT** port that fallback — a web boot failure surfaces an
error, not the native game (**recorded owner-approved deviation** from WPF behavior). Ordering is binding:
**implement the web port FIRST, then delete** — the head never loses the feature mid-stream. Linux caveat:
until `WebKitGtkBrowserHost` is real (row #5), DTRH has no in-app presence on Linux at all — accepted.

**Doctrine split (owner, 2026-07-10):** UCE stays correct and unchanged — it covers **ambient/session
conditioning that runs while the user uses the device normally**. DTRH is a **dedicated game environment
in its own window**: it does not interact with the rest of the desktop, so it needs NO compositor layers,
NO click-through/input-mask machinery, NO topmost. **Window contract (matches WPF `ChaosWebViewHost`):**
dedicated window hosting the browser control, **never Topmost** (`ChaosWebViewHost.cs:112-113` — free
Alt-Tab/minimize), launches windowed (`DtrhHostService.cs:101` `StartFullscreen=false`), **borderless
fullscreen** on the page's Fullscreen API toggle (`WindowStyle.None` + whole-screen manual bounds incl
taskbar, `ChaosWebViewHost.cs:138-154`; synced via `ContainsFullScreenElementChanged`). Owner requirement:
the game window is borderless or fullscreen — satisfied by the borderless-fullscreen toggle; port that
same contract onto an Avalonia `Window`.

A web-era roguelite ("endless rabbit hole") that renders a Three.js/WebGL 3D world inside a WebView2 host
and bridges to C# for XP, assets, and chaos payloads. This is the single largest parity gap introduced
since the port began. **FALSIFIED at the 2026-07-10 trust-nothing verification pass:** the old
"BLOCKED on a missing `IBrowserHost` seam (only `OpenExternalAsync` exists)" premise is wrong —
`IBrowserHost` is an implemented, rich 11-member in-app WebView seam (`CCP.Core/Platform/IBrowserHost.cs`:
`NavigateAsync`, `ExecuteScriptAsync`, `WebMessageReceived`/`PostWebMessageAsJson`,
`SetVirtualHostToFolder`, `CreateBrowserControl`, `PopOutAsync`, fullscreen/title/navigation events …)
with per-head impls: `WebView2BrowserHost` (Windows), `WebKitBrowserHost` (macOS), `WebKitGtkBrowserHost`
(Linux — a stub: shells out to `xdg-open`, `CreateBrowserControl()` returns null) and `MobileBrowserHost`
(Android). `ChaosTunnelService` already hosts the three.js "rabbit hole" tunnel through it with JSON
messaging (`CCP.Avalonia.Desktop.Windows/Services/Chaos/ChaosTunnelService.cs:24-33`). **EPIC phase 0
(the seam) is DONE on Windows**; remaining work = the game port itself (+ a real Linux in-app host — row
#5 territory). Coordinate with `unified-compositor-engine`/`port-feature`.

#### Appendix — phase breakdown (absorbed from the v6.2.11 release catalogue, deleted 2026-07-10; its knowledge now lives here)

| Sub-part | WPF source | Avalonia/Core target | Notes |
|---|---|---|---|
| **Web game bundle** — re-inventoried post-merge `a06509eb` 2026-07-10. Engine JS lives in SUBDIRS (the old top-level-only scan missed it): `engine/` (20 JS — `spawner.js` 99KB, `junctions.js` 69KB, `scene.js` 58KB, `tunnel.js` 32KB: the Three.js 3D world), `game/` (22 JS — `chaosRun.js` ~170KB, `chaosField.js` 63KB, `warren.js` 53KB, `catalog.js` 44KB, `biomeMech.js` 33KB: roguelite logic incl gold economy / Four Chambers / 16 biomes / junction v6), `shared/` (5), `vendor/` (10 — `three.module.min.js` 687KB, `omggif`), `assets/` (barks `manifest.js` 339KB + 458 mp3), + 5 top-level (`boot.js`/`bridge.js`/`hostMedia.js`/`m2test.js`/`spike.js`) + `styles.css`. RESOLVED: `chaosRun.js`/`warren.js` DO exist — under `game/`, not top-level | `Resources/web/dtrh/**` | `CCP.Avalonia` resources (Content-linked / `avares://`) | Platform-agnostic JS/HTML/CSS. Mostly copy+wire, served through the browser-host seam, not `Resources/`. |
| **Host + bridge services** | `Services/Chaos/DtrhHostService.cs` (914L, WebView2), `DtrhMetaBridge.cs` (406L), `DtrhSpike.cs` (226L) | new `CCP.Core/Services/Chaos/Dtrh*` (portable logic) + head browser-host impl | Split: JS↔C# message bridge + run/session orchestration is portable; the WebView2 surface is head-specific. |
| **Asset + session telemetry** | `DtrhAssetManifest.cs` (130L), `DtrhAssetStatsStore.cs` (127L), `DtrhSessionStatsStore.cs` (186L) | `CCP.Core/Services/Chaos/` | Portable (file I/O + models). Respect the privacy contract: per-asset engagement stays local. |
| **Chaos meta/progression model deltas** | `Services/Chaos/ChaosModels.cs`, `ChaosUpgrades.cs`, `ChaosMetaState.cs`, `ChaosLifetimeBoons.cs` (68L new) | `CCP.Core/Services/Chaos/` counterparts | Lifetime boons, upgrades, meta-state feed the roguelite. Mirror the model changes into Core so both heads share them. |
| **Legacy chaos WebView host** | `Chaos/ChaosWebViewHost.cs`, `Chaos/ChaosHubWindow.xaml.cs` | browser-host seam | WebView2 shells for the hub/game. |
| **Lab-tab launch hook** | `Views/Tabs/LabTabView.xaml`, `MainWindow.LabTab.cs`, `MainWindow.Lab.cs` | `CCP.Avalonia` Lab tab view + VM | The entry point that boots the web game. |
| **In-world integration edits** | `Chaos/ChaosFlashOverlay.cs`, `ChaosGifCascadeOverlay.cs`, `Services/Flash/FlashService.cs` (+15), `Services/Video/VideoService.cs` (+26), `Services/Tracking/GazeFocusService.cs` (+102, gaze-click follows camera), `GazeDebugCursorService.cs` (+28), `App.xaml.cs`, `AvatarTube/AvatarTubeWindow.Speech.cs`, `MainWindow.RemoteControl.cs` | mostly `CCP.Core` / `CCP.Avalonia` chaos + gaze + flash services | Commit `8343e1e0` "render bubble effects in-world; gaze-click follows camera; glitch/cascade draw the flash pool". VERIFY whether any standalone (non-DTRH) flash/video/gaze behavior changed and needs an independent port. |
| **Native chaos-run DECOMMISSION (LAST phase — only after the web port is live and user-verified)** | n/a (deletion in the Avalonia head, not a WPF port) | delete dead run-game code: `CCP.Avalonia/Chaos/` run-specific files (~12k lines total in the folder — `ChaosHubWindow.*` (hub, ~3.3k), `ChaosHudWindow`, `ChaosBoonBarOverlay`, `ChaosIntroWindow`, `ChaosUnlockCardOverlay`/`AvaloniaChaosUnlockCards`, `ChaosHappyPath`, `ChaosLessons`, `ChaosNarrativeDirector`, `ChaosBackdropService`, `AvaloniaChaosCatalogs`, `AvaloniaChaosStubs`, …) + run-specific `CCP.Core/Services/Chaos/` (`ChaosDraftPool`, `ChaosEconomy`, `ChaosScoring`, `ChaosSpawnDirector`, `ChaosSpawnCatalog`, `ChaosRunRules`, `ChaosRunKnobs`, …) + run-only compositor chaos layers | **Confirm-then-delete per file** (grep zero live refs before each removal; never bulk-delete). **CARVE-OUTS — shared with ambient/non-DTRH features, keep:** `BubbleEngine`/`BubbleState`/`IBubbleService`/`BubbleLayer` (ambient trigger-bubbles), gif-cascade + DVD + any layer reachable from ambient effects (#493 Gif Rain row #8a uses the cascade), `ChaosImagePool` (facade consumers), `ChaosCrashSentinel` (VERIFY), `ChaosMetaState`/meta persistence (likely becomes the web game's C# meta store via the bridge — VERIFY against `DtrhMetaBridge` before deleting anything meta). Each deletion commit runs full gates incl `--verify-layers`. |

### #7 — v6.2.11 verify-set · **VERIFY**

Five WPF-window- or Windows-installer-specific fixes from v6.2.11. The portable fixes (2b quiz #501 + 2c
speech #505) are DONE; 2a bark floor is N/A (Avalonia has no rule-based bark gate); 2d trigger-bubble is
already done. These five remain:

- **3a** lock-card repeat: gate on `LockCardWindow.IsAnyOpen()` not `Application.Current.Windows`
  (keep-alive pooled hidden window lingered → blocked every card after the first); `ForceCloseAll()` if
  `ShowOnAllMonitors` throws mid-show. VERIFY the Avalonia UCE lock-card path for the analogous hidden-
  pool-blocks-guard bug and a mid-show throw leaving the visible-set armed. Code-check 2026-07-10:
  `IsAnyOpen():99` + `ForceCloseAll():144` exist and are consumed (`AvaloniaLockCardService.cs:83`,
  `BubbleCountResultWindow.axaml.cs:340`); the runtime repeat-guard behavior itself — like 3b–3e —
  remains headed-unverified (no headless evidence).
- **3b** overlay z-order #497: spiral/pink must not bury session videos. VERIFY (likely N/A) — in the UCE,
  video (`VideoLayer` Z=10) and spiral/pink are z-ordered layers on one surface, so the WPF "video buried
  behind a filter window" class is structurally absent; confirm the layer ordering keeps session video
  visible under the filters.
- **3c** bounce-in-tray: bouncing/subliminal text must keep running on the close→tray gesture (WPF removed
  a stray `App.BouncingText?.Stop()`). VERIFY the Avalonia tray/minimize path does not stop bouncing text.
- **3d** weekly-quest "stuck on Loading" #496: when a stored weekly is completed but its definition no
  longer resolves (server rotated the pool), render a "complete — new one Monday" card instead of the
  blank Loading state; do NOT regenerate (would double-reward). NEEDS PORT if the Avalonia Quests tab is
  ported (`CCP.Core/Services/Progression/QuestService.cs` is present).
- **3e** update-restart #499: N/A until the Avalonia head has a production installer; when it ships one,
  port the equivalent via the `IUpdateInstaller` seam.

### #8a — #493 Gif Rain cascade multi-monitor · **STANDARD**

From the v6.2.10 release (its catalogue was deleted 2026-07-10; the work now lives here). The cascade must spawn across the correct monitor set (all monitors for a
dashboard trigger or chaos+DualMonitor; primary-only for a single-screen chaos run) so rain no longer
falls off the active screen on multi-monitor rigs. Target: `CCP.Avalonia/Compositor/Layers/ChaosGifCascadeLayer.cs`
(compositor lane — `unified-compositor-engine` skill mandatory). WPF fix spans the full virtual screen
+ a `_spawnLeft/_spawnWidth` spawn-band. Visual-verification gated.

### #8b — #493 dashboard bubble motion-override · **STANDARD**

`ChaosMotionMode` (Mixed/FloatUp/RainDown/RoamBounce instead of always FloatUp) for **ambient** dashboard
bubbles. Target = `CCP.Core/Services/Chaos/BubbleEngine.cs` (the ambient engine
`AvaloniaBubbleService._ambientEngine`): the Avalonia ambient bubble is a simplified physics-rising model
(vy<0 = rises); the AMBIENT engine uses no `ChaosMotion` today. (Corrected 2026-07-10: the blanket
"Avalonia uses NO ChaosMotion enum" phrasing was stale — chaos-RUN effect bubbles DO parse/use
`ChaosMotion`, `AvaloniaChaosStubs.cs:284,306-308`; only the dashboard ambient path lacks it.) Port = ADD FloatUp(rise)/RainDown(fall-from-top)/
RoamBounce(bounce) to match WPF `ChaosBubbleSpec` dashboard bubbles. MEDIUM behavioral change,
visual-unverifiable headless → defer to a focused session with visual verification (ignoring RoamBounce
= false port).

### R-scrub — scrub stale doc citations from `.cs` files · **MECHANICAL**

Comments-only, zero behavior change. The docs-rework (2026-07-10) deletes ~44 `.md` files; the `.cs`
code comments that cite them are out of scope for that `.md`-only workflow and land here. Remove or
re-point each citation basename to its successor (knowledge now lives in WPF source as the permanent
behavior reference + pinned tests + the surviving design doc):

- `ChaosEconomy.cs`, `ChaosScoring.cs`, `ChaosSpawnDirector.cs`, `ChaosBubbleHints.cs` (cited the deleted
  `chaos-run-engine-contracts/economy-scoring.md` + `spawn-system.md` — economy/spawn ported+tested
  `87515732`/`2d7bc384`; see CHAOS_DESIGN.md).
- `AvaloniaHeadStubs.cs` (cited the deleted `chaos-run-engine-port-plan.md` S1–S9 — all done + verified).
- `AttentionCheckLayer.cs` (cited the deleted `attention-check-layer-migration-spec.md` — DONE `57f6f048`).
- `ProfileSyncService.cs:27` (cited the deleted `profilesync-port-plan.md` — shipped).
- The 3 chaos test files (`ChaosEconomyTests`, `ChaosScoringTests`, `ChaosBubbleHintsTests`) that cite the
  same deleted contracts.

Gates: slnf 0 · WPF sln 0 · Core 542/542 (count never decreases) · smoke Findings 5.

- **CLAIM 2026-07-10 · wip @driver (workflow-run, continuous-mode session):** grounded live before work —
  full-basename grep over `**/*.cs` (all 44 deleted docs + the 2 pre-rework deletions) = **12 stale-cite
  lines across 11 files**: the row's 9 listed files PLUS `CCP.Core/Services/Settings/IProfileSyncService.cs:14`
  and `CCP.Avalonia/ServiceCollectionExtensions.cs:216` (both cite `profilesync-port-plan.md`; chokepoint
  file edit is comments-only). `AvaloniaHeadStubs.cs` has 3 sites (`:153`, `:1815`, `:2232`). No WPF-head
  hits. Plan: parallel per-file comment scrub via workflow agents → re-grep 0 hits → gates → one commit.
- **DONE 2026-07-10 (same session):** all 12 lines scrubbed/re-pointed across the 11 files; adversarial
  verifier confirmed comments-only (23+/17−, zero code tokens), residual grep 0 hits, all WPF file:line
  cites preserved (one net-ADDED: WPF `:2336-2338` on the pendulum comment). Gates: slnf 0 · WPF sln 0 ·
  Core 542/542 · smoke 44 tabs / 0 unhandled / Findings 16 = pre-existing drift at clean HEAD (see the
  smoke-drift row below; owner-waved 2026-07-10). Note for a future session: successor cite
  `Services/Chaos/CHAOS_DESIGN.md` lives in the WPF tree — if that head is ever removed, relocate the
  design doc into Core (recorded by the verifier as non-blocking).

### Smoke-drift record — baseline Findings 5 → 16 at clean HEAD · **owner-waved 2026-07-10**

First live smoke re-run since the recorded baseline (verified 2× at clean HEAD `b0319b0f`, 2026-07-10):
44 tabs / 0 unhandled / **Findings 16**. All 11 deltas are auth-gated `availablesubjects` surface noise,
diagnosed **benign-by-design, not parity bugs**: 10× loc-key-heuristic misfires on canonical subject tags
that BOTH heads render raw by recorded intent (WPF `AvailableSubjectsTabView.xaml` `Text="{Binding}"`
"mirrors cclabs-web /dashboard/subjects"; Avalonia `:186` identical; harness regex `SmokeTestRunner.cs:753`)
+ 1× transient `ConnectCommand` DataContext-null warning during tab content-swap (command IS source-generated,
`AvailableSubjectsTabViewModel.cs:112-113`; button live). Visibility flip = environment (smoke env's
settings.json gained cached cclabs auth after 2026-07-04), NOT git — every commit in `fb704a6d..HEAD`
exonerated. **Owner ruling 2026-07-10: continue the port; no re-baseline apparatus now — expected to
self-resolve.** Commits over this drift record "Findings 16 = known pre-existing drift", never "at
baseline". Deterministic fallback if it flaps: pin the smoke env logged-out (strip cached UnifiedId/
AuthToken) → findings return to 5. Never edit `SmokeTestRunner.cs`; never loc-map the chips (diverges
from WPF).
**Data point 2026-07-10 (same day, ~2h later):** live run at IMP-1 close produced **15** (9 tags —
'drone' absent from the server directory this run — + 1 ConnectCommand), a strict subset of the recorded
11-delta set with ZERO new findings. Confirms the count is server-content-coupled exactly as predicted;
session gate practice under the owner ruling: gates 1-3 hard, gate 4 = 44 tabs + 0 unhandled + delta-set
⊆ the recorded benign set (count-equality meaningless while the env is authed).

### BubbleLayer bubble.png bypasses the mod resolver · **STANDARD** — **DONE 2026-07-10 (this session)**

Found by the 2026-07-10 bubble-border completeness sweep: `BubbleLayer` always decoded the embedded
`avares://CCP.Avalonia/Assets/bubble.png`, ignoring mod bubble reskins (WPF resolves `bubble.png` through
mods, `Services/BubbleService.cs:812-820`). **FIXED:** `BubbleLayer.EnsureBubbleImage` now resolves via
`AvaloniaModResourceResolver.ResolveUri("bubble.png")` (mod override → embedded fallback) and subscribes
lazily to `ResourcesChanged` so a mid-run mod switch reskins live bubbles. The reload decodes OUTSIDE
`_sync`, swaps the immutable `SKImage` reference under `_sync`, and NEVER disposes the old handle —
preserving the never-freed invariant (0xC0000005 guard, AvaloniaUI/Avalonia#13521); leaks one handle per
rare mod switch. Decode-failure ladder keeps the current image (no blank bubbles). JUDGMENT-tier threading
review (6 attack vectors: use-after-free, render-thread DI/event, deadlock, dispose-ordering, parity,
`_dirty` access): **VERDICT SHIP**. Gates: slnf 0 · WPF sln 0 · Core 543/543 · `--verify-layers`
BubbleLayer PASS · smoke 44 tabs / 0 unhandled.

### Voice — E2E mic live run · **VERIFY**

Carry the open `⏳ Remaining` from [`voice-port-status.md`](voice-port-status.md): an end-to-end mic live
run (mantra repeat-after-me + voice commands) headed-verified against the WPF head, plus the coupled
items noted there.

### Tutorial — unported tutorial system · **DEFER**

The interactive tutorial overlay system is not fully ported. Recon lives in
[`TUTORIAL_SYSTEM_CONTEXT.md`](TUTORIAL_SYSTEM_CONTEXT.md); claim only after reading it. (Mod-creator and
some per-window tutorials are already shipped — see the shipped ledger.)

### AI_AUDIT — WPF-path refresh · **MECHANICAL (low-pri)** — **DONE 2026-07-10 (this session)**

[`AI_AUDIT.md`](../AI_AUDIT.md) (repo root) carried WPF-era paths that misled porting agents. **DONE:**
dispatched to a STANDARD-tier agent (glm-5.2) via the `workflow` tool; 117 path-only edits (line count
preserved 1326, findings/prose untouched) refreshed to the dual-head layout with WPF-frozen-ref vs
Core/Avalonia labelling. Driver-verified in-sandbox: 120/123 new path tokens exist on disk; the 3
"missing" are runtime-data files (`settings.json`/`webcam-calibration.json`/`local_chat_history.json`)
correctly cited at their `%APPDATA%` locations with their managing source class. Docs-only; no build
gates apply.

### subagents.json — `.kimi-code` re-sync · **MECHANICAL (low-pri, non-md)** — **DONE 2026-07-10 (this session)**

`.kimi-code/subagents.json` was divergent from `.pi/subagents.json`. **DONE** (STANDARD-tier agent via the
`workflow` tool): the files are NOT agent-definition registries — they are subagent RUNTIME-CONFIG scalars
(`maxConcurrent`/`defaultMaxTurns`/`graceTurns`). Aligned the mirror to authoritative `.pi`: `maxConcurrent`
9→10, `defaultMaxTurns` 0→20 (`graceTurns` already 10). Both files now byte-equal
(`{10,20,10}`). NOTE for a future session: the docs-index "known gap" described this as "divergent agent
definitions" — that was imprecise; the actual divergence was two config values, now reconciled.

### ai-command P3 gaps · **DEFER**

Preserved from the AI-command port follow-ups (dispatcher itself is DONE `70cf9803`/`9fa09853`/`424ea528`);
cloud faithfully omits commands):

- `IBubbleService.Start()` is parameterless — WPF passes a runtime freq for the bubbles-frequency
  override; needs a `Start(int?)` seam or a spawn-rate setter.
- ~~`LocalAiService` `[CONTEXT BLOCK]` enrichment~~ **CLOSED (verified 2026-07-10):**
  `EnsureEnrichmentMessage()` manages `[CONTEXT BLOCK]` at `LocalAiService.cs:426-439` (landed
  `424ea528`, Phase 3b). The remaining four gaps below still hold (code-checked same day).
- Enrichment `factsJson=""` — no Core `KnowledgeService` yet.
- Legacy Patreon bearer-token `/ai/chat` fallback (`IPatreonTokenProvider`) for V2-404 / no-cloud-identity
  users.
- `SystemPromptBuilder` hypnotube block uses slug-fallback names (WPF `KnownVideoLinks` reverse-map is a
  Window static) — needs a reverse-map seam for clickable-name fidelity. (SystemPromptBuilder parity
  itself is DONE `b84eb90`.)

---

## Improvement opportunities (filed 2026-07-10, trust-nothing verification pass)

Filed from the same-day evidence-based improvement scans (compositor/video/input · Core services +
seams + DI · non-compositor hot paths) against code `5e3ed650`. Every row is grounded in file:line
evidence from live code; none duplicates an OPEN row above (checked against #1/#2/#3/#4/#5/#8a/#8b
before filing). Scan finding I-6 (per-monitor dirty invalidation) was FOLDED into row #1's mechanism
rather than filed here; the Background-priority-tick hypothesis also feeds row #2. Owner ruling
2026-07-10: verified-existing features are fair game — big changes allowed on merit.

| Row | Evidence | Expected gain | Tier | Proportionality |
|---|---|---|---|---|
### DTRH web assets not bundled on Linux/macOS heads · **STANDARD (cross-platform DTRH gap, filed 2026-07-10)**

Only `CCP.Avalonia.Desktop.Windows.csproj:61` links `..\Resources\web\**\*` (Content) — so the DTRH web
bundle (`Resources/web/dtrh/**`) ships ONLY on the Windows head. `CCP.Avalonia.Desktop.Linux`,
`CCP.Avalonia.Desktop.macOS`, `CCP.Avalonia.Desktop`, and shared `CCP.Avalonia` have NO `Resources/web`
include. When the DTRH web port (row #6) lands, the Linux/macOS heads will have no game assets to load. Fix
= add the same `Content Include="..\Resources\web\**\*"` link to the Linux/macOS heads (or hoist it to a
shared `.props`) as part of, or just before, the row-#6 web port + the row-#5 `WebKitGtkBrowserHost`/
`WKWebView` browser host. Sub-item of the cross-platform DTRH work; not actionable in isolation until a
browser host exists on those OSes, but recorded so it is not missed.

---

**Merge `a539a7c7` (v6.3.0 "Deeper Down") reconciliation — 2026-07-10 (this session):** merged main
(6 commits: v6.3.0 release + DTRH web-game play-test batch) into `feat/crossplatform` (merge commit
`3d4362b6`, clean/no-conflict). All incoming code landed in the **frozen WPF head** + shared/linked assets;
port-side reconciliation done separately: (1) **version bump to 6.3.0** across all 8 Avalonia csproj + Android
`ApplicationDisplayVersion` + `CCP.Core/Services/Update/UpdateService.cs` (`CurrentVersion`, `AppVersion`,
`CurrentPatchNotes`→Deeper Down) — **stops the update-available popup on the Avalonia head** (verified in
smoke: `current=6.3.0, latest=6.3.0, isNewer=False`). (2) **Localization** v6.3.0 strings
(`btn_v6_3_0_is_out`/`tooltip_v6_3_0_deeper_down`) flow automatically — `CCP.Core.csproj:50` links the WPF
head's `Localization/Languages/*.json`. (3) **DTRH web assets** flow automatically to the Windows head (csproj
Content link); Linux/macOS gap filed as the row directly above. (4) **No UpdateService logic to mirror** — the
WPF `UpdateService.cs` 90-line diff was patch-notes + version only (verified). **Doc fix (this session):** the
AGENTS.md version-bump list named only the WPF `Services/Update/UpdateService.cs`; corrected to also list the
port-critical `CCP.Core/Services/Update/UpdateService.cs` (the one the Avalonia head actually reads).

**User-requested: responsive dashboard (no-scroll, scale-to-fit) — 2026-07-10 (this session):** the
dashboard tab (`SettingsTabView.axaml`) required vertical scrolling to see everything. Fixed by removing the
outer `ScrollViewer` so the tab fills its bounded `ContentControl`, wrapping the 4x4 feature-card block +
centre emblem in a `Viewbox Stretch="Uniform"` (explicit 640x600 design child; proportional `47*,53*` column
split to kill the dead gutter) so cards/pics/labels scale with window size, and converting the browser
panel's inner `StackPanel`→`Grid RowDefinitions="Auto,*"` with the browser container's fixed `Height="340"`
removed so it flexes via LAYOUT (native WebView2 host resizes safely — NOT under a render-transform, which
would break it). Verified: slnf 0 err, smoke 44 tabs/0 unhandled, per-theme dashboard screenshot shows all
16 cards + emblem + browser/audio/quick-links fitting with no scrollbar. JUDGMENT (advisor) validated the
approach + caught the StackPanel-infinite-measure + 640-gutter traps pre-implementation.

**User-reported PARITY BUG fixed: flash images/gifs not random — 2026-07-10 (this session):** the user saw
the same few flash images every launch despite a library of thousands. Root cause: `AvaloniaFlashService`
`GetImageFiles` scanned the images path with `SearchOption.TopDirectoryOnly`, so a library organized into
subfolders (categories) only ever exposed the handful of loose top-level files to `_random.Next`. The frozen
WPF head scans `SearchOption.AllDirectories` ("Scan subfolders to support user-organized categories",
`FlashService.cs:2039`), and the Avalonia video/bubble-count/content-pack scanners already use
`AllDirectories` — flash was the lone `TopDirectoryOnly` outlier. Changed to `AllDirectories` (restores
parity; the whole library is now in the pool). `_random = new()` was already time-seeded, so randomness was
fine once the pool was correct. Gates: slnf 0 err, smoke 44 tabs/0 unhandled.

**User-requested dashboard right-column rebalance + `?`-overlap fix — 2026-07-10 (this session):** (1) removed
the dashboard's duplicate "Media folder" picker (the System/gear settings popup already hosts it via
`SystemFeatureControl` "Assets folder") and dropped the right column from `RowDefinitions="*,Auto,Auto"` to
`"*,Auto"`, so the browser panel (the lone `*` row) gets the freed space and a proportionally larger share as
the window grows; audio + quick-links stay compact `Auto`. (2) Fixed the Quick Links help `?` button which
had no `HorizontalAlignment` in its single-cell header Grid — with an explicit `Width=22` it centred and
overlapped the "Quick Links" label; added `HorizontalAlignment="Right"` to match the Audio/Browser panels.
Gates: slnf 0 err, smoke 44 tabs/0 unhandled, per-theme dashboard screenshot confirms bigger browser, no
media row, `?` at the header's right edge.

**JUDGMENT redesign: compacted Audio + Quick Links panels — 2026-07-10 (this session):** dispatched a
JUDGMENT-tier agent (fable-5) to shrink + tidy both right-column panels. Audio: header/duck/chevron/help
collapsed into one 6-col grid, toggle On/Off text removed (`OnContent=""/OffContent=""`, established repo
pattern), slider margins → `Spacing`, advanced options stay collapsed behind `BtnAudioAdvanced`. Quick Links:
Join-Discord + RP toggle share one row, tighter paddings, display-name ellipsis. Also replaced two
pre-existing hardcoded `#33FF69B4` fills with `{DynamicResource TransparentPinkBrush}` (theme-compliance
win). ~55-60px reclaimed by the browser; cards now near-equal height (no dead space). All bindings/commands/
x:Names preserved (grep-verified). Driver reviewed diff + screenshot. Gates: slnf 0 err, smoke 44 tabs/0
unhandled.

**JUDGMENT redesign: Presets / Quests / Enhancements pages + dashboard helper buttons + skill-tree
layout parity — 2026-07-10 (this session, 3 surgical commits):** user reported those tabs looked bad
vs WPF (esp. Enhancements) and the dashboard helper buttons looked ugly.
- **Dashboard helper buttons** (`SettingsTabView.axaml`): bare default Buttons → themed `OutlineButton`
  (rounded pink outline, centered) with spaced emoji+label. HARNESS CONSTRAINT discovered: the smoke
  `GetTextFromObject` only reads `string`/`TextBlock`/`ContentControl` — a `StackPanel` content returns
  null and breaks helper-button detection (3 "not found" errors). Kept a SINGLE `TextBlock` with `Run`s
  (emoji + space + label) so `GetTextBlockText` concatenates inlines. Findings 19→16 confirms the fix.
- **Presets + Quests** (`PresetsTabView.axaml`, `QuestsTabView.axaml`): JUDGMENT (fable-5) minimalistic
  restyle. Set-diff verified 0 dropped bindings/x:Names (118→118 / 106→106). Quests keeps its 6 semantic
  status colors (gold/green/purple) hardcoded — confirmed matching WPF (theme-independent).
- **Enhancements** (`EnhancementsTabView.axaml` + `.axaml.cs` + `EnhancementsTabViewModel.cs`): two bugs
  fixed. (1) container-position no-op — `Canvas.Left/Top` on a DataTemplate root is ignored in an Avalonia
  ItemsControl (the ContentPresenter is the Canvas child), so nodes stacked at 0,0 with lines slashing
  across; fixed with the official v12 `Style Selector="ItemsControl > ContentPresenter"` idiom. (2) wrong
  layout — VM stacked each tier vertically (tier 4 = 9 skills → 1980px in a 460 canvas → top/bottom clip).
  Ported WPF's exact 3-horizontal-path position map (`MainWindow.Enhancements.cs:91-137`), re-anchored
  lines right-center→left-center, excluded secrets (WPF parity), canvas 2400x460→3200x640, and added
  height-driven scaling (`LayoutTransformControl` + `scale=clamp(viewportH/640,0.5,1.25)`) so cards +
  images grow/shrink with the window while width scrolls horizontally like WPF. Validated by advisor
  (JUDGMENT) + official Avalonia docs. Gates: slnf 0 err, WPF sln 0 err, smoke 44/0, Findings 16 (no
  HelperButton errors). Skill-tree visual not smoke-screenshotted — flagged for user eyeball.

**Claim-priority order (LIVE — the claimer updates this line as rows close/land):**
**#4 (WS3 sweep) → #3 (libmpv, CONDITIONAL)** for autonomous tiers. **row #2 re-baseline is now BLOCKED**
(this session): its scheduling half is DONE via IMP-2 and `MinFps=0` is root-caused (un-decodable YouTube
watch URL in the benchmark harness), but the clean 240s re-baseline is blocked by a PRE-EXISTING Release
`--max-benchmark` window-show SIGSEGV (new row above, JUDGMENT native-crash forensics — needs a minidump
triage before any fix; the harness URL fix is bundled there because it is unverifiable until the run works).
**#6 (DTRH web)** is a JUDGMENT multi-session epic (owner-written direction). AI_AUDIT + subagents.json DONE
(this session). BubbleLayer-mod-resolver DONE (this session). IMP-ECON1 DONE (this session) — economy double-pay fixed;
**Core test floor 542→543** (new pinning test). Original 12-row improvement queue fully resolved this session:
IMP-1 DONE `49ec3707`. IMP-11 DONE `28fa06a2`. IMP-5 DONE `23b4dd86`. IMP-7 DONE `85f036f1`. IMP-10 DONE
`53f2b4d7`. IMP-4 DONE `e4f40bc1`. IMP-9 DONE `066063e4`. IMP-6 DONE `7e6e0d9e`. IMP-2 DONE `60b10afc`.
IMP-8 EVALUATED → **DECLINED** `14db71f1` (flag-don't-force; coupling not worth the marginal gain —
rationale on the row). IMP-3 is CONDITIONAL on row #3's libmpv spike gate — never do both. Excluded from
the autonomous bar (product-gated / VERIFY / DEFER / visual-gated / Linux-VM): #1 (product Qs b+c), #5
(Linux VM), #7 & Voice (VERIFY), #8a/#8b (visual), Tutorial / ai-command P3 / #9 backlog (DEFER).

| **IMP-1 — `VideoLayer` lacks a `ConsumeDirty` override**: engine invalidates all windows at 60Hz while clips decode at ~25-30fps; `Update`'s `presented` flag (FRONT↔READY swap) IS the dirty signal | `Compositor/Layers/VideoLayer.cs` (no override; inherits always-true `BaseLayer.ConsumeDirty()`, `BaseLayer.cs:46`) | ~halves GPU render passes during plain video playback; `MandatoryVideoLayer` inherits the fix free | MECHANICAL | ~10 lines, UI-tick-only state, no protocol change; verify with `--verify-video` |
| **IMP-2 — engine render tick runs at Background dispatcher priority** — **DONE 2026-07-10**: parameterless `DispatcherTimer` = background priority (VERIFIED, Avalonia 12.0.5 `Avalonia.Base.xml` `DispatcherTimer.#ctor`) — the whole UCE hung off one starvable timer | `Compositor/CompositorEngine.cs` ctor → `new DispatcherTimer(DispatcherPriority.Render)` | removed scheduling-induced stutter; measured before/after `--benchmark`: Idle 122.6→141.6 fps (min 118→136), Active 123.2→144.2 fps (min 117→129) | STANDARD | DONE — ~+15–17% avg fps AND higher mins (starvation removed); verify-layers PASS, 44-tab smoke confirms no input starvation from Render>Input |
| **IMP-3 — native-size video decode**: fixed 1920×1080 `SetVideoFormat` forces per-frame swscale + double-scaling on non-1080p media (SD upscale, 4K decode→down→GPU-up) | `Compositor/Layers/VideoLayer.cs:36-37,298`; same pattern `Controls/AvaloniaInlineLoopVideo.cs:106` | removes a full-frame swscale per decoded frame for all non-1080p media; sharper output; buffers already per-PlayVideo so the protocol supports it (cap at monitor max) | JUDGMENT | medium (format callbacks + fixed-size fallback). **CONDITIONAL: decide after row #3's libmpv spike gate — libmpv replaces this pipeline wholesale; never do both** |
| **IMP-4 — per-frame native allocs on the render path** — **DONE 2026-07-10**: letterbox `SKPaint` per frame contradicted VideoLayer's own "allocation-free" header; BrainDrain allocated `SKImageFilter.CreateBlur` + `SKPaint` per frame | `VideoLayer.cs` letterbox bg paint cached (rebuild only on `BackgroundColor` change); `BrainDrainLayer.cs` blur paint+filter cached (rebuild only on sigma change) + tint paint reused (Color mutated in place) | removed 60–360 native alloc/free per second per monitor during video/brain-drain; blur-filter caching (rebuild only on sigma change) is the meaningful part | MECHANICAL (+JUDGMENT review: compositor internals) | DONE — render-thread-only cached state; pixel-identical output (WPF-parity math unchanged) |
| **IMP-5 — delete the dead dialog-mode seam** — **DONE 2026-07-10**: `Push/PopDialogMode` was a documented no-op, yet 12 sites still bracketed every dialog with the pair and carried a compositor dependency solely for it | `Compositor/CompositorEngine.cs` PushDialogMode/PopDialogMode + `_dialogModeRefCount` field DELETED; `Platform/AvaloniaDialogService.cs` `_compositor` field/ctor-param + 6 try/finally brackets removed; DI factory `ServiceCollectionExtensions.cs:127` dropped the `sp.GetService<CompositorEngine>()` arg | deleted the phantom coupling (dialog service → compositor), the dead ref-count field, and the 6 try/finally brackets; zero behavior change by construction (methods only inc/dec an unread counter) | MECHANICAL | DONE — no-op deletion, provable zero behavior change |
| **IMP-6 — chain `VideoLayer` teardown tasks** — **DONE 2026-07-10**: `Stop()` overwrote `_teardownTask`, so `WaitForTeardown` drained only the LATEST teardown; rapid stop→play→quit left an untracked native free (player dispose + `FreeHGlobal` after 400ms) racing process exit | `Compositor/Layers/VideoLayer.cs` `Stop()` now chains onto any still-pending prior teardown: `_teardownTask = previous is {IsCompleted:false} ? Task.WhenAll(previous,this) : this` | closes a crash-on-exit race window (segfault class) | STANDARD (+JUDGMENT review: native lifetime) | DONE — single-play path byte-identical; WaitForTeardown now drains ALL still-pending teardowns; no deadlock/double-free/unbounded-growth (review-confirmed) |
| **IMP-7 — remove dead seam method `IV2AuthService.SendHeartbeatAsync(string)`** — **DONE 2026-07-10**: had ZERO Core/Avalonia callers; the real heartbeat is the internal no-arg `ProfileSyncService.SendHeartbeatAsync()` on the 120s timer; the DI comment itself admitted it dead | interface member `IV2AuthService.cs` + impl `AvaloniaV2AuthService.cs` (−26) DELETED; stale DI comment `ServiceCollectionExtensions.cs:~219` updated | removed the misleading interface member + dead impl + the stale comment contradicting the single-heartbeat-owner invariant (invariant now strengthened) | STANDARD (+JUDGMENT review for the interface change: SHIP) | DONE — no behavior change; WPF head keeps its own legacy copy (`Services/Account/V2AuthService.cs:528`, untouched) |
| **IMP-8 — coalesce the two 30s dirty-save timers** — **EVALUATED → DECLINED 2026-07-10** (flag-don't-force): `AchievementService._saveTimer` + `QuestService._saveTimer` do identical "if dirty, serialize+flush" work | `CCP.Core/Services/Progression/AchievementService.cs:86`; `QuestService.cs:71` | one fewer always-waking timer + shared crash-recovery logic | JUDGMENT | **DECLINED** — the two are cleanly-independent progression services (separate state/files/dirty flags/lifecycles); coalescing needs a new shared save-coordinator abstraction coupling them (shared failure point, DI/disposal ordering, one slow flush delaying the other) for a negligible gain (both timers are already Background-priority 30s and OS-coalesced). Per the row's own "flag, don't force / not headline perf" steer, the coupling cost outweighs it. The genuinely reusable part is the atomic tmp-write boilerplate — better extracted later as a STATELESS `AtomicJsonStore` helper (no timer/lifecycle coupling) if the duplication ever becomes a real maintenance burden. Not left OPEN: this is the JUDGMENT disposition, not a deferral. |
| **IMP-9 — `BubbleEngine` per-tick allocations** — **DONE 2026-07-10**: 2 transient lists per active 32ms tick + 2 redundant `_bubbles.ToArray()` snapshots in the hazard passes | `CCP.Core/Services/Chaos/BubbleEngine.cs` — missed/moved → reusable `_missedBuffer`/`_movedBuffer` fields; the two hazard-pass `ToArray()`s → ONE shared reused `_tickSnapshot` passed to both `TickSpankSweeps`/`TickFieldHazards` | removed ~4 Gen0 allocs/frame (~120/s) on the UI thread in bubble-mode steady state | STANDARD (+JUDGMENT review: state-mutating) | DONE — behavior-preserving (shared snapshot equivalent via PopBubble idempotency + IsPopping-before-removal + per-pass IsPopping guards); pinned by BubbleEngine unit tests; hardened per review (ambient-gated copy, buffers cleared in Stop, trail-pop IsPopping guard, non-reentrancy documented) |
| **IMP-10 — `ActiveRipples` dead alloc-y getter** — **DONE 2026-07-10**: `ToList()` + closure on every read; repo-wide grep = the declaration only, zero readers | `CCP.Core/Services/Chaos/BubbleEngine.cs` getter DELETED (doc comment + expression body) | removed a latent per-frame footgun (any future per-frame reader = per-frame `List<>` alloc) | STANDARD | DONE — deleted the dead exposure getter only; `_ripples` state + `RIPPLE_LIFE_MS` (14 refs) + the Size-Queen ripple effect (`:1434-1445`) untouched; ripple-overlay port still pending, will re-add a zero-alloc accessor when a consumer exists |
| **IMP-11 — orphaned `AvaloniaBubble` control** — **DONE 2026-07-10 (`28fa06a2`)**: `new AvaloniaBubble(` had 0 call sites; chaos bubbles render via the compositor `BubbleLayer` (`AvaloniaBubbleService.cs:533-535` routes `SetFuse` there); its per-call `new SolidColorBrush` pattern read as a hot-path smell on every scan | `CCP.Avalonia/Chaos/AvaloniaBubble.cs` (274 lines) DELETED | deleted dead UI code + a misleading allocation pattern; simplification, not runtime speed | MECHANICAL | confirm-then-deleted: verified no XAML / resource-factory / reflection construction (sealed Panel, parameterized ctor, no typeof/Activator/nameof, no paired .axaml, glob-included) |

**Improvement-row claim ledger (append-only):**
- **EVALUATED → DECLINED 2026-07-10 · @driver (continuous-mode session):** IMP-8 (coalesce the two 30s
  save timers). Read both timers (`AchievementService.cs:86`, `QuestService.cs:71`): structurally identical
  (`new DispatcherTimer { Interval=30s }` → `OnAutoSaveTick`) but owned by two cleanly-independent Core
  progression services with separate state, files, dirty flags, and lifecycles. JUDGMENT: coalescing would
  introduce a new shared save-coordinator abstraction coupling the two (shared failure point, DI/disposal
  ordering, cross-service flush interference) for a negligible gain — both timers are already Background
  priority at 30s and OS-coalesced, so "one fewer wake" is noise on modern hardware. The row itself tags it
  "flag, don't force / not headline perf"; the coupling cost outweighs the benefit → DECLINED. Recorded the
  better-scoped alternative (extract a stateless `AtomicJsonStore` tmp-write helper, no timer coupling) for
  a future session if the boilerplate duplication becomes a maintenance burden. Docs-only; no code touched.
- **CLAIM+DONE 2026-07-10 · @driver (continuous-mode session):** IMP-2 (engine tick priority). Avalonia-API
  behaviour claim → ran the mandatory `avalonia-research` protocol: local docs had it as an unverified
  hypothesis, so verified against the PINNED package's own source of truth — Avalonia 12.0.5
  `Avalonia.Base.xml`: `DispatcherTimer.#ctor` (parameterless) “process the timer event at background
  priority”; `DispatcherPriority.Render` = “same priority as render” (above Input/Background per the enum
  docs). Changed `new DispatcherTimer { Interval=... }` → `new DispatcherTimer(DispatcherPriority.Render)
  { Interval=... }` (engine `_timer` only; the one-shot stagger timers left at Background — out of scope).
  Clean before/after `--benchmark`: **Idle 122.6→141.6 fps (min 118→136), Active 123.2→144.2 fps
  (min 117→129)** — ~+15–17% avg AND higher minimums (the starvation the row predicted). Gates: slnf **0
  err** (383 warn) · WPF sln **0 err** · Core **542/542** · `--verify-layers` PASS · `--smoke-test` 44 tabs /
  0 unhandled / Findings 19 = recorded benign drift (the 44 input-driven tab navigations completing shows
  Render>Input priority does NOT starve input). Row #2's fresh-hypothesis note updated to CONFIRMED+FIXED;
  the LibVLC decode-stall component of MinFps=0 remains separate under row #2. No subagent JUDGMENT review
  dispatched: the sole judgment (default-priority semantics + Render choice) was resolved directly against
  the authoritative pinned-package XML docs and empirically confirmed by the paired benchmark.
- **CLAIM+DONE 2026-07-10 · @driver (continuous-mode session):** IMP-6 (chain VideoLayer teardown tasks).
  Native-lifetime/compositor-internal crash-safety → dispatched the mandatory JUDGMENT-tier adversarial
  review (`claude-fable-5`, read-only, 6 attack vectors) on the diff: **VERDICT SHIP** — drain correct
  across N rapid stops (root `WhenAll` completes only when every leaf frees; the discard branch requires
  monotonic `IsCompleted==true`), no deadlock (`_teardownTask` UI-thread-only; deferred bodies pool-only,
  capture no UI state), no unbounded growth (chain collectible once root completes; guard prevents accreting
  done antecedents), single-play path byte-identical, no double-free (each teardown captures disjoint locals
  nulled under `_bufferLock`; the all-null guard skips redundant stops), faulted antecedents still fully
  awaited. Fix: `_teardownTask = previous is {IsCompleted:false} ? Task.WhenAll(previous,this) : this`.
  Gates: slnf **0 err** (383 warn) · WPF sln **0 err** · Core **542/542** · `--verify-video` logic PASS
  (frames advance 14→25, 3 monitors) · `--smoke-test` 44 tabs / 0 unhandled / Findings 19 = recorded
  benign drift only (1 StartSession baseline + 4 ChaosRun telemetry + 13 availablesubjects loc-keys + 1
  ConnectCommand; ZERO video/teardown findings). NOTE: `--verify-video` SIGSEGV-on-exit in 1/3 runs with
  the tiny broken calibration.mp4 (LibVLC mjpeg/converter errors) is a PRE-EXISTING native-teardown flake
  on the byte-identical single-play path — review-confirmed independent of this diff; 2/3 runs exit 0.
- **CLAIM+DONE 2026-07-10 · @driver (continuous-mode session):** IMP-9 (BubbleEngine per-tick allocs).
  State-mutating chaos logic → dispatched the mandatory JUDGMENT-tier adversarial review (`claude-fable-5`,
  read-only, 6 attack vectors) on the diff: **VERDICT SHIP** — the ONE shared snapshot is provably
  equivalent to the two re-snapshots because `PopBubble` is idempotent (early-returns on already-`IsPopping`)
  and sets `IsPopping` before any removal, and every hazard-pass victim loop guards on `IsPopping`; no code
  mutates `_bubbles` between the main loop and the two passes; darters cannot be popped mid-tick. Applied 4
  reviewer hardenings (all behaviour-neutral): the shared-snapshot copy is now gated on `_chaosActive &&
  !_chaosFrozen` (ambient ticks skip it, matching pre-IMP-9); the 3 buffers are cleared in `Stop()` (no
  post-run bubble rooting); an `IsPopping` guard added to the trail-pop outer loop; the non-reentrancy
  requirement documented on the buffer fields. Gates: slnf **0 err** (383 warn) · WPF sln **0 err** · Core
  **542/542** (BubbleEngine tests) · `--smoke-test` 44 tabs / 0 unhandled / Findings 5 (logged-out baseline)
  with the in-run ChaosRun completing on correct economy (score 209, sparks 107→131, xp advanced — exercises
  the changed tick path). Core-only; no compositor/video path. **FILED a new row from the review's nit #5:**
  IMP-ECON1 (pre-existing latent economy double-pay, out of IMP-9 scope).
- **CLAIM+DONE 2026-07-10 · @driver (continuous-mode session):** IMP-4 (cache per-frame render-path
  paints). Touches compositor render internals → followed `unified-compositor-engine` skill (read VideoLayer's
  protocol block first) and dispatched the mandatory JUDGMENT-tier adversarial review (`claude-fable-5`,
  read-only) on the diff: **VERDICT SHIP** — pixel-identical output, correct sigma-change guard
  (`_blurSigma=NaN` first-build), correct SKColor struct change-detector for `BackgroundColor`, blur access
  serialized under `_frameLock`. VideoLayer: cached `_bgPaint`/`_bgPaintColor`, rebuild only when
  `BackgroundColor` changes (MandatoryVideoLayer sets it post-base-ctor → lazy rebuild required). BrainDrain:
  cached blur paint+filter (rebuild on sigma change) + reused tint paint (Color mutated in place). Corrected
  a comment nit the reviewer caught (the lock-free tint fallback's teardown safety rests on shutdown ordering
  + intensity gate + engine try/catch, NOT `_frameLock`). Gates: slnf **0 err** (383 warn) · WPF sln **0 err**
  · Core **542/542** · `--verify-video` PASS (frames advance 15→25, 3 monitors) · `--verify-layers`
  BrainDrainLayer PASS (NO-DELTA + capture-exclusion OK) · `--smoke-test` 44 tabs / 0 unhandled / Findings 18
  = recorded benign drift · `--benchmark` Idle **122.6fps** (min 118) / ActiveSession **123.2fps** (min 117),
  no regression (single run; before/after vs benchmark-optimized.json is environmentally invalidated per
  row #2, and `--benchmark` skips the MaxIntensity phase). OBSERVATION for a future row (reviewer-surfaced,
  pre-existing, out of IMP-4 scope): `VideoLayer.Dispose()` has no caller — `AvaloniaVideoService.Dispose`
  only calls `Stop()`+`WaitForTeardown()`; the added `_bgPaint` disposal is correct-if-called and otherwise
  a process-lifetime single object (still a strict win). Related to but distinct from IMP-6.
- **CLAIM+DONE 2026-07-10 · @driver (continuous-mode session):** IMP-10 (delete dead `ActiveRipples`
  getter). Grounded live: repo-wide `grep ActiveRipples` = the declaration line only, ZERO readers (the
  "for optional overlay rendering" consumer was never ported — ripple/residue/tether are seam-only per the
  `--verify-layers` note). Not on any interface (concrete `BubbleEngine` getter) → no JUDGMENT trigger.
  Deleted the getter + its doc comment; left `_ripples`/`RIPPLE_LIFE_MS` (14 remaining refs) and the
  Size-Queen ripple simulation (`:1434-1445`) fully intact — removed dead EXPOSURE, not gameplay state.
  Gates: slnf **0 errors** (383 warn, Linq still used elsewhere) · WPF sln **0 errors** · Core **542/542**
  (BubbleEngine unit tests green) · `--smoke-test` 44 tabs / 0 unhandled / **Findings 5** (logged-out
  baseline this run). Core-only, no compositor/video path → `--verify-layers/video` not triggered.
- **CLAIM+DONE 2026-07-10 · @driver (continuous-mode session):** IMP-7 (remove dead
  `IV2AuthService.SendHeartbeatAsync(string)`). Interface-member removal → dispatched the mandatory
  JUDGMENT-tier adversarial review (`anthropic/claude-fable-5`, read-only) on the actual diff: independently
  re-verified ZERO callers of the one-arg method (all repo hits are the DISTINCT no-arg
  `ProfileSyncService.SendHeartbeatAsync()` real-heartbeat path, or the WPF frozen head's own copies),
  single implementer (`AvaloniaV2AuthService`, no test mocks), no reflection/serialization/DI dependency,
  WPF head untouched — **VERDICT: SHIP** (invariant strengthened). Removed the interface member, the impl
  (−26 lines incl. its doc block), and refreshed the stale DI comment; the no-arg ProfileSyncService
  heartbeat + its 3 Core tests are untouched. Gates: slnf **0 errors** (383 warn) · WPF sln **0 errors** ·
  Core **542/542** · `--smoke-test` 44 tabs / 0 unhandled / Findings 18 = recorded benign drift only
  (StartSession baseline + 12 availablesubjects loc-key misfires + ConnectCommand + ChaosRun telemetry;
  ZERO auth/heartbeat findings). No compositor/video path touched → `--verify-layers/video` not triggered.
- **CLAIM+DONE 2026-07-10 · @driver (continuous-mode session):** IMP-5 (delete dead dialog-mode seam).
  Grounded live: `PushDialogMode`/`PopDialogMode` callers repo-wide = only `AvaloniaDialogService.cs` (12
  sites) + the two defs; `_dialogModeRefCount` only inc/dec/reset, never read for logic (confirmed no-op,
  matching its own "kept for API compatibility but is a no-op" doc comment); only construction site is the
  DI factory `ServiceCollectionExtensions.cs:127`. Removed: the field + both methods from `CompositorEngine`;
  the `_compositor` field, ctor param, `using ...Compositor;`, and stale class-doc line from
  `AvaloniaDialogService`, unwrapping all 6 try/finally brackets (bodies unchanged — finally only called the
  no-op Pop); dropped the `CompositorEngine` DI arg. Residual grep 0 hits. Gates: slnf **0 errors** (383
  warn, unchanged — `System.Threading` still used elsewhere in CompositorEngine) · WPF sln **0 errors** ·
  Core **542/542** · `--verify-layers` exit 0 (one flaky FAIL on the NOISY-baseline screenshot-diff harness
  cleared on re-run — dialog no-op removal cannot touch rendering) · `--smoke-test` 44 tabs / 0 unhandled /
  Findings 18 = recorded benign drift set only (1 StartSession baseline blocker + 4 ChaosRun info telemetry
  + 12 availablesubjects loc-key misfires on server subject tags + 1 ConnectCommand null-DataContext;
  higher count than the recorded 16 is server-content-coupled — the authed server directory returned more
  subject tags this run, exactly as the drift record predicts; ZERO dialog/compositor/DI findings). No
  `port-parity-auditor` dispatched: WPF has no `Push/PopDialogMode` equivalent (Avalonia-invented
  compatibility no-ops), so there is no WPF behavior contract at stake; full-app smoke resolves
  `IDialogService` via DI across 44 tabs, proving the wiring.
- **CLAIM 2026-07-10 · wip @driver (continuous-mode session):** IMP-11 (orphaned `AvaloniaBubble`
  confirm-then-delete). Claimed as topmost of the LIVE claim-priority order. Confirm sweep before deletion:
  word-boundary grep for `\bAvaloniaBubble\b` across `**/*.cs`/`**/*.axaml`/`**/*.xaml`/`**/*.json`
  (excluding `AvaloniaBubbleService`/`AvaloniaBubbleWindow`) = only the class decl + ctor inside the file
  itself; zero `new AvaloniaBubble(` call sites; zero `typeof/Activator.CreateInstance/nameof`; no paired
  `AvaloniaBubble.axaml`; no explicit `.csproj` include (Avalonia glob); `sealed` (not a base class); all
  members instance-only (no statics consumed elsewhere). Precondition satisfied → deleted.
- **DONE 2026-07-10 (same session, commit `28fa06a2`):** `git rm CCP.Avalonia/Chaos/AvaloniaBubble.cs` (274 lines,
  dead UI control). Residual grep 0 hits. Gates: slnf **0 errors** (warnings 384→383, one fewer from the
  removed file) · WPF sln **0 errors** · Core **542/542** · `--smoke-test` 44 tabs / 0 unhandled /
  **Findings 5** (logged-out baseline this run — StartSession blocker only, exit 0). No compositor/video
  path touched (AvaloniaBubble was a standalone `Panel`, not a registered layer — `BubbleLayer` is the
  live compositor bubble path and is untouched), so `--verify-layers/--verify-video` not triggered.
- **CLAIM 2026-07-10 · wip @driver (workflow-run, continuous-mode session):** IMP-1 (VideoLayer `ConsumeDirty`
  override). Claimed after R-scrub `820526d5` + bubble-border fix `6346d964` closed out. Gate note: smoke
  runs at Findings 16 = recorded pre-existing drift (owner-waved; see drift row). Plan: recon dirty-path
  contract → implement override (~10 lines, UI-tick-only state) → --verify-video + --verify-layers →
  parity audit → full gates → one commit.
- **DONE 2026-07-10 (same session):** `VideoLayer.ConsumeDirty()` override landed — one file, +29/−1:
  UI-tick-only `_dirty` set on FRONT↔READY present (`Update`, :537) and on `Stop()` (:335), one-shot
  consumed (PinkTintLayer idiom); `IsActive => _bufferValid || _dirty` one-tick linger so the post-Stop
  clear is consumable. Invalidation now tracks the ~25-30fps present rate instead of unconditional 60Hz
  (the row's ~halved GPU passes during plain video playback); `MandatoryVideoLayer` inherits free (sealed,
  no extra compositor-painted elements — audited). BONUS: fixes a latent stale-frame trap — video stop
  under a co-active persistently-static layer (constant PinkTint) previously left the last frame trapped
  on screen forever (engine only idle-invalidates at activeCount==0). Adversarial audit: SHIP (all four
  failure classes clean at line level; threading UI-tick-only confirmed; no engine edits). Evidence:
  `--verify-video` with a real clip PASS (first frame published, frames advancing 14→25/~700ms, 3/3
  monitors) · `--verify-layers` PASS · slnf 0 · WPF sln 0 · Core 542/542 · smoke 44 tabs / 0 unhandled /
  findings delta-set ⊆ recorded drift (see drift row; count flapped 16→15 exactly as predicted —
  server-side 'drone' tag absent this run, proving the count is server-content-coupled; delta-set check,
  not count-equality, is the real signal while the smoke env is authed).

### IMP-ECON1 — latent chaos economy double-pay: defused/detonated lives never set `IsPopping` · **JUDGMENT** — **DONE 2026-07-10 (this session)**

Filed 2026-07-10 by the IMP-9 JUDGMENT review (nit #5; PRE-EXISTING, not caused by IMP-9). In
`CCP.Core/Services/Chaos/BubbleEngine.cs`, three terminal live-resolution sites set `IsDetonated`/`IsDefused`
but NOT `IsPopping`, so a same-tick player-ripple hazard pass re-popped the resolved live via `PopBubble`'s
`_onBenignPop` reward branch — an economy double-pay.
**FIXED:** `wpf-archaeologist` extracted the WPF ground truth — WPF sets `_isPopping = true` synchronously
BEFORE every economy callback (`CompleteDefuse BubbleService.cs:3688-3689`, `Detonate :3960-3961`), guarded
by `if (!_isAlive || _isPopping) return;`; `_isPopping` is WPF's single resolution latch (no separate
IsDefused/IsDetonated), removal deferred (corpse lingers). Ported that invariant: set `bubble.IsPopping =
true` before the callback at all three sites — `DetonateBubble` (:643), channel-defuse (:1346), fuse-out
detonation (:1381). Added pinning test `ImpEcon1_DetonatedLive_IsNotRePaidBySameTickPlayerRipple`
(`BubbleEngineParityTests.cs`) that reproduces the exact same-tick sequence (fuse-out → ripple → missed-
removal): FAILS pre-fix ("Collection was not empty"), passes post-fix. `port-parity-auditor` VERDICT SHIP —
also a WPF-parity improvement (corpse now freezes instead of spuriously moving/missing) + closes a sibling
double-detonate hole; no consumer needed the old unlatched corpse. Gates: slnf 0 · WPF sln 0 · Core
**543/543** (floor 542→543) · smoke 44 tabs / 0 unhandled / in-run ChaosRun economy correct.
- **Residual (low-pri follow-up, non-blocking):** the pinning test covers site 3 (fuse-out); sites 1
  (DetonateBubble) + 2 (channel-defuse) share the identical one-line latch and were structurally verified by
  the auditor but are not independently pinned — a channel-defuse test variant is cheap insurance if a future
  session touches this area.

Scan verdicts recorded as explicitly GOOD (checked, no action): VideoLayer's triple-buffer frame path
matches its zero-alloc spec (index-swap locks, zero-copy `SKImage.FromPixels`, stale-session guards);
z-order snapshot cached (rebuilt only on Register/Unregister); static-frame skip + idle watchdog + epoch
guards real; `FlashImageCache` memory policy correctly ported (LRU + GIF caps + ref-count lease — do NOT
"improve"); `AvaloniaMouseHook` disciplined; event-handler `+=`/`-=` hygiene clean in Avalonia transient
surfaces; `SettingsService`'s Newtonsoft use is load-bearing (member-level `Error` recovery contract) and
NOT worth an STJ swap.

---

## DEFER (standing backlog)

### #9 — standing DEFER rows

Each has a detail pointer; claim only in a focused session.

- **Ditzy Data PRO analytics UI** (~832 LoC charts/panels) — progression area; deferred from WS0 lot 7.
- **Discord Rich Presence** — entirely unported (WS0 lot-2 C6 confirmed); the `DiscordRichPresenceEnabled`
  toggle is a dead switch and the NuGet sits unused in `CCP.Core.csproj`. Transport IS cross-platform
  (named-pipe/unix-socket); recommended minimal Core `IDiscordPresence` lifecycle seam for
  session/idle presence, then the per-effect call-site wirings.
- **CompanionTab follow-ups** — OpenAI key-entry UI (the Avalonia `CompanionTabView` has NO AI provider
  config surface at all; full provider-config SURFACE needed — selector/endpoint/model/key-via-`ISecretStore`/
  test-connection); global chat hotkey runtime-toggle needs a restart (`RegisterChatGlobalHotkey` runs at
  init only — minor).
- **AvatarTube inbound emote routing** (skimmed from the deleted `CCP.Avalonia/AvatarTube/TODO.md` on
  2026-07-10) — outbound `SendEmoteAsync` IS wired (`RemoteControlTabViewModel`), but an inbound remote
  emote command is NOT routed to the active `AvatarTubeWindow` (no emote action in
  `AvaloniaRemoteCommandExecutor`). Needs a `wpf-parity` contract on whether the WPF head played inbound
  remote emotes on the tube before this is a parity gap vs. a future enhancement.
- **Calibration 16-point window pipeline** (~1300–1500 LoC) — the per-frame algorithm is already in Core
  (`837aaa1d`). CORRECTED 2026-07-10: the "3 fake-success shells" note was STALE — one calibration
  window exists in CCP.Avalonia, `WebcamCalibrationWindow` (708 LOC), and it now collects iris samples,
  fits the polynomial (fit-quality gate `WebcamCalibrationWindow.axaml.cs:298`), and commits calibration
  (S1c `df06d06d`); the port-plan marks the old "non-functional shell" note RESOLVED (the "3" was the 3
  tab callers opening the one window). Remaining scope = the full 16-point window pipeline. Detail:
  [`webcam-calibration-port-plan.md`](webcam-calibration-port-plan.md).
- **WS0 lots 7–11 residual P2/P3 findings (effect-seam-blocked):** whisper-audio busy gate (A1-15, blocked
  on the subliminal whisper linked-audio port); Bambi-Freeze pre-roll around triggered videos (V1-7, needs
  `ISubliminalService` freeze seam); scheduler web-video defer (V1-21, needs autonomy web-video flag);
  gaze attention-check revival kit (T1-1, dormant per WPF contract — revive only on a product call).

---

## SHIPPED ledger

One line per completed item; hashes are inlined here as evidence (captured from `git` / the live gate runs
at docs-rework time, 2026-07-10). This is evidence, not a claim surface — do not edit unless correcting a
hash. Re-read hashes live from `git` before re-claiming them.

| Workstream | Evidence |
|---|---|
| **WS0 verify-and-correct sweep — COMPLETE** (all 11 lots passed; parity rows 1–11; every merge-`5ce70de6` re-open re-closed) | ProfileSync slice 7 s7a `4f051ab0` / s7b `80e1442`; slice-6 economy bug caught+fixed pre-commit `766d8322`; #462 interaction-race pair `fb704a6d`; Core test floor 108 → **542/542** |
| **WS1 video through the compositor (Windows) — COMPLETE incl Phase E** (DoD #2 ✅) | A `85fa6570`, B `bbdb3077`/`99a50721`, C `07c094e1`, D `37bd454a`, E1 `6180efc2`, E2 `ed636a7c`, E3 `8069cfb7` (legacy `AvaloniaMultiMonitorVideoService` DELETED, grep 0 matches; compositor `VideoLayer`/`MandatoryVideoLayer` are the only video path) |
| **Chaos run engine (WS2/WP3) S1–S9 — COMPLETE** (handoff Q1–Q5 done, user-verified) | S1–S4 `2d7bc384`, S5 `490da8c6`, S6 `f5fa0757`, S7 `87515732`, S8 `f0fea4a0`, S9 `1f4c19fc`/`e61633c0` |
| **8 passive chaos overlays → layers** (`--verify-layers` 15/15) | cursor-glow `0624d639`, pop-text `a8bf6f10`, banner `798b6e64`, announcer `3df5cda7`, flash-wash `0e64e4e5`, DVD `35418baa`, gif-cascade `4c6c5992`, field-FX `9fc0b420` |
| **Dead passive windows deleted** | ChaosFxWindow→ChaosFxLayer `8df68031` (Z=118), ChaosWaveTimerOverlay→ChaosWaveTimerLayer `16fe5a92` (Z=155), AvaloniaBubbleWindow `c8bb20a1`; E-Stim arc → `ChaosEStimArcLayer` (Z=125) `05520f52` |
| **22-layer UCE lane — COMPLETE** (DoD #4 ✅; 9 session + 12 chaos + 1 attention-check = 22 registered `IAvaloniaLayer`s) | last LIVE passive effect migrated `57f6f048`; no passive effect window remains in `CCP.Avalonia` |
| **Companion AI — all three transports ported** | cloud `61ca0d1`, local/Ollama `2bd37899`, OpenAI `ca873d25`; AI-command dispatch `70cf9803`/`9fa09853`/`424ea528` (cloud faithfully omits); `IModerationLog` wired `b3b8da4`; `SystemPromptBuilder` parity `b84eb90` |
| **v6.2.11 sync** | merge `cd2ff1f9` (+ChaosImagePool facade fix + DtrhHostService `using` fix); all heads → 6.2.11; quiz #501 + speech #505 ported to Core; bark floor N/A (no rule gate); trigger-bubble settings already ported |
| **main → feat/crossplatform sync (DTRH dollhouse rewrite)** | merge `a06509eb` (main `6e55bcc3`; 41 commits, 620 files, +11,075/−1,953). Brought the web-era DTRH rewrite (in-ambient 3D hub, gold economy SchemaVersion 3, Four Chambers, journey rooms + 16 biomes, junction v6, duo-boon wave; engine JS under `Resources/web/dtrh/{engine,game,shared,vendor}` + 458 barks), the pre-6.3 bugfix batch (#518/#521/#514/#516/#512/#500), and the support-chat 0710 batch (video overlap arbitration, lockdown keys, presets, Discord share, settings backups, DtRH boot deadline). Clean merge, no conflicts. Gates: slnf 0 err (384 warn) · WPF sln 0 err · Core **542/542**. WPF-head content — not yet ported; row #6 re-inventoried against this tree. Old "The Fall" plan scrapped |
| **v6.2.9 #5** interaction-queue slot-leak guard | `f4a556a` |
| **Bubble white-border fix** (user-reported 2026-07-10): deleted the port-invented unconditional 2px white stroke ring in `BubbleLayer.RenderBubble` (old L292-294) — no WPF equivalent (WPF strokes ONLY in the image==null fallback, `BubbleService.cs:2710`); stroke relocated into the Avalonia fallback branch for exact parity. Fixes ambient + chaos bubbles in one shared paint path; 3/3 independent verifiers + parity audit SHIP; all 7 bubble render paths swept clean. Residual filed: mod-resolver row above | this session 2026-07-10 (`fix(av)` commit; gates: slnf 0 · WPF sln 0 · Core 542/542 · verify-layers exit 0 · smoke Findings 16 = recorded drift) |
| **Wins vs WPF** (recorded; WPF halves UNBACKED) | Avalonia recorded: startup ~2.0s (`benchmark-optimized.json` `MainWindowShownMs` 1976.9; better than the previously claimed 2.5s), working set ~422MB (`perf-avalonia.json`); chaos FPS-floor 2026-07-05 AvgFps 138.7 ≫ 30 floor (MinFps=0 caveat → open row #2). UNVERIFIED (2026-07-10): "~4.2s / ~1218MB WPF" — evidence gap: NO recorded WPF benchmark artifact exists anywhere in the repo; re-measure the WPF head before citing a vs-WPF win |
| **Pre-WS0 foundation** (Core carve-out, DI, theming, tabs/dialogs, smoke harness) | WPF→`CCP.Core` reference collapse + WPF `Models/` deleted; `Microsoft.WindowsAppSDK` pinned; 5-theme reskin (dashboard-design lit/unlit borders); ~44 tabs + ~40 dialogs ported; `--smoke-test` harness (44 tabs, Findings 5 baseline); Buttplug.io haptics; cross-platform audio-device detection |

---

## Triage inbox

SWEEPER deposits any genuinely-open item found while skimming the 11 TODO-debris files deleted in the
2026-07-10 docs rework. BOARD triages each to an OPEN row above or explicitly closes it with a reason.
**Skimmed all 11 on 2026-07-10; 8 were fully complete (every item checked), 3 source files deposited 4
items below — all triaged.**

| Deposited by | File → item | Why it might be open | Disposition |
|---|---|---|---|
| SWEEPER 2026-07-10 | `CCP.Avalonia/AvatarTube/TODO.md` → “Cross-platform z-order/always-on-top edge cases on Linux/macOS” (Windows HWND path implemented; corrected 2026-07-10: a Linux/macOS `Topmost`-pulse+`Activate()` fallback ALSO exists — `AvatarTubeWindow.ChatInput.cs:128-133` — so "HWND path only" was imprecise) | Linux/macOS always-on-top not verified (no test record) | **CLOSED → row #5** (Linux bring-up epic; X11/Wayland topmost + click-through mechanism in `crossplatform-rebuild-plan.md` WS4). |
| SWEEPER 2026-07-10 | `CCP.Avalonia/AvatarTube/TODO.md` → “Remote emote command routing from `IRemoteControlService` to the active tube” | Outbound `SendEmoteAsync` exists (`RemoteControlTabViewModel`); inbound command→tube play is not implemented (`AvaloniaRemoteCommandExecutor` has no emote action) | **→ new DEFER row** (see #9 “AvatarTube inbound emote routing” — needs `wpf-parity` contract). |
| SWEEPER 2026-07-10 | `CCP.Avalonia/Views/Deeper/TODO.md` → “engine/effect integration stubbed; `EnhancementHostService`/`IPlaybackTimeSource` not yet in Core” | TODO premise is **OUTDATED**: `EnhancementHostService` + `IPlaybackTimeSource` ARE in `CCP.Core/Services/Deeper/`, `AvaloniaLibVlcTimeSource` implements the interface, and `EnhancementPlayerWindow` resolves the host via DI (`ServiceCollectionExtensions.cs:315`) | **CLOSED** (premise outdated since the Core migration). Residual narrow stub: `AvaloniaSpeakPromptHost` time-source not yet wired (its own comment, `…/Deeper/AvaloniaSpeakPromptHost.cs:22`) — a speak-prompt sub-item, not a shipped-feature stub; left for the Deeper/calibration backlog. |
| SWEEPER 2026-07-10 | `CCP.Avalonia/TODO-avalonia-port-batch.md` → “What Remains”: real tab views for placeholder VMs, Core seam extraction, browser web-view, profile/Discord viewer, lockdown wiring | Broad sweep, not a discrete gap | **CLOSED → rows #4/#5** (Windows/Linux sweeps) + parity-matrix DEFER bullets (companion/browser/Discord). |
