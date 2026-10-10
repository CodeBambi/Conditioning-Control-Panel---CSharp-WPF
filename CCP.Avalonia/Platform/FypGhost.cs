using System;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// One live For You ghost: the real feed window is parked off the virtual desktop and a
    /// see-through, click-through mirror of it stands on its home monitor (WPF 7.1.5
    /// Services/Fyp/FypGhostOverlay.cs). Dispose drops the mirror and its two buttons and puts the
    /// real window back where it was. Idempotent.
    /// </summary>
    internal interface IFypGhostSession : IDisposable
    {
        /// <summary>The page's opacity slider, live (0.01..1).</summary>
        void SetOpacity(double opacity);

        /// <summary>The speaker button's glyph after the host flipped mute.</summary>
        void SetMuted(bool muted);

        /// <summary>The watchdog's question: is the mirror still see-through AND click-through, and
        /// is its source still being composed? False = take the ghost down (never leave an opaque
        /// or click-eating topmost sheet). Also re-asserts the mirror's place on top.</summary>
        bool Healthy();
    }

    /// <summary>The platform seam for For You ghost mode. Windows: <see cref="WindowsFypGhost"/>.
    /// Everywhere else (and in every headless test): <see cref="FypGhostUnavailable"/>.</summary>
    internal interface IFypGhostPlatform
    {
        /// <summary>
        /// Park <paramref name="sourceHwnd"/> and show its mirror. Null = it could not be made to
        /// compose; <paramref name="reason"/> says why, and NOTHING is left on screen or moved.
        /// </summary>
        IFypGhostSession? Enter(IntPtr sourceHwnd, double opacity, bool muted, Action onGear, Action onMute, out string? reason);
    }

    /// <summary>No ghost on this platform: the page gets WPF's own "unavailable" answer.</summary>
    internal sealed class FypGhostUnavailable : IFypGhostPlatform
    {
        public const string Reason = "not on this build";
        public static readonly FypGhostUnavailable Instance = new();

        public IFypGhostSession? Enter(IntPtr sourceHwnd, double opacity, bool muted, Action onGear, Action onMute, out string? reason)
        {
            reason = Reason;
            return null;
        }
    }

    /// <summary>The head-free decisions of the mirror (WPF FypGhostOverlay.Diagnose and the cover
    /// crop of UpdateThumbnailBounds), so they are testable without a desktop or a DWM.</summary>
    internal static class FypGhostRules
    {
        public const double OpacityMin = 0.01;

        public static double ClampOpacity(double v) => double.IsFinite(v) ? Math.Clamp(v, OpacityMin, 1.0) : 1.0;

        /// <summary>DWM_TNP_OPACITY for a slider value.</summary>
        public static byte OpacityByte(double v) => (byte)Math.Round(ClampOpacity(v) * 255.0);

        /// <summary>
        /// The verdict as a pure function of what the native calls said. Order matters: composition
        /// off explains every failure downstream of it; a rejected colour key explains a black sheet
        /// whether or not the thumbnail registered; a zero source size means DWM holds a registration
        /// with nothing to draw through it. Null = good to show.
        /// </summary>
        public static string? Diagnose(bool compositionEnabled, bool colorKeyApplied, int registerHr, int sourceWidth, int sourceHeight)
        {
            if (!compositionEnabled) return "composition-disabled";
            if (!colorKeyApplied) return "colorkey-rejected";
            if (registerHr != 0) return $"thumbnail-register-failed-0x{registerHr:X8}";
            if (sourceWidth <= 0 || sourceHeight <= 0) return "thumbnail-source-empty";
            return null;
        }

        /// <summary>COVER, not letterbox: the destination is the whole monitor and an aspect
        /// mismatch crops the SOURCE symmetrically (a bare desktop stripe is visible, a few cropped
        /// rows are not). Returns the source rectangle in the source's client pixels.</summary>
        public static (int Left, int Top, int Right, int Bottom) CoverSource(int srcW, int srcH, int destW, int destH)
        {
            destW = Math.Max(1, destW);
            destH = Math.Max(1, destH);
            if (srcW <= 0 || srcH <= 0) return (0, 0, 0, 0);
            double destAspect = (double)destW / destH;
            double srcAspect = (double)srcW / srcH;
            int x0 = 0, y0 = 0, x1 = srcW, y1 = srcH;
            if (srcAspect > destAspect)
            {
                int w = Math.Max(1, (int)Math.Round(srcH * destAspect));
                x0 = (srcW - w) / 2; x1 = x0 + w;
            }
            else if (srcAspect < destAspect)
            {
                int h = Math.Max(1, (int)Math.Round(srcW / destAspect));
                y0 = (srcH - h) / 2; y1 = y0 + h;
            }
            return (x0, y0, x1, y1);
        }
    }
}
