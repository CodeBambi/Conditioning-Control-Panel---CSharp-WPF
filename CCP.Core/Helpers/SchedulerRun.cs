using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// The Scheduler runtime: WPF MainWindow.StartStop.cs CheckSchedulerOnStartup (:747),
    /// SchedulerTimer_Tick (:786), IsInScheduledTimeWindow (:826) and the manual-stop memory of
    /// BtnStart_Click (:101-111), minus the timer and the engine calls. The head polls
    /// <see cref="Tick"/> every 30 s after a 60 s grace period and does what it answers.
    /// </summary>
    public sealed class SchedulerRun
    {
        public enum Action { None, Start, Stop }

        /// <summary>WPF MainWindow.xaml.cs:637: "checks every 30 seconds".</summary>
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        /// <summary>WPF MainWindow.xaml.cs:646: lets the app finish starting (post-update restarts).</summary>
        public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(60);

        public bool AutoStarted { get; private set; }
        public bool ManuallyStoppedDuringSchedule { get; private set; }

        /// <summary>WPF IsInScheduledTimeWindow: today must be an active day; an end before the start
        /// is an overnight window; unparseable times fall back to 16:00 / 22:00.</summary>
        public static bool IsInWindow(AppSettings settings, DateTime now)
        {
            bool isDayActive = now.DayOfWeek switch
            {
                DayOfWeek.Monday => settings.SchedulerMonday,
                DayOfWeek.Tuesday => settings.SchedulerTuesday,
                DayOfWeek.Wednesday => settings.SchedulerWednesday,
                DayOfWeek.Thursday => settings.SchedulerThursday,
                DayOfWeek.Friday => settings.SchedulerFriday,
                DayOfWeek.Saturday => settings.SchedulerSaturday,
                DayOfWeek.Sunday => settings.SchedulerSunday,
                _ => false
            };
            if (!isDayActive) return false;

            var start = SchedulerTime.ParseOrDefault(settings.SchedulerStartTime, new TimeSpan(16, 0, 0));
            var end = SchedulerTime.ParseOrDefault(settings.SchedulerEndTime, new TimeSpan(22, 0, 0));
            var t = now.TimeOfDay;
            return end < start ? (t >= start || t < end) : (t >= start && t < end);
        }

        /// <summary>WPF CheckSchedulerOnStartup: start at once when the app opens inside the window.</summary>
        public Action CheckOnStartup(AppSettings settings, DateTime now)
        {
            if (!settings.SchedulerEnabled) return Action.None;
            if (!IsInWindow(settings, now)) return Action.None;
            AutoStarted = true;
            return Action.Start;
        }

        /// <summary>WPF SchedulerTimer_Tick.</summary>
        public Action Tick(AppSettings settings, DateTime now, bool engineRunning)
        {
            if (!settings.SchedulerEnabled) return Action.None;
            bool inWindow = IsInWindow(settings, now);
            if (inWindow && !engineRunning && !AutoStarted && !ManuallyStoppedDuringSchedule)
            {
                AutoStarted = true;
                return Action.Start;
            }
            if (!inWindow && engineRunning && AutoStarted)
            {
                AutoStarted = false;
                return Action.Stop;
            }
            if (!inWindow)
            {
                AutoStarted = false;
                ManuallyStoppedDuringSchedule = false;
            }
            return Action.None;
        }

        /// <summary>WPF BtnStart_Click: the user stopped by hand. Inside the window this keeps the
        /// scheduler from starting it again until the window ends.</summary>
        public void NoteManualStop(AppSettings settings, DateTime now)
        {
            if (settings.SchedulerEnabled && IsInWindow(settings, now)) ManuallyStoppedDuringSchedule = true;
        }

        /// <summary>WPF BtnStart_Click: the user started by hand, which clears the manual-stop memory.</summary>
        public void NoteManualStart() => ManuallyStoppedDuringSchedule = false;
    }
}
