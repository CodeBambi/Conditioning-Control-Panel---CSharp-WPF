using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The thing she is holding: WPF <c>EmiDeskWindow.Props.cs</c>. An idle fidget brings a plate up
    /// at her right hand (a phone, a clipboard, a punch card), she reads it for 2.6 s and puts it
    /// away. IN = a 220 ms rise out from behind her, OUT = the same slide back down, then the image
    /// is taken off. The anchor, sizes, hold and rise are Core <c>EmiProps</c>; the art is the
    /// arcademy's own plates under Resources/web, and a missing plate makes the whole beat a no-op.
    /// </summary>
    public partial class EmiDeskWindow
    {
        private readonly Dictionary<string, Bitmap> _propCache = new(StringComparer.Ordinal);
        private readonly RotateTransform _propTilt = new();
        private readonly TranslateTransform _propRise = new();
        private bool _propWired;
        private string? _propKey;
        private string? _lastPropKey;
        private DispatcherTimer? _propTimer;
        private CancellationTokenSource? _propAnim;

        /// <summary>True while she is holding something.</summary>
        public bool PropUp => _propKey != null;

        private Image? PropView()
        {
            var img = _propImage;
            if (img == null) return null;
            if (_propWired) return img;
            _propWired = true;
            // The hand is the bottom-right corner: the tilt pivots there, the rise slides under it.
            img.RenderTransformOrigin = new RelativePoint(1, 1, RelativeUnit.Relative);
            img.RenderTransform = new TransformGroup { Children = { _propTilt, _propRise } };
            return img;
        }

        private void LayoutProp()
        {
            try
            {
                var prop = EmiProps.Get(_propKey);
                var img = PropView();
                if (prop == null || img == null) return;

                double bw = _bodyWidth;
                double bh = bw * BodyAspect;

                double w, h;
                if (prop.Sizing == EmiProps.Fit.Height)
                {
                    h = bh * prop.Frac;
                    w = h * PropPlateWidthOverHeight(prop.Key);
                }
                else
                {
                    w = bw * prop.Frac;
                    h = w / PropPlateWidthOverHeight(prop.Key);
                }

                img.Width = Math.Max(1, w);
                img.Height = Math.Max(1, h);
                img.Margin = new Thickness(0, 0, bw * (1 - EmiProps.RightFrac), bh * (1 - EmiProps.BottomFrac));
                _propTilt.Angle = prop.TiltDeg;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] prop layout failed for {Key}", _propKey); }
        }

        private double PropPlateWidthOverHeight(string key)
        {
            if (_propCache.TryGetValue(key, out var src) && src.Size.Height > 0)
                return src.Size.Width / src.Size.Height;
            return key switch
            {
                "phone" => 98.0 / 170.0,
                "clipboard" => 170.0 / 226.0,
                "punchcard" => 214.0 / 118.0,
                _ => 1.0
            };
        }

        /// <summary>Bring a plate up for <paramref name="holdMs"/> (default EmiProps.HoldMs), then put it away.</summary>
        public void ShowProp(string? key, int holdMs = 0)
        {
            try
            {
                var prop = EmiProps.Get(key);
                var view = PropView();
                if (prop == null || view == null) return;

                if (!_propCache.TryGetValue(prop.Key, out var bmp))
                {
                    var path = EmiProps.Path(prop.Key);
                    if (path == null)
                    {
                        Log.Debug("[EmiDesk] prop art missing for {Key}", prop.Key);
                        return;
                    }
                    bmp = new Bitmap(path);
                    _propCache[prop.Key] = bmp;
                }

                _propKey = prop.Key;
                view.Source = bmp;
                LayoutProp();                      // AFTER the cache fill: the aspect comes from the bitmap
                view.IsVisible = true;
                RunPropRise(up: true);

                int hold = holdMs > 0 ? holdMs : EmiProps.HoldMs;
                _propTimer?.Stop();
                var t = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(hold) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    if (ReferenceEquals(_propTimer, t)) HideProp();
                };
                _propTimer = t;
                t.Start();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowProp failed for {Key}", key); }
        }

        /// <summary>Put it away. <paramref name="animate"/> false takes it straight off (she is going).</summary>
        public void HideProp(bool animate = true)
        {
            try
            {
                _propTimer?.Stop();
                _propTimer = null;
                var view = _propImage;
                if (view == null || !_propWired) return;   // never shown: nothing to take off
                if (_propKey == null && !view.IsVisible) return;
                _propKey = null;

                if (!animate || !AliveMotionOk) { TakePropOff(); return; }
                RunPropRise(up: false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] HideProp failed");
                TakePropOff();
            }
        }

        private void TakePropOff()
        {
            try
            {
                _propAnim?.Cancel();
                _propRise.Y = 0;
                if (_propImage is { } v) { v.IsVisible = false; v.Source = null; }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] prop stand-down failed"); }
        }

        private void RunPropRise(bool up)
        {
            try
            {
                var view = _propImage;
                if (view == null) return;
                double travel = view.Height * EmiProps.RiseFrac;
                if (double.IsNaN(travel) || travel <= 0) travel = 20;

                _propAnim?.Cancel();
                _propAnim?.Dispose();
                _propAnim = null;

                if (!AliveMotionOk)
                {
                    _propRise.Y = 0;
                    if (!up) TakePropOff();
                    return;
                }

                var cts = new CancellationTokenSource();
                _propAnim = cts;
                _propRise.Y = up ? travel : 0;
                Tween(EmiProps.RiseMs, cts.Token, p =>
                {
                    // Sine out on the way up, sine in on the way down (WPF SineEase).
                    double e = up ? Math.Sin(p * Math.PI / 2) : 1 - Math.Cos(p * Math.PI / 2);
                    _propRise.Y = up ? travel * (1 - e) : travel * e;
                    if (!up && p >= 1 && _propKey == null) TakePropOff();   // unless a new beat started under the slide
                });
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] prop rise failed"); }
        }

        /// <summary>The idle beat: she brings something up, reads it, puts it away. False when there is no art.</summary>
        private bool RunPropBeat()
        {
            try
            {
                if (EmiProps.All.Count == 0) return false;

                string? key = _lastPropKey;
                for (int guard = 0; guard < 8 && key == _lastPropKey; guard++)
                    key = EmiProps.All[Rng.Next(EmiProps.All.Count)].Key;
                _lastPropKey = key;

                ShowProp(key);
                if (!PropUp) return false;         // art missing: do not put the reading face on nothing

                PlayChain(new EmiChain(
                    "prop", "IDLE BEAT (checks something)",
                    new[]
                    {
                        new EmiFrame(EmiProps.Face, EmiProps.HoldMs),
                        new EmiFrame(EmiProps.DoneFace, 420)
                    },
                    BodyFrame: "idle"), done: () => HideProp());
                Log.Debug("[EmiDesk] prop beat: {Prop}", key);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] prop beat failed");
                return false;
            }
        }
    }
}
