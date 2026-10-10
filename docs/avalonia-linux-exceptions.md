# Linux exceptions

The owner's rule for the cross-platform app: Linux limits are documented exceptions. This is the list.
One row per feature: what works on Linux, what is limited, what is not available and why.

**Status of this document (2026-10-10): read from the source on a Windows desk. Nothing here was run on
Linux today.** Rows written by the port's Linux author before 2026-10-09 were measured on a real box
(noted as "run before"). Everything else is "on paper": the code was checked against the API it calls,
not executed. The checklist at the end is the first hour on a real machine.

Kind of limit:

- **fundamental**: the platform does not offer it. Nothing to build.
- **not built**: possible on Linux, nobody wrote it yet. The row says what it would take.
- **needs a run**: built, compiles for Linux, never executed there.

## How the app runs on Linux

- The app is always an **X11 client**. Avalonia 12.1.2 has no Wayland backend and `Program.cs` pins
  `UseX11()`. On a Wayland session (GNOME, KDE Plasma 6) it runs through **XWayland**. A Wayland session
  with XWayland switched off cannot start the app at all.
- So "X11 only" below means: the feature works for windows that are X11 or XWayland clients. A **native
  Wayland window** (most GTK4 and Qt6 apps on a Wayland session) is invisible to those features. The
  compositor does not tell one client about another client's windows, keys or pixels. That is by design
  and has no portal for most of it.
- 64-bit only (x86_64, arm64). The X11 struct offsets are the LP64 layout.
- Build Linux releases **on Linux**. A cross-build from Windows compiles and publishes (proven today,
  `dotnet publish CCP.Avalonia -c Release -r linux-x64`, 0 errors), and the WinRT projection compiles out,
  but the Vosk package picks its native library by the build machine: the cross-built folder carries
  `libvosk.dll` and no `libvosk.so`, so speech would not load from it.

## Headline exceptions

| Feature | On Linux | Kind |
|---|---|---|
| Screen text reading (keyword OCR, keyword highlight from the screen) | Not available. The switches stay greyed with "not on this build". | not built |
| Brain Drain haze | Not available. Refused before it opens. | not built |
| Typed keyword triggers | X11 and XWayland windows only. No dead keys, no input methods. | fundamental (Wayland), not built (composition) |
| Do-not-disturb list, awareness (foreground app, title, fullscreen) | X11 and XWayland windows only. A native Wayland window reads as "no foreground". | fundamental |
| Idle time, typing-burst guess | X11 only, needs `libXss`. Under XWayland the idle counter only sees input that went to X windows. | fundamental (Wayland), needs a run |
| Hiding overlays from screen capture | Not available. Windows only (`SetWindowDisplayAffinity`). | fundamental |
| Camera shortcut from any app | Not available. Works only while the app has focus. | not built |
| Icon glyphs (Segoe MDL2 Assets) | Missing. Nav pills and hover bubbles fall back (text only, or an emoji). Other glyph-only buttons draw an empty box or nothing. | not built |
| Display face (Fredoka) | Packed (`Assets/fonts`, SIL OFL 1.1) and mapped by name on both OSes since 2026-10-10. Headless tests resolve it; no desk run yet. | built, needs a run |
| Web pages (games, intake, Chaos) | WebKitGTK. No autoplay without a click, no background-timer switches, WebRTC often missing. | fundamental for the switches |
| Goon video over 5 MB | Cannot be sent. **Same on Windows** in the port (the transcoder is WinRT in WPF, not ported). | not built, both OSes |
| In-app updater | Opens the releases page. No download and install. | by design (packages update through their own channel) |
| Ducking other apps' audio | `pactl` only. **Not built on Windows** in the port. | Linux works on paper; Windows not built |
| Microphone-in-use (meeting guard) | No probe. Reads as "unknown", which counts as "not in use". Windows reads the capture session list since 2026-10-10 (`Platform/MicrophoneInUseProbe.cs`). | not built on Linux |
| Desktop wallpaper effect | Not on this branch. No Linux backend to review. | not built |

## Input and awareness

| Feature | Works | Limited / not available | Kind | Code |
|---|---|---|---|---|
| Panic key | X11: XInput 2.1 raw keys on the root window, seen even while another app holds a grab. Wayland session: the GlobalShortcuts portal is asked for a shortcut while effects run. Run before. | On a native Wayland window with no portal (older GNOME, wlroots without the portal), the key is only seen while an X window has focus. The portal shows its own consent dialog once. | fundamental | `Platform/X11PanicKey.cs`, `Platform/PortalPanicShortcut.cs` |
| EMI desk summon chord | X11 `XGrabKey` on the root. Run before. | Not seen while a native Wayland window has focus. | fundamental | `Platform/X11SummonChord.cs` |
| Typed keyword triggers | X11: XInput 2.1 raw key presses, no grab, nothing intercepted. Layout, Shift, AltGr, Caps Lock and (since today) NumLock for the number pad are applied. | Text typed into a native Wayland window is never seen. No dead keys and no input-method composition: a composed character arrives as its base keys, so a keyword with an accent typed through a dead key does not match. Needs `libXi`. | fundamental (Wayland); not built (composition would need an XIM / xkbcommon compose state) | `Platform/X11KeyListener.cs` |
| Keyword triggers from screen text (OCR) | Nothing. | Nothing reads text from a screen grab on Linux. It would take a screen grab (the screenshot portal asks the user every time on GNOME; X11 root grab works only on a real X session) and an OCR engine (Tesseract as a dependency). | not built | `Platform/ScreenOcrService.cs` (`ReasonUnavailable`) |
| Foreground app for do-not-disturb | `_NET_ACTIVE_WINDOW` + `_NET_WM_PID` over XCB. **Fixed today:** the flash and video holds asked a guard that answered "" off Windows, so the list never held anything on Linux. | Native Wayland windows have no readable owner: they never hold a flash or a video. A sandboxed app (Flatpak) reports a pid from its own namespace and reads as unknown. | fundamental | `Platform/DoNotDisturbGuard.cs`, `Platform/X11Windows.cs` |
| Do-not-disturb app picker | Lists the owners of `_NET_CLIENT_LIST` windows. | X11 and XWayland windows only. | fundamental | `Platform/X11Windows.cs` |
| Awareness: window title | `_NET_WM_NAME`, then `WM_NAME`. Run before. | Native Wayland window: Unknown, she says nothing about it. A legacy `WM_NAME` that is not UTF-8 may show wrong letters. | fundamental | `Platform/X11ActiveWindow.cs` |
| Awareness: fullscreen | `_NET_WM_STATE_FULLSCREEN` on the active window. | X11 and XWayland windows only. A borderless window that only covers the screen (no fullscreen state) is not seen as fullscreen; Windows compares rectangles instead. | needs a run | `Platform/X11ActiveWindow.cs` |
| Awareness: idle seconds and typing guess | XScreenSaver extension (`libXss`). **Fixed today:** read wobble no longer counts as typing. | Without `libXss`: idle reads as "active", typing as "not typing". Under XWayland the counter resets only on input to X windows, so a user busy in a Wayland window can read as idle. | fundamental (Wayland), needs a run | `Platform/AwarenessProbes.cs` |
| Awareness: now playing | MPRIS over the session D-Bus, 3 s poll. | A player that does not speak MPRIS is not seen (some Flatpak sandboxes hide the bus name; some browsers need a setting). If the session bus restarts, the watcher stays off until the app restarts. | needs a run | `Platform/MprisMediaWatcher.cs` |
| Awareness: microphone in use | Nothing on Linux (the answer is "unknown"). Windows sweeps the capture endpoints for an active session, cached 5 s, as WPF. | On Linux the meeting guard never trips from the microphone; the fullscreen, typing and CCP-surface gates still apply. Linux would read PulseAudio / PipeWire source-outputs (`pactl list source-outputs`). | not built on Linux | `Platform/MicrophoneInUseProbe.cs` (`PlatformSweep` returns null off Windows) |
| OS reduced motion | GNOME: `gsettings enable-animations`. | Other desktops: reads as "animations on". | not built (KDE has its own key) | `Controls/Fx/OsReducedMotion.cs` |

## Overlays and windows

| Feature | Works | Limited / not available | Kind | Code |
|---|---|---|---|---|
| Click-through overlays (flash, spiral, pink filter, bubbles, bouncing text) | Override-redirect windows with an empty XFixes input shape. Run before, on XWayland too. | Needs `libXfixes` and, for tints, a compositing manager (a bare X server paints opaque blocks; the tint is refused there). | fundamental | `Platform/X11Overlay.cs` |
| Brain Drain haze | Nothing. | The haze blurs a grab of the screen under it. An X11 root grab under a compositor returns the haze itself, so it would feed back. It needs a grab that leaves our own windows out: XComposite per window, or the ScreenCast portal (asks the user, shows a recording indicator). | not built | `Views/Overlays/BrainDrainOverlay.cs` (`IsSupported`) |
| Hiding overlays from screen capture / streams | Nothing. | X11 and Wayland have no per-window capture exclusion. A stream or a screenshot shows the overlays. | fundamental | `Views/Overlays/BrainDrainOverlay.cs`, `KeywordHighlightOverlay.cs` |
| Welcome show stage | A passive override-redirect stage; only live bubbles take clicks (XFixes input rectangles per frame). **Fixed today:** override-redirect now goes on before any geometry. | The show refuses to open when the stage cannot be made click-through (no XFixes, or a future Wayland backend), instead of covering the desktop with a window that eats clicks. Esc and the panic key stop it through the X11 panic listener, not a global hook. | needs a run | `Views/Windows/WelcomeShow/FirstShowStageWindow.cs` |
| Friends landing, knock cards, notices, achievement toasts | Passive override-redirect windows: no focus, no taskbar entry, above a game. | Where override-redirect is refused the window is only placed: it may take focus and show in the taskbar. On tiling window managers override-redirect windows float, which is intended. | needs a run | `Views/Friends/LandingChrome.cs` |
| Tray icon | Only while a StatusNotifierWatcher is running (KDE, or GNOME with the AppIndicator extension). | Without one, closing the window quits the app instead of hiding to tray. | fundamental | `Views/Windows/MainShellWindow.Tray.cs` |
| OS notifications | `org.freedesktop.Notifications` over D-Bus, with click. | No notification server and the window hidden: the notice is dropped (logged once). | fundamental | `Platform/OsNotifications.cs` |
| Camera on/off shortcut | While the app has focus. | The any-app shortcut is a Windows keyboard hook. Linux would ride the X11 raw key listener (X11 only) or a second portal shortcut. | not built | `Views/Windows/MainShellWindow.CameraShortcut.cs` |

## Audio, video, camera, speech

| Feature | Works | Limited / not available | Kind | Code |
|---|---|---|---|---|
| Audio and video | System `libvlc.so.5`. Run before. | Needs VLC installed (the installer plan covers it, `docs/avalonia-linux-install.md`). | - | `Platform/LibVlcAudio.cs` |
| Ducking other apps | `pactl` sink-input volumes (PulseAudio or PipeWire-pulse). | No `pactl`: no ducking, one warning in the log. | needs `pactl` | `Platform/LibVlcAudio.cs` |
| Output device picker | `pactl list sinks`. | No `pactl`: an empty list, default device. | needs `pactl` | `Platform/LibVlcAudio.cs` |
| Microphone (speech, mantras, wake word) | `parec` / `pw-record` into Vosk. Run before. | Needs the recorder tool and a Linux-built `libvosk.so`. | - | `Platform/PulseMicSource.cs` |
| Camera list | `/sys/class/video4linux/video*/name`, capture nodes only (`index` 0). | Inside a Flatpak the camera needs the device permission. | needs a run | `Platform/CameraList.cs`, `Platform/WebcamTracker.cs` |
| Camera capture | OpenCV V4L2. | OpenCV 4.13 Linux runtime: glibc 2.28 + GTK3. | needs a run | `Platform/WebcamTracker.cs` |
| Discord Rich Presence | Same library as WPF; it opens `discord-ipc-N` under `$XDG_RUNTIME_DIR` and the Flatpak / Snap paths it probes. | A Flatpak or Snap Discord with a closed socket path: no presence, one warning. | needs a run | `Platform/DiscordRichPresenceService.cs` |

## Web pages (games, intake, Arcademy, Goon, Chaos)

| Feature | Works | Limited / not available | Kind | Code |
|---|---|---|---|---|
| Web view | WebKitGTK 4.1 / 4.0 (GTK3) or WPE WebKit. Run before. | Not installed on a fresh box: the page area shows the install hint and the app names the distro's package in one toast. webkitgtk-6.0 (GTK4) does not work. | - | `Views/Controls/WebHost.axaml.cs`, Core `LinuxDependencies` |
| Autoplay without a click | No. `init.autoplayOk` is false, pages wait for the first click before sound or video. | WebKitGTK has no command-line autoplay switch like WebView2's. | fundamental | `WebHost.AutoplayWithoutGesture` |
| Timers while the window is hidden | Throttled by WebKit. | The WebView2 no-throttling switches have no WebKitGTK twin. A game left behind another window may run its clock slowly. | fundamental | `WebHost.BrowserArguments` |
| WebRTC (Goon peer link) | Depends on the distro's WebKitGTK build. Often missing. | The page falls back to the server relay. Not re-checked in the code today (lane note). | needs a run | `Views/Games/GameWindow.Goon.cs` |
| Browser mute | A page script on both OSes. | A page that builds its own audio graph after the script ran can escape it. Not re-checked today (lane note). | both OSes | `Views/Controls/WebHostMedia.cs` |
| Goon own-media video | Pictures, and clips small enough to go as they are. | A clip that needs shrinking (over 5 MB per the lane note; the figure was not re-read today) cannot be sent: the transcoder is not ported (WPF uses WinRT MediaTranscoder). Linux would use LibVLC `sout` or ffmpeg. | not built, both OSes | `Views/Games/GameWindow.GoonTransfer.cs` |

## System integration

| Feature | Works | Limited / not available | Kind | Code |
|---|---|---|---|---|
| Desktop shortcut for a game | A `.desktop` entry in `~/.local/share/applications` (the app menu) and on the Desktop folder when there is one, executable bit set. | GNOME shows a new desktop file as untrusted until "Allow Launching" is chosen once (GNOME keeps that flag in `gio` metadata; the app does not set it). The icon is the shipped `.ico` path: GNOME and KDE usually draw it, the spec asks for PNG or SVG. | needs a run; PNG icons not built | Core `LauncherShortcuts.DescribeDesktopEntry`, `Platform/LauncherShortcutWriter.cs` |
| Start with the session | An XDG autostart entry. Run before. | - | - | `Platform/XdgAutostart.cs` |
| Sign-in token store | libsecret (GNOME Keyring, KWallet's secret service). Run before. | No secret service: sign-in works for the session and is not remembered; the app says so once. | fundamental | `Platform/SecretStore.cs` |
| Updates | The update pill opens the releases page. | No in-app download and install. | by design | `Platform/AppUpdater.cs` |
| Single instance | .NET named pipe (a socket under `/tmp`). | - | needs a run | `Platform/SingleInstance.cs` |
| Windows-only self-checks | `--win-panic-check` says Windows only on Linux. | `--x11-probe`, `--panic-check`, `--notify-check`, `--portal-check` say Linux only on Windows (since today). | - | `Program.cs` |

## Fonts

No font file is installed by the app. What the views name, and what Linux draws:

| Named in the views | Count | Linux before today | Linux now (on paper) |
|---|---|---|---|
| (nothing named: the Fluent theme) | most text | Inter (bundled, `Avalonia.Fonts.Inter`) | same |
| `Consolas, Courier New` (+ `monospace`, `Cascadia Mono`) | about 130 | The desktop's default **proportional** sans: clocks, prices and counters lost their column alignment. `monospace` is a CSS word, not a family the font manager resolves. | **Noto Sans Mono** (bundled for the EMI ring cards, `Resources/emi/fonts`) through `FontFamilyMappings` |
| `Segoe UI`, `Arial` | about 30 | The desktop's default sans (differs per distro) | **Inter** through `FontFamilyMappings` |
| `Fredoka, Segoe UI` (page titles, display text) | about 85 | Default sans | **Fredoka**, packed at `Resources/fonts` and mapped through `FontFamilyMappings` on every OS (`Platform/AppFonts.cs`), as WPF 7.1.5 packs `/Fonts/#Fredoka`. |
| `Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI` (icon glyphs, private-use code points) | about 12 named sites + the nav pills and hover bubbles | No font has these code points. Pills show the label only and hover bubbles show their fallback emoji (both test for the glyph). The other sites (settings cog, launcher links, friend notice close marks, conversation page) draw an empty box or nothing. | same |
| `Press Start 2P` | EMI ring cards | Bundled | same |
| `Segoe UI Emoji`, `Segoe UI Symbol` (plain Unicode symbols and emoji) | a few | Whatever fontconfig finds: Noto Color Emoji and DejaVu / Noto Symbols on most desktops. A minimal install without an emoji font draws boxes. | same |
| `Impact, Arial Black`, `Comic Sans MS` | 5 | Default sans | Inter |

The mappings live in `Platform/AppFonts.cs` and are applied off Windows only (on Windows every name
resolves, and the mapping is consulted only for a name that cannot be resolved). Not seen on a screen.

What bundling the missing two would take:

- **Fredoka**: DONE 2026-10-10 (`Assets/fonts/Fredoka.ttf` + `OFL-Fredoka.txt`, one mapping line,
  `DisplayFontTests`). It is one variable file whose default instance is Light; Avalonia reports the
  family as "Fredoka Light" and emboldens heavier weights, as WPF does. Wants an eye on both OSes.
- **Icon glyphs**: Segoe MDL2 Assets is Microsoft's and cannot ship. It takes a small icon font built
  from a redistributable set (Fluent UI System Icons, MIT) with the roughly 60 glyphs the app uses
  remapped onto the MDL2 code points, packed, and one mapping line for `Segoe MDL2 Assets`. About half a
  day with a font tool; no call-site edits.

## Packages a Linux box needs

`docs/avalonia-linux-install.md` has the installer plan. Libraries the code opens by name, beyond that
list (each is optional at runtime and fails quiet):

| Library / tool | For | Without it |
|---|---|---|
| `libXfixes.so.3` | click-through overlays, the welcome show | no overlays; the welcome show refuses to open |
| `libXi.so.6` | panic key, typed keywords | panic key through the portal only; no typed keywords |
| `libXss.so.1` | idle time, typing guess | idle reads as active |
| `libxcb.so.1` | do-not-disturb foreground app and picker | nothing is ever held; empty picker |
| `libsecret-1.so.0` | remembered sign-in | sign-in not remembered |
| `pactl` | ducking, output devices, microphone list | none of the three |
| `parec` or `pw-record` | microphone | no speech features |
| `gsettings` | OS reduced motion | reads as animations on |

`libXfixes`, `libXss` and `libxcb` are not in the startup dependency toast (Core `LinuxDependencies`)
yet. They are on every desktop install; a minimal container would miss them without a word.

## First hour on a real Linux box

1. Build on the box: `dotnet publish CCP.Avalonia -c Release -r linux-x64 --self-contained`, and
   `dotnet run --project CCP.LinuxSmoke -c Release` (the Core smoke CI already runs).
2. Self-checks, each prints PASS / FAIL: `--smoke`, `--overlay-check`, `--x11-probe`, `--panic-check`,
   `--notify-check`, `--portal-check` (Wayland session), `--tray-probe`, `--nav-check`.
3. Fonts: open Home and the Chaster tab. Clocks and prices must be monospaced (Noto Sans Mono) and
   `Segoe UI` text must be Inter. If not, `FontFamilyMappings` does not behave as its doc says: the
   fallback is a find-and-replace of the family strings.
4. Typed keyword: add a keyword, type it in an X11 app (xterm) and in a native Wayland app. Expect: fires
   in the first, not in the second. Try a digit on the number pad with NumLock on.
5. Do-not-disturb: put a running X11 app on the list, focus it, wait for a scheduled flash. Expect the
   "[DND] flash suppressed" log line.
6. Awareness: watch the log for the foreground sample while switching windows; leave the desk idle for a
   minute with a still mouse and confirm no typing burst is reported; play a track in an MPRIS player
   (Spotify, Firefox) and confirm title and artist are read.
7. Welcome show (Help > replay): the stage covers the primary screen exactly, clicks pass through to the
   desktop, bubbles pop, the panic key ends it. Repeat on KDE Wayland with two monitors (the stale-size
   replay this order guards against showed there).
8. Friends: receive a knock card while a game window has focus. The card must not take focus.
9. Launcher: create a game shortcut. Check the app menu entry starts the game, the Desktop file launches
   (GNOME: after "Allow Launching") and the icon draws.
10. Web pages: open Arcademy and Goon. First click starts sound. In Goon check whether the peer link or
    the relay is used.
11. Camera: the picker lists the camera by name; tracking starts.
12. Discord: presence shows with native Discord; note what happens with the Flatpak one.
