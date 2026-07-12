# WPF v6.3.2 "Deeper Down" hotfix → Avalonia-port reconciliation

**Merge:** `origin/main` v6.3.2 (`e446f904`, 8 commits `e55f8370..e446f904`) merged into `feat/crossplatform` at **`b0247c66`** — **0 conflicts** (v6.3.2 is WPF-side + `Resources/web`, disjoint from the port). Avalonia heads build, Core 861.

Every WPF-side change classified below with WPF `file:line` and the Avalonia disposition. Dispositions from the read-only reconciliation analysis (2026-07-12).

## Disposition table

| # | WPF change | Disposition | Avalonia action |
|---|------------|-------------|-----------------|
| 1 | Video off-UI-thread wedge watchdog (#529/#532) | **NA — root cause absent** | none (UCE eliminated the wedge) |
| 2 | Flash pack-image decrypt off-main-thread | **NA — no pack path; decode already off-thread** | none (caveat for future pack-flash port) |
| 3 | Perf-tier animated tease-bubble cap (#523) | **NA — native chaos is web-only** | none |
| 4 | `BubbleService` +2 (`BuildTeaseFace` cap) | **NA — same as #3, chaos-native tease** | none |
| 5 | Gaze-test window clickable Close (#528) | **PORTED** (in flight) | `CCP.Avalonia/Windows/WebcamGazeTrackerWindow.axaml` +Close button |
| 6 | DTRH Esc-ladder + C#-owned fullscreen | **PARTIAL → porting fullscreen** (in flight) | `DtrhHostOrchestrator` + `IDtrhNativeEffects` + `DtrhGameHostService` |
| 7 | DTRH web v2 bundle (`Resources/web/dtrh/**`) | **LANDS FREE** (bundle confirmed) | none (bridge = #6) |

**Also:** version bump 6.3.1 → **6.3.2** across all csprojs + WPF/Core `UpdateService` + installer + localization (in flight) — fixes the Avalonia head showing the Update-Available popup every launch (the Core `UpdateService` was stale at 6.3.1).

## Deliberate non-ports (rationale)

- **#1 Video wedge watchdog** — WPF `Services/Video/VideoService.cs:53-67,1280,2499-2568` arms an off-thread watchdog (`WedgeStallMs=22000`) that breaks a **layered-window-recreate dispatcher deadlock**. The Avalonia video path removed that root cause: video renders into the **persistent** UCE compositor window via `VideoLayer`/`MandatoryVideoLayer` (no per-video window create/destroy), and LibVLC `Stop()` is **already off-thread** (`VideoLayer.Stop()` → `Task.Run(() => player.Stop())` + bounded `Wait(500)`, citing "WPF #479 hazard"). The multi-minute lockout pathology cannot occur. Optional defense-in-depth (lighter heartbeat + off-thread `ForceCleanup` in `AvaloniaVideoService`) is judgment-tier and **not required for parity**.
- **#2 Flash pack-decrypt** — WPF `Services/Flash/FlashService.cs:1853-1860` moved the pack decrypt (`GetPackFileTempPath`, under `lock`) off the caller thread. `AvaloniaFlashService` has **no pack-image path** (no `_packImageList`/`IContentPackService`; `GetImageFiles()` scans the disk `images/` folder only) and regular decode is already off-thread (`SpawnDecoded` → `Task.Run`). **Caveat:** when pack-image flash is eventually ported, apply this hotfix's lesson (decrypt off-thread from the start).
- **#3 / #4 Perf-tier tease-bubble cap** — WPF `Services/PerformanceProfile.cs:73-83` + `BubbleService.cs:4300` swap `ChaosTuning.TEASE_MAX_ANIMATED` → `PerformanceProfile.MaxAnimatedTeaseBubbles(CurrentTier)` inside `BuildTeaseFace`. `PerformanceProfile` is **not ported** and native chaos tease rendering went **web-only** (owner ruling 2026-07-10); the web game caps its own bubbles in JS.

## Ported items

- **#5 Gaze-test Close button (#528)** — WPF `Windows/WebcamGazeTrackerWindow.xaml:37-44`: an always-visible Close bypasses the lockdown global keyboard hook that swallows Esc/Alt+F4. The Avalonia gaze window (`Maximized`/`Topmost`/no-decorations, Esc-only) could trap the user if the lockdown `IInputHook` swallows Esc → added an always-visible `BtnClose` (reusing `window_webcam_gaze_tracker_close_content`).
- **#6 DTRH C#-owned fullscreen** — WPF `Services/Chaos/DtrhHostService.cs:281-284,567-584`: web v2 **posts `{type:"fullscreen-set", on}`** (deliberately not the HTML5 Fullscreen API, which would hijack Esc); host sets fullscreen then echoes `{type:"fullscreen", on}` back. In Avalonia, `DtrhHostOrchestrator.Route()` had **no `fullscreen-set` case** (silently dropped) and `DtrhGameHostService.OnFullscreenChanged` used the now-dead HTML5 path → the DTRH fullscreen toggle + the fullscreen leg of the Esc-ladder were **broken with web v2**. Fix adds the Core route case + `IDtrhNativeEffects.SetHostFullscreen(bool)` + Windows-head `WindowState.FullScreen` handler + the echo (byte-for-byte with WPF). Windows-head-only (Linux/macOS have no DTRH WebView host yet).
- **#7 DTRH web v2 assets** — the `<Content Include="..\Resources\web\**\*">` glob (Windows csproj:61, Linux:36, macOS:26) covers `dtrh/**`, so `manifest.js`/`bubbles.js`/`panel.js`/`scene.js`/`settings.js`/`chaosRun.js`/`cheshireGuide.js`/`warren.js`/`styles.css` + `.webm` swaps ship to output and load via the `ccp.game`→`webRoot` virtual host. The only forced C# bridge change is #6's `fullscreen-set`.

## Localization (resolved)

- **Localized version-out badge** — `MainWindowViewModel.cs:551,2136` looks up `btn_v{version}_is_out` / `tooltip_v{version}_deeper_down`. The version-bump commit (`eeef31e2`) **added `btn_v6_3_2_is_out` ("💖 v6.3.2 IS OUT! 💖") + `tooltip_v6_3_2_deeper_down` to all 9 shared `Localization/Languages/*.json`** (linked into Core via `CCP.Core.csproj:50`, used by both heads), keeping the old 6_3_1 keys for history. The badge shows localized text — **no residual.** Update popup also fixed (Core `UpdateService` = 6.3.2).

## Status
- [x] Merge (`b0247c66`), heads build, Core 863
- [x] Version bump 6.3.2 merged (`f403261d`) — popup fixed; localized badge keys present (all 9 languages)
- [~] Version bump 6.3.2 (in flight) — fixes update popup
- [~] #5 gaze-close (in flight, GLM)
- [~] #6 DTRH fullscreen-set (in flight, fable-5)
- [x] #1/#2/#3/#4 documented non-ports; #7 lands free
