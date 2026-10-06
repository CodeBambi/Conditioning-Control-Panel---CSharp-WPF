using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// The section edge's embers (nav polish 9, proposal section 4): four thin
    /// <see cref="AmbientFxCanvas"/> strips, one per window side, each running only the
    /// <see cref="AmbientFxLayers.EdgeDrift"/> layer in the section hue. Four 30 px strips, not one
    /// full-window canvas: Skia rasters its whole surface every tick, and the ring is 4% of the
    /// window's pixels.
    ///
    /// <para>Nothing is allocated or started unless <see cref="MotionFx.AllowParticles"/> holds
    /// (Full motion on a tier with a particle budget). Reduced and Off get no strips at all, which
    /// is the static version of the design: the edge line and its glow, still. Each canvas keeps
    /// its own gating (window active, visible, ambient loops) and the tier's frame rate.</para>
    /// </summary>
    public class EdgeParticles : Grid
    {
        /// <summary>Strip thickness in native pixels (outside the Viewbox).</summary>
        public const double StripThickness = 30;
        /// <summary>Alpha multiplier handed to every strip.</summary>
        public const double StripIntensity = 0.55;
        /// <summary>Dust density handed to every strip (the strips run no dust; kept per the spec).</summary>
        public const double StripDustDensity = 0.35;

        private static readonly EdgeSide[] Sides = { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left };

        private readonly List<AmbientFxCanvas> _strips = new();
        private Color _hue = Color.FromRgb(0xB7, 0x9C, 0xFF);
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

        /// <summary>The live strips, Top, Right, Bottom, Left (empty when particles are not allowed).</summary>
        public IReadOnlyList<AmbientFxCanvas> Strips => _strips;

        /// <summary>The hue the strips paint (or will paint once mounted).</summary>
        public Color Hue => _hue;

        /// <summary>The pure gate: Full motion and a tier with a particle budget.</summary>
        public static bool ShouldMount(MotionLevel level, bool tierAllowsParticles) =>
            level == MotionLevel.Full && tierAllowsParticles;

        private bool Allowed()
        {
            if (MotionOverride is null && TierAllowsParticlesOverride is null)
                return MotionFx.AllowParticles;
            var level = MotionOverride ?? MotionFx.Level;
            var current = PerformanceProfile.CurrentTier;
            bool tier = TierAllowsParticlesOverride
                ?? (PerformanceProfile.AllowAmbientMotion(current) && PerformanceProfile.MaxAmbientParticles(current) > 0);
            return ShouldMount(level, tier);
        }

        /// <summary>
        /// Call once from the window after load with the current section hue. Re-checks the motion
        /// gate every time, so calling it again after a motion level change builds or tears down
        /// the strips to match.
        /// </summary>
        public void Mount(Color hue)
        {
            _hue = hue;
            _mountAsked = true;
            try
            {
                if (!Allowed())
                {
                    Teardown();
                    return;
                }
                if (_strips.Count == 0) Build();
                else Retint(hue);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EdgeParticles.Mount: {E}", ex.Message);
            }
        }

        /// <summary>Re-run the motion gate with the last hue (the motion level setting changed).</summary>
        public void Refresh()
        {
            if (_mountAsked) Mount(_hue);
        }

        /// <summary>Swap the hue on every live strip without reseeding.</summary>
        public void Retint(Color hue)
        {
            _hue = hue;
            foreach (var strip in _strips)
                strip.Retint(hue);
        }

        private void Build()
        {
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
                    Layers = AmbientFxLayers.EdgeDrift,
                    EdgeSide = side,
                    Tint = _hue,
                    Intensity = StripIntensity,
                    DustDensity = StripDustDensity,
                });
            }
        }

        private void Teardown()
        {
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
