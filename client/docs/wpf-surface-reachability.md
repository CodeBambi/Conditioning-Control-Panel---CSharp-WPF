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
