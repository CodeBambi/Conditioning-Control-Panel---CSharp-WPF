// PORTED from WPF 7.1.5 MainWindow.xaml SectionEdgeLift + MainWindow.SectionEdge.cs (the lift
// legs). WPF slid a LinearGradientBrush.RelativeTransform on each strip; Avalonia brushes have no
// relative transform, so each strip holds one rectangle the strip's own size, painted with the
// same three stops, and slides it by (relative offset x strip length) inside a clipped panel.
// Same picture, and a tick still dirties a 3 px strip and nothing else.

using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using CoreFx = global::ConditioningControlPanel.Fx;
using CoreNav = global::ConditioningControlPanel.Nav;

namespace ConditioningControlPanel.Avalonia.Controls.NavRail
{
    /// <summary>
    /// The travelling lift: four 3 px strips inside the corner curve (inset 8 px), each carrying
    /// the white-lifted hue over the frame line and sliding it along its edge in turn (top, right,
    /// bottom, left: one lap in <see cref="CoreNav.SectionEdgeRules.SpinSeconds"/> at
    /// <see cref="CoreNav.SectionEdgeRules.SpinFps"/>). Reduced parks the lift at the top centre,
    /// Off hides the strips. Numbers are Core's SectionEdgeRules; this control only draws.
    /// </summary>
    public sealed class SectionEdgeLift : Grid
    {
        private static readonly CoreFx.EdgeSide[] Sides =
            { CoreFx.EdgeSide.Top, CoreFx.EdgeSide.Right, CoreFx.EdgeSide.Bottom, CoreFx.EdgeSide.Left };

        private readonly Panel[] _tracks = new Panel[4];
        private readonly Rectangle[] _bands = new Rectangle[4];
        private readonly TranslateTransform[] _moves = new TranslateTransform[4];
        private readonly double[] _rel = new double[4];
        private readonly FrameClock _clock;
        private readonly Stopwatch _watch = new();
        private double _lapT, _lastMs;
        private CoreNav.SectionEdgeMotion _motion = CoreNav.SectionEdgeMotion.Fixed;

        public SectionEdgeLift()
        {
            IsHitTestVisible = false;
            Focusable = false;
            double px = CoreNav.SectionEdgeRules.LiftStripPx, inset = CoreNav.SectionEdgeRules.LiftStripInset;
            for (int i = 0; i < 4; i++)
            {
                var side = Sides[i];
                bool horizontal = side is CoreFx.EdgeSide.Top or CoreFx.EdgeSide.Bottom;
                var move = new TranslateTransform();
                var band = new Rectangle
                {
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    RenderTransform = move,
                };
                var track = new Panel
                {
                    Name = "SectionEdgeLift" + side,
                    IsHitTestVisible = false,
                    ClipToBounds = true,
                    Children = { band },
                };
                if (horizontal)
                {
                    track.Height = px;
                    track.Margin = new Thickness(inset, 0, inset, 0);
                    track.VerticalAlignment = side == CoreFx.EdgeSide.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                }
                else
                {
                    track.Width = px;
                    track.Margin = new Thickness(0, inset, 0, inset);
                    track.HorizontalAlignment = side == CoreFx.EdgeSide.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                }
                // Re-place the band when the strip resizes (the offset is relative to its length).
                int k = i;
                track.SizeChanged += (_, _) => Place(k);
                Children.Add(track);
                _tracks[i] = track;
                _bands[i] = band;
                _moves[i] = move;
                _rel[i] = CoreNav.SectionEdgeRules.LiftTravel(side).From;
            }
            _clock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(1000.0 / CoreNav.SectionEdgeRules.SpinFps) };
            _clock.Tick += (_, _) => Tick();
        }

        /// <summary>What the lift is doing (tests).</summary>
        internal CoreNav.SectionEdgeMotion Motion => _motion;

        /// <summary>True while the lap clock runs (tests).</summary>
        internal bool IsSpinning => _clock.IsEnabled;

        /// <summary>Seconds into the current lap (tests).</summary>
        internal double LapSeconds => _lapT;

        /// <summary>A side's band offset, in strip lengths (-1 and 1 = off the strip).</summary>
        internal double OffsetOf(CoreFx.EdgeSide side) => _rel[(int)side];

        /// <summary>
        /// A side's leg inside the lap, WPF BuildLiftLeg verbatim: parked at its start until its
        /// turn, across the side in a quarter of the lap (linear), parked at its end for the rest.
        /// </summary>
        internal static double LegAt(CoreFx.EdgeSide side, double lapSeconds)
        {
            double lap = CoreNav.SectionEdgeRules.SpinSeconds;
            double t = lapSeconds % lap;
            if (t < 0) t += lap;
            var (from, to) = CoreNav.SectionEdgeRules.LiftTravel(side);
            double start = CoreNav.SectionEdgeRules.LiftLegStart(side);
            double leg = CoreNav.SectionEdgeRules.LiftLegSeconds;
            if (t < start) return from;
            if (t >= start + leg) return to;
            return from + (to - from) * ((t - start) / leg);
        }

        /// <summary>Paints the three stops (offsets SectionEdgeRules.LiftOffsets) on all four bands.</summary>
        public void SetStops(uint[] stops)
        {
            var offsets = CoreNav.SectionEdgeRules.LiftOffsets;
            var tuples = new (uint, double)[Math.Min(stops.Length, offsets.Length)];
            for (int i = 0; i < tuples.Length; i++) tuples[i] = (stops[i], offsets[i]);
            var across = NavPaint.Horizontal(tuples);
            var down = NavPaint.Vertical(tuples);
            for (int i = 0; i < 4; i++)
                _bands[i].Fill = Sides[i] is CoreFx.EdgeSide.Top or CoreFx.EdgeSide.Bottom ? across : down;
        }

        /// <summary>
        /// Starts, pauses, resumes or parks the lift. Spin runs the lap only while
        /// <paramref name="run"/> (window active, shown, not minimised) and keeps its place when
        /// paused; Fixed holds the top lift at the centre; Solid hides the strips.
        /// </summary>
        public void Apply(CoreNav.SectionEdgeMotion motion, bool run)
        {
            IsVisible = motion != CoreNav.SectionEdgeMotion.Solid;
            if (motion != CoreNav.SectionEdgeMotion.Spin)
            {
                _clock.Stop();
                _watch.Reset();
                _lapT = 0;
                _motion = motion;
                for (int i = 0; i < 4; i++) SetRel(i, CoreNav.SectionEdgeRules.LiftRest(Sides[i], motion));
                return;
            }
            if (_motion != CoreNav.SectionEdgeMotion.Spin)
            {
                // A fresh lap, as WPF's new clocks start one.
                _lapT = 0;
                for (int i = 0; i < 4; i++) SetRel(i, LegAt(Sides[i], 0));
            }
            _motion = motion;
            if (run)
            {
                if (!_clock.IsEnabled)
                {
                    _watch.Restart();
                    _lastMs = 0;
                    _clock.Start();
                }
            }
            else
            {
                _clock.Stop();
                _watch.Reset();
            }
        }

        private void Tick()
        {
            double now = _watch.Elapsed.TotalMilliseconds;
            // A long gap (a stalled frame) moves the lap on by at most a few frames, never a jump.
            double gap = Math.Clamp(now - _lastMs, 0, 250);
            _lastMs = now;
            Step(gap / 1000.0);
        }

        /// <summary>Advances the lap (tests call it; the clock calls it every frame).</summary>
        internal void Step(double seconds)
        {
            _lapT = (_lapT + seconds) % CoreNav.SectionEdgeRules.SpinSeconds;
            for (int i = 0; i < 4; i++) SetRel(i, LegAt(Sides[i], _lapT));
        }

        private void SetRel(int i, double rel)
        {
            _rel[i] = rel;
            Place(i);
        }

        private void Place(int i)
        {
            var b = _tracks[i].Bounds;
            bool horizontal = Sides[i] is CoreFx.EdgeSide.Top or CoreFx.EdgeSide.Bottom;
            if (horizontal) { _moves[i].X = _rel[i] * b.Width; _moves[i].Y = 0; }
            else { _moves[i].Y = _rel[i] * b.Height; _moves[i].X = 0; }
        }

        /// <summary>Stop the lap when the control leaves the tree.</summary>
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _clock.Stop();
            base.OnDetachedFromVisualTree(e);
        }
    }
}
