# E-Stim arc visual slice (Q10b) - ready-to-execute spec

Status: ✅ **DONE 2026-07-05 (owner-authorized). DO NOT RE-EXECUTE THIS SPEC.**
The arc now renders through the compositor layer `ChaosEStimArcLayer` (Z=125), NOT the
`ChaosEStimOverlay.Strike` / `ChaosSkiaFxOverlay.Strike` window renderers referenced in the
snippets below — **those window classes have been DELETED**. Wiring landed as:
`BubbleEngine.EStimBurstAt` emits `(fromPx -> victim CenterPx)` bolts via an authorized
`onEStimArc` callback -> head `OnEStimArc` -> `ChaosEStimArcLayer.Strike` + a throttled
`estim_zap` cue. The code snippets below are kept ONLY as the historical WPF-parity record; the
Avalonia class/method names in them (`ChaosEStimOverlay`, `ChaosSkiaFxOverlay`) no longer exist.

--- ORIGINAL (now-executed) SPEC BELOW ---

This slice edited the FROZEN `BubbleEngine` (a sanctioned edit, same class as the VideoLayer
teardown fix and the fps-timing change). It was a mechanical task: every decision below made
and cited.

Scope of THIS slice: emit E-Stim arc bolts + the `estim_zap` cue from the ONE
cleanly-ported discharge path (`EStimBurstAt`, the Electrified-Rabbits free
synergy). The charged-pop arc and the residue field are DEFERRED (see the Deferred paths section) because their Core mechanics diverged from WPF
and need separate reconciliation.

---

## WPF ground truth

`Services/BubbleService.cs` `EStimBurstAt(Point fromPx, int maxArcs, double rangePx)`
at lines 407-441:

- Builds `pool` = nearby chainable bubbles within `rangePx`, sorted by distance.
- For each of the nearest `maxArcs`: `bolts.Add((fromPx, target.CenterPx));` then
  schedules a staggered pop.
- After the loop:
  ```csharp
  if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Strike(bolts); else ChaosEStimOverlay.Strike(bolts);
  var now = DateTime.UtcNow;
  if ((now - _lastBurstZap).TotalMilliseconds >= 140)
  {
      _lastBurstZap = now;
      PlayChaosCue("estim_zap", 0.45f);
  }
  ```
- Key: `Strike(bolts)` fires on EVERY burst; the `estim_zap` audio is throttled to
  >=140ms via `_lastBurstZap` (a mowing rabbit fires bursts per victim, so the crack
  must not machine-gun). The visual is NOT throttled.

Bolt = `(fromPx, target.CenterPx)`. `CenterPx` = bubble center in pixels.

## Core current state (the frozen file to edit)

`CCP.Core/Services/Chaos/BubbleEngine.cs`:

- `EStimBurstAt(Point fromPx, int maxArcs)` at line ~1556: builds `pool` (nearby
  chainable, sorted by distance), then `for (...) PopBubble(pool[i].Id);`. It does
  NOT build bolts and does NOT invoke any callback. Comments at :1542/:1552-1555
  explicitly defer "Strike/zap FX" as head-side follow-ups.
- Called from :1544 (Electrified Rabbits: `if (Knobs.ElectrifiedRabbits) EStimBurstAt(victimPx, ESTIM_ARCS_PER_POP);`).
- `CenterPx(BubbleState b)` helper already exists (returns the pixel center).
- `BeginChaosMode(...)` takes the full behavioral callback set; `EndChaosMode`
  clears them (mirror the existing `_chaosOn*` field lifecycle).

## Head renderer + audio (already present - do not create)

`CCP.Avalonia/Chaos/ChaosSkiaFxOverlay.cs`:
- `public static bool Enabled` (:32, gated by `AppSettings.ChaosSkiaFxEnabled`, default true).
- `public static void Strike(IReadOnlyList<(Point From, Point To)> boltsPx)` (:60).

`CCP.Avalonia/Chaos/ChaosEStimOverlay.axaml.cs`:
- `public static void Strike(IReadOnlyList<(Point From, Point To)> boltsPx)` (:73).

Audio: `AvaloniaChaosSfx.Play("estim_zap", 0.45f)` (same pattern as the many
`AvaloniaChaosSfx.Play(...)` cues already wired in `AvaloniaHeadStubs.cs`).

---

## The change (2 files)

### 1. `CCP.Core/Services/Chaos/BubbleEngine.cs` (FROZEN - authorized only)

- Add a field beside the other `_chaosOn*` callbacks:
  ```csharp
  private Action<IReadOnlyList<(Point From, Point To)>>? _chaosOnEStimArc;
  ```
- Add an optional param to `BeginChaosMode(...)`:
  ```csharp
  Action<IReadOnlyList<(Point From, Point To)>>? onEStimArc = null,
  ```
  and assign `_chaosOnEStimArc = onEStimArc;` where the other callbacks are assigned.
- Clear it in `EndChaosMode` alongside the other `_chaosOn* = null;` lines.
- In `EStimBurstAt`, build the bolt list while popping and invoke the callback AFTER
  the pop loop (mirror WPF: visual every burst, no throttle here):
  ```csharp
  var bolts = new List<(Point From, Point To)>(Math.Min(pool.Count, maxArcs));
  for (int i = 0; i < pool.Count && i < maxArcs; i++)
  {
      bolts.Add((fromPx, CenterPx(pool[i])));
      PopBubble(pool[i].Id);
  }
  if (bolts.Count > 0) _chaosOnEStimArc?.Invoke(bolts);
  ```
  (Replaces the current bare `for (...) PopBubble(pool[i].Id);`.)

### 2. `CCP.Avalonia/Services/AvaloniaHeadStubs.cs` (head)

- In the `BeginChaosMode(...)` call, add the argument (next to the other callbacks):
  ```csharp
  onEStimArc: OnEStimArc,
  ```
- Replace the `:474` marker line with a private handler + a >=140ms audio throttle
  field (mirror WPF `_lastBurstZap`):
  ```csharp
  private DateTime _lastEStimZap;
  private void OnEStimArc(IReadOnlyList<(global::Avalonia.Point From, global::Avalonia.Point To)> bolts)
  {
      if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Strike(bolts); else ChaosEStimOverlay.Strike(bolts);
      var now = DateTime.UtcNow;
      if ((now - _lastEStimZap).TotalMilliseconds >= 140)   // WPF BubbleService.cs:437
      {
          _lastEStimZap = now;
          AvaloniaChaosSfx.Play("estim_zap", 0.45f);        // WPF PlayChaosCue("estim_zap", 0.45f)
      }
  }
  ```
  Verify the Core bolt tuple type matches the head signature (Core builds
  `System.Drawing`-free `Point` - confirm the Core `Point` is the shared type the
  head's `Strike` already accepts; both overlays take `(Point From, Point To)`).
  If the Core `Point` differs from `Avalonia.Point`, project each bolt at the seam.

## Deferred paths (NOT in this slice)

- Charged-pop arc (WPF `BubbleService.cs:340-396`): WPF decrements the charge, arcs,
  and calls `Strike(bolts)` + `_chaosOnEStimArc?.Invoke(chargesLeft)`. The Core charged
  discharge path must be located and confirmed before emitting bolts there.
- Residue field (WPF `:1419` single-bolt pop): Core `_residues` (:1453) DIVERGED - it
  does fuse-acceleration + velocity jitter, not single-bolt pop. Emitting the residue
  Strike requires reconciling that mechanic first.

## Verification

- `bash ConditioningControlPanel/tools/run-gates.sh` - expect slnf 0, WPF 0,
  Core 540 (no test-floor change; this is head/engine wiring, not new pure logic),
  smoke 44 tabs / 0 unhandled.
- Manual: enter chaos, acquire the Electrified Rabbits boon, let a rabbit mow a
  cluster - arc bolts should draw from each mowed victim into nearby bubbles and the
  `estim_zap` crack should fire (throttled, not machine-gunning).
- One commit, `--no-verify`, WPF cites in the message, update the Q10b row to DONE
  (EStimBurstAt path) with the charged-pop + residue paths still listed as deferred.
