# Unified Compositor Engine (UCE) — Plan & Goals

> Status: **in progress / not yet at parity.** This doc is the working plan to finish the UCE so
> the Avalonia head renders all video + overlays through one Skia compositor and the legacy
> multi-window / multi-monitor services can be deleted. It is the status tracker the design skill
> (`.pi/skills/unified-compositor-engine/SKILL.md`) is not.
>
> The ground-truth migration doc (`crossplatform-rebuild-plan.md` §1A) does **not** currently track
> the compositor — the only real status lives here and in the code.

## Goal (north star)

One `CompositorEngine` driving **one full-screen, click-through, topmost `CompositorWindow` per
monitor**, into which every visual effect renders as an `IAvaloniaLayer` on a single Skia surface:
video (regular + mandatory), flash, subliminal, bouncing text, bubbles, brain-drain, spiral, pink
tint, lock card, and (later) chaos overlays.

**Done means:**
1. Every overlay/video effect renders through the UCE with **1:1 behavioral parity** with the WPF head.
2. No effect creates its own `Window`; no per-service `Topmost` / `SetWindowPos` z-fighting.
3. The legacy `AvaloniaMultiMonitorVideoService` and per-overlay `*Window` classes are **deleted**.
4. It is **at least as fast and lighter** than the multi-window approach (the whole reason for the
   rewrite: bounded memory, no per-effect window/compositor overhead).

**Non-goals (for now):** replacing LibVLC as the decoder (Phase 3b in the skill — stays interim),
the unified audio mixer (skill Phase 5), Android.

## Current state

| Piece | State |
|---|---|
| `CompositorEngine` 60 Hz loop + per-monitor windows | ✅ working |
| `CompositorControl` + `ICustomDrawOperation` render-thread path | ✅ wired (engine invalidates each tick) |
| Click-through (`WS_EX_TRANSPARENT` + `WM_NCHITTEST` subclass) | ✅ working; `WS_EX_LAYERED` correctly removed |
| Spiral, pink tint, brain-drain layers | ✅ render (spiral full-screen fix landed) |
| Full-screen coverage (window uses `screen.Bounds`, taskbar incl.) | ✅ landed |
| **Mandatory / regular video layer** | ✅ renders + performs (Phase A harness-proven; Phase D.1/D.2 perf pass landed 2026-07-04: zero per-frame alloc, engine-driven tick) |
| Video audio: volume / output-device / mute control | ✅ done (Phase B1) — `ApplyAudioSettings` at start + live `ApplyVolume` on slider; re-verified 2026-07-05 `--verify-video` ("Audio applied, player volume 100, effective 100") |
| Mandatory-video attention checks / duration / safety timer / segment mode | ✅ done (Phase B2) — orchestration runs on the layer (`OnCompositorVideoStarted` → `BeginPlaybackOrchestration`: ExtendTimeout + safety timer + attention arm; `LengthKnown` upgrades timeout; natural end runs full `OnVideoEnded`); attention checks rehomed to `AttentionCheckLayer`. Re-verified 2026-07-05 `--verify-video` ("layer orchestration armed = True") |
| Legacy `AvaloniaMultiMonitorVideoService` | ✅ DELETED (Phase E3, `8069cfb7`) — class + `IMultiMonitorVideoService` + `VideoOverlayWindow` gone (0 grep matches); compositor `VideoLayer`/`MandatoryVideoLayer` are the only video path, no `CCP_LEGACY_VIDEO` gate |
| Flash / subliminal / bouncing-text / bubbles / lock-card on UCE | ✅ harness-verified 2026-07-04 (`--verify-layers`, Phase C): flash/subliminal/bubbles/bouncing + pink/spiral/brain-drain all register/activate/render/tear down; dual-surface capture affinity asserted both directions. Lock-card is NOT a layer (still a window) — truthfully out of scope until migrated |
| Chaos overlays | ✅ migration COMPLETE (WS2/WP3): TWELVE passive chaos overlays are layers now, harness-verified — `ChaosFieldFxLayer` (Z=100) + `ChaosDvdLayer` (Z=105) + `ChaosGifCascadeLayer` (Z=110) + `ChaosFlashWashLayer` (Z=115) + `ChaosFxLayer` (Z=118, vignette pulses — migrated 2026-07-05 from `ChaosFxWindow`) + `ChaosCursorGlowLayer` (Z=130, the template) + `ChaosEffectBannerLayer` (Z=140) + `ChaosPopTextLayer` (Z=145) + `ChaosAnnouncerLayer` (Z=150) + `ChaosWaveTimerLayer` (Z=155, wave/clock/score pill — migrated 2026-07-05 from `ChaosWaveTimerOverlay`) + `ChaosEStimArcLayer` (Z=125, E-Stim arc bolts) + `ChaosVibeTrailLayer` (Z=128, vibe-pop cursor trail) + `AttentionCheckLayer` (Z=160); ALL 4 formerly-unwired passive windows (EStim/EStimGlow/VibeTrail/SkiaFx) DELETED 2026-07-05 (dead code removed so they can't be re-wired). Remaining: only the interactive set (hook click-swallow gap). Full coverage audit: `docs/uce-coverage-audit.md`; queue + recipe in Phase F below |

## Avalonia v12 idiomatic confirmation (researched, not from memory)

Confirmed against the official docs + Avalonia repo (Avalonia 12.x):

1. **Custom Skia rendering is `ICustomDrawOperation` + `ISkiaSharpApiLeaseFeature`.** Inside
   `Render(ImmediateDrawingContext)`: `context.TryGetFeature<ISkiaSharpApiLeaseFeature>()` →
   `using var lease = feature.Lease()` → `lease.SkCanvas`. This is exactly what `CompositorDrawOp`
   does — the current approach is correct. ([custom-rendering docs](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering))

2. **`Control.Render` runs on the UI thread; `InvalidateVisual()` is the documented way to request a
   redraw.** A UI-thread `DispatcherTimer` calling `InvalidateVisual()` each frame (current engine)
   is valid but couples the frame loop to the UI thread. There is a known issue where
   `ICustomDrawOperation.Render` isn't re-invoked without an explicit invalidate
   ([#12247](https://github.com/AvaloniaUI/Avalonia/issues/12247)) — our per-tick invalidate is the
   workaround.

3. **The more idiomatic continuous-render primitive is `CompositionCustomVisualHandler`.** Its
   `OnRender` runs **on the render thread**, self-drives via `sender.RequestNextFrameRendering()`,
   and receives UI→render data via `SendHandlerMessage()` / `OnMessage`. This decouples the
   compositor frame clock from the UI thread — the right long-term target for a 60 Hz video
   compositor. ([custom-rendering docs](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering), [examples](https://github.com/wieslawsoltes/CustomDrawingAvaloniaExamples))

4. **`SKCanvas.DrawBitmap(SKBitmap, …)` recreates an `SKImage` on every call** (the shim added after
   the old `DrawBitmap` was removed; Avalonia fixed the equivalent in
   [PR #18164](https://github.com/AvaloniaUI/Avalonia/pull/18164)). `VideoLayer.Render` currently
   does exactly this per frame → draw a **persistent `SKImage`** instead, and stop allocating a new
   `SKBitmap` per frame in `OnRenderTick`. **(Landed 2026-07-04, Phase D.1 — see below.)**

5. **Click-through transparency has no built-in Avalonia support — native P/Invoke is required**, and
   `WS_EX_LAYERED` + `UpdateLayeredWindow` is **incompatible with GPU rendering on Windows**. This
   validates `CompositorWindow.ApplyNativeTransparency` keeping `WS_EX_TRANSPARENT` and dropping
   `WS_EX_LAYERED`. Linux/X11 needs the **XShape** extension; Wayland is compositor-specific
   (`crossplatform-rebuild-plan.md` §870 already flags this). ([#11911](https://github.com/AvaloniaUI/Avalonia/discussions/11911), [#13827](https://github.com/AvaloniaUI/Avalonia/discussions/13827))

## Plan — phased, parity-first

Order matters: **prove UCE video → reach parity → flip default → delete legacy.** Do not delete the
fallback before the replacement is proven (deleting now = no working video).

### Phase A — Make UCE video render (unblock everything)
- [x] Wire loggers into `VideoLayer` / `MandatoryVideoLayer` (done) + `EncounteredError` + first-frame log.
- [x] Reproduce a mandatory video; read `VideoLayer:` log lines to bisect. **DONE 2026-07-04 via the new `--verify-video <path>` harness** (`VideoVerification.cs`, mirrors `--verify-spiral`; Program.cs sets `CCP_UCE_VIDEO=1` pre-DI). Bisect verdict: **no broken stage remains** — layer registers (Z=15), LibVLC vmem callbacks fire, first frame copies (1920x1080), engine composites, frames advance 49→70 across 700ms (live 30fps), exit 0. The "does not render" premise was stale (docs lagged the code; earlier logger-wiring fixes evidently resolved it and it was never re-tested).
- [x] Fix the identified root cause. **No root cause exists** — the path is functional (see harness PASS above). Probes added for regression-proofing: `VideoLayer.HasRenderedFrame` + `FramesCopied` (monotonic counter).
- [~] **Acceptance:** a mandatory video plays full-screen through the compositor, no legacy `VideoOverlayWindow` — **proven by harness** (frame pipeline + engine windows + no legacy window creation; legacy skip guards code-verified at `AvaloniaVideoService.cs:846/:862`). `[~]` not `[x]`: "VISIBLY" (pixel-level on-screen check) is deferred to Phase B's side-by-side WPF parity runs — the harness asserts everything except literal screen pixels. Re-run anytime: `dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --verify-video "<local .mp4>"` (exit 0 = pass).

### Phase B — Video parity with the legacy path
Match what `AvaloniaMultiMonitorVideoService` + `VideoOverlayWindow` do today:
- [x] **Audio:** volume (`LibVlcAudioHelper.GetEffectiveVolume`), output-device selection, mute — route `UpdateVolume()` to the layer, not just `_currentWindow` / `_multiMonitor`. **DONE (B1):** `ApplyAudioSettings` at playback start, `ApplyVolume` on live slider updates; gated by `--verify-video` stage 6.
- [x] **Attention checks:** decouple `IsPlaying` / `SetupAttention` / `CheckSpawnTargets` / duration / safety timer / segment-arming from `VideoOverlayWindow` so they fire on the UCE layer (`OnVideoWindowStarted`'s body must run for the layer). **DONE 2026-07-04 (B2):** `VideoLayer` now raises `VideoStarted` from LibVLC `Playing` (not at the `Play()` call) plus a narrow `LengthKnown` event / `DurationMs` / `SeekTo(ms)`; the service's `OnCompositorVideoStarted` runs the shared `BeginPlaybackOrchestration` body (ExtendTimeout + safety timer + 2s attention arm — same method the window path calls), `OnCompositorVideoLengthKnown` upgrades the fallback timeout to the accurate duration (WPF `LengthChanged`→`StartSafetyTimer` contract), and the mandatory layer's natural end runs the full `OnVideoEnded` evaluation (pass/fail XP, penalties, retry loop, mercy) with `CleanupInternal` teardown symmetry. Double-run/stale-post guard: `_layerOrchestrationActive`, cleared in `CleanupInternal`. Non-gating `--verify-video` diagnostic prints "layer orchestration armed".
- [~] **Dual-monitor + strict mode + segment (random-slice) mode** behave as WPF. **Segment: DONE (B2)** — armed state is captured+disarmed one-shot in the layer branch of `StartVideoPlayback` and the deferred 700ms seek runs from `OnCompositorVideoLengthKnown` via `VideoLayer.SeekTo` (mirrors `VideoOverlayWindow.OnLengthChanged`). **Strict mode: key-blocking is N/A on the UCE path by construction** — the compositor window is permanently click-through + no-activate and receives no keyboard input, so strict blocking (ESC/panic/Alt+F4 suppression) is inherently satisfied; the flip side is that **non-strict ESC-dismiss and the panic key have no receiver on the pure layer path** (no global key hook feeds it yet) — documented gap, `// Phase B2` note in `StartVideoPlayback`. `_currentStrictMode` is recorded for bookkeeping, but attention-fail retries do NOT currently inherit it (CleanupInternal resets it before the retry callback reads it — pre-existing window-path behavior too; WPF inherits `_strictActive` :2186; see B2 residuals). **Dual-monitor:** the engine composites the layer on every monitor by design; explicit side-by-side WPF verification still open. **Also still open:** layer-path `PositionChanged` wiring (`PrimaryPlaybackTimeMsChanged` / Deeper time rules / live watch-position credit) — ✅ DONE 2026-07-04: `VideoLayer.PlaybackTimeChanged` (throttled ~1/s `TimeChanged` relay) feeds `_lastWatchPositionMs` + `PrimaryPlaybackTimeMsChanged`, and `FinalizeWatchCredit` live-reads `VideoLayer.CurrentTimeMs` at teardown; see B2 residuals for detail.
- [x] `VideoAboutToStart` / `VideoStarted` / `VideoEnded` fire with correct timing. **DONE 2026-07-04 (B2):** `VideoAboutToStart` before the 1.3s pre-announce (unchanged); `VideoStarted` now anchors to actual LibVLC `Playing` (was: at the `Play()` call, i.e. before playback existed / even on failed opens); `VideoEnded` fires exactly once per natural end via `CleanupInternal(notifyEnded: true)` after the attention evaluation, and — window-path parity — not at all on an attention-fail retry teardown.

**B2 residuals (adversarial review 2026-07-04 — verdict SAFE TO BANK, 0 blockers; fixed pre-bank:
side-effects-before-guard, stale-post `VideoEnded` leak, failed-open wedge via layer
`EncounteredError`→end-pipeline routing (WPF :1498-1511 parity), false strict-retry comment).
Deferred with evidence:**
- ✅ FIXED 2026-07-04 (commit below): `_attentionPenalties` now resets on obligation end
  (WPF Cleanup :2620 / ForceCleanup :895) for BOTH Avalonia paths, but NOT between
  attention-fail retries of the same obligation. Design: a private `_retryPending` flag is set
  in `OnVideoEnded`'s retry branch (just before the fail-message `ShowMessage`) and cleared
  once the retry callback's `PlayFile` has run its (penalty-preserving) initial
  `CleanupInternal`. `CleanupInternal` resets `_attentionPenalties` iff `!_retryPending`; this
  mirrors WPF's retry path calling `CloseAll` (no reset) vs final `Cleanup`/`ForceCleanup`
  (reset). `Stop`/`ForceCleanup` clear `_retryPending` first so a user/system stop always
  resets. The URL layer natural-end branch (`OnCompositorVideoEnded` non-orchestrated) resets
  too — WPF's URL `EndReached` calls `CloseAll` only (:1029-1032) and does NOT reset; resetting
  here is the deliberate obligation-end parity FIX, applied to both Avalonia URL paths. Per-path
  table in the commit message. Verified: slnf 0 err, WPF sln 0 err, Core 205/205, verify-video
  PASS exit 0, smoke Findings 5.
- ✅ FIXED 2026-07-04 (commit below): layer-path watch-position credit wired. `VideoLayer`
  raises a new throttled `PlaybackTimeChanged` event relaying LibVLC `TimeChanged` (marshaled
  to the UI thread like Playing/LengthChanged). THROTTLE: ~1 relay/s max via
  `_lastTimeRelayTickMs` (TimeChanged fires ~4x/s; the watermark needs no more), reset in
  PlayVideo/Stop; a stale-session check (`ReferenceEquals(sender, _player)` inside the post)
  drops queued posts from a dying/replaced player so they can't poison the next session's
  watermark. The service subscribes BOTH layers (mandatory AND URL — WPF wires `TimeChanged` on
  both its mandatory :1383 and URL :1004 players) into `OnCompositorVideoTimeChanged`, which
  raises `_lastWatchPositionMs` (max) and fans out `PrimaryPlaybackTimeMsChanged` (the Core
  seam the Deeper `VideoServiceTimeSource` consumes — wired exactly as the window path's
  `onPositionChanged` does). A narrow `VideoLayer.CurrentTimeMs` live read in
  `FinalizeWatchCredit` recovers the exact position at teardown regardless of the throttle lag
  (natural end credits ~full duration: `VideoEnded` fires before `Stop()` in the layer's
  EndReached post, so the synchronous OnVideoEnded→CleanupInternal→FinalizeWatchCredit chain
  reads the still-alive player). Non-double-count preserved: watermark + `_creditedWatchSeconds`
  are max/delta-based and zeroed in the finally; the natural-end full-duration seed
  (`_lastWatchPositionMs <= 0`) is now a last-resort fallback that stops applying once the
  watermark is live — the WPF MediaElement-fallback contract (:2196-2200) intact.
- Strict-mode retries do not inherit strictness (CleanupInternal resets `_currentStrictMode`
  before the retry callback reads it; WPF inherits `_strictActive` :2186). Pre-existing window-path
  behavior; truthful NOTE at the recording site.
- Safety margin is duration+30s vs WPF's +5s (pre-existing Avalonia convention, safe direction);
  WPF's post-seek re-check `Time < startMs-1000` (:1417) not ported (also absent on the window path).
- Non-strict ESC-dismiss/panic has no receiver on the pure layer path (compositor window receives
  no keyboard) — needs the global key hook (WP3 territory).
- Legacy multi-monitor path never had attention orchestration (`OnMultiMonitorPlaybackStarted`
  raises only `VideoStarted`) — pre-existing, moot after Phase E.

### Phase C — Verify the other migrated layers (skill Phase 2)
- [~] Exercise flash, subliminal, bouncing-text, bubbles, lock-card, pink/spiral/brain-drain end-to-end vs WPF (z-order, opacity, timing, multi-monitor). Mark rows in `avalonia-ui-parity-matrix.md`. **DONE 2026-07-04 via the new `--verify-layers` harness** (`LayerVerification.cs`, mirrors `--verify-spiral`/`--verify-video`; per-layer table + evidence in `avalonia-ui-parity-matrix.md` §"UCE layer verification"): all 7 migrated layers PASS — registered at exact `CompositorLayers` z (engine.GetLayer), activated through their OWNING services (flash `TriggerFlashOnce`, subliminal `FlashSubliminalCustom`, bouncing `Start(pool)`, bubbles `Start/SpawnOnce`, overlays `ShowOverlaySustained`), RENDER proven by GDI screen-capture MD5 deltas (per-screen working-area + center-crop), teardown clean. **Dual-surface P0 guardrails asserted BOTH directions:** SubliminalLayer produced a capture delta (capture-visible by design, WPF WDA_NONE contract) and BrainDrainLayer, sequenced alone, produced NO delta on the ambient-stable screens while active with `ExcludedWindowCount` 1 (WDA_EXCLUDEFROMCAPTURE working), excluded surface torn down ~500ms after hide. Lock-card recorded honestly as SKIPPED: no LockCardLayer exists (grep-verified; still a window). Layer bugs found: none; one harness-isolation app fix landed (`App.axaml.cs` `isHarnessRun` now covers `--verify-*` so launch behaviors/scheduler can't fire real sessions into verify runs — observed contaminating the first attempt). `[~]` not `[x]`: side-by-side WPF timing/opacity/easing + click-through-over-effect + mixed-DPI placement comparison still needs human eyes; z-ORDER between layers is asserted only as registration constants, not visually. Re-run anytime: `dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --verify-layers` (exit 0 = pass).

### Phase D — Performance pass (separate edits, after parity)
- [x] `VideoLayer`: reuse a persistent `SKBitmap`/`SKImage` (kill the ~480 MB/s per-frame alloc); draw a cached `SKImage`, not `DrawBitmap`. **DONE 2026-07-04:** triple-buffered decoder boundary — LibVLC decodes straight into 3 pinned RV32 buffers (`Marshal.AllocHGlobal`, allocated once per `PlayVideo`); 3 long-lived zero-copy `SKImage.FromPixels` wrappers created once per `PlayVideo`; `Render` draws the FRONT wrapper via `DrawImage`. Zero per-frame allocation AND zero per-frame pixel copy (old path also memcpy'd ~8 MB/frame — one copy fewer). LibVLC only ever receives the DECODE buffer, DisplayCallback rotates DECODE↔READY under an outstanding-lock count, the UI tick swaps FRONT↔READY — protocol comment block in `VideoLayer.cs`. Verified: `--verify-video` exit 0 at 21 frames/700ms (same as before), on-screen screenshots show distinct advancing frames (no GPU raster-texture staleness with the long-lived wrappers on the leased canvas), smoke 5 findings, benchmark parity.
- [x] Fold `VideoLayer._renderTimer` into the engine `Update()` pass (drop the second 60 Hz timer). **DONE 2026-07-04:** private `DispatcherTimer` deleted; the presentation swap lives in `Update(TimeSpan)` on the engine's single 16 ms tick. The engine only ticks `IsActive` layers — `IsActive` is true from `PlayVideo`'s buffer allocation and the service calls `_compositor?.Start()` first (first window created synchronously), so no first-frame loss vs the old timer.
- [x] **Evaluate moving the engine loop to `CompositionCustomVisualHandler` (render-thread, self-driven). EVALUATED 2026-07-05 → DEFER (no profiling need).** Research (assembly-verified against the pinned Avalonia **12.0.5** binaries, not memory): the API family IS present + stable in v12 — `CompositionCustomVisualHandler` + `ElementComposition.GetElementVisual`/`SetElementChildVisual` + `Compositor.CreateCustomVisual` + `CompositionCustomVisual.SendHandlerMessage` + `OnRender(ImmediateDrawingContext)` with `ctx.TryGetFeature<ISkiaSharpApiLeaseFeature>().Lease().SkCanvas` (same Skia lease as today's `ICustomDrawOperation`). **API-NAME CORRECTION for any future implementer:** the render-thread re-arm primitive is **`RegisterForNextAnimationFrameUpdate()` + `OnAnimationFrameUpdate()`** — NOT `RequestNextFrameRendering()` (that is the WinUI/`Windows.UI.Composition` name; the substring `FrameRendering` is absent from `Avalonia.Base.dll`). **Why DEFER:** the render-thread model's real wins (UI thread not woken per 16ms tick, vsync-aligned pacing, `CompositionNow` monotonic clock, keeps animating while UI thread is blocked) do NOT reliably reduce total CPU when the bottleneck is not UI-thread scheduling. The engine already sustains **idle ~121 fps / active ~130 fps** (fresh `--benchmark` 2026-07-05, Debug) — far above the 60 target / 30 floor — so this is a low-ROI, high-blast-radius (all 20 layers) architectural change, wrong to attempt speculatively on the shared branch. **Re-trigger threshold** (when this becomes worth doing, as an isolated benchmarked change): profiling shows UI-thread contention causing overlay jank/dropped frames while the UI thread is busy, OR effect FPS drops below the 60 target under load / the 30 floor. Per-window caveat when triggered: each `TopLevel` has its own `Compositor`, so it is one handler + one `CompositionCustomVisual` per monitor window (cannot share one handler across the per-monitor topmost windows), and all handler callbacks run on the render thread → UI→render data must go via `SendHandlerMessage` with immutable snapshots.
- [x] **Dirty-rect / opaque-cull (skill Phase 7). EVALUATED 2026-07-05 → DEFER (conditional, no need).** The plan gates this on "only if profiling shows a need"; the fresh benchmark shows no need (FPS well above targets; idle CPU ~1%, active ~3.5%). Same re-trigger threshold as above — revisit only if a full-load profile shows a render-bound frame budget.

> **Phase D verdict 2026-07-05:** the two hot-path optimizations that mattered (D.1 zero-alloc video, D.2 folded tick) are DONE; the two remaining items are correctly DEFERRED with an evidence-backed no-need decision + a concrete re-trigger threshold. Phase D is complete for the current perf reality. This is a Debug-run FPS observation (floor/target met); the Release WS3 perf gate vs `benchmark-optimized.json` is a separate workstream, not WS1.

### Phase E — Flip default to UCE-only, then delete legacy (skill Phase 6)

**DEFAULT FLIPPED + LIVE-VERIFIED 2026-07-05.** Compositor video is now the runtime default
(`AvaloniaVideoService` ctor: `useCompositorVideo = compositor != null && CCP_LEGACY_VIDEO != 1`);
the legacy path is a temporary opt-OUT escape hatch. E1 (`6180efc2`) routed ESC-dismiss/panic
through the global `IInputHook` (the layer path has no window to receive keys). E2 (`ed636a7c`)
flipped the default + added the `--verify-visible` eyes-verification harness. **User-confirmed by
running:** the mandatory video renders through the compositor and the spiral/pink overlays
composite ON TOP of it (Stage 2 dump: `Video=ACTIVE + Spiral=ACTIVE + PinkTint=ACTIVE`) - the
original 'video covers the overlays' bug is fixed. Remaining deletion work (below) is E3.

**RESOLVED 2026-07-05 - shutdown segfault under a THRASHING video stream.**
FIX (commit pending this edit): `VideoLayer.Stop()` now stores its deferred vmem-teardown `Task`
(`_teardownTask`, only when there is something to free so a redundant Stop cannot overwrite a
pending real teardown) and exposes `WaitForTeardown(timeoutMs=1500)`; `AvaloniaVideoService.Dispose`
drains both layers before returning, so the process no longer exits while the ~400 ms deferred native
player.Dispose()/FreeHGlobal is pending. The 400 ms race-avoidance defer itself is unchanged; only
the shutdown drain is added. VERIFIED: `--max-benchmark` re-run exits 0 (was 139/segfault), the FPS
report writes cleanly, and the mandatory video plays to completion (with the attention-check benchmark
fix). S9 FPS captured: max-intensity 4-min AvgFPS=157.0, MaxFPS=206, AvgCPU=19.8%, PeakCPU=57.4%,
WorkingSet~2.3 GB. PERF-WATCH (not a blocker): 157 avg is ~12% under the stored optimized reference
(178, 3-min); not apples-to-apples (this run decodes the full video through the UCE path over 4 min),
but worth a dedicated perf pass if it drops further - well above the aim 60 / floor 30 targets.
Historical severity note: normal video + clean exit never crashed (`--verify-visible` x3 exit 0); the
crash needed the benchmark's failing-stream teardown state - it was a robustness issue, not an
everyday crash-on-exit.
Found via `--max-benchmark` 2026-07-05: the 4-min max session (incl. Phase 3 heavy chaos) runs
to COMPLETION (session completes, XP awarded - the chaos port S5-S8 is validated under max load),
then the process SEGFAULTS during shutdown teardown (exit 139), which also prevents the FPS
results write (`benchmark-report.json` left stale). Diagnosis: `VideoLayer.Stop()`
(`CCP.Avalonia/Compositor/Layers/VideoLayer.cs:358`) defers the LibVLC vmem teardown via a
fire-and-forget `Task.Run(async () => { await Task.Delay(400); player.Dispose(); ... Marshal.FreeHGlobal(buffer); })`.
Nothing awaits it, so on app shutdown (`App.axaml.cs` FlushPersistentState -> DisposeServiceIfPossible(IVideoService))
the process tears down the LibVLC instance while that deferred task is still pending -> it frees
pinned vmem buffers / disposes a player against a half-dead native LibVLC = native crash. The old
`VideoOverlayWindow` used a `VideoView` (LibVLC-owned surface, no manual vmem buffers) so it never
had this race - which is why the Jul-4 benchmark completed and today's did not. Aggravated by a
failing stream state (`Failed to create video converter` + mjpeg/cache_read spam) leaving the
decoder thrashing at teardown. Secondary suspect (less likely): the audio ducker's WASAPI teardown
(a `ducking_recovery.json` was left behind, but that is a symptom of dying-while-ducked, not proof
of cause). FIX SHAPE (needs a careful pass, forbidden-zone native race): give `VideoLayer` a
DETERMINISTIC synchronous teardown for the shutdown path that drains `_locksOutstanding` (do NOT
naively free synchronously - that reintroduces the exact mid-DisplayCallback race the 400ms defer
avoids), OR make the video service `Dispose` await the pending teardown before the LibVLC instance
is disposed. Do it as a focused pass with the `unified-compositor-engine` skill, not inline.

Observations from the eyes-verification (not regressions, follow-ups):
- Flash popups do NOT spawn while a mandatory video is active (`--verify-visible` Stage 2 flash
  count stays 0). Pre-existing (the legacy video window covered flashes too); confirm vs WPF
  whether flashes should overlay a mandatory video before treating as a bug.
- Spiral overlay maxes at ~10% opacity by design; near-invisible at low user settings.

- [x] Remove the `_mandatoryVideoLayer == null` / `_videoLayer == null` fallback guards. **DONE E3**: branches 2 (multi-monitor) and 3 (per-window fallback) deleted; the compositor branch is the whole body with a defensive log-warning else (compositor is always registered).
- [x] Audit and rehome the **references** to `IMultiMonitorVideoService`. **DONE E3**: RemoteCommandExecutor / MainWindowViewModel / AppInfoTabViewModel (video smoke-test) / `AvaloniaVideoInfo` (IsPlaying seam) all rehomed to `IVideoService`; AutonomyService comment updated; App.axaml.cs dispose line removed.
- [x] Delete `AvaloniaMultiMonitorVideoService` + `VideoOverlayWindow`. **DONE E3**: `AvaloniaMultiMonitorVideoService.cs` + `IMultiMonitorVideoService.cs` deleted; the `VideoOverlayWindow` nested class + `CreateWindow`/`SpawnSecondaryWindows`/`_currentWindow`/`_secondaryWindows` gutted (~659 net lines out of `AvaloniaVideoService`). NOTE: the per-overlay chaos `*Window` classes are Phase F, not this slice.
- [x] Remove their DI registrations + dead `using`s + the `CCP_LEGACY_VIDEO` escape hatch. **DONE E3**: `useCompositorVideo = compositor != null` (no opt-out remains).
- [x] **Acceptance MET**: `CCP.Desktop.slnf` 0 errors, WPF sln 0, Core 467/467, smoke 5 findings; `--verify-visible` post-deletion shows `Video=ACTIVE + Spiral=ACTIVE + PinkTint=ACTIVE` (video renders through the compositor with NO fallback). Memory-vs-legacy comparison is moot now that the legacy path is gone; benchmark stays green.

**PHASE E COMPLETE 2026-07-05 (E1 `6180efc2` + E2 `ed636a7c` + E3).** The Avalonia head plays all video through the compositor; the legacy multi-monitor / per-window video path is deleted.

### Phase F — Chaos layer migration queue (WS2 / WP3)

Passive chaos overlays become `IAvaloniaLayer`s on the existing engine, one class per commit
(goal doc WP3). Interactive chaos surfaces (HUD, boon bar, toy button, unlock card, backdrop,
bubble host) keep their windows/input model per `overlay-clickthrough` until the hook
click-swallow gap is resolved.

**Z-band decision (landed with the template):** chaos layers live in a dedicated band
**100–199, ABOVE PinkTint (70)** — constants in `CompositorLayers.cs`. WPF evidence:
`Chaos/ChaosWindowZ.cs` re-stacks every chaos window to the TOP of the topmost band on
show/arm (`RaiseTopmost`/`RaiseAboveVideo` — a single `SetWindowPos(HWND_TOPMOST)`), and
`ChaosModeService.RaiseGameLayerAboveVideo` lifts the whole game layer when a mandatory
video lands — so in WPF a freshly-raised chaos overlay sits above the video AND above
earlier-shown session-effect windows (spiral/pink tint). Within the band: ambient field FX
100–119, cursor-attached telegraphs 120–139, informational text (banners/pop text/announcer/
wave timer) 140+.

**Capture-affinity finding (grep-verified 2026-07-04):** NO WPF chaos window calls
`SetWindowDisplayAffinity` (only keyword-highlight, brain-drain and subliminal touch
affinity in the WPF head). Chaos visuals are therefore **capture-VISIBLE**: every chaos
layer stays on the MAIN surface (`ExcludeFromCapture` default false).

#### Migration queue (inventory swept 2026-07-04; LOC = .axaml.cs/.cs line count)

"Live" = reachable from the current Avalonia head. Several ported overlay classes are
UNWIRED (0 callers) until the deferred **chaos run-engine faithful port** backlog row lands
— they are ranked after the live ones because an unwired migration cannot be verified.

| # | Class (`CCP.Avalonia/Chaos/`) | Class· | LOC | Live path | Order / notes |
|---|---|---|---|---|---|
| 1 | `ChaosCursorGlowOverlay` | PASSIVE | 174 | ✅ rabbit caller | ✅ **MIGRATED → `ChaosCursorGlowLayer` (Z=130), the template** — old window deleted; also fixed a 2x-too-fast breath (legacy port passed WPF's 620ms half-leg as ScalePulse's FULL cycle) |
| 2 | `ChaosPopText` | PASSIVE | 182 | ⚠️ seam-only | ✅ **MIGRATED → `ChaosPopTextLayer` (Z=145)** — old window deleted; Skia outlined text (Segoe UI Bold 22, stroke-under-fill, SaveLayer group opacity = WPF window-opacity fade), 14-floater cap, WPF timings (60/230/200ms, rise +6→-22 DIP over the whole 490ms). HONEST wiring note: the ✅ in the old row was a false positive — `Show` had ZERO production callers in the Avalonia head (only `RaiseActive` z-churn; the WPF call sites live in the unported ChaosModeService/BubbleService bubble-effect paths). The `AvaloniaChaosService.ShowChaosPopText(px,py,text,tint)` seam (gated on `ChaosAnnouncerEnabled`, WPF contract) is live and proven by `--verify-layers` Stage 4c; the run-engine port wires production callers to it. Also fixes the legacy window's DIP-anchor-as-PixelPoint mixed-DPI bug by defining the seam in PHYSICAL px |
| 3 | `ChaosEffectBannerOverlay` | PASSIVE | 202 | ✅ porn_dvd toy (restored) | ✅ **MIGRATED → `ChaosEffectBannerLayer` (Z=140)** — old window deleted; WPF contract (entries keyed by id, side-by-side centered on the primary work-area top+6 DIP, fade-in 200ms, throb 1.0↔1.03 sine 850ms/leg = 1700ms full cycle — legacy had this cycle RIGHT, fade-out 380ms, End frees the id immediately, duplicate id = let-it-ride, announce art 56 DIP else outlined text 34). HONEST wiring note: the old ✅ was a false positive — Show/End had ZERO callers (only EnsureCreated + RaiseActive churn, all deleted); the vestigial `_dvdBannerOn` flag was WPF's dvd banner wiring minus the calls, RESTORED through `ShowEffectBanner`/`EndEffectBanner` (porn_dvd toy = real production caller). Legacy bugs fixed: shared single OpacityFade cancelled prior entries' in-flight fades; `_pulses[id]?.Dispose()` KeyNotFoundException per first add. `CloseEffectBanners` wired into CleanupAfterRun (WPF CloseActive parity). Verified: `--verify-layers` Stage 4d |
| 4 | `ChaosAnnouncerOverlay` | PASSIVE | 316 | ✅ (4 callers, rewired) | ✅ **MIGRATED → `ChaosAnnouncerLayer` (Z=150)** — old window deleted; the priority QUEUE moved to `AvaloniaChaosService` byte-equivalent to WPF's static queue (stable max-priority dequeue, gameplay pri 0 / narrator 100+band, per-line dwell 650/TEACH 3000ms, `_showing` flag, STORY interrupt = CutShort from CURRENT opacity, `ChaosAnnouncerEnabled` / `NarrativeActive` gates); the layer renders ONE line (in 110ms + BackEase(0.6) pop 0.85→1.0/180ms, hold, out 240ms) and fires `LineCompleted` on the engine tick = the WPF fade.Completed→ShowNext chain. All 4 caller groups rewired to `AnnounceChaos`/`AnnounceChaosNarrator` (ChaosHappyPath ×8 + TeachHoldMs, ChaosOverlayWindow, ChaosNarrator.Speak); partial-class stub deleted. Legacy parity bugs fixed: scale pop used standard c1=1.70158 back-out instead of WPF BackEase amplitude 0.6 (stronger overshoot); palette drifted to theme brushes for Mantra/Temptation/Depth (WPF constants restored); run teardown never dropped queue/line (WPF CloseActive parity now in CleanupAfterRun). Verified: `--verify-layers` Stage 4e |
| 5 | `ChaosFlashOverlay` | PASSIVE | 201 | ✅ via `AvaloniaEffectPayloads` | ✅ **MIGRATED → `ChaosFlashWashLayer` (Z=115)** — old window deleted; WPF contract (one wash at a time, new Show swaps + restarts from 0; fade-in 500ms → hold max(600,durationMs) concurrent with the fade → fade-out 700ms; peak clamp 0.02..1.0; UniformToFill = cover-fit TOP-LEFT-anchored clipped to stage — WPF clips right/bottom, Avalonia centers; empty pool = silent no-op; NO settings gate). Distinct from the session `FlashLayer` (Z=30) by design — NOT merged. Decode-ONCE off-thread via `SkiaImageDecoder` (stills cap 2560 = WPF DecodePixelWidth; animated 1280/40 frames = the WPF animated-WEBP wash budget — WPF GIFs streamed native-res per-frame which decode-once can't mirror — + spiral 96MB cap); the legacy window streamed GIF frames via AvaloniaAnimatedGif. Legacy drifts fixed: stage forced primary (WPF StageBounds is dual-aware — stage = effect-screen union now); run teardown never closed the wash (WPF ChaosModeService.cs:3097/3146 CloseActive — `ClearChaosFlashWash` now in CleanupAfterRun). Caller rewired: braindrain payload → `AvaloniaChaosService.ShowChaosFlashWash` (WPF defaults 10%/10s); the WPF dashboard-glitch custom-opacity path has no Avalonia caller yet (payload ctor lacks braindrainOpacity — pre-existing head gap, noted not forced). Verified: `--verify-layers` Stage 4f (Show 1400ms/0.35 via the owning service, full-screen delta, Clear teardown) — 12/12 PASS |
| 6 | `ChaosDvdOverlay` | PASSIVE | 349 | ✅ porn_dvd toy | ✅ **MIGRATED → `ChaosDvdLayer` (Z=105)** — old window + the dead `ChaosDvdHostOverlay` (0 callers, grep-verified — the Avalonia DVD port never used host mode) deleted; window POOL machinery dies too (it existed to dodge WPF layered-window churn). WPF contract exact: Segoe UI Bold 46×fontScale(0.5..1.5), stroke #0B0812 pen 2×2.6 round-join under fill, pad 8.6 DIP (OutlinedText.Build), 6-hue palette +1/bounce, 230 DIP/s × clamp(speedMult 0.3..2.0) at 20–80°, fade-in 180ms → 0.85, life max(1,s), retire fade 240ms with motion stopped, dt clamp 0.1s, bounce on the primary WORK AREA, shared 250ms boing throttle, Casting-Couch bounce splits (±0.61 rad, toy cap 8, dvd_launch @0.35) and Intrusive-Thoughts rabbit split (once/instance, +2s even at the thought cap 8, dvd_bounce @0.4). Physics in the layer's Update as pure render state (velocity DIP/s → physical px via the primary scale); side effects (PopBubblesInRect/AnyDarterIntersects/sfx) stay in the SERVICE via delegates invoked outside the layer lock (announcer LineCompleted precedent). porn_dvd caller → `LaunchChaosDvd`; banner interplay reads `ChaosDvdToyActive` (thoughts excluded, WPF AnyToyActive). Honest notes: the Spanker smack-to-turn clickable path is DEAD in this head (SpankerRedirect never assigned — layer passive until that port) and no Avalonia caller launches thoughts yet. Legacy drifts fixed: fixed 33ms step (WPF uses real clamped delta — the per-leg duration bug class), and run teardown never closed the logos (WPF ChaosModeService.cs:3101/3150/3236 — `ClearChaosDvdLogos` now in CleanupAfterRun). Verified: `--verify-layers` Stage 4g (Launch via the owning service, full-screen delta mid-flight, AnyToyActive asserted, natural 2s+240ms expiry) — 13/13 PASS |
| 7 | `ChaosGifCascadeOverlay` | PASSIVE | 409 | ✅ via `AvaloniaEffectPayloads` | ✅ **MIGRATED → `ChaosGifCascadeLayer` (Z=110)** — old window deleted; WPF contract exact: clamps (gifSize 40–600, fallSpeed 0.5–30, opacity 0.05–1, startScale 0.1–1), spawn interval 1000/max(0.05,rate) ms with ONE immediate spawn, life max(1,s) closes the spawner and in-flight clips fall out, re-Show replaces clips, caps EXACT (14 alive / 3 animated / 3MB animate ceiling — over-budget falls as a display-size still), speed = fallSpeed×(0.7+rnd×0.6) DIP-per-16ms delta-scaled (stall clamp 0.1s), growth startScale→1 by 75% of the way down center-origin, width=gifSize Uniform-by-aspect, despawn past stage+gifSize. **Decode-ONCE** per clip off-thread via `SkiaImageDecoder` (animated: display-size + 48 frames = the WPF AnimatedWebp.AttachAnimation budget the cascade rode + spiral 96MB cap; stills at display size) — the legacy window STREAMED per-frame GIF decodes through AvaloniaAnimatedGif (the exact per-frame decode the UCE forbids). Legacy drifts fixed: animated-webp support dropped (WPF animates webp; SKCodec restores it); window sized in physical px units interpreted as DIPs (clips spawned past the right edge on scaled displays — the PopText DPI bug class); stage forced primary (WPF StageBounds is dual-aware); run teardown never closed the cascade (WPF ChaosModeService.cs:3098/3147 — `ClearChaosGifCascade` now in CleanupAfterRun). Caller rewired: GifCascadePayload → `ShowChaosGifCascade` (knobs unchanged). HONEST note: `IsGifCascadeRaining` (WPF IsRaining — heavy gate + VideoService read it in WPF) is exposed but has ZERO Avalonia consumers yet; those arrive with the run-engine/video-gate port. Verified: `--verify-layers` Stage 4h (Show via the owning service, full-screen delta with clips in frame, Clear teardown; honest SKIP on an empty pool) — 14/14 PASS |
| 8 | `ChaosFieldFxOverlay` | PASSIVE | 440 | ⚠️ seam-only (was a false positive) | ✅ **MIGRATED → `ChaosFieldFxLayer` (Z=100)** — old window deleted; the floor of the chaos band (WPF "bottom of the gameplay band: ambient FX that read fine UNDER the bubbles"). HONEST wiring correction: the old "✅ (2 callers)" was a FALSE POSITIVE — the 2 callers were `EnsureCreated` pre-warm + `RaiseActive` z-churn (both deleted under the compositor model); ALL drawing seams (Ripple/SnapRipple/Residue/TrailDot/SetTether/ClearTether) had ZERO callers in the Avalonia head (the WPF call sites live in unported BubbleService paths — shockwaves, field hazards, rabbit trails, The Bound). The `ChaosField*` service seams are live and proven by `--verify-layers` Stage 4i; the bubble-engine FX port wires production callers. WPF contract restored exactly: ripple ring (190,#7AE0FF) 6 DIP, scale 0.05→1 cubic-out WITH stroke riding the growth (WPF RenderTransform), opacity 0.95→0 linear; snap cast = LINEAR cyan front (200-alpha, matches the kill front) + eased pink echo at 0.82r + EIGHT shards at 2πi/8+0.39 translating 0.2r→0.85r over 0.7·life (the legacy window drew TEN random-angle endpoint-growing shards — a drift, fixed); residue crackle 0.55–1.0 per 90ms then linear fade (legacy also skipped its px→DIP radius conversion — moot in px space); trail dots 90-slot ring buffer, opacity 0.65→0 + scale 1.1→0.3 both LINEAR over max(0.3,lifeSec) (legacy used 1.25→0 ease-out, no floor — fixed); tether dash 4,3, thickness 5−dist/250 clamp 1.5..5. `ClearChaosFieldFx` wired into CleanupAfterRun (WPF ChaosModeService.cs:3108/3157/3243). Zero per-frame alloc (unit-radius spark/residue shaders + transforms; dash effect cached per monitor scale). Verified: `--verify-layers` Stage 4i (ripple+residue+tether via the owning service, center-crop delta, Clear teardown) — 15/15 PASS |
| 9 | ~~`ChaosEStimGlowOverlay`~~ | PASSIVE | 184 | 🗑 **DELETED 2026-07-05** | dead/unwired window removed; charge-glow halo = the deferred charged-pop FEATURE, not a window to migrate |
| 10 | ~~`ChaosEStimOverlay`~~ | PASSIVE | 267 | ✅ **arc → `ChaosEStimArcLayer` (Z=125); window DELETED 2026-07-05** | lightning bolts between bubbles — arc renders via the layer |
| 11 | `ChaosWaveTimerOverlay` | PASSIVE | 243 | ✅ **MIGRATED → `ChaosWaveTimerLayer` (Z=155)** | old window (+.axaml) deleted 2026-07-05; primary top-right pill (WAVE x/y + m:ss clock, red ≤10s + 0.25↔1.0/840ms final-rush breath, gold score) rendered in Skia (SKTextBlob + rounded pill, zero per-frame alloc — blobs rebuilt only on string change). `Update`→`SetValues`, draft-out `Clear`→`Hide`, run-end `CloseActive`→`Clear`. Was LIVE-wired (row mislabelled "unwired"). Primary-monitor only (bounds-origin gate). `--verify-layers` Stage 4k PASS (activated + full/pill delta + Clear teardown) |
| 12 | ~~`ChaosVibeTrailOverlay`~~ | PASSIVE | 300 | ✅ **MIGRATED → `ChaosVibeTrailLayer` (Z=128); window DELETED 2026-07-05** | warm buzzing glow + fading cursor trail; wired to the live vibe_popping toy lifecycle (StartVibeTrail/StopVibeTrail + 16ms IPointerState cursor feed). `--verify-layers` Stage 4n PASS (DIFFER center-crop) |
| 13 | `ChaosFxWindow` | PASSIVE | 172 | ✅ **MIGRATED → `ChaosFxLayer` (Z=118)** | old window deleted 2026-07-05; full-screen colour-vignette impact pulse. WPF `Pulse` contract kept (peak clamp 0.22+strength*0.5→0.15..0.72, snap 40ms→fade 300ms, elliptical radial 0.9/1.0, stops α0@0/α0@0.45/α255@1.0) via a Skia unit-shader + canvas-transform (zero per-frame alloc, cursor-glow pattern). Only `Pulse` was wired in the head; `BeginEdgeHold`/`SetHeatTint`/`FreezeBurst` had 0 head callers (dead API) and were dropped honestly. Rendered per effect-screen (WPF was primary-only single-window; identical when DualMonitor off — a documented dual improvement). `--verify-layers` Stage 4j PASS (activated + full/edge delta + auto-complete/Clear teardown). NOTE: this row + row 11 (WaveTimer) were mislabelled "unwired"; the run-engine port had wired them (audit correction, `docs/uce-coverage-audit.md`). | 
| 14 | ~~`ChaosSkiaFxOverlay`~~ | PASSIVE | 632 | 🗑 **DELETED 2026-07-05** | unwired superset renderer removed; its effects render via the simpler layers now (E-Stim bolts → `ChaosEStimArcLayer`, cursor glow → `ChaosCursorGlowLayer`, field trail → `ChaosFieldFxLayer`) — no consolidation target remains |
| – | ~~`ChaosBubbleHostOverlay`~~ | — | 172 | 🗑 **Avalonia impl DELETED 2026-07-05** | experimental window-based shared-host bubble renderer removed (+ `SharedHostBubbleRenderer` + the Avalonia Hub checkbox); chaos bubbles always use the compositor `BubbleLayer`. The `ChaosBubbleSharedHost` setting stays in Core for the WPF head only (WPF still has its own shared-host path) |
| – | `ChaosHudWindow` | INTERACTIVE | 728 | ✅ | HUD — stays window |
| – | `ChaosBoonBarOverlay` | INTERACTIVE | 226 | ✅ | boon bar — stays window |
| – | `ChaosToyButtonWindow` | INTERACTIVE | 237 | ✅ | toy button — stays window |
| – | `ChaosOverlayWindow` | INTERACTIVE | 1103 | ✅ | run stage/boon draft (Pick buttons, click-through toggling) — stays window |
| – | `ChaosUnlockCardOverlay` | INTERACTIVE | 270 | ❌ unwired | unlock card — stays window |
| – | `AvaloniaBubbleWindow` | LEGACY-DEAD | 95 | ❌ 0 callers | bubbles are on `BubbleLayer`; deletion candidate (file a row) |
| – | `ChaosHubWindow` | NON-CHAOS UI | 602+ | ✅ | dollhouse hub — normal window |
| – | `ChaosIntroWindow` | NON-CHAOS UI | 182 | ✅ | modal intro card — normal dialog |

#### How to migrate a chaos overlay (the template recipe — from the cursor-glow diff)

1. **Extract the WPF contract** from the original under `ConditioningControlPanel/Chaos/`
   (e.g. `Chaos/ChaosCursorGlowOverlay.cs`): geometry in DIPs, colors/stops, animation
   min/max/period (WPF `DoubleAnimation` duration is PER LEG; `AutoReverse` doubles the
   full cycle — the legacy Avalonia ScalePulse ports got this wrong at least once), and
   check the WPF window for `SetWindowDisplayAffinity` (none found so far ⇒ main surface).
2. **Add the z constant** to `CCP.Avalonia/Compositor/CompositorLayers.cs` inside the chaos
   band (100–199, sub-ranges above), citing the WPF show-order evidence.
3. **Write the layer** in `CCP.Avalonia/Compositor/Layers/` as a `BaseLayer` subclass
   (`ChaosCursorGlowLayer.cs` is the reference): state fields under one `lock`; public
   mutators the service calls; `IsActive` content-driven; animation clocked by
   `Update(deltaTime)` accumulation (never a private `DispatcherTimer`); Skia objects
   (shaders/paints/images) built ONCE, zero per-frame allocations; geometry in PHYSICAL px,
   DIP→px via the screen-aware `Render(canvas, bounds, screen, dt)` overload's
   `screen.Scaling`, with the 3-param abstract override forwarding to it (`BrainDrainLayer`
   pattern); `ExcludeFromCapture` stays default false.
4. **Register in the owning service** (`AvaloniaChaosService` in
   `Services/AvaloniaHeadStubs.cs` for run-driven effects): add
   `CompositorEngine? compositor = null` to the ctor, construct the layer, and
   `compositor?.RegisterLayer(layer)` once for the service lifetime (the
   `AvaloniaOverlayService` pattern — idle layers cost nothing; a null engine = layer
   silently renders nothing, UCE rule 7 caveat).
5. **Replace every old-window call site** with service methods that drive the layer
   (`ArmCursorGlow`/`MoveCursorGlow`/`DisarmCursorGlow` replaced the
   `ChaosCursorGlowOverlay.*` statics). Drop WPF z-churn calls (`RaiseAboveVideo`/
   `RaiseTopmost`/`EnsureCreated`/`CloseActive`) — z comes from `CompositorLayers` (rule 9)
   and registration is app-lifetime (no keep-alive churn).
6. **Delete the old window files** (`.axaml` + `.axaml.cs`) once callers are gone; verify
   with a repo-wide grep for the class name (no csproj edits needed — default globs).
7. **Extend `--verify-layers`** (`tests/CCP.Avalonia.Desktop.Windows.Smoke/LayerVerification.cs`):
   `ExpectLayer<T>` in the registration sweep + a stage driving the effect THROUGH the
   owning service (public mutators; concrete-cast like `AvaloniaChaosService` if the seam
   is head-specific), asserting activate / capture-delta / teardown; add the teardown call
   to the `finally`. If the effect genuinely needs a live chaos run to trigger, say so in
   the row honestly instead of faking a trigger path.
8. **Gates + commit** (one class per commit): slnf 0 errors · WPF sln 0 errors · Core tests
   (count never decreases) · `--verify-layers` exit 0 · `--verify-video` exit 0 ·
   `--smoke-test` `Findings: 5` baseline; update this queue table.

## Risks / open questions
- **Render-thread bitmap draw:** is drawing a UI-thread-allocated `SKBitmap` onto the leased GPU
  canvas the cause of the no-show? Phase A bisect answers this; Phase D's persistent-`SKImage` is the
  likely correct+faster shape regardless.
- **Cross-platform click-through:** Windows is solved; Linux (XShape) / macOS / Wayland are
  open and out of scope until desktop-Windows parity holds.
- **Chaos migration (skill Phase 4 / Phase F above)** is underway — all EIGHT live passive overlays are layers (rows 1–8); the big unknown is the unwired half of the queue (rows 9–14), blocked on the chaos run-engine faithful port backlog row, plus the interactive set behind the hook click-swallow gap.

## Verification commands
```bash
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj
```
Drive each feature in the running app (parity is proven by exercising, not by reading) and check
`ConditioningControlPanel/ccp-run.log` for `VideoLayer:` / `CompositorEngine` lines.

## Sources
- [Avalonia — Custom rendering](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering)
- [ICustomDrawOperation.Render not re-invoked (#12247)](https://github.com/AvaloniaUI/Avalonia/issues/12247)
- [Don't re-create SKImage on every WriteableBitmap draw (PR #18164)](https://github.com/AvaloniaUI/Avalonia/pull/18164)
- [Custom drawing examples (wieslawsoltes)](https://github.com/wieslawsoltes/CustomDrawingAvaloniaExamples)
- [Pass-through transparency v11 (#11911)](https://github.com/AvaloniaUI/Avalonia/discussions/11911) · [Click-through transparent window (#13827)](https://github.com/AvaloniaUI/Avalonia/discussions/13827)
