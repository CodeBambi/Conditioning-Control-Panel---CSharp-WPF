# WPF surface reachability — how a user actually reaches a feature

**Status:** evidence document. Extracted 2026-08-18 by `wpf-archaeologist` from the shipping WPF tree.
**Baseline:** v6.8.1 (`ec3189b7`, merge `1d1f8997`). Citation validity re-verified against this baseline
after the merge moved 9 of the 36 cited files (see §6).

This document exists because the port's dashboard row (`task-board.md`, "Dashboard entry points for
landed surfaces") was about to be worked on a **false premise**. The premise was: landed surfaces need
a dashboard card that opens them. WPF does not work that way, and building it that way would have been
a divergence dressed as parity.

---

## 1. The load-bearing rule

> **Dashboard tiles navigate. They never launch.**

Stated verbatim in the wall's own header comment (`MainWindow/MainWindow.Presets.cs:1036`,
"Navigation tiles still navigate to the ONE existing entry, never launch") and repeated as the reason
the Play page's Loom card deliberately refuses to call `LoomHostService.Launch()`
(`Views/Tabs/PlayTabView.xaml:1292-1299`; `MainWindow/MainWindow.Presets.cs:1007`).

The corollary is a one-entry rule: **each feature is allowed exactly one window-opening button
anywhere in the app**, and it lives on the feature's destination page, never on a tile
(`MainWindow/MainWindow.Presets.cs:1007`, "the Spiral module keeps exactly ONE editor entry").

**So every surface in WPF is TWO HOPS from the home screen**: a tile or rail door navigates to a page,
and a button on that page opens the window.

**None of the three surfaces the port has landed has a dashboard tile at all.** The Home mosaic
(`Views/Tabs/SettingsTabView.xaml`, key `"settings"`) is a 4x4 velvet mosaic of 11 tiles: Flash,
Video/BubbleCount, Subliminals, Bouncing Text, the nameless tease tile, Spiral/PinkFilter, the `?` box,
MindWipe/BrainDrain, the Vault, Bubble Pop, Lock Card (`Views/Tabs/SettingsTabView.xaml:800,806,812,
818,840,1083,1113,1209,1220,1250,1256`). DTRH, Loom, Goon, FYP, Intake, Remote and Deeper each had a
mosaic tile for one day in Aug 2026 and it was **removed on purpose**.

---

## 2. The card gesture grammar

| Gesture | Meaning | Evidence |
|---|---|---|
| Left-click anywhere on the card | **OPEN** the feature's Studio module page | `Features/FeatureCard.xaml.cs:607,613` -> `Views/Tabs/SettingsTabView.xaml:791` -> `MainWindow/MainWindow.Presets.cs:1039` |
| Right-click | **TOGGLE** the feature on/off | `Features/FeatureCard.xaml.cs:616,624,625` -> `MainWindow/MainWindow.Presets.cs:1241` |

- **Clicking the title is identical to clicking the body.** The bottom gradient strip carrying the
  title is `IsHitTestVisible="False"` (`Features/FeatureCard.xaml:74-76,83`), so title clicks fall
  through to the card root. **WPF has no separate title gesture.** The port must not invent one.
- The whole card surface is the target: `Cursor="Hand"`, `MouseLeftButtonUp`, `MouseRightButtonUp` are
  declared on the UserControl root (`Features/FeatureCard.xaml:4-6`).
- The `?` help button swallows both gestures (`Features/FeatureCard.xaml.cs:611-612,619-620`).
- Tier badge, active ring, rim-light, tease veil and title strip are all `IsHitTestVisible="False"` —
  decoration, never input targets (`Features/FeatureCard.xaml:63,76,101,113,158`).
- The toggle flips the persisted flag **and** starts/stops the live service if the engine is running,
  then saves; it is refused outright while a running session owns that dose
  (`MainWindow/MainWindow.Presets.cs:1245,1251-1264`).
- The grammar is surfaced to the user in a rail chip tooltip: "left-click opens it, right-click turns
  it on" (`Views/Tabs/SettingsTabView.xaml:453`).
- Split tiles resolve **which half** by cursor position at click time
  (`Features/SplitFeatureCard.xaml.cs:307-318`); hover sweeps the seam so the hovered half fills the
  tile and owns the click (`Features/SplitFeatureCard.xaml.cs:41-46`).
- No card takes keyboard focus: mouse handlers only, no keyboard activation path found in either card
  control.

### Card states

| State | Look | Input |
|---|---|---|
| **Lit** (feature on) | 3.5px mod-tinted ring + drop-shadow glow, **breathing together on one 3.5s clock** (ring 0.55->1.0, glow 0.50->0.90); parked at peak values when the window is inactive, minimized, hidden, or reduced motion is on | normal |
| **Priced** (tier-gated) | small "TIER N" pill top-left; art deliberately **NOT** dimmed | clickable; `TierGate` raises the refusal |
| **Locked** (level-gated) | `#C0000000` overlay, lock glyph, caption `Lvl N`; content to 35% opacity; ring suppressed; hover killed | right-click does nothing, **left-click still raises Click** |
| **Teased** | 26-radius Gaussian blur, `#59000000` veil, large "?", tier-livery rim, title reads "???" | still hovers and clicks; opens a teaser popup |
| **Hidden** | collapsed entirely | reserved for "this door does not exist for you" |

Evidence: `Features/FeatureCard.xaml.cs:28-32,40,44,306-355,388-410,427-440,448-483,577-588`;
markup `Features/FeatureCard.xaml:63-71,109-114,117-133,151-166`.

**The locked/priced distinction is doctrine, spelled out in the source**: a lock band advertises
something buyable; a door the server has not opened is **collapsed, not locked**, because it is not
for sale (`MainWindow/MainWindow.PlayTab.cs:117-125`). Priced cards are deliberately left bright so
the art still sells the feature (`Features/FeatureCard.xaml:135-150`).

**In v6.8.0/v6.8.1 no live dashboard tile ever sets `IsLocked` or `LockLevel`** — an exhaustive search
for writers of either property across `MainWindow/`, `Views/` and `Features/` found none. The locked
look is a card capability with no current user. **The port must not build it as though it were live.**

---

## 3. Route: DTRH (Down The Rabbit Hole)

**Hop 1** — nav rail "Play" door medallion (`MainWindow/MainWindow.xaml:741`, loc key `nav_door_play`,
`Localization/Languages/en.json:4687`) or its sub-entry (`:785`); both call `ShowTab("play")`
(`MainWindow/MainWindow.TabNavigation.cs:941`).

**Hop 2** — the Play page's top **hero card**, visible title `DOWN THE RABBIT HOLE` at 25px bold with
a "NEW" pill (`Views/Tabs/PlayTabView.xaml:426,432`). Two buttons:
- `FALL IN` (pink primary, tooltip "you were always going to.") -> `BtnStartChaos_Click`
  (`Views/Tabs/PlayTabView.xaml:455,458` -> `PlayTabView.Cards.cs:44` -> `MainWindow.Lab.cs:219`)
- `Quick Drop` (tooltip "skip the dollhouse. fall straight in with your saved settings") — same
  destination, skips the picker, reuses the last slot (`MainWindow.Lab.cs:308,313-323`)

These are the **only** in-app launchers; `DtrhHostService.Launch` has no other UI caller (callers:
`MainWindow.Lab.cs:239,252,321` and the `--dtrh` CLI at `App.xaml.cs:2428`).

**Gate:** Tier 2, `TierGate.DemandLab("Down the Rabbit Hole", "dtrh")` (`MainWindow.Lab.cs:228,313`).
Allowed with Lab access **or** when the server names `"dtrh"` as today's free feature
(`Services/TierGate.cs:88-94`); **fails closed when the Patreon service is null**.

**When gated the card is present, fully readable, and still clickable**: a violet lock band paints over
it (scrim `#A8120A1E`, about 66% alpha so the art still reads; 1px `#FFB47BFF` rim; a flask glyph at
26px; the line "Lab only") and is `IsHitTestVisible="False"` so clicks pass through
(`Views/Tabs/PlayTabView.xaml:508-512`, styles `:246,248,251,260`; `hm3_rail_lock_t2` = "Lab only",
`en.json:4842`). A gated click therefore **arrives and is refused out loud**: an 8-second Warning toast
"Down the Rabbit Hole is a Tier 2 perk - upgrade your pledge to unlock it." with a "See tiers" action
opening App Info & Data (`Services/TierGate.cs:128,133`; `en.json:4704,4705`). Nothing opens.

The card's two checkboxes are the one genuinely *disabled* part when gated, because they write settings
through a TwoWay binding with no handler to refuse in (`MainWindow/MainWindow.PlayTab.cs:88-91`).

**Setup step:** a modal save-slot picker, `ChaosSlotPickerWindow.Pick(this)` (`MainWindow.Lab.cs:246`
-> `Chaos/ChaosSlotPickerWindow.xaml.cs:48,53`) — 720x560 borderless, centred on the main window,
headed "Down the Rabbit Hole" / "Choose your save" / "Pick a slot to descend into. Each keeps its own
progress.", three slot cards, `Cancel` and `Descend`. Cancel returns null and nothing opens.

**Already-open:** the click never reaches the picker; it refocuses the existing window
(`MainWindow.Lab.cs:237-241`; `Services/Chaos/DtrhHostService.cs:73`).

**What opens:** a separate top-level modeless window titled "Down the Rabbit Hole" hosting a local
WebView2 page (`DtrhHostService.cs:120,134`), **windowed not fullscreen** (`:128`), input-enabled
(`:124`), glued above the main window by native `GWL_HWNDPARENT` ownership rather than `Topmost`
(`:133`; `Chaos/ChaosWebViewHost.cs:521-616`). Frame: normal title bar, resizable, in the taskbar, not
topmost, black background, **85% of the PRIMARY screen and centred on the PRIMARY screen** regardless
of where the main window is (`ChaosWebViewHost.cs:223,233,234,277,279,290,293`).

**Immediately after it opens the main CCP window is minimized to the tray with no notification**, and
keyboard focus is pushed into the web view (`DtrhHostService.cs:156`; `MainWindow.RemoteControl.cs:1517`).
Closing the game window restores the main window from the tray (`DtrhHostService.cs:995-998`).
While it is up it owns the panic key: the global Esc/panic path hands off and returns unconditionally
(`MainWindow/MainWindow.xaml.cs:1077`).

**Two alternate destinations behind the same click.** Unticking "3D game (recommended)"
(`ChaosWebGameEnabled`, default `true`) makes FALL IN start the classic WPF descent instead
(`MainWindow.Lab.cs:233,242,255-264`). And if the web page reports a WebGL boot failure the host
records `BootFailedThisSession`, tears the window down and **silently** launches that same classic
path; every later click this session skips the web window entirely
(`DtrhHostService.cs:859-870`, read at `MainWindow.Lab.cs:234`).

**Failure path:** any exception in the handler shows a blocking `MessageBox` titled "Down the Rabbit
Hole" reading "Couldn't start Down the Rabbit Hole:" plus the message (`MainWindow.Lab.cs:269`).

---

## 4. Route: the Loom studio

**Hop 1** — the mosaic's diagonal split tile, **top-left half** titled "Spiral Overlay" (emoji
stripped, mod-aware, `MainWindow/MainWindow.UiUpdates.cs:101,124`). Left-click calls
`OpenStudioModule("spiral")` (`Views/Tabs/SettingsTabView.xaml:1087` -> `SettingsTabView.xaml.cs:279-282`
-> `MainWindow/MainWindow.Presets.cs:1009`), which selects the rack row and switches to the Studio tab.
**It does not open any window** (`MainWindow.Presets.cs:976-978,1009`).

**Hop 2** — on the Spiral Overlay module page, a wide outlined button labelled
`THE LOOM — weave your own spiral` (literal, **not localized**, `Features/SpiralFeatureControl.xaml:128-133`),
placed above the spiral library grid specifically so it is reachable without scrolling (`:126`).
Handler `BtnOpenLoom_Click` -> `LoomHostService.Launch()` (`Features/SpiralFeatureControl.xaml.cs:444,448`).
This is the **only** caller of `LoomHostService.Launch` in the whole app.

**A second signpost to the same place:** a full-width strip in the Play page's "MORE" zone, title
`pl6_loom_title` = "Loom", blurb "weave your own spirals. it lives on the Spiral module in the Studio.",
button `pl6_open_in_studio` = "Open in Studio" (`Views/Tabs/PlayTabView.xaml:1300,1340-1348`;
`en.json:4875-4877`). Its handler navigates to the module and **explicitly refuses to launch**
(`PlayTabView.Cards.cs:109-110`).

**Gating: NONE anywhere on this path.** `LoomHostService.Launch` has no entitlement check
(`Services/Chaos/LoomHostService.cs:30-77`), the rack entry carries no tier
(`Views/Tabs/StudioTabView.xaml.cs:490`, default `tier = 0` at `:548`), the Play strip carries no lock
band. **The WPF source itself flags this as possibly unintended**: "out of scope: LoomHostService has
no tier check at all - worth a ledger row" (`Views/Tabs/PlayTabView.xaml:1298-1299`). The code says
ungated; see §7 ambiguity 3.

**Not session-locked either:** the button carries no `SessionLock.Owned` marker, unlike `ChkEnable`
and the opacity slider on the same page (`Features/SpiralFeatureControl.xaml:45,85`), and only marked
controls are disabled during a running session (`MainWindow/MainWindow.SessionFeatureLock.cs:312,447`).
The Loom stays openable mid-session.

**No setup step:** the click goes straight to `Launch()`.

**What opens:** a separate top-level modeless window titled "The Loom", another local WebView2 host,
windowed (`LoomHostService.cs:58,59`). **It is NOT owned by the main window** — `OwnedByMainWindow` is
not set and defaults false (`LoomHostService.cs:41-64`; option declared `ChaosWebViewHost.cs:116`) — so
it can fall behind the main window. **That is the one structural difference from the DTRH window.**
Same frame otherwise (85% of primary, centred, title bar, resizable, taskbar, not topmost, black).
Idempotent: a second click refocuses (`LoomHostService.cs:32`). Closed with the title-bar X; the host
releases its singleton on `Closed` (`:69`).

**Edge cases.** The Spirals folder (`%LOCALAPPDATA%/ConditioningControlPanel/Spirals`,
`Services/Chaos/DtrhLoomStore.cs:34`) is created before launch, because WebView2 silently skips a
virtual-host mapping whose folder is missing (`LoomHostService.cs:38`). With an empty folder the
underlying Spiral page shows an inline empty-state line rather than an empty grid
(`SpiralFeatureControl.xaml:194`), and saves made in the Loom appear live via a store-changed
subscription (`SpiralFeatureControl.xaml.cs:474-479`). **Failure path: logged only — no dialog,
nothing visibly happens** (`SpiralFeatureControl.xaml.cs:450-453`).

---

## 5. Route: the companion window — and why the port's button is a divergence

**WPF has no dashboard button that opens the companion window.**

- The dashboard's companion element is a quiet one-line "Companion" strip, sub-line "Chat, takeover,
  awareness and permissions", with a chevron, which **navigates to the Companion page**
  (`Views/Tabs/SettingsTabView.xaml:1864,1868,1883,1887` -> `SettingsTabView.xaml.cs:405-408`;
  `en.json:4686,4838`).
- The primary way the window appears is **not a gesture at all**: it is created automatically during
  main-window startup whenever `AvatarEnabled` is true (`MainWindow/MainWindow.xaml.cs:2912` ->
  `MainWindow/MainWindow.Companion.cs:145`), and **`AvatarEnabled` defaults to `true`**.
- The explicit in-app control is on the Companion page's hero card: an eye `ToggleButton` (tooltip
  "show companion") bound to `ToggleShownCommand` (`Views/Controls/Companion/CompanionHeroCard.xaml:606-609,616`
  -> `Runtime/CompanionHeroRuntimeVm.cs:117-118` -> `MainWindow/MainWindow.CompanionRoom.cs:82`).
  When she is off the card reads "she's asleep — wake her?" with a "Wake her up" pill
  (`CompanionHeroCard.xaml:488-491`; `en.json:4530,4547`).
- A third entry lives outside the app window: the tray menu's "Wake Bambi Up!"
  (`MainWindow/MainWindow.xaml.cs:348-351` -> `MainWindow.Companion.cs:265`).
- No entitlement gate on showing the window (the gates on that page are for AI chat, not the avatar).
- The window is chrome-less: `WindowStyle="None"`, `AllowsTransparency="True"`, `ShowActivated="False"`,
  `ShowInTaskbar="False"`, `ResizeMode="NoResize"`, `SizeToContent="WidthAndHeight"`
  (`AvatarTube/AvatarTubeWindow.xaml:9-16`); owned through raw `GWL_HWNDPARENT`, not `Window.Owner`
  (`AvatarTube/AvatarTubeWindow.Windowing.cs:491,500`).

**Consequence for the port.** `MainWindow.axaml`'s "Open companion" button
(`client/src/CcpClient.Desktop/Views/MainWindow.axaml:67-73`) reproduces neither WPF entry: WPF's
dashboard element navigates to a page, and the window itself appears at startup from a setting. The
port's button is a **divergence**, not the "one real entry point" the board row calls it.

---

## 6. Citation validity at v6.8.1

The extraction ran while the v6.8.0 -> v6.8.1 merge landed underneath it. 9 of the 36 cited files
changed between `f2db1e25` and `ec3189b7`. Each was re-anchored by token:

| File | Delta | Verdict |
|---|---|---|
| `MainWindow/MainWindow.Presets.cs` | +98 -58 | **cited region intact.** Every hunk falls in lines 262-513 (a preset-chip-rail redesign). `:1007`, `:1009`, `:1036` re-grep to exactly those lines |
| `Views/Tabs/SettingsTabView.xaml` | +13 -0 | comment-only (mod-art framing note); mosaic markup untouched |
| `Views/Tabs/PlayTabView.xaml` | +8 -1 | comment-only (mod-art framing note) |
| `MainWindow/MainWindow.TabNavigation.cs` | +30 -1 | `ShowTab("play")` re-greps to `:941` exactly |
| `MainWindow/MainWindow.xaml` | +94 -3 | nav-rail citations shift by 1-2 lines: door medallion `:739` -> **`:741`**, sub-entry `:784` -> **`:785`** |
| `MainWindow/MainWindow.xaml.cs` | +202 -40 | changed regions are 418+, 1570+, 1987+, 2132+; cited `:1077`, `:348-351`, `:2912` need re-grep before use |
| `Chaos/ChaosWebViewHost.cs` | +284 -7 | frame citations `:116,:223,:233,:277,:279,:293` intact; **the glue block moved: `:528-541` -> `:521-616`** |
| `AvatarTube/AvatarTubeWindow.xaml` | +53 -0 | pure append at `:584`; cited `:9-16` intact |
| `Localization/Languages/en.json` | +56 -4 | key-addressed, not line-addressed; unaffected |

**Tooling note carried from the extraction:** `Read` and `Grep` returned line numbers 7-40 lines off
the real content for several large files (`App.xaml.cs`, `MainWindow.Presets.cs`, the tail of
`PlayTabView.xaml`). **If a citation here does not resolve, re-grep the quoted token rather than
trusting the offset.**

---

## 7. Ambiguities — owner decisions, not implementation guesses

1. **What "the dashboard" means for the port.** WPF's mosaic contains no DTRH, Loom or companion tile,
   so "reach surface X from the dashboard" has no one-hop WPF answer for any of the three. If the
   port's dashboard is meant to correspond to WPF's **Play** page rather than its **Home** mosaic, the
   parity target for DTRH is the Play hero card. **Settled by the owner naming which WPF page the
   port's dashboard corresponds to.**
2. **Whether the port's companion button is an intended addition** (see §5) — reproduce WPF's
   page-navigation, or keep the direct toggle as a deliberate divergence.
3. **Whether the Loom's lack of a tier gate is intentional.** Code says ungated; the WPF source
   comment says someone thinks it may be an oversight (`PlayTabView.xaml:1298-1299`). **The port
   should reproduce the CODE unless the owner says otherwise**, and record which it chose.
4. **Keyboard reachability.** No keyboard shortcut, accelerator or command-palette row was found for
   DTRH, the Loom or the companion (`Services/SettingsPaletteIndex.cs`,
   `Windows/SettingsPaletteWindow.xaml.cs` contain no matching rows). Absence is not proven beyond
   those searches.
5. **DPI behaviour.** All window sizing is expressed in WPF DIPs against
   `SystemParameters.PrimaryScreenWidth/Height` (`ChaosWebViewHost.cs:223,293`); no explicit DPI code
   exists on these paths. Settled by a headed capture on a mixed-DPI multi-monitor rig.


---

## 8. Live UI survey of the shipping v6.8.1 app (2026-08-18)

Everything above was read from source. This section was observed by **driving the running app**
(pid attached over UI Automation, navigation gestures only, captures to PNG). It supersedes source
inference where the two differ, and it is the design target the owner asked the port to take
inspiration from: *"since the new ui in wpf has improved a lot you can take insparation from it."*

**Method note, worth keeping:** the app holds a single-instance mutex (`App.xaml.cs:43`), so
`Process.Start` on a running app hands off and exits, leaving a dead handle and no window. Attach to
the existing process instead. The survey never invoked anything that launches, purchases, sends,
deletes or changes a setting; the one exception was dismissing a "Got it" onboarding card that was
occluding every capture, which is disclosed here rather than left implicit.

### 8.1 The rail is SIX DOORS, each with sub-entries

Verified by `AutomationId`, so these are stable handles rather than guessed labels:

| Door | AutomationId | Tooltip |
|---|---|---|
| Home | `DoorHome` | Home |
| Studio | `DoorStudio` | Studio (carries a NEW badge) |
| Companion | `DoorCompanion` | Companion |
| Play | `DoorPlay` | Play |
| You | `DoorYou` | You |
| Library | `DoorLibrary` | Library (carries a NEW badge) |

Sub-entries observed, each its own button: `BtnSettings` (Dashboard), `BtnNavStudio` (Effects Rack),
`BtnPresets` (Presets), `BtnNavHaptics`, `BtnCompanion` (Companion settings), `BtnNavBambiTakeover`
(Takeover), `BtnNavSheListening` (She's Listening), `BtnLab` (Play), `BtnNavAwareness`, `BtnDeeper`,
`BtnPatreonExclusives` (Premium), `BtnDiscordTab` (Profile), `BtnNavGradedIntake`, `BtnNavSpiral`
(The Spiral), `BtnNavLockdown`.

### 8.2 A NAME COLLISION THAT WOULD HAVE SENT THE PORT TO THE WRONG PAGE

**`BtnNavSpiral` — "The Spiral", tooltip "Where your descent is drawn" — is THE DESCENT, not the
Spiral Overlay effect.** Observed live: a day-by-day tracker page headed `THE SPIRAL` with
`DAY 1 · NOT BANKED YET`, `THE EDGE`, `NEXT STAGE IN 1 DAY`, a per-day recap strip
("TAP A DAY ON THE SPIRAL — ITS RECAP PRINTS HERE"), a legend (YOU / EVENT KEEPSAKE / QUESTS DONE /
YOUR MOMENTS / STAGE / PAUSE SEAM / AHEAD / SEALED) and a `REPLAY YEAR` control.

This is the v6.8.1 shape change the sync flagged as `SpiralMapWindow` deleted and replaced by
`Views/Tabs/SpiralTabView` — **a window became a tab**, and it took the name "The Spiral" with it.
The Loom lives on the **Spiral OVERLAY** module inside the Studio rack, which is a different surface
with a near-identical name. **Any port work that routes "spiral" to one page is wrong for the other.**

### 8.3 The Studio rack — the improvement to take inspiration from

Header: **"Studio / Every effect, one rack. Pick a module on the left."** Its own onboarding card
states the model, and it is the clearest statement of the v6.8.1 UI direction anywhere in the product:

> The dashboard popups are gone. Flashes, subliminals, bubbles, bouncing text and the rest are all
> rows in the list down the left. Left-click a row to open its panel. Right-click it to flip that
> effect on or off without opening anything at all. The dot on each row is live: at a glance you can
> see everything that is currently running. Dashboard tiles land here too, on the module you clicked.
> Same dials, one room.

**Rack contents, observed:** grouped rows, each with an icon, a label, and a **live state dot** on the
right (lit when that effect is running).

| Group | Rows |
|---|---|
| EFFECTS | Flash Images, Mandatory Video, Subliminals, Spiral Overlay, Magenta Filter, Visuals |
| GAMES & CARDS | Bubble Pop, Bubble Count, Lock Card, Bouncing Text |
| IMMERSION | Mind Wipe, Brain Drain (NEW), Haptics |
| TIMING | Scheduler, Intensity Ramp |

**This confirms the click grammar in §2 is still current at v6.8.1, and generalises it: it is the
RACK ROW grammar now, not only the mosaic-tile grammar.** Left-click opens, right-click toggles.

### 8.4 The Loom route, verified live rather than inferred

`DoorStudio` -> rack row **Spiral Overlay** -> module panel containing, in order: a header with the
module name, its one-line description and an **Enable** toggle; a settings card (Opacity slider,
Randomize spiral toggle, Display monitor dropdown); then three full-width outlined buttons —
**`THE LOOM — weave your own spiral`**, `CORNER GIFs — pin a GIF to a screen corner`, `Select GIF`;
then a **SPIRAL LIBRARY** card with Open folder / Refresh, a thumbnail grid, and the empty-state line
"No spirals in your folder yet — only the built-in spiral is available."; and a large preview pane.

The Loom button was located at runtime as a real `Button` whose UIA name is
`THE LOOM — weave your own spiral`, confirming the source reading at
`Features/SpiralFeatureControl.xaml:128-133` and confirming it is **not gated and not session-locked**
on a live entitled account.

### 8.5 The Play page, observed

Headed **"Play / Games, modes, and the deep end."** The DTRH hero card carries a **PRIME SUBJECT**
diamond badge (this is the tier livery's user-facing wording — the port should not render the literal
string "TIER 2"), the title **`DOWN THE RABBIT HOLE`**, a blurb, and on the right `FALL IN` (pink
primary), `Quick Drop` (outlined), and two options: **Announcements** and **3D game (recommended)**.

> **CORRECTION (2026-08-18, SP-094).** This section originally recorded the title as
> `THE RABBIT HOLE`. That was wrong. The capture it was transcribed from
> (`evidence/wpf-ui-v681/play-page.jpg`) has the left of the title occluded by the onboarding
> card and the tier badge, and the visible glyphs were written down as though they were the
> whole string — while §3, taken from source, already said `DOWN THE RABBIT HOLE`
> (`Views/Tabs/PlayTabView.xaml:426`, literally `🐇 DOWN THE RABBIT HOLE`).
> **The §8 rule "observation beats source" was applied to an observation that was partially
> hidden. An occluded observation is not an observation; it is a guess with a photograph
> attached.** Cross-check every reading in this survey against source before relying on it, and
> treat any string whose capture is overlapped by the onboarding card, a badge or a popup as
> unread rather than as read.

Below it a **TOGETHER** section: **Goon Game** (BETA) with meta chips "free to join / send your own
media / host a room" and a `Jump In !` button; and **Remote Control** with a **BASIC SUBJECT** badge
and an `Open` button.

### 8.6 Chrome that persists on every page

A top bar (mod selector "Circe's Lock", MOD MANAGER, profile chip, active mod name, level badge,
version, a support link, language selector, an update button, help, avatar), an **XP/level strip**,
and a **bottom action bar** (favourite, a large gradient **START**, a dropdown chevron, **Save**,
**Exit**). The Home page additionally hosts a **Browser panel** (HypnoTube, Enhance toggle, an audio
slider with a Duck mode) and, at its foot, the **Companion strip** described in §5 and a
"Logged in as" identity row.

**Design consequence for the port:** the persistent chrome is a shell concern, not a page concern.
A port shell that models "rail + page host + persistent top bar + persistent action bar" reproduces
the observable structure without copying any WPF layout code.

---

## 9. Port divergences, recorded at SP-091 (the navigation shell)

The owner settled the design freedom on 2026-08-18 ("Neither, improve on both"), so the port's
layout is not held to WPF's page topology — but **every divergence is written here, in the commit
that creates it**, because an unrecorded divergence is indistinguishable from a bug.

Each row names the v6.8.1 fact it diverges from, what the port does, and **why**. Where the reason
is "the port has nothing to wire yet", it says so: a gap recorded as a feature is never revisited.

| # | v6.8.1 fact | Port at SP-091 | Reason |
|---|---|---|---|
| **D1** | The rail is **six doors** — `DoorHome`, `DoorStudio`, `DoorCompanion`, `DoorPlay`, `DoorYou`, `DoorLibrary` (§8.1) | **Three doors**: Studio, Companion, System. Home, Play, You, Library and The Spiral are **absent** | Each absent door has no ported destination. A door that opens an empty room is the same unreachability the shell exists to end. WPF's own doctrine for a door that is not open is **collapse, not lock** (`MainWindow/MainWindow.PlayTab.cs:117-125`) — a lock band advertises something buyable, and these are not for sale, they do not exist yet |
| **D2** | WPF has **no System door**; diagnostics live on the Home page's bottom button row ("System", "App Info & Data", §8.6) | The port's rail carries a **System** door | The SP-003/SP-006 startup-trace and capability-state proofs are a standing rule on the port's shell markup and must stay reachable by a gesture. The port has no Home page to hang them off |
| **D3** | WPF opens on **Home** (§8.1) | The port opens on **Studio** | There is no Home surface to open on |
| **D4** | The Studio rack is **four groups, fifteen rows** (§8.3) | **One group (EFFECTS)**, and as of SP-105 **four rows** in WPF's own order: Flash Images, Subliminals, Spiral Overlay, Pink Filter. Originally one row (Spiral Overlay); Flash Images landed at SP-098 and the other two at SP-105 | The remaining eleven modules are not ported. A rack of rows that open blank panels is the trap at row granularity, so a row lands with the module behind it — which is why the ORDER is upstream's with the unported rows removed rather than the order the modules happened to land in |
| **D5** | The Spiral Overlay row carries a **live state dot**: `Add("spiral", …, () => App.Settings?.Current?.SpiralEnabled)` (`Views/Tabs/StudioTabView.xaml.cs:490-491`), and the live capture shows lit dots on running rows (`client/docs/evidence/wpf-ui-v681/studio-rack-spiral-overlay.jpg`) | **No dot on the row** | **The port has no spiral-overlay effect whose state could be reported.** This is a GAP, not parity: a dot that always reads "off" would assert that the effect exists and is currently stopped, which is the fake-available shape the truthful-capability contract bans. (WPF *does* omit the dot on one row — `Visuals`, `:494-496`, "A dot that cannot be wired honestly is omitted" — but that rule was written for the single row with no master toggle and does not generalise to this one.) **Closes when a spiral-overlay effect lands** |
| **D6** | Right-click on a rack row **quick-toggles the effect** (`StudioTabView.xaml.cs:657-660`), the same second gesture the dashboard tiles carry (§2, §8.3) | **Right-click on the row does nothing** — no toggle, and no context menu either | **The port has no effect flag to flip.** Again a GAP, not parity: WPF's toggle-less rows do fall through unhandled (`:659`, "Rows with no Toggle fall through unhandled (Visuals)"), but WPF's *spiral* row is not one of them. The gesture is left genuinely unhandled rather than swallowed by a fake toggle. **Closes with D5** |
| **D7** | The Spiral Overlay module panel carries an Enable toggle, an Opacity slider, a Randomize toggle, a Display-monitor dropdown, three action buttons, a SPIRAL LIBRARY card and a preview pane (§8.4) | The panel carries **one honest line** saying the overlay effect is not ported, and the Loom button | Rendering dead dials is the greyed control that swallows the gesture. `CORNER GIFs` and `Select GIF` are likewise absent, not disabled |
| **D8** | The Loom button's XAML content is `🌀 THE LOOM — weave your own spiral` (`Features/SpiralFeatureControl.xaml:128-133`) | The port's button reads **`THE LOOM — weave your own spiral`** | The emoji-stripped form is the button's live UIA name (§8.4); the app strips emoji mod-aware (`MainWindow/MainWindow.UiUpdates.cs:101,124`). Observation beats source where they disagree |
| **D9** | A **second signpost** to the Loom exists in the Play page's MORE zone ("Open in Studio", `Views/Tabs/PlayTabView.xaml:1300,1340-1348`), which navigates and explicitly refuses to launch (§4) | No analogue | The port has no Play page (D1). The one-entry rule (`MainWindow.Presets.cs:1007`) is still held: exactly one control in the port opens the Loom |
| **D10** | Persistent chrome on every page: top bar, XP/level strip, bottom action bar with START / Save / Exit (§8.6) | **None of it.** The shell is rail + page host + a diagnostic footer | No mod, level, session engine, favourite, Save or Exit semantics are ported. The footer carries the SP-007 layout probe and the current route, which the headed harness drives against |
| **D11** | The companion window's PRIMARY appearance is not a gesture at all: it is created at startup whenever `AvatarEnabled` is true, and that defaults true (`MainWindow/MainWindow.xaml.cs:2912` -> `MainWindow.Companion.cs:145`, §5) | The port opens it only on request, from a control on the Companion page | §5 recorded the port's front-surface "Open companion" button as a divergence because WPF's dashboard element **navigates**. That half is now **closed**: the button moved behind the Companion door, so the port is two hops like WPF. The **startup-appearance** half remains divergent and is unclosed |
| **D12** | DTRH is reached from `DoorPlay` -> the Play hero card's `FALL IN` (§3), gated Tier 2 (`MainWindow.Lab.cs:228,313`) | **No DTRH door and no DTRH launcher anywhere in the shell** | The port has no entitlement service. An ungated DTRH button would hand out paid content and a stubbed always-allowed gate is the banned fake-available shape. **Closes at SP-092**, which lands the gate; `NavigationRouteTableTests` fails if a DTRH door appears before then |
| **D13** | — (port-internal) | The Loom's launch is idempotent-refocus, ungated, with no setup step | This is PARITY, listed for completeness: `Services/Chaos/LoomHostService.cs:29-31` (refocus if open), `:30-77` (no tier check), §4 (no picker). §7 ambiguity 3 asked whether the missing tier gate is intentional; the port reproduces **the code**, as §7 directs, and this row is the record of that choice |

**Not yet closed by this row, and named so it is not mistaken for done:** the port's shell reaches
three destinations. DTRH, Graded Intake, the AvatarTube demonstrator and the Chaos tunnel backdrop
are still reachable only by a CLI flag. Their doors are absent rather than dead, which is the honest
state, not the finished one.

---

## 10. Port divergences, recorded at SP-094 (the Play door and the DTRH gate)

The route in §3 is now real: **`DoorPlay` -> the Play page's DTRH hero card -> `FALL IN` /
`Quick Drop` -> the Tier-2 gate -> `DtrhLaunchCoordinator`**, from a cold start with no command-line
arguments. Same rule as §9: every divergence is written here, in the commit that creates it.

| # | v6.8.1 fact | Port at SP-094 | Reason |
|---|---|---|---|
| **D14** | The rail is **six doors** (§8.1); §9 **D1** recorded Play as ABSENT | The rail is **four doors**: Studio, Companion, **Play**, System — in that order, because Play sits after Companion in WPF's own rail and System is the port's own door (D2) | D1's stated condition — "each absent door has no ported destination" — is discharged **for Play only**. Home, You, Library and The Spiral stay absent for exactly the reason D1 gave |
| **D15** | §9 **D12**: "No DTRH door and no DTRH launcher anywhere in the shell … **Closes at SP-092**" | **CLOSED.** The launcher exists and is gated by `Features/Dtrh/DtrhGate.cs` over SP-092's `HostLoginEntitlement` | The condition D12 named (no entitlement service; an ungated button would hand out paid content) no longer holds. DTRH is still **not a door**: WPF reaches it in two hops and so does the port, so `NavigationRouteTableTests` still refuses "dtrh"/"rabbit" in any door's id, label or tooltip |
| **D16** | The lock band advertises the lock **before** any click — a violet scrim reading "Lab only" (`Views/Tabs/PlayTabView.xaml:508-512`; `hm3_rail_lock_t2`, `en.json:4842`) | The band **appears when a press is refused** and carries the refusal text; its caption is `LAB ONLY` on a real refusal and `COULD NOT VERIFY` on an unknown one | The port cannot know a tier without an async read of the shipping app's login. A band painted at page-mount from a cached read would advertise a state nobody re-checked. WPF's colours and its load-bearing property are kept exactly: scrim `#A8120A1E`, tier-2 rim `#FFB47BFF`, corner 15, and **`IsHitTestVisible="False"`** so the press passes through (`:245,248,252,257`) |
| **D17** | The refusal is an **8-second Warning toast** whose "See tiers" action opens App Info & Data (`Services/TierGate.cs:128,133`; `en.json:4704,4705`) | The refusal is text on the card, plus a class-and-code diagnostic line. WPF's sentence is carried **verbatim**; the upgrade route is named **in words** | The port has no toast system and no App Info & Data page. A "See tiers" button with nowhere to go is the dead control this packet exists to avoid |
| **D18** | The card carries **Announcements** and **3D game (recommended)** checkboxes, and they are the ONE genuinely disabled part when gated (`PlayTabView.xaml:488,494`; `MainWindow/MainWindow.PlayTab.cs:88-91`) | **Absent, not disabled** | Neither setting is ported, and unticking the 3D box selects the classic WPF descent, which the port does not have (`MainWindow.Lab.cs:233,242,255-264`). Rendering them would be dead dials — the §9 D7 rule |
| **D19** | The title carries a **NEW** pill (`PlayTabView.xaml:432`) | Absent | Nothing about this surface is new in the port, and a NEW pill that is always on is decoration that means nothing |
| **D20** | **The main window is tucked into the tray** the instant the hole opens, and restored from the tray when it closes (`Services/Chaos/DtrhHostService.cs:156` -> `MainWindow/MainWindow.RemoteControl.cs:1517` -> `Services/Notifications/TrayIconService.cs:145-148`; restore at `DtrhHostService.cs:998`) | **No tuck.** The shell is **plain-minimized** when the host window opens and restored to its prior state when the flow ends. **A user sees:** the CCP button stays in the taskbar the whole time (WPF's leaves it), there is **no tray icon**, **no tray menu**, and **no first-minimize balloon** | SP-093 landed the icon capability but no MENU, so a tuck built on it would hide the window behind an icon that does nothing on right-click — worse than WPF and worse than not tucking. A menu-only fix would still not be parity: WPF's tuck fires a balloon on its **first-ever** invocation (`TrayIconService.cs:152-157` — the comment at `RemoteControl.cs:1515` says "no notification" and the **code** says otherwise), and WPF's menu carries four items including the companion wake entry (`TrayIconService.cs:96-109`, §5). And every user-visible claim such a tuck would make is a **headed** claim this packet may not make. So the port reuses its own landed shape for this exact situation — `Features/Intake/IntakeHostWindow.axaml.cs:120-162`, "Plain MainWindow minimize (explicitly NOT tray tuck)" with prior-state restore — and `ITrayPresence` stays unwired. **Closes when `ITrayPresence` grows a menu surface and a headed gate proves the tuck** — **SUPERSEDED at §12 D35.** SP-096 built the menu, wired the icon, and refused the tuck again for a cause this row did not know about: Avalonia's `Window.Hide()` hides the windows OWNED by the hidden one, and the descent is owned by the shell. The half of this row that survives is the balloon (now §12 D38) and the menu contents (now §12 D36); the close condition here is retired |
| **D21** | **WPF has no third answer.** With a null Patreon service `RequiresLab` evaluates `App.Patreon?.HasLabAccess == true` to false and refuses (`Services/TierGate.cs:88-94`) — "I could not tell" is rendered to the user as "you are not a patron" | The port refuses too, but with a **different message** that says entitlement could not be verified, names which part could not be told, and says in words that nothing was decided about the account | **This is a deliberate IMPROVEMENT on WPF, recorded as such rather than smuggled in as parity.** It is also not an edge case: this build ships `UnconfiguredTierSource`, so `Unavailable(tier-authority-absent)` is the **only** branch a real user reaches until an owner permission decision lands. If it rendered as WPF's refusal, the port would tell every user they had stopped paying |
| **D22** | — (port-internal) | `--dtrh-demo` reaches `DtrhLaunch.Coordinator` **directly**, stepping past the gate | Not an oversight. Gating the headed-evidence path would make DTRH evidence depend on the developer's Patreon tier, and with no authority configured a gated `--dtrh-demo` would refuse everywhere and capture nothing. The **user** path is gated; the evidence path is one `--dtrh-demo` flag away from being unreachable, and the reason is written at the call site so it is not "fixed" later |
| **D23** | WPF's scrim carries a **glyph plus ONE short, no-wrap, ellipsis-trimmed line**, and its own comment says why: *"The pitch is the toast the click raises, which carries the 'See tiers' button - not this"* (`PlayTabView.xaml:270-273`). The prose lives on the toast, an opaque surface with its own ground | The prose lives on the band, on **its own opaque plate** inside it. The scrim keeps WPF's `#A8120A1E` (~66%) and still shows the badge, the title's edge, the blurb and both buttons around the plate | **FOUND BY A HEADED CAPTURE, AS A REAL DEFECT.** The first implementation put the three-sentence message straight onto the scrim, reasoning only about hit-testing. It composited through onto the card's own title and blurb, and the refusal — whose whole value is that it says something honest and specific — was hard to read. The two obvious fixes are both wrong: an opaque scrim buys legibility by destroying the quality WPF's alpha exists for (*"so the card art still reads through it. Seeing what you are missing is the entire job"*, `:247-248`), and shortening the message trades away the sentences that make `Unavailable` honest rather than a euphemism. The plate is the layer WPF already has, in the only place the port can put it. `PlayPageHeadlessTests` pins both alphas — scrim `A8`, plate `FF` — so neither half can be "fixed" into the other |
| **D24** | **WPF grants on TWO conditions, not one.** `TierGate.RequiresLab` is `App.Patreon?.HasLabAccess == true \|\| App.DailyFree?.IsFreeToday(dailyKey) == true` (`Services/TierGate.cs:90-91`), and **both** DTRH call sites pass the KEYED overload — `TierGate.DemandLab("Down the Rabbit Hole", "dtrh")` (`MainWindow/MainWindow.Lab.cs:228,313`). The source says what the key buys, two lines above: *"Keyed: on a server-declared DtRH drop day (DailyFreeService, off-pool override) the door opens for everyone"* (`:225-227`). §3 of this document said the same thing in its own words — *"Allowed with Lab access **or** when the server names `dtrh` as today's free feature"* (`:105-107`) | **The tier term only.** `Features/Dtrh/DtrhGate.cs` grants on tier 2 and nothing else. **What a user sees differently: on a drop day, a free user who would fall in on WPF is refused by the port** — and refused with the tier message, which on that day is not merely a gap but actively wrong, since the feature really is free that day | The port has no `DailyFreeService`, no `/config/daily-feature` fetch, and no server of any kind. DTRH reaches that list **only** through a server override — the local rotation never lands on tier-2 content (`Services/DailyFreeService.cs:16-18`; `IsFreeToday` at `:143-144`; `TodayKey` at `:133-140`). So there is nothing to bind to, and inventing a second grant condition out of nothing would be a worse answer than a recorded gap: a locally-decided "free today" would hand out tier-2 content on a date this port picked for itself. **Closes when the port has a server-supplied daily-free key**, at which point the gate ORs it in exactly where WPF does — before the tier comparison, with the same `"dtrh"` key. **How it stayed invisible for a whole submission, recorded because the mechanism matters more than the miss:** the gate's own comments cited `TierGate.cs:88-94` — the range that *contains* the second term — while describing only `HasLabAccess`, and quoted the keyed call while describing unkeyed semantics. Same failure mode as the §8.5 title: a partial reading written down as if it were the whole. The comments now name both terms and point here |

**Not closed by this section, and named so it is not mistaken for done:** the port's shell now
reaches four destinations. Graded Intake, the AvatarTube demonstrator and the Chaos tunnel backdrop
are still reachable only by a CLI flag. Nothing here is presentation-verified: the gate, the band and
the route are headless (draw-level) facts, and the minimize/restore, the DTRH host window itself and
every composited pixel remain undischarged headed claims.

> **Closed at SP-095, in part.** Of the three, exactly ONE was a user surface in WPF. Graded Intake
> now has a door (D25). The Chaos tunnel and the AvatarTube demonstrator were investigated and
> **refused** one, with the evidence in D30 and D31 — so the sentence above is no longer the honest
> summary and §11 replaces it.

---

## 11. Port divergences, recorded at SP-095 (the doors that were command-line only)

Three subsystems were reachable only by typing a CLI flag: Graded Intake (`--intake-demo`), the
Chaos tunnel backdrop (`--tunnel-demo`) and the AvatarTube demonstrator (`--avatartube-demo`).
**A door is owed only where WPF has a user surface**, so each was investigated against the shipping
source before anything was built, and only one earned one. Adding three doors because three flags
exist would have dressed harness scaffolding as a feature — the same decoration §9 D1 refuses.

The route that landed: **`Graded Intake` door -> the Graded Intake page -> `Begin Intake` -> the
weekly-pass gate -> the one `IntakeLaunchCoordinator`**, from a cold start with no command-line
arguments.

**D26 was overturned in review and is the most important row here.** The door first landed
**ungated**, which handed every user the patron privilege — unlimited retakes of paid content. The
reasoning behind that mistake, and the source that settles it, are written out in full in the row
itself rather than quietly corrected, because the failure mode generalises: the wording of a
refusal and the direction of a gate are separate questions, and getting the first right does not
excuse getting the second wrong.

| # | v6.8.1 fact | Port at SP-095 | Reason |
|---|---|---|---|
| **D25** | Graded Intake is a rail **SUB-entry**, not a top-level door: `BtnNavGradedIntake` (`MainWindow/MainWindow.xaml:811-812`, tooltip `tab_gradedintake` = "Graded Intake", `en.json:802`) sits inside the **Play** door's entry list (`MainWindow/MainWindow.TabNavigation.cs:600-601`), and its handler is a bare `ShowTab("gradedintake")` (`:947`) | A **top-level rail door** labelled "Graded Intake", route id `intake`, placed **after Play** | The port's rail has no sub-entries at all — it is one flat list of doors (§9 D2's shape). WPF's two-level rail is not ported, so an entry that WPF nests under Play can only be expressed here as a door or not at all, and "not at all" is what left this surface CLI-only for four waves. The position preserves the one structural fact that survives flattening: it belongs **with** Play. **What a user sees differently:** WPF users open the Play door and find Graded Intake listed inside it; port users see it as its own rail entry. **Closes if the port ever grows rail sub-entries**, at which point this becomes one |
| **D26** | The launch is **pass-gated**, and unlimited retakes are the PATRON PRIVILEGE. `BtnStartIntake_Click` refuses when `App.IntakePass.CanStartIntake` is false (`MainWindow/MainWindow.Lab.cs:119-146`; `CanStartIntake = Premium \|\| Available`, `Services/Progression/IntakePassService.cs:140-146`). The source says what is being sold: `Premium` is *"Patron. The pass system does not apply - **unlimited runs, no week, no door**"* (`:13`), and the class header is *"The intake is a **premium Exclusive** … free accounts get **ONE run per week** … while **retakes stay a reason to subscribe**"* (`:26-29`). The page paints a matching curtain (`Views/Tabs/GradedIntakeTabView.xaml:191-192`; `MainWindow.Lab.cs:368-434`) | **Gated.** `Features/Intake/IntakePassGate.cs` decides, `IntakeLaunch` asks it on every press, and on this build every user is **refused** — with a message that says the port could not DETERMINE their pass, never that they used their run | **THIS ROW REPLACES A WRONG ONE, AND THE MECHANISM MATTERS MORE THAN THE MISS.** SP-095 first shipped this door **ungated**, reasoning that a local refusal would be a claim about the install that nobody could lift. That reasoning was sound about the WORDING and wrong about the DIRECTION: an ungated door hands every user the patron privilege — an **over-grant**, the same class as the `(EntitlementTier)0` hole SP-094 closed at `DtrhGate`, and the opposite of §10 D24, which errs toward refusing something WPF would allow. It was also **this packet that made it live**: while the intake was `--intake-demo` only it was unreachable, so ungated cost nothing; the door is what turns an unreachable ungated surface into a reachable one. **And the honest refusal was already in the source.** WPF gives a free, signed-out user `NeedsLogin` — *"The pass is per-account, so there is nothing to hand out yet"* (`:15`, branch at `:115`) — and **not** `Spent`. The port has no account of any kind, so it cannot determine the pass at all: that is SP-092's third answer, `Unavailable`, rendered the way SP-094 renders it — refusing out loud, in its own words, never wearing the refusal's clothes (§10 D21). **What a user sees:** "This build could not determine your Graded Intake pass… That is a gap in the port, not a finding about your access: nothing was decided about you." **Closes with the same owner permission decision that gates DTRH** — an entitlement authority. At that point `Premium` and a READ `Available` proceed, and the gate is already written to do exactly that |
| **D27** | A **second** refusal on the same handler: `App.Ai == null \|\| !App.Ai.IsAvailable` -> "Login Required" MessageBox (`MainWindow/MainWindow.Lab.cs:148-153`), because "the intake's server AI accent uses the same Patreon-bearer gate" (`:105-106`) | Absent | The port has no `App.Ai` on this path and SP-054 landed the intake as a flow that runs without one. A hardcoded "AI unavailable, refused" would make the door dead on **every** machine — the fake-refusal shape, and worse than D26 because nothing would ever satisfy it. **Closes with the AI-availability capability** |
| **D28** | A **first-ever** run deliberately does not duck the shell: `Launch(duckMainWindow: !firstEver)` (`MainWindow.Lab.cs:155-159`), because minimizing for a first-timer "reads as 'the app just crashed'" | The port ducks **unconditionally** (`Features/Intake/IntakeHostWindow.axaml.cs:120-131`) | Pre-existing at SP-054 and recorded here because the door makes it reachable by gesture for the first time. The port has no "has ever completed an intake" signal wired to the duck decision — `IntakePunchCard` holds the fact, the launch path does not read it. **What a user sees differently: a port user's very first intake minimizes the shell; WPF's does not.** **Closes by reading the punch card at the launch site**; it is a one-line fix behind a landed store, and it is listed rather than done because this packet's subject is the door |
| **D29** | The page also carries a **BETA pill** (`GradedIntakeTabView.xaml:64`), a **weekly-pass banner** (`:104-124`), hidden **classic-quiz** controls (`:131-147`) and a **past-runs list** (`:166-175`) | All absent | Same rule as §9 D7 / §10 D18: the port has no pass to announce, no classic quiz, and no persisted past-run index to list, so rendering any of them would be a dead dial. The one that would be *cheap* to fake is the banner, which is exactly why it is named here |
| **D30** | **The Chaos tunnel is a BACKDROP, not a destination — in WPF too.** It is "the endless three.js 'rabbit hole' tunnel rendered UNDER the whole Chaos game" (`Chaos/ChaosTunnelService.cs:20`), a non-topmost fullscreen window carrying `WS_EX_NOACTIVATE\|WS_EX_TOOLWINDOW` so it cannot take focus and never appears in Alt-Tab (`:31-32`), gated on `ChaosTunnelEnabled` and **default OFF** (`:34,:58`). Every caller is the classic descent's own service — `Services/Chaos/ChaosModeService.cs:345` (preload under the countdown), `:518` (show at run start), `:3042`, `:3246`. Its user-facing control is a **checkbox**, `ChkTunnel` (`Chaos/ChaosHubWindow.xaml.cs:1566` read, `:1667` write), on the Chaos **setup lobby** — the Warren — which is a PRE-run screen: all three of its construction sites return early when a descent is already running (`MainWindow/MainWindow.Lab.cs:242` then `:262-264`; `Chaos/ChaosOverlayWindow.xaml.cs:873` then `:880`; `Services/Chaos/DtrhHostService.cs:879` then `:886`) | **NO door, and none is owed.** `--tunnel-demo` remains the only way to render it | **WPF has no tunnel entry point either**, so a rail door here would be a port invention with no counterpart — SP-091's trap applied to a route with a destination nobody in WPF can navigate to. What the port is actually missing is the **Chaos RUN** — and the lobby that configures it — for the backdrop to sit under, which is a feature row, not a door. A setting on a setup screen is not a destination either way. **`Features/Chaos/ChaosTunnelDemoDrive.cs:12-13` used to say "the greenfield dashboard has no Chaos game entry point — typed named limit", which framed this as a port gap awaiting a door; that comment is CORRECTED by this packet rather than satisfied.** **Closes — as a backdrop, still never as a door — when a Chaos run lands and the tunnel is shown under it** |
| **D31** | **The AvatarTube is never opened by a dashboard gesture.** The companion window's primary appearance is not a gesture at all: it is created during main-window startup whenever `AvatarEnabled` is true (`MainWindow/MainWindow.xaml.cs:2912` -> `InitializeAvatarTube`, `MainWindow/MainWindow.Companion.cs:145`), and that defaults true. The explicit in-app control is the **Companion page** hero card's eye toggle (`MainWindow/MainWindow.CompanionRoom.cs:82` `SetAvatarEnabled`), and the third entry is the tray's "Wake Bambi Up!" (§5) | **NO door.** The reachability question is answered by the existing **Companion** door | Two independent reasons, and either alone is sufficient. (1) There is no WPF gesture for a door to reproduce: WPF's own two entries are a startup setting and a control on the Companion page, and the port already carries the second behind its Companion door (§9 D11, first half closed). (2) `Features/AvatarTube/AvatarTubeDemonstratorWindow.axaml.cs:11-22` **declares itself a DEMONSTRATOR** — "really-functioning, superseded by the first real AvatarTube feature, owner may async-veto" — with demonstrator-valued constants and Mode/Talk/Pause/Pack/Attach harness controls. Wiring a rail door to it would dress harness scaffolding as a feature, which is the one thing this packet was told not to do. **What genuinely remains open, and is NOT a door problem:** the port's companion window renders no avatar at all (the animation engine is landed but mounted on nothing a user reaches), and the startup-appearance half of §9 D11 is still divergent. **Both close with the first real AvatarTube feature**, on the Companion page where WPF puts the control |
| **D32** | — (port-internal) | `--intake-demo` reaches `IntakeLaunch.Coordinator` **directly**, stepping past the pass gate | The §10 D22 decision, applied to a second surface and for a sharper reason. This build has no intake entitlement authority at all, so a gated `--intake-demo` would refuse on **every** machine and capture nothing; and once an authority exists, the pass is spent by a **completed run** (`IntakeHostWindow.axaml.cs:546`), so evidence capture would depend on whether the developer happened to run an intake earlier in the same ISO week. It is still the SAME coordinator the button drives — one construction site, two callers. The **user** path is gated, and the reason is written at the call site so it is not "fixed" later |
| **D33** | A thrown pass evaluation **fails closed to `Spent`** (`Services/Progression/IntakePassService.cs:123-130`) and the page then paints the spent copy (`MainWindow.Lab.cs:414-427`) — so a user whose settings hiccuped is told their week is gone | Refuses just as hard, but as **`RefusedUndeterminable`** with the could-not-determine message | **A deliberate improvement, recorded rather than smuggled in as parity.** The over-grant stays closed — nothing opens either way — but "the check threw" and "you have had your run" are different facts, and WPF's own comment for that branch says the choice was about not wedging the page, not about accuracy. This is the §10 D21 rule applied to a second surface: "I could not tell" must never be rendered as a finding about the person |
| **D34** | The intake gate is a **CURTAIN**: `IsHitTestVisible="True"` on the scrim with `GradedIntakeGatedContent.IsEnabled = open` behind it (`Views/Tabs/GradedIntakeTabView.xaml:191-192`; `MainWindow.Lab.cs:398-399`) — the opposite of the DTRH band, which passes clicks through | The port's band is **hit-test transparent** and nothing behind it is disabled | WPF can afford a curtain because it paints the gate **at navigation**, from a state it already knows, so the button under it was never pressable. The port's context — and therefore its pass — is prepared on the first press, so its band necessarily appears **after** a press (§10 D16's reason, second surface). A band that then swallowed the next press would trap the user on a refusal that can expire at Monday 00:00 local, with no way to re-ask. So the press keeps arriving and the gate keeps being asked, which is also what makes the refusal honest rather than sticky |

**What SP-095 does NOT prove.** Every fact above is **draw-level** (`verification-harness.md`): visual
tree, arranged bounds, real headless input routing, hit-test flags, style-resolved brushes, and the
strings on the page — plus the gate itself, which is a pure function proved in the unit suite.
Nothing here is presentation-verified. Specifically undischarged and belonging to a headed capture:
that the intake host window ever presents, that a run boots, that the second press really
*refocuses* a live host window (only "both presses reach one coordinator" is proved headlessly),
that the shell's duck and restore behave on a desktop, and every composited pixel of the new door,
the new page and the refusal band.

**Still reachable only by a CLI flag after this section, and by design:** the Chaos tunnel backdrop
(D30) and the AvatarTube demonstrator (D31). That is now a **recorded reason**, not a gap — which is
the state this document exists to distinguish.

---

## 12. Port divergences, recorded at SP-096 (the tray menu, and the tuck that stayed refused)

SP-093 landed the tray **icon** with no menu. SP-094 then refused to tuck the shell into it, because
an icon with no right-click menu strands the user, and recorded that at §10 **D20** with a close
condition: *"Closes when `ITrayPresence` grows a menu surface and a headed gate proves the tuck."*
SP-096 built the menu — and then **refused the tuck anyway, for a completely different reason, one
that was measured rather than reasoned about.**

**The measurement, first, because everything below rests on it.** In Avalonia 12.1.1
`Window.Hide()` on an owner **also hides every window owned by it**, and the owner's later `Show()`
does **not** bring them back; while the owner is hidden, `Show(owner)` throws
`InvalidOperationException: Cannot show window with non-visible owner.`. Minimizing propagates to
nothing. Pinned, permanently, by
`ShellTrayHeadlessTests.AvaloniaHidesOwnedWindowsWithTheirOwner_WhichIsWhyTheShellIsNeverHidden` —
which exists so a future reader who thinks the plain minimize is timidity runs into the fact instead
of "fixing" it, and so an Avalonia release that changes the behaviour reds a test in the *re-open
this deliberately* direction.

| # | v6.8.1 fact | Port at SP-096 | Reason |
|---|---|---|---|
| **D35** | **The main window is HIDDEN into the tray** when the hole opens and shown again when it closes (`Services/Chaos/DtrhHostService.cs:156` -> `MainWindow/MainWindow.RemoteControl.cs:1517` -> `Services/Notifications/TrayIconService.cs:145-148`; restore at `DtrhHostService.cs:998`). WPF survives that while the descent window is OWNED by the main window, because Win32 does not propagate a hide to owned windows | **Still no hide. The shell is plain-minimized — and now a shell-confirmed tray icon carrying the full menu goes up for exactly the interval WPF's icon is up**, and comes down on restore | **This row replaces D20's stated cause with a better-founded one.** D20's cause (no menu) is discharged: the menu exists and the OS itself is the oracle for it. What remains is the measurement above. The port owns the descent window exactly as WPF does — `Features/Dtrh/DtrhLaunchCoordinator.cs:167`, `window.Show(_owner)` — and **WPF says why that ownership is load-bearing, at the construction site**: `OwnedByMainWindow = true`, *"Glue the descent above MainWindow via native ownership: main gets raised by plenty of things we don't control (avatar barks, a video window closing, a tray restore) and used to land on top of the game. Ownership makes the window manager keep the pair in order — without Topmost, which would cover other apps too."* (`DtrhHostService.cs:129-132`). In Avalonia the two properties are **mutually exclusive at 12.1.1**: keep the descent owned, or hide the shell. Hiding would make the game window vanish, would break the SP-027 watchdog's one permitted relaunch (`DtrhLaunchCoordinator.cs:113` -> `:167` throws on a hidden owner), and would take an open companion window down with no re-show. Keeping the descent visible and above the shell is the load-bearing outcome; "the shell's taskbar button disappears" is not. **What a user sees differently: WPF's CCP taskbar button disappears during a descent and the port's does not.** Everything else WPF's tray gives, the port now gives too — three ways back where WPF has two. **The alternative was considered and is an OWNER call, not this packet's:** unowning the descent window would buy the literal tuck at the price of the defect WPF's own comment describes. **Closes if Avalonia stops propagating hide to owned windows, or if an owner-preserving hide lands** — at which point the pinned test above reds and says so |
| **D36** | The tray menu is **four entries**: `Show Dashboard`, a wake entry, a separator, `Exit` (`Services/Notifications/TrayIconService.cs:96-110`). The wake label is `App.Mods?.IsBambiMode == true ? "Wake Bambi Up!" : "Wake Up!"` (`:102`), and §5 records that entry as one of the three ways a user reaches the companion — the only one that works while the main window is out of the way | **The same four entries, in the same order, with the same strings**, wired to the port's own verbs: restore the shell, open the companion window (`MainWindow.ShowCompanion`), separator, shut the application down through the one guarded teardown (`App.axaml.cs:88-95`) | The wake label is **`Wake Up!` permanently**, because the port has no mod system and therefore lives permanently in WPF's `false` branch. That is WPF's own string for the state the port is in, not a paraphrase of the other one — recorded so nobody later "restores" a bambi label the port has no way to earn. WPF's Exit preamble (lockdown check, engine stop, `KillAllAudio`, overlay dispose, `SaveSettings`) has no port counterpart: none of those subsystems exists, and the settings flush is the first thing `ApplicationHost.ShutdownAsync` does anyway |
| **D37** | Restoring from the tray sets `WindowState = WindowState.Normal` **unconditionally** (`TrayIconService.cs:172`), so a maximized dashboard comes back un-maximized after a descent | The prior state is recorded at the duck and restored: **a maximized shell comes back maximized** | A deliberate improvement, recorded rather than smuggled in as parity. It is also the port's own landed shape for this exact situation, twice over (`Features/Intake/IntakeHostWindow.axaml.cs:153-160`, and SP-094's `DuckOwner`/`RestoreOwner` which this packet absorbs), so matching WPF would have meant making two landed surfaces disagree in order to reproduce a behaviour that reads as a bug |
| **D38** | WPF's tuck fires a balloon on its **first-ever** invocation in the process — `ShowBalloonTip(2000, "Conditioning Control Panel", "Running in background. Click the tray icon to restore.", Info)`, latched by `_hasShownFirstMinimizeNotification` (`TrayIconService.cs:23,150-157`) — even though the DTRH tuck's own XML comment says *"No notification (the game window is the focus)"* (`MainWindow/MainWindow.RemoteControl.cs:1515`) | **The code, not the comment.** The first duck of the process asks for that balloon, with that title, that text and that 2000 ms timeout; every later duck asks for none | The comment describes an intent the call does not carry out, and it is the call a user experiences. One deliberate refinement: the port consumes the once-ever latch only when a balloon is really **asked for**, which requires a placed icon. WPF always has an icon by that point so the case never arises for it; here it does, and burning the once-ever balloon on a duck where the tray refused would mean a user whose first descent happened under a wedged Explorer never sees it at all |
| **D39** | — (port-internal) | WPF hides the window and then shows the icon (`TrayIconService.cs:145-146`); the port **minimizes first and places the icon second** | Not cosmetic. WPF's second step is `Visible = true` on an already-constructed `NotifyIcon` and effectively cannot fail; the port's is a real `Shell_NotifyIcon` round trip a session can refuse. Doing the window first is safe here only *because* it is a minimize — the taskbar button survives it — which makes the icon a strict addition that may fail without consequence. This ordering and the no-hide rule are one decision seen from two sides |

**What SP-096 proves, and the line it stops at.** The menu is verified by asking **USER32** what it
holds — `GetMenuItemCount` / `GetMenuItemID` / `GetMenuString` in the product, and an independent
second set including `GetMenuItemInfo`'s `MFT_SEPARATOR` in the test probe: the same
two-independent-code-paths discipline SP-093 used for `NIM_MODIFY`. The right-click route is driven
by posting **the shell's own notification** to the **real owner window** and pumping it, so the real
window proc runs; `TrackPopupMenu` sits behind a seam whose product default is the real call, and
the seam is handed the real OS-held `HMENU` and answers with the id the OS itself reports.

**Undischarged and belonging to a headed capture:** that the icon is visible to a human, that a real
click or right-click lands on it, `TrackPopupMenu`'s modal loop and the menu it draws, whether a
balloon ever appears (Windows suppresses notifications under Focus Assist, quiet hours, a
full-screen app and the per-app switch, and reports none of it back), and that the shell's minimize
and restore behave on a real desktop. **Linux is unchanged and still refused**, with
`TrayPresenceFactory.LinuxManualGate` naming the exact three-part gate that would settle it — and
the branch it produces is now covered by a test: minimize, no icon, no balloon, taskbar button kept,
and a typed reason code in the diagnostic line.

### D40 — `Duck()` places no icon when the shell is already minimized; WPF's `MinimizeToTray()` always does

**Recorded at the wave-39 land 2026-08-18, found by the SP-096 final review and NOT by the lane.**

WPF's `MinimizeToTray()` (`Services/Notifications/TrayIconService.cs:145-158`) has no already-minimized guard: it hides, places the icon and fires the first-minimize balloon unconditionally. The port's `ShellTray.Duck()` early-returns when the shell is already minimized, so in that state **WPF gives the user an icon, a menu and a balloon where the port gives none** — and WPF's flow-end `ShowWindow()` un-minimizes where the port's `Restore()` no-ops, leaving the shell minimized after a descent WPF would have restored from.

**Reachability was checked before this was weighed, and it is currently zero:** every port launch gesture requires an interactive visible shell, and the SP-027 relaunch path is caught one branch earlier by `_ducked`. So no user route reaches it today.

**Why it is recorded anyway.** The guard is inherited from SP-094's plain-minimize shape rather than invented by SP-096, it is the kind of branch a later packet makes reachable without noticing (any non-gesture launch — a scheduler, a resume, a remote trigger — walks straight into it), and an unrecorded divergence is indistinguishable from a bug. **Close condition: when any launch path can start a descent while the shell is minimized, either drop the guard to match WPF or record why the port keeps it.**


## 13. Port divergences, recorded at SP-097 (the failure a user should see)

**Numbering note.** The packet said "record divergences from D40 onward". **D40 was already taken** —
recorded at the wave-39 land by the SP-096 final review — so this section starts at **D41**.

**What was wrong.** WPF wraps its DTRH and Graded Intake handlers whole and, when anything throws,
logs it and shows a blocking `MessageBox` with `MessageBoxImage.Warning` reading *"Couldn't start
Down the Rabbit Hole:"* / *"Couldn't start Graded Intake:"* plus `ex.Message`
(`MainWindow/MainWindow.Lab.cs:161-166`, `:266-271`, `:333-338`). The port caught only around
`ResolveAsync`. `PlayPage` fired `_ = dtrh.FallInAsync()`, so a throw from the descent became an
**unobserved task exception** — raked up by the panic hook at some later GC (`Program.cs:313`) and
never shown to anybody. `IntakePage` was worse in a way the packet brief did not state: its click
handler called `intake.Launch()` **synchronously**, so a throw escaped into Avalonia's dispatcher
rather than into a discarded task. Same user-visible defect, different process outcome; the fix
covers both, and both launchers now wrap the whole flow the way WPF's handlers do.

| # | v6.8.1 fact | Port at SP-097 | Reason |
|---|---|---|---|
| **D41** | The failure surface is a **modal `MessageBox`** with a warning icon, dismissed by the user, after which the card is live again (`MainWindow.Lab.cs:164-165`, `:269-270`) | An **in-page fault plate** in the SP-094/SP-095 idiom - a **different element** (`FaultBand`, not the refusal `GateBand`/`PassGate`), in a **different livery** (amber rim `#FFF0A02E` and warm plate `#FF241505`, never the tier-2 violet `#FFB47BFF` or the intake's `#FFD05CE8`), under WPF's **own headline**, and hit-test transparent so the retry still arrives. The two bands are **mutually exclusive**: raising one lowers the other | The port has no dialog service, so a modal here would be a new window class whose every user-visible property (modality, activation, focus return, z-order) is **presentation**-class evidence a headless frame cannot discharge. It also already has an honest idiom for saying something out loud on this exact card. **The differentiation is the load-bearing part, not the container:** the refusal band already means *"we could not determine your entitlement"*, and a failure that rendered identically would teach a user that a broken app and an unknown subscription are one event. WPF never faces that question because its failure is a different WINDOW; the port reuses the surface, so it carries the difference in element, colour, headline and words at once - five axes, so no single later tidy-up collapses them. `PlayPageHeadlessTests.AFailureLooksNothingLikeARefusal_AndTheTwoAreNeverUpTogether` pins all five |
| **D42** | The dialog body is `ex.Message` alone; the exception **type** goes only to the log (`App.Logger?.Error(ex, ...)`, `:163`, `:268`) | The user reads the **type AND the message**; the diagnostic carries **both** as well | Strictly WPF's disclosure plus the type - the type is what survives a paraphrase in a bug report, and the trap this packet was authored against is a catch that renders a band and drops the detail. **One path deliberately keeps the old rule:** a throw from `HostLoginEntitlement.ResolveAsync` is still converted to `Unavailable(tier-authority-fault)` with the exception **type only** and still renders as a **refusal**, not a fault. The question asked there was *what is this account entitled to*, and "could not be determined" is the honest answer; the message on that one path can carry a path or a bearer. `PlayPageHeadlessTests.WhenResolvingTheEntitlementTHROWS_ItStaysARefusal_AndLandsTheTierAuthorityFaultFallback` pins both halves, including that the reader's secret-shaped message never reaches the log |
| **D43** | The body puts a **blank line** between the headline and the message (`"...:\n\n" + ex.Message`) | A **single** newline | **Measured, and it is not about taste.** In Avalonia 12.1.1 a wrapped `TextBlock` inside these plates whose text contains an **empty line** wedges the layout pass: the press that raised the band never returns, so the app (and the test suite) hangs rather than fails. It reproduces with a two-character body (`"A:\n\nB"`), with and without `LineHeight`, and - the part that makes it a port-wide fact - **identically in the existing refusal plate**, so it is a property of the surface and not of this feature. Single newlines are what the landed refusal copy already uses and are proven safe. `LaunchFaultTextTests.NoStringAPlateCanRENDER_ContainsABlankLine_AndTheSetIsDERIVED_NeverHandListed` holds the line in the unit suite, because the failure mode it prevents is a hang and a hang is the one failure a test suite reports worst. **Its set is DERIVED, after review caught the first version being the very defect it guards against:** it hand-listed eight strings, omitted `IntakePassGate.SpentMessageFormat` (rendered on six days in seven) and sampled `UnverifiedMessage` at one of eleven reason codes, while claiming to cover every plate string. It now reflects every public string member of `DtrhGate`, `IntakePassGate` and `LaunchFaultText`, drives both gates Decide over every reason code, tier and (state, reason) pair, and sweeps the composed fault bodies - ~150 strings, with the derivation itself pinned by name and by count floors so an over-narrow filter fails loudly. Band TITLES are deliberately NOT claimed: they are NoWrap TextBlocks and the wedge was measured on a wrapped one. **Closes when an Avalonia release lays out an empty line in a wrapped `TextBlock` without wedging** - re-measure with that two-character body before restoring WPF's spacing |
| **D44** | The `MessageBox` **grows to fit** whatever `ex.Message` contains, and is then dismissed | The message is flattened to one line and **clamped to 400 characters**, with a marker saying it was cut | The plate is a fixed panel inside a card that stays on screen, not a window that sizes itself and goes away. An unclamped multi-line message would push its own tail - and the *"this is a fault, not a decision"* sentence under it - out of the plate, which is the §10 D23 illegibility defect arriving by a second route: the words would be right and unreadable. The clamp keeps the **type at the front**, so it can never be what the clamp removes, and says out loud that the text was truncated rather than letting a sentence appear to end mid-word |

**What SP-097 proves, and where it stops.** Draw-level only: that the fault plate exists, is visible,
carries the type and the message, is a different element in a different livery from the refusal
band, never appears beside it, and never swallows the next press. **Undischarged and belonging to a
headed capture:** that the amber really reads as "something broke" to a human, that the plate
composites legibly over the card at real scaling, and every other composited-pixel claim. Nothing
here is a `presentation-verified` claim.

**Four never-executed paths closed by the same packet**, each with a fact that fails if it
regresses: `MainWindow.RequestApplicationExit`'s no-lifetime branch (the tray menu's `Exit`, the one
entry whose effect nothing had ever run - asserted through the real menu item, with the lifetime
precondition asserted FIRST so the entry can never shut the test runner down); `DtrhLaunch`'s
`catch` -> `Unavailable(tier-authority-fault)` fallback (reached through a read seam that throws,
the only input that makes the sealed capability throw); `IntakePage`'s `RefusedSpent` and
`RefusedNeedsAccount` render arms (driven through the page with injected entitlement seams and the
real pass service's own completion-spend, never a stubbed decision); and `DtrhLaunch.RestoreOwner`
(the real coordinator's `FlowEnded`, raised by really cancelling the real slot picker).

---

## 14. Port divergences, recorded at SP-098 (the session, and the first effect that runs)

**Numbering note.** §13 ended at **D44**, so this section starts at **D45**.

**What landed.** The port had a shell and no app: five doors, a tray, honest refusals, and zero
effects. It now has a **conditioning session** — WPF's ENGINE, not its scripted-session layer — with
**one real effect under it**, Flash Images, reachable by a real gesture from a cold start:
`DoorStudio` -> rack row **Flash Images** -> its module panel, and the shell's own action bar ->
**START**.

**Two names, one word.** WPF has two things called "session" and the port models only one of them.
The **engine** is `MainWindow/MainWindow.StartStop.cs:34,159,296` and `App.IsEngineRunning`
(`:269,:387`): `START` arms every enabled module and stop disarms them. The **scripted session** is
`Services/Session/SessionEngine.cs` + `SessionManager.cs` — a definition with phases, a duration and
XP — and it is **not ported** (D51).

### D5 and D6 — what closed, and what did not

| gap | at SP-091 | at SP-098 |
|---|---|---|
| **D5** (live dot) | no dot on any row, "the port has no spiral-overlay effect whose state could be reported" | **CLOSED for the `Flash Images` row**, which has a real effect and therefore a dot that can report truthfully (three states — D45). **STILL OPEN for `Spiral Overlay`**, which still has no ported effect; WPF's own rule for such a row is to omit the dot (`StudioTabView.xaml.cs:494-496`), and a dot that always read "off" would be the fake-available shape the capability contract bans |
| **D6** (right-click toggle) | the gesture is unhandled on every row | **CLOSED for the `Flash Images` row**: right-click flips the persisted dial and starts/stops the live effect, WPF's own body (`MainWindow/MainWindow.Presets.cs:1250`, `:1264`). **STILL OPEN for `Spiral Overlay`**, deliberately unhandled — WPF's own case for a toggle-less row (`StudioTabView.xaml.cs:659`) |

Both close for a row when an effect lands behind that row, and for no other reason.
`StudioRackHeadlessTests` holds both halves: the flash row has the dot and the toggle, the spiral row
has neither, in the same run.

### The divergences

| # | v6.8.1 fact | Port at SP-098 | Reason |
|---|---|---|---|
| **D45** | The rack dot reads the **persisted enable flag** — `Add("flash", …, () => App.Settings?.Current?.FlashEnabled)` (`Views/Tabs/StudioTabView.xaml.cs:484-485`) — while the Studio onboarding card promises *"The dot on each row is live: at a glance you can see everything that is currently running"* (§8.3) | A **three-state** dot: `Off` (the module's dial is off), `Armed` (dial on, no session owns it), `Live` (an owned operation really has work scheduled) | **WPF's copy and WPF's code disagree, and the source wins (the §8 rule) — but a two-state dot has to be wrong about one of them.** WPF's two readings coincide only while the engine runs, because `StartEngine` gates each service on its flag (`MainWindow.StartStop.cs:181,200,…`) and the quick-toggle starts/stops the live service (`Presets.cs:1250`); with the engine stopped, WPF's dot is lit for something that is not running. The port says both things instead of picking one and asserting the other. `Live` derives from the OPERATION authority (`AsyncOperationOwner.IsLive` plus a real pending timer), never from a cached bool — the `StatusTickerParticipant.IsOperationLive` precedent — so a cancelled generation reads not-live even if nobody repainted the row |
| **D46** | Switching a module ON mid-session **does not arm it** if it was off when the engine started: `FlashService.Start()` returns at `if (_isRunning) return;` (`Services/Flash/FlashService.cs:347`) while `_isRunning` was already set by the unconditional `App.Flash.Start()` at `MainWindow.StartStop.cs:178`, so the quick-toggle (`Presets.cs:1250`) and the panel checkbox (`Features/FlashFeatureControl.xaml.cs:168-175`) both silently do nothing | Arming is **idempotent about the generation** (a second arm starts no second generation — the re-entrant double-toggle guard) but **always re-evaluates the schedule**, so a module switched on during a session runs | This is the one place the port declines to reproduce the code, and it is deliberate: the rack's own onboarding text tells the user *"Right-click it to flip that effect on or off"*, and a gesture that is documented, offered and inert is worse than one that is absent. WPF's own path even self-heals by accident — nudging the frequency slider calls `RefreshSchedule` (`FlashService.cs:527-531`), which arms what the toggle would not — so the observable intent is not in doubt. Recorded rather than filed as a WPF bug, because it is upstream's tree, not this packet's |
| **D47** | A flash **puts images on screen above every other application**: one layered, always-on-top, `WS_EX_TRANSPARENT` click-through window per flash, re-asserted to `HWND_TOPMOST` as other layers fight it (`FlashService.cs:3615`, `:3667-3668`, `:3862-3868`, `:206-240`) | **Nothing is drawn.** The schedule, the dials, the pool and the draw are ported exactly; the on-screen half is **absent and named on the module panel in plain words** | **That half is a compositor, and `docs/constitution.md` classes the previous port attempt as failure evidence largely because of overlay work.** It is a platform capability with its own packet and its own headed evidence, not something to smuggle in behind a first effect. The port refuses the two dishonest alternatives as well: it does not draw the flash inside its own window (a different, lesser outcome dressed as the real one), and it does not let the module imply pixels appeared. What the user gets instead is truthful: the dot lights, the panel counts the flashes as they come due and says how many images each drew, and a notice says the drawing half is not ported. **Nothing in this packet is a `presentation-verified` claim, and nothing in it proves a flash was ever visible** |
| **D48** | The persistent bottom action bar carries a favourite toggle, a large gradient **START**, a start-options chevron, **Save** and **Exit** (§8.6); `START` additionally refuses while a remote controller is connected (`MainWindow.StartStop.cs:40`) or while lockdown is active (`:44-49`) | A shell action bar with **START alone**, never disabled, painted WPF's pink `#FFD05CE8` idle and WPF's red `#FFFF6B6B` running (`:756`, `:779`), captioned with WPF's literal `START`/`STOP` (`:762`, `:787`) minus the glyphs (the §9 D8 emoji rule) | §9 D10 recorded the whole bar as absent; this narrows it to the one verb with a ported meaning. Favourite, the chevron's "Jump right in" (`:127`), Save and Exit all name subsystems the port does not have, and rendering them would be the greyed control that swallows the gesture (§9 D7). There is no remote controller and no lockdown to refuse for. The button is **never disabled in either state**, which is WPF's shape and matters most in the stop direction: a stop the user cannot press is the failure a panic button exists to prevent |
| **D49** | The Flash module panel carries **a dozen dials** — enable, frequency, images, opacity, image scale, duration, fade, clickable, glow, solid mode, hydra limit and linked timing, centre exclusion (`Features/FlashFeatureControl.xaml`, `CCP.Core/Models/AppSettings.cs:749-960`) | **Three**: enable, flashes per hour (1-180), images per flash (1-20). The rest are **absent, not disabled** | Every absent dial describes how a flash is DRAWN, and nothing draws (D47). A persisted opacity nothing reads is the storage form of the greyed control, and a slider that moves a number nobody consumes is worse than no slider. The three that remain are the three the running effect really reads, and `StudioRackHeadlessTests` proves it by moving one and watching the next flash change |
| **D50** | Every dial lives in the one `settings.json` behind `App.Settings.Current`, under WPF's names (`FlashEnabled`, `FlashFrequency`, `SimultaneousImages`) | A **new document**, `session_preset.json`, with `FlashEnabled`, `FlashesPerHour`, `ImagesPerFlash`; WPF's clamps kept verbatim in the setters (`AppSettings.cs:769,835`) and WPF's defaults kept verbatim (`:751` true, `:763` 10, `:831` 5) | The `AssetSelectionDocument` precedent: a new document is additive by construction — no schema bump elsewhere, no absent-member case, and a `Missing` load is simply fresh defaults. It is deliberately not a member of `DemoSettings`, whose own doc says it is "not a feature model". Two members are renamed because WPF's names describe a mechanism the port does not have: `FlashFrequency` carries its unit in a comment only ("Flashes per hour", `:763`) and the schedule's whole law is `3600.0 / this`; `SimultaneousImages` describes windows on screen, of which there are none. Behaviour is byte-for-byte WPF's — the formula, the ±30 % band, the 3-second floor, the clamps, the with-replacement draw |
| **D51** | Pressing stop **during a scripted session** opens a confirmation dialog listing the session name, elapsed and remaining time and the XP that will be lost, and stop is refused outright during lockdown (`MainWindow.StartStop.cs:44-49,52-88`); the quick-toggle is likewise refused while a running session owns that dose (`Presets.cs:1242`) | **STOP always stops, immediately, with no dialog and no refusal**, and no quick-toggle is ever refused | The scripted-session layer (`Services/Session/SessionEngine.cs`, `SessionManager.cs`), XP, progression and lockdown are not ported, so there is no prescribed dose to defend and nothing to lose by stopping. The divergence is recorded because it is **user-visible and will change**: when the scripted session lands, stop acquires a confirmation and the toggle acquires a refusal, and that must be a deliberate addition rather than a surprise. Until then the safer default is the one where stop stops |

**What SP-098 proves, and where it stops.** Proven at unit level with an injected clock and no
wall-clock wait: the pacing law and its clamps, the dials and their persistence, the pool and the
active-pool seam, and — the load-bearing one — that stop really stops (the effect's owned generation
terminates `Cancelled`, no timer survives on the clock, no operation is left outstanding or
unobserved, and ten further clock windows produce nothing). Proven at draw level through the real
shell with real input: the cold-start route, START/STOP, the dot's three states, the right-click
toggle, the dials, and the panel's account of what happened. **Undischarged:** every
composited-pixel claim — the action bar's real placement, the dot's legibility at real scaling, the
button's colours as a human sees them — belongs to a headed capture. **And, said once more because
it is the shape of the packet: no flash has been shown on a screen, and nothing here should be read
as saying one was.**

## SP-099 — the overlay surface (the capability, wired to nothing)

D47 said the drawing half of a flash "is a compositor, and `docs/constitution.md` classes the
previous port attempt as failure evidence largely because of overlay work. It is a platform
capability with its own packet and its own headed evidence." This is that packet. It lands the
surface and **wires it to no effect at all** — nothing draws, and D47 stays open until a later
packet consumes this.

**What the first attempt did here, since it is the reason this packet exists.** Its overlay seam is
`void Show(); void Hide(); void SetClickThrough(bool); void SetBounds(PixelRect)`
(`ConditioningControlPanel/CCP.Core/Platform/IOverlaySurface.cs`) — not one member can report a
refusal, so an overlay that never appeared and an overlay that covered the screen were the same
call. Its cross-platform click-through is a method body containing only a comment
(`CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:31-35`). Its Windows override sets
`WS_EX_LAYERED` and never calls `SetLayeredWindowAttributes`
(`CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45`), which — measured on this machine
during SP-099 — produces a window the OS reports `IsWindowVisible = TRUE` for while
`GetLayeredWindowAttributes` returns FALSE: present, on top, and composited from nothing. Its Linux
surface is a documented "never-throw seam" where "overlay operations degrade to logged no-ops"
(`CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlaySurface.cs:9-78`) behind a selector "guaranteed
never to throw and never to return null" (`LinuxOverlayBackendSelector.cs:41-88`). And its
availability is `SupportsOverlays = IsDesktop;`
(`CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs:29-30`), with a single verification harness
that prints "Tell me what you saw" and ends `Environment.ExitCode = 0; // human-judged; never fail
the process` (`tests/CCP.Avalonia.Desktop.Windows.Smoke/VisibleOverlayVerification.cs`).

| # | v6.8.1 fact | Port at SP-099 | Reason |
|---|---|---|---|
| **D52** | A flash window is a layered always-on-top click-through window **with an `Image` in it** and an opacity animation on it (`Services/Flash/FlashService.cs:3611-3625`, and the whole spawn path around `:494`/`:688`) | The surface is **empty**: a uniform `LWA_ALPHA` tint, no content, no renderer, and **no effect, session, view or capability registry is wired to it**. `Present` says so in its own success string ("nothing is drawn on this surface"), and a test pins that sentence | Capability first, consumed later — the SP-093 pattern. Wiring it to Flash Images would entangle this packet's evidence with the effect's, and a headed gate that cannot be discharged here would then block a capability that is otherwise sound. **This narrows D47 rather than closing it**: the surface now exists and is proven from the OS; nothing has yet been drawn on it, and no flash has been shown on a screen |
| **D53** | Topmost is **contested and re-asserted on a cadence**: `RaiseAllToFront` re-raises every live flash window roughly once a second, driven by the chaos layer, "so an already-showing flash is never briefly buried under a re-raised bubble" (`FlashService.cs:206-243`) | The port re-asserts `HWND_TOPMOST` **on demand only** — inside `Present`, `SetClickThrough` and each hit-test query, bounded by a 32-iteration count with no wall-clock wait | There is no chaos layer, no bubble host and no second overlay to fight, so a background cadence would be a timer with no contender. The contention is real and was measured (the window that won the point under a click-through surface on this machine was the shipping WPF app, topmost and re-raising), which is why the re-assertion exists at all — just not on a clock. **Consequence to state plainly: sustained topmost over minutes of contention is NOT proven.** A later consumer that needs it adds the cadence and owns its own evidence |
| **D54** | Flash windows are **pooled and recycled**, and clickability is flipped per spawn on the live hwnd (`:3584-3607`, `:3654-3673`). The reason is specific: resizing a *realized* layered WPF window deadlocks the UI thread on `MediaContext.CompleteRender` (dump-confirmed, `:3576-3583`), so a size mismatch gets a fresh window sized before its first `Show()` | One window per presence, **not pooled**. The click-through flag is flipped on the same live hwnd exactly as WPF does it, and re-placement is a `SetWindowPos` | WPF's pooling exists to avoid a WPF render-thread hazard the port cannot have: there is no `MediaContext` in this path, and the window is never resized after realization — it is re-placed. Pooling without that hazard would be a cache with no defect to prevent. WPF's *symmetry* bug is not copied either: the first attempt's disable path dropped only `WS_EX_TRANSPARENT` and left `WS_EX_LAYERED` behind, so this re-asserts `WS_EX_LAYERED \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE` on every flip, which is what WPF's `:3666` does |
| **D55** | Monitor geometry is in **device-independent pixels** with a per-screen DPI scale, and the scale is carried on `MonitorInfo.DpiScale` because the conversion has to be undone later (`:2204-2245`, `:4130-4141`) | `OverlayBounds` is **physical pixels**, and `OverlayDisplays.Enumerate()` reports each display's physical rect, work area and primary flag with **no DPI at all** | A Win32 top-level window's coordinates are physical pixels and there is no WPF layout system above this surface, so there is nothing to convert. DPI becomes a real concern the moment content is drawn on the surface, and nothing is drawn (D52). Recorded because it **will** matter: the packet that draws must decide its own scaling, and inheriting WPF's DIP convention by accident would be worse than choosing it |
| **D56** | The overlay is a Windows mechanism and the shipping product is Windows-only, so the question never arises | **Linux gets no overlay**: `OverlayPresenceFactory.CreateFor(Linux)` returns a typed `Unavailable(overlay-mechanism-absent)` whose detail names the route (X11 override-redirect + `_NET_WM_STATE_ABOVE` + an empty XFixes `ShapeInput` region), names why Wayland is a refusal rather than a harder Linux (no protocol an ordinary client may use; `wlr-layer-shell` is wlroots-only and Mutter does not implement it; the pinned Avalonia 12.1.1 graph ships `Avalonia.X11` + `Avalonia.FreeDesktop` and no Wayland package), and carries the **exact four-step manual gate** that would settle it | `ISecretStore` and `ITrayPresence` set the precedent and the constitution sets the rule: a stub, a no-op fallback or a Windows-only test never proves cross-platform support. The refusal covers `Withdraw` and `SetClickThrough` as well as `Present`, and never reports `IsPresenting` — there is no path through it a caller can mistake for a surface on screen. This machine cannot discharge the gate: the port's Linux environment is WSLg, whose XWayland root has no `_NET_CLIENT_LIST` (`port-lessons.md:52`), so z-order cannot be trusted there at all. Board row: **BLOCKED**, not WIP |

**Citation correction (SP-099).** The SP-099 packet cited `Topmost = true` at `FlashService.cs:3612`,
`WS_EX_TRANSPARENT` at `:3666` and `SetWindowPos ... HWND_TOPMOST` at `:3862`. In the tree at this
SHA they are `:3615`, `:3667-3668` and `:3867` — a drift of about three lines. D47 above already
carried the correct trio, so the document was right and the packet's copy had drifted. The tree
wins; every SP-099 citation is against the tree.

**What SP-099 proves, and where it stops.** Proven from the operating system, in the pure-logic test
project, with an independent second copy of every P/Invoke and a negative control that re-runs on
every suite execution: the surface exists and is visible; the OS holds exactly the rectangle that was
asked for; the OS holds the requested non-zero `LWA_ALPHA`, so there is something for the compositor
to draw (and the instrument proves it can say "the OS holds none" by building the CCP ghost on
purpose and reading -1 back from it); the OS's own z-order walk puts the surface above every ordinary
window; the window manager's hit test routes the surface's centre **away** from it while
click-through is on and **to** it while click-through is off, both at the same point in the same run;
showing it never takes the foreground; withdrawing it really removes it from both the visible set and
the hit test; and disposing leaves no top-level window behind. **Undischarged, and named:** that
anything is drawn (nothing is); that a human sees the surface above another application's window —
DWM composition, exclusive-fullscreen and DirectX applications, Magnifier, RDP and mirror drivers can
all defeat it with every query above still answering yes, and `presentation-verified` is a headed
capture this packet does not claim; that a **real pointer** passes through, since `WindowFromPoint` is
the window manager's routing question asked, not delivered input (`SendInput` would move the user's
cursor mid-suite and fails silently on a locked workstation, so it is a headed gate); multi-monitor
and cross-DPI placement, because this machine reports one display; sustained topmost under contention
(D53); and every part of Linux (D56). **Said once, plainly: nothing has been drawn on this surface,
and nothing here should be read as saying a flash was shown.**

## SP-100 — the flash draws (D47 closes on Windows; the claim it closes with is named)

SP-098 landed the schedule and said "nothing is drawn". SP-099 landed the surface and said "wired to
nothing". This packet joins them: the effect hands the paths it drew to the surface, on the one UI
thread, and the operating system is asked what the surface holds afterwards. **D47 is closed on
Windows and stays open on Linux (D56).**

**The measurement that decided the content route**, taken on this machine before any of it was
written (SP-100 `record.md` §1). A `WS_POPUP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW |
WS_EX_NOACTIVATE` window at `LWA_ALPHA 255`, painted by `BitBlt` from a top-down 32bpp DIB into
`GetDC(hwnd)`: the composited desktop carries the painted pixel **with no wait at all**, above the
shipping WPF product; painting the window while it is HIDDEN is discarded (`GetPixel` returns
`CLR_INVALID`), so the order must be show-then-paint; a freshly shown, never-painted layered window
composites as what was underneath it, so show-then-paint has no black-frame artifact;
`PrintWindow(hwnd, dc, 0)` returns the painted content deterministically while
`PW_RENDERFULLCONTENT` returned an all-black bitmap on the first call after a show.

**And the measurement that made D57 a pin rather than an argument.** `UpdateLayeredWindow` on this
window, after `SetLayeredWindowAttributes`, fails with `ERROR_INVALID_PARAMETER` (87) — **until**
`WS_EX_LAYERED` is cleared and re-set, which is the sequence the API's own documentation prescribes;
after that it returns TRUE and `GetLayeredWindowAttributes` returns FALSE for as long as ULW owns the window (a later `SetLayeredWindowAttributes` restores it, measured at re-review) — which is exactly the state an alpha-ramp packet would be in. The ghost
check is therefore exactly two ordinary lines away from silence, and the style toggle on its own is
harmless (the OS still reports the alpha), so the hazard does not announce itself in halves. A fact
now asks the alpha on the far side of a paint, requires a full re-`Present` to still earn
`Available`, and requires the content to survive the re-placement; the mutation above reds all three.

| # | v6.8.1 fact | Port at SP-100 | Reason |
|---|---|---|---|
| **D57** | A flash window is a WPF `Window` with `AllowsTransparency = true` — per-pixel alpha through `UpdateLayeredWindow` — carrying an `Image` and an opacity animation (`Services/Flash/FlashService.cs:3611-3625`, `:1274-1281`) | Content is a **GDI `BitBlt` of a B,G,R,X frame into the raw `WS_POPUP` hwnd**, composited at the constant `LWA_ALPHA` SP-099 already sets and already asks the OS to confirm. No `UpdateLayeredWindow`, no per-pixel alpha, and **no Avalonia top-level** | `UpdateLayeredWindow` is mutually exclusive with `SetLayeredWindowAttributes`: taking it would stop `GetLayeredWindowAttributes` answering for the window, and that call is the **ghost check** — the one measured discriminator between a real surface and the defect the first attempt shipped (`CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45`). It would buy per-pixel alpha that WPF's flash does not use: upstream's window is a black-backed rectangle (`:1245`) filled edge to edge by an `Image` pinned to the window's own size (`:1274-1281`) at one uniform opacity. The Avalonia route was rejected for a bigger reason: it REPLACES the hwnd, and with it every SP-099 confirmation (z-order walk, both-polarity hit test, alpha read-back, foreground check are all written against a handle this backend owns), and it is the first attempt's own shape (`CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:14`) |
| **D58** | A flash shows **N windows at once**, each at its own random point, `SimultaneousImages` per firing up to 20 (`AppSettings.cs:832`), capped at `MAX_CONCURRENT_FLASH = 10` for the per-flash layered-window path (`FlashService.cs:50`, `:1174-1181`) | Identical: **one surface per image**, one random placement each, cap **10** | The port is on WPF's classic per-flash layered-window path, which is the path the 10 cap exists for. The dial still goes to 20 and the cap still wins, exactly as upstream |
| **D59** | Window opacity, image scale, flash duration, fade time, glow, solid mode, clickability, hydra and centre-exclusion are **dials** (`AppSettings.cs:749-960`) | Not ported as dials (SP-098 D49); the port uses **WPF's shipped DEFAULT for each** as a constant, cited at its line: opacity 100 % (`:853`), image scale 100 (`:838`), duration 5 s (`:926`), avoid-centre off (`:936`) | A dial nobody can move is worse than a constant nobody can move — it looks configurable and is not. The values are upstream's own defaults, so what a user sees is what an untouched shipping install shows them. Each becomes a real dial when the panel that moves it lands, and none of them changes behaviour today |
| **D60** | A flash window's lifetime is `FlashDuration * 1000 + 1000` ms and it then **fades out** (`FlashService.cs:1073`, `:1246`, the `IsFadingOut` animation path) | The lifetime is kept **exactly** — 6 s at the shipped default, driven on the injected session clock — and the surface then **leaves with no fade** | A fade is a per-frame animation over a layered window's alpha, and this packet deliberately builds no frame loop: SP-099's own residual says `Present` is wrong per frame, and an animation is the one thing a headless suite cannot verify and a headed capture cannot verify cheaply either. The disappearance is instant instead of over ~0.4 s. **Recorded because it is user-visible**, and because the fade is the natural first job of whatever packet adds an alpha ramp |
| **D61** | Flash windows are **clickable by default** (`FlashClickable = true`, `AppSettings.cs:772`): clicking one pops it, spawns hydra children and scores XP (`:3667`) | The surface is always **click-through** (`WS_EX_TRANSPARENT`, WPF's other arm at `:3668`) | Pop, hydra, gaze and XP are not ported. A surface that CATCHES clicks and does nothing with them would swallow the user's input over whatever it covers — the exact desktop-breaking failure `overlay-input-not-passing-through` exists to refuse. WPF's own click-through arm is what a user who turns clicking off gets today, so this is upstream's other configuration rather than a new behaviour |
| **D62** | Topmost is re-asserted about **once a second** by the chaos layer's `RaiseAllToFront` (`FlashService.cs:206-243`) | **Same cadence**, driven by the injected session clock, for exactly as long as a surface is up — and no timer at all when nothing is showing | This narrows SP-099's D53, which recorded on-demand-only re-assertion. The contender is not hypothetical: measured twice on this machine, the window that owned the point under the surface was the shipping WPF product, topmost. A six-second flash that loses the band after one second is a flash nobody sees. `IOverlayPresence.Reassert()` returns **nothing**, because it confirms nothing — it is one `SetWindowPos`, and a `CapabilityState` there would be a claim with no round-trip behind it |
| **D63** | Images decode through WPF's imaging stack (WIC), and decode failures are retried with fresh picks (`LoadImagesUntilAsync`) | Images decode through **GDI+ (`gdiplus.dll`)** at display size, over black. A path that cannot be decoded contributes **no surface**, and the flash still counts and still re-schedules. **WebP does not decode**, though it is in the pool's extension list | GDI+ is part of Windows, needs no package, and — the reason it was chosen — works in a process with **no Avalonia runtime**, which is what lets the entire draw path be proven in the pure-logic test project rather than only where a UI toolkit has been initialised. The WebP gap is real and named: it is a decoder swap (WIC, or Avalonia's Skia once a Skia-backed headless rig exists), not a design change. Composing over black is upstream's own composition — its flash window's background is `Brushes.Black` (`:1245`) — and it is what stops a transparent PNG showing the desktop through a shape nobody asked for on a surface with one uniform alpha |
| **D64** | Not applicable: WPF's own window is the content host, so there is no second read | `Paint` earns `Available` only after the OS is asked for the surface's content **back** and returns the frame at 1024 spread sample points including all four corners and the centre | Symmetric with every other claim in this capability. Measured consequence, stated rather than hidden: because the read-back travels through a DIB with the same header as the frame, a **consistent orientation error is invisible to it** — the mutation that flips `biHeight` passes the product's own confirmation and is caught only by the test's independent instruments (`PrintWindow` and the composited desktop). That is the argument for the second instrument, and it is why the test frame is two-tone |

**What SP-100 proves, and where it stops.** Proven from the operating system, in the pure-logic test
project, with instruments that share no declaration with the product: the surface's own device
context holds the painted frame; the OS's own `PrintWindow` rendering of that window is the frame,
every pixel of it, the right way up; **the composited desktop carries the frame at the surface's
rectangle**, both halves, with no wall-clock wait, mapped through the OS's own DPI ratio; withdrawing
takes it back off the composited desktop; and a real `.png` on disk, through the product's decoder
and presenter, reaches the composited desktop and leaves it when the flash is hidden. Proven with no
screen involved: `Present` is called once per surface per flash and never per frame, `IsPresenting`
is never consulted, the stagger is 300 ms, the lifetime is 6 s, the cadence is 1 s and stops with the
last surface, the cap is 10, and stop takes every surface off at once and cancels the ones that had
not appeared yet. **Undischarged, and named:** that a **human sees a flash** — a composited desktop
read from inside the process cannot see a Magnifier, a mirror driver, an exclusive-fullscreen swap
chain, colour management or a monitor that is physically off, and `presentation-verified` remains the
orchestrator's headed capture; multi-monitor and cross-DPI placement, since this machine reports one
display and the port places on the primary only; sustained topmost over minutes of real contention;
the fade that no longer happens (D60); WebP (D63); and **every part of Linux**, where the overlay
refuses by design and the flash runs, counts and stops with nothing on screen.

**One consequence outside this packet's File Scope, reported not fixed.**
`client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml:152` still tells the user *"Showing the
images over your other windows is not ported yet: that needs an always-on-top click-through surface
this build does not have. The schedule above is real and runs - it just has nowhere to draw."* On
Windows that sentence is now **false**, and on Linux it is still exactly right. `Views/**` is outside
SP-100's File Scope, so the text is untouched here; the follow-up is one string that has to say both
halves — drawn where the overlay is available, absent and named where it refuses. **Closed at SP-101,
below.**

## SP-101 — the second effect, and what the first one's template cost

SP-098 built a session spine and one effect; SP-100 made it draw. Thirteen more rack modules follow
that shape and **none had ever been copied**. This packet built the second — **Subliminals**, WPF's
third EFFECTS row (`Views/Tabs/StudioTabView.xaml.cs:488-489`) — for the sake of finding out whether
the shape was a template or an accident. The verdict, the shared machinery and the three defects the
second module surfaced are in `spine-tasks/SP-101-second-effect/record.md`; what belongs here is what
a user of the shipping app would notice.

**Subliminals is not a near-copy of Flash Images, and that is why it was the right second.** Five
things differ, and each is a place a copied implementation would have been silently wrong: the dial
counts per **minute** and the floor is **one second**, against per hour and three
(`Services/Subliminal/SubliminalService.cs:172-187` vs `Services/Flash/FlashService.cs:538-563`); the
module ships **off** where flash ships **on** (`CCP.Core/Models/AppSettings.cs:1234` vs `:751`), which
is why `StartEngine` calls the flash service unconditionally (`MainWindow/MainWindow.StartStop.cs:178`)
and reaches this one only behind its flag (`:186-187`); an empty pool **counts nothing** here and
counts a flash there (`SubliminalService.cs:207-212` returns before the counter at `:611-612`); the
pool is words in settings, not files in a folder; and the card is one full-screen surface for a fifth
of a second, not a burst of placed rectangles for six.

| # | v6.8.1 fact | Port at SP-101 | Reason |
|---|---|---|---|
| **D65** | A subliminal's card **fades**: 50 ms in, hold, 50 ms out, as a storyboard over the window's opacity (`SubliminalService.cs:1253-1281`) | The card appears at its target opacity, stays for **exactly WPF's whole envelope** (`50 + max(100, frames×17) + 50` ms) and leaves. **No ramp at either end** | The same decision SP-100 recorded for the flash fade (D60), taken again because the reason is unchanged: an alpha ramp is a per-frame animation over a layered window, `Present` is not a frame path (SP-099's own residual), and this packet builds no frame loop. The DURATION — the part a user's attention actually measures — is kept exact; what is lost is ~100 ms of ramp on a 200 ms card |
| **D66** | Subliminal cards go to **every monitor** when `DualMonitorEnabled` (default **true**, `AppSettings.cs:1917`) — one keep-alive window per screen (`SubliminalService.cs:629-631`) | **Primary display only**, one card | Not a subliminal decision: `OverlayDisplays.Enumerate()` reports one display on this machine and the port places on the primary for flashes too (SP-100's own undischarged list). It closes for both modules at once, in whatever packet takes multi-monitor placement |
| **D67** | The card's colours, transparency, solid mode, focus-steal and the compositor layer are **dials** (`AppSettings.cs:1326-1378`) | WPF's shipped **defaults as constants**: background `#000000` opaque (`:1326`, and `:1333` — `SubBackgroundTransparent` ships false), text magenta `#FF00FF` (`:1340`), outline white `#FFFFFF` (`:1354`), Arial Bold 120 px (`SubliminalService.cs:1237-1248`), the eight outline offsets verbatim (`:992-996`). Solid mode, focus-steal and the compositor layer are **not ported at all** | The SP-098 D49 rule: a dial nobody can move is worse than a constant nobody can move. Solid mode and the compositor host exist upstream to relieve a **WPF render-thread hazard** (`#461`, named in the service's own comments) that a raw Win32 surface does not have, and focus-steal is an anti-feature on a click-through card. What a user sees is what an untouched shipping install shows them |
| **D68** | A subliminal can carry **linked whisper audio** with ducking (`SubliminalService.cs:216-240`), a **haptic** anticipation pattern (`:577-600`), **XP** (`:243`, `:255`), a "Bambi Freeze" → "Bambi Reset" follow-up (`:276-404`), and remote-control / Deeper one-shots (`:258-275`) | **None of it.** The module schedules, draws and stops | Each is a subsystem this port does not have (audio device routing, haptics, progression, remote control). Recorded rather than stubbed: a silent no-op would make the module look complete. The pacing, the pool, the card and the stop are the whole of what is claimed |
| **D69** | `SubliminalDuration` is in **frames**, converted with `Math.Max(100, value × 17)` ms (`SubliminalService.cs:615-617`) | **Identical, including the oddity**: the shipped default of 2 frames yields 34 ms, the floor wins, and the dial does nothing at all until it passes 6 | The unit is strange and the floor makes most of the range inert, but a user's persisted number has to keep meaning what it meant. Normalising it to milliseconds would silently re-time every existing install |
| **D70** | WPF merges newly shipped default phrases back into the pool on launch, minus a `RemovedDefaultSubliminals` set that exists so a phrase the user deleted cannot resurrect (`AppSettings.cs:1292-1302`, `#892`) | The user's pool **replaces** the shipped one outright; no merge, no removed-set | The merge is a settings-migration feature, not part of the effect, and half of it (a resurrection guard for a merge that does not happen) would be dead weight. The shipped 21 phrases are the default for a pool that has never been written |
| **D71** | One `AppSettings` holds every module's dials | Subliminals persists to its **own document**, `session_subliminal.json`, beside `session_preset.json` | Half procedural, half substantive, and both halves are stated. `Persistence/**` was outside SP-101's File Scope, so the shared session preset was not edited. It was also the better shape: fifteen modules editing one document is a chokepoint, and the store's Degraded path takes the WHOLE document to defaults — so a hand-broken phrase list would today reset the user's flash frequency too. One file per module quarantines that. **Fold it into `session_preset.json` if the owner prefers one file; nothing behavioural depends on which way it goes** |
| **D72** — **CLOSED at SP-105** | The Studio rack has a Subliminals row with a dot and a right-click toggle | The port's rack **now has one**, with the full grammar: left-click opens its panel, right-click quick-toggles the module through the same `SessionEngine.QuickToggle` entry every other row uses, and the dot reports the module's own three-state `Dot` | Closed by the packet that also needed a rack row of its own, so the two landed together and the rack's ORDER could be settled once (`StudioTabView.xaml.cs:483-493`, minus the unported rows) instead of twice. The original entry, kept for the record: `Views/**` was open in SP-101 for exactly one reason — the false sentence above — and adding a rack row was not it |

**D47's Studio sentence is closed.** `StudioPage.axaml:152` no longer says the drawing half is not
ported. The line is derived from the surface presenter's own last `CapabilityState`, verbatim — so it
names the mechanism before anything has been attempted, reports a real placement when the OS confirms
one, and repeats the **backend's own refusal**, reason code and manual gate included, on a build where
the overlay is absent. It asserts nothing about the platform, which is how the previous sentence came
to be false the day SP-100 landed.

**What SP-101 proves, and where it stops.** Proven with no screen involved: two modules arm, pace,
count, draw and stop under one engine, one clock and one operation registry, with two independent
generations and two terminal outcomes; the second module's dial period, floor, default, pool rule and
counting rule are each its own; a card reaches the surface full-screen, at the module's opacity,
click-through, present-before-paint, withdrawn if the paint fails, for exactly WPF's envelope, and
replaced rather than stacked by the next one; stop takes it off at once and leaves no timer. Proven on
Windows only, in the pure-logic project: the GDI+ text raster produces an opaque card carrying WPF's
magenta phrase over its white outline — on Linux the same fact asserts that it rasters nothing and
throws nothing. **Undischarged, and named:** that a human sees a subliminal — no headed capture is
taken here and `presentation-verified` remains the orchestrator's; the fade (D65); multi-monitor
(D66); every part of Linux, where the overlay refuses by design and the module runs, counts and stops
with nothing on screen; and the rack row (D72, **closed at SP-105**), which until then meant only a
test or a persisted file could switch this module on.

---

## SP-105 — a continuous effect, and the rack rows that switch modules on

Two effects ran under the session spine and **both were timed**. This packet built the third
deliberately from the other kind: **Pink Filter**, WPF's fifth EFFECTS row
(`Views/Tabs/StudioTabView.xaml.cs:493-494`), which upstream drives with **no timer at all** — the
whole mechanism is `s.PinkFilterEnabled = !s.PinkFilterEnabled; App.Overlay?.RefreshOverlays();`
(`MainWindow/MainWindow.Presets.cs:1255`), with no `Start`, no `Stop`, no tick, and not even the
`if (running)` the three paced rows above it carry (`:1250-1252`). The template verdict is in
`spine-tasks/SP-105-continuous-effect/record.md`; what belongs here is what a user would notice.

**What a user gets that they did not have before.** Three rack rows now carry the full grammar
instead of one — Flash Images, Subliminals (D72, closed) and Pink Filter — so every ported module can
be switched on from the UI. A session with the tint on shows it from the instant START is pressed
until STOP, with no interval anywhere in it.

**Where the port and v6.8.1 differ for this module:**

| # | v6.8.1 fact | Port at SP-105 | Reason |
|---|---|---|---|
| **D73** | The tint is **one window per resolved screen** (`OverlayService.cs:1149-1157`), with a per-effect monitor target that falls back to `DualMonitorEnabled` (`AppSettings.cs:1949`) | **Primary display only**, one surface | Not a Pink Filter decision: the same D66 single-display limit both other modules already carry. `OverlayDisplays.Enumerate()` reports one display on this machine, and multi-monitor placement closes for all three modules at once |
| **D74** | The tint can be **held by something other than the user**: timed holds, sustained holds and the Deeper opacity ramp let autonomy, remote control and the enhancement engine own it for a while and refuse the reconciler's teardown (`OverlayService.cs:900-965`, `:2963-2965`); `PulseOverlays` doubles every overlay's intensity for ~1 s (`:461-500`) | **None of it.** The module's dial is the only thing that turns the tint on or off | Every holder is a subsystem this port does not have (autonomy, remote control, the Deeper engine). Recorded rather than stubbed: a hold nothing can take would look implemented and never fire, and the hold bookkeeping without holders is dead weight the next reader would have to disprove |
| **D75** | After **3 s of sustained topmost loss** the overlay windows are destroyed and rebuilt, up to a capped number of attempts, then it falls back to forcing z-order (`OverlayService.cs:633-663`, `:2597-2622`) | **Not ported.** The surface re-asserts its band on the cadence and never rebuilds itself | The recreate path exists upstream to escape a WPF-specific freeze cluster its own comments name (`#431`/`#451`/`#780`), on layered WPF `Window`s. This surface is a raw Win32 top-level window with no WPF render thread behind it, so the failure it recovers from has no counterpart here. Porting a recovery for a fault this build cannot have would be mechanism, not outcome |
| **D76** | The band is reclaimed **two ways**: a conditional pass every 500 ms that re-asserts only the windows that actually lost it (`OverlayService.cs:633`, `:2450-2500`), and an unconditional kick every 5 s (`:666-673`) | **The 5 s unconditional kick only** | `IOverlayPresence.Reassert()` deliberately confirms nothing and the capability exposes **no z-order query** to condition on — that is SP-099's own design, because a `SetWindowPos` that returns is not evidence of a band. There is therefore no honest way to implement "re-assert only if it was lost", and porting the 500 ms pass unconditionally would be ten times upstream's `SetWindowPos` traffic to claim the same outcome. **Closes if the overlay capability ever grows an earned z-order read** |
| **D77** | The tint colour is `user hex -> the active MOD's filter colour -> hot pink` (`OverlayService.cs:682-686`) | **`user hex -> hot pink`**, both ends exact | The port has no mod system to ask. The middle term is not silently dropped: with no mods installed, upstream's chain produces the same answer this one does, so an untouched install is identical. **Closes with the mod system** |
| **D78** | `PinkFilterOpacity` clamps to `[0, 50]` (`AppSettings.cs:3737`), and at **0** WPF still creates a full-screen layered window holding alpha 0 — a window the OS agrees exists, is visible and is on top, that composites nothing | The dial keeps WPF's range, and at 0 the port **places nothing**. The arm result is `Degraded(pink-filter-transparent)` and the row's dot reads **Armed**, with the panel saying "the opacity is at 0%, so there is nothing to draw" | The port's overlay refuses to construct an invisible surface by design — that ghost is the exact failure `OverlayReasonCodes.OverlayNotComposited` was written to catch, measured on the first port attempt. **What the user sees is identical** (nothing); what differs is that the port does not leave an invisible always-on-top window over the desktop, and that it can say why the module is doing nothing |
| **D79** | The rendered alpha is `(byte)(opacity / 100.0 * 255)` — a **truncation** (`OverlayService.cs:1180-1181`) | The overlay capability rounds to nearest and floors at 1 (`Overlay/OverlaySurfaceRequest.cs`) | At most **one step of 255** different, and it is not this module's choice: the rounding belongs to the overlay capability, which is outside this packet's File Scope and is shared by all three drawing modules. Recorded rather than worked around locally, because a per-module correction would put two alpha laws in the port |
| **D80** | Every module's dials live in one `AppSettings` | Pink Filter persists to its **own document**, `session_pinkfilter.json` | The D71 shape, applied a second time and for the same two reasons — `Persistence/**` is outside this packet's File Scope, and one document per module keeps a corrupt file from taking every other module's dials to defaults. The owner's call to fold them into one file is still open and nothing behavioural depends on it |
| **D81** | The Pink Filter panel carries a **colour picker with a reset**, a ramp-link checkbox and a display-monitor dropdown (`Features/PinkFilterFeatureControl.xaml`) | An Enable toggle, an opacity slider, and a **read-only swatch** naming the tint in force | The §9 D7 rule: a dial nobody can move is worse than a constant nobody can move, and a greyed control swallows the gesture and says nothing. The persisted colour IS honoured — a hand-edited `#RRGGBB` is read, parsed and drawn — so the swatch reports something real rather than standing in for a control |
| **D82** | The Subliminals panel's frequency slider writes and saves; the Flash panel's **also** re-paces the live schedule (`Features/SubliminalFeatureControl.xaml.cs:89-98` vs `Features/FlashFeatureControl.xaml.cs:177-188`) | **The same asymmetry, kept.** The port's subliminal frequency slider does not re-pace; its flash slider does; its pink opacity slider re-tints immediately (`Features/PinkFilterFeatureControl.xaml.cs:99-109`) | It looks like an inconsistency and it is upstream's, so it is kept: a user who has learned that moving one slider takes effect now and another at the next firing is entitled to that timing. Recorded so the next reader does not "fix" it |

**What SP-105 proves, and where it stops.** Proven with no screen involved: a module with no clock
arms, runs, re-tints, refuses in type and stops under the same engine, the same operation registry and
the same stop as two modules that fire on one; its dot is `Live` only while its surface is confirmed
up, and reads `Armed` when the overlay refuses, when the opacity is zero, and when no UI thread is
bound; the tint is placed full-screen, click-through, present-before-paint, with **no lifetime timer
at all**, and holds the topmost band on WPF's own 5 s cadence; and the two landed modules' facts pass
unchanged. Proven on the real controls, headless: the rack is in WPF's order, three rows carry a dot
and a working right-click toggle, the Spiral Overlay row still carries neither, and each new row opens
its own panel showing the module's real persisted dials.
**Undischarged, and named:** that a human sees a pink tint — no headed capture is taken here and
`presentation-verified` remains the orchestrator's; every part of Linux, where the overlay refuses by
design and this module therefore arms, reports its refusal verbatim and shows nothing; multi-monitor
(D73); and every holder, pulse and recovery path in D74/D75/D76.
