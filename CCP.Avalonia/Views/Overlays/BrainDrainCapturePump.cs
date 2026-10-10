using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using ConditioningControlPanel.Services.Compositor;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/Compositor/BrainDrainCapturePump.cs: the desktop
    /// grab + blur (+ melt warp) that feeds the Brain Drain haze, on a thread of its own (#777: a
    /// desktop blt cannot be time-bounded, so it must never run on the UI thread).
    ///
    /// <para>Threading contract, as WPF: everything native and every Skia object that is written to
    /// lives on this thread. Output crosses one immutable <see cref="SKImage"/> at a time through a
    /// per-monitor slot; the UI thread TAKES it and owns it from then on. Parameters flow the other
    /// way as volatile writes. Shutdown is fire-and-forget: the UI thread never joins this thread.</para>
    ///
    /// <para>Self-capture: the Windows grab is a plain SRCCOPY with NO CAPTUREBLT, which leaves
    /// layered windows out of a desktop DC blt, and every overlay on this head is WS_EX_LAYERED
    /// (Platform/Win32Overlay). So the haze cannot feed back into itself whatever the capture
    /// affinity says.</para>
    ///
    /// <para>The alpha byte (#960 / #975): GDI leaves the fourth byte of a 32bpp BI_RGB pixel
    /// undefined and some drivers leave 0, which a Bgra wrap draws as fully transparent. The capture
    /// is wrapped as Rgb888x (opaque by construction) and <see cref="SwapRedBlue"/> puts the
    /// channels back. Never widen it back to Bgra8888.</para>
    /// </summary>
    internal sealed class BrainDrainCapturePump
    {
        internal const SKColorType CaptureColorType = SKColorType.Rgb888x;

        internal static readonly SKColorFilter SwapRedBlue = SKColorFilter.CreateColorMatrix(new float[]
        {
            0, 0, 1, 0, 0,
            0, 1, 0, 0, 0,
            1, 0, 0, 0, 0,
            0, 0, 0, 1, 0,
        });

        /// <summary>The grab for one monitor: fills <see cref="Bits"/> (32bpp, B G R X, top-down,
        /// <see cref="W"/> x <see cref="H"/>) with a shrunk copy of that monitor. Created, used and
        /// freed on the pump thread.</summary>
        internal interface IGrab : IDisposable
        {
            IntPtr Bits { get; }
            bool Capture();
        }

        /// <summary>Whether this platform has a grab that leaves the app's own overlays out. Windows
        /// only today: an X11 root grab under a compositor returns the haze itself, so it would
        /// blur its own blur (not ported).</summary>
        internal static bool IsSupported => OperatingSystem.IsWindows();

        /// <summary>The grab factory (tests swap it for a painted buffer).</summary>
        internal static Func<PixelRect, int, int, IGrab?> GrabFactory = (bounds, w, h) =>
            OperatingSystem.IsWindows() ? GdiGrab.Create(bounds, w, h) : null;

        private sealed class Slot
        {
            public readonly object Gate = new();
            public SKImage? Pending;                 // pump -> UI hand-off
            public PixelRect Bounds;
            public int W, H;
            public IGrab? Grab;
            public SKSurface? Surface;
        }

        private readonly int _downscale;
        private readonly TimeSpan _interval;
        private readonly bool _melt;
        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new(false);
        private volatile bool _stop;

        private volatile PixelRect[] _requestedScreens;
        private volatile float _sigma;
        private volatile float _meltAmplitude;
        private volatile Slot[] _slots = Array.Empty<Slot>();
        private int _framesPublished;

        public int FramesPublished => Volatile.Read(ref _framesPublished);
        public int SlotCount => _slots.Length;
        public bool Melt => _melt;

        // Pump-thread-only state.
        private readonly SKPaint _blurPaint = new() { ColorFilter = SwapRedBlue };
        private readonly SKPaint _plainPaint = new() { ColorFilter = SwapRedBlue };
        private SKImageFilter? _blurFilter;
        private float _lastSigma = -1f;
        private SKImage? _meltNoiseTile;
        private float _meltTileCoverage;
        private int _meltNoiseShort = -1;
        private SKPaint? _meltPaint;
        private int _errorsLogged;
        private DateTime _nextSlotRetryUtc = DateTime.MinValue;
        private static readonly TimeSpan SlotRetryBackoff = TimeSpan.FromSeconds(1);

        // Noise field constants (WPF, same values).
        private const float NoiseCellFraction = 0.20f;
        private const float NoiseCellAspect = 0.65f;
        private const int NoiseOctaves = 2;
        private const float NoiseSeed = 7f;
        private const int NoiseTilePx = 256;
        private const float NoiseCellsPerTile = 12f;
        private const float MeltDriftPxPerSec = 6f;
        private const float MeltSwayPx = 2.5f;
        private const float MeltSwayRate = 0.55f;   // rad/s

        private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear, SKMipmapMode.None);

        public BrainDrainCapturePump(int downscale, TimeSpan interval, bool melt,
            PixelRect[] screens, float sigma, float meltAmplitude, bool start = true)
        {
            _downscale = Math.Max(1, downscale);
            _interval = interval;
            _melt = melt;
            _requestedScreens = screens;
            _sigma = sigma;
            _meltAmplitude = meltAmplitude;
            _thread = new Thread(Run) { IsBackground = true, Name = "BrainDrain-Capture" };
            if (start) _thread.Start();
        }

        public void SetBlur(float sigma, float meltAmplitude) { _sigma = sigma; _meltAmplitude = meltAmplitude; }

        public void SetScreens(PixelRect[] screens) { _requestedScreens = screens; try { _wake.Set(); } catch { } }

        /// <summary>Fire-and-forget: the thread frees its own handles as its last act.</summary>
        public void Shutdown()
        {
            _stop = true;
            try { _wake.Set(); } catch { }
        }

        /// <summary>UI thread: the newest frame for the monitor at <paramref name="bounds"/>, if the
        /// pump has parked one since the last take. The caller owns the image.</summary>
        public bool TryTakeFrame(PixelRect bounds, out SKImage? fresh)
        {
            fresh = null;
            foreach (var slot in _slots)
            {
                if (slot.Bounds != bounds) continue;
                if (!Monitor.TryEnter(slot.Gate, 2)) return true;   // never wait on the pump
                try { fresh = slot.Pending; slot.Pending = null; }
                finally { Monitor.Exit(slot.Gate); }
                return true;
            }
            return false;
        }

        private void Run()
        {
            var clock = Stopwatch.StartNew();
            try
            {
                while (!_stop)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    try
                    {
                        SyncSlots();
                        CaptureOnce((float)clock.Elapsed.TotalSeconds);
                    }
                    catch (Exception ex)
                    {
                        if (_errorsLogged++ < 5) Log.Debug("BrainDrain capture pump failed: {Error}", ex.Message);
                    }
                    var spent = Stopwatch.GetElapsedTime(t0);
                    var wait = _interval - spent;
                    if (wait < TimeSpan.FromMilliseconds(1)) wait = TimeSpan.FromMilliseconds(1);
                    _wake.WaitOne(wait);
                }
            }
            catch (Exception ex) { Log.Error(ex, "BrainDrain capture pump died"); }
            finally
            {
                try { ReleaseSlots(_slots); } catch { }
                _slots = Array.Empty<Slot>();
                try { _blurFilter?.Dispose(); _blurPaint.Dispose(); _plainPaint.Dispose(); _meltPaint?.Dispose(); _meltNoiseTile?.Dispose(); } catch { }
                try { _wake.Dispose(); } catch { }
            }
        }

        /// <summary>One synchronous pass on the calling thread (tests): build the slots, capture once.</summary>
        internal void RunOnceForTest(float phaseSeconds)
        {
            SyncSlots();
            CaptureOnce(phaseSeconds);
        }

        /// <summary>Tests: free what <see cref="RunOnceForTest"/> built.</summary>
        internal void ReleaseForTest()
        {
            ReleaseSlots(_slots);
            _slots = Array.Empty<Slot>();
        }

        private void SyncSlots()
        {
            var want = _requestedScreens;
            var have = _slots;
            if (want.Length == have.Length)
            {
                bool same = true;
                for (int i = 0; i < want.Length; i++)
                    if (have[i].Bounds != want[i]) { same = false; break; }
                if (same) return;
            }
            if (DateTime.UtcNow < _nextSlotRetryUtc) return;

            ReleaseSlots(have);
            _slots = Array.Empty<Slot>();
            var built = new List<Slot>(want.Length);
            foreach (var bounds in want)
            {
                var slot = CreateSlot(bounds);
                if (slot != null) built.Add(slot);
            }
            _slots = built.ToArray();
            _nextSlotRetryUtc = built.Count == want.Length ? DateTime.MinValue : DateTime.UtcNow + SlotRetryBackoff;
        }

        private Slot? CreateSlot(PixelRect bounds)
        {
            var (dw, dh) = BrainDrainLayerRules.CaptureSize(bounds.Width, bounds.Height, _downscale);
            var grab = GrabFactory(bounds, dw, dh);
            if (grab == null) return null;
            var surface = SKSurface.Create(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) { grab.Dispose(); return null; }
            return new Slot { Bounds = bounds, W = dw, H = dh, Grab = grab, Surface = surface };
        }

        private void CaptureOnce(float phaseSeconds)
        {
            var slots = _slots;
            if (slots.Length == 0) return;

            // The sigma pushed in is ALREADY source-space; rebuild the filter only when it moved.
            float sigma = _sigma;
            if (MathF.Abs(sigma - _lastSigma) > 0.01f)
            {
                _lastSigma = sigma;
                _blurFilter?.Dispose();
                _blurFilter = sigma > 0.05f ? SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp) : null;   // Clamp: the frame edge stays opaque (SkiaSharp 3 defaults to Decal, which fades it out)
                _blurPaint.ImageFilter = _blurFilter;
            }

            // Melt: displacement + blur in ONE chain; only the cheap wrappers are rebuilt per tick.
            SKShader? drift = null;
            SKImageFilter? noiseFilter = null, meltFilter = null;
            float amplitude = _meltAmplitude;
            float gutter = 0f;
            if (_melt && amplitude > 0.05f)
            {
                var tile = EnsureNoiseTile(slots);
                if (tile != null)
                {
                    float tierScale = 4f / _downscale;
                    float dx = MeltSwayPx * tierScale * MathF.Sin(phaseSeconds * MeltSwayRate);
                    float dy = (MeltDriftPxPerSec * tierScale * phaseSeconds) % _meltTileCoverage;
                    float s = _meltTileCoverage / NoiseTilePx;
                    // Linear: a nearest-sampled noise tile would quantise the warp into visible blocks.
                    drift = SKShader.CreateImage(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, Linear,
                                                 SKMatrix.CreateScaleTranslation(s, s, dx, dy));
                    noiseFilter = SKImageFilter.CreateShader(drift);
                    if (noiseFilter != null)
                    {
                        meltFilter = SKImageFilter.CreateDisplacementMapEffect(
                            SKColorChannel.R, SKColorChannel.G, amplitude, noiseFilter, _blurFilter);
                        (_meltPaint ??= new SKPaint { ColorFilter = SwapRedBlue }).ImageFilter = meltFilter;
                        gutter = amplitude * 0.5f + 2f;
                    }
                }
            }

            try
            {
                foreach (var slot in slots)
                {
                    if (_stop) return;
                    if (slot.Surface == null || slot.Grab == null) continue;
                    if (!slot.Grab.Capture()) continue;

                    var info = new SKImageInfo(slot.W, slot.H, CaptureColorType, SKAlphaType.Opaque);
                    using var raw = SKImage.FromPixels(info, slot.Grab.Bits, slot.W * 4);   // zero-copy wrap
                    if (raw == null) continue;

                    var canvas = slot.Surface.Canvas;
                    canvas.Clear(SKColors.Transparent);
                    if (meltFilter != null)
                    {
                        // Scale about the centre so the displacement gutter falls off-frame.
                        canvas.Save();
                        canvas.Translate(slot.W * 0.5f, slot.H * 0.5f);
                        canvas.Scale((slot.W + 2f * gutter) / slot.W, (slot.H + 2f * gutter) / slot.H);
                        canvas.Translate(-slot.W * 0.5f, -slot.H * 0.5f);
                        canvas.DrawImage(raw, 0, 0, Linear, _plainPaint);   // opaque base: a warped edge that sampled outside the capture lands on the capture, never on nothing
                        canvas.DrawImage(raw, 0, 0, Linear, _meltPaint);
                        canvas.Restore();
                    }
                    else
                    {
                        // ALWAYS through _blurPaint: it also carries SwapRedBlue.
                        canvas.DrawImage(raw, 0, 0, _blurPaint);
                    }

                    var frame = slot.Surface.Snapshot();
                    SKImage? stale;
                    lock (slot.Gate) { stale = slot.Pending; slot.Pending = frame; }
                    stale?.Dispose();
                    Interlocked.Increment(ref _framesPublished);
                }
            }
            finally
            {
                if (_meltPaint != null) _meltPaint.ImageFilter = null;
                meltFilter?.Dispose();
                noiseFilter?.Dispose();
                drift?.Dispose();
            }
        }

        private SKImage? EnsureNoiseTile(Slot[] slots)
        {
            if (slots.Length == 0) return null;
            int shortEdge = Math.Min(slots[0].W, slots[0].H);
            if (_meltNoiseTile != null && _meltNoiseShort == shortEdge) return _meltNoiseTile;

            _meltNoiseTile?.Dispose();
            _meltNoiseTile = null;
            _meltNoiseShort = shortEdge;
            _meltTileCoverage = Math.Max(8f, shortEdge * NoiseCellFraction) * NoiseCellsPerTile;
            float freq = NoiseCellsPerTile / NoiseTilePx;
            using var noise = SKShader.CreatePerlinNoiseTurbulence(
                freq, freq * NoiseCellAspect, NoiseOctaves, NoiseSeed, new SKSizeI(NoiseTilePx, NoiseTilePx));
            using var surface = SKSurface.Create(
                new SKImageInfo(NoiseTilePx, NoiseTilePx, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) return null;
            using var paint = new SKPaint { Shader = noise };
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawRect(0, 0, NoiseTilePx, NoiseTilePx, paint);
            _meltNoiseTile = surface.Snapshot();
            return _meltNoiseTile;
        }

        private static void ReleaseSlots(Slot[] slots)
        {
            foreach (var slot in slots)
            {
                try
                {
                    SKImage? pending;
                    lock (slot.Gate) { pending = slot.Pending; slot.Pending = null; }
                    pending?.Dispose();
                    slot.Surface?.Dispose();
                    slot.Surface = null;
                    slot.Grab?.Dispose();
                    slot.Grab = null;
                }
                catch { }
            }
        }

        /// <summary>The Windows grab: a per-monitor memory DC + DIB section filled by a HALFTONE
        /// StretchBlt off the desktop DC (WPF CreateSlot + CaptureOnce, same calls).</summary>
        private sealed class GdiGrab : IGrab
        {
            private const int SRCCOPY = 0x00CC0020, HALFTONE = 4;
            private readonly PixelRect _bounds;
            private readonly int _w, _h;
            private IntPtr _memDc, _hBitmap, _oldObj;
            public IntPtr Bits { get; private set; }

            private GdiGrab(PixelRect bounds, int w, int h) { _bounds = bounds; _w = w; _h = h; }

            public static GdiGrab? Create(PixelRect bounds, int w, int h)
            {
                var memDc = CreateCompatibleDC(IntPtr.Zero);
                if (memDc == IntPtr.Zero) return null;
                var bmi = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -h,          // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,      // BI_RGB
                };
                var hBitmap = CreateDIBSection(memDc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                    DeleteDC(memDc);
                    return null;
                }
                return new GdiGrab(bounds, w, h)
                {
                    _memDc = memDc, _hBitmap = hBitmap, Bits = bits, _oldObj = SelectObject(memDc, hBitmap),
                };
            }

            public bool Capture()
            {
                var screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) return false;
                try
                {
                    SetStretchBltMode(_memDc, HALFTONE);
                    // Plain SRCCOPY, no CAPTUREBLT: layered windows are left out (the self-capture guard).
                    if (!StretchBlt(_memDc, 0, 0, _w, _h, screenDc, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SRCCOPY))
                        return false;
                    GdiFlush();
                    return true;
                }
                finally { ReleaseDC(IntPtr.Zero, screenDc); }
            }

            public void Dispose()
            {
                if (_memDc != IntPtr.Zero)
                {
                    if (_oldObj != IntPtr.Zero) SelectObject(_memDc, _oldObj);
                    DeleteDC(_memDc);
                    _memDc = IntPtr.Zero;
                }
                if (_hBitmap != IntPtr.Zero) { DeleteObject(_hBitmap); _hBitmap = IntPtr.Zero; }
                Bits = IntPtr.Zero;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct BITMAPINFOHEADER
            {
                public int biSize, biWidth, biHeight;
                public short biPlanes, biBitCount;
                public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            }

            [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
            [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
            [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
            [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
            [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
            [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
            [DllImport("gdi32.dll")] private static extern bool GdiFlush();
            [DllImport("gdi32.dll")]
            private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h,
                IntPtr src, int sx, int sy, int sw, int sh, int rop);
            [DllImport("gdi32.dll")]
            private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage,
                out IntPtr bits, IntPtr section, uint offset);
        }
    }
}
