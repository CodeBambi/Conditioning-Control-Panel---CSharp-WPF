using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/Notifications/BrainDrainVisualPolicy.cs.
    /// Brain Drain is one feature with two halves: the audio (clips at random moments) and the
    /// picture (a haze over the desktop). The picture dial is <c>BrainDrainBlurStrength</c>, and 0
    /// means no picture at all - not "the faintest picture".
    /// </summary>
    public static class BrainDrainVisualPolicy
    {
        /// <summary>The strength that means "no picture". Lowest the slider goes.</summary>
        public const int Off = 0;

        /// <summary>True when this strength draws nothing at all.</summary>
        public static bool IsSilent(int blurStrength) => blurStrength <= Off;

        /// <summary>Does the BASE feature want the blur up? The feature has to be on and its visual
        /// dial above zero.</summary>
        public static bool WantsBlur(bool featureEnabled, int blurStrength) =>
            featureEnabled && !IsSilent(blurStrength);
    }

    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/Notifications/BrainDrainKeepClear.cs.
    /// "Keep pictures clear": where the blur windows have to sit in the topmost band so the app's
    /// own pictures stay sharp. Pure, so the rule is pinned without a window.
    /// </summary>
    public static class BrainDrainKeepClear
    {
        /// <summary>One visible topmost window, in z-order (index 0 is the top).</summary>
        public readonly record struct BandEntry(IntPtr Hwnd, bool Ours, bool Drain);

        /// <summary>The window the blur must be put directly under, or zero when it already sits
        /// below every window this process owns (or there is nothing to sort).</summary>
        public static IntPtr AnchorIfNeeded(IReadOnlyList<BandEntry> band)
        {
            int lowestOurs = -1, highestDrain = -1;
            for (int i = 0; i < band.Count; i++)
            {
                var e = band[i];
                if (e.Drain) { if (highestDrain < 0) highestDrain = i; }
                else if (e.Ours) lowestOurs = i;
            }
            if (lowestOurs < 0 || highestDrain < 0) return IntPtr.Zero;
            return highestDrain < lowestOurs ? band[lowestOurs].Hwnd : IntPtr.Zero;
        }
    }
}

namespace ConditioningControlPanel.Services.Compositor
{
    /// <summary>
    /// The numbers of WPF Services/Compositor/BrainDrainLayer.cs and BrainDrainCapturePump.cs, same
    /// values: how strong the haze is for a dial position. The head owns the capture and the draw
    /// (CCP.Avalonia Views/Overlays/BrainDrainOverlay).
    /// </summary>
    public static class BrainDrainLayerRules
    {
        /// <summary>Blur strength per intensity point, SOURCE px per unit (WPF RadiusScale).</summary>
        public const double RadiusScale = 0.14;

        public const int AlphaFullIntensity = 100;
        public const double AlphaFloor = 0.10;
        public const double AlphaCeiling = 0.85;
        public const double AlphaCurveExponent = 0.66;

        /// <summary>WPF PerformanceProfile.BrainDrainDownscale at the Quality tier (the port has no tiers).</summary>
        public const int Downscale = 4;

        /// <summary>WPF BrainDrainLayer.Start: 60 in high refresh, else 30.</summary>
        public static int Fps(bool highRefresh) => highRefresh ? 60 : 30;

        /// <summary>WPF BrainDrainLayer.AlphaFor: 0.10 at 1, rising by the exponent to 0.85 at 100
        /// and above. Never fully opaque: the real screen always ghosts through.</summary>
        public static byte AlphaFor(int intensity)
        {
            int i = Math.Clamp(intensity, 1, AlphaFullIntensity);
            double t = (i - 1) / (AlphaFullIntensity - 1.0);
            double a = AlphaFloor + (AlphaCeiling - AlphaFloor) * Math.Pow(t, AlphaCurveExponent);
            return (byte)Math.Clamp(Math.Round(a * 255.0), 0, 255);
        }

        /// <summary>WPF SetIntensity + Sigma: the gaussian sigma on the DOWNSCALED capture
        /// (radius = intensity x RadiusScale / downscale, sigma = radius / 3).</summary>
        public static float SigmaFor(int intensity, int downscale = Downscale) =>
            (float)(intensity * RadiusScale / Math.Max(1, downscale) / 3.0);

        /// <summary>WPF SetIntensity: the melt displacement scale in SOURCE px, amp = 1 + intensity x 0.045
        /// quoted at downscale 4, clamped at the legacy 200 ceiling.</summary>
        public static float MeltAmplitudeFor(int intensity, int downscale = Downscale) =>
            (float)((1.0 + Math.Clamp(intensity, 0, 200) * 0.045) * 4.0 / Math.Max(1, downscale));

        /// <summary>WPF CreateSlot: the downscaled capture size, even numbers, never under 2.</summary>
        public static (int W, int H) CaptureSize(int width, int height, int downscale = Downscale) =>
            (Math.Max(2, (width / Math.Max(1, downscale)) & ~1), Math.Max(2, (height / Math.Max(1, downscale)) & ~1));
    }
}
