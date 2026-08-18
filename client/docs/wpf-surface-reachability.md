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
| **D4** | The Studio rack is **four groups, fifteen rows** (§8.3) | **One group (EFFECTS), one row (Spiral Overlay)** | The other fourteen modules are not ported. A rack of rows that open blank panels is the trap at row granularity |
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
| **D20** | **The main window is tucked into the tray** the instant the hole opens, and restored from the tray when it closes (`Services/Chaos/DtrhHostService.cs:156` -> `MainWindow/MainWindow.RemoteControl.cs:1517` -> `Services/Notifications/TrayIconService.cs:145-148`; restore at `DtrhHostService.cs:998`) | **No tuck.** The shell is **plain-minimized** when the host window opens and restored to its prior state when the flow ends. **A user sees:** the CCP button stays in the taskbar the whole time (WPF's leaves it), there is **no tray icon**, **no tray menu**, and **no first-minimize balloon** | SP-093 landed the icon capability but no MENU, so a tuck built on it would hide the window behind an icon that does nothing on right-click — worse than WPF and worse than not tucking. A menu-only fix would still not be parity: WPF's tuck fires a balloon on its **first-ever** invocation (`TrayIconService.cs:152-157` — the comment at `RemoteControl.cs:1515` says "no notification" and the **code** says otherwise), and WPF's menu carries four items including the companion wake entry (`TrayIconService.cs:96-109`, §5). And every user-visible claim such a tuck would make is a **headed** claim this packet may not make. So the port reuses its own landed shape for this exact situation — `Features/Intake/IntakeHostWindow.axaml.cs:120-162`, "Plain MainWindow minimize (explicitly NOT tray tuck)" with prior-state restore — and `ITrayPresence` stays unwired. **Closes when `ITrayPresence` grows a menu surface and a headed gate proves the tuck** |
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
