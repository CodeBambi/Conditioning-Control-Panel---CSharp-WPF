# Linux / macOS Head Readiness Map (seam-level, 2026-07-12)

**Source of truth for "what works vs no-ops off Windows."** Derived by static analysis of each head's
`Program.cs` seam registrations vs the shared `CCP.Avalonia` defaults and `CCP.Avalonia.Desktop`
bootstrap (`ProgramShared.Run`). NOT runtime-verified on Linux/macOS (no Linux host in the driver env —
all three heads compile green via `CCP.Desktop.slnf`; Core 750 tests pass cross-platform in CI).

## Registration model
- **Windows head** `Program.cs` registers ~23 real native seams directly (Win32/NAudio/WebView2/OpenCV).
- **Linux head** `Program.cs` registers ONLY `IBrowserHost → WebKitGtkBrowserHost`, then delegates to
  `ProgramShared.Run`.
- **macOS head** `Program.cs` registers ONLY `IBrowserHost → WebKitBrowserHost`, then delegates.
- **`ProgramShared.Run`** (all desktop heads) gives: LibVLC video, `DesktopSecretStore`,
  `DesktopWallpaperProvider`, `DesktopSingleInstanceService`.
- Anything a seam has no head registration AND no shared `CCP.Avalonia` default for → resolved from an
  Avalonia-level default (often a `Null*`/stub) or an optional ctor (feature silently inert).

## WORKS on Linux/macOS (real cross-platform impl registered)
| Capability | Impl | Note |
|---|---|---|
| Video playback | LibVLC (`AddDesktopLibVLC`) | Linux needs system `libvlc`/`vlc`; macOS `VideoLAN.LibVLC.Mac` |
| Secrets at rest | `DesktopSecretStore` | verify libsecret (Linux) / Keychain (macOS) backing |
| Wallpaper | `DesktopWallpaperProvider` | impl completeness per-DE unverified |
| Single instance | `DesktopSingleInstanceService` | |
| Web browser / DTRH game host | `WebKitGtkBrowserHost` (Linux) / `WebKitBrowserHost` (macOS) | ⚠️ DTRH web assets NOT bundled on Linux/macOS heads (board row) |
| All portable Core logic | `CCP.Core` | sessions, flash/subliminal TEXT, **bark ENGINE decisions**, progression, quests, achievements, AI chat/companion, gamification, quiz — CI-tested |
| Audio ducking (seam present) | `AvaloniaSystemAudioDucker` shared default | best-effort `pactl`/`osascript` per rebuild-plan; effectiveness unverified |
| Webcam (base seam present) | `AvaloniaWebcamService` shared default | ⚠️ real eye-tracking is Windows-only (below) |
| Startup reg / Update installer | `AvaloniaStartupRegistration` / `AvaloniaUpdateInstaller` shared defaults | completeness unverified |

## NO-OP / MISSING on Linux/macOS (Windows-only seam, no cross-platform impl)
| Capability | Windows-only impl | Linux/macOS effect |
|---|---|---|
| **Overlay click-through / topmost input regions** | `WindowsOverlaySurface` (`IOverlaySurface`) | **BIG ONE** — no shared default. The per-region compositor input model has no X11/Wayland impl → ambient overlays / click-through degrade. WS4 judgment work. |
| **Screen capture** | `WindowsFrameSource` (`IFrameSource`) | no shared default → AvatarTube live-screen mirror + screen-derived effects inert |
| **Awareness (foreground title)** | `WindowsForegroundWindowTitleProvider` | no provider → awareness engine no-ops gracefully (AI-1/2/10 Windows-only). Needs X11/Wayland/AXAPI. |
| **Webcam eye-tracking + gaze** | `AvaloniaWebcamTrackingService` + gaze drift (OpenCV/ONNX, win runtime) | real tracking Windows-only; needs Linux/macOS OpenCV runtimes |
| **Screen OCR keyword triggers** | `AvaloniaScreenOcrService` (win) | no shared default → OCR keyword feature inert |
| Audio device selection | `WindowsAudioDeviceService` (NAudio) | Windows-only |
| Audio waveform | `NAudioWaveformProvider` (win) / `NullAudioWaveformProvider` (shared) | **no-op everywhere** |
| Speech wake / recognition | `WindowsSpeechService` / `NullSpeechService` (shared) | voice commands no-op everywhere |
| Window chrome | `WindowsWindowChrome` | custom chrome Windows-only |
| Audio duration provider | `NAudioDurationProvider` (win) | affects bark whisper-gate timing off Windows — verify shared default |

## GOVERNING PRINCIPLE (owner directive 2026-07-12): must work on ANY Linux system
The owner does not have a specific Linux test machine and will NOT be the verification constraint —
end users run arbitrary distros/display servers/DEs. Therefore EVERY Linux platform-seam impl is
**multi-backend by construction**, behind one seam, selected at runtime, with a fallback chain that
never hard-fails:
- **X11** backend (Xlib/XCB) — covers most DEs incl. XWayland.
- **Wayland** backend — wlr-layer-shell / foreign-toplevel / PipeWire-portal where the protocol allows
  (sway/Hyprland/wlroots full-strength; GNOME-Mutter / KDE best-effort).
- **Graceful fallback** — a reduced-but-usable path so an unknown environment still gets *something*
  (e.g. plain always-on-top window; "unknown" activity; no-op with logged reason) rather than a crash.
- Runtime detection via `XDG_SESSION_TYPE` / `WAYLAND_DISPLAY` / `DISPLAY`.
- **Verification is CI-based, per backend** (owner-independent): X11 via `xvfb`, Wayland via a headless
  compositor (`weston --backend=headless` or `sway` headless). No slice lands "compile-only."
Applies to: `IOverlaySurface`, `IForegroundWindowTitleProvider`, `IFrameSource`, screen OCR, audio
ducking — each is a backend family, not a single impl. "Multiple versions" = these backends.

## Bring-up leverage order (highest first — pending judgment-tier sequencing)
1. **Overlay surface / click-through (X11 first, Wayland best-effort)** — unblocks the entire ambient
   conditioning surface; it is the WS4 judgment core (board row #5).
2. **Screen capture (`IFrameSource`)** — AvatarTube live mirror + screen effects.
3. **Foreground-title provider** — lights up awareness (AI-1/2/10) on Linux/macOS.
4. **Webcam/OpenCV runtimes** — eye-tracking/gaze.
5. **Screen OCR**, audio device/duration, speech — lower reach.
6. **DTRH web-asset bundling** on Linux/macOS heads (separate filed row) — cheap, unblocks the game.

> **Hard constraint:** the driver has no Linux host. Every impl here is compile-only until exercised on a
> real Linux desktop (or CI with an X display). Sequencing must pair each slice with a concrete
> verification path (CI headless / owner Linux test), or it is unverifiable.

## Landing log — Linux/macOS bring-up (2026-07-12, @driver via workflows)

Order of attack was hardest-first per the leverage list; every impl fable-5-hardened where judgment-sensitive.

- **[DONE] Foreground-title provider (Linux X11)** — `6dc22c30`. `LinuxForegroundWindowTitleProvider` +
  `X11TitleBackend` (own `XOpenDisplay`, atoms interned, per-display chained `XlibErrorTrap` on both
  `_NET_ACTIVE_WINDOW` root read + `_NET_WM_NAME`/`WM_NAME` UTF8 read; `XFree` on every buffer incl
  type-mismatch). Registered in Linux `Program.cs`. **Lights up awareness AI-1/2/10 on X11/XWayland.**
  Limitation: an X11 connection on a native-Wayland session sees only XWayland titles → native-Wayland
  apps return "unknown" honestly; `wlr-foreign-toplevel-management` backend remains a documented wave-3 gap
  (NOT stripped). Privacy: titles memory-only, never disk/net/logs, no PID enum (`AwarenessTitlePrivacyTests`).
- **[DONE, wiring in flight] Screen capture (Linux X11 `IFrameSource`)** — `6dc22c30`. `LinuxFrameSource` +
  `X11BasicFrameSourceBackend` (`XGetImage` + stride/bit-order/alpha repack to the tight-packed `RawFrame`
  contract, `XDestroyImage` every image, scoped trap, black-frame fallback = opaque black incl partial
  hot-plug clamp). Privacy: frames memory-only, never disk/net/logs, no idle capture (consumer-pull only;
  `FrameSourcePrivacyTests`). DI registration deferred to the wave-2 smoke-harness owner (in flight).
- **[IN FLIGHT] Overlay activation (Linux X11)** — the X11 overlay backend (XFixes input-shape click-through +
  `_NET_WM_STATE_ABOVE`, per-region = union of CAPTURING layers) landed earlier (`f643fa91`); wave-2a wires
  `AvaloniaPlatformCapabilities.SupportsClickThrough` (was hardcoded `IsWindows`) to an X11-availability
  probe so the click-through feature becomes SELECTABLE on Linux (stays default-OFF).
- **[IN FLIGHT] CI smoke-harness (Linux)** — making the repo-root `linux-smoke.yml` xvfb lane actually
  exercise the head (boot→tabs→exit0) instead of boot+timeout; `SmokeTestRunner.cs` stays untouched.
- **[DONE] DTRH web-asset bundling (Linux + macOS)** — `7b684d45`. `Content Include=..\Resources\web\**` mirror.
- **[DONE] macOS hard-seam contracts** — `63aafd55`. fable-5 designs for overlay (`NSWindow.level` +
  `ignoresMouseEvents` per-region poller), foreground-title (AXAPI/CGWindowList), framesource (ScreenCaptureKit
  vs CGDisplayStream), preflight-gated never-prompt-from-seam TCC strategy, macos-latest headed-smoke CI recipe.
  Impl pending §7.1 web-verification pass. **Cross-platform base: Avalonia 12.1.0, X11 backend (no native Wayland).**

Core test floor progression this wave: 820 → 833 (fg-title) → 835 (framesource) → 848 (merge) → 851 (fable-5 harden).
