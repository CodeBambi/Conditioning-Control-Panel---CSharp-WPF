// PORTED from WPF 7.1.5 ConditioningControlPanel/Controls/NavRail/EdgeParticles.cs (nav polish 9
// embers, polish 11 fog). The gate and numbers are Core (ConditioningControlPanel.Fx
// EdgeParticleRules, SectionEdgeRules.FogGain); this file builds and retints the four strips.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using Serilog;
using CoreFx = global::ConditioningControlPanel.Fx;
using CoreNav = global::ConditioningControlPanel.Nav;

namespace ConditioningControlPanel.Avalonia.Controls.NavRail
{
    /// <summary>
    /// The section edge's living light: four <see cref="AmbientFxCanvas"/> strips, one per window
    /// side, in the section hue. Each strip runs the EdgeFog layer (soft puffs drifting along the
    /// edge) and, at Full, the EdgeDrift embers on top, kept to their 30 px band. Four 56 px strips,
    /// never one full-window canvas: Skia rasters its whole surface every tick.
    ///
    /// <para>Nothing is allocated unless the tier allows ambient motion and has a particle budget
    /// and the motion level is not Off. Each canvas keeps its own gating (window active, visible)
    /// and the frame-locked clock of lane C's <c>FrameClock</c>.</para>
    /// </summary>
    public class EdgeParticles : Grid
    {
        private readonly List<AmbientFxCanvas> _strips = new();
        private uint _hue = CoreNav.NavStripRules.Lilac;
        private CoreFx.EdgeFogMode _mode = CoreFx.EdgeFogMode.None;
        private bool _mountAsked;

        /// <summary>Test seams: stand in for the user's motion level and the tier's particle budget.</summary>
        internal MotionLevel? MotionOverride { get; set; }
        internal bool? TierAllowsParticlesOverride { get; set; }

        public EdgeParticles()
        {
            IsHitTestVisible = false;
            Focusable = false;
        }

        /// <summary>The live strips, Top, Right, Bottom, Left (empty when nothing is allowed).</summary>
        public IReadOnlyList<AmbientFxCanvas> Strips => _strips;

        /// <summary>The hue the strips paint (or will paint once mounted), ARGB.</summary>
        public uint Hue => _hue;

        /// <summary>What the live strips run right now.</summary>
        public CoreFx.EdgeFogMode Mode => _mode;

        /// <summary>True while the fog runs: the section edge thins its static band to match.</summary>
        public bool FogLive => _mode != CoreFx.EdgeFogMode.None && _strips.Count > 0;

        private CoreFx.EdgeFogMode CurrentMode()
        {
            var level = MotionOverride ?? AmbientFxCanvas.Env.Level;
            bool tier = TierAllowsParticlesOverride
                ?? CoreFx.EdgeParticleRules.TierAllowsParticles(AmbientFxCanvas.Env.CurrentTier);
            return CoreFx.EdgeParticleRules.ModeFor(level, tier);
        }

        /// <summary>
        /// Call from the section edge's painter with the section hue and its crossfade time.
        /// Re-checks the gate every time: a motion level or tier change builds, rebuilds or tears
        /// down the strips to match; otherwise the live strips crossfade to the new hue.
        /// </summary>
        public void Mount(uint hue, int fadeMs = 0)
        {
            _hue = hue;
            _mountAsked = true;
            try
            {
                var mode = CurrentMode();
                if (mode == CoreFx.EdgeFogMode.None)
                {
                    Teardown();
                    return;
                }
                if (mode != _mode || _strips.Count == 0)
                {
                    Teardown();
                    _mode = mode;
                    Build();
                }
                else Retint(hue, fadeMs);
            }
            catch (Exception ex) { Log.Debug("EdgeParticles.Mount: {E}", ex.Message); }
        }

        /// <summary>Re-run the motion gate with the last hue (the motion level setting changed).</summary>
        public void Refresh()
        {
            if (_mountAsked) Mount(_hue, 0);
        }

        /// <summary>Crossfade the fog to <paramref name="hue"/> over <paramref name="fadeMs"/>;
        /// the embers swap at once (a 2 px mote reads no crossfade).</summary>
        public void Retint(uint hue, int fadeMs)
        {
            _hue = hue;
            var c = NavPaint.C(hue);
            double gain = CoreNav.SectionEdgeRules.FogGain(hue);
            foreach (var strip in _strips)
            {
                strip.Retint(c);
                strip.SetEdgeFog(c, gain, fadeMs);
            }
        }

        private void Build()
        {
            // The head's canvas enum carries the same flag values as Core's (pinned by the WPF tests).
            var layers = (AmbientFxLayers)(int)CoreFx.EdgeParticleRules.LayersFor(_mode);
            var tint = NavPaint.C(_hue);
            foreach (var side in CoreFx.EdgeParticleRules.Sides)
            {
                var strip = new AmbientFxCanvas { Name = "EdgeStrip" + side };
                if (CoreFx.EdgeParticleRules.IsHorizontal(side))
                {
                    strip.Height = CoreFx.EdgeParticleRules.StripThickness;
                    strip.HorizontalAlignment = HorizontalAlignment.Stretch;
                    strip.VerticalAlignment = side == CoreFx.EdgeSide.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                }
                else
                {
                    strip.Width = CoreFx.EdgeParticleRules.StripThickness;
                    strip.VerticalAlignment = VerticalAlignment.Stretch;
                    strip.HorizontalAlignment = side == CoreFx.EdgeSide.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                }
                Children.Add(strip);
                _strips.Add(strip);
                strip.StartLayers(new AmbientFxConfig
                {
                    Layers = layers,
                    // Same order and values on both enums; the cast survives the Core dedupe.
                    EdgeSide = (EdgeSide)(int)side,
                    Tint = tint,
                    Intensity = CoreFx.EdgeParticleRules.StripIntensity,
                    DustDensity = CoreFx.EdgeParticleRules.StripDustDensity,
                    EdgeDriftBandPx = CoreFx.EdgeParticleRules.EmberBandPx,
                    EdgeFogReduced = _mode == CoreFx.EdgeFogMode.Reduced,
                    EdgeFogGain = CoreNav.SectionEdgeRules.FogGain(_hue),
                });
            }
        }

        private void Teardown()
        {
            _mode = CoreFx.EdgeFogMode.None;
            if (_strips.Count == 0) return;
            foreach (var strip in _strips)
            {
                try { strip.Stop(); } catch { }
            }
            _strips.Clear();
            Children.Clear();
        }
    }
}
