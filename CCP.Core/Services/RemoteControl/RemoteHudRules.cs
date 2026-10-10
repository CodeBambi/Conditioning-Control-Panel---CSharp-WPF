using System;

namespace ConditioningControlPanel.RemoteHud
{
    /// <summary>
    /// The pure half of the Remote Control v2 HUD: everything the pill decides that does not need a
    /// window. Kept WPF-free so it is unit tested (RemoteHudRulesTests).
    /// </summary>
    public static class RemoteHudRules
    {
        /// <summary>What the round badge shows when the controller gave no name.</summary>
        public const string NoNameInitial = "?";

        /// <summary>
        /// A second Stop inside this window is swallowed. The local Stop runs the real panic path,
        /// and two panic presses inside two seconds quit the app when nothing is running: a
        /// double-click on a button must never be the thing that closes CCP.
        /// </summary>
        public static readonly TimeSpan StopDebounce = TimeSpan.FromMilliseconds(2500);

        /// <summary>How long the "Sent" confirmation stays on the pill after a press.</summary>
        public static readonly TimeSpan ConfirmFor = TimeSpan.FromMilliseconds(1600);

        public enum StrengthTag { None, Half, Quarter }

        /// <summary>The HUD is up while a session runs and a controller is connected, nothing else.</summary>
        public static bool ShouldShow(bool sessionActive, bool controllerConnected)
            => sessionActive && controllerConnected;

        /// <summary>m:ss under an hour, h:mm:ss after. Negative spans read 0:00.</summary>
        public static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            var total = (long)Math.Floor(elapsed.TotalSeconds);
            var h = total / 3600;
            var m = (total % 3600) / 60;
            var s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }

        /// <summary>
        /// Time on the pill. Prefers the service's connect time; when the service has none yet
        /// (older build, or the stub), counts from when the HUD itself first saw the controller.
        /// </summary>
        public static TimeSpan Elapsed(DateTime? connectedSinceUtc, DateTime seenSinceUtc, DateTime nowUtc)
            => nowUtc - (connectedSinceUtc ?? seenSinceUtc);

        /// <summary>Half under full strength, Quarter at the 0.25 floor, nothing at 1.</summary>
        public static StrengthTag StrengthFor(double easyFactor)
        {
            if (double.IsNaN(easyFactor) || easyFactor >= 0.999) return StrengthTag.None;
            return easyFactor <= 0.251 ? StrengthTag.Quarter : StrengthTag.Half;
        }

        /// <summary>First letter or digit of the name, upper-cased; <see cref="NoNameInitial"/> otherwise.</summary>
        public static string InitialFrom(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return NoNameInitial;
            foreach (var c in name.Trim())
            {
                if (char.IsLetterOrDigit(c))
                    return char.ToUpperInvariant(c).ToString();
            }
            return NoNameInitial;
        }

        /// <summary>The name to put in the headline, or null for "Someone has your remote".</summary>
        public static string? DisplayName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var trimmed = name.Trim();
            return trimmed.Length > 24 ? trimmed.Substring(0, 24) : trimmed;
        }

        /// <summary>True when a Stop press should act (none in the last <see cref="StopDebounce"/>).</summary>
        public static bool StopPressAccepted(DateTime? lastStopUtc, DateTime nowUtc)
            => lastStopUtc == null || nowUtc - lastStopUtc.Value >= StopDebounce || nowUtc < lastStopUtc.Value;

        /// <summary>
        /// Whether the HUD's Stop may also run the local panic path. Never more permissive than the
        /// panic key itself (same rule as the blink stop, owner 2026-09-28): with the panic key off,
        /// Stop sends the signal and stops the remote's effects, and that is all.
        /// </summary>
        public static bool LocalPanicAllowed(bool panicKeyEnabled) => panicKeyEnabled;
    }
}
