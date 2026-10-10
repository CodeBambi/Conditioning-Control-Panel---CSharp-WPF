// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Marquee.cs, region "Marquee Banner"
// (InitializeMarqueeBanner, RefreshMarqueeFromSettings, MarqueeResponse) and UpdateMarqueeMessage.
// The strip itself (StartMarqueeAnimation) lives on the page that carries MarqueeText:
// CCP.Avalonia/Views/Tabs/SettingsTabView.Marquee.cs.
//
// Same as WPF: the one-shot neutral-default migration, the legacy-default replacement, a GET of
// /config/marquee 3 s after load and every 5 minutes, the same DTO ({ message }), a new non-blank
// message that differs from the saved one is saved and the strip restarts, and any failure is silent
// and keeps the saved message. Under CCP_USERDATA_DIR SandboxNet refuses the request; that lands in
// the silent catch and the saved message keeps scrolling.
//
// NOT PORTED here (still in the STILL BLOCKED list of MainShellWindow.Marquee.cs):
// CheckServerUpdateBanner, CheckServerAnnouncement and CheckIntakePassNudge (the update banner needs
// the in-app updater, the announcement and nudge need the startup popup arbitration).

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal const string MarqueeConfigUrl = "https://codebambi-proxy.vercel.app/config/marquee";

        private DispatcherTimer? _marqueeRefreshTimer;


        /// <summary>WPF InitializeMarqueeBanner, minus the update banner / announcement / nudge.</summary>
        private void InitializeMarqueeBanner()
        {
            try
            {
                var settings = CoreSettings.Current;
                settings.MigrateMarqueeMessage(App.Mods?.ActiveModId);

                var currentSaved = settings.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(currentSaved) ||
                    currentSaved.Contains("WELCOME TO YOUR CONDITIONING") ||
                    currentSaved.Contains("RELAX AND SUBMIT"))
                {
                    settings.MarqueeMessage = MarqueeScroller.ResolveDefaultMarqueeMessage();
                }

                // The page starts its own strip on attach; this covers a page that attached first.
                HomeTab?.StartMarqueeAnimation();

                RunOnceWhileOpen(RefreshMarqueeFromSettings, TimeSpan.FromSeconds(3));

                _marqueeRefreshTimer?.Stop();
                _marqueeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
                _marqueeRefreshTimer.Tick += (_, _) => RefreshMarqueeFromSettings();
                _marqueeRefreshTimer.Start();
                Closed += (_, _) => _marqueeRefreshTimer?.Stop();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize marquee banner");
            }
        }

        private async void RefreshMarqueeFromSettings()
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await httpClient.GetAsync(MarqueeConfigUrl);
                if (!response.IsSuccessStatusCode) return;

                var json = await response.Content.ReadAsStringAsync();
                var newMessage = ParseMarqueeMessage(json);

                // Compare against the raw saved value, never the upper-cased strip copy (WPF fix:
                // a mixed-case server message otherwise restarted the strip on every poll).
                if (!string.IsNullOrWhiteSpace(newMessage) && newMessage != CoreSettings.Current.MarqueeMessage)
                {
                    Log.Information("Marquee message updated from server: {Message}", newMessage);
                    CoreSettings.Current.MarqueeMessage = newMessage;
                    await Dispatcher.UIThread.InvokeAsync(() => HomeTab?.StartMarqueeAnimation());
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to refresh marquee from server: {Error}", ex.Message);
            }
        }

        /// <summary>The server's message, or null for a body that is not the MarqueeResponse shape.</summary>
        internal static string? ParseMarqueeMessage(string json)
        {
            try { return JsonSerializer.Deserialize<MarqueeResponse>(json)?.message; }
            catch (JsonException) { return null; }
        }

        private sealed class MarqueeResponse
        {
            public string? message { get; set; }
        }

        /// <summary>
        /// WPF UpdateMarqueeMessage: the external entry point. Upper-cases, appends " • " unless the
        /// message already ends in a bullet or a space, saves, and restarts the strip.
        /// </summary>
        public void UpdateMarqueeMessage(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                var newMessage = message.Trim().ToUpperInvariant();
                if (!newMessage.EndsWith("•") && !newMessage.EndsWith(" "))
                    newMessage += " • ";

                CoreSettings.Current.MarqueeMessage = newMessage;
                Dispatcher.UIThread.Post(() => HomeTab?.StartMarqueeAnimation());
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to update marquee message: {Error}", ex.Message);
            }
        }
    }
}
