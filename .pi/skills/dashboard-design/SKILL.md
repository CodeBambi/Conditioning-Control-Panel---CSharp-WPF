---
name: dashboard-design
description: "Design and verify any user-facing greenfield client surface under client/: dashboard, cards, feature popups, tabs, dialogs, panels, themes, and AvatarTube-adjacent layout. Preserves CCP's five-theme dark-neon grammar, lit/unlit/locked card meaning, stable quick-toggle behavior, responsive layout, and accessible scrollable popups without inheriting WPF or first-attempt resource topology."
---

# dashboard-design

## Evidence

Use `img state/` and WPF as visual/product evidence. Read `client/docs/capability-inventory.md#dashboard-and-feature-popups`, architecture A-004/A-005, and the task row. Do not copy WPF dimensions/resource names or first-attempt theme services/brush algorithms by default.

## Visual grammar

Support the five built-in identities:

- CCP Default;
- Bambi Sleep;
- Sissy Hypno;
- Dronification;
- Circe's Lock.

The dashboard is dark with theme-specific neon accents, readable themed artwork, restrained glow, and clear hierarchy. Theme switching updates visible colors, artwork, contrast, and state without restart.

### Feature cards

- **Enabled:** theme accent border and glow.
- **Disabled:** dark/unlit border; artwork remains readable.
- **Locked:** visible lock treatment, active ring suppressed, quick-toggle rejected.
- **Neutral:** `Visuals` and `System` have no invented aggregate on-state.

State reflects the live feature, not stale markup. Changes from cards, popups, automation, settings, or sessions update immediately.

### Interaction

- Left-click opens a modeless feature settings popup.
- Plain right-click on every unlocked toggleable card immediately invokes the stable feature command, persists, and live-starts/stops during a session.
- Localized title, artwork, capitalization, and visual-tree position are never command identity.
- Help and test actions remain separate and cannot steal plain right-click.

## Feature popups

- Owned, modeless, centered on the owner, and within the owner's monitor working area.
- Compact for short content; finite viewport for tall content.
- Vertical wheel, trackpad, touch, keyboard bring-into-view, scrollbar controls, and thumb dragging work.
- Horizontal scrolling disabled unless a feature contract explicitly requires it.
- Nested scrolling chains predictably.
- Mixed scaling and secondary monitors do not hide controls.
- Close/Escape/focus restoration follows the per-window contract.

## Window behavior

Shared chrome may share theme resources but must not flatten semantic differences. Read the window manifest/task: ownership, modality, activation/focus, taskbar/Alt-Tab, topmost, resize, placement, decorations, close/hide/reuse, and shutdown are per-window behavior.

## Implementation rules

- Use theme tokens/resources instead of hardcoded theme colors.
- Follow Avalonia's selector/class/pseudo-class styling model; do not port WPF `Style.Triggers`, `DataTrigger`, `VisualStateManager`, `BasedOn`, or keyed-style assignment literally. Use bindings/converters when state is data-driven.
- Put templates in the appropriate `DataTemplates` collection and use Avalonia template/type matching rather than assuming WPF resource lookup.
- Keep text localizable and artwork free of baked-in UI strings unless approved.
- Verify current Avalonia v12 styling, resource, popup, scrolling, and animation APIs through `avalonia-research`.
- Prefer responsive layout and minimum readable sizes over literal WPF pixels.
- Do not use transforms that make hit-testing, scrolling, or native controls unreliable.
- Ensure keyboard navigation, focus visibility, contrast, and scaling.

Current source: [official WPF migration guide](https://docs.avaloniaui.net/docs/migration/wpf/) and [cheat sheet](https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet). Verify deeper styling/binding pages for the selected pattern.

## Avalonia MCP for dashboard and UX work

When admitted and available in Pi, use `decriptor/AvaloniaUI.MCP` as a bounded design-review assistant, not a dashboard generator.

Good uses:

- validate a small AXAML fragment for basic XML/namespace and common-pattern mistakes;
- ask for candidate selector, pseudo-class, responsive-layout, keyboard/focus, contrast, and accessibility concerns;
- run its heuristic performance pass on one complex card, popup, or template;
- use its control reference to discover candidate primitives, then verify them in current v12 docs.

Do not use its generated themes, palettes, controls, animations, visual states, architecture, or WPF conversion as production output. Those generators are generic, do not know CCP's five themes or lit/unlit/locked semantics, and upstream source pins Avalonia 11.3.1 while emitting stale WPF patterns. The MCP cannot judge CCP visual hierarchy or rendered fidelity.

For a user-facing slice, the recommended chain is: contract and screenshots -> current official v12 docs -> hand-authored smallest AXAML -> advisory MCP review -> real build/headed interaction -> K3 screenshot review. Record accepted and rejected MCP findings rather than copying its response.

## Acceptance

On Windows and Linux:

1. sweep all five themes live;
2. sweep supported languages;
3. exercise every card stopped and during a session;
4. verify immediate state rings and locked/neutral/help exceptions;
5. open every popup on primary/secondary mixed-scale monitors and reach the final control through every required scrolling path;
6. resize/move/minimize/restore owner and verify focus/window behavior;
7. compare composition to the appropriate `img state/` reference without requiring identical implementation.

Screenshots prove appearance; headed interaction proves behavior.

## Consultation

Use council for new theme architecture, major responsive layout, window-shell changes, accessibility tradeoffs, or divergence from the approved screenshots/behavior. Supply screenshots, contracts, and platform evidence.

## Related skills

- `wpf-parity`, `avalonia-research`, `port-feature`, `port-plan`.
