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

All three are `WindowStyle=None` + `AllowsTransparency=True` + `ShowActivated=False` +
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
    BtnClose            the hover x (18 DIP, opacity 0 until hover)
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
| `Services/EmiDesk/EmiDebug.cs` | 73 | The two documented QA env overrides (§10.6). |
| `Windows/EmiDesk/EmiDeskWindow.xaml(.cs)` | 120 + 1230 | The surface, drag, resize, placement, DPI, the pet gesture, the seams. |
| `Windows/EmiDesk/EmiDeskWindow.Ring.cs` | 268 | Ring toggle + the sibling window's lifetime. |
| `Windows/EmiDesk/EmiDeskWindow.Glass.cs` | 486 | The idle clock, the glitch flip, the channel painters' driver. |
| `Windows/EmiDesk/EmiDeskWindow.Bubble.cs` | 719 | The speech bubble, the typed cadence, the ask chips. |
| `Windows/EmiDesk/EmiDeskWindow.Fx.cs` | 385 | Summon smoke, CRT power-on/off, sparkle scatter, hearts/sparks/tears/storm/bang. |
| `Windows/EmiDesk/EmiRingWindow.xaml(.cs)` | 38 + 798 | The fan: cards, layout, hooks, pin, pick. |
| `Windows/EmiDesk/EmiMutePromptWindow.xaml(.cs)` | 77 + 103 | Mute / Keep / Don't ask. |
| `Controls/EmiDock.xaml(.cs)` | 70 + 176 | The nav-rail chip. Self-wiring; its face is a **binding**, not a poll. |
| `Views/Controls/AppSettings/EmiDeskSettingsSection.xaml(.cs)` | — | The settings block. |
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
- **Interactions** — left-click opens (via `Pick`, which owns the usage counter and the moments),
  right-click pins/unpins (max 6; pinning all six turns the suggester off, deliberately), Escape or a
  click anywhere else folds it, dragging her folds it on the **first movement** rather than on the
  drop.
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

### 10.6 The QA env overrides (`Services/EmiDesk/EmiDebug.cs`)

Two documented environment variables, absent in every normal launch:

| Var | Effect |
|---|---|
| `EMI_DESK_IDLE_MS` | Overrides `EmiChannels.IdleBeforeFlip` (clamped 1 000 … 3 600 000 ms) so the glass is reachable inside a play-test instead of after 90 s. |
| `EMI_DESK_DEBUG` | The **QA cadence**: skips the per-moment cooldown, the 45 s global floor, the odds roll, and the *two cadence* ask gates (the 10-minute gap and the 3-summon minimum). |

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

- **No voice.** `IEmiVox` exists as a seam for the bleep/vox pass; nothing implements it. She is
  silent audio-wise by design for v1 (BRIEF: stretch goal).
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
