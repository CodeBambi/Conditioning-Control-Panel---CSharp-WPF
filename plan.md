# Checkpoint plan — "does the video surface swallow the user's clicks"

## What upstream actually does (source, not the board's inference)

Every mandatory-video window upstream creates is an OPAQUE, TOPMOST, ACTIVATED WPF window that
SWALLOWS every mouse press at the window level. Three render paths, one policy:

- `Services/Video/VideoService.cs:2619-2636` — `WindowStyle.None`, `Topmost = true`,
  `Background = Brushes.Black`, `ShowActivated = withAudio`. No `AllowsTransparency`, no
  click-through, no input region.
- `:2862-2874` — a `Fill = Brushes.Transparent`, `IsHitTestVisible = true` rectangle above the
  video surface, comment verbatim: "This overlay catches all clicks before they reach the video
  surface".
- `:2894-2907` — `win.PreviewMouseDown` with `e.Handled = true`, comment verbatim: "it swallows
  every one of them".
- `:4162-4166` (MediaElement fallback) and `:4226-4230` (mirror) do the same `e.Handled = true`.
- `:2920-2921` — `if (withAudio) win.Activate();` then `DisableChildWindowInput(win)`
  (`:7264-7295`), which `EnableWindow(child, false)`s LibVLC's native child HWNDs so the mouse
  reaches the TOP-LEVEL window and not the renderer.
- `:7205-7255` `PreventClickRaise` denies only the z-order raise (`WM_MOUSEACTIVATE` →
  `MA_NOACTIVATE`), and its own comment says this is "WHILE KEEPING the mouse message — so the WPF
  click overlay + attention targets still register".

**The board's hint is wrong and source is trusted over it.** Census #21 (strict lock) is about
DISMISSABILITY, not clicks: `:4274` vetoes `Closing`, `:4276-4306` blocks the panic key / Alt+F4,
and the non-strict branch `:4328-4345` consumes ESC as "dismiss this video". Both branches assume
the window already OWNS the input; the dial only decides whether that ownership can be escaped.
Upstream's default IS a click sink.

## Two more places the board row is contradicted by source

1. **It is not fullscreen.** `Effects/VideoSurfacePresenter.cs:230-233` +`:507-522` place
   `0.55 x 0.42` of the primary display, centred — recorded as D123, whose stated reason is that "a
   fullscreen topmost surface the user cannot dismiss is the one shape this port must not ship
   while it has no panic key".
2. **The input policy is not entirely unasserted.** `VideoCapabilityTests.cs:193-195` already
   reads `WS_EX_TRANSPARENT` back off the OS and requires it absent, with a one-line reason. What
   is missing is (a) the declaration at the CREATION site and (b) an OUTCOME pin — the flag
   read-back is the tool `overlay-clickthrough/SKILL.md:48` and `Win32OverlayPresence.cs:504-511`
   both say cannot carry this claim alone.

## Decision: (a) — declare the sink

Port the outcome upstream ships. Do not add `WS_EX_TRANSPARENT`: a click-through mandatory video
would be a picture the pointer falls through, which is neither upstream's behaviour nor honest
about what is in front of the user.

## Work

1. `Video/Win32VideoPresence.cs` `EnsureWindow` — declare the omission at the `CreateWindowExW`
   site with the citations above, the way `Pointer/Win32PointerSurface.cs:850-852` and
   `Input/Win32InputPresence.cs:1097-1099` do. No behaviour change.
2. `client/tests/CcpClient.Tests/VideoSurfaceObservations.cs` — one new measured run that asks the
   WINDOW MANAGER where a point routes and injects a REAL click at it, with a click-counting
   scratch window underneath as the application-that-would-have-got-it.
3. New `client/tests/CcpClient.Tests/VideoInputRoutingTests.cs` — the facts, in
   `RealDesktopCollection` via `RealDesktopFacts`.
4. `VideoCapabilityTests.cs:193-195` — correct the comment so the flag read-back stops reading as
   the input policy and points at the outcome fact.

Three legs, so "nothing arrived" cannot be satisfied by a target nothing can reach:
before the surface exists the point routes to the scratch target and an injected click ARRIVES;
while the surface is up the point routes to the SURFACE and the same click does NOT arrive;
after `Withdraw` the point routes back to the target and a click arrives again.

Mutation that reds it: add `WsExTransparent` to the `CreateWindowExW` at
`Win32VideoPresence.cs:405`.

## Explicitly not touched

Capture. No `SetWindowDisplayAffinity` exists anywhere in `client/src` and none is added; it is
the owner's (`client/port.txt:35-36`, A-001 unresolved question 3).
