# Possession - the haunted-UI layer of Lockdown

> Read this before touching anything under `Services/Possession/`, the Lockdown card, or the
> tripwire hooks. Contracts live in `PossessionContracts.cs`; this file is the WHY and the rules.

## What it is

Lockdown used to be a timed cage: timer + forced Strict Lock + panic key off + system keys blocked.
Those STAY, but as default-on **Safeties** toggles inside the Lockdown card. The spotlight moves to
**Possession**: while a lockdown runs, the app's own UI misbehaves - There Is No Game / Doki Doki
style - escalating with the timer, reacting to escape attempts, and using the companion as a warden.
Owner pitch + locked decisions: artifact "There Is No Exit" (2026-08-22).

Owner-locked decisions (2026-08-22):
1. Tab stays **Lockdown**; the layer is **Possession** (`PossessionDirector`, `IPossessionEffect`,
   `poss:Possession.Role`). Never rename the tab or ShowTab key `lockdown`.
2. Default intensity **Eerie** (rungs 0-3). **Full Doki** opt-in (adds rung 4 + themed Doki dialogs).
   **Gentle** caps at rung 2.
3. Fake crash / fake deletion ALLOWED but **obviously themed**: crimson chrome, companion portrait,
   in-character title, NEVER a real path / log name / Windows chrome. Full Doki only.
4. Everything inside Lockdown's existing premium gate (`TierGate.DemandPremium`). No new tiering.
5. Scope: every NON-CONTENT window (main window first; dashboard wall, Settings palette, Lock Card
   later). Playback / content windows (video, flash, overlays, browser, DTRH, Arcademy) stay clean.
6. Attribution: ember charge pre-roll + warden names big effects + cursor ember ring. No ledger.
7. First run: the warden states the rules (warning-dialog paragraph + one bark + one-time intro card).

## THE RULE: clarity in front

Surprises are **unexpected but never mistaken for a bug**. Test: "was that Lockdown?" must be
answerable in one second, from across the room. Every effect speaks the grammar:

1. **The charge** - ~400 ms ember ripple (`#FF8A5C`, the Lockdown tab's own hue) over the target
   BEFORE anything moves. No effect may start without it (`IPossessionAttribution.ChargeAsync`).
2. **The possessed outline** - thin ember outline + faint tint while a ghost misbehaves; gone the
   instant `Undo` runs (`IPossessionAttribution.Possess` handle).
3. **The cursor tell** - ember ring around the cursor while ANY ghost is live (refcounted).
4. **The warden names the big ones** - moves / falls / dissolves / retitles / dialogs get a bark
   that names the thing ("oops, the Stop button moved"). R0/R1 micro-tics stay silent but still
   carry charge + tint. Effects call `ctx.Name(effectId, targetName)`; the director routes it.
5. **Ember means Possession, only** - crimson is the theme (the room is red); ember is the verb
   (the room is DOING something). Themed Doki dialogs follow the same rule so support can tell
   them from a real crash at a glance.

Anything that fails the one-second test in play-test gets cut or gets a bark.

## The ladder (elapsed fraction of the timer)

| Rung | Name      | Window   | Feel                                            | Cadence (Eerie) |
|------|-----------|----------|-------------------------------------------------|-----------------|
| R0   | Settle    | 0-10%    | deniable in WHAT, never in WHO: 1-px nudges, one glyph typo for 2 s, a toggle that takes a beat to respond, cursor ring flickers once | ~60-90 s |
| R1   | Drift     | 10-35%   | Start/Stop buttons swap, the X dodges the cursor, labels drift a few px, a letter slips | ~30-45 s |
| R2   | Melt      | 35-60%   | cards sag/melt on hover, toggles crumble to ash when clicked (and re-form), timer digits wobble (value stays TRUE) | ~20-30 s |
| R3   | Collapse  | 60-85%   | letters fall out of titles to the rubble floor, a card falls off its column, window-edge pulses, warden knocks things over | ~12-20 s |
| R4   | It knows  | 85-100%  | (Full Doki only) title bar retitles in-character, empty tube, themed fake "crash"/"deletion" dialog, the room stares back | ~8-15 s |

Caps: Gentle never passes R2 and halves cadence; Eerie caps at R3; Full Doki reaches R4.
Concurrency: max live ghosts 1 (R0-R1), 2 (R2), 3 (R3+). Per-target cooldown 90 s. Never possess
the same target twice in a row. The rung change itself gets an `EdgePulse` + `PossessionRungChanged`
bark (1 per rung, no repeat).

## Tripwires (escape attempts)

`LockdownService.NotifyEscapeAttempt(kind)` raises `EscapeAttempted(EscapeAttempt)` with per-kind
repeat counts. Kinds: `close`, `minimize` (allowed - it still trips), `syskey` (throttled 1 / 2 s),
`stop`, `wrong_phrase`, `settings`. Reaction scales with (rung, repeat):

- repeat 1: EdgePulse(0.5) + tripwire bark naming the attempt.
- repeat 2: EdgePulse(0.8) + 120 ms ember blink (SKIPPED when photosafe -> slow pulse instead) +
  title flicker + `ScreenShake.Shake(0.4, 250)` + bark.
- repeat 3+: above + warden STARE (tube glides to the window, one line) ; Full Doki: a themed Doki
  dialog instead of the MessageBox.
Blink-length scares only. Never block Ctrl+Alt+Del; never suppress bare Esc (#680).

## Warden (the companion)

Verbs: **knock** (glide beside a card, a beat, the card falls - R3), **stare** (glide to the
window centre, one line - tripwire repeat 3+ / R4), **leave** (R4: tube goes empty / off-screen),
**return** (reassembly). Uses the bubble-egg movement API (`GlideToBubbleAsync` family; add a
`GlideToPointAsync` if needed). Gates: `LockdownWardenEnabled`, `App.AvatarWindow.CanPerformBubbleEgg`
(not busy), 90 s cooldown between appearances, never while `App.Video.IsPlaying`.

## Exit = reassembly

On `LockdownDeactivated` the director undoes EVERY live effect in reverse order over ~3 s
(`UndoAsync(duration)`), rubble flies back, outlines drop, cursor ring off, warden returns, then
the existing `RestoreLockdownTheme` / `lockdown_off` bark run. `UndoAll()` must also be safe on
crash-recovery / dispose (no awaits needed - synchronous reset path).

## Settings (Models/AppSettings.cs, Lockdown section)

| Property | Default | Meaning |
|---|---|---|
| `LockdownForceStrictLock` (bool) | true | Safety: force Strict Lock ON during lockdown |
| `LockdownDisablePanicKey` (bool) | true | Safety: panic key OFF during lockdown |
| `LockdownBlockSystemKeys` (bool) | true | Safety: hook suppresses Win / Alt+Tab / Alt+F4 / Ctrl+Esc |
| `LockdownPossessionEnabled` (bool) | true | master switch for the haunt |
| `LockdownPossessionIntensity` (int) | 1 (Eerie) | 0 Gentle / 1 Eerie / 2 Full Doki |
| `LockdownTripwiresEnabled` (bool) | true | escape attempts get a reaction |
| `LockdownWardenEnabled` (bool) | true | companion roams / knocks / stares |
| `LockdownPhotosafe` (bool) | false | no blinks / strobes / hard shakes; charge = static tint |
| `LockdownPossessionIntroSeen` (bool) | false | first-run rules card shown |

All with `[JsonProperty]`-style persistence like their neighbours (auto-save via OnPropertyChanged).

## Barks

New triggers (see `PossessionBarkTriggers`): `PossessionRungChanged` (ctx `rung`),
`PossessionEffect` (ctx `effect`, `target`), `PossessionTripwire` (ctx `kind`, `repeat`, `total`),
`PossessionWarden` (ctx `verb`), `PossessionRules` (first run). Rules go in all three packs
(`Resources/sounds/companion_audio/mods/<mod>/bark_rules.json`) as TEXT-ONLY variants (audio null is
fine - BarkService.ResolveBarkAudio returns null and the bubble still shows). Voice per mod: read the
pack's existing `lockdown_on/off/tick` lines and match them. Lines must NAME the thing when the
effect is big: use the `target` context value.

## Architecture / file ownership

```
Services/Possession/
  PossessionContracts.cs      contracts (stable - change only with a reason written here)
  POSSESSION.md               this file
  PossessionDirector.cs       state machine: rung from elapsed fraction, weighted deck, cooldowns,
                              concurrency caps, tick loop, tripwire reactions, reassembly exit
  PossessionDeck.cs           pure logic (testable, no WPF): rung math, weighting, cooldown picks
  EmberAttribution.cs         IPossessionAttribution over the host's GhostLayer (charge overlay,
                              possessed outline, cursor ring, edge pulse)
  Ghost.cs                    snapshot-and-puppet helper: RenderTargetBitmap of a control -> Image in
                              GhostLayer; hide/restore the real control; rubble placement
  TransformLease.cs           wrap a control's RenderTransform in a TransformGroup (prior + ours) and
                              restore EXACTLY on release - mirror ScreenShakeService.TargetEntry
  Warden.cs                   companion choreography (knock / stare / leave / return)
  Effects/*.cs                one IPossessionEffect per file
MainWindow/MainWindow.Possession.cs   IPossessionHost impl: ghost layer + rubble floor added to RootGrid,
                              target registry from poss:Possession.Role, IsUsable
Views/Tabs/LockdownTabView.xaml(.cs)  Safeties + Possession controls on the card; rung readout while active
Models/AppSettings.cs         settings above
Services/Haptics/LockdownService.cs   NotifyEscapeAttempt / EscapeAttempted; honours the two safety toggles
Services/Companion/BarkService.cs     NotifyPossession* wrappers -> Raise(trigger)
```

Hard rules for every file:
- `Undo` restores the control EXACTLY. Transform leases restore the prior transform object, not a
  new identity. Text effects restore the original string. Opacity / IsHitTestVisible restored.
- Never possess: the lockdown timer's VALUE (digits may wobble, the number stays true), the secret
  exit box, the premium gate, the warning dialog, anything inside playback/content windows, the
  avatar tube's own chrome, the title bar's real close/minimize buttons' HIT-TESTING (the X may
  dodge, it must stay clickable where it lands).
- Never start an effect while `IPossessionHost.IsUsable` is false; live effects may finish.
- Everything dispatcher-safe (`Application.Current?.Dispatcher`), wrapped in try/catch, logs via
  `App.Logger` at Debug for routine picks and Warning for failures. A failing effect undoes itself.
- Photosafe + `SystemParameters.ClientAreaAnimation == false` = no flicker effects, static charge.
- Ember `#FF8A5C` only for Possession. Crimson `#DC143C` stays the Lockdown theme colour.
- No em-dashes in user-facing strings (house rule). Loc keys for UI strings in all 9 language files.
