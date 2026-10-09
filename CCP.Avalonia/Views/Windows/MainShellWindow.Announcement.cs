using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// WPF MainWindow.Marquee.cs CheckServerAnnouncement (:857): 7 s after the shell opens, ask the server for
    /// its one live announcement and hand it to the startup Inbox - shown at once when nothing is quiet,
    /// otherwise a "📣" row. Dismissing the row does both halves of the popup's own bookkeeping (local slot and
    /// the per-account record), so news waved away unread never returns on this PC or the next.
    /// </summary>
    public partial class MainShellWindow
    {
        /// <summary>The Core client; seeded by the app at startup only. Unseeded (every test that does not set it) = no check.</summary>
        internal static Func<V2AuthService>? AnnouncementClient;

        private bool _announcementArmed;

        private void InitializeServerAnnouncement()
        {
            Opened += (_, _) =>
            {
                if (_announcementArmed || AnnouncementClient == null) return;
                _announcementArmed = true;
                DispatcherTimer.RunOnce(() => _ = CheckServerAnnouncementAsync(), TimeSpan.FromSeconds(7));
            };
        }

        /// <summary>One check: fetch, skip the dismissed id, route through the startup ladder.</summary>
        internal async Task CheckServerAnnouncementAsync()
        {
            try
            {
                var make = AnnouncementClient;
                if (make == null) return;
                var a = await make().GetAnnouncementAsync(CoreSettings.Current.UnifiedId);
                if (a == null || a.Id == CoreSettings.Current.DismissedAnnouncementId) return;
                Log.Information("Server announcement received: id={Id}, title={Title}", a.Id, a.Title);

                string id = a.Id!, title = a.Title!, message = a.Message ?? "";
                Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "announcement:" + id,
                    Glyph = "📣",
                    Title = title,
                    Summary = Summarise(message),
                    Open = () => new AnnouncementPopup(id, title, message, a.ImageUrl, a.LinkUrl, a.Theme).Show(),
                    Dismiss = () =>
                    {
                        CoreSettings.Current.DismissedAnnouncementId = id;
                        CoreSettings.Save();
                        RecordAnnouncementDismissal(id);
                    },
                });
            }
            catch (Exception ex) { Log.Debug("Failed to check server announcement: {Error}", ex.Message); }
        }

        /// <summary>WPF AnnouncementPopup.RecordServerDismissal: the account half; fire-and-forget, never throws.</summary>
        internal static void RecordAnnouncementDismissal(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(CoreSettings.Current.UnifiedId)) return;
            try { _ = AnnouncementClient?.Invoke().DismissAnnouncementAsync(id); }
            catch (Exception ex) { Log.Debug("Could not record announcement dismissal: {Error}", ex.Message); }
        }
    }
}
