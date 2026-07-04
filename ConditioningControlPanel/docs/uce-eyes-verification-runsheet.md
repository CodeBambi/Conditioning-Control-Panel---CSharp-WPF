# UCE Side-by-Side Eyes-Verification Run-Sheet (WP2 gate for Phase E)

Created 2026-07-04. Owner-facing. This is the ONLY remaining human step before
`unified-compositor-engine-plan.md` Phase E (flip default to UCE-only + delete legacy
video windows) is unblocked. Everything below is what the automated harnesses could NOT
assert (they proved registration/z-constants/render-deltas/teardown; they cannot judge
literal pixels, easing feel, or cross-window visual order).

Record results by editing the two `[~]` rows in `unified-compositor-engine-plan.md`
(lines ~87, ~93, ~142) to `[x]` with a dated note per check, and mark the matching rows
in `avalonia-ui-parity-matrix.md`. Any FAIL: file a task-board row with what you saw on
which monitor/DPI, and leave the `[~]` as-is.

## Setup (two apps at once)

```bash
# Terminal 1 — WPF reference head (the behavior contract)
dotnet run --project ConditioningControlPanel/ConditioningControlPanel.csproj

# Terminal 2 — Avalonia head with UCE video opted in (layers are already default)
CCP_UCE_VIDEO=1 dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj
# (PowerShell: $env:CCP_UCE_VIDEO="1"; dotnet run --project ...)
```

- Use the SAME assets folder for both heads so images/videos/timing sources match.
- Best comparison mode: trigger the same effect in both heads back-to-back and watch on
  the same monitor. For timing checks, phone-record both screens and step frame-by-frame.
- Multi-DPI checks need one monitor at 100% and one at 125/150% scaling (Settings →
  Display) — remember both apps must be RESTARTED after changing scaling.

## Checklist

### A. Per-layer timing / opacity / easing (UCE plan line ~142)

Trigger each effect in both heads; compare feel, fade curves, and opacity ceilings.

| # | Effect | How to trigger | What to compare | Pass? |
|---|--------|----------------|-----------------|-------|
| A1 | Flash images | Start session with flash on (or dashboard one-shot) | fade-in/out duration, stagger (300ms; hydra children 100ms), max opacity, random placement spread | [ ] |
| A2 | Subliminal | Enable subliminals, wait a cycle | flash duration, opacity %, font/placement | [ ] |
| A3 | Bouncing text | Enable bouncing text | speed, edge bounce, opacity, text pool | [ ] |
| A4 | Bubbles | Enable bubbles | spawn rate, float speed, wobble, pop visual | [ ] |
| A5 | Pink tint | Enable pink filter | tint strength matches slider %, whole-screen coverage | [ ] |
| A6 | Spiral | Enable spiral | rotation speed, opacity, center placement, GIF/skia parity | [ ] |
| A7 | Brain drain | Enable brain drain | blur/effect strength, ramp-in | [ ] |

### B. Visual z-order between layers (harness asserted constants only)

Expected bottom→top: Video(10) < MandatoryVideo(15) < LockCard(20, still a window) <
Flash(30) < Subliminal(40) < Bubbles(45) < BouncingText(50) < BrainDrain(55) <
Spiral(60) < PinkTint(70).

| # | Check | Pass? |
|---|-------|-------|
| B1 | Enable flash + subliminal + spiral + pink tint simultaneously: subliminal draws OVER flash; spiral over both; pink tint tints EVERYTHING | [ ] |
| B2 | Play a mandatory video with flash+spiral on: effects render OVER the video | [ ] |
| B3 | Same as B1 on the WPF head: relative order identical | [ ] |

### C. Click-through over effects

| # | Check | Pass? |
|---|-------|-------|
| C1 | With spiral + pink tint + bouncing text active: click/type into a browser and an editor underneath — every click lands, no focus steal, cursor normal | [ ] |
| C2 | With flash clickable ON: clicking a flash image pops it (hydra multiplies if on); clicking BESIDE it passes through | [ ] |
| C3 | Known gap (decide, don't fix here): Avalonia pop clicks also LEAK to the app underneath (hook can't swallow — WP3 decision row) — confirm current behavior and note it | [ ] |

### D. Mixed-DPI placement (100% + 125/150% monitors)

| # | Check | Pass? |
|---|-------|-------|
| D1 | Flash images land fully on-screen with sane sizes on BOTH monitors | [ ] |
| D2 | Spiral centered per-monitor; pink tint covers both monitors edge-to-edge | [ ] |
| D3 | Subliminal/bouncing text position correctly on the scaled monitor | [ ] |

### E. UCE video, eyes-on (UCE plan lines ~87, ~93)

With `CCP_UCE_VIDEO=1`:

| # | Check | Pass? |
|---|-------|-------|
| E1 | Mandatory video plays FULL-SCREEN, no black bars/letterbox drift, correct aspect | [ ] |
| E2 | Dual-monitor: video composites on every monitor (WPF contract) | [ ] |
| E3 | Audio: volume slider, device routing, mute all work during layer playback | [ ] |
| E4 | Attention check appears over video, is clickable, pass/fail credit works | [ ] |
| E5 | Segment (random-slice) mode: starts mid-video ~700ms after length known | [ ] |
| E6 | Loop + natural end + watch-credit at teardown behave as WPF | [ ] |
| E7 | Known documented gap: non-strict ESC-dismiss + panic key have NO receiver on the layer path (no global key hook). Confirm and decide: acceptable for Phase E, or blocks it? | [ ] |

### F. Re-run harnesses after any code change made during this session

```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --verify-layers   # expect exit 0, 15/15
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --verify-video "<local .mp4>"   # expect exit 0
```

## On full pass

Phase E is unblocked: flip UCE video to default, audit the 9 `IMultiMonitorVideoService`
references, delete `AvaloniaMultiMonitorVideoService` + `VideoOverlayWindow` + per-overlay
`*Window` classes + DI registrations (UCE plan lines ~151-155). E7 must be explicitly
accepted or fixed first — it is the only functional regression candidate on the list.
