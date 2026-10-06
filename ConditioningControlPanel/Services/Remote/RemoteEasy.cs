using System;

namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>
    /// The subject's Easy button, as arithmetic. Each press halves the factor, never below
    /// <see cref="Floor"/>; remote haptics and the remote-set spiral / pink opacity are multiplied
    /// by it. Pure, so the HUD and the tests agree on what "half strength" means.
    /// </summary>
    public static class RemoteEasy
    {
        public const double Floor = 0.25;

        public static double Next(double factor) => Math.Max(Floor, Math.Clamp(factor, Floor, 1.0) / 2);

        /// <summary>An opacity the controller asked for, eased. Never rounds a non-zero ask to zero.</summary>
        public static int Scale(int requested, double factor)
        {
            if (requested <= 0) return 0;
            var v = (int)Math.Round(requested * Math.Clamp(factor, 0, 1), MidpointRounding.AwayFromZero);
            return Math.Max(1, v);
        }
    }

    /// <summary>
    /// One overlay opacity (spiral or pink) under Easy. Remembers what the controller asked for
    /// and, when Easy had to touch the subject's own value, what that value was, so the end of
    /// the session can hand it back.
    /// </summary>
    public sealed class EasedOpacity
    {
        /// <summary>Unscaled value the controller asked for (or the subject's own, once Easy took it over).</summary>
        public int? Requested { get; private set; }
        /// <summary>The subject's own value before Easy first changed it, to restore at session end.</summary>
        public int? SubjectOriginal { get; private set; }

        /// <summary><c>set_*_opacity</c>: the controller's ask, eased. Returns the value to store.</summary>
        public int Ask(int requested, int max, double factor)
        {
            Requested = Math.Clamp(requested, 0, max);
            return RemoteEasy.Scale(Requested.Value, factor);
        }

        /// <summary>Easy was pressed. Returns the new value to store, or null when this overlay
        /// is neither controller-set nor showing.</summary>
        public int? Rescale(int current, bool showing, double factor)
        {
            if (Requested == null)
            {
                if (!showing) return null;
                SubjectOriginal ??= current;
                Requested = current;
            }
            return RemoteEasy.Scale(Requested.Value, factor);
        }

        public void Reset()
        {
            Requested = null;
            SubjectOriginal = null;
        }
    }
}
