using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>What the section edge's strips run at a motion level and tier.</summary>
    public enum EdgeFogMode
    {
        /// <summary>No strips at all: the static band and the line are the design (Off, or a tier
        /// with no particle budget).</summary>
        None,
        /// <summary>Reduced: the fog alone, half the puffs at half the speed. No embers.</summary>
        Reduced,
        /// <summary>Full: the whole fog plus the small embers as sparkle.</summary>
        Full,
    }

    /// <summary>
    /// The section edge's living light (nav polish 9 embers, polish 11 fog): four
    /// <see cref="AmbientFxCanvas"/> strips, one per window side, in the section hue. Each strip
    /// runs the <see cref="AmbientFxLayers.EdgeFog"/> layer (soft puffs that drift along the edge,
    /// breathe in and out and fade, a slow big layer under a quicker small one) and, at Full, the
    /// <see cref="AmbientFxLayers.EdgeDrift"/> embers on top, kept to their 30 px band. Four
    /// 56 px strips, not one full-window canvas: Skia rasters its whole surface every tick, and
    /// the ring is under a fifth of the window's pixels.
    ///
    /// <para>Nothing is allocated unless the tier allows ambient motion and has a particle budget
    /// and the motion level is not Off. Off gets no strips (the static band reads as the hue).
    /// Each canvas keeps its own gating (window active, visible) and the tier's frame rate.</para>
    /// </summary>
    public class EdgeParticles : Grid
    {
        /// <summary>Strip thickness in native pixels (outside the Viewbox): the fog's depth.</summary>
        public const double StripThickness = EdgeFogMath.StripPx;
        /// <summary>The embers keep to their wave 9 band inside the deeper strip.</summary>
        public const double EmberBandPx = 30;
        /// <summary>Alpha multiplier on the embers.</summary>
        public const double StripIntensity = 0.55;
        /// <summary>Dust density handed to every strip (the strips run no dust; kept per the spec).</summary>
        public const double StripDustDensity = 0.35;

        private static readonly EdgeSide[] Sides = { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left };

        private readonly List<AmbientFxCanvas> _strips = new();
        private Color _hue = Color.FromRgb(0xB7, 0x9C, 0xFF);
        private EdgeFogMode _mode = EdgeFogMode.None;
        private bool _mountAsked;

        /// <summary>Test seams: stand in for the user's motion level and the tier's particle budget.</summary>
        internal MotionLevel? MotionOverride { get; set; }
        internal bool? TierAllowsParticlesOverride { get; set; }

        public EdgeParticles()
        {
            IsHitTestVisible = false;
            Focusable = false;
            SnapsToDevicePixels = true;
        }

        /// <summary>The live strips, Top, Right, Bottom, Left (empty when nothing is allowed).</summary>
        public IReadOnlyList<AmbientFxCanvas> Strips => _strips;

        /// <summary>The hue the strips paint (or will paint once mounted).</summary>
        public Color Hue => _hue;

        /// <summary>What the live strips run right now.</summary>
        public EdgeFogMode Mode => _mode;

        /// <summary>True while the fog runs: the section edge thins its static band to match.</summary>
        public bool FogLive => _mode != EdgeFogMode.None && _strips.Count > 0;

        /// <summary>The pure gate for the embers: Full motion and a tier with a particle budget.</summary>
        public static bool ShouldMount(MotionLevel level, bool tierAllowsParticles) =>
            ModeFor(level, tierAllowsParticles) == EdgeFogMode.Full;

        /// <summary>The pure gate: Off or no budget = none, Reduced = half fog, Full = everything.</summary>
        public static EdgeFogMode ModeFor(MotionLevel level, bool tierAllowsParticles) =>
            !tierAllowsParticles || level == MotionLevel.Off ? EdgeFogMode.None
            : level == MotionLevel.Reduced ? EdgeFogMode.Reduced
            : EdgeFogMode.Full;

        /// <summary>The layers a strip runs in a mode.</summary>
        public static AmbientFxLayers LayersFor(EdgeFogMode mode) => mode switch
        {
            EdgeFogMode.Full => AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift,
            EdgeFogMode.Reduced => AmbientFxLayers.EdgeFog,
            _ => AmbientFxLayers.None,
        };

        private EdgeFogMode CurrentMode()
        {
            var level = MotionOverride ?? MotionFx.Level;
            var current = PerformanceProfile.CurrentTier;
            bool tier = TierAllowsParticlesOverride
                ?? (PerformanceProfile.AllowAmbientMotion(current) && PerformanceProfile.MaxAmbientParticles(current) > 0);
            return ModeFor(level, tier);
        }

        /// <summary>Mount with no crossfade (the first paint).</summary>
        public void Mount(Color hue) => Mount(hue, 0);

        /// <summary>
        /// Call from the section edge's painter with the section hue and its crossfade time.
        /// Re-checks the gate every time: a motion level or tier change builds, rebuilds or tears
        /// down the strips to match; otherwise the live strips crossfade to the new hue.
        /// </summary>
        public void Mount(Color hue, int fadeMs)
        {
            _hue = hue;
            _mountAsked = true;
            try
            {
                var mode = CurrentMode();
                if (mode == EdgeFogMode.None)
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
            catch (Exception ex)
            {
                App.Logger?.Debug("EdgeParticles.Mount: {E}", ex.Message);
            }
        }

        /// <summary>Re-run the motion gate with the last hue (the motion level setting changed).</summary>
        public void Refresh()
        {
            if (_mountAsked) Mount(_hue, 0);
        }

        /// <summary>Swap the hue on every live strip at once.</summary>
        public void Retint(Color hue) => Retint(hue, 0);

        /// <summary>Crossfade the fog to <paramref name="hue"/> over <paramref name="fadeMs"/>;
        /// the embers swap at once (a 2 px mote reads no crossfade).</summary>
        public void Retint(Color hue, int fadeMs)
        {
            _hue = hue;
            double gain = SectionEdgeRules.FogGain(hue);
            foreach (var strip in _strips)
            {
                strip.Retint(hue);
                strip.SetEdgeFog(hue, gain, fadeMs);
            }
        }

        private void Build()
        {
            var layers = LayersFor(_mode);
            foreach (var side in Sides)
            {
                var strip = new AmbientFxCanvas();
                bool horizontal = side is EdgeSide.Top or EdgeSide.Bottom;
                if (horizontal)
                {
                    strip.Height = StripThickness;
                    strip.HorizontalAlignment = HorizontalAlignment.Stretch;
                    strip.VerticalAlignment = side == EdgeSide.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                }
                else
                {
                    strip.Width = StripThickness;
                    strip.VerticalAlignment = VerticalAlignment.Stretch;
                    strip.HorizontalAlignment = side == EdgeSide.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                }
                Children.Add(strip);
                _strips.Add(strip);
                strip.StartLayers(new AmbientFxConfig
                {
                    Layers = layers,
                    EdgeSide = side,
                    Tint = _hue,
                    Intensity = StripIntensity,
                    DustDensity = StripDustDensity,
                    EdgeDriftBandPx = EmberBandPx,
                    EdgeFogReduced = _mode == EdgeFogMode.Reduced,
                    EdgeFogGain = SectionEdgeRules.FogGain(_hue),
                });
            }
        }

        private void Teardown()
        {
            _mode = EdgeFogMode.None;
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
