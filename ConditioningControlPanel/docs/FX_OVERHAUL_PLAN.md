# CCP FX Overhaul — Vision & Execution Plan

*Drafted 2026-07-30 from a 4-agent recon pass (UI surface inventory, Skia/compositor
infrastructure, mod theming, big-client design research) plus screenshots of all 12
reachable tab surfaces at v6.5.2.*

---

## 1. Vision — "the signage is already lit; make the room feel alive"

The CCP already has a strong, coherent identity: neon-pink signage art on deep
navy, glassy cards, chunky CTAs. What it lacks is *life between clicks* — most
tabs are static walls once rendered. Big clients (Riot, Steam, Opera GX,
Discord) solve this with a small number of laws, not with more effects:

1. **Two clocks.** Interaction motion 80–400ms; ambient motion 8–60s loops.
   Nothing in between (a 2s loop is the uncanny valley).
2. **Calm chrome, lively content.** Nav/buttons/forms only ever *react*
   (hover, press, transition). Ambient loops live in each tab's hero region —
   one focal looping element per view, everything else quiet.
3. **Event moments get the budget.** Level-ups, unlocks, quest completes get
   one-shot particle bursts — high perceived polish, zero idle cost.
4. **Theme from the mod, never from a name.** Every FX color resolves through
   the mod accent chain. Bambi = pink `#FF69B4`, Dronification = green
   `#00FF41`, Circe = magenta `#E81CA8` — automatically, with zero manifest
   edits for existing mods.
5. **Performance is a tier, not a hope.** Every loop is gated by
   `PerformanceProfile`, pauses when the window is unfocused/minimized, and
   degrades to static art — never "less pretty but still burning CPU."

The aesthetic direction per surface: **neon that breathes** (glow pulses on the
active/primary thing), **glass that catches light** (sheen sweeps on hover and
on rare events), **air in the room** (one slow mod-tinted fog/aurora drift
behind hero content), and **numbers that move** (odometer counters, bar fills
with cap-bloom).

---

## 2. What recon established (load-bearing facts)

### Rendering
- **Do NOT extend the fullscreen compositor** for in-window FX. It is
  strictly per-monitor topmost overlay windows, and keeping its shared tick
  alive for ambient loops would defeat the idle-parking that fixed #550.
- **The in-window pattern already exists and is proven:** plain
  `SKElement` (`IsHitTestVisible=false`) + self-stopping ~30ms
  `DispatcherTimer`. Reference implementations to copy wholesale:
  `Chaos/ChaosBackdropService.cs` (`OnPaint`/`ApplyBreath`/`DrawAuthoredFx`/
  `BuildBloom`, timer stops itself at `:143-169`) and the ChaosHub `MenuFog`
  (`Chaos/ChaosHubWindow.xaml.cs:2620+`, `FogPuff` sim).
- `SkiaSharp.Views.WPF 2.88.8` referenced; CPU `SKElement` path chosen
  deliberately (csproj comment) — no `SKGLElement`.
- **Viewbox trap:** all MainWindow content lives in a `Viewbox Stretch="Fill"`
  over a fixed 1489×901 canvas (`MainWindow.xaml:112-114`). SKElements inside
  it get bitmap-scaled (fine for soft fog washes; wrong for crisp particles).
  Crisp FX goes in `RootGrid` outside the Viewbox; author radii small.
- Cheap-tier fallback already shipped in-repo: animated `GradientStop`
  storyboards (`MainWindow.Enhancements.cs:603` `CreateAnimatedSkillTreeBrush`)
  and ellipse-opacity particle fields (`MainWindow.Animations.cs:194-226`).

### Theming
- `ModTheme` exists (7 hex slots, `Models/ModManifest.cs:100-122`). The runtime
  pattern to copy is `RefreshThemeAwareElements()`
  (`MainWindow.xaml.cs:1192-1440`) / `Services/RecapTheme.cs`: overwrite
  `Application.Current.Resources` Color+Brush keys → everything
  `DynamicResource`-bound repaints.
- Authoritative mod-switch signal: `ModService.ModChanged`
  (`Services/ModService.cs:42`, raised `:762`). Note the MainWindow theme
  refresh currently rides `ApplyActiveModChange()` instead — FX must subscribe
  to `ModChanged`.
- Built-in mods have `InstalledPath == null` → per-mod *asset file* lookups
  never fire for them. **FX must be color-driven (hex slots), not art-path
  driven.**
- ~142 literal `FF69B4` in 20+ XAML files never re-tint. Clean up
  opportunistically wherever the FX pass touches a file.

### Perf & safety rails (non-negotiable for every build agent)
- `PerformanceProfile` (3 tiers) already has `AllowGlow(tier)` /
  `MaxGlowBlurRadius(tier)` — declared but barely consulted. The overhaul adds
  `AllowAmbientMotion(tier)` / particle budgets and actually consults them.
- Copy the Chaos perf-governor shape (`ChaosModeService.cs:875-925`): hitch
  detector → instant degrade, ~5s recovery.
- **No new layered/transparent popups or windows** (Application Hang 1002
  family). No per-frame `SKBitmap` allocation (draw persistent `SKImage`s).
  Honor the global decode `SemaphoreSlim(2,2)` (`AnimatedWebp.cs:70`).
  Screens only via `App.GetAllScreensCached()`.
- `Storyboard.SetTargetName` **silently no-ops across the tab-UserControl
  namescope** (`MainWindow.UiUpdates.cs:1425-1428`) — use
  `element.BeginAnimation(...)` directly for anything inside a tab view.
  (`StartLockdownPulse` carries this latent bug today.)
- `ShowTab()` only stops 3 ambient loops today (`TabNavigation.cs:109-111`) —
  every new per-tab loop needs a registered stop hook.
- Ambient storyboards: `Timeline.DesiredFrameRate = 20-30`. All loops pause on
  `Window.Deactivated`, minimize, and (Skia canvases) stop their timer when
  idle. Detect RDP/software rendering (`RenderCapability.Tier`,
  `TerminalServerSession`) → static-art mode.
- Reduced-motion: three-way setting Full / Reduced / Off + honor the OS
  "Animation effects" flag. Reduced keeps crossfades, kills parallax/particles/
  loops.
- WPF silently clips overflow (no scrollbar, no warning) — size FX slots for
  peak scale; verify with UIA measurements, not eyeballs.

---

## 3. Architecture — Phase 0 (the platform everything else rides on)

**PR-0a: `FxTheme` + `ModFxPalette`**
- `Models/ModFxPalette` sibling of `ModTheme` (`fxPalette` manifest key), all
  nullable: `mistColor`, `particleColor`, `glowColor`, `flashTint`,
  `mistOpacity`.
- `ModService` accessors after `GetFilterColorHex()` (`ModService.cs:~810`)
  with the fallback chain `FxPalette → Theme.FilterColor → Theme.AccentColor`
  (+ `Get*Rgb()` twins). Extend the hex validation block (`:447-461`).
- `Services/FxTheme.cs` mirroring `RecapTheme`: `ApplyForActiveMod()` writes
  `FxMist*/FxParticle*/FxGlow*` Color+Brush keys into app resources; seed
  design-time defaults in `Resources/Theme/Colors.xaml`; call at init and in
  the `ModChanged` handler (`App.xaml.cs:1398-1410` block).
- Creator UI: 4 rows in `ModCreatorWindow` theme section + export path +
  `/creator-mod` template.

**PR-0b: `AmbientFxCanvas` control**
- One reusable control (`Controls/AmbientFxCanvas.cs`): `SKElement`,
  hit-test invisible, self-stopping 30fps `DispatcherTimer`, pause on window
  deactivate/minimize/tab-hide, per-frame try/catch, reads `FxTheme` colors and
  `PerformanceProfile` at start (never per-tick).
- Built-in layer vocabulary (compose per tab via simple config):
  `FogDrift` (2–4 blurred mod-tinted puffs, 20–40s), `AuroraWash` (animated
  SKShader gradient), `DustField` (n additive sprites, budgeted),
  `SheenSweep` (angled band, one-shot or 8s+ loop), `GlowBreath`
  (pre-baked glow image, opacity 0.6↔1.0), `BurstOneShot` (60–150 particles,
  1.2s, then full teardown) — burst code adapted from `ChaosFxLayer`'s
  struct-array sim, element-local coords.
- Registered with `ShowTab` lifecycle so switching tabs stops the old canvas.

**PR-0c: perf + motion plumbing**
- `PerformanceProfile`: add `AllowAmbientMotion(tier)`,
  `MaxAmbientParticles(tier)`, `FxTargetFps(tier)`; retrofit the *existing*
  unconditional loops (program banner fog, season shimmer, skill-tree brush,
  marquee) to consult them.
- Mini perf-governor inside `AmbientFxCanvas` (hitch → halve particle budget /
  drop to Reduced for that session).
- Reduced-motion setting (Full/Reduced/Off) in AppSettings + a real, reachable
  UI home for it and the two stranded perf checkboxes (currently inside the
  collapsed legacy dashboard host).
- Shared motion library in `Resources/Theme/Motion.xaml` + small C# helpers:
  `HoverLift` (scale 1.02, 150ms), `PressSquish` (0.97, 80ms), `SheenSweep`
  style, `GlowPulse`, staggered-entrance helper (fade + 10px rise, 40ms
  stagger, cap 6), odometer number tween, bar-fill + cap-bloom.

---

## 4. The pass, surface by surface

### Phase 1 — chrome (every tab benefits at once)
| Target | FX |
|---|---|
| Tab transitions | Replace bare 200ms fade (`AnimateTabIn`) with choreography: outgoing 100ms fade → incoming 12px directional slide + fade 200ms + entrance stagger on the new tab's top-level cards |
| Nav buttons (14) | Restore the gutted `ExpandableIcon` hover motion safely (BeginAnimation, IsLoaded/Template guards); animated active-tab treatment (glow underline that glides via the shared canvas or per-button glow breath) |
| START button | The app's one always-on hero CTA: slow glow breath + sheen sweep every ~8s (pre-baked glow art, opacity-only anim) |
| XP bar | Odometer tween on XP text, subtle moving highlight on the fill, keep the existing level-up flash as the "cap bloom" |
| Support banner / marquee | Sheen pass on banner text crossfade; marquee gets edge-fade masks |

### Phase 2 — Dashboard (the flagship)
| Region | FX |
|---|---|
| Behind mosaic grid | One `AmbientFxCanvas`: mod-tinted `FogDrift` + sparse `DustField` (the only looping layer on the tab) |
| FeatureCards (11) | Animate the already-wired-but-dead `ActiveGlow` (free win); hover = lift + rim-light; active cards get a slow breathing ring instead of the static 3.5px stroke |
| Center logo / intake flip | Ken Burns idle drift (scale 1.00→1.04, 40s) + occasional sheen; flip ceremony already exists |
| Premium rail (9 chips) | Hover lift + chip art nudge; live status dots get pulse; no loops |
| Browser card | **Airspace:** WebView2 is native HWND — FX on the frame/header only (animated border gradient, status-badge pulse) |
| Program Today card | Already has fog/sheen/sparkles — port onto `AmbientFxCanvas` + FxTheme so it re-tints per mod and respects tiers |

### Phase 3 — per-tab hero passes (one focal FX each + shared micro-interactions everywhere)
| Tab | Hero FX (the one loop) | Event/micro |
|---|---|---|
| Presets | Sheen on the selected session card | Card hover lift, entrance stagger, Load Preset press feedback |
| Quests | Keep season-banner shimmer (now tier-gated) | Progress fill + cap bloom on quest bars; reroll button squish; quest-complete burst |
| Programs | Already rich — align existing fog/sheen/pips to FxTheme + tiers | Enroll press, day-pip glow already good |
| Enhancements | Skill-tree animated brush stays (tier-gated); add faint `DustField` behind canvas | Node-unlock `BurstOneShot`; owned-node glow breath |
| Achievements | None (grid stays calm) | Entrance stagger; hover holo-foil tilt on unlocked tiles; unlock reveal = blur-dissolve + burst |
| Leaderboard | Podium #1 glow breath | Rank-change slide/flash; you-bar pulse on jump-to-me (exists — align) |
| Companion | Avatar hero breath (scale 1.00↔1.015, 5s) + rare blink/emote | Provider-connect success sheen |
| Deeper | Header wave glyph slow drift | Row hover reveal (exists) + lift |
| Assets | None | Tree hover, pack-card sheen on hover, Media Log button pulse when new entries |
| Profile | Keep rotating OG border | Card entrance, search focus glow |
| Available Subjects | NeonPurple `FogDrift` (very sparse) | Card entrance stagger, Connect press |
| Lab | Rabbit Hole launcher gets ember `DustField` (it's the one "portal" surface) | Bureau placeholder gets a folder-stamp thunk on first view |
| Takeover | **Replace placeholder ellipse with a real Skia orb** (layered glow + slow rotation + particle wisps — the tab's whole identity) | — |
| Premium gates (8 tabs) | **One shared animated gate treatment** (scrim fog drift + lock glow breath + CTA sheen) — single control, 8 surfaces upgraded |
| Blink/Haptics/She's Listening/Awareness | Status-driven pulses only (mic level, connection state) — forms stay calm | Shared micro-interactions |

### Phase 4 — event moments (one-shots, all `BurstOneShot`)
Level-up (XP bar cap-bloom + burst at the bar), achievement unlock toast,
quest complete, program day complete, enhancement purchase, prestige.
All colors via FxTheme; all skipped under Reduced/Off.

---

## 5. Execution — PR/agent breakdown

All build agents = **Opus**, per standing rule. Each PR ends green
(`dotnet build` + 559 tests) and gets a capture-harness before/after screenshot
set (`.claude/skills/x-drafts/scripts/capture.ps1` — scope windows by name
`"Conditioning Control Panel"`, never `main`, the compositor host shadows it).

| PR | Content | Agents |
|---|---|---|
| PR-0 | FxTheme + ModFxPalette + AmbientFxCanvas + PerformanceProfile additions + motion library + reduced-motion setting + retrofit existing loops | 2 (infra, then review) |
| PR-1 | Chrome: tab choreography, nav, START, XP bar, banner/marquee | 1–2 |
| PR-2 | Dashboard flagship pass | 1–2 |
| PR-3 | Tab batch A: Presets, Quests, Achievements, Leaderboard, Enhancements | 2 (parallel by tab, worktree isolation) |
| PR-4 | Tab batch B: Companion, Deeper, Assets, Profile, Subjects, Lab, Takeover orb, shared gate control | 2 (parallel) |
| PR-5 | Event one-shots + polish + full play-test + hang-hunt + perf soak (idle CPU before/after on each tab) | 1 + owner play-test |

Acceptance bar per PR: idle CPU on an open tab ≤ +1% vs baseline at Quality
tier, 0% at Performance tier (loops off); no new windows; mod-switch re-tints
every new FX live; Reduced/Off verified; screenshots reviewed via nano-banana
picker rules (owner eyes on art-adjacent changes).

---

## 6. Open decisions (owner)

1. **Scope cut:** everything above, or Phase 0–2 first and reassess?
2. **Audio-reactive accents** (companion avatar / dashboard pulse from output
   level) — flashy but adds an audio-tap; in or out?
3. **Sound design** (Opera GX-style UI ticks) — explicitly out of scope unless
   requested.
4. Reduced-motion default for existing users: Full or Reduced?
