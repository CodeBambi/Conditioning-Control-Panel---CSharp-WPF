using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Nav polish wave 9 (2026-10-06): the section edge. The window's 3 px frame and a faint
    // 28 px band inside it wear the section hue; PaintSectionWash calls PaintSectionEdge with the
    // same crossfade time as the page wash. Rules (alphas, stops, motion) live in SectionEdgeRules.
    //
    // Review fix (2026-10-06): the travelling lift is no longer a brush transform on the
    // full-window line Border (that dirtied the whole window every tick). It is four 3 px lift
    // strips (SectionEdgeLift), each sliding its band along one edge in turn, so a tick repaints
    // a strip and nothing else.
    //
    // Nav polish wave 11 (2026-10-07): the band becomes a drifting fog of light. EdgeParticles
    // runs soft puffs in the hue along every side (Full: the whole fog plus the embers; Reduced:
    // half the puffs at half the speed); while they run, the flat band underneath thins to
    // SectionEdgeRules.FogBand at FogBandShare of its alpha. Off and low tiers keep the full band.
    public partial class MainWindow
    {
        /// <summary>The glow band's stops (four brushes x three stops) and the line's three.</summary>
        private readonly List<GradientStop> _edgeGlowStops = new();
        /// <summary>The four band rectangles (their depth thins while the fog runs).</summary>
        private readonly List<Rectangle> _edgeGlowBands = new();
        private bool _edgeFogLive;
        private readonly List<GradientStop> _edgeLineStops = new();
        /// <summary>The four lift strips' stops (three each) and their transforms, by side.</summary>
        private readonly List<GradientStop> _edgeLiftStops = new();
        private readonly Dictionary<EdgeSide, TranslateTransform> _edgeLiftMoves = new();
        private readonly Dictionary<EdgeSide, AnimationClock> _edgeLiftClocks = new();
        private Color _edgeHue = NavStripRules.Lilac;
        private bool _edgeReady;

        /// <summary>The fog and ember strips follow the edge's hue and crossfade with it. Mount
        /// re-checks the motion gate too, so a motion change lands here before the band reads
        /// <see cref="EdgeParticles.FogLive"/>.</summary>
        private void RetintEdgeParticles(Color hue, int ms) => SectionEdgeParticles?.Mount(hue, ms);

        /// <summary>Collects the edge's brushes and wires the lift to the window's state. Called
        /// once after load, beside InitializeNavRail. Paints Home first: the authored Lilac.</summary>
        private void InitializeSectionEdge()
        {
            if (_edgeReady) return;
            try
            {
                _edgeGlowStops.Clear();
                _edgeGlowBands.Clear();
                _edgeLineStops.Clear();
                _edgeLiftStops.Clear();
                _edgeLiftMoves.Clear();
                if (SectionEdgeGlow != null)
                {
                    foreach (var child in SectionEdgeGlow.Children)
                    {
                        if (child is not Rectangle r || r.Fill is not LinearGradientBrush brush) continue;
                        if (brush.IsFrozen) { brush = brush.Clone(); r.Fill = brush; }
                        _edgeGlowStops.AddRange(brush.GradientStops);
                        _edgeGlowBands.Add(r);
                    }
                }

                var line = GlassWindowEdge?.BorderBrush as LinearGradientBrush;
                if (line != null)
                {
                    if (line.IsFrozen) { line = line.Clone(); GlassWindowEdge!.BorderBrush = line; }
                    _edgeLineStops.AddRange(line.GradientStops);
                }

                if (SectionEdgeLift != null)
                {
                    foreach (var child in SectionEdgeLift.Children)
                    {
                        if (child is not Rectangle r || r.Fill is not LinearGradientBrush brush) continue;
                        if (brush.IsFrozen) { brush = brush.Clone(); r.Fill = brush; }
                        _edgeLiftStops.AddRange(brush.GradientStops);
                    }
                    _edgeLiftMoves[EdgeSide.Top] = SectionEdgeLiftTop;
                    _edgeLiftMoves[EdgeSide.Right] = SectionEdgeLiftRight;
                    _edgeLiftMoves[EdgeSide.Bottom] = SectionEdgeLiftBottom;
                    _edgeLiftMoves[EdgeSide.Left] = SectionEdgeLiftLeft;
                }

                _edgeReady = true;
                // Also mounts the fog strips (EdgeParticles decides by motion level and tier).
                PaintSectionEdge(_edgeHue, 0);

                Activated += (_, _) => UpdateSectionEdgeMotion();
                Deactivated += (_, _) => UpdateSectionEdgeMotion();
                StateChanged += (_, _) => UpdateSectionEdgeMotion();
                IsVisibleChanged += (_, _) => UpdateSectionEdgeMotion();
            }
            catch (Exception ex) { App.Logger?.Debug("InitializeSectionEdge failed: {E}", ex.Message); }
        }

        /// <summary>Retints the frame line, the glow band and the lift to <paramref name="hue"/>
        /// over <paramref name="ms"/> (0 = at once), then re-checks the travelling lift.</summary>
        private void PaintSectionEdge(Color hue, int ms)
        {
            _edgeHue = hue;
            // A section change before load still lands: the first call collects the brushes
            // and paints this hue at once.
            if (!_edgeReady) { InitializeSectionEdge(); return; }
            try
            {
                var level = MotionFx.Level;

                // The strips first: whether the fog runs decides how strong the band is.
                RetintEdgeParticles(hue, ms);
                bool fogLive = SectionEdgeParticles?.FogLive == true;
                if (fogLive != _edgeFogLive || ms <= 0) SetEdgeBandDepth(SectionEdgeRules.BandDepth(fogLive));
                _edgeFogLive = fogLive;

                var glow = SectionEdgeRules.GlowStops(hue, fogLive);
                for (int i = 0; i < _edgeGlowStops.Count; i++)
                    AnimateEdgeStop(_edgeGlowStops[i], glow[i % glow.Length], ms);

                var line = SectionEdgeRules.LineStops(hue, level);
                for (int i = 0; i < _edgeLineStops.Count && i < line.Length; i++)
                    AnimateEdgeStop(_edgeLineStops[i], line[i], ms);

                var lift = SectionEdgeRules.LiftStops(hue, level);
                for (int i = 0; i < _edgeLiftStops.Count; i++)
                    AnimateEdgeStop(_edgeLiftStops[i], lift[i % lift.Length], ms);

                UpdateSectionEdgeMotion();
            }
            catch (Exception ex) { App.Logger?.Debug("PaintSectionEdge failed: {E}", ex.Message); }
        }

        /// <summary>Sets the four bands' depth into the window (top and bottom take a height,
        /// the sides a width).</summary>
        private void SetEdgeBandDepth(double depth)
        {
            foreach (var band in _edgeGlowBands)
            {
                if (band.VerticalAlignment is VerticalAlignment.Top or VerticalAlignment.Bottom
                    && band.HorizontalAlignment == HorizontalAlignment.Stretch)
                    band.Height = depth;
                else
                    band.Width = depth;
            }
        }

        private static void AnimateEdgeStop(GradientStop stop, Color to, int ms)
        {
            if (ms <= 0)
            {
                stop.BeginAnimation(GradientStop.ColorProperty, null);
                stop.Color = to;
                return;
            }
            stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Starts, pauses, resumes or parks the travelling lift for the current motion
        /// level and window state. Full runs the lap only while the window is active, shown and
        /// not minimised; Reduced holds the lift still at the top centre; Off hides the strips.</summary>
        private void UpdateSectionEdgeMotion()
        {
            if (!_edgeReady || SectionEdgeLift == null || _edgeLiftMoves.Count == 0) return;
            try
            {
                var motion = SectionEdgeRules.MotionFor(MotionFx.Level, MotionFx.AllowAmbientLoops);
                SectionEdgeLift.Visibility = motion == SectionEdgeMotion.Solid ? Visibility.Collapsed : Visibility.Visible;
                if (motion != SectionEdgeMotion.Spin)
                {
                    StopEdgeLiftClocks();
                    foreach (var (side, move) in _edgeLiftMoves)
                        SetLiftRest(side, move, SectionEdgeRules.LiftRest(side, motion));
                    return;
                }

                bool run = SectionEdgeRules.SpinShouldRun(motion, IsActive, IsVisible,
                                                          WindowState == WindowState.Minimized);
                if (_edgeLiftClocks.Count == 0)
                {
                    if (!run) return;
                    foreach (var (side, move) in _edgeLiftMoves)
                    {
                        var clock = BuildLiftLeg(side).CreateClock();
                        _edgeLiftClocks[side] = clock;
                        move.ApplyAnimationClock(LiftAxis(side), clock);
                    }
                    return;
                }

                foreach (var clock in _edgeLiftClocks.Values)
                {
                    var controller = clock.Controller;
                    if (controller == null) continue;
                    if (run && clock.IsPaused) controller.Resume();
                    else if (!run && !clock.IsPaused) controller.Pause();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("UpdateSectionEdgeMotion failed: {E}", ex.Message); }
        }

        /// <summary>One side's leg inside the lap: parked at its start until its turn, across the
        /// side in a quarter of the lap, parked at its end for the rest. Forever, at the ambient
        /// frame rate; the four legs share one Duration so they stay in step.</summary>
        private static DoubleAnimationUsingKeyFrames BuildLiftLeg(EdgeSide side)
        {
            var (from, to) = SectionEdgeRules.LiftTravel(side);
            double start = SectionEdgeRules.LiftLegStart(side);
            var leg = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(SectionEdgeRules.SpinSeconds),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            leg.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (start > 0)
                leg.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(start))));
            leg.KeyFrames.Add(new LinearDoubleKeyFrame(to,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(start + SectionEdgeRules.LiftLegSeconds))));
            Timeline.SetDesiredFrameRate(leg, SectionEdgeRules.SpinFps);
            return leg;
        }

        private static DependencyProperty LiftAxis(EdgeSide side) =>
            side is EdgeSide.Top or EdgeSide.Bottom ? TranslateTransform.XProperty : TranslateTransform.YProperty;

        private static void SetLiftRest(EdgeSide side, TranslateTransform move, double at)
        {
            var axis = LiftAxis(side);
            move.ApplyAnimationClock(axis, null);
            move.SetValue(axis, at);
        }

        private void StopEdgeLiftClocks()
        {
            if (_edgeLiftClocks.Count == 0) return;
            foreach (var (side, move) in _edgeLiftMoves)
                move.ApplyAnimationClock(LiftAxis(side), null);
            _edgeLiftClocks.Clear();
        }
    }
}
