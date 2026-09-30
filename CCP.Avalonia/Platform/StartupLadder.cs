using System;
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
    /// ponytail: no modal ladder, launcher hold or game-host park on this head, so ModalUp /
    /// LadderBusy / GameHostUp / LauncherHolding stay false and Route never answers Defer; port the
    /// pump when the startup modals move onto a shared ladder.
    /// </summary>
    internal static class StartupLadder
    {
        public static StartupInbox Inbox { get; } = new();

        private static DateTime? _firstLaunchUntilUtc;

        /// <summary>WPF BeginFirstLaunchQuiet: called once from the far side of the first-run wizard.</summary>
        public static void BeginFirstLaunchQuiet(TimeSpan span)
        {
            if (span <= TimeSpan.Zero) return;
            var until = DateTime.UtcNow + span;
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

            if (StartupQueueCore.Route(StartupSurfaceKind.Passive, World(DateTime.UtcNow)) == StartupRouting.Present)
            {
                StartupInbox.RunSafely(item.Open, item.Key, "open");
                return;
            }
            if (Inbox.File(item))
                Log.Information("[Startup] '{Key}' went to the Inbox ({Count} waiting)", item.Key, Inbox.UnreadCount);
        }

        /// <summary>Test seam: forget the quiet window and every row.</summary>
        internal static void ResetForTests()
        {
            _firstLaunchUntilUtc = null;
            Inbox.Items.Clear();
        }
    }
}
