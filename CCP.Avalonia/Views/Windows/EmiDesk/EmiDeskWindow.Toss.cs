using System;
using System.Threading;
using Avalonia;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// Being picked up, put down and thrown: WPF <c>EmiDeskWindow.Toss.cs</c>. A drag past the
    /// threshold lifts her (a surprised face, the lift sound, the <c>dragged</c> moment); letting go
    /// lands her with a squash that settles back to rest (<c>dropped</c>); letting go while she is
    /// moving faster than 1500 DIP/s for 120 ms is a fling: a harder thud, the dizzy chain, then
    /// <c>flung</c> with the 20 / 50 lifetime milestones. Every landing ends at scale 1.
    /// </summary>
    public partial class EmiDeskWindow
    {
        internal const double FlingSpeedDip = 1500.0;
        internal const int FlingHoldMs = 120;
        internal const int FlingGraceMs = 150;

        private const double LandSquashX = 1.08, LandSquashY = 0.94;
        private const int LandMs = 340;
        private const double ThudSquashX = 1.10, ThudSquashY = 0.88;
        private const int ThudMs = 280;
        private const string PickupFace = "o_o";

        private static readonly EmiChain LandChain = new(
            "land", "PUT DOWN",
            new[] { new EmiFrame("^_^", 600), new EmiFrame(EmiChains.RestFace, 160) },
            BodyFrame: "celebration");

        private double _tossLastY;
        private double _tossVy;
        private DateTime _fastSince = DateTime.MinValue;
        private DateTime _flingUntil = DateTime.MinValue;
        private CancellationTokenSource? _landAnim;

        /// <summary>What the last put-down was: "flung", "dropped" or null (test seam).</summary>
        internal string? LastLanding { get; private set; }

        private double TossY()
        {
            double s = DipScale;
            return Position.Y / (s <= 0 ? 1.0 : s);
        }

        private void OnPickedUp()
        {
            try
            {
                _tossLastY = TossY();
                _tossVy = 0;
                _fastSince = DateTime.MinValue;
                _flingUntil = DateTime.MinValue;

                EmiSfx.Lift();
                if (!ChainLive && _wobbleFace == null) DrawFace(PickupFace);
                FireDeskEvent("dragged");
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pickup failed"); }
        }

        /// <summary>One wobble frame of the drag: track the speed, arm the fling window (WPF NoteTossFrame).</summary>
        internal void NoteTossFrame(double dt, double vx) => NoteTossFrame(dt, vx, DateTime.UtcNow, TossY());

        internal void NoteTossFrame(double dt, double vx, DateTime now, double y)
        {
            try
            {
                if (dt <= 0) return;
                double raw = (y - _tossLastY) / dt;
                _tossLastY = y;
                _tossVy = _tossVy * WobbleVelKeep + raw * (1.0 - WobbleVelKeep);

                double speed = Math.Sqrt(vx * vx + _tossVy * _tossVy);
                if (speed >= FlingSpeedDip)
                {
                    if (_fastSince == DateTime.MinValue) _fastSince = now;
                    if ((now - _fastSince).TotalMilliseconds >= FlingHoldMs)
                        _flingUntil = now.AddMilliseconds(FlingGraceMs);
                }
                else _fastSince = DateTime.MinValue;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] toss frame failed"); }
        }

        private void OnPutDown() => OnPutDown(DateTime.UtcNow);

        internal void OnPutDown(DateTime now)
        {
            try
            {
                bool flung = now < _flingUntil;
                _flingUntil = DateTime.MinValue;
                _fastSince = DateTime.MinValue;
                string zoneRow = ZoneRow();

                if (flung)
                {
                    LastLanding = "flung";
                    int total = EmiState.NoteFling();
                    EmiSfx.Thud();
                    PlayLanding(thrown: true);
                    var flingCtx = new { zoneRow, flings20 = total >= 20, flings50 = total >= 50 };
                    PlayChain("dizzy", () => FireDeskEvent("flung", flingCtx));
                    return;
                }

                LastLanding = "dropped";
                var ctx = new { zoneRow, flings20 = false, flings50 = false };
                EmiSfx.Bump();
                PlayLanding(thrown: false);
                if (ChainLive) { FireDeskEvent("dropped", ctx); return; }
                PlayChain(LandChain, () => FireDeskEvent("dropped", ctx));
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] put down failed"); }
        }

        /// <summary>The landing squash: in on a cubic, back out on one soft overshoot, ending at rest.</summary>
        private void PlayLanding(bool thrown)
        {
            try
            {
                if (_closingForGood) return;

                _landAnim?.Cancel();
                _landAnim?.Dispose();
                var cts = new CancellationTokenSource();
                _landAnim = cts;

                Tween(thrown ? ThudMs : LandMs, cts.Token, p =>
                {
                    var (sx, sy) = LandingScale(thrown, p);
                    _squashScale.ScaleX = sx;
                    _squashScale.ScaleY = sy;
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] landing squash failed");
                _squashScale.ScaleX = 1;
                _squashScale.ScaleY = 1;
            }
        }

        /// <summary>WPF PlayLanding's key frames as one curve. p = 1 is exactly (1, 1): the out.</summary>
        internal static (double X, double Y) LandingScale(bool thrown, double p)
        {
            if (p >= 1) return (1, 1);
            if (p <= 0) return (1, 1);

            static double EaseOut(double t) { t = 1 - t; return 1 - t * t * t; }
            static double Lerp(double a, double b, double t) => a + (b - a) * t;
            // One oscillation that decays onto the target (WPF ElasticEase, Oscillations 1, Springiness 4).
            static double Settle(double t) => 1 - Math.Exp(-4 * t) * Math.Cos(2 * Math.PI * t) * (1 - t);

            if (thrown)
            {
                if (p < 0.20) { double t = EaseOut(p / 0.20); return (Lerp(1, 0.95, t), Lerp(1, 1.06, t)); }
                if (p < 0.55) { double t = EaseOut((p - 0.20) / 0.35); return (Lerp(0.95, ThudSquashX, t), Lerp(1.06, ThudSquashY, t)); }
                double b = Settle((p - 0.55) / 0.45);
                return (Lerp(ThudSquashX, 1, b), Lerp(ThudSquashY, 1, b));
            }

            if (p < 0.40) { double t = EaseOut(p / 0.40); return (Lerp(1, LandSquashX, t), Lerp(1, LandSquashY, t)); }
            double back = Settle((p - 0.40) / 0.60);
            return (Lerp(LandSquashX, 1, back), Lerp(LandSquashY, 1, back));
        }

        /// <summary>Which third of her screen she landed in: top, mid or bottom.</summary>
        private string ZoneRow()
        {
            try
            {
                var body = BodyScreenRect;
                var centre = new PixelPoint((int)Math.Round(body.X + body.Width / 2), (int)Math.Round(body.Y + body.Height / 2));
                var screen = Screens.ScreenFromPoint(centre) ?? Screens.Primary;
                if (screen == null) return "mid";
                var work = screen.WorkingArea;
                return EmiTossRules.ZoneRow(centre.Y, work.Y, work.Height);
            }
            catch { return "mid"; }
        }
    }

    /// <summary>WPF EmiTossRules: the landing row, as arithmetic.</summary>
    public static class EmiTossRules
    {
        public static string ZoneRow(double y, double top, double height)
        {
            if (height <= 0) return "mid";
            double f = (y - top) / height;
            return f < 1.0 / 3.0 ? "top" : f >= 2.0 / 3.0 ? "bottom" : "mid";
        }
    }
}
