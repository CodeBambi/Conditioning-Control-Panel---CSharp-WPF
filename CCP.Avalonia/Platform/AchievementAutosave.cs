using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The head half of the lifetime counters (WPF AchievementService's autosave timer and the
    /// GamificationBridge subscriptions that have a signal on this head).
    ///
    /// <para>One 30 s tick, as WPF: it writes achievements.json only when a counter moved, and checks the
    /// two setting-combination badges (System Overload, Total Lockdown). It never writes settings and it
    /// credits no conditioning time: that has ONE tracker (Core ConditioningTime).</para>
    /// </summary>
    internal static class AchievementAutosave
    {
        internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
        private static DispatcherTimer? _tick;
        private static Action? _message;

        internal static void Start(AchievementEngine engine)
        {
            Stop();
            AchievementEngine.Attach(engine);   // XP banked + mod switched, straight from Core
            _message = () => Dispatcher.UIThread.Post(() =>
            {
                try { engine.TrackCompanionMessage(); }   // WPF GamificationBridge.OnCompanionMessageSent, marshalled as there
                catch (Exception ex) { Log.Debug(ex, "companion message count"); }
            });
            App.UserMessageSent += _message;
            _tick = new DispatcherTimer(Interval, DispatcherPriority.Background, (_, _) => Tick(engine));
            _tick.Start();
        }

        /// <summary>One autosave tick (a test calls it).</summary>
        internal static void Tick(AchievementEngine engine)
        {
            try { engine.CheckSettingCombos(CoreSettings.Current); }
            catch (Exception ex) { Log.Debug(ex, "achievement combo check"); }
            try { _ = engine.SaveIfDirtyAsync(); }
            catch (Exception ex) { Log.Debug(ex, "achievement autosave"); }
        }

        internal static void Stop()
        {
            _tick?.Stop();
            _tick = null;
            if (_message != null) { App.UserMessageSent -= _message; _message = null; }
            AchievementEngine.Attach(null);
        }
    }
}
