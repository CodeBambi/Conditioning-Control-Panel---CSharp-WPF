using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Motion;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls.Depth
{
    /// <summary>
    /// Port of WPF 7.1.5 Services/UI/DashboardCardDepth.cs (polish wave 10): the Home mosaic tile
    /// takes the shared lamp. Visual feedback only; the owning card keeps every click and toggle.
    /// Travel from Core <see cref="DepthRules.TravelFor"/> (pressed PressTravelPx, on ActiveSinkPx,
    /// hovered idle lifts HoverLiftPx), an off tile at rest stands HoverLiftPx/2 proud of its
    /// socket, the bevel swaps DepthRaisedBevel / DepthPressedBevel by state. The release springs
    /// past rest by ReleaseOvershootPx (HudPlankRules.FaceTrack, the same spring), sampled on a
    /// 16 ms clock that stops when the move ends. The pointer tilt is not ported (ponytail).
    /// </summary>
    public sealed class DashboardCardDepth
    {
        /// <summary>An off tile at rest stands this far proud of its socket (up = negative).</summary>
        public const double RestLiftPx = DepthRules.HoverLiftPx / 2;

        private readonly Control _owner;
        private readonly Border _bevel;
        private readonly TranslateTransform _travel = new();
        private readonly Func<bool> _enabled, _active;
        private bool _pressed, _hovered;
        private double _target = double.NaN;
        private DispatcherTimer? _timer;
        private long _started;
        private int _ms;
        private double _from;
        private Keyframe[] _track = Array.Empty<Keyframe>();

        public DashboardCardDepth(Control owner, Control face, Border bevel,
            Func<bool> enabled, Func<bool> active, Func<PointerPressedEventArgs, bool> accepts)
        {
            _owner = owner;
            _bevel = bevel;
            _enabled = enabled;
            _active = active;
            face.RenderTransform = _travel;
            owner.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (!_enabled() || !accepts(e)) return;
                _pressed = true;
                Refresh();
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            owner.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Release(),
                global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            owner.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Release(),
                global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            face.PointerEntered += (_, _) => { _hovered = true; Refresh(); };
            face.PointerExited += (_, _) => { _hovered = false; Release(); };
            owner.Unloaded += (_, _) => { _hovered = false; _timer?.Stop(); Release(); };
            owner.Loaded += (_, _) => Refresh();
        }

        /// <summary>Where the face sits, px DOWN from the socket line, for a state.</summary>
        public static double TravelFor(bool enabled, bool pressed, bool active, bool hovered)
        {
            if (!enabled) return 0;
            double t = DepthRules.TravelFor(enabled, pressed, active, hovered);
            return !pressed && !active && !hovered ? -RestLiftPx : t;
        }

        /// <summary>The bevel key for a state: on and pressed sit in the socket, the rest is raised.</summary>
        public static string BevelKeyFor(bool pressed, bool active) =>
            pressed || active ? "DepthPressedBevel" : "DepthRaisedBevel";

        private static MotionLevel Level
        {
            get
            {
                try { return AmbientFxCanvas.Env.Level; }
                catch { return MotionLevel.Full; }
            }
        }

        private void Release()
        {
            _pressed = false;
            Refresh();
        }

        /// <summary>Re-reads the card's state (on / enabled) and moves the face and bevel to it.</summary>
        public void Refresh()
        {
            bool enabled = _enabled();
            bool active = enabled && _active();
            double target = TravelFor(enabled, _pressed, active, _hovered);
            _bevel.IsVisible = enabled;
            _bevel.Opacity = _pressed ? 1 : active ? 0.75 : 0.9;
            _bevel.BorderBrush = global::ConditioningControlPanel.Avalonia.Controls.NavRail.NavPaint.Depth(BevelKeyFor(_pressed, active));
            if (target == _target) return;
            _target = target;
            double from = _travel.Y;
            int ms = DepthRules.Ms(_pressed ? DepthRules.PressMs : DepthRules.ReleaseMs, Level);
            if (ms <= 0 || !_owner.IsEffectivelyVisible || double.IsNaN(from))
            {
                _timer?.Stop();
                _travel.Y = target;
                return;
            }
            bool springUp = !_pressed && target < from;
            _from = from;
            _ms = ms;
            _track = HudPlankRules.FaceTrack(target, springUp, ms);
            _started = Environment.TickCount64;
            _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
            _timer.Start();
            Step();
        }

        private void Step()
        {
            double t = Environment.TickCount64 - _started;
            _travel.Y = Keyframes.Sample(_track, _from, t);
            if (t >= _ms)
            {
                _timer?.Stop();
                _travel.Y = _target;
            }
        }

        /// <summary>Test seam: the face's travel now (px, + = down).</summary>
        public double Travel => _travel.Y;
    }
}
