# Rework: scope the below-video pin the way upstream scopes it

Scaffolding only. Removed before the final report.

## Verified against WPF (read-only)

- `Services/Notifications/OverlayService.cs:2793-2801` — `ReassertZOrder` iterates exactly
  `_pinkFilterWindows`, `_spiralWindows`, `_brainDrainBlurWindows`; `:2807-2818` adds compositor
  hosts. Flash and bouncing text are not in the loop.
- `Services/Flash/FlashService.cs:203-224` — `RaiseAllToFront`, "the top attention layer by
  design", force-raises every legacy flash window with NO video test (`ForceTopmost`, `:3865-3877`,
  a bare `HWND_TOPMOST`). Only the compositor-HOST branch skips while video plays, and `:230-235`
  says why: `OverlayService.ReassertZOrder` pins the HOST below the video.
- `Services/Subliminal/BouncingTextService.cs:390-398` — 500 ms re-assert "when competing with
  flash/video/overlay windows"; `:1048-1052` is a bare `SetWindowPos(HWND_TOPMOST)`.
- `ResolveZOrderAction` (`:2851-2860`): video rule first, then compositor-host yield, then
  `needsPin || force`. Non-force tick with no video and the band held => `None`.

## Design (why it is not a one-liner)

Flash, pink filter and spiral all drive `Effects/OverlaySurfaceSet.OnCadence`, which calls
`IOverlayPresence.Reassert()`. The scope has to be a property of the MODULE, not of the window.

- `Overlay/VideoTopmostAnchor.cs` — the video publishes its handle; `Resolve` is a pure function so
  the decision is drivable without a window.
- `IOverlayPresence.ReassertBelowVideo()` as a DEFAULT interface member delegating to `Reassert()`
  — no churn in ~10 test fakes, and `UnsupportedOverlayPresence` needs no edit.
- `OverlaySurfaceSet` gains `yieldToVideo` (default false). Pink filter and spiral pass true.
  Flash and subliminals do not. `Glyph/Win32GlyphSurface` is NOT touched at all.
- `OnReconcile`: while an anchor is up, re-assert live slots even when the band is HELD — upstream's
  video rule runs ahead of its `needsPin` test, so a clip starting under a live tint is in front
  within one 500 ms tick instead of waiting out the 5 s cadence.
- Video not playing => anchor 0 => `HWND_TOPMOST`, unchanged. Video stops => `Release` before the
  hide; the surface stays in the topmost band and retakes the top on its next cadence tick.

## Reproduction that survives

Real-desktop facts driving the REAL modules against the REAL window manager, using the
pre-existing `OverlayWindowProbe` (`ReadZOrder`, `ScratchWindow`) plus an `IsAbove` helper taken
from the refused branch: same `Win32OverlayPresence` class, same real anchor window, pink filter
parks BELOW and flash stays ABOVE.

## Findings to report

- The refused lane's "HWND_TOPMOST is the sole hWndInsertAfter in the client" is false. Re-derived:
  `Features/Chaos/ChaosTunnelService.cs:334` passes a window handle, `ChaosTunnelWindow.cs:161`
  passes `HwndBottom`. Every other real call site passes `HwndTopmost`.
- `client/docs/window-behavior-manifest.md` §8.3 cites source line numbers my edits move.
