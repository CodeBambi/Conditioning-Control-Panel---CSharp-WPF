---
name: overlay-clickthrough
description: "Define, research, implement, or debug greenfield overlay input behavior under client/: transparency, passive click-through, interactive regions, focus, activation, topmost, Alt-Tab/taskbar, capture visibility, and multi-monitor behavior on Windows, Linux X11, and Wayland. Behavior first; never assumes the first attempt's Win32 styles, mouse hook, capture mask, or helper classes are the new design."
---

# overlay-clickthrough

## Start with the contract

Before selecting a mechanism, record for each effect:

- painted region and monitor scope;
- passive, capturing, or interactive pointer policy;
- whether a handled event may reach the application underneath;
- keyboard focus and activation behavior;
- taskbar/Alt-Tab and topmost behavior;
- capture inclusion/exclusion;
- behavior during minimize/restore, display changes, failure, and teardown;
- Windows acceptance;
- Linux X11 and Wayland acceptance or exact gap.

Read `client/docs/architecture.md`, `capability-inventory.md`, task board, and WPF evidence. Input policy for effects not yet documented is a product decision, not an invitation to copy a dated first-attempt rule.

## Safety invariants

- Passive regions allow normal desktop click, type, drag, and scroll.
- Interactive regions receive intended input.
- A handled overlay click does not unintentionally activate/click the underlying application unless explicitly required.
- Overlays do not unexpectedly activate, steal focus, or appear as ordinary task-switching windows.
- No failure leaves an invisible input-blocking or permanently topmost surface.
- Teardown and display/window transitions restore normal desktop input.
- A silent Linux no-op or disabled feature is not support.

## Research before mechanism

Use `avalonia-research` every time. Verify current Avalonia v12 and OS/backend behavior before choosing:

- framework hit testing/input pass-through;
- native input regions;
- Windows extended styles or hooks;
- X11 Shape/XFixes;
- Wayland surface/input-region support and compositor limitations;
- focus prevention and task-switcher behavior;
- capture exclusion APIs.

The official WPF migration cheat sheet explicitly warns that Avalonia transparent windows do not inherit WPF-like click-through behavior. Treat every WPF `AllowsTransparency`/layered-window assumption as unported until headed platform evidence proves the chosen mechanism.

Do not assume `WS_EX_TRANSPARENT`, `WS_EX_LAYERED`, `WS_EX_NOACTIVATE`, a low-level hook, window subclassing, a per-frame capture mask, or first-attempt helpers are approved. Historical crash workarounds are evidence to investigate, not architecture.

## Platform discipline

### Windows

Prove native handle timing, focus/activation, passthrough, handled-click swallowing, topmost transitions, capture behavior, and cleanup on supported Windows versions. Avoid unsupported window-procedure manipulation without current evidence and a spike.

### Linux

Test X11 and Wayland separately on named distributions/compositors/window managers. Document whether the backend permits input regions, non-activation, topmost, taskbar hiding, and mixed interactive/passive regions. If equivalence is impossible, file `PRODUCT DECISION REQUIRED`; never trap the desktop or claim support via no-op.

## Capture policy

Capture visibility is per feature contract. Do not infer it from click-through or shared composition. Self-capture prevention, streaming visibility, OCR feedback, and privacy can require different surfaces or mechanisms. Record and test both inclusion and exclusion.

## Verification

Use headed tests on Windows and Linux:

- click/type/drag/scroll through passive regions into another application;
- interact with every intended region;
- detect click leakage beneath handled interactions;
- test Alt-Tab/taskbar, focus, activation, minimize/restore, owner close, monitor hot-plug/rearrangement, panic/abnormal termination;
- verify capture inclusion/exclusion with actual capture;
- verify no orphan topmost or input-blocking surface remains.

## Consultation and stop conditions

Council is required before selecting native interop, global hooks, Wayland degradation, capture architecture, or changing an effect's input policy. Stop for unknown Linux equivalence, ambiguous product policy, privacy/safety changes, or an unverified v12 mechanism.

## Historical evidence

WPF and `ConditioningControlPanel/CCP.*` may show behavior and previous failures. Read narrowly. Do not edit or copy them. The old `references/callsite-catalog.md` is historical only and cannot define greenfield call sites.

## Related skills

- `avalonia-research`, `wpf-parity`, `unified-compositor-engine`, `port-feature`.
