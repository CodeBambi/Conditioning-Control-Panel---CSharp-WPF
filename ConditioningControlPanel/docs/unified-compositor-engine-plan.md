# Unified Compositor Engine (UCE) — State & Work Plan

> **Status:** UCE rendering surface COMPLETE. WS1 (video) and WS2 (chaos + passive-overlay
> migration) are DONE; the legacy multi-window video path is DELETED. This is the **single
> canonical UCE tracker**: it records **state** (what renders, the layer registry) and **live
> work** (the per-region input mask, the FPS re-baseline, the optional libmpv spike). The
> **rules** for authoring/editing layers live in the skill, not here — *skill = rules,
> plan = state + work*.
>
> **Branch:** `feat/crossplatform` @ `5e3ed650` · **App:** 6.2.11 · **Re-crowned 2026-07-10**
> (docs rework; this file absorbed the surviving intent of the former standalone UCE goal doc,
> now retired — the full deletion record lives in `docs-index.md`).

## Doctrine — UCE is THE mandatory render path

Every animated or real-time visual — video, flash, subliminal, spiral, brain drain, pink tint,
bouncing text, bubbles, keyword highlight, **all** chaos FX — renders as an `IAvaloniaLayer`
inside the one `CompositorEngine`: **one topmost `CompositorWindow` per monitor, z-ordered
layers, one 60Hz tick, PER-REGION click-through** (2026-07-09 team review: only the theme
color-filter and the spiral are ambient "tinted glass" the user works through; every other
active layer captures input over the region it paints; `AvaloniaMouseHook` swallows clicks
inside the capture mask). **No new per-effect windows, ever.** Interactive surfaces (main UI,
dialogs, AvatarTube, HUD, boon bar, lock card) stay windows.

The full matters/does-not-matter table, the rendering doctrine, the porting doctrine, and the
non-negotiable guardrails live in `skia-rebuild-goal.md` (the umbrella driver). This doc does
not restate them — it tracks UCE state and the remaining UCE work.

**Where to look:**
- **Layer-authoring rules (read BEFORE touching any layer):** `.pi/skills/unified-compositor-engine/SKILL.md`
  — persistent `SKImage` not `SKBitmap`; one engine-owned invalidation per frame; services own
  state, layers only render it; thread safety for background-thread frames; z from
  `CompositorLayers` only; the staggered per-monitor window creation; never `SetWindowSubclass`
  on an Avalonia HWND; the capture-affinity dual-surface split; the migration recipe. Plus
  `overlay-clickthrough` for all ex-style / hook / hit-test / topmost work.
- **Coverage ground truth:** `uce-coverage-audit.md` — the 22-layer registry verified against
  `RegisterLayer` call sites + the interactive-window justification.
- **Human visual verification:** `uce-eyes-verification-runsheet.md` — side-by-side WPF-vs-Avalonia
  checklist (timing/opacity/easing, z-order, click-through, mixed-DPI).
- **Work tracker:** `avalonia-migration-task-board.md`. UCE live work = board rows **#1**
  (per-region input mask), **#2** (FPS re-baseline), **#3** (optional libmpv spike). Claim ONE
  row per session.

## 1. Status — WS1 (video) + WS2 (chaos / passive overlays): COMPLETE

The video path runs through the compositor and the legacy multi-window video path is gone.
Every passive effect that "just draws" is a layer; only interactive surfaces remain windows.

### WS1 — Video through the compositor (Windows): DONE, legacy path DELETED

Phases A→E in order: prove UCE video renders → reach parity with the legacy path → verify the
other migrated layers → perf pass → flip default → delete legacy (delete ONLY after proven by
running).

| Phase | Commit | What landed |
|---|---|---|
| A | `85fa6570` | `VideoLayer`/`MandatoryVideoLayer` render: LibVLC vmem → `SKImage`; the `--verify-video` harness bisect found no broken stage (the "does not render" premise was stale — docs had lagged the code). |
| B | `bbdb3077` / `99a50721` | Parity with the legacy path, rehomed onto the layer: audio (volume / output-device / mute), attention checks + duration + safety timer + segment (random-slice) mode, `VideoAboutToStart`/`VideoStarted`/`VideoEnded` timing, watch-position credit. Adversarial review verdict: safe to bank, 0 blockers. |
| C | `07c094e1` | `--verify-layers` harness — every migrated layer PASS (register/activate/render-delta/teardown; dual-surface capture affinity asserted both directions). |
| D | `37bd454a` | Perf: zero per-frame alloc (triple-buffered pinned decoder buffers + long-lived zero-copy `SKImage.FromPixels`); the per-layer `_renderTimer` folded into the engine's single `Update()` tick. `CompositionCustomVisualHandler` + dirty-rect evaluated → DEFERRED (no profiling need; idle ~121 / active ~130 fps). |
| E1 | `6180efc2` | ESC/panic routed through the global `IInputHook` (the layer path has no window to receive keys). |
| E2 | `ed636a7c` | Default flipped to compositor video; user eyes-verified (video renders with spiral/pink tint compositing ON TOP — the "video covers the overlays" bug is fixed). |
| E3 | `8069cfb7` | **Legacy path DELETED:** `AvaloniaMultiMonitorVideoService` + `IMultiMonitorVideoService` + `VideoOverlayWindow` removed (grep-confirmed 0 matches in `CCP.Avalonia`); `HasOpenVideoWindows => IsPlaying`, `PrimaryVideoWindow => null`; no `CCP_UCE_VIDEO` / `CCP_LEGACY_VIDEO` env gate. The compositor `VideoLayer` / `MandatoryVideoLayer` are the only video path. |

**Acceptance met (2026-07-05):** `CCP.Desktop.slnf` 0 errors · WPF sln 0 · Core tests pass
(count never decreases) · `--verify-video` exit 0 · `--verify-visible` shows
`Video=ACTIVE + Spiral=ACTIVE + PinkTint=ACTIVE`.

### WS2 — Chaos run engine + passive-overlay migration: DONE

- **Run engine S1–S9** (S1–S4 JUDGMENT, then MECHANICAL):
  `2d7bc384` (S1–S4: `ChaosSpawnCatalog` + `ChaosRunRules` + `ChaosScoring` + `ChaosSpawnDirector`
  + live-lambda knobs via `ChaosRunKnobs`) → `490da8c6` (S5 draft/boon) → `f5fa0757`
  (S6 payload dispatch + heavy gate + `EffectPayload.Ambient`) → `87515732` (S7 run lifecycle +
  economy) → `f0fea4a0` (S8 hints + layer production callers) → `1f4c19fc` / `e61633c0`
  (S9 verify — benchmark clean, user-confirmed).
- **Dead passive windows deleted:** `ChaosFxWindow`→`ChaosFxLayer` `8df68031` (Z=118);
  `ChaosWaveTimerOverlay`→`ChaosWaveTimerLayer` `16fe5a92` (Z=155); `AvaloniaBubbleWindow`
  `c8bb20a1` (bubbles consolidated into `BubbleLayer`); the 4 formerly-unwired passive windows
  (`ChaosEStim` / `EStimGlow` / `VibeTrail` / `SkiaFx`) DELETED as dead code.
- **Window-migration lane COMPLETE:** the standalone attention-check — the last LIVE passive
  effect on a `Window` — migrated to `AttentionCheckLayer` (Z=160, `57f6f048`). No passive
  effect window remains in `CCP.Avalonia`.

## 2. Layer registry — 22 registered `IAvaloniaLayer`s

Verified by a live grep (`class …Layer` under `CCP.Avalonia/Compositor/`) and `RegisterLayer`
call sites (full audit: `uce-coverage-audit.md`). Z constants are authoritative in
`CompositorLayers.cs`; lower renders first (behind). The chaos band is **100–199, ABOVE
PinkTint (70)** — WPF `Chaos/ChaosWindowZ.cs` re-stacks every chaos window to the top of the
topmost band on show/arm (`RaiseTopmost` / `RaiseAboveVideo`).

**Session effects (9)** — `Compositor/Layers/`:

| Layer | Z | Registered by |
|---|---|---|
| `VideoLayer` | 10 | `AvaloniaVideoService` |
| `MandatoryVideoLayer` | 15 | `AvaloniaVideoService` |
| `FlashLayer` | 30 | `AvaloniaFlashService` |
| `SubliminalLayer` | 40 | `AvaloniaSubliminalService` |
| `BubbleLayer` | 45 | `AvaloniaBubbleService` |
| `BouncingTextLayer` | 50 | `AvaloniaBouncingTextService` |
| `BrainDrainLayer` | 55 | `AvaloniaOverlayService` (excluded surface — capture-hidden) |
| `SpiralLayer` | 60 | `AvaloniaOverlayService` |
| `PinkTintLayer` | 70 | `AvaloniaOverlayService` |

**Passive chaos overlays (12)** — registered in `AvaloniaHeadStubs`:

| Layer | Z | Note |
|---|---|---|
| `ChaosFieldFxLayer` | 100 | floor of the chaos band |
| `ChaosDvdLayer` | 105 | |
| `ChaosGifCascadeLayer` | 110 | |
| `ChaosFlashWashLayer` | 115 | |
| `ChaosFxLayer` | 118 | full-screen colour-vignette impact pulse |
| `ChaosEStimArcLayer` | 125 | `05520f52` |
| `ChaosVibeTrailLayer` | 128 | |
| `ChaosCursorGlowLayer` | 130 | the migration template |
| `ChaosEffectBannerLayer` | 140 | |
| `ChaosPopTextLayer` | 145 | |
| `ChaosAnnouncerLayer` | 150 | |
| `ChaosWaveTimerLayer` | 155 | |

**Attention-check (1):** `AttentionCheckLayer` — Z=160 (`57f6f048`; the last LIVE window-based
passive effect migrated).

**Not counted toward the 22** (non-layer infra): `BaseLayer`, `ChaosLayer`, `CompositorLayer`
(base classes), and `PlaceholderLayer` (test stub).

**Justified windows (correctly NOT layers — interactive per doctrine):** interactive chaos
surfaces (`ChaosHudWindow`, `ChaosBoonBarOverlay`, `ChaosToyButtonWindow`, `ChaosOverlayWindow`,
`ChaosUnlockCardOverlay`); `LockCardWindow` (`LockCard=20` z is reserved; there is deliberately
no `LockCardLayer`); `AvatarTubeWindow`; dialogs and transient popups; and `CompositorWindow`
itself (the host — one per monitor). Full list + the borderline `BubbleCountWindow` review:
`uce-coverage-audit.md` §C.

## 3. Live work — board rows #1, #2, #3

Nothing is in flight (post-crash reconciliation 2026-07-09 established no agent is working on
anything; any "claimed / WIP" note is historical debris, not a live lock). Claim ONE board row
per session; this section is the implementable detail those rows point at.

### Row #1 — Per-region input mask + `AvaloniaMouseHook` click-swallow  [JUDGMENT · HUMAN+SMART]

The 2026-07-09 team review scoped UCE click-through to **per-region**. This **SUPERSEDES** the
old "swallow: fix or accept" question — fixing the hook is now **REQUIRED scope, not optional**.

**The implementable contract:**

- **Ambient layers (input PASSES THROUGH):** the theme **color filter** (`PinkTintLayer`) and
  the **spiral** (`SpiralLayer`) ONLY. A screen region covered by ONLY those two is "tinted
  glass" the user works through.
- **Capture layers (input CAPTURED over painted region):** every OTHER active layer — video,
  mandatory video, flash, subliminal, brain drain, bouncing text, bubbles, keyword highlight,
  all chaos FX.
- **Capture mask:** the compositor exposes a per-frame mask = the **immutable union** of every
  active capture-layer's painted region (a snapshot taken on the tick; never mutated mid-frame).
- **Window ex-styles:** the per-monitor `CompositorWindow` stays
  `WS_EX_TRANSPARENT | WS_EX_LAYERED`. The `overlay-clickthrough` skill owns the exact ex-style
  lifecycle — never `SetWindowSubclass` on an Avalonia HWND (races the v12 window-proc and
  crashes with native `0xC0000005`).
- **Hook behavior:** `AvaloniaMouseHook` **SWALLOWS** clicks inside the mask (they do not reach
  the app behind); clicks over ambient-only or bare-desktop regions pass through. Include the
  WPF **hold-to-defuse no-swallow exception** — hold-to-defuse bubbles must still NOT swallow.
- **Deliberate divergence from WPF:** WPF keeps subliminal / flash / brain-drain click-through;
  the UCE captures them. This is a recorded **product decision** (2026-07-09 team review) — the
  loop-protocol "would diverge from WPF" stop-condition is satisfied.

**Open questions (resolve before/while implementing — record answers in the board row):**
1. Chaos-run behavior: should a chaos-run active state change the mask policy?
2. Keyboard vs pointer-only: does the mask block keyboard focus-stealing too, or pointer only?
3. Keyword highlight: it captures over the region it paints — but it can paint over the user's
   OWN text in another app. Decide the rule (product call).

Mechanism design and the X11 equivalent (XShape/XFixes input region) live in
`crossplatform-rebuild-plan.md` §7.4 and the `overlay-clickthrough` skill. Note: on Linux there
is **zero click-through code today** (`SupportsClickThrough = IsWindows`) — that is board epic
#5 (WP5), separate from this Windows-scoped row.

### Row #2 — FPS re-baseline + MinFps=0 video-stall investigation  [JUDGMENT]

The 2026-07-05 Release `--max-benchmark` (@glm5.2) held the floor — **AvgFps 138.7 ≫ 30 floor**
across a full run incl. a 60s Chaos phase — but the comparison to `benchmark-optimized.json` is
**environmentally invalidated on this machine, NOT a code regression:**

- **MinFps=0** is a ≥1s render stall correlated with **LibVLC web-video decode failures** — a
  video-path stall, NOT a Skia/UCE regression.
- Phase 2 is 120s of web video that **FAILED to decode** (~half the run); its LibVLC
  decode-retry loop accounts for BOTH the AvgFps drop and the ~4× CPU.
- Secondary confound: the 180s→240s duration drift (the extra 60s is the heaviest Chaos phase).

**The work:** re-baseline cleanly at 240s (or fix the 180→240 drift in the harness);
investigate the video-failure → render-stall (MinFps=0) link. Full analysis + evidence:
`benchmark-2026-07-05-analysis.md` (pairs with `docs/benchmark-optimized.json`). This is the
re-baseline row the kickoff gates reference — "not worse than `benchmark-optimized.json`"
carries this caveat until re-baselined.

### Row #3 — WP2b optional libmpv engine-swap spike  [JUDGMENT · benchmark-gated · AFTER Phase E]

Phase E is DONE, so the spike is unblocked but **optional / opportunistic** — the compositor
architecture is proven and the current LibVLC vmem path is the working baseline. Decision
record (owner-authorized 2026-07-04):

- **Sequencing (do NOT reorder):** WP2a (UCE video on the current LibVLC engine) is DONE.
  WP2b (the engine-swap spike) follows, benchmark-gated — one variable at a time in a
  correctness-critical subsystem.
- **Primary candidate:** `HanumanInstitute.LibMpv.Avalonia` — libmpv render API, near-zero-copy
  GL, excellent low-end perf + frame timing, cross-platform incl. Linux. Use mpv's **LGPL
  build** (`-Dgpl=false`) — same posture as today's LibVLC (LGPL dynamic link); the app is MIT.
- **Secondary (rejected for now):** libvlc 4 D3D11 output callbacks — LibVLCSharp 4 is still
  preview/nightly as of 2026-07; re-check before the spike.
- **Acceptance to adopt:** ≥20% CPU reduction OR measurably smoother frame pacing at 1080p on
  the low-end target; **zero behavior regressions** (attention checks, multi-monitor, loop,
  volume/device/mute, spikes, mini-player); behind the **same `IVideoService` / `VideoLayer`
  seams**; one engine per commit; **revert-not-patch** on any Windows regression.

## Acceptance criteria (folded from the UCE goal — the perf gate)

A UCE feature is accepted only when ALL hold — "code looks right" is a hypothesis; running it
is evidence:

1. **1:1 behavioral parity with the WPF head** for the ported effect — side-by-side, exercised
   end-to-end incl. the "when" case (trigger twice, switch monitor, etc.). The per-region
   capture policy (Row #1) is the one recorded deliberate divergence.
2. **No effect creates its own `Window`**; no per-service `Topmost` / `SetWindowPos` z-fighting
   (z comes from `CompositorLayers` only).
3. **At least as fast and lighter** than the multi-window approach — preferably measurably
   improved (the whole point: bounded memory, no per-effect window/compositor overhead). The
   perf gate and reference baseline live in `skia-rebuild-goal.md` and
   `docs/benchmark-optimized.json` (with the Row #2 re-baseline caveat).
4. **Don't claim parity you didn't exercise.** A harness PASS (`--verify-layers` /
   `--verify-video`) proves registration / render-delta / teardown — NOT literal pixels or
   easing feel. The human eyes-verification runsheet (`uce-eyes-verification-runsheet.md`)
   covers what the harness cannot.

## Verification ladder

```bash
# Gates before EVERY commit (all must pass; from repo root)
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly      # 0 errors
dotnet build ConditioningControlPanel.sln -clp:ErrorsOnly                   # 0 errors (WPF guardrail)
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release   # ALL pass; count NEVER decreases
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --smoke-test      # Findings: 5 baseline, exit 0

# UCE-specific (Debug builds)
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --verify-layers    # exit 0; all registered layers PASS
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --verify-video "<local .mp4>"   # exit 0
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --benchmark         # not worse than docs/benchmark-optimized.json (Row #2 caveat)
```

- **Gate snapshot (live 2026-07-10):** `CCP.Desktop.slnf` **0 errors**, Core tests **542/542**
  — re-run live before claiming them; the Core count is a floor that must never decrease.
- Watch `ccp-run.log` for `VideoLayer:` / `CompositorEngine` lines when diagnosing the video path.
- Behavior parity = side-by-side with the WPF head, per feature
  (`uce-eyes-verification-runsheet.md`).
