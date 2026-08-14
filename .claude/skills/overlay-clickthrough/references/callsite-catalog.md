# Click-through / topmost P/Invoke call-site catalog

Line numbers are as of 2026-07-02 (branch feat/crossplatform) and will drift; use them as
search anchors, not gospel. All paths relative to repo root.

## WPF head: standard click-through ex-style block

The common core is `GWL_EXSTYLE |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT`,
copy-pasted per file (there is NO shared helper in the WPF head). `WS_EX_LAYERED` is ORed
explicitly only in some of them; WPF windows with `AllowsTransparency=true` already carry
the layered bit, which is why many sites omit it. Flag variations noted per site:

Full four-flag block (TOOLWINDOW|NOACTIVATE|TRANSPARENT|LAYERED):
- `ConditioningControlPanel/Services/Notifications/OverlayService.cs` :818, :1243, :1320, :1681 (spiral, pink filter, brain drain; :2005-2009 omits TOOLWINDOW; WDA_EXCLUDEFROMCAPTURE :2034 and brain-drain self-capture exclusion ~:1685)
- `ConditioningControlPanel/Services/Subliminal/SubliminalService.cs` :741-747 (TRANSPARENT always, NOACTIVATE toggled for focus-steal feature; capture INCLUSION: deliberately sets WDA_NONE ~:760 so subliminals appear in recordings; the EXCLUDEFROMCAPTURE const at :1047 is declared but never applied)
- `ConditioningControlPanel/Services/KeywordHighlightService.cs` :132-134 (capture exclusion :141/:434)
- `ConditioningControlPanel/Services/BlinkTrainerService.cs` :688-690
- `ConditioningControlPanel/Services/Session/SessionEngine.cs` :1412-1414 (corner GIF overlay; omits NOACTIVATE)

TOOLWINDOW|NOACTIVATE|TRANSPARENT without explicit LAYERED:
- `ConditioningControlPanel/Services/Subliminal/BouncingTextService.cs` :647-651
- `ConditioningControlPanel/Services/Tracking/GazeDebugCursorService.cs` :385
- Chaos overlays (~14 files): `ChaosFxWindow.cs` :184, `ChaosFlashOverlay.cs` :193,
  `ChaosGifCascadeOverlay.cs` :317, `ChaosSkiaFxOverlay.cs` :632, `ChaosPopText.cs` :206,
  `ChaosAnnouncerOverlay.cs` :311, `ChaosFieldFxOverlay.cs` :441,
  `ChaosCursorGlowOverlay.cs` :174, `ChaosWaveTimerOverlay.cs` :216,
  `ChaosEStimOverlay.cs` :288, :482, `ChaosVibeTrailOverlay.cs` :269,
  `ChaosEffectBannerOverlay.cs` :214, `ChaosDvdHostOverlay.cs` :134,
  `ChaosBubbleHostOverlay.cs` :182

## WPF head: dynamic togglers

- `ConditioningControlPanel/Services/Flash/FlashService.cs` `ApplyClickability` :2256-2272
  (flips WS_EX_TRANSPARENT per spawn on recycled windows; NativeMethods consts :2656-2670;
  HideFromAltTab :2359-2361)
- `ConditioningControlPanel/Chaos/ChaosOverlayWindow.xaml.cs` `SetClickThrough`/`ApplyExStyles`
  :1060-1080 (play=click-through, draft/results=interactive; BringToFront focus steal :1084-1095)
- `ConditioningControlPanel/Chaos/ChaosDvdOverlay.cs` :480-483 (clickable parameter)
- `ConditioningControlPanel/Services/BubbleService.cs`: per-bubble pooled shells,
  `HideFromAltTab` rebuilds ex-style from a known base on every (re)show :4551-4569,
  `MakeCorpseClickThrough` mid-death-animation :4538-4549

## WPF head: shared-host + global hook

- `ConditioningControlPanel/Services/Input/GlobalMouseHook.cs` :25-32, :56-74
  (WH_MOUSE_LL; Func<Point,bool> callbacks on hook thread; return true = swallow)
- `ConditioningControlPanel/Services/BubbleService.cs` `OnSharedHostLeftDown` :542-567
  (hit-test ChaosClickDiscsSnapshot; swallow on hit EXCEPT hold-to-defuse :563-566;
  ripple snapshot `ChaosBubbleCentersSnapshot` :128)

## WPF head: interactive topmost (NO TRANSPARENT, absorb clicks without focus)

- `ConditioningControlPanel/Chaos/ChaosBackdropService.cs` :478-486
- `ConditioningControlPanel/Chaos/ChaosTunnelService.cs` :30-31, :557-565
- `ConditioningControlPanel/Chaos/ChaosToyButtonWindow.cs` :192-200
- `ConditioningControlPanel/Chaos/ChaosUnlockCardOverlay.cs` :481-482
- `ConditioningControlPanel/Chaos/ChaosBoonBarOverlay.cs` :256-258 (hover tooltips wanted)
- `ConditioningControlPanel/Chaos/ChaosHudWindow.xaml.cs` :866-875 (relies on WPF per-pixel
  alpha-0 pass-through; Avalonia does NOT have this)

## WPF head: topmost management

- `ConditioningControlPanel/Chaos/ChaosWindowZ.cs` :9-18 (contract header), :20-114
- `ConditioningControlPanel/Services/Video/VideoService.cs` :1256-1263, :2903-2922
  (MakeNonActivating during chaos), :3236-3244 (attention target re-asserts HWND_TOPMOST ~32ms)
- `ConditioningControlPanel/Windows/LockCardWindow.xaml.cs` :153, :244
- `ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Windowing.cs` :448-466
  (TOOLWINDOW toggle + SWP_FRAMECHANGED), :1249-1280 (position lerp, ReassertTopmost)

## Avalonia head

- `ConditioningControlPanel/CCP.Avalonia/Chaos/ChaosWin32Helper.cs` :12-39
  (shared `ApplyOverlayExStyles(window, transparent)`; ~20 callers from Opened handlers;
  interactive callers pass false: ChaosBackdropService :491, ChaosUnlockCardOverlay :235,
  ChaosToyButtonWindow :226, ChaosHudWindow :662; dynamic: ChaosDvdOverlay :331 passes !clickable)
- `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorWindow.axaml.cs`
  `ApplyNativeTransparency` :84-120 (LAYERED|TRANSPARENT|TOOLWINDOW|NOACTIVATE +
  SWP_FRAMECHANGED, re-applied on Opened/Activated/WindowState; comments :74-83 foreground
  trap, :106-113 SetWindowSubclass ban)
- `ConditioningControlPanel/CCP.Avalonia/Chaos/ChaosOverlayWindow.axaml.cs` `SetClickThrough`
  :971, `ApplyExStyles` :993-1006, Topmost pulse :986-987
- `ConditioningControlPanel/CCP.Avalonia/Platform/AvaloniaMouseHook.cs` :29-61
  (WH_MOUSE_LL, Windows-only; always CallNextHookEx :112 - CANNOT swallow)
- `ConditioningControlPanel/CCP.Avalonia/Services/AvaloniaBubbleService.cs` :320-364
  (hook -> Dispatcher.UIThread.Post -> BubbleLayer.HitTest; hook refcounting :394-419)
- `ConditioningControlPanel/CCP.Avalonia/Services/Flash/AvaloniaFlashService.cs` :117-121,
  :261-288 (hook only when FlashClickable; FlashLayer.HitTest)
- `ConditioningControlPanel/CCP.Avalonia/Compositor/Layers/FlashLayer.cs` :176-184 (HitTest),
  `BubbleLayer.cs` :130-146 (disc HitTest)
- `ConditioningControlPanel/CCP.Avalonia/Chaos/AvaloniaChaosWindowZ.cs` :22-45
  (RaiseTopmost pulse + SetWindowPos; RaiseAboveVideo)
- `ConditioningControlPanel/CCP.Avalonia/Chaos/AvaloniaBubbleWindow.Windows.cs` :23-32, :60-67
  (WM_MOUSEACTIVATE subclass - the remaining SetWindowSubclass risk; TOOLWINDOW|NOACTIVATE|LAYERED
  without TRANSPARENT; per-pixel TODO :50-52)
- `ConditioningControlPanel/CCP.Avalonia/AvatarTube/AvatarTubeWindow.Windowing.cs` :147-152,
  :350-362, :386-387, :403-406
- `ConditioningControlPanel/CCP.Avalonia/Services/KeywordTriggers/AvaloniaKeywordHighlightService.cs`
  :150, :154, :247-256 (ex-styles + WDA_EXCLUDEFROMCAPTURE)
- `ConditioningControlPanel/CCP.Avalonia/Chaos/ChaosBubbleHostOverlay.cs` :10-25 (keep-alive
  contract header), :38-47 (window recipe), :147 (ApplyExStyles)
- Legacy video windows (interactive): `CCP.Avalonia/Services/Video/AvaloniaVideoService.cs`
  :1015-1018, :1485-1488, :1617-1619

## Seam

- `ConditioningControlPanel/CCP.Core/Platform/IOverlaySurface.cs` (SetClickThrough contract)
- `ConditioningControlPanel/CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs` :30-34 (no-op)
- `ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs` :23-39
- `ConditioningControlPanel/CCP.WindowsOnly/WpfOverlaySurface.cs` :36-52
- `ConditioningControlPanel/CCP.Avalonia/Platform/AvaloniaPlatformCapabilities.cs` :42
  (SupportsClickThrough = IsWindows)
