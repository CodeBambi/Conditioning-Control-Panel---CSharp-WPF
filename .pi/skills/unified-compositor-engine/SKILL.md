---
name: unified-compositor-engine
description: "Plan, research, implement, or verify shared real-time visual composition in the greenfield client under client/: video frame fan-out, deterministic ordering, tint/spiral layering, overlays, capture boundaries, performance, and multi-monitor geometry. Preserves the successful shared-composition idea and safety contracts without copying the first attempt's UCE classes, interfaces, timers, Skia lease, fixed z-values, windows, or status claims."
---

# unified-compositor-engine

This skill retains the product principle, not the old engine implementation.

## Approved direction

Read architecture A-001 and A-003 plus `capability-inventory.md#video-presentation`.

- Overlapping real-time visuals share a composition domain by default.
- Ordering is explicit and deterministic, never accidental native-window activation order.
- Separate windows are allowed when observable focus, accessibility, native control, capture, security, or isolation behavior requires them.
- Shared composition does not imply shared state ownership, input policy, or capture policy.

The following are not approved merely because the first attempt used them: compositor classes, layer interfaces, DI lifetime, Skia lease API, timer cadence, fixed numeric z-values, window count, frame buffers, hooks, capture-affinity split, or package versions.

## Current ordering contract

Verified client decisions currently establish:

1. video frame and opaque aspect-fit bars at the bottom;
2. spiral above video and bars;
3. bounded color overlay above spiral;
4. other effects require explicit relative-order contracts before implementation.

Do not invent or import remaining z-order. Record product decisions in client docs.

## Tint safety

- Actual rendered tint never reaches 100% opacity.
- Persistent user state and temporary requests have separate ownership.
- Video, browser, mandatory playback, ramps, pulses, failures, panic, display changes, and teardown never overwrite persistent tint state.
- Overlapping temporary requests remain below the ceiling and restore the latest valid underlying value deterministically.
- Acceptance observes rendered pixels/state, not only settings.

## Unified video presentation

For mandatory, local, direct URL, and supported online sources:

- one native decoder;
- one playback clock/source/network lifecycle;
- one audible stream and output-device state;
- the same presented frame fanned out to every monitor;
- independent aspect-fit and centering per monitor;
- opaque black letterbox/pillarbox bars beneath spiral/tint;
- no browser-fullscreen or browser-screen-capture mirror fallback;
- clean end/error/panic/hot-plug/shutdown teardown.

Handle negative coordinates, monitors above/below, gaps, mixed scaling/resolution, portrait/flipped orientation, rearrangement, and hot-plug. Prove whether the OS already applies orientation before adding transforms.

## Layer contract

Every visual effect must define:

- state owner and lifecycle;
- active/dirty condition;
- monitor scope and geometry space;
- relative order;
- blend/opacity and safety bounds;
- input policy through `overlay-clickthrough`;
- capture inclusion/exclusion;
- update cadence and invalidation ownership;
- background-thread handoff and disposal;
- failure and teardown behavior;
- Windows/Linux acceptance.

Services/product logic own state; render code presents immutable/synchronized snapshots. Never draw from decoder/background threads directly into the render target.

## Research and implementation selection

Use `avalonia-research` before choosing custom drawing, render-thread scheduling, direct Skia integration, surfaces, native windows, invalidation, decoder packages, or Linux graphics paths. Prefer stable current APIs. Unstable APIs need an approved spike and fallback strategy.

The official migration cheat sheet maps WPF `CompositionTarget.Rendering` to `TopLevel.RequestAnimationFrame` for UI-thread frames and points to composition custom visuals for render-thread callbacks. Treat this as a research starting point, not approval of either primitive; measure the greenfield workload before selecting it.

Treat first-attempt allocation measurements and buffer designs as experiment evidence only. Profile the greenfield implementation before optimizing. Avoid per-frame allocations/copies when measured, but do not pre-copy old complexity.

## Verification

- Simultaneously render video, bars, spiral, tint, and representative higher effects; prove order.
- Observe tint through overlap, error, cancellation, and teardown.
- Use frame-numbered media and instrumentation to prove one decoder and identical presented frame across all screens.
- Verify one audio/source lifecycle regardless of monitor count.
- Test display topology/orientation/hot-plug.
- Verify input and capture policies independently.
- Measure startup, frame cadence, allocations, CPU/GPU, memory, and stalls against an approved client baseline when one exists.
- Run headed Windows and Linux X11/Wayland acceptance for every claimed backend.

Compilation or a registered layer is not proof that pixels render.

## Consultation and stop conditions

Council is required for render architecture, package/decoder admission, native surfaces, input/capture split, layer-order decisions, or cross-platform degradation. Debate materially different viable designs. Stop if Linux behavior, tint safety, frame ownership, or headed proof is unresolved.

## Historical references

`ConditioningControlPanel/CCP.Avalonia/Compositor/` and this skill's `references/video-pipeline.md` are first-attempt evidence only. They may reveal pitfalls but never establish greenfield completion or mandatory internals.

## Related skills

- `avalonia-research`, `overlay-clickthrough`, `wpf-parity`, `port-plan`, `port-feature`.
