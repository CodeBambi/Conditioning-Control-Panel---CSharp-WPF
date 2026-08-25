# Linux headed evidence: what is reached, and the exact gate on everything else

`client/port.txt` requires the precise unavailable-platform gate whenever evidence cannot be
obtained: a named mechanism and why, never "not supported yet". This file is that record for the
named checks in `client/tools/verify/checks.json`, and it is machine-checked —
`LinuxEvidenceGateTests` reads both this file and the manifest on every floor run, so a check added
to the manifest reds the floor until it is classified here, and a gate whose reason is a placeholder
reds it too.

## The environment, named exactly

Every reading below was taken on:

- **WSL2 on Windows 11**, Ubuntu 26.04 LTS, kernel `6.6.87.2-microsoft-standard-WSL2`.
- **.NET SDK 10.0.400**, building a **native ext4 clone at `~/ccp`**, never the `/mnt/c` checkout —
  a Linux build over `/mnt/c` overwrites the same `bin/Debug/net10.0` assemblies
  `check-floor.mjs`'s freshness assertion measures, and the two platforms would lie to each other.
- **X11 through XWayland**, `DISPLAY=:0`, hosted by WSLg's nested Weston. **There is no desktop
  environment behind it — not GNOME, not KDE**, and no reading here may be reported as either.
- **Avalonia 12.1.1 with no Wayland backend at all.** `avalonia.x11` is the only Linux windowing
  package in the resolved graph, so "separate X11 and Wayland" is answered rather than outstanding:
  no Wayland reading is obtainable from this product.
- **Software presentation**, forced by `CCP_X11_SOFTWARE=1`, because Avalonia presenting through GL
  leaves the window contents in a GPU surface `XGetImage` cannot read. **The GPU-presented frame a
  real user sees is still unphotographed on Linux.**
- **Scale 1.75**, matching the Windows leg, so the capture sizes and pixel counts below are directly
  comparable rather than merely similar.

## The element route

`capture.ps1` finds every surface it photographs through **UIA**: an `AutomationId` lookup, a screen
`BoundingRectangle`, and a pattern read that confirms the state before a pixel is taken. Linux has
no UIA. That single absence — not presentation, not input, not focus, all three of which the
2026-08-24 run already proved — is why the Linux leg reached three checks and no more.

The route is **AT-SPI over D-Bus**, and it needed **no new package**, exactly as XTEST did not:

- Avalonia 12.1.1 ships a complete AT-SPI server. `Avalonia.FreeDesktop.AtSpi` is a separate NuGet
  package that resolves into the app's own output directory, and `Avalonia.X11` wires it up in
  `X11AtSpiAccessibility`.
- `at-spi2-core` 2.60.0 and `python3-dbus` 1.4.0 are already installed in this image, and
  `org.a11y.Bus` is **D-Bus activated**, so the accessibility bus stands itself up on first contact.

**There is no accessibility opt-in in 12.1.1, and that was measured in both directions rather than
read off the API surface.** The obvious expectation is that Avalonia waits for the desktop's own
switch, because `X11AtSpiAccessibility` does subscribe to `org.a11y.Status` and does have an
`OnAccessibilityEnabledChanged`. Measured 2026-08-25: with `org.gnome.desktop.interface
toolkit-accessibility` forced **false** and the harness setting nothing, the tree is still published
and every query answers; with **no session bus at all**, nothing is published and the route refuses
by name. So the one precondition is a session bus carrying `org.a11y.Bus`. The harness therefore
*reads* that bus's address rather than *writing* `org.a11y.Status.IsEnabled`, which is dconf-backed
and would have made a persistent change to the user's own desktop settings as a side effect of
taking a screenshot.

### The one place the Linux route is not a translation of the Windows one

**AT-SPI does not carry the `AutomationId`.** Measured on the running shell: every Avalonia element
publishes the attributes `{toolkit: Avalonia, explicit-name: true}` and nothing else, with no `id`
among them. What it does carry is the accessible **name**, which Avalonia fills from
`AutomationProperties.Name` and otherwise falls back to the control's text.

Three consequences, all of them load-bearing below:

1. A control that carries an `AutomationId` and no `Name` is **not addressable** from Linux.
2. Two controls that carry the **same** `Name` are indistinguishable here even though UIA tells them
   apart by id. `atspi.py` refuses such a selector rather than resolving it to the first match.
3. A role qualifier (`@slider`, `@push-button`) is a legitimate narrowing where a caption
   `TextBlock` happens to carry the same words as its control's `Name` — which is the shape, not an
   accident: `client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml:1849-1855` gives the master volume slider the name
   `Master volume` and puts a caption reading `Master volume` beside it.

## Reached: 10 of 45

`REACHED-probe` means the rectangle came from the app's own layout probe or is the whole window
(banked 2026-08-24). `REACHED-atspi` means it came from the element route added 2026-08-25.

Every reached check below **scores 0.000 on a real capture of the opposite state**, except the two
noted as deliberately same-livery pairs, which invert against each other's partner instead — that is
the manifest's own design and is recorded at `client/tools/verify/checks.json:404-417`.

| Check | Route | Linux reading | Windows reading |
|---|---|---|---|
| `rail-door-unselected-border` | REACHED-probe | 892/918 = 0.972. | 896/918 = 0.976. |
| `rail-door-selected-border` | REACHED-probe | 884/918 = 0.963. | 888/918 = 0.967. |
| `dashboard-background` | REACHED-probe | 150181/154000 = 0.975. | 151178/154000 = 0.982. |
| `rack-row-unselected-ground` | REACHED-atspi | 7605/7605 = 1.000. | 7605/7605 exact. |
| `rack-row-selected-marker` | REACHED-atspi | 161/189 = 0.852. | not separately recorded. |
| `rack-row-selected-fill` | REACHED-atspi | 7575/7605 = 0.996. | 7605/7605 exact. |
| `rack-row-dot-armed` | REACHED-atspi | 136/196 = 0.694. | 0.714. |
| `rack-row-dot-off` | REACHED-atspi | 36/36 = 1.000. | 36/36 exact. |
| `studio-dial-live-track` | REACHED-atspi | 208/936 = 0.222. | 208/936 = 0.2222. |
| `audio-dial-live-track` | REACHED-atspi | 208/936 = 0.222. | 208/936 = 0.2222. |

The two dials are the strongest comparison this port has: the same control at the same value, the
same capture size of 653x88, and the **same 208 track pixels of 936** on two operating systems whose
rendering stacks share no code.

## Gated: 7 of 45, each with the mechanism that stops it

A `GATED` row names a mechanism this lane could not remove from inside `client/`. The vocabulary is
closed and `LinuxEvidenceGateTests` enforces it, so no row can degrade into "not supported yet".

- **`ambiguous-accessible-name`** — the drive must tell two SIMULTANEOUSLY VISIBLE controls apart
  and both publish the same accessible name, which AT-SPI cannot disambiguate because it carries no
  `AutomationId`. Measured 2026-08-25: after the real click on the panel's Start Session button, the
  accessible tree holds **two** `push button` elements both named `Start Session` — the
  confirmation's own at `518,456 123x39` and the panel's at `505,518 220x45`. `capture.ps1` tells
  them apart as `ScriptedSessionConfirmButton` and `ScriptedSessionStartButton`
  (`client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml:2308-2313` and
  `client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml:2343-2349`). Every check below needs a real
  scripted session, there is no flag for one and deliberately so, and the confirmation is the second
  of the four gestures that start one — so the gate binds before any pixel is taken.

  **THE COLLISION IS FIXED IN PRODUCT SOURCE, AND THESE FIVE ROWS STILL SAY GATED. Both halves of
  that sentence are deliberate.** On 2026-08-25 the confirmation's accessible name became
  `Start Session (confirm)` while its caption stayed upstream's `Start Session`
  (`client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml.cs:2099-2113`), pinned by
  `SessionRackHeadlessTests.TheTwoStartButtonsThatAreOnScreenTogether_DoNotAnswerToTheSameACCESSIBLENAME`
  and re-verified headed on Windows — all five pairs below captured and every named check green
  (`studio-dial/locked`, `audio-dial/running`, `session-start/running`, `session-history/kept`,
  `session-history/not-kept`). What that changes is the MECHANISM; what it does not change is the
  EVIDENCE. No Linux run has been taken since, so nothing here has photographed those five checks on
  X11, and a row moved to REACHED on the strength of a Windows result would be exactly the
  unmeasured claim this record exists to refuse. The next Linux headed lane can expect the drive to
  get past the confirmation; until it has, these rows stay where they are.
- **`no-webkit-on-this-image`** — `Avalonia.Controls.WebView` 12.0.1 ships a GTK adapter
  (`Avalonia.Controls.Gtk.GtkWebViewAdapter`) that binds `libWPEWebKit-2.0.so.1` /
  `libwebkit2gtk`, and this image has **none of them**: `ldconfig -p` finds no
  `libwebkit2gtk-4.1.so.0`, no `libwebkitgtk-6.0.so.4` and no `libWPEWebKit-2.0.so.1`, and `dpkg -l`
  lists zero webkit packages. GTK3 itself is present, so this is specifically the web engine and not
  the toolkit. It is a property of the machine image rather than of the port: an `apt install` would
  change the answer, and this lane deliberately installed nothing, because every other Linux
  capability here was established WITHOUT one and mixing the two claims would make the set
  unreadable.

| Check | Gate | Why |
|---|---|---|
| `studio-dial-locked-track` | GATED: ambiguous-accessible-name | Needs a session running; the confirmation cannot be pressed. |
| `audio-dial-running-track` | GATED: ambiguous-accessible-name | Same drive, same second gesture. |
| `session-start-running-fill` | GATED: ambiguous-accessible-name | The running caption only exists after the confirmation. |
| `session-history-kept-row-fill` | GATED: ambiguous-accessible-name | Start AND stop both go through that confirmation. |
| `session-history-not-kept-plate` | GATED: ambiguous-accessible-name | Same, and its partner. |
| `goon-page-backdrop` | GATED: no-webkit-on-this-image | The surface is a real embedded browser. |
| `goon-page-explainer-card` | GATED: no-webkit-on-this-image | Same page, same engine. |

### Three gates that were nearly written and are NOT true

Recorded because each was the plausible guess and each was wrong when measured, which is the whole
reason to measure.

1. **The file dialogs are not gated.** `toast saved` and `toast refused` both drive a real native
   file dialog to completion, and this image has **no XDG desktop portal at all** — zero
   `xdg-desktop-portal` packages, no `/usr/libexec/xdg-desktop-portal`, no portal D-Bus service
   files. That looked decisive until the resolved assemblies were read: `Avalonia.X11` carries a
   three-way ladder — `DBusSystemDialog` (the portal), `Avalonia.X11.NativeDialogs.GtkSystemDialog`,
   and `ManagedStorageProvider` from `Avalonia.Dialogs.dll`, which ships in the app's own output —
   and `libgtk-3.so.0` IS installed here. So a picker has somewhere to go. Both toast checks are
   UNDRIVEN, not gated.
2. **The mantra window is not session-driven.** It comes off the Play page's Mantra card and its
   Begin button, so it needs no scripted session and the `ambiguous-accessible-name` gate does not
   touch it. What it needs is KEY injection, which `xinput.py` does not yet wire — and XTEST
   supplies `XTestFakeKeyEvent` on the same already-installed `libXtst.so.6` that the clicks use, so
   that is missing harness code and not a missing capability.
3. **`trainer-card-level earned` is not session-driven either.** `capture.ps1` seeds
   `progression.json` directly, which any platform can do.

## Undriven: 28 of 45 — the route supports them and this lane did not spend the time

These are **not** gates and must not be reported as any. Each one's controls are addressable through
the element route, each one's state is reachable with gestures the harness already drives or with a
seeded file, and what is missing is drive code in `capture-wslg.sh`. They are listed so the
distinction between "this platform cannot" and "nobody has yet" survives the next reader.

| Check | Status | What it still needs |
|---|---|---|
| `trainer-card-ground` | UNDRIVEN | Intake navigation, and the card rect derived from its own two TextBlocks. |
| `trainer-card-ink` | UNDRIVEN | Same rect, same drive. |
| `trainer-card-level-fresh-track` | UNDRIVEN | The Intake drive; no session and no seed. |
| `trainer-card-level-earned-fill` | UNDRIVEN | The Intake drive over a seeded `progression.json`. |
| `trainer-card-record-read-top-status-clear` | UNDRIVEN | A seeded award record plus the Intake drive. |
| `trainer-card-record-read-honor-progress-ink` | UNDRIVEN | Same. |
| `trainer-card-record-earned-honor-status-clear` | UNDRIVEN | Same, three-category record. |
| `trainer-card-record-earned-row-name-ink` | UNDRIVEN | Same. |
| `trainer-card-record-unreadable-top-status-ink` | UNDRIVEN | Same, with the truncated record. |
| `trainer-card-record-unreadable-honor-status-ink` | UNDRIVEN | Same. |
| `trainer-card-record-unreadable-clear-column` | UNDRIVEN | Same. |
| `session-row-easy-stripe` | UNDRIVEN | The stripe cell derived from the row and its meta cell. |
| `session-row-hard-stripe` | UNDRIVEN | Same derivation, the other session. |
| `session-start-idle-fill` | UNDRIVEN | The idle button needs no confirmation, only the rack drive. |
| `companion-permissions-panel-fill` | UNDRIVEN | A second window; `atspi.py` finds frames by title already. |
| `companion-permissions-closed-ground` | UNDRIVEN | Same window, the closed state. |
| `companion-privacy-broad-seat` | UNDRIVEN | Same window, one radio drive. |
| `companion-privacy-titles-seat` | UNDRIVEN | Same window, the other seat. |
| `companion-transcript-closed-ground` | UNDRIVEN | Same window plus a seeded memory file. |
| `companion-transcript-open-ground` | UNDRIVEN | Same, opened. |
| `popquiz-card-ground` | UNDRIVEN | The Pop Quiz card's own drive. |
| `popquiz-card-question-ink` | UNDRIVEN | Same card. |
| `toast-saved-accent` | UNDRIVEN | A real export through whichever picker the X11 ladder selects here. |
| `toast-saved-plate` | UNDRIVEN | Same gesture, same toast. |
| `toast-refused-accent` | UNDRIVEN | A real import of something that is not a backup. |
| `toast-refused-plate` | UNDRIVEN | Same toast. |
| `mantra-window-fresh-dim` | UNDRIVEN | The Play drive plus `XTestFakeKeyEvent` in `xinput.py`. |
| `mantra-window-typed-lit` | UNDRIVEN | Same, plus the typed characters. |

## Two defects this route found that the Windows route cannot see

1. **The rack dot's derived cell is two pixels above the dot at scale 1.75.** `capture.ps1` derives
   the 8-DIP dot cell from the row's own rectangle as `row.Y + (row.H - dot)/2`, and has no second
   reading to check it against because Avalonia gives an `Ellipse` no UIA peer. AT-SPI publishes the
   `Ellipse` as an element with its own bounds. Measured: at scale 1 the derivation and the dot's own
   bounds are identical; at scale 1.75 the derivation says `y=301` and the dot really sits at
   `y=303`, because `(63-14)/2` is 24.5 and the row's content presenter rounds elsewhere. The check
   still passes on Windows because a 14-pixel dot shifted 2 pixels still overlaps — but the
   derivation is naming a rectangle that is not quite the dot. `capture-wslg.sh` now captures the
   dot's own bounds and keeps the arithmetic as corroboration.

2. **Two simultaneously visible buttons publish the identical accessible name `Start Session`.**
   This is the `ambiguous-accessible-name` gate above, and it is a product accessibility defect
   rather than only a harness inconvenience: any assistive technology that addresses controls by
   name — which is what AT-SPI clients do, and what a screen reader announces — offers the user two
   indistinguishable "Start Session, button" targets, one of which starts a session and one of which
   does not. A distinct `AutomationProperties.Name` on `ScriptedSessionConfirmButton` lifts both the
   defect and the gate, and **that change was made on 2026-08-25**: the confirmation now publishes
   `Start Session (confirm)` and its visible caption is untouched, because the caption is the ported
   outcome. The Windows headed re-verification this asked for was taken — the five captures the gate
   names, every declared check green. The DEFECT is therefore closed; the GATE row above is a
   statement about evidence rather than about the mechanism, and it stays until a Linux run
   re-measures it.

## What none of this establishes

No animation, media, video, drag, resize, modality, multi-window focus transfer, keyboard, IME or
scroll-momentum behaviour is verified on Linux by any of it. The GPU-presented frame is
unphotographed. Input is proved for left-click, right-click and the wheel, on the Studio page only.
No human has looked at the screen. And a nested Weston compositor with no desktop environment is not
GNOME and not KDE.
