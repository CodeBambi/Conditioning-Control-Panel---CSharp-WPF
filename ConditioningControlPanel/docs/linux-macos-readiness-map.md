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
