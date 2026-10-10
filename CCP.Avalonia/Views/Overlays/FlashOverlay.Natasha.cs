using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    // HB18. Three pieces of WPF FlashService this head lacked:
    //   Natasha's favourite on flashes (the flash dodge, :1854-1870, :2772-2800),
    //   luminance sync (:2203-2300) and the picture override (TriggerFlashOnceWithImage, :782).
    // Not here: the jackpot remix (no JackpotRemixDirector on this head, so its row stays absent).
    internal static partial class FlashOverlay
    {
        // ---- Natasha's favourite: the flash dodge ----

        /// <summary>WPF ActivityTracker.GetIdleSeconds: GetLastInputInfo on Windows, the XScreenSaver
        /// idle counter on an X11 desk (Platform/InputIdleProbe). Unknown reads as -1, and a player
        /// nobody can prove is at the desk is never dealt a red flash.</summary>
        internal static Func<int> IdleSeconds = Platform.InputIdleProbe.DeskIdleSeconds;

        /// <summary>WPF :1860-1864: only a first-generation flash the player can dodge (the setting on,
        /// clickable, at the desk), only while the price can land (tab on, linked, row on, no safety
        /// hold), and then one in ten.</summary>
        internal static bool RollNatasha(AppSettings s, bool clickable, int generation, Random rng)
        {
            try
            {
                return generation == 0
                    && NatashasFavourite.FlashMayRoll(s.ChasterFlashDodge, clickable, IdleSeconds())
                    && ChasterHead.Service?.CanBook(NatashasFavourite.EventId) == true
                    && NatashasFavourite.Roll(rng);
            }
            catch (Exception ex) { Log.Debug("natasha flash roll: {E}", ex.Message); return false; }
        }

        /// <summary>The ring's clock. Tests swap it (RunsAlone).</summary>
        internal static Action<Action, TimeSpan> DodgeTimer = (run, after) => DispatcherTimer.RunOnce(run, after);

        /// <summary>WPF StartNatashaDodge: the red one showed and its ring is running. A click, a stare
        /// or a fling in time dodges it; nothing books until the ring empties.</summary>
        internal static void StartNatashaDodge(FlashOverlayWindow w)
        {
            w.Popped += _ => w.NatashaDodged = true;
            w.Flung += () => { w.NatashaDodged = true; w.HideDodgeRing(); };
            w.ShowDodgeRing();
            DodgeTimer(() => EndNatashaDodge(w), TimeSpan.FromMilliseconds(NatashasFavourite.DodgeMs));
        }

        /// <summary>The ring emptied: +5:00, unless it was dodged or the app already cleared it
        /// (panic, stop). Unprompted: the player did nothing, so Circe does not say they popped it.</summary>
        internal static void EndNatashaDodge(FlashOverlayWindow w)
        {
            try
            {
                w.HideDodgeRing();
                var entry = Active.FirstOrDefault(e => e.Window == w);
                var up = entry.Window != null && !w.IsLeaving;
                if (!NatashasFavourite.DodgeBooks(up, w.NatashaDodged)) return;
                var r = entry.Rect;
                ChasterHead.Service?.NoteAt(NatashasFavourite.EventId,
                    new ScreenPoint(r.X + r.Width / 2.0, r.Y + r.Height / 2.0), unprompted: true);
            }
            catch (Exception ex) { Log.Debug("[Chaster] natasha dodge: {E}", ex.Message); }
        }

        // ---- Luminance sync (haptics Phase F) ----

        private const int LuminanceCacheMax = 500;
        private const int LuminanceSampleSize = 8;
        private static readonly Dictionary<string, double> LuminanceCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The flash's average brightness scaled by the user's dial, or -1 when the feature is
        /// off or the picture cannot be sampled (stay silent rather than guess). Never re-decodes.</summary>
        internal static double LuminanceFor(Bitmap? picture, string? key)
        {
            try
            {
                var haptics = CoreHaptics.Service;
                if (haptics == null || !haptics.Settings.LuminanceSyncEnabled) return -1;   // the whole cost when off
                var scale = haptics.Settings.LuminanceSyncIntensity;
                if (scale <= 0 || picture == null) return -1;
                double luminance;
                lock (LuminanceCache)
                {
                    if (string.IsNullOrEmpty(key) || !LuminanceCache.TryGetValue(key, out luminance))
                    {
                        luminance = SampleLuminance(picture);
                        if (luminance >= 0 && !string.IsNullOrEmpty(key))
                        {
                            if (LuminanceCache.Count >= LuminanceCacheMax) LuminanceCache.Clear();
                            LuminanceCache[key] = luminance;
                        }
                    }
                }
                return luminance < 0 ? -1 : luminance * Math.Clamp(scale, 0, 1);
            }
            catch (Exception ex) { Log.Debug("Flash: luminance sync failed (non-fatal): {E}", ex.Message); return -1; }
        }

        /// <summary>Push it onto the continuous Luminance layer for as long as the flash is up. The
        /// layer self-clears after the lifetime, so a flash that dies in an unusual way (panic key,
        /// engine stop) still cannot leave the toy humming.</summary>
        internal static void PushLuminance(double value, TimeSpan lifetime)
        {
            if (value < 0) return;
            try
            {
                CoreHaptics.Service?.SetLayer(HapticLayer.Luminance, value,
                    autoZeroMs: (int)Math.Clamp(lifetime.TotalMilliseconds, 100, 30_000));
            }
            catch (Exception ex) { Log.Debug("Flash: luminance push failed (non-fatal): {E}", ex.Message); }
        }

        /// <summary>Average perceptual luminance (Rec. 601) of an already-decoded picture, 0..1; -1 when
        /// it cannot be sampled. Downscaled to 8x8; a transparent pixel contributes nothing (a mostly
        /// transparent PNG is not "bright").</summary>
        internal static double SampleLuminance(Bitmap? source)
        {
            try { return SampleLuminanceCore(source); }
            catch (Exception ex) { Log.Debug("Flash: luminance sample failed: {E}", ex.Message); return -1; }
        }

        internal static double SampleLuminanceCore(Bitmap? source)
        {
            {
                if (source == null || source.PixelSize.Width <= 0 || source.PixelSize.Height <= 0) return -1;
                int w = Math.Min(LuminanceSampleSize, source.PixelSize.Width), h = Math.Min(LuminanceSampleSize, source.PixelSize.Height);
                using var small = source.CreateScaledBitmap(new PixelSize(w, h));
                w = small.PixelSize.Width; h = small.PixelSize.Height;
                if (w <= 0 || h <= 0) return -1;
                var stride = w * 4;
                var pixels = new byte[stride * h];
                var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { small.CopyPixels(new PixelRect(0, 0, w, h), pin.AddrOfPinnedObject(), pixels.Length, stride); }
                finally { pin.Free(); }
                var rgba = small.Format == global::Avalonia.Platform.PixelFormat.Rgba8888;
                var premul = small.AlphaFormat == global::Avalonia.Platform.AlphaFormat.Premul;
                double sum = 0, weight = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var alpha = pixels[i + 3] / 255.0;
                    if (alpha <= 0) continue;
                    double c0 = pixels[i] / 255.0, g = pixels[i + 1] / 255.0, c2 = pixels[i + 2] / 255.0;
                    double r = rgba ? c0 : c2, b = rgba ? c2 : c0;
                    var lum = 0.299 * r + 0.587 * g + 0.114 * b;
                    sum += premul ? lum : lum * alpha;   // premultiplied channels already carry the alpha
                    weight += alpha;
                }
                return weight <= 0 ? 0 : Math.Clamp(sum / weight, 0, 1);
            }
        }

        // ---- The picture override (WPF TriggerFlashOnceWithImage) ----

        /// <summary>Where a pinned picture lives: absolute as given, else under assets/images. Null =
        /// nothing pinned or the file is gone, and the caller falls back to an ordinary random flash.
        /// ponytail: a remote (http) path falls back too; this head's online pictures come from the
        /// consented clip pool only.</summary>
        internal static string? ResolveOverride(string? imagePath, string assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return null;
            if (imagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || imagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                var resolved = Path.IsPathRooted(imagePath) ? imagePath : Path.Combine(assetsRoot ?? "", "images", imagePath);
                return File.Exists(resolved) ? resolved : null;
            }
            catch { return null; }
        }

        /// <summary>One-shot flash that shows a specific picture instead of a random one (Deeper Effect
        /// items and Chaos payloads that pin one). One pictured lead plus the rest of the burst
        /// (<paramref name="amount"/> in all, 1-20). A missing file falls back to a random flash.</summary>
        public static void TriggerOnceWithImage(Visual host, string? imagePath, int durationMs, int? size = null, int amount = 1)
        {
            if (_busy) { Log.Debug("Flash: TriggerOnceWithImage skipped - busy"); return; }
            var lead = ResolveOverride(imagePath, CorePaths.EffectiveAssets);
            if (lead == null && !string.IsNullOrWhiteSpace(imagePath))
                Log.Debug("Flash: TriggerOnceWithImage path not found ({Path}); falling back to random", imagePath);
            TriggerOnce(host, lead == null ? 1 : Math.Clamp(amount, 1, 20), durationMs, size, lead);
        }
    }

    internal sealed partial class FlashOverlayWindow
    {
        /// <summary>Dealt to Natasha (WPF FlashWindow.IsNatasha): red halo, a 4 s ring.</summary>
        internal bool IsNatasha;
        /// <summary>A click, a stare or a fling got it in time.</summary>
        internal bool NatashaDodged;
        /// <summary>The drag ended in a fling (WPF FlashDragOutcome.Fling).</summary>
        internal event Action? Flung;
        internal void RaiseFlung() => Flung?.Invoke();

        private DodgeRing? _dodgeRing;
        internal bool DodgeRingShown => _dodgeRing is { IsVisible: true };

        /// <summary>WPF BuildDodgeRing: a 34 DIP red ring, top right, emptying over the dodge time.</summary>
        internal void ShowDodgeRing()
        {
            if (_dodgeRing != null || Content is not Control body) return;
            _dodgeRing = new DodgeRing();
            Content = null;
            Content = new Panel { Children = { body, _dodgeRing } };
            Closed += (_, _) => _dodgeRing?.Stop();
        }

        internal void HideDodgeRing()
        {
            if (_dodgeRing == null) return;
            _dodgeRing.Stop();
            _dodgeRing.IsVisible = false;
        }

        /// <summary>The ring itself. A bounded 4 s timer, never an infinite animation.</summary>
        private sealed class DodgeRing : Control
        {
            private const double D = 34, T = 4;
            private static readonly IBrush Disc = new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x06, 0x0C)).ToImmutable();
            private static readonly IPen Red = new Pen(new SolidColorBrush(Color.FromRgb(NatashasFavourite.R, NatashasFavourite.G, NatashasFavourite.B)).ToImmutable(), T).ToImmutable();
            private readonly DateTime _start = DateTime.UtcNow;
            private readonly DispatcherTimer _timer;

            public DodgeRing()
            {
                Width = Height = D;
                Margin = new Thickness(10);
                HorizontalAlignment = HorizontalAlignment.Right;
                VerticalAlignment = VerticalAlignment.Top;
                IsHitTestVisible = false;
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
                {
                    InvalidateVisual();
                    if ((DateTime.UtcNow - _start).TotalMilliseconds >= NatashasFavourite.DodgeMs) Stop();
                });
                _timer.Start();
            }

            public void Stop() => _timer.Stop();

            public override void Render(DrawingContext dc)
            {
                var c = new Point(D / 2, D / 2);
                dc.DrawEllipse(Disc, null, c, D / 2, D / 2);
                var left = NatashasFavourite.DodgeLeft((DateTime.UtcNow - _start).TotalMilliseconds);
                if (left <= 0) return;
                var r = (D - T) / 2;
                if (left >= 0.999) { dc.DrawEllipse(null, Red, c, r, r); return; }
                var sweep = 2 * Math.PI * left;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(c.X, c.Y - r), false);
                    ctx.ArcTo(new Point(c.X + r * Math.Sin(sweep), c.Y - r * Math.Cos(sweep)), new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise);
                    ctx.EndFigure(false);
                }
                dc.DrawGeometry(null, Red, g);
            }
        }
    }
}
