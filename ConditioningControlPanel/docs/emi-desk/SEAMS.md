# EMI Desk - seams for B2 (ring) and B3 (glass / offers / lines)

B1 owns the window, the face, the chains, summon/dismiss, the dock chip, the hotkey, the mute
arbitration, the settings and the state file. **B2 and B3 should not need to edit a single B1
file.** Everything they hook is either a `partial void ...Core(...)` on `EmiDeskWindow`, a public
event on `EmiDeskService`, or a host panel handed over as a `Panel`.

Add new files beside the existing ones:

| Chunk | File to add |
|-------|-------------|
| B2 | `Windows/EmiDesk/EmiDeskWindow.Ring.cs` |
| B3 | `Windows/EmiDesk/EmiDeskWindow.Glass.cs`, `Windows/EmiDesk/EmiDeskWindow.Bubble.cs` |

All three are `namespace ConditioningControlPanel;` and `public partial class EmiDeskWindow : Window`.

> **NAMESPACE TRAP.** Everything under `Windows/` lives in the FLAT `ConditioningControlPanel`
> namespace. A `ConditioningControlPanel.Windows.*` namespace shadows the WinRT `Windows` root and
> breaks `Services/ScreenOcrService.cs` with a CS0234 that names a file you never touched. The same
> comment sits at the top of `DescentFuseWindow.xaml.cs`. Do not "tidy" it.

---

## 1. The partial-method seams on `EmiDeskWindow`

Declared in `Windows/EmiDesk/EmiDeskWindow.xaml.cs` (region `SEAMS`). Partial methods return
`void`, may take `ref`, and compile away entirely when nobody implements them, so B1 builds and runs
today with none of these present.

```csharp
partial void OnBodyClickedCore(ref bool handled);
partial void OnGlassClickedCore(ref bool handled);
partial void OnGlassLiveQuery(ref bool live);
partial void OnTearDownCore();
partial void OnBubbleTextCore(string? text);
partial void OnChainFxCore(string kind);
partial void OnBodyMoveCore(string move, ref bool handled);
```

### `OnBodyClickedCore(ref bool handled)` - B2
Fires on mouse-up over her body when the gesture was a click, not a drag (movement stayed under
`DragThresholdPx` = 6 px) and not a resize, and when `InputLocked` is false. Set `handled = true` to
claim it. This is where the ring opens and closes. Unclaimed clicks do nothing at all.
Ordering: a glass hit is offered first (below); the body click only runs if the glass declined.

### `OnGlassClickedCore(ref bool handled)` - B3
Fires on the same mouse-up when the point landed inside `GlassRect` **and** `OnGlassLiveQuery` said
a channel is up. Set `handled = true` to consume it; leave it false and the click falls through to
`OnBodyClickedCore`.

### `OnGlassLiveQuery(ref bool live)` - B3
The only question B1 asks about glass state. Set `live = true` while a channel is on screen.
Consulted in two places: before routing a click to `OnGlassClickedCore`, and before an idle beat
plays (she does not blink-cycle over a live channel).

### `OnTearDownCore()` - B2 and B3
Called once, on the dispatcher, **before** the dismiss outro starts and again on `ShutDown()`. Close
the ring window, kill the channel, cancel any open ask. Must be idempotent: it can be called when
nothing is open.

### `OnBubbleTextCore(string? text)` - B3
Every chain frame that carries a bubble instruction calls this; `null` clears. Because B1 drives it
from the chain player, the locked `.` / `..` / `...` typing cadence and the `SayHoldMs` dwell come
for free. Render into `BubbleHost`.

### `OnChainFxCore(string kind)` - B2 / B3
A chain asked for a one-shot particle burst. `kind` is the chain's own fx token (`hearts`, `spark`,
`tears`, `storm`, `bang`, ...). B1 draws **nothing** for these; the summon smoke and the dismiss
sparkles are separate and live in `EmiDeskWindow.Fx.cs`. Draw into `OverlayHost`.

### `OnBodyMoveCore(string move, ref bool handled)` - optional
A chain asked for a one-shot body move (`bounce`, `nod`, `droop`, `shiver`, `thud`). B1 runs the
canonical five itself; set `handled = true` only to override one or to add a new token.

---

## 2. The hosts and geometry `EmiDeskWindow` hands over

```csharp
public Panel GlassHost   { get; }   // GlassCanvas - a Canvas laid over the screen area of the body
public Panel OverlayHost { get; }   // OverlayCanvas - full-window, above everything, for ring + fx
public Panel BubbleHost  { get; }   // BubbleCanvas - above the body, below the overlay
public EmiFace Face      { get; }   // the live face element (its DPs are bindable - the dock does)

public bool   InputLocked { get; }  // true during summon / dismiss; honour it, no ring, no glass
public bool   ChainLive   { get; }  // a chain is playing right now
public bool   Transiting  { get; }  // mid summon or dismiss
public double BodyWidth   { get; }  // current body width in DIPs, 152..420

public Rect  GlassRect            { get; }  // the screen rect in WINDOW coords (DIPs)
public Rect  BodyScreenRect       { get; }  // the body in PHYSICAL PIXELS, for a sibling window
public Point RingAnchorScreenPoint{ get; }  // PHYSICAL PIXELS, centre X / 48% height
```

**`GlassCanvas` is `IsHitTestVisible="False"` and must stay that way.** A hit-testable overlay eats
the drag gesture, and she stops being draggable by her own screen. The glass click is resolved
geometrically in the body's mouse-up handler instead (`GlassRect.Contains(p)`).

**The ring needs its own window.** `EmiDeskWindow` carries only `OverlayPad` = 120 DIPs of air
around the body, which is not enough for a full ring at 420 px. Build the ring as a sibling
`Topmost` / `ShowActivated=false` layered window and place it off `RingAnchorScreenPoint` and
`BodyScreenRect`. **Both are PHYSICAL PIXELS, not DIPs** - this is the coordinate trap that ate the
gaze work; convert with
`PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11`, never assume 1.0.

Keep the ring following her: subscribe to `Moved` and `Resized` on the window.

---

## 3. Events

On `EmiDeskWindow`:

```csharp
public event EventHandler?         PointerActivity;  // any press / drag / resize / pet-arming hover
public event EventHandler<double>? Resized;          // new body width in DIPs, after layout
public event EventHandler?         Moved;            // once per drop, not per mouse-move
```

On `EmiDeskService` (`App.EmiDesk`):

```csharp
public event EventHandler<bool>?      OutChanged;     // she was summoned (true) / dismissed (false)
public event EventHandler<EmiMoment>? MomentFired;    // Fire(momentId, ctx) - B3 picks the line here
public event EventHandler<string>?    TargetOpened;   // NoteOpen(targetId) - an offer was taken
public event EventHandler?            AvatarSpeaking; // the tube just spoke (mute-arbitration signal)
```

`Fire` and `NoteOpen` are deliberate **stubs** in B1: they log at Debug and raise. B3 owns the line
picking, the pools, `seenByPool`, `recentIds`, `ignoreStreak` and `bedtimeUntil` - the fields are
already carried in `EmiState` and are already persisted, so B3 adds behaviour, not storage.

`Summon()` fires the moment `deskFirstBoot` the very first time ever, `deskSummon` after that.
`Dismiss()` fires `deskDismiss`. Those three are the only moments B1 raises.

---

## 4. Playing to her from a later chunk

```csharp
public void DrawFace(string? text, bool small = false, bool flat = false);
public void SetPose(string? frame);                       // "idle", "smug", "pet", "sway1", ...
public void PlayChain(string chainId, Action? done = null, string? bodyFrameOverride = null);
public void PlayChain(EmiChain? chain, Action? done = null, string? bodyFrameOverride = null);
public void Say(string? line, string reactionFace = "^_^", Action? done = null);
public void CancelChain();
public void RestartIdleBeats();
public void StopIdleBeats();
```

`EmiChains.MakeSay(line, reactionFace)` builds the say chain if you want to compose one; `Say` is
the shortcut. Dwell is `EmiChains.SayHoldMs(len)` = `max(4050, 1890 + 61 * len)` ms, ported verbatim
from `chains.js`. Chain ids live in `EmiChains.Chains`; `EmiChains.Get(id)` is the lookup.

Do not touch `EmiChains.Player` directly - the window owns exactly one and cancels it on teardown.

---

## 5. State

`Services/EmiDesk/EmiState.cs`, `%LOCALAPPDATA%\ConditioningControlPanel\emi-desk.json`.
`EmiState.Current` loads lazily and tolerates a corrupt file (it starts fresh rather than throwing).
`SaveSoon()` debounces 500 ms on the dispatcher; `SaveNow()` writes temp-then-move.

Fields already carried for B3: `Pins`, `Usage` / `UsageAt`, `SeenByPool`, `RecentIds`,
`IgnoreStreak`, `BedtimeUntil`, `FirstBootSeen`, plus the window placement
(`WinLeftPx` / `WinTopPx` in physical px and the `Monitor` device name; the width is NOT here, it
lives in `AppSettings.EmiDeskWidth` and only there).
`NoteUsage(id)` and `NoteLine(pool, id)` are the two helpers.

---

## 6. House rules that apply to B2 and B3 too

- Serilog prefix `"[EmiDesk]"` on every line.
- try/catch inside **every** timer tick and async continuation, and
  `if (Application.Current?.Dispatcher == null) return;` before touching UI from one.
- No new NuGet packages.
- Never modify `Resources/web/arcademy/**`. The face metrics, chains and body art are ported from
  there; if the web changes, port the change, do not reach across.
- No em-dashes in anything the user can read.
- Honour `InputLocked`, and honour `App.Settings.Current.EmiDeskGlass` before showing any glass.

---

## 7. Seams added by chunk B3 (the voice, the offers, the glass)

B3 owns `EmiDeskWindow.Bubble.cs` and `EmiDeskWindow.Glass.cs`, plus `EmiLineEngine`, `EmiOffers`,
`EmiChannels` and `EmiGifRain`. It added four things other chunks touch.

### 7.1 `partial void OnRingOpenQuery(ref bool open)`  (B2 implements)

Declared in `EmiDeskWindow.xaml.cs` next to `OnGlassLiveQuery`. B3 asks it before letting the glass
wander off to a channel: an open ring is the loudest "the user is mid-thought" signal there is, and
a channel that flips up behind an open ring is a channel nobody sees.

Unimplemented it leaves `open` false, which is the correct default while there is no ring.

### 7.2 `partial void OnReadyCore()`  (B3 implements, B2 may add to it)

Called at the end of the window constructor, after the first `ApplyBodyWidth`. The counterpart to
`OnTearDownCore`: start ambient loops here, never in a static initialiser, so a second widget can
never inherit the first one's timers. B3 starts the glass idle watch from it.

### 7.3 Ring stubs on `EmiDeskService`  (B2 fills the bodies)

```csharp
public bool IsTargetAvailable(string? targetId);   // currently always false + a Debug line
public void OpenTarget(string? targetId);          // currently an Information line, no-op
public void PinTop(string? targetId);              // currently an Information line, no-op
```

`EmiOffers.EffectFeasible` calls `IsTargetAvailable` for every `open:<id>` and `pinTop:<id>` effect,
so with the stubs in place those offers are dropped at DRAW time and never shown. That is the safe
failure: a chip that does nothing is worse than an offer that never appears. B2 replaces the three
bodies and every call site is already correct. `pinTop:` additionally refuses a target already in
`EmiState.Current.Pins`.

### 7.4 The window's voice API  (B3 owns; call it, do not reimplement it)

```csharp
public void SpeakLine(LineDraw line);   // a drawn line, on the locked . / .. / ... cadence
public void HoldFace(LineDraw line);    // a HOLD row: a face, held, no bubble
public void ShowAsk(AskDraw ask);       // put a question up and WAIT
public void CancelAsk(string why);      // any non-chip ending; counts as an ignore
public bool AskLive { get; }            // a question is up and unanswered
public void SnapToNearestCorner();      // used by the shrink effect
```

Nothing outside `EmiDeskService.Fire` should call the first three. `Fire(momentId, ctx)` is the only
entry point: it raises `MomentFired` for everyone, and then, only while she is out, draws once and
routes the result here. `dismissed` and `appClosing` fire and count but never reach a bubble.

The engine's own surface is `EmiLineEngine.Instance.Draw(momentId, ctx)` /
`DrawAsk(momentId, ctx)` / `Ack(id)`; `Ack` is called by the window when the line actually reaches
the screen, so a line nobody saw never burns its cooldown.

### 7.5 Two things B3 relies on that are easy to break

- `GlassCanvas`, `OverlayCanvas` and `BubbleCanvas` are `IsHitTestVisible="False"`. Keep them that
  way. The glass tap is resolved geometrically in `OnBodyMouseUp` against `GlassRect`, and the ask
  chips open `BubbleHost.IsHitTestVisible` for exactly as long as there are chips to click. A
  hit-testable overlay would eat her drag.
- `EmiState.Limits` and `EmiState.SummonCount` (added by B3) back the lines file's `limit` blocks and
  the "no offers before the third summon" rule. `EmiState.NoteSummon()` is what counts a summon.
