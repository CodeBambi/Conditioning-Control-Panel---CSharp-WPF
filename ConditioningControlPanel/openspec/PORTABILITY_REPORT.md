# CCP Portability Report

Generated 2026-06-15. Method: 8 parallel recon agents produced OpenSpec capability specs
(`openspec/specs/01..08`) classifying every subsystem **Portable / Mixed / OS-Specific**
against the rubric in `PORTABILITY_RUBRIC.md`.

## Codebase size (real — excludes the 3 `.claude/worktrees/` repo copies)

| Layer | LOC |
|---|---|
| XAML markup | 30,369 |
| Code-behind (`.xaml.cs`, 93 files) | 78,375 |
| Pure `.cs` (Services / Models / etc.) | 124,152 |
| **Total C#** | **~202,500** (+30K XAML) |

## Headline

- **The UI is a rewrite.** ~133K LOC of WPF/XAML + Win32 presentation (the app is almost
  entirely code-behind — a *single* ViewModel exists). Leaving Windows means re-platforming
  essentially all of it. This is the bulk of the codebase.
- **The engine is portable.** The brains — AI orchestration, gamification/Chaos economy,
  sessions, networking/auth transport, content/mod system, all data models — are ~65–75%
  portable, gated by a *small, repeating* set of seams, not by scattered rewrites.
- **Net across all C#: ~40–45% is salvageable** into a cross-platform Core; the remaining
  ~55–60% is presentation / OS-shell that any re-platform rewrites anyway.

## Per-cluster verdicts

| Cluster | Spec | Portable | What ports / what blocks |
|---|---|---|---|
| 01 Core & lifecycle | `01` | ~10–15% | Settings POCO, paths, log-scrub port. Blocks: WPF bootstrap, `SetWindowsHookEx`, tray, DWM, wallpaper, minidump |
| 02 Visual overlays | `02` | ~25–30% | Physics/timing/fade math port. Blocks: borderless Win32+WPF overlay windows, GDI capture |
| 03 Media | `03` | ~30–35% | Playlists, FFT, downloader, metadata. Blocks: LibVLC**.WPF** VideoView, NAudio (WASAPI/WaveOut) |
| 04 AI & companion | `04` | ~65–70% | All AI HTTP/JSON, parsing, personality, Bark data. Blocks: DispatcherTimer, NAudio phrase audio, 1 Win32 GIF window |
| 05 Gamification | `05` | ~70–75% | XP/quests/achievements/Chaos economy = pure logic. Blocks: DispatcherTimer + popup/chime feedback |
| 06 Networking & auth | `06` | ~70–75% | HttpClient+JSON transport, crypto, OAuth loopback (already X-platform). Blocks: DPAPI ×4, WebView2 |
| 07 Sensors & I/O | `07` | ~40% (30% LOC) | Gaze math, haptics (WebSocket), OCR logic. Blocks: native capture backend, WinRT/DirectShow, Win32 foreground/idle |
| 08 UI shell | `08` | ~12% | POCO models, i18n runtime port. Blocks: ~133K LOC WPF/XAML |

## Cross-cutting seams (fix once, unlock many clusters)

Ranked by how many clusters they infect:

1. **Scheduler / UI-dispatch** — `DispatcherTimer` + `Application.Current.Dispatcher` is the
   single most pervasive coupling, present in nearly every long-lived service across clusters
   04/05/06/07/01. It is what makes most "Portable" logic land as "Mixed." Mechanically
   swappable for `PeriodicTimer` / `System.Threading.Timer` + an `IUiDispatcher` /
   `SynchronizationContext` abstraction. **Biggest single lever.**
2. **`ISecretStore`** — replaces DPAPI (`ProtectedData`) in the 4 secure token/key stores
   (cluster 06). libsecret (Linux) / Keychain (macOS) off Windows.
3. **`IAudioPlayer`** — replaces NAudio output (companion phrases, SFX, calibration cues;
   clusters 03/04). NAudio is wholly Windows-only here (WASAPI/WaveOut).
4. **`IEffectSink` / `IOverlaySurface` / `IGameFeedback`** — replaces direct WPF-window
   spawning from logic (clusters 02/04/05). Lets the engine emit effects without owning windows.
5. **`IScreenProvider`** — replaces WinForms `Screen` geometry (clusters 02/03/05/07).
6. **`IThumbnailDecoder` / `IImageDecoder`** — replaces `BitmapImage`/`pack://` (clusters 02/06).
7. **`IVideoSurface`** — replaces LibVLCSharp**.WPF** `VideoView` (the *only* Windows binding in
   video; LibVLCSharp.Shared core is cross-platform).
8. Smaller one-offs: `IHotkeyProvider`, `IWallpaperProvider`, `IAutostartProvider`, `ITrayIcon`,
   `IInputHook`, `IUpdateInstaller`, `IWindowChrome`, `IFrameSource`, `IBrowserHost`.

## Free wins (dead dependencies — confirmed by grep)

- **SharpDX, SharpDX.DXGI, SharpDX.Direct3D11** — 0 `.cs` references. The csproj comment claims
  "Desktop Duplication API"; the actual screen capture is GDI `CopyFromScreen`. Remove all 3.
- **OpenAI-DotNet** — 0 `using OpenAI` references. AI is hand-rolled `HttpClient`. Remove.
- **OllamaSharp** — 1 vestigial import in `LocalAiService` (bypassed for hand-rolled HTTP per
  in-code comment). Remove the import + package.

## Irreducibly Windows (true rewrites, no clean cross-platform analog)

- Low-level global keyboard/mouse hooks (`SetWindowsHookEx WH_*_LL`) — lockdown + ripple input taps
- WebView2 embedded browser (`BrowserService`, Deeper auto-discovery)
- DWM caption tinting, system tray (Avalonia/Forms-equivalent exists but is a reimplementation)
- The entire WPF/XAML view layer (~133K LOC)
- Desktop wallpaper override, CCD display-topology switch, minidump watchdog

## Recommended plan

**Phase 0 — Cleanup (hours).** Remove the 3 dead dep families above. Zero risk, shrinks the
native footprint and removes "is this used?" confusion.

**Phase 1 — Carve `CCP.Core` (the high-value move, port or not).** Create a `net8.0`
platform-agnostic class library. Move in: all POCO models, localization runtime, AI
orchestration/parsing/personality, gamification + Chaos economy rules, networking transport +
crypto, session persistence, Deeper fetch, moderation, log scrubber, path/settings logic.
Introduce the seam interfaces (start with `IUiDispatcher` + scheduler, `ISecretStore`,
`IAudioPlayer`, `IEffectSink`). The existing WPF app implements them. **No behavior change** —
this is mechanical extraction. Payoff even with zero porting: testability, faster builds, a clean
engine/UI boundary, and it makes the rest of the question tractable.

**Phase 2 — Make Core compile & run off-Windows.** Swap `DispatcherTimer` → scheduler
abstraction and DPAPI → `ISecretStore` everywhere in Core. At this point the engine builds and
unit-tests on Linux/macOS headless. This is the concrete "how much is portable" proof.

**Phase 3 — (Only if a cross-platform product is the goal) New UI.** Avalonia is the natural
target (XAML-family, Win/Linux/macOS). Rebuild views against Core. Cross-platform substitutes:
video → `LibVLCSharp.Avalonia`; audio → LibVLC or ManagedBass; tray → Avalonia tray; browser →
drop or Avalonia WebView. Accept feature loss on the irreducible set (global hooks, wallpaper,
DWM, desktop-duplication-style capture) or gate them as Windows-only.

**Bottom line:** A headless/portable **engine** is very achievable and worth doing on its own
merits — ~70% of the IP-bearing logic ports behind ~6 interfaces, dominated by one (the
scheduler/dispatcher). A full cross-platform **app** is a large project whose cost is almost
entirely the UI rewrite (~133K LOC), not the engine.
