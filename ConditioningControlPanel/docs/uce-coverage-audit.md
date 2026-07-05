# UCE Coverage Audit — what does and does not render through the compositor

Date: 2026-07-05. Author: Fable 5 (session pursuing `docs/skia-rebuild-goal.md`).
Method: read-only code sweep (no runtime), verified against `CompositorEngine.RegisterLayer`
call sites, `Compositor/Layers/*`, `CompositorLayers.cs`, and every `: Window` subclass in
`CCP.Avalonia`. Skills applied: `unified-compositor-engine`, `port-audit`.

## Why this exists

Goal doctrine (`skia-rebuild-goal.md` §"Rendering doctrine: Skia everywhere"): **every
animated or real-time visual EFFECT renders as a compositor layer** (engine mode + game
mode). Windows that legitimately stay windows are the **INTERACTIVE surfaces only** (rule
of thumb: "if the user clicks IN it, it may be a window; if it just draws, it is a layer").
This audit answers the owner's question — *check what does and does not use UCE first* —
and turns the gap into an ordered migration backlog.

## Verdict

**Green.** The session-effect set, the *live* passive chaos overlays, AND the last window-based
passive effect (attention-check) are all on the UCE (**22 registered layers** as of 2026-07-05:
9 session + 12 chaos + 1 attention-check — `ChaosEStimArcLayer` (Z=125) + `ChaosVibeTrailLayer`
(Z=128) added 2026-07-05, the owner-authorized E-Stim arc + vibe-pop cursor trail). The
window-migration lane is complete.

> **Fresh re-verification 2026-07-05 (post v6.2.10 merge `5603442`, by @glm5.2):** independent
> read-only re-sweep of every `: Window` subclass in `CCP.Avalonia` reconciles 1:1 with this
> audit — no new LIVE passive effect window appeared and the 6.2.10 merge added none (it touched
> only WPF-head files + shared loc/csproj/`UpdateService`; zero under `CCP.Avalonia` source).
> Section A counts 9 + 10 = 19 listed layers; the 20th is `AttentionCheckLayer` (Z=160, §B1#3,
> migrated) — the verdict's "20" is correct, the §A header "19" counts only the two §A tables.
> UPDATE 2026-07-05: `ChaosEStimArcLayer` (Z=125) has LANDED (builds green, harness Stage 4m PASS);
> the E-Stim arc (§D#6) now renders through the compositor via the owner-authorized
> `BubbleEngine.EStimBurstAt` → head `OnEStimArc` path. **DoD item 4 holds for the LIVE rendering
> set**. UPDATE 2026-07-05: all 4 formerly-unwired passive windows (EStim/EStimGlow/VibeTrail/
> SkiaFx) are now DELETED (dead code removed so they cannot be re-wired or mis-used). EStimGlow's
> charge-glow remains a DEFERRED FEATURE (the charged-pop mechanic), not a window to migrate.
>
> Independent re-verification post-landing (`fb0dfd1`/`fb5414` agent, 2026-07-05): slnf 0 err/0 warn,
> Core 542/542, smoke 44 tabs / Findings 5 / exit 0 — **identical to baseline, no regression**.

> **Progress 2026-07-05:** gaps #1–2 closed — `ChaosFxWindow`→`ChaosFxLayer` (Z=118, `8df68031`)
> and `ChaosWaveTimerOverlay`→`ChaosWaveTimerLayer` (Z=155, `16fe5a92`); both windows deleted;
> `--verify-layers` Stages 4j/4k PASS. Dead `AvaloniaBubbleWindow` deleted (`c8bb20a1`).
> All gates green each commit (slnf 0 · WPF 0 · Core 542 · verify-layers exit 0 · smoke Findings 5).
>
> **Window-migration lane complete:** the standalone attention-check — the last LIVE passive
> effect on a `Window` — was migrated to `AttentionCheckLayer` (Z=160, `--verify-layers` Stage 4l
> PASS; code co-mingled in co-agent commit `57f6f048`). The remaining 4 window-based effects
> (`ChaosEStim`/`EStimGlow`/`VibeTrail`/`SkiaFx`) are **unwired** (no live caller — blocked, not
> migration gaps). `AvaloniaOverlaySurface`
> was mis-listed as dead — it is a **live** `IOverlaySurface` consumer (see B3) and is kept.
> No interactive surface is wrongly a layer; no migrated effect regressed to a window.

---

## A. USES UCE — 21 registered layers (the done set)

Verified by `RegisterLayer` call sites.

### Session effects (9) — `Compositor/Layers/`
| Layer | Z | Registered by |
|---|---|---|
| `VideoLayer` | 10 | `AvaloniaVideoService:201` |
| `MandatoryVideoLayer` | 15 | `AvaloniaVideoService:219` |
| `FlashLayer` | 30 | `AvaloniaFlashService:94` |
| `SubliminalLayer` | 40 | `AvaloniaSubliminalService:62` |
| `BubbleLayer` | 45 | `AvaloniaBubbleService:73` |
| `BouncingTextLayer` | 50 | `AvaloniaBouncingTextService:70` |
| `BrainDrainLayer` | 55 | `AvaloniaOverlayService:93` (excluded surface) |
| `SpiralLayer` | 60 | `AvaloniaOverlayService:92` |
| `PinkTintLayer` | 70 | `AvaloniaOverlayService:91` |

Video path: Phase E complete (default flipped to compositor video; legacy
`AvaloniaMultiMonitorVideoService` + `VideoOverlayWindow` deleted).

### Passive chaos overlays (12) — registered in `AvaloniaHeadStubs`
| Layer | Z |
|---|---|
| `ChaosFieldFxLayer` | 100 |
| `ChaosDvdLayer` | 105 |
| `ChaosGifCascadeLayer` | 110 |
| `ChaosFlashWashLayer` | 115 |
| `ChaosFxLayer` | 118 |
| `ChaosEStimArcLayer` | 125 |
| `ChaosVibeTrailLayer` | 128 |
| `ChaosCursorGlowLayer` | 130 |
| `ChaosEffectBannerLayer` | 140 |
| `ChaosPopTextLayer` | 145 |
| `ChaosAnnouncerLayer` | 150 |
| `ChaosWaveTimerLayer` | 155 |

All 12 harness-verified (`--verify-layers`, `unified-compositor-engine-plan.md` Phase F; Stages 4j/4k/4m/4n added for ChaosFx/ChaosWaveTimer/ChaosEStimArc/ChaosVibeTrail).

---

## B. DOES NOT USE UCE — passive effects still rendering as WINDOWS (the gap)

These "just draw" (all click-through / `IsHitTestVisible=false`) yet run as their own
top-level `Window`. Per doctrine they should be `IAvaloniaLayer`s. Migration recipe:
`unified-compositor-engine-plan.md` §"How to migrate a chaos overlay".

> **Doc-drift fixed:** `unified-compositor-engine-plan.md` Phase F rows 11 (`ChaosWaveTimerOverlay`)
> and 13 (`ChaosFxWindow`) are marked "❌ unwired" — STALE. The chaos run-engine port (S5–S9)
> wired them; this sweep found live callers (cited below). Treat this audit as authoritative;
> correct the Phase F table when those two are migrated.

### B1 — LIVE (actively rendered as a window today) → highest priority
| # | Class | File | LOC | What it draws | Live caller |
|---|---|---|---|---|---|
| 1 | ~~`ChaosFxWindow`~~ ✅ **DONE → `ChaosFxLayer` (Z=118)** | (deleted 2026-07-05) | 172 | full-screen colour vignette pulses | was `_fx.Pulse`; now `_fxLayer.Pulse` on the compositor |
| 2 | ~~`ChaosWaveTimerOverlay`~~ ✅ **DONE → `ChaosWaveTimerLayer` (Z=155)** | (deleted 2026-07-05) | 243 | click-through wave-clock pill | was `Update/Clear/CloseActive`; now `_waveTimerLayer.SetValues/Hide/Clear` on the compositor |
| 3 | Standalone attention-check overlay | `Services/AttentionCheck/AvaloniaAttentionCheckService.cs:208-239` | 442 (svc) | pulsing gaze target (`AttentionCheckControl` in a bespoke transparent click-through `Window`) | `ScheduleNext`→`ShowCheck` when the feature is enabled (NOTE: gaze attention-check ships **dormant** per WPF pre-ship contract — lot 4/5 — so it is enabled-gated, but the code path is live and window-based) |

### B2 — UNWIRED (window exists, no live caller yet) → migrate when the feature is wired
These have no production caller today (the run-engine paths that drive them are the E-Stim
visual chain / vibe / skia-glow features, still unwired). Convert to layers as their callers land.
| # | Class | File | LOC | What it draws | Blocking note |
|---|---|---|---|---|---|
| 4 | ~~`ChaosEStimOverlay`~~ ✅ **arc DONE → `ChaosEStimArcLayer` (Z=125)** | `Chaos/ChaosEStimOverlay.axaml.cs` | 267 | lightning bolts between bubbles | Arc `Strike` now wired: owner-authorized `BubbleEngine.EStimBurstAt` → head `OnEStimArc` → `ChaosEStimArcLayer.Strike` + throttled `estim_zap` cue (2026-07-05). Window DELETED 2026-07-05 (arc renders via the layer; dead code removed) |
| 5 | ~~`ChaosEStimGlowOverlay`~~ 🗑 **DELETED 2026-07-05** | `Chaos/ChaosEStimGlowOverlay.axaml.cs` | 184 | E-Stim charge glow halo | Dead/unwired window removed; its charge-glow = the DEFERRED charged-pop FEATURE (not ported), so the window is gone and can't be mis-wired |
| 6 | ~~`ChaosVibeTrailOverlay`~~ ✅ **DONE → `ChaosVibeTrailLayer` (Z=128)** | `Chaos/ChaosVibeTrailOverlay.axaml.cs` | 300 | warm cursor glow + fading trail | MIGRATED 2026-07-05: wired to the already-live vibe_popping toy lifecycle (StartVibeTrail on UseToy + 16ms IPointerState cursor feed, StopVibeTrail on expiry/teardown). Window DELETED 2026-07-05 |
| 7 | ~~`ChaosSkiaFxOverlay`~~ 🗑 **DELETED 2026-07-05** | `Chaos/ChaosSkiaFxOverlay.cs` | 632 | Skia bloom + sparks (WPF's DEFAULT glow renderer) | Dead/unwired superset renderer removed; its effects (glow/bolts/burst/ripple/trail) render via the simpler layers now — no longer a consolidation target |

### B3 — DEAD / VESTIGIAL cleanup
| Class | File | LOC | Finding |
|---|---|---|---|
| ~~`AvaloniaBubbleWindow`~~ ✅ **DELETED `c8bb20a1`** | (deleted 2026-07-05) | 95 | 0 callers (grep-verified repo-wide); bubbles render on `BubbleLayer`. `AvaloniaBubble` (inner visual) retained. |
| `AvaloniaOverlaySurface` — **NOT dead, KEEP** | `Platform/AvaloniaOverlaySurface.cs` | small | Earlier "dead" call was WRONG. It is the **live** `IOverlaySurface` impl: DI-registered (`ServiceCollectionExtensions.cs:130`), injected + resolved in `MainWindowViewModel` (`:48`/`:110`), and consumed by **Core `AchievementService.cs:918-919`** which gates on `CoreApp.Overlay is IOverlaySurface surface && surface.IsVisible`. Do not delete — it is a `Window` acting as a visibility seam, not a rendered effect surface. |

---

## C. Justified windows (correctly NOT layers — interactive per doctrine §3)

Not defects. Listed so a future sweep doesn't re-flag them.

- **Interactive chaos surfaces** (user clicks IN them, hook click-swallow gap): `ChaosHudWindow`,
  `ChaosBoonBarOverlay`, `ChaosToyButtonWindow`, `ChaosOverlayWindow` (Pick buttons / boon draft),
  `ChaosUnlockCardOverlay`. (The experimental `ChaosBubbleHostOverlay` shared-host bubble renderer was DELETED 2026-07-05 — chaos bubbles always use the compositor `BubbleLayer`; the `ChaosBubbleSharedHost` setting stays in Core for the WPF head only.)
- **Chaos non-effect UI**: `ChaosHubWindow` (dollhouse hub), `ChaosIntroWindow` (modal intro card).
- **Interactive session surfaces**: `LockCardWindow` (type-to-unlock; `LockCard=20` z is reserved
  but there is deliberately no `LockCardLayer`), `MantraWindow`, `QuizWindow`, `PopQuizWindow`,
  `BubbleCountResultWindow`, `AvatarTubeWindow`.
- **App shell / dialogs / popups**: `MainWindow`, all `Dialogs/*`, and the transient
  notification/celebration popups (`AchievementPopup`, `QuestCompletePopup`, `PinkRushPopup`,
  `AnnouncementPopup`, `SeasonRecapWindow`, `SessionCompleteWindow`, `EasterEggWindow`) — dismissable
  dialogs, not continuous overlays the user works under.
- **Media/help/editor windows**: `MiniPlayerWindow`, `HelpVideoWindow`, `ModCreatorWindow`,
  `SessionEditorWindow`, Deeper editor/player windows, webcam windows, splashes, `TutorialOverlay`.
- **The UCE host itself**: `CompositorWindow` (one per monitor — this *is* the compositor).
- **Debug-only** (not production): `VideoSpikeWindow`, `InlineLoopSpikeWindow`, `AudioSpikeWindow`.

### C-review — borderline (needs a product call, not obviously a gap)
- `BubbleCountWindow` (+ per-monitor windows :164/:171, per-bubble sub-windows, plays its own
  video): an **interactive** minigame (bubbles are clicked) that also plays video and spawns many
  windows. Bringing it onto the UCE (video layer + bubble layer + interactive hit-test) is a large,
  low-ROI effort. Flag as review; not scheduled.

---

## D. Migration backlog (ordered) → file as task-board rows

One class per commit, per the migration recipe; gates each commit (slnf 0 · WPF 0 · Core
floor · `--verify-layers` exit 0 · `--verify-video` exit 0 · `--smoke-test` Findings 5).

1. ~~**`ChaosFxWindow` → `ChaosFxLayer`**~~ ✅ **DONE 2026-07-05** (Z=118; `--verify-layers`
   Stage 4j PASS; only the live `Pulse` ported, dead edge/heat/freeze API dropped).
2. ~~**`ChaosWaveTimerOverlay` → `ChaosWaveTimerLayer`**~~ ✅ **DONE 2026-07-05** (Z=155;
   `--verify-layers` Stage 4k PASS; Skia text pill, primary-monitor only).
3. ~~**`AvaloniaBubbleWindow` delete**~~ ✅ **DONE `c8bb20a1`** (dead, 0 callers). **`AvaloniaOverlaySurface`
   JUSTIFIED** — not dead; live `IOverlaySurface` consumer (Core `AchievementService` visibility gate + `MainWindowViewModel`). Kept.
4. ~~**Standalone attention-check → layer**~~ ✅ **DONE 2026-07-05** (`AttentionCheckLayer` Z=160;
   `--verify-layers` Stage 4l PASS `DIFFER center-crop`, clean Hide fade). Confirmed PASSIVE
   (`IsHitTestVisible=false` + webcam gaze-dwell). New layer + service rewire (Window→layer,
   self-registered via injected `CompositorEngine`, WPF 180 ms opacity fade preserved) + Stage 4l.
   All gates green. **Last LIVE window-based passive effect — the window-migration lane is now
   complete.** (Code co-mingled in co-agent commit `57f6f048` via a broad `git add`; verified in HEAD.)
5. ~~`ChaosSkiaFxOverlay` → layer~~ **RETIRED 2026-07-05: window DELETED (dead code); its effects
   render via the simpler layers, so no consolidation is needed.**
6. **E-Stim arc DONE 2026-07-05** (`ChaosEStimOverlay` → `ChaosEStimArcLayer` Z=125; window DELETED).
   `ChaosEStimGlowOverlay` window also DELETED 2026-07-05; its charge-glow is the deferred
   charged-pop FEATURE (not a window migration).
7. **VibeTrail DONE 2026-07-05** (`ChaosVibeTrailOverlay` → `ChaosVibeTrailLayer` Z=128; the vibe_popping toy path was already live — only the visual was unported).

Interactive set (section C) stays windows until the `AvaloniaMouseHook` click-swallow gap is
resolved (WP3 JUDGMENT / owner decision).

## Guardrails carried into every migration
- Geometry in PHYSICAL px (DIP→px via `screen.Scaling`); Skia objects built ONCE (zero per-frame
  alloc); state under one lock; `IsActive` content-driven; z from `CompositorLayers` only.
- `ExcludeFromCapture` stays `false` for chaos (capture-VISIBLE — no WPF chaos window sets
  `SetWindowDisplayAffinity`). Never touch the subliminal/main-surface capture contract.
- Never modify the WPF head; never edit `Compositor/*` engine internals for a layer migration
  (new `Layers/*` classes are fine).
