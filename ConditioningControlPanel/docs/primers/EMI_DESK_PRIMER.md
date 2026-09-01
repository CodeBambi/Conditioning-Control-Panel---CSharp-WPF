# EMI Desk — Feature Primer

> **Purpose.** One-load orientation for **EMI Desk**: the summoned desktop widget (`Services/EmiDesk/`,
> `Windows/EmiDesk/`, `Controls/EmiDock.*`). Read §0 first — the vocabulary collides badly with the
> **avatar tube** and with **the Arcademy's web EMI**, and mixing them up is the #1 way to break this
> feature. §1 is the anatomy, §2 the seams, §3 the line engine (the load-bearing section), §4 the
> ring, §5 the glass, §6 the moments, §7 settings + the mute arbiter, §8 the file map, §9
> where-to-change-X, §10 **the traps** (all of them hit during the first live QA run), §11 what is
> deliberately not done.
>
> **Freshness.** Written from the first live run of the feature, **2026-08-29**, branch
> `feat/emi-desk`. Every `file:line` was read-verified when written; line numbers drift, so confirm
> with a quick read before quoting. §0–§10 track the code and rarely rot; §11 is a dated snapshot of
> owner decisions — check `docs/emi-desk/BRIEF.md` before assuming any of it still holds.
>
> **The build brief is the authority on the *product*.** `docs/emi-desk/BRIEF.md` holds the owner's
> locked decisions; `docs/emi-desk/SEAMS.md` holds the chunk contract; `docs/emi-desk/LINES-SCHEMA.md`
> and `docs/emi-desk/MOMENTS.md` hold the line grammar. This primer explains how the code *behaves*
> and where the mines are. When they disagree, BRIEF wins on intent and this file wins on fact.

---

## 0. What this is — and the three EMIs (READ THIS)

**EMI Desk** is a pink mascot who is **summoned onto the desktop**, sits in a frameless always-on-top
window, shows a face on the little screen she carries, occasionally says one preset line, and fans a
six-card ring of app features when you click her. She is not a chat bot, not an assistant, and she
makes **no network calls and no AI calls, ever**. Every word she says comes out of a shipped JSON
file.

There are **three different things called EMI** in this repo, and only one of them is this feature:

| Which EMI | Where | What it is |
|---|---|---|
| **EMI Desk** (this primer) | `Services/EmiDesk/`, `Windows/EmiDesk/` | The **native WPF desktop widget**. Summoned, ~6.2k lines of service + ~4.2k lines of window. No WebView2 anywhere. |
| **Campus EMI** | `Resources/web/arcademy/emi/*.js` (`face.js`, `chains.js`, `widget.js`) | The **web mascot inside the Arcademy**. EMI Desk is a *port* of her: `EmiFace` and `EmiChains` are verbatim ports of `face.js` / `chains.js` and are locked by `EMI-DESIGN-LOCK.md`. |
| **Discord EMI** | private repo `CC-Labs-llc/CCP-Server`, `bot/` | The **Discord bot persona**. Shares the voice, shares nothing else. |

And the one she is NOT:

- **The avatar tube** (`AvatarTubeWindow`) is the **AI companion**: a different character, a different
  window, a different voice, network-backed, with its own bark system. EMI Desk's single most
  important product rule is **the two of them never talk at once** — see §7's mute arbiter.

### Her shape in one paragraph

`App.EmiDesk` (an `EmiDeskService`) owns everything. It creates **one** `EmiDeskWindow` lazily on the
first summon and keeps it forever, hidden between summons. That window is a transparent, layered,
non-activating tool window carrying her body PNG, a face canvas, a glass overlay, a speech bubble and
her FX; a **second** sibling window (`EmiRingWindow`) covers the work area and holds the six-card fan.
A **third** (`EmiMutePromptWindow`) asks once whether to mute the avatar. In the main window's nav
rail sits `EmiDock`, a 40×40 chip with a live mini-face that summons her.

---

## 1. Anatomy

### 1.1 The windows

All four are `WindowStyle=None` + `AllowsTransparency=True` + `ShowActivated=False` +
`ShowInTaskbar=False` + `Topmost=True`, and stamp `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` in
`OnSourceInitialized`. That combination is what makes her a desktop ornament rather than a window:
she never steals focus, never appears in Alt-Tab, and never takes the foreground away from what you
were doing.

**`EmiDeskWindow`** (`Windows/EmiDesk/EmiDeskWindow.xaml` + four partials, ~2 800 lines total) is
deliberately **bigger than she is**: `OverlayPad` DIPs of fully transparent air on every side, so the
summon smoke, the sparkle scatter and the speech bubble have room to fly outside her silhouette
without a second window. The padding has `Background="{x:Null}"`, which WPF does not hit-test, so a
click on the air falls straight through to whatever is behind her.

Layer order, bottom to top:

```
Root                    (Background = null: click-through air)
  BodyRoot              her silhouette — the ONLY hit-testable region
    BodyImage           the pose PNG (Resources/web/arcademy/art/emi/body*.png)
    FaceLayer           (IsHitTestVisible=False)
      FaceView          the EmiFace renderer
      GlassCanvas       the glass channel painter's host
    BtnClose            the hover x (18 DIP glyph in a 34 DIP hit area, opacity 0 until hover)
    BtnGear             the hover gear, top left, same split (opens her options; §15)
    ResizeGrip          the corner handle (22+ DIP hit area, glyph always faintly lit)
  OverlayCanvas         summon / dismiss FX          (IsHitTestVisible=False)
  BubbleCanvas          the speech bubble + ask chips (IsHitTestVisible=False)
```

**The hit-testing rule is load-bearing:** `GlassCanvas`, `OverlayCanvas` and `BubbleCanvas` are all
`IsHitTestVisible="False"`. A glass tap is resolved **geometrically** in `OnBodyMouseUp` against
`GlassRect`, not by a hit-testable overlay — a hit-testable overlay would eat the drag and she would
stop being draggable. If you ever add a layer over her body, it must be non-hit-testable and its
clicks must be resolved the same way.

### 1.2 The pieces

| File | Lines | What it owns |
|---|---:|---|
| `Services/EmiDesk/EmiDeskService.cs` | 1281 | The facade: `App.EmiDesk`. Summon/dismiss/toggle, the hotkey, the moment bus (`Fire`), `NoteOpen`, the mute arbitration, teardown. |
| `Services/EmiDesk/EmiLineEngine.cs` | 1287 | **The voice.** Loads `desk-lines.json`, runs the draw algorithm, owns holds, cooldowns, limits, the shuffle bags and the ask gates. |
| `Services/EmiDesk/EmiChains.cs` | 567 | Verbatim port of `chains.js`: the face animation sequences (`glee`, `smug`, `cool`, …), the frames, the one-shot fx and body moves. **LOCKED.** |
| `Services/EmiDesk/EmiFace.cs` | 517 | Verbatim port of `face.js`: the kaomoji renderer, a `FrameworkElement` that draws glyph **outlines** so the face stays crisp at any size. **LOCKED.** |
| `Services/EmiDesk/EmiChannels.cs` | 540 | The glass channels (`spiral`, `video`, `burst`, `rain`), their idle timings and their painters. |
| `Services/EmiDesk/EmiOffers.cs` | 433 | What an offer or a glass tap actually *fires*, plus the feasibility probes the engine calls at draw time. |
| `Services/EmiDesk/EmiTargets.cs` | 440 | The ring **catalogue**: 27 doors, each three lambdas (`IsAvailable`, `IsLocked`, `Open`). |
| `Services/EmiDesk/EmiSuggester.cs` | 245 | Which six of them are on the ring today (pins first, then exponential-decay usage score). |
| `Services/EmiDesk/EmiState.cs` | 347 | `%LOCALAPPDATA%\ConditioningControlPanel\emi-desk.json` — the runtime ledger. |
| `Services/EmiDesk/EmiBarkBridge.cs` | 206 | One hook inside `BarkService.Raise` that mirrors ~25 app events into moments. |
| `Services/EmiDesk/EmiNames.cs` | 227 | Machine key → a name she is allowed to say out loud. Returns **null**, never the raw key. |
| `Services/EmiDesk/EmiGifRain.cs` | 91 | Calls the Chaos gif cascade for her. Not a fork of it. |
| `CCP.Core/Services/EmiDesk/EmiDebug.cs` | 73 | The two documented QA env overrides (§10.6). |
| `CCP.Core/Services/EmiDesk/EmiChrome.cs` | 196 | **When her chrome is lit.** Pure, clock-injected hover region + grace timer (§15.1). |
| `Windows/EmiDesk/EmiDeskWindow.xaml(.cs)` | 120 + 1230 | The surface, drag, resize, placement, DPI, the pet gesture, the seams. |
| `Windows/EmiDesk/EmiDeskWindow.Ring.cs` | 268 | Ring toggle + the sibling window's lifetime. |
| `Windows/EmiDesk/EmiDeskWindow.Glass.cs` | 486 | The idle clock, the glitch flip, the channel painters' driver. |
| `Windows/EmiDesk/EmiDeskWindow.Bubble.cs` | 719 | The speech bubble, the typed cadence, the ask chips. |
| `Windows/EmiDesk/EmiDeskWindow.Fx.cs` | 385 | Summon smoke, CRT power-on/off, sparkle scatter, hearts/sparks/tears/storm/bang. |
| `Windows/EmiDesk/EmiRingWindow.xaml(.cs)` | 38 + 798 | The fan: cards, layout, hooks, pin, pick. |
| `Windows/EmiDesk/EmiOptionsWindow.xaml(.cs)` | 300 + 560 | **Her options**, opened by the gear: her cards, her settings, her ring (§15.2). |
| `Windows/EmiDesk/EmiMutePromptWindow.xaml(.cs)` | 77 + 103 | Mute / Keep / Don't ask. |
| `Controls/EmiDock.xaml(.cs)` | 70 + 176 | The nav-rail chip. Self-wiring; its face is a **binding**, not a poll. |
| `Views/Controls/EmiRingPicker.xaml(.cs)` | 128 + 285 | The 25-tile pin wall. **One control, two hosts** (settings tab + options panel), one pin store. |
| `Views/Controls/AppSettings/EmiDeskSettingsSection.xaml(.cs)` | — | The settings block. Hosts `EmiRingPicker` with `ShowHeader="False"`. |
| `Resources/emi/desk-lines.json` | 357 KB | **90 moments, 90 pools, 67 asks, 1 dork pool, 40 deferred ids.** |
| `Resources/emi/fonts/NotoSansMono-latin.ttf` | — | The **face** font. Bundled because a fallback font turns the kaomoji into tofu. |
| `Resources/emi/fonts/PressStart2P-latin.ttf` | — | The **bubble/chip** font. |

---

## 2. The seams

The feature was built in five chunks by five agents against a written contract
(`docs/emi-desk/SEAMS.md`). The join is a set of **C# partial methods** on `EmiDeskWindow`, so a
chunk that was never written simply compiles away to nothing:

| Seam | Implemented by | Meaning |
|---|---|---|
| `OnReadyCore()` | window | The surface is built; safe to touch. |
| `OnBodyClickedCore(ref bool handled)` | `.Ring.cs` | A real click on her body (not a drag, not a resize, not a glass tap). Toggles the ring. |
| `OnGlassLiveQuery(ref bool live)` | `.Glass.cs` | "Is a channel up right now?" — asked before routing a click. |
| `OnGlassClickedCore(ref bool handled)` | `.Glass.cs` | A click inside `GlassRect` while a channel is live. Fires the channel's effect. |
| `OnRingOpenQuery(ref bool open)` | `.Ring.cs` | "Is the fan up?" — the glass refuses to flip behind an open ring. |
| `OnBodyMoveCore(...)` | `.Fx.cs` | The one-shot body moves (`bounce`, `nod`, `droop`, `shiver`, `thud`). |
| `OnChainFxCore(...)` | `.Fx.cs` | The one-shot bursts (`hearts`, `sparks`, `tears`, `storm`, `bang`). |
| `OnBubbleTextCore(...)` | `.Bubble.cs` | A chain frame carried a bubble instruction. |
| `OnTearDownCore()` | **`.Ring.cs`, which then calls `TearDownGlass()`** | She is leaving. |

> **A partial method may have only ONE implementing declaration.** `OnTearDownCore` is wanted by both
> the ring and the glass. The ring's file owns it and hands the rest straight on to
> `TearDownGlass()`, so neither half is lost and the ring folds *first* (a fan left hanging over the
> desktop after she has poofed reads as a crash). If you add a fourth partial that wants a seam
> someone already implements, extend the existing implementer — do not add a second declaration.

Geometry crosses the seams in **two different coordinate spaces**, and this is the single most
dangerous thing in the feature:

| Member | Space |
|---|---|
| `GlassRect` | **DIPs**, in the window's own coordinates |
| `BodyScreenRect` | **PHYSICAL screen pixels** |
| `RingAnchorScreenPoint` | **PHYSICAL screen pixels** |
| `EmiState.WinLeftPx` / `WinTopPx` | **PHYSICAL screen pixels** |
| `AppSettings.EmiDeskWidth` | **DIPs** |
| `DragThresholdDip` (the click/drag line) | **DIPs**, both sides |

See §10.1.

---

## 3. The line engine (the load-bearing section)

### 3.1 The file

`Resources/emi/desk-lines.json` (v1) has six top-level keys:

```jsonc
{
  "version": 1,
  "generated": "…",
  "moments":  { "<momentId>": { pools, odds, cooldownMs, priority, mix, spiceCeiling,
                                hold, askOdds, limit, cooldownKey, poolWhen,
                                holdMs, tailMs, holdUntilReleased } },   // 90
  "pools":    { "<poolId>": [ { t, face, … } ] },                        // 90
  "asks":     [ { id, moment, q, face, chips, yes, no, effect, spice, when } ],  // 67
  "dork":     { "pool": "common.dork", "odds": 0.08, "limit": {"per":"launch","max":1} },
  "deferred": [ … ]                                                      // 40 moment ids with no lines yet
}
```

`EmiLineEngine.Instance` is a singleton, loads the file once, and logs
`[EmiDesk] lines file v1: 90 moments, 90 pools, 67 asks` on success. **If you do not see that line in
`logs/app-*.log`, she is mute and nothing else you change will make her talk.**

### 3.2 The draw order (LINES-SCHEMA §5)

`Draw(momentId, ctx)` walks these in order and returns `null` at the first refusal:

1. **Holds and the panic silence.** A live hold (`lockdownCountdown`, `intakeRunning`, …) or an armed
   panic silence returns null immediately. Holds are released by their own `ReleaseHold(momentId)`
   call — `EmiBarkBridge` does this for `lockdownCountdown` on `LockdownDeactivated`, and a hold that
   is never released makes her permanently mute.
2. **Limits.** `limit: {per, max}` buckets (`launch`, `day`, `ever`) and `perTarget`.
3. **Cooldown.** `cooldownMs` against `cooldownKey ?? momentId`.
4. **The 45 s global floor** (`GlobalFloorMs`). One line per 45 s, whatever fired.
5. **The odds roll** (`odds`).
6. **The ask branch.** `askOdds > 0` **and** `AskGatesPass()` **and** the odds roll → an `AskDraw`
   instead of a `LineDraw`.
7. **The dork roll.** 8 %, once per launch, swaps in `common.dork`.
8. **Pool choice.** `poolWhen` conditions first, then `mix` between the moment's own pool and the
   common pool.
9. **The shuffle bag.** `EmiState.SeenByPool` deals every line in a pool once before any repeats, and
   a 40-entry global `RecentIds` ring stops the same line landing twice across pools.

**Priority 3 bypasses steps 4 and 5 only** (the ceremony moments: `desktopFirstBoot`, `summoned`,
`levelUp`, `panicPressed`, …). It never bypasses holds, limits, bedtime or spice.

`spiceCeiling` filters lines against `AppSettings.EmiDeskSpice` (0 = Innocent, 1 = Suggestive,
2 = Anything).

### 3.3 The ask gates

`AskGatesPass()` refuses unless **all** of these hold:

- `AppSettings.EmiDeskOffers` is on;
- the situation is right (`AskSituationOk()`: not mid-session, no ring open, no live bubble…);
- **not bedtime** (`EmiState.BedtimeUntil`);
- `_ignoredAsksThisLaunch < AskIgnoreLimit` (3) — **she stops asking once you have ignored three**;
- **cadence:** ≥ `AskGapMs` (10 min) since the last ask, and `SummonCount >= AskMinSummons` (3).

The last two are the only ones the QA switch skips (§10.6). The others are protections, not cadence,
and are in force in every build.

**Feasibility is checked at draw time and fails silently** (`EmiOffers.EffectFeasible`). An offer to
play a video on a machine with no videos is never *shown*, rather than shown and then fizzling. This
is why the probe lives in `EmiOffers` and is called from the engine, not from the window.

### 3.4 Substitution

`{target}`, `{n}`, `{level}`, `{streak}` and friends are substituted, never translated. A `{target}`
reaching the engine must already be a **lowercase human display name** — that is `EmiNames`' entire
job. `EmiNames.Feature("gradedintake")` returns `"graded intake"`; for a key it cannot map it returns
**null**, and a moment fired without `{target}` simply skips the token lines and draws a plain
sibling. Speaking `gradedintake` out loud is the failure this prevents.

---

## 4. The ring

Six cards, ever. No scroll, no second page.

- **Catalogue** — `EmiTargets.All`, 27 entries, each `EmiTarget(Id, LabelKey, ThumbPath, Hue,
  IsAvailable, IsLocked, Open, Gate)`. The ring never learns what a feature *is*; it asks three
  lambdas. Adding a door is one `T(...)` line plus a loc key `emi_desk_target_<id>`.
  **`Id` is the usage key, the pin key and the `ringPick` payload — renaming a shipped id silently
  resets that feature's score.**
- **`IsAvailable` vs `IsLocked`** are not the same. Unavailable **hides the card** (a dark door, a
  withheld shop: `arcademy`, `justdrop`). Locked **shows the card with a padlock** and routes the
  click to the app's own tier-gate refusal — never to a sales pitch of her own.
- **Suggester** — score is `Σ 0.5 ^ (ageDays / 7)` over every recorded open (kept incrementally in
  `EmiState.OpenScore` + `UsageAt`). Pins take their slots first in pin order; the rest go to the top
  scores, ties broken by catalogue order — which is also exactly what a brand-new user sees, because
  every score is then zero. **At most one locked card** is ever in the ring, and only when the user
  has not already earned six unlocked ones.
- **Every open counts, wherever it came from.** `App.EmiDesk.NoteOpen(id)` is called from
  `MainWindow.ShowTab`, `OpenStudioModule` and each host's `Launch` — not only from the ring.
- **Opening it** (wave 3, and it changed) - **right-click her body**, or left-click the **cards
  glyph**, the six-dot chip that fades in top-left on hover opposite the x. The **left click is the
  pat now** and never opens the ring: see §13. Escape, a click anywhere else, a second right-click
  or the glyph again folds it; dragging her folds it on the **first movement** rather than the drop.
  A pat does **not** fold it (a pat is affection, not a dismissal, and folding on one would grow
  `RingIgnoreStreak` on the friendliest thing the user does).
- **Card interactions** - left-click a card opens it (via `Pick`, which owns the usage counter and
  the moments), right-click a card pins/unpins it (max 6; pinning all six turns the suggester off,
  deliberately). Unchanged by wave 3, and the ring window's own handlers must stay that way.
- **Pinning from settings** - the Settings > EMI Desk > *Her ring* picker is a second front end onto
  the SAME `EmiState.Pins`, always written through `EmiSuggester.TogglePin` / `ClearPins`. There is
  no second store, and a source guard (`EmiGestureAndPinWiringTests`) keeps it that way.
- **Layout** — a full circle when there is room, and a half fan pushed *away* from whichever screen
  edge she is parked against, then every card clamped into the work area regardless (which is what
  makes a corner park honest). Radius is `BodyWidth/2 + CardW/2 + 14` DIP, bumped just enough that
  six cards on that chord never overlap (`FanRadius`). Cards are 112 × 84 DIP with an 8 px label.

---

## 5. The glass

The little screen she carries. While she is out and nobody is touching her, after
`EmiChannels.IdleBeforeFlip` (**90 s**) the glass **glitches** for `GlitchMs` (220 ms, four torn
frames) and flips to one of four channels for `ChannelLife` (10 s):

| Channel | Painted | A tap fires |
|---|---|---|
| `spiral` | WPF shapes | the app's own spiral overlay, 6 s, at the user's own opacity |
| `video` | a frame from the user's own videos folder | one video, the Videos tab's own path |
| `burst` | flash thumbnails | a short flash burst |
| `rain` | falling gif tiles | `ChaosGifCascadeOverlay` for 10 s |

**Local assets only.** Nothing in `EmiChannels` or `EmiOffers` fetches anything; the app-wide
remote-media consent only ever matters downstream inside services that already own their own remote
helpers. A channel whose library is empty is not offered (`EmiChannels.Pick()` skips it).

The face keeps painting **underneath** the whole time, so killing a channel is hiding one node and
never touches the locked face renderer. The glass will not flip while the ring is open
(`OnRingOpenQuery`), while an ask is live, or while `AppSettings.EmiDeskGlass` is off.

---

## 6. Moments — and how to add one

A **moment** is the app telling EMI that something happened. It is a string id plus an optional
payload. Firing one is one line:

```csharp
App.EmiDesk?.Fire("sessionHalfway", new { target = EmiNames.Feature(tab) });
```

`Fire` is safe from any thread, safe when she is not out (it is a cheap no-op), and never throws.

There are **two** ways a moment reaches her:

1. **The bulk mirror.** `EmiBarkBridge` puts one call at the top of `BarkService.Raise` and maps ~25
   triggers the avatar's bark service already owns the subscription lifetime for. A row is
   `Row(Moment, Ctx, Pick, Side)`; `Pick` may return a *different* moment id (`featureOpened` →
   `featureOpenedRepeat` on the second visit) or **null to drop the fire** (an idle transition that is
   going the wrong way). Adding a mirrored moment = one row in `_table`.
   The mirror **never touches the bark's own context** — it builds its own from the same `fill`
   delegate, so reading a value cannot perturb the bark about to be matched.
2. **An inline call** at the real raise point, for anything the bark context is lossy about. The whole
   `session` family and `brainDrainOn` are deliberately *not* mirrored for exactly this reason:
   `SessionCompleted`'s bark context discards the XP, the elapsed time and the pause count.

### To add a moment

1. Pick the id. It is the vocabulary the pools key off, so it is permanent.
2. Add `moments["<id>"]` and `pools["<id>"]` to `Resources/emi/desk-lines.json` (and remove the id
   from `deferred` if it is listed there).
3. Fire it: a row in `EmiBarkBridge._table` if a bark trigger already exists, otherwise one
   `App.EmiDesk?.Fire(...)` at the raise point.
4. If it takes a `{target}`, map the key through `EmiNames` **at the hook**, never in the engine.
5. If it is a **hold** (`hold: true`), you must also call `EmiLineEngine.Instance.ReleaseHold("<id>")`
   on the ending edge, or she goes mute forever.

The **40 `deferred` ids** in the lines file are moments that are wired but have no lines written yet
(`loomOpened`, `dayTurned`, `weekend`, `discordLinked`, …). They draw nothing and log nothing. They
are a content backlog, not a bug.

---

## 7. Settings, and the mute arbiter

### 7.1 Settings (`Models/AppSettings.cs:8000+`, UI in `Views/Controls/AppSettings/EmiDeskSettingsSection.xaml`)

| Key | Default | Meaning |
|---|---|---|
| `EmiDeskEnabled` | `true` | Master switch. Off = no chip, no hotkey, no widget. |
| `EmiDeskHotkey` | `"Ctrl+Alt+E"` | The summon chord. **A modifier is required**; bare keys are refused at capture *and* at arm time. |
| `EmiDeskMuteAvatar` | `true` | Ask to mute the avatar tube while she is out. |
| `EmiDeskMuteDontAsk` | `false` | The user ticked "don't ask again". Cleared by turning `EmiDeskMuteAvatar` off and on again. |
| `EmiDeskSpice` | `2` | 0 Innocent / 1 Suggestive / 2 Anything. Filters lines by `spiceCeiling`. |
| `EmiDeskOffers` | `true` | Whether she may ask (offers). Off = the ask branch never fires. |
| `EmiDeskGlass` | `true` | Whether the glass may flip to a channel. |
| `EmiDeskWidth` | `220` | Her body width in DIPs (152 … 420). **The only home for the width** — `EmiState` keeps the rect and the monitor, not the size. `RestorePlacement()` re-reads this on every summon, so a change made while she was away (the shrink offer, the slider) is on her when she next comes out. |

The section has a **seventh row** since wave 3, *Her ring*, which is not an `AppSettings` key at
all: it writes `EmiState.Pins` through `EmiSuggester`. See 13.4.

### 7.2 The mute arbiter — the product's core rule

Two voices at once is the failure mode this whole feature exists to avoid; **a mute the user never
chose is the second one**. So both halves are required:

```csharp
public bool AvatarMuted =>
    IsOut && App.Settings.Current.EmiDeskMuteAvatar && _muteAccepted;
```

- `IsOut` short-circuits first, so "she is not out" costs nothing and the avatar is instantly loud
  again the moment she leaves.
- `_muteAccepted` is set by `EmiMutePromptWindow` (Mute / Keep / Don't ask). "Don't ask" counts as
  agreeing from then on.
- `BarkService.cs:1544` returns `GateDecision { WouldFire = false, Reason = "emi-desk-mute" }`. So the
  arbiter is **visible in the log**:
  `[BARK] blocked trigger=Idle rule=idle_quip_48 … reason=emi-desk-mute`. If you are debugging "the
  avatar went quiet", grep that string.
- The reverse direction: `EmiDeskService.NoteEmiSpoke()` tells `BarkService` that an external line
  was spoken, so the avatar's own min-gap counts her lines too — but only when she is *not* muting
  him, otherwise she would be silencing him twice over.
- `TubeBubbleLive` lets her **wait** rather than overlap when the avatar already has a bubble up.

---

## 8. Where to change X

| I want to… | Go to |
|---|---|
| change what she *says* | `Resources/emi/desk-lines.json` — never the engine |
| change *when* she may say it | the moment's `odds` / `cooldownMs` / `limit` in the same file |
| make her shut up for a while | a `hold: true` moment, plus `ReleaseHold` on the ending edge |
| add a ring card | one `T(...)` in `EmiTargets.Build()` + loc key `emi_desk_target_<id>` |
| change the ring geometry | `EmiRingWindow.xaml.cs` — `CardW`/`CardH`/`BodyGap`/`CardGap`/`FanRadius` |
| change the bubble size or font | `EmiDeskWindow.Bubble.cs:45-70`, the constants block |
| add a glass channel | `EmiChannels.All` + a painter + an `EmiOffers` effect + a feasibility probe |
| add a face animation | `EmiChains.Chains` — but it is a **locked port**; change `chains.js` first |
| change the summon FX timing | `EmiDeskWindow.Fx.cs:29-32` |
| change the idle-before-glass | `EmiChannels.IdleBeforeFlip` (90 s is an owner lock) |
| change where she parks | `EmiDeskWindow` `SnapToNearestCorner` / `ClampIntoWorkArea` |
| change how long her chrome lingers | `EmiChromeHover.DefaultGraceMs` (750 ms; §15.1) |
| change the size of a chip's hit area | `EmiDeskWindow.ChipPad`, then `ApplyBodyWidth` — never the drawn glyph |
| add a row to her options panel | `EmiOptionsWindow.xaml` + a handler; the setting must already exist in `AppSettings` |
| change the pin wall | `Views/Controls/EmiRingPicker` — **both** hosts get it |
| find out why she said nothing | `logs/app-*.log`, grep `[EmiDesk]` — then §3.2 in order |

---

## 9. Reading the log

Every EmiDesk file writes through **static** `Serilog.Log`. The lines you should see on a healthy
launch, in this order:

```
[EmiDesk] app events wired
[EmiDesk] face font from NotoSansMono-latin.ttf
[EmiDesk] state loaded (N pins, M tracked targets)
[EmiDesk] lines file v1: 90 moments, 90 pools, 67 asks
[EmiDesk] summon hotkey armed: Ctrl+Alt+E (slot=0xB1B4)
```

then, per summon:

```
[EmiDesk] summoned (user), firstBoot=false, summon #3
[EmiDesk] pixel font from PressStart2P-latin.ttf     (first bubble only)
[EmiDesk] ring open with 6 cards
[EmiDesk] dismissed
```

The file sink's minimum level is **Information** (`App.xaml.cs:1507`), so **every `Log.Debug` in the
feature is invisible in a normal build**. Several genuinely important refusals in this feature were
written at Debug; if you are chasing a silent no-op, check whether the branch you suspect logs at
Debug before concluding it was not taken.

---

## 10. The traps

Every one of these was hit on the first live run (QA, 2026-08-29). They are here because none of them
produce an error.

### 10.1 THE COORDINATE TRAP — DIPs vs physical pixels

`BodyScreenRect`, `RingAnchorScreenPoint` and the persisted `winLeftPx`/`winTopPx` are **physical
screen pixels**. `GlassRect`, `Window.Left/Top/Width/Height` and `EmiDeskWidth` are **DIPs**. The
conversion is the window's *own* scale:

```csharp
var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
double s = m?.M11 ?? 1.0;     // NEVER assume 1.0
```

On a mixed-DPI desk (the dev rig is 125 % + 100 %) an assumed 1.0 puts the ring 25 % off her body and
restores her onto the wrong monitor. Anything crossing a seam is documented with its space in
`SEAMS.md`; keep it that way.

**This trap has already bitten once, and quietly.** The drag threshold — "how far may the mouse
travel and still count as a click?" — was one constant read in *two* spaces: the body measured
`PointToScreen` deltas (physical) while the ring's drag watch measured `GetPosition` deltas (DIPs).
At 125 % they disagreed by a quarter, so a 7 px hand tremor was a click to the ring and a drag to
the body: the ring refused to toggle *and* she crept across the desktop by exactly the tremor. It is
now one name in one space (`DragThresholdDip`, `EmiDeskWindow.xaml.cs`), the body scales before it
measures, and `EmiDeskLiveRunRegressionTests` fails if either half drifts back.

### 10.2 The hotkey arming trap

`EmiDeskService.ApplyHotkey()` is called from **MainWindow's `Loaded`** handler, and WPF raises
`Loaded` from *inside* `mainWindow.Show()` (`App.xaml.cs:2466`) — while `App.MainWindowRef` is not
assigned until the line *after* `Show()` returns (`App.xaml.cs:2480`). The original code read
`App.MainWindowRef`, saw null, logged at **Debug** (invisible, §9) and returned **without ever
retrying**: `Ctrl+Alt+E` was dead in every build and only the dock chip summoned her. It now falls
back to `Application.Current?.MainWindow` (which the `Window` constructor sets), and the "no main
window" branch logs at Warning. **If you add another early caller, do not reintroduce the
`MainWindowRef`-only read.**

### 10.3 The static Serilog sink

`App.Logger` is an *instance*; the house convention elsewhere is `App.Logger?.Xxx(...)`. Every
EmiDesk file `using Serilog;` and writes through the **static** `Log.Information(...)`. Serilog hands
static callers a `SilentLogger` unless `Log.Logger` is assigned, and nothing in this app assigned it —
so the entire `[EmiDesk]` log stream (215 call sites, plus ~150 pre-existing ones in Descent,
Haptics, V2Auth and LocalizationManager) went to the floor, and the `Log.CloseAndFlush()` at shutdown
was a no-op. Fixed by `Log.Logger = Logger;` immediately after `CreateLogger()` in `App.xaml.cs`.
**Do not remove that line.**

### 10.4 The namespace trap

Everything under `Windows/` is in the **flat `ConditioningControlPanel` namespace**. A tidy-minded
`namespace ConditioningControlPanel.Windows.EmiDesk;` shadows the WinRT `Windows` root namespace and
breaks `Services/ScreenOcrService.cs` with `CS0234`. The header of every file under `Windows/EmiDesk/`
says so; believe it.

### 10.5 The chord, not a key

The panic and pause bindings ride a modifier-blind `WH_KEYBOARD_LL` hook that does **not consume** the
press. A summon chord whose *base* key is one of them would summon EMI and tear the session down in
the same keystroke. `ApplyHotkey` refuses that case explicitly via `PanicPolicy.FindHookClash`, and
the settings capture refuses a bare key. Every refusal is a **logged no-op** — the chip keeps working,
so a taken chord costs a line in the log and nothing else.

### 10.6 The QA env overrides (`CCP.Core/Services/EmiDesk/EmiDebug.cs`)

Two documented environment variables, absent in every normal launch:

| Var | Effect |
|---|---|
| `EMI_DESK_IDLE_MS` | Overrides `EmiChannels.IdleBeforeFlip` (clamped 1 000 … 3 600 000 ms) so the glass is reachable inside a play-test instead of after 90 s. |
| `EMI_DESK_DEBUG` | The **QA cadence**: skips the per-moment cooldown, the 45 s global floor, the odds roll, and the *two cadence* ask gates (the 10-minute gap and the 3-summon minimum). |
| `EMI_DESK_RESET_ONBOARDING` | Puts the three gesture nudge tracks back to a fresh install at startup (pat count, ring opens, the three gist latches, the lifetime fire counts) so the tutorial can be play-tested more than once per machine. Only those five fields; the summon count and the streaks are untouched. **Ctrl+Shift+Alt+click her body** does the same thing live, without a restart. |

`EMI_DESK_DEBUG` moves **nothing that protects the user**: holds, the panic silence, bedtime, limits,
spice, feasibility and the ignore streak are all still in force. When either is set the launch logs
`[EmiDesk] DEBUG overrides active: …`. The static constructor is fully try/caught, because a throwing
static constructor poisons the type for the whole process.

### 10.7 Her window is not her

`EmiDeskWindow` is `OverlayPad` DIPs bigger than she is on every side. `Window.Left` is the *window's*
left; `BodyScreenRect.X` is `(Left + OverlayPad) * s`. Forgetting the pad puts everything you compute
half a body-width off.

### 10.8 WM_DPICHANGED is swallowed

WPF's automatic rescale of a layered window is a synchronous `CompleteRender` delivered inside the
drag's modal move loop, and it deadlocks against this surface's own writers (the chain timer, the FX
sweep). She keeps her birth DPI and gets one controlled re-clamp on a 450 ms settle instead
(`EmiDeskWindow.WndProc`). Do not "fix" this by letting WPF handle the message.

### 10.8b The mute prompt runs a nested message pump

`Summon()` sets `IsOut = true` and then **blocks** in `MaybeAskAboutMuting()` →
`EmiMutePromptWindow.Ask()` → `ShowDialog()`. A modal runs a nested pump, and this app's global
hotkey, dock chip and tray menu all keep working inside it — so a user can send her away *in the
middle of her own summon*. Before the guard, the dialog returned and the summon carried on and
`Show()`ed her anyway: **she came back on screen with `IsOut == false`**, and since `Dismiss()`, the
hover x and the avatar mute arbiter all guard on `IsOut`, nothing could touch her again for the rest
of the session. The x was dead, the chord re-summoned instead of dismissing, and the avatar talked
straight over her.

The invariant, and it is the one to hold on to if this code is ever reshaped:

> **A summon that was overtaken must not finish, and a widget that is on screen must always be
> dismissable.**

Both halves are enforced in `EmiDeskService`: `_summonGen` is stamped before the prompt and checked
after it (a stale generation returns without showing her), `Dismiss()` bumps the generation *first*
so a summon parked in the pump is invalidated, and `Toggle()`/`Dismiss()` fall back to the window's
real `Visibility` when the flag disagrees — a desync is logged at Warning and repaired rather than
being allowed to strand her. Anything new that blocks inside `Summon()` (another dialog, a
`DoEvents`, an `await` that pumps) must sit behind the same check.

### 10.9 The ring is a second window

`EmiRingWindow` covers the whole work area, and its click-away hook is a **global** low-level mouse
hook, so it must (a) be cheap, (b) touch only the frozen `_hotPx` snapshot, and (c) **always return
false** — swallowing the click would cost the user the thing they were clicking on. The snapshot
includes **her body rect**, because a click on her is her own toggle and not a dismissal; if you
change `CardW`/`CardH`/the radius you must re-check that snapshot or the hit rects drift off the
drawn cards. `ShutDown()` closes the widget without running the tear-down seam, so the ring is also
killed from the widget's `Closed` handler or it outlives her.

### 10.10 She writes real user state

`%LOCALAPPDATA%\ConditioningControlPanel\emi-desk.json` is her ledger — where she was parked, which
cards are pinned, which lines each pool has already dealt, the bedtime cutoff. Load is lazy and never
throws (a corrupt file is logged once and replaced). Save is **debounced 500 ms on the dispatcher**, so
the ring's rapid-fire counter bumps collapse into one write. When testing, back it up: deleting it
resets someone's whole history with her, silently.

---

## 11. Deliberately not done

Owner decisions and known gaps as of 2026-08-29. Check `docs/emi-desk/BRIEF.md` before assuming any of
these still stand.

- ~~**No voice.**~~ Done in wave 2: `Services/EmiDesk/EmiVox.cs` implements `IEmiVox`. See §12.
- **No AI, no network, ever.** Preset lines only. There is no code path from EMI Desk to OpenRouter or
  to the server, and adding one would break the product's premise.
- **No remote media.** Local assets only in `EmiChannels`/`EmiOffers` (§5).
- **40 deferred moments** are wired but unwritten (§6). Content backlog.
- **`crashRecovered` cannot speak at boot.** The moment fires before the widget exists, and she is
  summoned, not always-on — so nobody is there to say it. It needs a "say it on the next summon"
  queue, which does not exist.
- **The dork odds are a constant** (8 %, once per launch, in the lines file's `dork` block). There is
  no user-facing knob and the owner has not asked for one.
- **Offers WAIT rather than give up.** An unanswered ask stays until it is answered or cancelled; there
  is no auto-timeout. The protection is the ignore streak (3), not a clock.
- **No double-click gesture anywhere near her.** It would delay the ring click, which is the primary
  interaction.
- **Multi-monitor position restore is honest but untested on >2 monitors.** She restores to the
  monitor named in `EmiState.Monitor` and re-clamps into its work area if that monitor is gone.

---

## 12. Wave 2 polish (2026-08-29)

The owner's second live run. Seven items, all of them feel rather than function, plus the two things
the QA lap turned up on its own. What changed, and what you now have to keep true.

### 12.1 The ring frame and the fan

`EmiRingWindow` only. The card frame is **3 DIP** of `#88FF69B4` at rest, the full `#FF69B4` on
hover, **4 DIP** solid pink when the card is pinned, and there is a **1 DIP dark seam drawn inside**
it as a sibling border (a `Border` has exactly one `BorderThickness`, so the seam cannot be part of
the frame). The fan is `PopMs = 340`, `PopStaggerMs = 62`, `FadeMs = 210`, `PopFromScale = 0.55`,
`PopBackAmplitude = 0.30`: **650 ms** from the click to the last card at rest, against 180/40/0.45
before. Every transform is created with the card, never mid-animation, because minting a
`ScaleTransform` inside a storyboard on a layered window is the stutter people report as "the
animation is not smooth".

### 12.2 The corner fan is a solver now, not a clamp

`Services/EmiDesk/EmiRingLayout.cs` is a **pure function** of (centre, body, work area, count) and it
is the only place the ring geometry lives. It grows the radius in 6 DIP steps over the longest
feasible arc (720 half-degree samples) until every card is inside the work area AND every pair is
`MinCardGap = 10` DIPs apart as **rectangles**, and falls back to a column when no radius works.
The old code fanned on a fixed radius and then clamped each card into the work area, which honours
"on screen" by breaking "not on top of each other" - parked bottom right it stacked three cards half
under the taskbar. Because it is pure, `Tests/…/EmiRingLayoutTests.cs` walks every corner and edge of
five desktops at one to six cards in a millisecond. **Keep it pure**: the moment it reads a window it
stops being testable and the corner rots again.

### 12.3 React: squash, pet, wobble

`Windows/EmiDesk/EmiDeskWindow.React.cs`, with four named transforms authored together in the XAML
(`CrtScale`, `SquashScale`, `WobbleRotate`, `MoveShift`) so no animation has to re-parent another.
The rotate sits **above** the scales: a squash applied after a rotation shears her.

- **Click** squashes to `SquashY 0.92 / SquashX 1.06` over `SquashDownMs 90` and settles back over
  `SquashUpMs 260` with an elastic ease. It runs **before** the click is routed, so no outcome can
  swallow a click in silence.
- **Head click** was the wave-2 pet and is **gone as a special case**: wave 3 made the whole
  silhouette the pat. `HeadBottomFrac = 0.30` now only arms the 1.2 s **hover** pet. See §13.
- **Drag** drives `WobbleRotate` from a low-passed horizontal velocity (`WobbleVelKeep 0.78`,
  `WobbleFollow 0.35`, `WobbleDegPerVel 0.010`) clamped to `WobbleMaxDeg 9`, swaps the face to
  `>_<` past `WobbleFaceVel 420` and `@_@` past `WobbleDizzyVel 1150`, and settles as a damped
  pendulum over `WobbleSettleMs 720` on release.

The **6 DIP click/drag threshold is untouched** and must stay untouched: it is what separates a
click from a drag on a window with no chrome.

### 12.4 The arcademy farewell

`EmiDeskService.FarewellForArcademy()`, called from `ArcademyHostService.Launch` **before**
`Fire("arcademyOpened")`. She draws the `arcademyBye` pool at priority 3, says it, and dismisses
herself with the wink outro at `ArcademyByeDismissMs = 1360 + 1600` ms - the 1360 is the LOCKED dot
cadence, so a flat 1.6 s would power her off mid-read. The farewell claims her voice for
`ArcademyByeSuppressMs`, which is how the bye beats `arcademyFromRing`: the ring fires its own line
*after* the target's `Open()` has already reached the host. There is a hardcoded fallback line, and
the moment is fired through a **const, not a literal**, because `EmiMomentIdWiringTests` scans
`Fire("...")` literals against the pool file and the pool is authored separately. That step around
the typo guard is only safe because `EmiDeskFarewellTests` checks the id, the order in the host and
the fallback instead.

### 12.5 BLIPESE on the desktop

`Services/EmiDesk/EmiVox.cs` behind `IEmiVox`, wired to the bubble seam in
`EmiDeskWindow.Bubble.cs`. **The campus blips are WebAudio synthesis, not audio files** - there was
nothing to copy - so the port brings the oscillator with it: `MakeScore` is a line-for-line port of
`emi/vox.js` (seeded per line text by FNV-1a into mulberry32, so the same sentence always sounds like
itself), and the score is rendered offline into one mono 16-bit 44.1 kHz WAV, cached by content hash
under `%LOCALAPPDATA%\ConditioningControlPanel\emi\vox` (cap 96 files) and played through
`App.Audio.PlayOneShot` with the tag `emi-vox`. Never mint a `WaveOutEvent` per cue.

She always bleeps - the vox is **not** gated on the avatar-mute arbiter, because the arbiter is about
her *voice actress*, not about her UI - but she is silenced by `IsOutputSuppressed`, by a
`MasterVolume` of 0, and by `EmiLineEngine.Instance.HoldActive`, which covers panic and every safety
moment. No new setting: the master volume already is one.

**Her texture is a separate service.** `Services/EmiDesk/EmiSfx.cs` owns the three one-shots the
widget makes on its own - the pat, and the ring fanning open and folding shut - and keeps this exact
gate, copied rather than shared: the vox is her VOICE and the sfx is her UI, and the day one grows a
rule the other must not inherit, a shared helper is the thing that gets it wrong. Every cue is an
override-then-fallback chain in the `ChaosSfx` shape (`emi/<cue>.mp3` wins if it exists, else a
sound already in the repo), so bespoke art needs no code change - and `EmiSfxAssetTests` asserts each
chain's LAST link exists, because a typo in a filename otherwise ships as "the pat has no sound"
with nothing anywhere saying why. The trims are deliberately low (owner, 2026-08-30: "a bit too
loud", twice) and the pat carries a 130 ms floor so a double click cannot machine-gun it.

The vox hangs off **`ShowBubble`**, not off the chain seam, because the bubble has two authors: a
chain frame arrives through `OnBubbleTextCore`, and the ask cadence calls `ShowBubble` directly. On
the seam alone every question was silent, which is the one line she most wants read. `HideBubble`
cuts the burst. One to three dots route to `Tick()` rather than a burst, so the `.` `..` `...`
cadence ticks and only the words sing.

**The mood trap.** `EmiChains.Player.Step()` fires `hooks.Bubble` **before** `hooks.Draw`, and
`MakeSay` carries no `BodyFrame` at all, so the voice cannot be read off the pose when the bubble
asks for it. `_voxMood` is set in `Say` (from the reaction face) and in `PlayChain` (from the chain's
own pose, and only when it has one, so `Say`'s value survives). If you ever make the say chain carry
a frame, delete the `Say` half - not the other one.

### 12.6 The bubble, and the two things QA found

The window is `OverlayPadX = 330` DIPs wider than she is on each side and `OverlayPad = 120` taller,
which is the room the bubble has; `LayoutBubble` flips it to her left when the right runs out, refuses
a flip that clips worse than what it was fixing, and then **clamps** into the window and the work
area both, left edge winning. That last clamp is the half that was missing.

**THE MEASURE TRAP**, which is why the clamp looked broken when it was not. `DesiredSize` is what
the bubble asked for on the last measure pass, and at the instant a line lands it can be a long way
under the box that is actually drawn (the pixel font arrives after the text, the wrap resolves on
the arrange). Clamping an optimistic 91 DIPs is how a 370 DIP bubble ended up starting 90 DIPs from
the right edge of the monitor. `LayoutBubble` now positions against `max(DesiredSize, ActualSize)`
**and** re-runs on the bubble's `SizeChanged`, so whatever box finally exists is the box that got
clamped. Setting `Canvas.Left/Top` cannot change a size, so that hook cannot feed itself.

Two more fell out of the live lap:

- **The chips were not clamped.** Parked at the right edge, the offer read `ooh` and half of a `nah`.
  `LayoutChips` now reuses the exact window the bubble was clamped into (`_bubbleClampLo` /
  `_bubbleClampRight`) and right-aligns to the bubble on a flip. The right-hand chip is always the
  "no": losing it turns a two-way offer into a one-way one.
- **The offer owns the bubble.** Any chain that starts while a question is up - a click reaction, a
  pet, an idle beat that slipped the stop - used to repaint the bubble, and a chain frame with no
  text used to clear it, leaving the two chips sitting under an empty crown. `OnBubbleTextCore` now
  returns early while `_ask != null`. The ask cadence is unaffected: it calls `ShowBubble` directly.

---

## 13. Wave 3: the gestures, the nudges and the ring picker (2026-08-29)

Owner report, verbatim: *"I still see emi Not reacting to the pats (when u click it) we should have
the left click pat emi. Make it know btw, some barks every so and often till the user gets the gist,
then no more, nudging towards the petting. Also how can a user customize the bubbles with their
favourite feat rn?"*

### 13.1 The gesture table, as it now stands

| Gesture | What it does |
|---|---|
| **Left-click anywhere on her body** | **Pat.** Squash + the `pet` chain + a line from the `petted` pool. |
| Left-click inside a live glass channel | Belongs to the glass (unchanged, and still resolved geometrically). |
| **Right-click her body** | **Open / fold the ring.** |
| **Left-click the gear** (top-left, hover) | Open her options menu (cards, size, spice, offers, glass, pins). |
| Hover her head for 1.2 s | Pat. The second trigger for the same gesture; it counts the same. |
| Left-click the x (top-right, hover) | Send her away. |
| Drag past 6 DIP | Move her. Folds the ring on the first movement. |
| Drag the corner grip | Resize, 152..420 DIP, aspect locked. |
| Right-click a ring CARD | ~~Pin / unpin it.~~ **Nothing.** The pin left the cards on 2026-08-30 - it was undiscoverable and the owner reported the button as unusable. Pinning is now the tile wall in `EmiRingPicker`, hosted by BOTH the settings tab and the gear menu. `PinToggled` survives as a seam with no raiser: see 13.2. |
| Escape, or a click anywhere else | Fold the ring. |
| Ctrl+Shift+Alt+left-click her body | **QA only**: replay the gesture tutorial. |

The **6 DIP click/drag threshold is still untouched** and must stay untouched: it is what separates
a click from a drag on a window with no chrome.

**Why the left click had to move.** Wave 2 put the pat on the top 30% of her and left the other 70%
toggling the ring, so the obvious gesture - click the mascot - did the one thing that is not
affection. `PetFromClick()` in `React.cs` now consumes **every** completed left click on her body,
and returns true on all of its early exits (a chain in flight, input locked, mid-transit) because
the caller has already played the squash: a click never goes nowhere.

**Inside `PetCooldownMs` (6 s)** she plays `PetFlickChain` instead: `^_~` for 320 ms, rest for 180,
with the pet pose and a bounce, and **no line**. Not the `wink` chain, which runs 1.28 s and reads
as a whole beat. This is also what makes a **double click harmless** - the second click of the pair
lands ~200 ms into a 6 s cooldown, so it can never draw a second line.

**Only a pat that got past the cooldown counts** toward `EmiState.PetsTotal`. The flicks are
acknowledgement, not affection she registered; counting them would let a mashed pointer reach the
"gist" in one second.

### 13.2 The cards glyph  *(superseded by §15.2 — it is a gear now)*

`BtnCards` in `EmiDeskWindow.xaml`: 18 DIP, top-LEFT, six pink pixel dots in a `#E60E0E1C` disc with
a `#FF69B4` ring - the exact mirror of `BtnClose`, on the same 140 ms `FadeChrome` fade, scaled by
the same `chip` in `ApplyBodyWidth`. It exists because **nothing on a desktop advertises a right
click**, and the ring had just lost its only discoverable affordance.

> **The word "door" is on EMI's absolute fence** (`docs/emi-desk/tools/check-lines.py`, `VOICE.md`):
> it is an Arcademy story spoiler. Call this thing her **cards** in every string, tooltip and loc
> key. Code identifiers are unconstrained; user-visible text is not.

### 13.3 The nudge machine

`Services/EmiDesk/EmiNudges.cs`. Three teaching tracks, each with a pool of the same name in
`desk-lines.json` (priority 2, spice 0, `limit {per:"ever", max:6}`):

| Track | First | Repeat | Stops forever when |
|---|---|---|---|
| `petNudge` | 25 s after a summon | 4 min | `PetsTotal >= 3` (latched as `PetGistGot`) |
| `ringNudge` | 40 s, from the **2nd** summon | 6 min | `RingOpens >= 2` (latched as `RingGistGot`) |
| `pinNudge` | on a ring open, 900 ms after the fan deals, at most once per summon | - | the first pin is made (`PinGistGot`) |

Shared brakes: a **hard lifetime cap of 6 fires per track**, **never two nudges within 90 s**, and
nothing at all unless `Quiet` - which reuses `EmiDeskService.AskSituationOk()` plus "she is visible,
no chain is live, no engine hold", so the tutorial cannot develop its own idea of calm and drift
away from the offers'.

**`EmiNudgeMachine` is pure**: no timers, no dispatcher, no `App`, an injectable clock and the world
behind `IEmiNudgeWorld`. That is what lets `EmiNudgeMachineTests` walk twenty summons and every
stopping condition in a millisecond, and it is the property to protect - a teaching line that
outlives the lesson is the single easiest way to make her the thing people turn off.

**Attempt vs fire.** `Attempted(world, track, spoke)` is called whether or not a line reached the
screen. The repeat clock and the 90 s floor move on the **attempt**, so an engine refusal costs a
retry interval instead of turning the 5 s poll into a hot loop; only a line that really landed
spends one of the six lifetime fires.

**`DrawNudge` is deliberately two-path.** When `EmiLineEngine.Instance.MomentIds` knows the track,
the engine's answer is final (a refusal is a refusal - faking a line past a cooldown would make the
tutorial the loudest thing she owns). When it does **not** know the track at all - the pools have
not landed in this tree - a hardcoded line stands in, behind nothing but the safety hold. That path
never goes through `Fire`, which is why the track ids are consts: `EmiMomentIdWiringTests` scans
`Fire("...")` literals, exactly as `ArcademyByeMoment` does.

**QA:** `EMI_DESK_RESET_ONBOARDING=1` (see §10.6) or **Ctrl+Shift+Alt+click her body** replays the
whole tutorial, re-arming against the current summon so the first pet nudge is 25 s away without a
restart. It resets only the five onboarding fields; the summon count and the streaks are untouched.

### 13.4 The ring picker in settings

Row 7 of `EmiDeskSettingsSection`: a `WrapPanel` of 92x66 tiles, one per **available** target, art +
name, pink frame and a dot when pinned. **Checked = pinned**, into the same `EmiState.Pins` the
ring's own right-click writes, through `EmiSuggester.TogglePin` - never a list of its own. "Let her
choose" is `EmiSuggester.ClearPins()`.

Three details that are load-bearing:

- At six pins every unchecked unlocked tile goes **disabled**, so `TogglePin`'s refusal of a seventh
  is something the user sees coming rather than a click that silently does nothing. The tile is then
  set to whatever the **store** ended up saying, not to what the click asked for.
- **Locked targets are shown, disabled, with the gate reason**; unavailable ones are skipped
  entirely. Same rule as the ring, and `ToolTipService.SetShowOnDisabled` is required or the reason
  never appears.
- The wall is **rebuilt on every Loaded**, not refreshed: a pledge landing or a mod uninstall changes
  `Available` / `Locked` while the tab is closed, and both are delegates, not properties.

`EmiDeskService.RefreshRing()` re-fans an open ring from any thread, so a pin made in settings shows
in a fan that happens to be up rather than on the next open.

## 14. ALIVE wave A: the watching wave (2026-08-29)

`docs/emi-desk/ALIVE-PLAN.md` is the pitch; this is what shipped of it. Wave A is the "she is
looking at me" half: no physics, no new windows, no new packages. Six behaviours, one clock, and a
priority rule that puts every one of them last.

**The two files.** `Services/EmiDesk/EmiAlive.cs` holds the numbers and every decision as pure
static functions and two clock-free state machines (`FidgetScheduler`, `PokeLadder`), so the wave is
walked in milliseconds by `Tests/ConditioningControlPanel.Tests/EmiAliveTests.cs` with no window
anywhere. `Windows/EmiDesk/EmiDeskWindow.Alive.cs` is the window half: it reads the cursor, converts
it, asks `EmiAlive` what should happen, and plays it. If you are changing a number, it is in
`EmiAlive`. If you are changing what a beat looks like, it is in `Alive.cs`.

### 14.1 One clock, hung off her visibility

A single `DispatcherTimer` at `EmiAlive.PollMs` (100 ms, `DispatcherPriority.Background`) drives all
six items. It is started and stopped from `IsVisibleChanged`, NOT from the summon: every road that
puts her on screen or takes her off it (summon, dismiss, a bare `Hide` in teardown) moves the timer
with her and none of them has to remember to. `ShutDown()` and `OnClosedCleanup` also call
`StopAlive()` so nothing survives her window.

It deliberately does NOT ride `StopIdleBeats` / `RestartIdleBeats`, which every chain touches: the
lean has to keep tracking while she talks, and the wave has to be able to see the moment she is free
again.

**The coordinate trap applies here in full (see 10.1).** `GetCursorPos` returns PHYSICAL pixels and
so does `BodyScreenRect`; everything `EmiAlive` computes is in DIPs. The tick divides both by
`DipScale` once and passes DIPs down. Do not add a step that mixes them.

### 14.2 The priority rule

`CanPerk()` is the only gate any wave-A beat passes through, and it says no while ANY of these is
true: `Busy()`, a chain is live, an ask is up, the line engine holds, she is being dragged, she is
being resized. A wave-A face is always the lowest priority thing on her face and yields instantly.
`EmiAlive.CanPerk` is the pure half of that and is pinned by a test matrix.

The blink is the one exception, and only because it claims nothing: it is a bare `DrawFace` lid swap
that re-checks `Busy()` at every step (see 14.3).

### 14.3 Blink parity

The pitch stage rolled a coin on a 4.2 s tick and, when it won, played the 2.7 s `blink` CHAIN. That
meant up to twelve seconds of stone stillness, then two blinks in a row, and every blink stopped and
restarted the idle beats.

It is now the campus blink (`Resources/web/arcademy/emi/widget.js`, `idle()`): a raw lid swap of
`BlinkHoldMs` (110 ms) on a `BlinkEveryMs` (5200 ms) clock, plus `BlinkJitterMs` (600 ms) either way
so it never becomes a metronome, and one blink in `DoubleBlinkOneIn` (7) doubled with a 120 ms gap.
The idle timer re-jitters its own interval on every tick. Every step of the swap re-checks
`BlinkStillOurs()`, because the lid is a bare `DrawFace`: if a chain took the face while her eyes
were shut, the restore must not paint over it.

### 14.4 The lean

`GazeShift` is a `TranslateTransform` on the FaceView element in the XAML, under her body's own
transform group, so the lean composes with the CRT scale, the click squash and the drag wobble for
free and never touches the locked `EmiFace` renderer.

`EmiAlive.GazeTarget` is the campus rule: `dx / GazeDiv` (60) clamped to `GazeMaxDip` (3) scaled by
how big she is (`bodyWidth / 150`). **The cap scales and the divisor does not**, which is what keeps
her saturating at the same RELATIVE distance (about 1.2 body widths) at every size; scaling both
would make a big EMI notice you from four times further away. Easing is campus `0.15` per 60 Hz
frame, converted once to a per-poll constant by `GazeEasePerPoll` so the 100 ms tick has the same
time constant as the 16 ms one.

Target is zero (and eases home, it does not snap) whenever the wave does not own her face, she is
dragged, or motion is reduced.

### 14.5 Approach, linger, fidgets, pokes

- **Approach.** Crossing into `ApproachDip` (120) of her EDGE earns one beat: the canon `glance`
  chain if you came at her faster than `GlanceSpeedDipPerMs` (1.2), the quiet `o_o` perk if you
  walked. Then nothing for `ApproachCooldownMs` (30 s), so she is a mascot and not a bell you can keep ringing. It is
  edge triggered on entry, so a pointer parked inside the radius costs nothing.
- **Linger.** A pointer resting ON her for `LingerMs` (2 s) with no click gets `^_^`; still no pat at
  `LingerAwayMs` (4 s) gets the flat look away. The episode ends the moment the pointer leaves, and
  ANY touch cancels the look away: the point of that beat is that the pat never came. A stage
  advances whether or not the face reached the screen (one attempt per stage), because retrying
  every 100 ms would make her stare at a parked pointer.
- **Fidgets.** Every 25 to 50 s of genuine idleness, one small wordless thing, never the same one
  twice running (`FidgetScheduler`): a 2 DIP twitch, a 1 degree weight shift held 2 s, or a glance.
  Every 20 to 40 minutes, a stretch (`1.04` scale up and settle, `>_<` into `^_^`). Both wait for a
  quiet moment rather than forcing one.
- **Pokes.** `PokeLadder` counts pats inside a 4 s window: the second earns the annoyed face, the
  third the glare plus a shiver, then a 60 s truce during which pokes are simply forgiven. It does
  NOT decide what a pat does, `PetCooldownMs` still does; it only says which face the cooldown's
  flick wears, which is why the two cannot fight. Three pats inside four seconds are all inside the
  six second pet cooldown by construction, so the ladder can only ever re-dress a flick and can
  never eat a pat that was going to draw a line.

### 14.6 Reduced motion

`AliveMotionOk` is `MotionFx.Level == MotionLevel.Full`. At Reduced and Off the MOVING half goes
away (the lean, the twitch, the weight shift, the stretch's scale) and the FACES still play, which
is exactly what the campus does under `prefers-reduced-motion`. A look is not motion.

### 14.7 QA notes

There is no environment override for the wave. The blink (5.2 s) and the approach cooldown (30 s)
are watchable in a single sitting; the fidget (25 to 50 s) needs a minute of patience with the
pointer parked well away from her, and the stretch (20 to 40 minutes) is realistically only ever
seen by accident. If wave B wants one, the cheap version is an `EmiDebug` switch read at the two
places `_fidgetDue` and `_stretchDue` are set in `StepFidgets`; it was left out here because
threading it into the pure scheduler is not the one-liner the rest of `EmiDebug` is.

Measured on the live lap (2026-08-29, 2560x1440 at 125 percent): blink lid 110 ms measured as one to
two sample frames at 31 ms, intervals landing inside the 4.6 to 5.8 s window, and the lean tracking
the pointer smoothly with visible saturation at both ends of each axis and no jitter while parked.

---

## 15. The chrome wave: the forgiving hover, and the gear (2026-08-30)

Two owner reports off the live run, and they landed together because they are the same corner of her.

### 15.1 Her chrome is a REGION now, with a grace

> "when we hover the buttons next to emi (the drag buttons, the X to close her, or the arrow to
> resize), they should show and be clickable. Right now I gotta hover EMI and be fast enough to
> catch those buttons before they disappear."

The old rule was one pair of handlers on `BodyRoot` straight onto a 140 ms fade. The brief blamed
chips overhanging the transparent pad; that is **not** what it was. `BodyRoot` is exactly `bw x bh`
and both chips sit inside it. The three real causes:

1. the pointer **clips outside her outline** for a frame or two while arcing toward a corner;
2. a **grip drag** walks the cursor down and right, off her, in the first ten pixels, so the handle
   being actively held faded back to `GripRestOpacity`;
3. the squash and wobble `RenderTransform`s momentarily **shrink the hit rect** under a stationary
   pointer, which delivers a leave nobody asked for.

Every one of those is "left for a moment", and 140 ms is not enough time to come back.

`CCP.Core/Services/EmiDesk/EmiChrome.cs` now owns the decision and none of the drawing:

- `EmiChromePart` (Body / Close / Gear / Grip) is a **flag set**, not a single value: WPF can hand
  out a child's enter before its parent's leave, so "which one is it?" is unanswerable and "how many
  are holding it open?" is;
- `EmiChromeHold` (Drag / Resize / Press / Menu) pins it lit through the gestures that take the
  pointer off her as part of doing their job;
- leaving the whole region arms a **750 ms grace**; re-entering any part cancels it. 750 is a travel
  budget: a pointer crossing a 152 to 420 DIP silhouette to a corner is on the move for roughly 250
  to 500 ms, and past about a second the chrome stops reading as forgiving and starts reading as
  stuck.

It is pure and clock-injected for the same reason `EmiNudges` and `EmiRingLayout` are, and
`EmiChromeHoverTests` walks all of it, including the cases a play-test cannot reproduce reliably.
The window drives it from `WireChromePart` / `WireChromePress` and a one-shot `DispatcherTimer`
re-armed on every event, and calls `ResetChrome()` on hide - a leave that never arrives because the
window was pulled out from under the pointer would otherwise latch `Body` forever.

**The hit areas grew; the glyphs did not.** Each chip is now `chip + 2 x ChipPad` DIPs of
`Background="Transparent"` with the drawn chip held in place by the button's `Padding` - the split
`ResizeGrip` already had. `ApplyBodyWidth` computes that padding **asymmetrically** so the hit area
never overhangs `BodyRoot`: the outward sides take only the inset that is really there (~4 DIPs at
her default width) and hand the rest to the inward sides. That is not tidiness. The pad around her
is `Background="{x:Null}"` precisely so clicks fall through, and a chip hanging into that air would
turn her transparent corner into a click trap. For the same reason both buttons are
`IsHitTestVisible` only **while lit**: at rest her whole silhouette pats her, corners included.

### 15.2 The six dots are a gear, and the gear opens her options

> "the Pin button is not usable right now, I propose we remove it from there and replace the three
> dots to move emi (not needed, we drag her) with a little gear, that brings up the EMI option menu"

The owner read the wave-3 ring glyph as a move handle. It never was one - she is dragged by her whole
body - so the dots are gone and `BtnCards` is `BtnGear`: eight axis-aligned rectangles on a 16 x 16
grid (a hollow hub and four teeth), `EdgeMode="Aliased"`, never a circle. A smooth anti-aliased cog
beside a pixel CRT mascot reads as somebody else's icon set.

`EmiOptionsWindow` is her fourth window and carries the same recipe as the other three, plus one
consequence worth stating out loud: **`WS_EX_NOACTIVATE` means it can never hold the keyboard.**
Everything in it is mouse-only, the summon chord is *shown* rather than captured, and the rebind
stays in the settings tab, which can take focus. It is **non-modal** on purpose (§10.8b: anything
modal on a summon path needs the `_summonGen` guard), placed with the same physical-pixels-over-own-
DPI arithmetic as `EmiRingWindow.PlaceWindow`, and dismissed by the same scoped low-level mouse hook
that never swallows the click. Her body is in the hook's hot list so a second gear click *toggles*
instead of racing itself.

It contains, in order:

1. **Open her cards.** First, and full width. The dots were the ring's only visible affordance and
   nothing on a desktop advertises a right click. Right-click on her body still opens the fan
   directly; that gesture is untouched, and both roads still end in `ToggleRingFromGesture`.
2. **Her options:** the chord (read-only), her size, the mute arbiter, the 0..2 spice ceiling, her
   offers, her glass. Every row is an `AppSettings` key that already exists and is already read at
   the moment it matters. **Nothing here invents a setting with no behaviour behind it.**
3. **Her ring:** `EmiRingPicker`, the *same control* the settings tab hosts.

### 15.3 One pin wall, two hosts

The 25-tile picker moved out of `EmiDeskSettingsSection` into `Views/Controls/EmiRingPicker`. There
is exactly one pin store - `EmiState.Pins`, written through `EmiSuggester` - and the surest way to
end up with two was to write the picker twice. The settings tab sets `ShowHeader="False"` and draws
the count line and the reset button in its own section hue; the panel uses the built-in header.

**Every brush and style in that control is a literal.** One host has MainWindow's resource
dictionary behind it and the other has nothing at all, so a `StaticResource` reaching out of the
control would resolve in the settings tab and take a BAML `EndOfStream` in the panel. Same rule in
`EmiOptionsWindow.xaml`.
