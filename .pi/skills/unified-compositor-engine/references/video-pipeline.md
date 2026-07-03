# UCE video pipeline reference

The missing piece of the UCE is proven video rendering. This file collects the frame-delivery
patterns and the traps already discovered. Verify any API named here against current v12
sources before relying on it (see `avalonia-research` skill); this file records project
knowledge, not gospel.

## Frame source: LibVLC memory callbacks (interim decoder)

Keep LibVLC for decoding but stop using `VideoView` for the UCE path. LibVLCSharp exposes
memory callbacks that hand you raw frames:

- `MediaPlayer.SetVideoFormat("RV32", width, height, pitch)` (or `SetVideoFormatCallbacks`
  to negotiate size from the media).
- `MediaPlayer.SetVideoCallbacks(lockCb, unlockCb, displayCb)`:
  - `lock`: return a pointer to your pixel buffer (pin it; allocate once, reuse).
  - `display`: the frame is complete; publish it to the layer.

Frames arrive on a LibVLC thread. Publish with a swap-under-lock, never by drawing:

```csharp
// producer (LibVLC display callback)
var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
var img = SKImage.FromPixelCopy(info, buffer, stride); // COPIES - safe to publish
// (SKImage.FromPixels(pixmap, releaseProc) wraps WITHOUT copying; only use it with
//  explicit double-buffering, never on a live decoder buffer)
lock (_frameLock) { _pending?.Dispose(); _pending = img; }

// consumer (VideoLayer.Render on the engine tick)
lock (_frameLock) { if (_pending != null) { _current?.Dispose(); _current = _pending; _pending = null; } }
if (_current != null) canvas.DrawImage(_current, bounds.ToSKRect(), _samplingLinear);
```

## Known traps (measured in this repo)

1. **`SKCanvas.DrawBitmap(SKBitmap, ...)` allocates an `SKImage` per call.** Measured
   ~480 MB/s of garbage in `VideoLayer`. The fix (persistent cached `SKImage`) is plan
   Phase D and has NOT landed yet: `VideoLayer.cs` still allocates an `SKBitmap` per frame
   and calls `DrawBitmap` per render. Do not copy the current code as a pattern.
2. **Never wrap a live decoder buffer without copying**: the buffer may be overwritten
   mid-draw by the decoder. Copy on publish (`SKImage.FromPixelCopy`, or double-buffer
   and use `SKImage.FromPixels(pixmap, releaseProc)` which wraps zero-copy). Never share
   one mutable buffer between decoder and canvas without a swap protocol.
3. **Dispose discipline:** every swapped-out `SKImage` must be disposed on swap, not
   finalized; leaking them exhausts native memory long before managed GC notices.
4. **Audio is separate.** The UCE path currently lacks volume/device/mute wiring; audio
   still flows through LibVLC/`IAudioPlayer`. When testing the video layer, verify the
   audio controls in the Audio panel still work, or you are regressing gap items already
   tracked in `unified-compositor-engine-plan.md`.
5. **Attention checks are tied to the legacy `VideoOverlayWindow`.** Porting mandatory
   video to `MandatoryVideoLayer` without re-homing attention checks silently disables
   an entire feature. Grep for attention-check usages before flipping any default.
6. **Multi-monitor:** the legacy `AvaloniaMultiMonitorVideoService` drives secondary
   monitors with real windows. The UCE engine already creates one `CompositorWindow`
   per monitor; the video layer must render per-window, not assume the primary.

## Diagnosis workflow for "video layer shows nothing"

1. Run the Windows head and grep `ccp-run.log` for `VideoLayer:` and `CompositorEngine`
   lines: is the layer registered, is it `IsActive`, are frames arriving (frame counter
   logs), is `Render` being called?
2. If frames arrive but nothing shows: check z-order (Video=10 is bottom; is an opaque
   layer above it?), check the `SKImage` color type (BGRA8888 vs RGBA mismatch renders
   black), check bounds mapping (pixels vs DIPs).
3. If no frames arrive: verify `SetVideoFormat`/`SetVideoCallbacks` were called before
   `Play()`, and that the media actually decodes (play the same file through the legacy
   path).
4. Compare against the WPF head's `VideoService` behavior for timings and events
   (`VideoAboutToStart`, `VideoStarted`, `VideoEnded` equivalents must still fire).

## Target end-state (Phase E and beyond)

Only after video parity is proven by running: flip the default to UCE, rehome the ~9
`IMultiMonitorVideoService` references, then delete the legacy windows. A future
milestone may replace LibVLC decoding with platform decoders (MediaFoundation /
AVFoundation / VAAPI via FFmpeg) feeding GPU-backed `SKImage`s; that is out of scope
until the interim path is at parity.
