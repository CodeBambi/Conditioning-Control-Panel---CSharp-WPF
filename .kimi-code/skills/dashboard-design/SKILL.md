---
name: dashboard-design
description: "CCP's visual design language: themed neon dashboard, feature cards with lit/unlit borders, per-mod reskinning. Use this skill when building or modifying ANY user-facing CCP surface (dashboard, cards, tabs, dialogs, panels), when choosing colors or brushes, when working with themes/mods, or when the user references the 'img state' screenshots or asks for the UI to look right/better. The core grammar: a colored glowing card border means the feature is ON, a dark unlit border means OFF, and every surface must re-skin across all five themes."
---

# dashboard-design

## Canonical visual reference

`img state/` at the repo root (filenames contain spaces) holds the approved dashboard look, one per theme:
`good view.png` (Sissy Hypno, purple), `default good view.jpg` (CCP Default, pink), `bambi sleep good view.jpg` (Bambi Sleep, pink/magenta), `drone good view.jpg` (Dronification, matrix green), `circe lock good view.jpg` (Circe's Lock). Look at them before designing anything; they are the acceptance target.

The port's license to differ: **UX may look somewhat different from WPF as long as behavior is identical and this design language is kept.** Improvements are welcome; drift from the grammar below is not.

## The card grammar (the load-bearing convention)

A dashboard feature card = themed neon artwork + label + optional help "?" button.

- **ON**: border ring in the theme accent color + outer glow. WPF implementation: `Features/FeatureCard.xaml` `ActiveBorder` (BorderBrush `{DynamicResource PinkBrush}`, thickness 3.5) + `ActiveGlow` DropShadow (opacity 0.55), driven by the `IsActive` property.
- **OFF**: border dark/unlit; artwork stays visible but the card reads as dormant.
- **Locked**: the active ring is SUPPRESSED even if the underlying setting is true, and right-click toggle is ignored (`FeatureCard.xaml.cs` `ApplyActiveState`). A `LockedOverlay` communicates the lock.
- **Neutral cards**: Visuals and System have no single enabled flag; they stay unlit by design. Do not invent an on-state for them.
- Interactions: left-click opens the feature's settings popup (non-modal); right-click quick-toggles the feature AND starts/stops the running service.

State is driven from `AppSettings` flags (`FlashEnabled`, `SpiralEnabled`, `PinkFilterEnabled`, ...) via `MainWindow.Presets.cs` `RefreshFeatureCardActiveStates()` + a settings `PropertyChanged` listener in WPF. Any Avalonia card surface must keep this live: toggling a setting anywhere updates the border immediately, no restart, no tab switch.

## Layout regions (from the screenshots)

Top: title bar; nav chips (Dashboard, Presets, Quests, Enhancements, Deeper, Subjects, Assets, language); second row (Achievements, Leaderboard, Companion, Profile, Lab, Premium); level/XP progress bar.
Left/main: 4x4 feature-card grid (`VelvetFeatureGrid` in WPF's `Views/Tabs/SettingsTabView.xaml`; the settings tab IS the home dashboard) around a 2x2 center emblem that carries the active theme's branding.
Right column: Browser panel (HypnoTube/BambiCloud connect, webcam tracking, pop out), Audio panel (Master/Video/Duck sliders, output device, duck toggle, test), Quick Links.
Bottom: Webcam / App Info / Scheduler+Intensity Ramp / Catalogue buttons; marquee news ticker; START (accent) / Save / Exit (red) action bar.
Left edge: companion character in a tube with speech bubble.

## The theme system (themes ARE mods)

- Schema: `CCP.Core/Models/ModManifest.cs` `ModTheme` - `AccentColor`, `BackgroundColor`, `PanelColor`, `SurfaceColor`, `FilterColor` (hex strings). Secondary accent (WPF `ModService.GetSecondaryColorHex`): four built-ins hardcode it (CCP Default, Bambi Sleep, Sissy Hypno, Dronification); Circe's Lock and custom mods derive it via HSL hue shift (`ComputeSecondaryFromAccent`). The Avalonia head currently returns `Theme.AccentDarkColor` instead (`AvaloniaModService`) - a known divergence to be aware of when colors differ between heads.
- Five built-ins in `CCP.Core/Models/BuiltInMods.cs`: CCP Default (`builtin-ccp-default`), Bambi Sleep (`builtin-bambisleep`), Sissy Hypno (`builtin-sissyhypno`), Dronification (`drone-mode` - deliberately a community-style id), Circe's Lock (`builtin-locked`). Mods are `.ccpmod` ZIP packages; Drone and Circe's Lock ship large artwork packs.
- Switching: `ModService.ActivateMod()` fires `ModChanged`; WPF rewrites `Application.Current.Resources` color+brush keys in `RefreshThemeAwareElements()`; the Avalonia equivalent is `CCP.Avalonia/Services/Theme/AvaloniaThemeService.cs` (DI singleton). Re-skin is live, no restart.
- Card artwork is theme-resolved: `ModResourceResolver.ResolveImage("features/*.png")` prefers the active mod's extracted resources over built-in assets. Artwork path strings are case-inconsistent (`features/flash.png` vs `features/Pink_filter.png`); match exact strings when porting.
- Known per-theme quirk: CCP Default uses a 4-anchor `AccentGradientBrush`; all other mods get a solid accent gradient. Preserve it.

## Rules

1. **Never hardcode a hex color for anything theme-related.** Bind `DynamicResource` theme keys (`PinkBrush`, `PinkColor`, `DarkerBg`, `PanelBg`, `SurfaceBg`, `SecondaryBrush`, `AccentGradientBrush`, ...). A hardcoded accent passes on one theme and fails the other four.
2. **Every new surface must pass the 5-theme sweep**: switch all five mods live and confirm colors, artwork, and readability follow. This is part of the port's definition of done.
3. **On/off affordance is mandatory** for anything representing a toggleable feature: lit accent border + glow when on, unlit when off, suppressed when locked. Do not substitute checkmarks or toggle switches on dashboard cards.
4. Dark base, neon accent: backgrounds come from theme Background/Panel/Surface keys; accent is used for borders, glows, highlights, and primary actions, not for large fills (exception: START button and the pink filter itself).
5. Remember `DynamicResource` cannot feed converter properties in Avalonia; restructure (bind the brush directly or use StaticResource for converter inputs).
6. Respect localization: no baked-in English strings in artwork or XAML; keys via the loc merge workflow (`port-feature`).

## Verifying a design change

Run the Windows head, open the dashboard, and check: card borders reflect actual settings state live (toggle via right-click and via the feature popup); the 5-theme sweep; window resize behavior; then compare the overall composition against the matching `img state/` screenshot. For behavior questions (what a card click must do), defer to `wpf-parity`.

## Related skills

- `wpf-parity` - behavior must match WPF even when looks improve
- `port-feature` - implementation workflow + AXAML conversion cheatsheet
- `avalonia-research` - before using any new Avalonia styling/animation API
