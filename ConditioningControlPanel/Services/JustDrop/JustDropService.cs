using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// JUST DROP - the web session shop, hosted rather than ported.
    ///
    /// <para><b>Doctrine.</b> The shop, its order book, its share/delete/replay actions and every
    /// effect it plays live in the web app at <see cref="ExpressUrl"/>. The desktop door is a THIN
    /// WebView2 host of that page: there is no desktop order list, no desktop share dialog and no
    /// WPF re-implementation of any effect, deliberately and permanently. This service owns only
    /// the two things a web page cannot own for itself - whether the door exists at all, and what
    /// the app does when the page says a drop finished.</para>
    ///
    /// <para><b>The bridge.</b> The player posts JSON with a fixed envelope
    /// (<c>{ source:'justdrop', v:1, type, payload }</c>). Anything that is not that envelope -
    /// malformed JSON, a foreign <c>source</c>, a future <c>v</c>, an unknown <c>type</c> - is
    /// dropped with a Debug line and never throws: a page update must never be able to crash the
    /// host, and the host must never guess at a shape it does not recognise.</para>
    /// </summary>
    internal static class JustDropService
    {
        /// <summary>The one URL this door hosts. Per-user, behind the site's own login - the user
        /// signs in INSIDE the webview once and the cookie survives restarts because the door's
        /// WebView2 environment has its own persistent user-data folder (see JustDropTabView).
        /// <para>Host is <c>app.cclabs.app</c>, the dashboard app. Bare <c>cclabs.app</c> is the
        /// marketing site and does not serve this route.</para></summary>
        public const string ExpressUrl = "https://app.cclabs.app/dashboard/express";

        /// <summary>The bridge envelope's <c>source</c> discriminator. Fixed by the web player.</summary>
        private const string BridgeSource = "justdrop";

        /// <summary>Highest envelope version this host understands. A page posting a higher
        /// <c>v</c> is talking a protocol we have not shipped yet, so its messages are ignored
        /// rather than half-read.</summary>
        private const int BridgeVersion = 1;

        // ============================== the gate ==============================

        /// <summary>
        /// Local kill switch, the same shape as <c>OverlayService.BrainDrainWithheld</c>: flip it
        /// to <c>true</c> and the door cannot appear no matter what the server says. It exists so
        /// a client-side problem (a page that wedges WebView2, say) can be shut off in a patch
        /// release without a server change, and it is <c>false</c> because the door IS built.
        ///
        /// <para>It is NOT what hides the door today - <see cref="ServerEnabled"/> is. Read
        /// <see cref="DoorAvailable"/>, never either half on its own.</para>
        /// </summary>
        /// <remarks>static readonly, not const: a const would make the rest of every guard
        /// compile-time unreachable (CS0162), exactly as OverlayService documents.</remarks>
        public static readonly bool Withheld = false;

        /// <summary>
        /// The server's verdict, and the reason the door is absent on a fresh install.
        ///
        /// <para><b>Default false, and deliberately not persisted anywhere.</b> This follows the
        /// <c>/config/update-banner</c> family (MainWindow.Marquee.cs): an ad-hoc GET, re-asked
        /// every launch, no AppSettings row, no cache file, fail-CLOSED when the server is
        /// unreachable. That is the correct posture for "does this feature exist for this user"
        /// and the wrong one for an entitlement - the tier gates cache for 24h with a 14-day
        /// grace precisely because a paying user must not lose what they bought when the network
        /// blinks, whereas an unreleased door reappearing offline would be the bug.</para>
        ///
        /// <para>Not an AppSettings property on purpose: nothing the user can edit, export, sync
        /// or hand-patch in settings.json may open this door.</para>
        /// </summary>
        private static bool ServerEnabled;

        /// <summary>Single source of truth for "is there a Just Drop door". Everything that shows,
        /// hides, navigates to or badges the door asks this - never <see cref="Withheld"/> or the
        /// server flag alone.</summary>
        public static bool DoorAvailable => !Withheld && ServerEnabled;

        /// <summary>Raised on the UI thread when <see cref="DoorAvailable"/> flips, so the rail
        /// can reveal the door mid-session when the server answer lands ~9s after launch.</summary>
        public static event EventHandler? AvailabilityChanged;

        private const string ConfigUrl = "https://codebambi-proxy.vercel.app/config/justdrop";

        /// <summary>
        /// Ask the server whether this door exists. Fire-and-forget from MainWindow's startup
        /// stagger; every failure path leaves <see cref="ServerEnabled"/> exactly as it was, which
        /// on a cold start means false.
        /// </summary>
        public static async Task RefreshAvailabilityAsync()
        {
            if (Withheld) return;   // nothing the server can say would matter

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = await http.GetAsync(ConfigUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Debug("JustDrop: /config/justdrop returned {Status}; door stays hidden",
                        (int)response.StatusCode);
                    return;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JObject? parsed = null;
                try { if (!string.IsNullOrWhiteSpace(body)) parsed = JObject.Parse(body); } catch { }
                var enabled = parsed?["enabled"]?.Value<bool>() == true;

                if (enabled == ServerEnabled) return;
                ServerEnabled = enabled;
                App.Logger?.Information("JustDrop: server flag = {Enabled}", enabled);
                RaiseAvailabilityChanged();
            }
            catch (Exception ex)
            {
                // Debug, not Warning: a shop nobody has been given yet failing to answer is the
                // normal case, and this runs on every launch for every user.
                App.Logger?.Debug("JustDrop: availability check failed ({Error}); door stays hidden", ex.Message);
            }
        }

        private static void RaiseAvailabilityChanged()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { AvailabilityChanged?.Invoke(null, EventArgs.Empty); }
                catch (Exception ex) { App.Logger?.Warning(ex, "JustDrop: AvailabilityChanged handler threw"); }
            }));
        }

        // ============================== the bridge ==============================

        /// <summary>
        /// XP for one finished drop. Calibration against the app's existing grants: an FYP clip is
        /// 5 and an attention hit 15, a Pop Quiz is 25, a mantra 30, and a whole Training Program
        /// day is 200+. A drop is a short complete run, so it sits with the Pop Quiz rather than
        /// with a program day - and it is a FIXED number, not derived from the payload's
        /// <c>durationSec</c>, because the page is not a trusted clock.
        /// </summary>
        public const int SessionCompleteXp = 25;

        /// <summary>
        /// Consumes one raw <c>WebMessageReceived</c> payload. MUST be called on the UI thread
        /// (CoreWebView2 raises there, and both the XP grant and the toast want it).
        /// Never throws.
        /// </summary>
        public static void HandleWebMessage(string? json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;

                JObject envelope;
                try { envelope = JObject.Parse(json); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("JustDrop bridge: unparseable message dropped ({Error})", ex.Message);
                    return;
                }

                // Not ours. The page shares its WebView2 with whatever the site itself posts, so a
                // foreign source is expected traffic, not an error.
                if (!string.Equals((string?)envelope["source"], BridgeSource, StringComparison.Ordinal)) return;

                var version = envelope["v"]?.Value<int?>() ?? 0;
                if (version != BridgeVersion)
                {
                    App.Logger?.Debug("JustDrop bridge: envelope v{V} ignored (host speaks v{Ours})",
                        version, BridgeVersion);
                    return;
                }

                var type = (string?)envelope["type"];
                var payload = envelope["payload"] as JObject;
                var orderCode = (string?)payload?["orderCode"];
                var sizeId = (string?)payload?["sizeId"];
                var durationSec = payload?["durationSec"]?.Value<int?>();

                switch (type)
                {
                    case "ready":
                        App.Logger?.Information("JustDrop bridge: page ready");
                        break;

                    case "session-start":
                        App.Logger?.Information("JustDrop bridge: session-start order={Order} size={Size} duration={Duration}s",
                            orderCode ?? "?", sizeId ?? "?", durationSec ?? 0);
                        break;

                    case "session-exit":
                        App.Logger?.Information("JustDrop bridge: session-exit order={Order} after {Duration}s",
                            orderCode ?? "?", durationSec ?? 0);
                        break;

                    case "session-complete":
                        App.Logger?.Information("JustDrop bridge: session-complete order={Order} size={Size} duration={Duration}s",
                            orderCode ?? "?", sizeId ?? "?", durationSec ?? 0);
                        AwardSessionComplete();
                        break;

                    default:
                        // A page shipped ahead of the host: log the name so the next reader knows
                        // what to implement, and do nothing.
                        App.Logger?.Debug("JustDrop bridge: unknown type '{Type}' ignored", type ?? "(null)");
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "JustDrop bridge: message handling failed");
            }
        }

        /// <summary>
        /// The one thing a finished drop changes in the app: XP, and a toast saying so.
        /// <para>AddXP self-guards (not logged in, idle anti-cheat, skill multipliers), so there is
        /// nothing to pre-check here - but it can still throw, and a throw from a web message must
        /// not take the door down, so each half is caught separately: a failed grant should not
        /// cost the user the toast, and a failed toast should not cost them the XP.</para>
        /// </summary>
        private static void AwardSessionComplete()
        {
            try
            {
                // XPSource.Other, matching Programs / Quests / Pop Quiz / the Intake. NOT
                // XPSource.Session: that source is the session engine's, and borrowing it would
                // make companion bonuses and quest counters read a web drop as a local session.
                App.Progression?.AddXP(SessionCompleteXp, XPSource.Other);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "JustDrop: session-complete XP failed"); }

            try
            {
                App.Notifications?.Show(Loc.GetF("jd_toast_session_complete", SessionCompleteXp),
                    NotificationType.Success, TimeSpan.FromSeconds(6));
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "JustDrop: session-complete toast failed"); }
        }
    }
}
