using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The alive poll: WPF <c>EmiDeskWindow.Alive.cs</c> :155-530. One 100 ms timer while she is
    /// visible: her face leans toward the cursor (the lean eases in AND back to rest), she perks up
    /// when the cursor walks up (30 s cooldown), a hover that lingers gets an expectant face and
    /// then a look away, and the idle fidgets (antenna twitch, glance) fire on their authored
    /// 25-50 s jitter. Every beat is a chain or an eased offset, so it ends on its own; StopAlive
    /// puts the gaze back at rest.
    ///
    /// <para>The cursor is read from the OS (user32 GetCursorPos / X11 XQueryPointer through
    /// Platform/X11Pointer), because she watches it while it is over OTHER windows.</para>
    ///
    /// <para>not ported: the weight shift and the rare stretch (body squash tweens) and the
    /// screen beat (no glass channels): a scheduler pick of one of those
    /// declines and the next fidget runs instead, which is WPF's own decline path.</para>
    /// </summary>
    public partial class EmiDeskWindow
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AlivePoint { public int X, Y; }

        [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
        private static extern bool AliveGetCursorPos(out AlivePoint p);

        /// <summary>The cursor in screen pixels, or null when the OS will not say. Tests swap it.</summary>
        internal Func<PixelPoint?> CursorProbe = ReadCursor;

        private static PixelPoint? ReadCursor()
        {
            try
            {
                // Headless hosts (tests) have no desktop lifetime: she never reads the real cursor there.
                if (global::Avalonia.Application.Current?.ApplicationLifetime == null) return null;
                if (OperatingSystem.IsWindows())
                    return AliveGetCursorPos(out var p) ? new PixelPoint(p.X, p.Y) : null;
                return Platform.X11Pointer.Read()?.At;
            }
            catch { return null; }
        }

        private DispatcherTimer? _aliveTimer;
        private double _gazeX, _gazeY;               // the lean she is wearing, DIPs
        private double _gazeNudgeX, _gazeNudgeY;     // a fidget's standing look, DIPs
        private DateTime _gazeNudgeUntil = DateTime.MinValue;
        private Point _aliveLastCursor;
        private DateTime _aliveLastTick = DateTime.MinValue;
        private bool _apInside;
        private DateTime _apCoolUntil = DateTime.MinValue;
        private DateTime _lingerFrom = DateTime.MinValue;
        private int _lingerStage;
        private int _lingerPets0;
        private readonly EmiAlive.FidgetScheduler _fidgets = new();
        private DateTime _fidgetDue = DateTime.MaxValue;

        private static readonly EmiChain PerkChain = new(
            "perk", "APPROACH (walked up)",
            new[] { new EmiFrame(EmiAlive.PerkFace, EmiAlive.PerkHoldMs) }, BodyFrame: "idle");

        private static readonly EmiChain LingerChain = new(
            "linger", "HOVER LINGER (expectant)",
            new[] { new EmiFrame(EmiAlive.LingerFace, EmiAlive.LingerHoldMs) }, BodyFrame: "idle");

        private static readonly EmiChain LingerAwayChain = new(
            "lingerAway", "HOVER LINGER (look away)",
            new[] { new EmiFrame(EmiAlive.LingerAwayFace, EmiAlive.LingerHoldMs) }, BodyFrame: "idle");

        private static readonly EmiChain TwitchChain = new(
            "fidgetTwitch", "FIDGET (antenna twitch)",
            new[] { new EmiFrame(EmiChains.RestFace, 500) }, BodyFrame: "idle");

        /// <summary>The lean and the twitch are motion: Full only (WPF AliveMotionOk).</summary>
        private static bool AliveMotionOk
        {
            get
            {
                try { return CoreSettings.Current.MotionLevel == MotionLevel.Full; }
                catch { return true; }
            }
        }

        /// <summary>True while the 100 ms poll runs; the lean she wears (test seams).</summary>
        internal bool AliveRunning => _aliveTimer?.IsEnabled == true;
        internal (double X, double Y) Gaze => (_gazeShift.X, _gazeShift.Y);

        private void StartAlive()
        {
            try
            {
                if (PresentationActive || _closingForGood) return;
                StopAlive();

                _aliveLastTick = DateTime.MinValue;
                _apInside = false;
                _lingerFrom = DateTime.MinValue;
                _lingerStage = 0;
                ResetGaze();
                _fidgetDue = DateTime.UtcNow.AddMilliseconds(FidgetDelayMs());

                _aliveTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(EmiAlive.PollMs)
                };
                _aliveTimer.Tick += OnAliveTick;
                _aliveTimer.Start();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] alive poll start failed"); }
        }

        private void StopAlive()
        {
            try
            {
                var t = _aliveTimer;
                _aliveTimer = null;
                if (t != null) { t.Stop(); t.Tick -= OnAliveTick; }
                HideProp(animate: false);
                ResetGaze();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] alive poll stop failed"); }
        }

        private void ResetGaze()
        {
            _gazeX = _gazeY = _gazeNudgeX = _gazeNudgeY = 0;
            _gazeNudgeUntil = DateTime.MinValue;
            _gazeShift.X = 0;
            _gazeShift.Y = 0;
        }

        private void OnAliveTick(object? sender, EventArgs e) => AliveStep(DateTime.UtcNow);

        /// <summary>One poll (WPF OnAliveTick :229). Internal so a test can drive the clock.</summary>
        internal void AliveStep(DateTime now)
        {
            try
            {
                if (_closingForGood || !IsVisible) return;

                double s = DipScale;
                if (s <= 0) s = 1.0;
                if (CursorProbe() is not { } raw) return;
                var cursor = new Point(raw.X / s, raw.Y / s);

                var bodyPx = BodyScreenRect;
                var body = new Rect(bodyPx.X / s, bodyPx.Y / s, bodyPx.Width / s, bodyPx.Height / s);
                if (body.Width <= 0 || body.Height <= 0) return;

                double speed = 0;
                if (_aliveLastTick != DateTime.MinValue)
                {
                    double ms = (now - _aliveLastTick).TotalMilliseconds;
                    if (ms > 0)
                    {
                        double dx = cursor.X - _aliveLastCursor.X, dy = cursor.Y - _aliveLastCursor.Y;
                        speed = Math.Sqrt(dx * dx + dy * dy) / ms;
                    }
                }
                _aliveLastCursor = cursor;
                _aliveLastTick = now;

                StepGaze(cursor, body, now);
                StepApproach(cursor, body, now, speed);
                StepLinger(cursor, body, now);
                StepFidgets(now);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] alive tick failed"); }
        }

        /// <summary>May an ambient beat take her face right now (WPF CanPerk :280).</summary>
        private bool CanPerk()
        {
            bool hold = false;
            try { hold = EmiLineEngine.Instance.HoldActive; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] hold probe threw"); }
            return EmiAlive.CanPerk(busy: Busy(), chainLive: ChainLive, askLive: false,
                holdActive: hold, dragging: _dragging, resizing: _resizing);
        }

        private void StepGaze(Point cursor, Rect body, DateTime now)
        {
            bool active = AliveMotionOk && !Busy() && !_dragging && !_resizing;

            double tx = 0, ty = 0;
            if (active)
            {
                if (now < _gazeNudgeUntil) { tx = _gazeNudgeX; ty = _gazeNudgeY; }
                else (tx, ty) = EmiAlive.GazeTarget(cursor, body, _bodyWidth);
            }

            double k = EmiAlive.GazeEasePerPoll;
            _gazeX = EmiAlive.Ease(_gazeX, tx, k);
            _gazeY = EmiAlive.Ease(_gazeY, ty, k);
            if (Math.Abs(_gazeX - tx) + Math.Abs(_gazeY - ty) < 0.02) { _gazeX = tx; _gazeY = ty; }

            _gazeShift.X = _gazeX;
            _gazeShift.Y = _gazeY;
        }

        private void NudgeGaze(double dirX, double dirY, int ms)
        {
            _gazeNudgeX = EmiAlive.GazeNudge(dirX, _bodyWidth);
            _gazeNudgeY = EmiAlive.GazeNudge(dirY, _bodyWidth);
            _gazeNudgeUntil = DateTime.UtcNow.AddMilliseconds(Math.Max(1, ms));
        }

        private void StepApproach(Point cursor, Rect body, DateTime now, double speed)
        {
            bool inside = EmiAlive.WithinApproach(cursor, body);
            if (inside == _apInside) return;
            _apInside = inside;
            if (!inside) return;

            if (now < _apCoolUntil) return;
            _apCoolUntil = now.AddMilliseconds(EmiAlive.ApproachCooldownMs);
            if (!CanPerk()) return;

            if (speed > EmiAlive.GlanceSpeedDipPerMs) PlayChain("glance", bodyFrameOverride: "idle");
            else PlayChain(PerkChain);
        }

        private void StepLinger(Point cursor, Rect body, DateTime now)
        {
            if (!body.Contains(cursor))
            {
                _lingerFrom = DateTime.MinValue;
                _lingerStage = 0;
                return;
            }

            int pets = 0;
            try { pets = EmiState.Current.PetsTotal; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] linger pet count read failed"); }

            if (_lingerFrom == DateTime.MinValue)
            {
                _lingerFrom = now;
                _lingerStage = 0;
                _lingerPets0 = pets;
                return;
            }

            double held = (now - _lingerFrom).TotalMilliseconds;
            bool touched = pets > _lingerPets0;

            if (_lingerStage == 0 && held >= EmiAlive.LingerMs)
            {
                _lingerStage = 1;
                if (!touched && CanPerk()) PlayChain(LingerChain);
                return;
            }
            if (_lingerStage == 1 && held >= EmiAlive.LingerAwayMs)
            {
                _lingerStage = 2;
                if (!touched && CanPerk()) PlayChain(LingerAwayChain);
            }
        }

        private void StepFidgets(DateTime now)
        {
            if (now < _fidgetDue) return;
            if (!CanPerk()) return;   // due, not forced: it waits for a quiet moment
            _fidgetDue = now.AddMilliseconds(FidgetDelayMs());

            var kind = _fidgets.Next();
            if (RunFidget(kind)) { Log.Debug("[EmiDesk] fidget {Kind}", kind); return; }
            var alt = _fidgets.Next();
            Log.Debug("[EmiDesk] fidget {Kind} declined, {Alt} instead", kind, alt);
            RunFidget(alt);
        }

        private int FidgetDelayMs()
        {
            int authored = _fidgets.NextDelayMs();
            return EmiDebug.FidgetMs is int ms ? ms : authored;
        }

        private bool RunFidget(EmiFidget kind)
        {
            try
            {
                switch (kind)
                {
                    case EmiFidget.Twitch:
                        if (!AliveMotionOk) return false;
                        PlayChain(TwitchChain);
                        AnimateOffset(EmiAlive.TwitchDip, 0, 0.22, 1);
                        return true;

                    case EmiFidget.Glance:
                        PlayChain("glance", bodyFrameOverride: "idle");
                        NudgeGaze(Rng.Next(2) == 0 ? -1 : 1, 0, 900);
                        return true;

                    case EmiFidget.Prop:
                        if (!RunPropBeat()) return false;
                        NudgeGaze(1, 1, EmiProps.HoldMs);
                        return true;
                }
                return false;   // weight shift, screen: not on this head yet, the next one runs
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] fidget {Kind} failed", kind);
                return false;
            }
        }
    }
}
