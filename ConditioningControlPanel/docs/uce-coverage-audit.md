# UCE Coverage Audit — what does and does not render through the compositor

Date: 2026-07-05. Author: Kimi Agent (session pursuing `docs/skia-rebuild-goal.md`).
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

**Yellow.** The session-effect set and the 8 *live* passive chaos overlays are fully on
the UCE (17 registered layers). The remaining gap is **7 passive "just draws" effects that
still render as their own `Window`** (2 live + 4 unwired chaos + 1 live standalone
attention-check), plus **2 dead/vestigial windows to delete**. No interactive surface is
wrongly a layer; no passive effect that was migrated regressed to a window.

---

## A. USES UCE — 17 registered layers (the done set)

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

### Passive chaos overlays (8) — registered in `AvaloniaHeadStubs` (`RegisterLayer` :258–282)
| Layer | Z |
|---|---|
| `ChaosFieldFxLayer` | 100 |
| `ChaosDvdLayer` | 105 |
| `ChaosGifCascadeLayer` | 110 |
| `ChaosFlashWashLayer` | 115 |
| `ChaosCursorGlowLayer` | 130 |
| `ChaosEffectBannerLayer` | 140 |
| `ChaosPopTextLayer` | 145 |
| `ChaosAnnouncerLayer` | 150 |

All 8 harness-verified (`--verify-layers`, per `unified-compositor-engine-plan.md` Phase F rows 1–8).

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
| 1 | `ChaosFxWindow` | `Chaos/ChaosFxWindow.cs` | 172 | full-screen colour vignette pulses | `AvaloniaHeadStubs:2170` `new ChaosFxWindow()` + `_fx.Pulse(color,strength)` :2171 |
| 2 | `ChaosWaveTimerOverlay` | `Chaos/ChaosWaveTimerOverlay.axaml.cs` | 243 | click-through wave-clock pill | `AvaloniaHeadStubs.Update/Clear/CloseActive` :802/:1837/:1882/:2185/:2302 |
| 3 | Standalone attention-check overlay | `Services/AttentionCheck/AvaloniaAttentionCheckService.cs:208-239` | 442 (svc) | pulsing gaze target (`AttentionCheckControl` in a bespoke transparent click-through `Window`) | `ScheduleNext`→`ShowCheck` when the feature is enabled (NOTE: gaze attention-check ships **dormant** per WPF pre-ship contract — lot 4/5 — so it is enabled-gated, but the code path is live and window-based) |

### B2 — UNWIRED (window exists, no live caller yet) → migrate when the feature is wired
These have no production caller today (the run-engine paths that drive them are the E-Stim
visual chain / vibe / skia-glow features, still unwired). Convert to layers as their callers land.
| # | Class | File | LOC | What it draws | Blocking note |
|---|---|---|---|---|---|
| 4 | `ChaosEStimOverlay` | `Chaos/ChaosEStimOverlay.axaml.cs` | 267 | lightning bolts between bubbles | `Strike` has no head caller — Q10b (frozen `BubbleEngine` seam, BLOCKED on user authorization; ready spec `docs/chaos-run-engine-contracts/estim-arc-visual-slice.md`) |
| 5 | `ChaosEStimGlowOverlay` | `Chaos/ChaosEStimGlowOverlay.axaml.cs` | 184 | E-Stim charge glow halo | same E-Stim chain (`Arm`/`Disarm` unwired) |
| 6 | `ChaosVibeTrailOverlay` | `Chaos/ChaosVibeTrailOverlay.axaml.cs` | 300 | warm cursor glow + fading trail | no head caller (vibe-trail feature unwired) |
| 7 | `ChaosSkiaFxOverlay` | `Chaos/ChaosSkiaFxOverlay.cs` | 632 | Skia bloom + sparks; **WPF's DEFAULT glow renderer** (`ChaosSkiaFxEnabled ?? true`) | no head caller; natural consolidation target for the cursor-glow bloom variant |

### B3 — DEAD / VESTIGIAL → delete or justify (not a migration, a cleanup)
| Class | File | LOC | Finding |
|---|---|---|---|
| `AvaloniaBubbleWindow` | `Chaos/AvaloniaBubbleWindow.cs` | 95 | 0 callers (grep-verified); bubbles are on `BubbleLayer`. Delete. |
| `AvaloniaOverlaySurface` | `Platform/AvaloniaOverlaySurface.cs` | small | `IOverlaySurface` `Window` seam; injected into `MainWindowViewModel` (`_overlaySurface`) but only assigned, no render call found. Compositor (`PinkTintLayer` etc.) superseded it. Verify no consumer, then delete the impl (keep the Core seam if other heads need it). |

---

## C. Justified windows (correctly NOT layers — interactive per doctrine §3)

Not defects. Listed so a future sweep doesn't re-flag them.

- **Interactive chaos surfaces** (user clicks IN them, hook click-swallow gap): `ChaosHudWindow`,
  `ChaosBoonBarOverlay`, `ChaosToyButtonWindow`, `ChaosOverlayWindow` (Pick buttons / boon draft),
  `ChaosUnlockCardOverlay`, `ChaosBubbleHostOverlay` (bubble host input).
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

1. **`ChaosFxWindow` → `ChaosFxLayer`** (LIVE, 172 LOC, vignette pulses). Smallest live win;
   z in the field band. `_fx.Pulse` → service mutator driving the layer.
2. **`ChaosWaveTimerOverlay` → `ChaosWaveTimerLayer`** (LIVE, 243 LOC, wave pill). Info-text
   sub-band (≥140). `Update/Clear/CloseActive` → service mutators.
3. **`AvaloniaBubbleWindow` delete** (dead) + **`AvaloniaOverlaySurface` delete-or-justify**
   (vestigial) — cleanup, no behavior change.
4. **Standalone attention-check → layer** (LIVE-but-dormant): route `AttentionCheckControl`'s
   pulsing target through a compositor layer instead of a bespoke `Window`. Confirm the WPF
   contract + dormancy first (`wpf-parity`).
5. **`ChaosSkiaFxOverlay` → layer** (632 LOC, WPF default glow) — consolidation target; larger.
6. **E-Stim chain (`ChaosEStimOverlay` + `ChaosEStimGlowOverlay`) → layers** — gated on the
   Q10b frozen-`BubbleEngine` authorization landing (ready spec exists).
7. **`ChaosVibeTrailOverlay` → layer** — gated on the vibe-trail feature being wired.

Interactive set (section C) stays windows until the `AvaloniaMouseHook` click-swallow gap is
resolved (WP3 JUDGMENT / owner decision).

## Guardrails carried into every migration
- Geometry in PHYSICAL px (DIP→px via `screen.Scaling`); Skia objects built ONCE (zero per-frame
  alloc); state under one lock; `IsActive` content-driven; z from `CompositorLayers` only.
- `ExcludeFromCapture` stays `false` for chaos (capture-VISIBLE — no WPF chaos window sets
  `SetWindowDisplayAffinity`). Never touch the subliminal/main-surface capture contract.
- Never modify the WPF head; never edit `Compositor/*` engine internals for a layer migration
  (new `Layers/*` classes are fine).
