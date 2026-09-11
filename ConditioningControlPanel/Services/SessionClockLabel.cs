using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The two clocks a user asked to be able to look away from: the lockdown time-left readout
    /// (HideLockdownTimer) and the running session's countdown on the START buttons
    /// (ShowSessionCountdown). Pure string work. MainWindow resolves the loc templates and hands
    /// them in, so every branch here can be pinned down without a language file loaded.
    /// </summary>
    public static class SessionClockLabel
    {
        /// <summary>
        /// What every lockdown clock shows while the digits are hidden. Keeps the colon so it still
        /// reads as a clock rather than a broken one, and is about the width of "00:00" so the
        /// Lockdown page does not shift - the digits' TextBlock is also the five-click exit handle
        /// and has to stay exactly where it was.
        /// </summary>
        public const string HiddenLockdownClock = "••:••";

        /// <summary>The Lockdown page and the title-bar badge: h:mm:ss past the hour, mm:ss under it.</summary>
        public static string LockdownClock(TimeSpan remaining, bool hidden) =>
            hidden ? HiddenLockdownClock
                   : remaining.TotalHours >= 1 ? remaining.ToString(@"h\:mm\:ss") : remaining.ToString(@"mm\:ss");

        /// <summary>The Home rail chip: total minutes and seconds, no hour split.</summary>
        public static string RailClock(TimeSpan remaining, bool hidden) =>
            hidden ? HiddenLockdownClock : Countdown(remaining);

        /// <summary>MM:SS with total minutes, the shape the session labels have always used.</summary>
        public static string Countdown(TimeSpan remaining) =>
            Minutes(remaining) + ":" + Seconds(remaining);

        /// <summary>
        /// The Presets tab's stop button. <paramref name="withClock"/> is the resolved
        /// "STOP SESSION ({0}:{1})" template, <paramref name="plain"/> the bare "STOP SESSION".
        /// </summary>
        public static string StopButton(TimeSpan remaining, bool showCountdown, string withClock, string plain) =>
            showCountdown ? Format(withClock, Minutes(remaining), Seconds(remaining)) : plain;

        /// <summary>
        /// The title-bar START button while a session runs. <paramref name="withClock"/> is the
        /// resolved "{0} {1}:{2}{3}" template; without the countdown it is the name and the pause
        /// mark, nothing else.
        /// </summary>
        public static string StartButton(string name, TimeSpan remaining, bool showCountdown,
                                         string pauseIndicator, string withClock) =>
            showCountdown ? Format(withClock, name, Minutes(remaining), Seconds(remaining), pauseIndicator)
                          : name + pauseIndicator;

        private static string Minutes(TimeSpan t) => ((int)t.TotalMinutes).ToString("D2");
        private static string Seconds(TimeSpan t) => t.Seconds.ToString("D2");

        /// <summary>Same forgiveness Loc.GetF has: a translation with a broken placeholder shows
        /// the template rather than throwing on a one-second tick.</summary>
        private static string Format(string template, params object[] args)
        {
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }
    }
}
