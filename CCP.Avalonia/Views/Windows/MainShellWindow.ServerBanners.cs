// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Marquee.cs, regions
// "Server-Controlled Update Banner" (CheckServerUpdateBanner, UpdateBannerResponse) and
// "Server-Triggered Announcement" (CheckServerAnnouncement, AnnouncementResponse).
//
// Same timings as WPF InitializeMarqueeBanner: the update banner 5 s after load, the announcement
// 7 s after load. Same endpoints, same DTOs, same rules: the banner only lights the pill when its
// version is newer than this build (Tag "UrgentUpdate", btn_update_to_version + the download /
// install tooltip); the announcement shows once per id (DismissedAnnouncementId), goes through
// PresentOrInbox (StartupLadder), and claims the launch's one popup slot. Every failure is silent
// (Debug), so a sandbox (SandboxNet refuses the request) keeps the shell quiet.
//
// ponytail: the account half of a dismissal (App.ProfileSync.DismissAnnouncementAsync, so the same
//   news does not greet the user again on another PC) has no port; only the local slot is recorded.
// ponytail: CheckIntakePassNudge (14 s) needs App.IntakePass; _serverAnnouncementShownThisLaunch is
//   kept so the nudge can read it once that service lands.

using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal const string UpdateBannerUrl = "https://codebambi-proxy.vercel.app/config/update-banner";
        internal const string AnnouncementUrl = "https://codebambi-proxy.vercel.app/config/announcement";

        internal sealed class UpdateBannerResponse
        {
            [JsonPropertyName("enabled")] public bool Enabled { get; set; }
            [JsonPropertyName("version")] public string? Version { get; set; }
            [JsonPropertyName("message")] public string? Message { get; set; }
            [JsonPropertyName("url")] public string? Url { get; set; }
        }

        internal sealed class AnnouncementResponse
        {
            [JsonPropertyName("enabled")] public bool Enabled { get; set; }
            [JsonPropertyName("id")] public string? Id { get; set; }
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("message")] public string? Message { get; set; }
            [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
            [JsonPropertyName("link_url")] public string? LinkUrl { get; set; }
            [JsonPropertyName("theme")] public string? Theme { get; set; }
        }

        /// <summary>The server banner's download page, when it sent one (WPF _serverUpdateUrl).</summary>
        private string? _serverUpdateUrl;

        /// <summary>A server announcement already used this launch's popup budget.</summary>
        private bool _serverAnnouncementShownThisLaunch;

        private void InitializeServerBanners()
        {
            RunOnceWhileOpen(CheckServerUpdateBanner, TimeSpan.FromSeconds(5));   // MainShellWindow.Lifetime.cs: a closed shell is never rooted by it
            RunOnceWhileOpen(CheckServerAnnouncement, TimeSpan.FromSeconds(7));
        }

        /// <summary>WPF opens the banner's url from the pill when set. True when it opened.</summary>
        internal bool TryOpenServerUpdateUrl()
        {
            if (string.IsNullOrEmpty(_serverUpdateUrl)) return false;
            return ExternalOpener.Open(_serverUpdateUrl);
        }

        internal static Version CurrentAppVersion() =>
            typeof(MainShellWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);

        /// <summary>The pure half of CheckServerUpdateBanner: the banner applies only when enabled,
        /// carries a version, and that version is newer than <paramref name="current"/>.</summary>
        internal static UpdateBannerResponse? ParseUpdateBanner(string json, Version current)
        {
            try
            {
                var r = JsonSerializer.Deserialize<UpdateBannerResponse>(json);
                if (r?.Enabled != true || string.IsNullOrWhiteSpace(r.Version)) return null;
                return Version.TryParse(r.Version, out var v) && v > current ? r : null;
            }
            catch { return null; }
        }

        /// <summary>The pure half of CheckServerAnnouncement: enabled, an id and a title, not the
        /// one the user already dismissed.</summary>
        internal static AnnouncementResponse? ParseAnnouncement(string json, string? dismissedId)
        {
            try
            {
                var r = JsonSerializer.Deserialize<AnnouncementResponse>(json);
                if (r?.Enabled != true || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Title)) return null;
                return r.Id == dismissedId ? null : r;
            }
            catch { return null; }
        }

        private async void CheckServerUpdateBanner()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await http.GetAsync(UpdateBannerUrl, ClosedToken);
                if (!response.IsSuccessStatusCode) return;
                var result = ParseUpdateBanner(await response.Content.ReadAsStringAsync(ClosedToken), CurrentAppVersion());
                if (result == null) return;

                Log.Information("Server update banner enabled: version={Version}, message={Message}", result.Version, result.Message);
                _serverUpdateUrl = result.Url;
                await Dispatcher.UIThread.InvokeAsync(() => ApplyServerUpdateBanner(result));
            }
            catch (Exception ex) { Log.Debug("Failed to check server update banner: {Error}", ex.Message); }
        }

        internal void ApplyServerUpdateBanner(UpdateBannerResponse result)
        {
            if (Named<Button>("BtnUpdateAvailable") is not { } b) return;
            b.Tag = "UrgentUpdate";
            b.Bind(ContentControl.ContentProperty, LocF("btn_update_to_version", result.Version));
            b.Bind(ToolTip.TipProperty, LocF(!string.IsNullOrEmpty(result.Url)
                ? "tooltip_update_to_version_download" : "tooltip_update_to_version_install", result.Version));
        }

        private async void CheckServerAnnouncement()
        {
            try
            {
                var url = AnnouncementUrl;
                var unifiedId = CoreSettings.Current.UnifiedId;
                if (!string.IsNullOrWhiteSpace(unifiedId)) url += $"?unified_id={Uri.EscapeDataString(unifiedId)}";

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await http.GetAsync(url, ClosedToken);
                if (!response.IsSuccessStatusCode) return;
                var result = ParseAnnouncement(await response.Content.ReadAsStringAsync(ClosedToken), CoreSettings.Current.DismissedAnnouncementId);
                if (result == null) return;

                Log.Information("Server announcement received: id={Id}, title={Title}", result.Id, result.Title);
                await Dispatcher.UIThread.InvokeAsync(() => PresentServerAnnouncement(result));
            }
            catch (Exception ex) { Log.Debug("Failed to check server announcement: {Error}", ex.Message); }
        }

        internal void PresentServerAnnouncement(AnnouncementResponse result)
        {
            // Claimed at routing time, not when the popup opens (WPF): a parked Inbox row is as good a
            // reason for the weekly nudge to stand down as the popup on screen.
            _serverAnnouncementShownThisLaunch = true;

            var id = result.Id!;
            var title = result.Title!;
            var message = result.Message ?? "";
            var imageUrl = result.ImageUrl;
            var linkUrl = result.LinkUrl;
            var theme = result.Theme;

            StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
            {
                Key = "announcement:" + id,
                Glyph = "📣",
                Title = title,
                Summary = SummariseForInbox(message),
                Open = () => new AnnouncementPopup(id, title, message, imageUrl, linkUrl, theme).Show(),
                Dismiss = () =>
                {
                    try { CoreSettings.Current.DismissedAnnouncementId = id; }
                    catch (Exception ex) { Log.Debug("Announcement row dismiss: {E}", ex.Message); }
                },
            });
        }

        /// <summary>WPF MainWindow.Inbox.cs Summarise: one line of the body for an Inbox row.</summary>
        internal static string SummariseForInbox(string? body, int max = 90)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            var flat = body.Replace("\r", " ").Replace("\n", " ").Trim();
            while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
            return flat.Length <= max ? flat : flat.Substring(0, max - 1).TrimEnd() + "…";
        }
    }
}
