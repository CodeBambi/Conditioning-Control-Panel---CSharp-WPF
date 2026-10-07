using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// This head's half of WPF's <c>App.StartupLadder</c> (Services/Startup/StartupPresenter.cs): the
    /// passive route and the Inbox. A passive surface opens now unless something already owns the
    /// user (session, tour, the ten-minute first-launch window), in which case it becomes an Inbox
    /// row - the same Core rule WPF reads (<see cref="StartupQueueCore.Route"/>).
    /// Passive surfaces open one at a time (WPF 8cbcbea01): while a feature card or an
    /// announcement is up, or one opened under <see cref="StartupQueueCore.PassiveSettle"/> ago,
    /// the next waits and is re-routed once it has gone.
    /// ponytail: no modal ladder, launcher hold or game-host park on this head, so ModalUp /
    /// LadderBusy / GameHostUp / LauncherHolding stay false and Route never answers Defer; port the
    /// pump when the startup modals move onto a shared ladder.
    /// </summary>
    internal static class StartupLadder
    {
        public static StartupInbox Inbox { get; } = new();

        private static DateTime? _firstLaunchUntilUtc;
        private static DateTime? _lastPassiveUtc;
        private static readonly List<InboxItem> _held = new();
        private static DispatcherTimer? _passiveTimer;

        /// <summary>The ladder's clock; tests step it (PLAYBOOK P08).</summary>
        internal static TimeProvider Time = TimeProvider.System;
        private static DateTime UtcNow => Time.GetUtcNow().UtcDateTime;

        /// <summary>WPF IsQuiet: a passive surface offered now would become an Inbox row.</summary>
        public static bool IsQuiet =>
            StartupQueueCore.Route(StartupSurfaceKind.Passive, World(UtcNow)) != StartupRouting.Present;

        /// <summary>WPF BeginFirstLaunchQuiet: called once from the far side of the first-run wizard.</summary>
        public static void BeginFirstLaunchQuiet(TimeSpan span)
        {
            if (span <= TimeSpan.Zero) return;
            var until = UtcNow + span;
            if (_firstLaunchUntilUtc is DateTime existing && existing >= until) return;
            _firstLaunchUntilUtc = until;
            Log.Information("[Startup] first-launch quiet window open for {Minutes:0.#} min", span.TotalMinutes);
        }

        internal static QuietInputs World(DateTime nowUtc) => new()
        {
            TutorialActive = CoreTutorial.IsActive,
            SessionRunning = CoreSession.IsSessionRunning,
            FirstLaunchUntilUtc = _firstLaunchUntilUtc,
            NowUtc = nowUtc,
        };

        /// <summary>WPF PresentOrInbox: open now, or park as a row (one per key). Safe from any thread.</summary>
        public static void PresentOrInbox(InboxItem item)
        {
            if (item == null) return;
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => PresentOrInbox(item)); return; }

            if (StartupQueueCore.Route(StartupSurfaceKind.Passive, World(UtcNow)) == StartupRouting.Present)
            {
                // One passive surface on screen at a time: the next one waits for it to close.
                if (StartupQueueCore.PassiveWaits(_lastPassiveUtc, UtcNow, PassiveWindowUp()))
                {
                    if (!_held.Any(h => string.Equals(h.Key, item.Key, StringComparison.OrdinalIgnoreCase))) _held.Add(item);
                    Log.Debug("[Startup] '{Key}' waits for the passive surface already up", item.Key);
                    EnsurePassiveWatch();
                    return;
                }
                _lastPassiveUtc = UtcNow;
                StartupInbox.RunSafely(item.Open, item.Key, "open");
                return;
            }
            if (Inbox.File(item))
                Log.Information("[Startup] '{Key}' went to the Inbox ({Count} waiting)", item.Key, Inbox.UnreadCount);
        }

        // Passive windows open right now. Tracked by class handler rather than read from the
        // desktop lifetime, so it holds for any host (and headless tests, which have none).
        private static readonly HashSet<Window> _passiveUp = new();

        static StartupLadder()
        {
            Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is IPassiveStartupSurface) _passiveUp.Add(w); });
            Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => _passiveUp.Remove(w));
        }

        /// <summary>A feature card or an announcement is on screen.</summary>
        private static bool PassiveWindowUp() => _passiveUp.Any(w => w.IsVisible);

        /// <summary>Re-asks the held surfaces once the passive one on screen has closed.</summary>
        private static void EnsurePassiveWatch()
        {
            if (_passiveTimer != null) return;
            _passiveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _passiveTimer.Tick += (_, _) => PumpHeld();
            _passiveTimer.Start();
        }

        /// <summary>One tick of the watch: re-routes the held surfaces once nothing passive is up.</summary>
        internal static void PumpHeld()
        {
            if (StartupQueueCore.PassiveWaits(_lastPassiveUtc, UtcNow, PassiveWindowUp())) return;
            _passiveTimer?.Stop();
            _passiveTimer = null;
            var held = _held.ToList();
            _held.Clear();
            foreach (var h in held) PresentOrInbox(h);
        }

        /// <summary>Test seam: forget the quiet window, the passive pacing and every row.</summary>
        internal static void ResetForTests()
        {
            _firstLaunchUntilUtc = null;
            _lastPassiveUtc = null;
            _held.Clear();
            _passiveTimer?.Stop();
            _passiveTimer = null;
            Time = TimeProvider.System;
            Inbox.Items.Clear();
        }
    }
}
