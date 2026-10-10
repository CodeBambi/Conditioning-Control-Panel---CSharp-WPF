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
    /// <para>One 30 s tick, as WPF: it writes achievements.json only when a counter moved. The two
    /// setting-combination badges (System Overload, Total Lockdown) are checked by Core when one of
    /// their six settings changes, not on a timer. It never writes settings and it credits no
    /// conditioning time: that has ONE tracker (Core ConditioningTime).</para>
    ///
    /// <para>Also here because they have no other home on this head: the once-ever companion chat
    /// backfill, the relapse stamp of a manual stop, and Alt+Tab during a running engine (Windows
    /// only; on Linux it is unknown and never counted against the player).</para>
    /// </summary>
    internal static class AchievementAutosave
    {
        internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
        private const int VK_TAB = 0x09, VK_MENU = 0x12, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        private static DispatcherTimer? _tick;
        private static Action? _message;
        private static Action<int, string?>? _keyDown, _keyUp;
        private static Action<Action>? _oldPost;
        private static volatile bool _leftAlt, _rightAlt;
        /// <summary>Tests only: is the engine running (WPF _isRunning).</summary>
        internal static Func<bool> EngineRunning = () => CoreEngine.IsRunning;

        internal static void Start(AchievementEngine engine)
        {
            Stop();
            // Core events (XP banked, mod switched, enhancement saved, combination settings, a remote
            // session start) reach the engine on the UI thread, as every WPF handler did.
            _oldPost = AchievementEngine.UiPost;
            AchievementEngine.UiPost = a => { if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a); };
            AchievementEngine.Attach(engine);
            _message = () => Dispatcher.UIThread.Post(() =>
            {
                try { engine.TrackCompanionMessage(); }   // WPF GamificationBridge.OnCompanionMessageSent, marshalled as there
                catch (Exception ex) { Log.Debug(ex, "companion message count"); }
            });
            App.UserMessageSent += _message;
            // WPF GamificationBridge.BackfillCompanionChatCount: once ever, and only with a brain to read.
            try { engine.BackfillCompanionChat(App.Brain); }
            catch (Exception ex) { Log.Debug(ex, "companion chat backfill"); }
            if (OperatingSystem.IsWindows())
            {
                // WPF MainWindow.xaml.cs:913: Tab with Alt held while the engine runs. The listener is the
                // panic key's own non-consuming hook; nothing here can stand between a press and panic.
                _keyDown = (vk, _) => OnKeyDown(vk);
                _keyUp = (vk, _) => OnKeyUp(vk);
                Win32PanicKey.KeyDown += _keyDown;
                Win32PanicKey.KeyUp += _keyUp;
            }
            _tick = new DispatcherTimer(Interval, DispatcherPriority.Background, (_, _) => Tick(engine));
            _tick.Start();
        }

        /// <summary>One key-down from the Windows listener thread (a test drives it without a hook).</summary>
        internal static void OnKeyDown(int vk)
        {
            if (vk is VK_LMENU or VK_MENU) { _leftAlt = true; return; }
            if (vk == VK_RMENU) { _rightAlt = true; return; }
            if (vk != VK_TAB || !(_leftAlt || _rightAlt) || !EngineRunning()) return;
            AchievementEngine.OnCurrent(e => e.TrackAltTab(), "alt-tab");
        }

        internal static void OnKeyUp(int vk)
        {
            if (vk is VK_LMENU or VK_MENU) _leftAlt = false;
            else if (vk == VK_RMENU) _rightAlt = false;
        }

        /// <summary>WPF MainWindow.StartStop.cs:99: the Stop button opens the relapse window exactly as a
        /// panic press does (a start inside ten seconds of it is the Relapse badge).</summary>
        internal static void NoteManualStop()
        {
            try { App.Achievements?.TrackPanicPressed(); }
            catch (Exception ex) { Log.Debug(ex, "manual stop relapse stamp"); }
        }

        /// <summary>One autosave tick (a test calls it).</summary>
        internal static void Tick(AchievementEngine engine)
        {
            try { _ = engine.SaveIfDirtyAsync(); }
            catch (Exception ex) { Log.Debug(ex, "achievement autosave"); }
        }

        internal static void Stop()
        {
            _tick?.Stop();
            _tick = null;
            if (_message != null) { App.UserMessageSent -= _message; _message = null; }
            if (_keyDown != null) { Win32PanicKey.KeyDown -= _keyDown; _keyDown = null; }
            if (_keyUp != null) { Win32PanicKey.KeyUp -= _keyUp; _keyUp = null; }
            (_leftAlt, _rightAlt) = (false, false);
            AchievementEngine.Attach(null);
            if (_oldPost != null) { AchievementEngine.UiPost = _oldPost; _oldPost = null; }
        }
    }
}
