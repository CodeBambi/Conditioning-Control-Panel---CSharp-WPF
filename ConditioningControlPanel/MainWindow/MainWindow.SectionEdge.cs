using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Nav polish wave 9 (2026-10-06): the section edge. The window's 3 px frame and a faint
    // 28 px band inside it wear the section hue; PaintSectionWash calls PaintSectionEdge with the
    // same crossfade time as the page wash. Rules (alphas, stops, motion) live in SectionEdgeRules.
    public partial class MainWindow
    {
        /// <summary>The glow band's stops (four brushes x three stops) and the line's three.</summary>
        private readonly List<GradientStop> _edgeGlowStops = new();
        private readonly List<GradientStop> _edgeLineStops = new();
        private Color _edgeHue = NavStripRules.Lilac;
        private bool _edgeReady;
        private AnimationClock? _edgeSpinClock;

        /// <summary>Ember strips (the particles lane) hook in here; a no-op until they land.</summary>
        partial void RetintEdgeParticles(Color hue);

        /// <summary>Collects the edge's brushes and wires the spin to the window's state. Called
        /// once after load, beside InitializeNavRail. Paints Home first: the authored Lilac.</summary>
        private void InitializeSectionEdge()
        {
            if (_edgeReady) return;
            try
            {
                _edgeGlowStops.Clear();
                _edgeLineStops.Clear();
                if (SectionEdgeGlow != null)
                {
                    foreach (var child in SectionEdgeGlow.Children)
                    {
                        if (child is not Rectangle r || r.Fill is not LinearGradientBrush brush) continue;
                        if (brush.IsFrozen) { brush = brush.Clone(); r.Fill = brush; }
                        _edgeGlowStops.AddRange(brush.GradientStops);
                    }
                }

                var line = GlassWindowEdge?.BorderBrush as LinearGradientBrush;
                if (line != null)
                {
                    if (line.IsFrozen) { line = line.Clone(); GlassWindowEdge!.BorderBrush = line; }
                    _edgeLineStops.AddRange(line.GradientStops);
                }

                _edgeReady = true;
                PaintSectionEdge(_edgeHue, 0);

                Activated += (_, _) => UpdateSectionEdgeMotion();
                Deactivated += (_, _) => UpdateSectionEdgeMotion();
                StateChanged += (_, _) => UpdateSectionEdgeMotion();
                IsVisibleChanged += (_, _) => UpdateSectionEdgeMotion();
            }
            catch (Exception ex) { App.Logger?.Debug("InitializeSectionEdge failed: {E}", ex.Message); }
        }

        /// <summary>Retints the frame line and the glow band to <paramref name="hue"/> over
        /// <paramref name="ms"/> (0 = at once), then re-checks the travelling lift.</summary>
        private void PaintSectionEdge(Color hue, int ms)
        {
            _edgeHue = hue;
            // A section change before load still lands: the first call collects the brushes
            // and paints this hue at once.
            if (!_edgeReady) { InitializeSectionEdge(); return; }
            try
            {
                var level = MotionFx.Level;

                var glow = SectionEdgeRules.GlowStops(hue);
                for (int i = 0; i < _edgeGlowStops.Count; i++)
                    AnimateEdgeStop(_edgeGlowStops[i], glow[i % glow.Length], ms);

                var line = SectionEdgeRules.LineStops(hue, level);
                for (int i = 0; i < _edgeLineStops.Count && i < line.Length; i++)
                    AnimateEdgeStop(_edgeLineStops[i], line[i], ms);

                RetintEdgeParticles(hue);
                UpdateSectionEdgeMotion();
            }
            catch (Exception ex) { App.Logger?.Debug("PaintSectionEdge failed: {E}", ex.Message); }
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
        /// level and window state. Full spins only while the window is active, shown and not
        /// minimised; Reduced and Off hold the lift still at the top centre.</summary>
        private void UpdateSectionEdgeMotion()
        {
            if (!_edgeReady || SectionEdgeSpin == null) return;
            try
            {
                var motion = SectionEdgeRules.MotionFor(MotionFx.Level, MotionFx.AllowAmbientLoops);
                if (motion != SectionEdgeMotion.Spin)
                {
                    if (_edgeSpinClock != null)
                    {
                        SectionEdgeSpin.ApplyAnimationClock(RotateTransform.AngleProperty, null);
                        _edgeSpinClock = null;
                    }
                    SectionEdgeSpin.Angle = SectionEdgeRules.FixedAngle;
                    return;
                }

                bool run = SectionEdgeRules.SpinShouldRun(motion, IsActive, IsVisible,
                                                          WindowState == WindowState.Minimized);
                if (_edgeSpinClock == null)
                {
                    if (!run) return;
                    var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(SectionEdgeRules.SpinSeconds))
                    { RepeatBehavior = RepeatBehavior.Forever };
                    Timeline.SetDesiredFrameRate(spin, SectionEdgeRules.SpinFps);
                    _edgeSpinClock = spin.CreateClock();
                    SectionEdgeSpin.ApplyAnimationClock(RotateTransform.AngleProperty, _edgeSpinClock);
                    return;
                }

                var controller = _edgeSpinClock.Controller;
                if (controller == null) return;
                if (run && _edgeSpinClock.IsPaused) controller.Resume();
                else if (!run && !_edgeSpinClock.IsPaused) controller.Pause();
            }
            catch (Exception ex) { App.Logger?.Debug("UpdateSectionEdgeMotion failed: {E}", ex.Message); }
        }
    }
}
