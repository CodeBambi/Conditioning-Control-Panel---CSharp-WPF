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
| R0   | Settle    | 0-10%    | deniable in WHAT, never in WHO: 3-4 px nudges with an overshoot, one glyph typo held 4 s, a card breathing 3 %, a toggle that takes a beat to respond | 20-30 s |
| R1   | Drift     | 10-35%   | Start/Stop buttons swap, the X dodges the cursor, labels drift 6-8 px, a letter slips | 12-18 s |
| R2   | Melt      | 35-60%   | cards sag/melt on hover, toggles crumble to ash when clicked (and re-form), timer digits wobble (value stays TRUE), SCENES begin | 8-12 s |
| R3   | Collapse  | 60-85%   | letters fall out of titles to the rubble floor, a card falls off its column, window-edge pulses, warden knocks things over | 5-8 s |
| R4   | It knows  | 85-100%  | (Full Doki only) title bar retitles in-character, empty tube, themed fake "crash"/"deletion" dialog, the room stares back | 4-6 s |

Caps: Gentle never passes R2 and doubles the cadence; Eerie caps at R3; Full Doki reaches R4 and takes
0.8x. First haunt: no sooner than **20 s** after activation (`PossessionDeck.FirstWait`).
Concurrency: max live ghosts **2** (R0-R1), **3** (R2), **4** (R3+); a scene counts as its beat count.
Per-target cooldown **45 s**. Never possess the same target twice in a row. The rung change itself gets
an `EdgePulse` + `PossessionRungChanged` bark (1 per rung, no repeat).

> These numbers are wave 2 (2026-08-23). Wave 1 shipped 60-90 / 30-45 / 20-30 / 12-20 / 8-15 with a
> 45 s first wait, 90 s cooldown and MaxLive 1/1/2/3/3, and the owner's first live 10-minute Eerie run
> produced **two named effects in nine minutes**. Density is not a polish item here: below some rate the
> feature reads as an intermittent bug rather than as a room. `PossessionDeckTests` pins every row.

## The target registry (auto-tag)

`MainWindow.Possession.cs` walks the live visual tree and INFERS a role from the control type. Hand
tags (`poss:Possession.Role` / `poss:Possession.Name`) always win; `poss:Possession.Exclude="True"`
takes an element and its whole subtree out of the deck (it Inherits, unlike Role).

| Type | Role | Note |
|---|---|---|
| `ToggleButton` / `CheckBox` / `RadioButton` / switch-styled toggles | Toggle | checked BEFORE ButtonBase - they are ButtonBase too |
| `ButtonBase` (incl. the custom title-bar close / minimize / maximize) | Button | the X may dodge, it must stay clickable where it lands |
| `Slider` | Slider | |
| `ComboBox` | Combo | |
| `TextBox` / `PasswordBox` | TextBox | never `TxtLockdownExit` |
| `ScrollViewer` with `ScrollableHeight > 0` | Scroll | |
| `ProgressBar` | Progress | |
| `Image` >= 48 px | Image | |
| `TextBlock` with `FontSize >= 18` | Title | |
| other `TextBlock` with 3-40 chars | Label | capped to the 24 nearest the cursor |
| `Border`: CornerRadius > 0, non-null Background, >= 60x60, contains an interactive control, contains no other qualifying card | Card | innermost card wins |

**The leaf rule** is what keeps this cheap and correct: once an element resolves to an interactive
role we do not descend into it. WPF's visual tree walks straight through control TEMPLATES, so
without it a ScrollBar contributes two RepeatButtons and a Thumb, a ComboBox its toggle button, and
every button its caption TextBlock. Cards and ScrollViewers are the exceptions (we need their
contents). Filters: `IsVisible`, `IsEnabled`, `IsHitTestVisible`, >= 8x8 **in window pixels** (the UI
lives in a Viewbox over a 1585x901 design canvas, so `ActualWidth` is design units), and on-screen.

Never enrolled, subtree and all: `Possession.Exclude`, the GhostLayer / RubbleFloor, and the names
`TxtLockdownTimer`, `TxtLockdownExit`, `BtnEmergencyExit`, `EERoot`, `LockdownGate`,
`TxtPossessionRung`, `PossessionPips`, `TxtXP` (THE BANK's odometer rewrites it ~every 70 ms during
token flights, so a text effect there is a silent no-op and its undo would stamp a stale number).
An explicit hand-tag `poss:Possession.Role` beats the name blocklist - that is what lets
`TxtLockdownTimer` carry `Role="Timer"` for the wobble while staying auto-untouchable; `Exclude`
and `IsVisible` still win over everything. Other windows (avatar tube, playback, content) are separate
visual trees and are unreachable from this walk by construction; popups likewise.

Names: `Possession.Name` -> string `Content` -> `ToolTip` string -> `AutomationProperties.Name` ->
"that button". Keys: `x:Name` when there is one, else role + a stable hash of the visual path.

Caching: rebuilt lazily, at most once per 750 ms, invalidated by a throttled `LayoutUpdated` (which
covers tab switches, expanding cards and filling lists without this file knowing about any view) and
force-refreshed after 10 s. The walk time is logged at Debug and is typically well under 5 ms.

## PossessionPointer

`Services/Possession/PossessionPointer.cs` is a static fed from the window's `PreviewMouseMove` /
`PreviewMouseDown` (attached from `MainWindow.Possession.cs`): `Position`, smoothed `Velocity`,
`Hovered`, `LastClicked` / `LastClickAt`, plus `Pressed` / `HoverChanged` events. It is a plain static
rather than a service because it is written dozens of times a second and read inside the pick.
Everything on it is a HINT - a stale reading only costs one slightly-off victim.

**Proximity (A5):** half the picks (`PossessionDeck.ShouldUseProximity`) restrict the candidate victims
to those within `ProximityRadius` (160 px) of the cursor, and fall back to the full pool when fewer
than two qualify or nothing there may run. Targetless effects sit out proximity rounds - a title typo
has no coordinates to be near.

## Scenes (R2+)

`Services/Possession/Scenes/` - `IPossessionScene` is 3-5 BEATS across several controls over 4-8 s.
An effect is a word; a scene is a sentence, and from Melt upwards the room is supposed to feel
authored. Every beat speaks the same grammar (charge -> possess -> move -> undo) and the warden names
the scene ONCE at the top, so the whole choreography is attributed as one act.

| Scene | What happens |
|---|---|
| `scene_rail_sweep` | the ember charge walks the nav doors left to right; each door it touches sags and loses a letter; edge pulse at the end |
| `scene_where_you_are` | the card nearest the pointer breathes twice, then the breath does not come back: it sags, leans and settles wrong |
| `scene_the_count` | the version tag and level readout drift apart in opposite directions, then the heading above them mis-spells itself |

From R2 one pick in three is a scene. It is handed to the director through `PossessionSceneEffect`, an
`IPossessionEffect` adapter, so scenes inherit the existing cancellation, live-ghost ledger, cooldowns,
reassembly exit and crash-safe `UndoAll` rather than growing a second lifecycle. A scene counts as its
`Beats` against the concurrency cap, and claims victims through a director-supplied booking callback
(so they get their cooldown like any other victim when the scene ends).

`WhereYouAreScene` deliberately does NOT borrow `MeltEffect`: melt is hover-driven (it arms a
MouseEnter handler and only bites when the pointer arrives), so inside a scene it would mostly do
nothing, and reusing the catalog's shared instance would fight the director's `IsLive` bookkeeping.

## Event-driven ghosts (B15)

`PossessionEvents.cs`, subscribed on `AttachHost` and dropped on `DetachHost`. The cadence ladder is a
metronome that fires whether or not anyone is in the room; this closes the loop so the haunt answers
what the user DOES.

| The user... | ...the room answers | From |
|---|---|---|
| presses a card | that card breathes | R0 |
| changes a setting | the Label nearest the cursor mis-types itself | R1 |
| hovers a Start / Stop button | it dodges | R1 |
| clicks a nav door | a letter drops out of it | R3 |

Everything routes through `PossessionDirector.RequestReactive(effectId, target, minRung)`, which
applies a **6 s** floor between reactive ghosts, the concurrency cap, the rung gate, the intensity and
photosafe gates and the per-target cooldown. Callers never check anything. The Possession settings
themselves are exempt from the SettingChanged reaction: answering "the user just turned the haunt
down" by haunting them is the one joke that reads as the app ignoring consent.

## Timer restart (Emergency Exit sent them back)

`LockdownService.TimerRestarted` (see `EMERGENCY_EXIT.md`, verdict `sendback`) rewinds the clock to its
FULL duration. The director drops `CurrentRung` back to whatever the new elapsed fraction says (Settle),
clears `_barkedRungs` so every rung can announce itself again, undoes every live ghost over about a
second (the reassembly path, but quick - this is a reset, not a curtain call), pulses the edge at 0.6,
and sets `_nextDue` to a full `FirstDelay` so there is a silence to notice before it all starts again.

The bark wrapper `BarkService.NotifyPossessionTimerRestarted(string reason, int restart)` exists and
the director calls it directly (the reflection probe this section once described is gone). The pack
rules fire it only from the second restart on (`restart_gte: 2`); the first sendback is voiced by
`ee_sendback` instead.

## Tripwires (escape attempts)

`LockdownService.NotifyEscapeAttempt(kind)` raises `EscapeAttempted(EscapeAttempt)` with per-kind
repeat counts. Kinds: `close`, `minimize` (allowed - it still trips), `syskey` (throttled 1 / 2 s),
`stop`, `wrong_phrase`, `settings`, `emergency_exit` (the big button), `starve` (the user switched the
LAST running feature off - raised by the Dose keeper, see below). Reaction scales with (rung, repeat):

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
| `LockdownDoseKeeperEnabled` (bool) | true | Safety: a lockdown refuses to run empty (the Dose, below) |

All with `[JsonProperty]`-style persistence like their neighbours (auto-save via OnPropertyChanged).

## Barks

New triggers (see `PossessionBarkTriggers`): `PossessionRungChanged` (ctx `rung`),
`PossessionEffect` (ctx `effect`, `target`), `PossessionTripwire` (ctx `kind`, `repeat`, `total`),
`PossessionWarden` (ctx `verb`), `PossessionRules` (first run), `PossessionTimerRestarted` (ctx `reason`,
`restart`), `PossessionRemember`, `LockdownConscript` (ctx `features`, `round`, `engine` - the Dose). Rules go in all three packs
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
- Never start an effect while `IPossessionHost.IsUsable` is false; live effects may finish. A playing
  video is NOT a reason to stop (wave 2, A3): a lockdown run is mostly video, so the old pause meant
  the haunt spent most of its life asleep. What still stops it: minimized / not loaded, an open Lock
  Card, and ANY content window that has taken the screen - every `ChaosWebViewHost` takeover host is
  checked (DTRH, Loom, Arcademy, Bureau, Goon, JustDrop, FYP, Intake, and the Emergency Exit game
  window itself).
- Everything dispatcher-safe (`Application.Current?.Dispatcher`), wrapped in try/catch, logs via
  `App.Logger` at Debug for routine picks and Warning for failures. A failing effect undoes itself.
- Photosafe + `SystemParameters.ClientAreaAnimation == false` = no flicker effects, static charge.
- Ember `#FF8A5C` only for Possession. Crimson `#DC143C` stays the Lockdown theme colour.
- No em-dashes in user-facing strings (house rule). Loc keys for UI strings in all 9 language files.

## Verifying without entering Lockdown

`ConditioningControlPanel.exe --possession-preview <outDir>` (Services/Dev/PossessionPreview.cs) runs
every catalog effect against the real main window - charge / live / undone shots per effect via
RenderTargetBitmap (works with the display asleep), a `_report.txt` with undo-exactness deltas, then
exits. It NEVER activates LockdownService (no hook, no safeties), so it is safe unattended. Use it
after any effect or attribution change; judge the shots against "clarity in front".

Traps learned building it (2026-08-22):
- GhostLayer/RubbleFloor are RootGrid SIBLINGS of the `<Viewbox Stretch="Fill">` that scales
  DesignCanvas (1585x901). `TransformToAncestor(GhostLayer)` THROWS for every victim (swallowed ->
  empty bounds -> the whole attribution grammar silently invisible). Always `TransformToVisual`,
  and take sizes from the transformed bounds, never ActualWidth (design units; X/Y scale differ).
- A broken effect fails through clean `return`s, so the log says everything worked. Trust the shots.
- `wobble` has no target outside a live lockdown (the Timer lives in the active panel); `swap` needs
  two visible Button targets in one parent (the main window only tags BtnStart - tag more buttons
  or accept that swap mostly fires in rooms with button pairs).

## Wave 2 - density (owner play-test verdict 2026-08-23: "not dense, not impressive")

Diagnosis of the first live 10-minute Eerie run: only 11 possessable targets in the whole window
(7 tab doors, title, version tag, level label, Home Start) because every other tag sat on the
Lockdown card, which is HIDDEN while a lockdown runs; the haunt paused whenever a video played;
2 big effects named in 9 minutes; every warden bark after the first was muted by the global bark
gap (fixed in 3a0eaaddc). Owner-approved wave (picker 2026-08-23), per-owner file ownership:

Foundation rows (A1, A2, A3, A5, A6, A7, B15, timer restart) landed 2026-08-23; the ladder table,
target registry, PossessionPointer, Scenes, event-driven ghosts and timer-restart sections above are
the shipped behaviour, not the plan.

| Item | Owner | Files |
|---|---|---|
| A1 auto-tag the visible window by control type (Button/Toggle/CheckBox/Slider/Combo/card Border/door/title-bar X+min/ScrollViewer/ProgressBar/TextBox), names from Content/ToolTip/AutomationId; the hand tags stay as overrides | Opus FOUNDATION | `MainWindow/MainWindow.Possession.cs`, `Services/Possession/Possession.cs` |
| A2 cadence x2.5 (R0 20-30 s, R1 12-18, R2 8-12, R3 5-8, R4 4-6), first tic 20 s, target cooldown 45 s, MaxLive 2/2/3/4/4 | FOUNDATION | `PossessionDeck.cs` |
| A3 no pause for video/whisper (pause only for fullscreen takeovers + Lock Card) | FOUNDATION | `PossessionDirector.cs` |
| A5 proximity targeting (half the picks = control nearest cursor / hovered / last clicked) | FOUNDATION | `PossessionDeck.cs`, `PossessionDirector.cs`, `MainWindow.Possession.cs` |
| A6 scenes (R2+: 4-8 s choreographies of 3-5 beats) | FOUNDATION | `Services/Possession/Scenes/*.cs`, `PossessionDirector.cs` |
| A7 micro-tic visible floor (nudge 3-4 px + overshoot, typo 4 s, breathe 3 %, drift 6-8 px) | FOUNDATION | `Effects/Nudge|Typo|Breathe|Drift` |
| B15 event-driven ghosts (FeatureOpened -> card breathes, SettingChanged -> its label retypes, hover Stop -> dodge, door click -> letter drop) | FOUNDATION | `PossessionDirector.cs` (+ an `PossessionEvents.cs` adapter) |
| Timer restart handling (`LockdownService.TimerRestarted`): rung -> Settle, `_barkedRungs` cleared, UndoAll over 1 s, EdgePulse, bark `PossessionBarkTriggers.TimerRestarted` | FOUNDATION | `PossessionDirector.cs` |
| B1 ghost cursor, B2 predictive dodge incl. title-bar X/min, B3 real Start/Stop swap + "Stay" relabel, B4 slider ghost-thumb creep + toggle lies, B5 label rewrite + glyph rot (per-mod line packs) | Opus EFFECTS-A | `Effects/GhostCursorEffect.cs`, `Effects/Dodge*` (edit), `Effects/Swap*` (edit), `Effects/SliderCreepEffect.cs`, `Effects/ToggleLieEffect.cs`, `Effects/RewriteEffect.cs`, `Effects/GlyphRotEffect.cs`, `Effects/PossessionEffectCatalog.WaveA.cs` |
| B7 XP drain / level lie, B8 tube steals an ACTIVE card (a usable card on the current tab - the option is really gone until reassembly), B9 room tilt/sag/deepen + per-escape 1 px shrink/2 px nudge (self-contained service on the lockdown events), B10 tab misroute + door reorder, B11 crimson toasts, B14 scroll hijack, C1 fake "deleting your sessions" dialog (Full Doki) | Opus EFFECTS-B | `Effects/XpDrainEffect.cs`, `Effects/StealCardEffect.cs` (+ `Warden.cs` steal verb), `Effects/RoomWarpEffect.cs`, `Effects/MisrouteEffect.cs`, `Effects/ReorderDoorsEffect.cs`, `Effects/ToastEffect.cs`, `Effects/ScrollHijackEffect.cs`, `Effects/DeleteDialogEffect.cs`, `Effects/PossessionEffectCatalog.WaveB.cs`, `MainWindow/MainWindow.NavRail.cs` (misroute hook only) |
| B12 chat knows (lockdown flag in the companion prompt), B13 audio tics (ember tick SFX on big effects + 300 ms pitch-dip at rung change / tripwire repeat 3), C2 it remembers (flag + next-launch tic + line), C3 portrait glitch frames (R4, photosafe-gated), C4 Full Doki retitle at R3 + note in the empty tube | Opus COMPANION | `Services/Possession/PossessionAudio.cs`, `Effects/RetitleEffect.cs`, `Warden.cs` (note), `AvatarTubeWindow` glitch hook, companion prompt builder, `Models/AppSettings.cs` (`LockdownPossessionRemembers*`), `Effects/PossessionEffectCatalog.WaveC.cs` |

REJECTED by owner: B6 name drop (sensitive userbase - never use the Windows username), D2/D3
(content windows stay clean, full stop). Build mutex + loc-additions rule: see
`Services/EmergencyExit/EMERGENCY_EXIT.md` "Ownership".

Rules that do not move: ember only for Possession; Undo restores EXACTLY; never the timer VALUE, the
secret exit box, the Emergency Exit button's hit-testing, the premium gate, content windows.

### Companion wave (B12, B13, C2, C3, C4) - shipped

The four things the COMPANION owns in wave 2. Everything here obeys the rules above; what follows is
only what a reader cannot infer from them.

**B12 chat knows** - `Services/Companion/Brain/PromptAssembler.cs`. While `App.Lockdown.IsActive`, the
chat and reaction prompts carry one line: minutes left, rung, intensity, how many escape attempts, and
the warden brief (tease them about wanting out, stay in the mod voice, never reveal or hint at the
secret exit phrase, never promise to end it, keep it short). It goes in the DYNAMIC TAIL, right after
the anti-fixation exclusion set - never in the stable prefix, which is cached until a mod switch and
would keep briefing her as the warden an hour after the lockdown ended. `AiPurpose.Chat` and
`Reaction` only: `Memory` writes durable facts and `Summary` writes prose about the conversation, and
a "you are the warden" instruction in either would be recorded as something the USER believes.
Injectable as a ctor `Func<string?>` for tests. Escape count comes from `PossessionRemember`, which
counts `EscapeAttempted.Total` (LockdownService keeps its own total private).

**B13 audio tics** - `Services/Possession/PossessionAudio.cs`, gated by the new
`AppSettings.LockdownAudioTics` (bool, default true). Two synthesized cues, rendered once into
`%LOCALAPPDATA%/ConditioningControlPanel/possession/` and played through `AudioService.PlayOneShot`
like any bubble pop, so device selection, the one-shot cap, the endpoint circuit breaker and disposal
are unchanged. A ~50 ms ember tick at -18 dBFS on every BIG effect (`EffectStarted`, throttled 1 per
1.5 s); a 300 ms dip at a rung change and at tripwire repeat >= 3 (throttled 1 per 5 s). The dip is
`AudioService.Duck(60)` for 300 ms under an 80 Hz stinger, NOT a pitch wobble: there is no shared
master graph to wobble (one `WaveOutEvent` per clip, and `LayeredAudioService`'s mixer has no
pitch/varispeed stage), so a -2 semitone dip would mean re-plumbing every audio path for a 300 ms
effect. Deliberately NOT gated on `LockdownPhotosafe` - photosafe is a visual accommodation. The
service subscribes to the director only while a lockdown runs; the setting is re-read at PLAY time so
flipping it mid-lockdown takes effect at once.

**C2 it remembers** - `Services/Possession/PossessionRemember.cs` +
`AppSettings.LockdownPossessionRememberPending` (bool, default false). Set when a lockdown ends and the
intensity WAS Full Doki (read at deactivate time, not next launch). About 20 s after the main window is
up on the next launch, the Lockdown door takes one ember charge (its own `EmberAttribution` over
MainWindow-as-host; the director never exposes its own) and the companion says one line, bark trigger
`PossessionRemember`. Nothing moves, no effect starts, and it is skipped if a lockdown is running or
there is no tube. The flag is cleared the instant the service reads it, before anything is scheduled,
so a crash can never leave it charging every launch.

**C3 glitchportrait** - `Effects/GlitchPortraitEffect.cs`, registered in
`Effects/PossessionEffectCatalog.WaveC.cs` at weight 2. R4, Full Doki, `UsesFlicker` true (so photosafe
skips it; there is no still variant, because a static torn portrait is a rendering bug rather than a
glitch), `IsBig` false, no charge ripple (the victim is in another window, so the ember tint ON the torn
bands is the attribution). `AvatarTubeWindow.GlitchPortrait(ms)` draws three clipped, offset copies of
the current portrait into `PossessionGlitchLayer`, a sibling of the avatar images inside
`AvatarBounceHost` - same layout slot, so a copy lines up without measuring anything. The real emote /
pose / crossfade pipeline is never written to.

**C4 retitle at R3 + the note** - `RetitleEffect.MinRung` is now `Collapse`, not `ItKnows` (still Full
Doki via MinIntensity). At R4 the loudest thing the mode owns did not exist until the last 15 % of the
timer, which on a one-hour lockdown meant minute 51. `Warden.LeaveAsync` now leaves a small crimson
note card in the empty tube (`AvatarTubeWindow.ShowPossessionNote`, three variants, loc keys
`possession_tube_note_1..3`); `ReturnAsync` clears it unconditionally and first, so reassembly during
app shutdown cannot leave one behind. Crimson, not ember: ember is the verb, and a note is the opposite
of something happening.

Verified: full `--possession-preview` pass, 24 effects, undo-exactness failures 0, retitle confirmed
firing at Collapse. The synthesized buffers are checked standalone (length, bounded, finite, peak at
the intended dBFS, non-silent, zero at both boundaries, decaying, deterministic, valid RIFF header).
Note for whoever touches the rig next: `PossessionPreview.Order` is a hand-written id list, so
`glitchportrait` will not appear in a report until it is added there - and it needs a tube, which the
rig does not photograph.

## The Dose - a lockdown refuses to run empty (`Services/Haptics/LockdownDoseKeeper.cs`)

Owner play-test 2026-08-23: "I can start lockdown without the engine on, making it moot, or turn off all
the features with the engine on." The keeper is the answer, in the warden's voice: *you aren't picking
anything, so I pick.*

- **Activation**: engine off -> started for the user ~2 s after Activate (the dialog and the card's
  active-panel swap settle first). Nothing switched on -> a dose is conscripted first.
- **While locked** (every CountdownTick): engine stopped (session ended, ramp finished, scheduler
  window closed, remote Stop) -> restarted after a 4 s grace. Dose empty -> the moment it goes empty
  is a tripwire (`EscapeKinds.Starve`, so Possession answers it like the X), then after a grace of
  6 / 4 / 2 s (shrinks per round) the warden switches features back ON: 2 the first round, +1 per
  round, 4 max. Order: what the user had on at activation (shuffled), then the starter pool (flash,
  subliminal, spiral, pink filter, bouncing text, bubbles), then from round 2 the escalation pool
  (mandatory video - only when the videos folder has files). Tier 2 features (mind wipe, lock card,
  bubble count, brain drain) COUNT as a dose but are never picked; so do audio-only / whispers /
  corner gif / takeover.
- **A running session owns the dose** (`MainWindow.IsSessionFeatureLockActive`) - the keeper stands
  down until it ends.
- **Deactivation gives everything back**: flipped toggles return to their pre-lockdown value, an
  engine the keeper started is stopped. `%LOCALAPPDATA%/ConditioningControlPanel/lockdown-dose.json`
  carries the flipped keys across a crash; `LockdownDoseKeeper.RecoverIfNeeded()` (App startup, right
  after `LockdownService.RecoverIfNeeded`) switches them back off.
- Every flip goes through `MainWindow.SetWallFeature(key, on)` - the same seam the wall cards use, so
  the service actually starts/stops and the rings repaint. The keeper never touches values,
  frequencies, volumes or any safety control. It pulses the ember edges (`PossessionDirector.PulseEdges`)
  when Possession is haunting; otherwise the bark alone carries the attribution.
- Barks (3 packs): `ld_dose_engine` / `ld_dose_engine_pick` (engine started [+ picks]),
  `ld_dose_first` (round 1), `ld_dose_again` (round 2+, `{round}`), `poss_trip_starve`. `{features}` is
  the joined display list ("Flash and Subliminals").
- Off switch: `LockdownDoseKeeperEnabled` (Safeties row "Nothing running? Lockdown picks for you"),
  listed in both activation dialogs like the other safeties. Pure half tested in
  `Tests/.../LockdownDoseKeeperTests.cs`.
