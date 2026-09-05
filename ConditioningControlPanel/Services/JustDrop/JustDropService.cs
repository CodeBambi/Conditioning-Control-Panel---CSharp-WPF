using System;
using System.Collections.Generic;
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

        /// <summary>
        /// The PUBLIC taste page - a capped, signed-out preview of one order code. This is the
        /// only form a shared drop travels as, and note the host: the NAKED <c>cclabs.app</c>,
        /// which permanently redirects to the dashboard app. Short enough to read aloud, and
        /// unfurlers follow the redirect, so the pretty link is also the working one.
        /// <para>Building the URL is all the desktop does with it. Sharing is still not a desktop
        /// feature in the sense the doctrine above forbids - there is no share dialog, no expiry
        /// picker and no order created here; the clipboard gets a link to a page the web owns.</para>
        /// </summary>
        private const string TasteBaseUrl = "https://cclabs.app/taste";

        /// <summary>Taste-page URL for one order code. Codes are base64url, but escape anyway.</summary>
        public static string TasteUrl(string orderCode) =>
            $"{TasteBaseUrl}/{Uri.EscapeDataString(orderCode ?? string.Empty)}";

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

        // ─── the settlement table (mirror of ccpmobile src/lib/justdrop/dropXp.ts fallback
        //     settlement — computeDropXPFallback) ─────────────────────────────────────────
        // The flat 25 is retired: a 60-minute XXL and a 2-minute taste paid the same, which
        // made the drop the worst-paying session in the app. Desktop only ever hears
        // {orderCode, sizeId, durationSec} from the page, so it prices a drop exactly the way
        // the mobile host's size-only fallback does: size base, no item bonus, a quick-taste
        // floor under any clock we don't trust, the 4th-drop-of-the-day quarter, then the
        // level multiplier. Keep every number in step with dropXp.ts — the two hosts must pay
        // the same drop the same.
        //
        // The page's durationSec is browser-authored and NEVER trusted. The host runs its own
        // clock from the bridge's session-start message; a completion with no host-measured
        // start (page reloaded mid-run, an older page build) settles as a taste, exactly as
        // mobile treats a missing/unbelievable clock.

        /// <summary>Completion base by size. Sub-linear in duration on purpose (dropXp.ts rationale 1).</summary>
        private static readonly IReadOnlyDictionary<string, int> SizeBaseXp =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["S"] = 300, ["M"] = 650, ["L"] = 1000, ["XXL"] = 1400 };

        /// <summary>Expected wall-clock per size, in seconds (S 5min / M 15 / L 30 / XXL 60).</summary>
        private static readonly IReadOnlyDictionary<string, int> SizeExpectedSeconds =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["S"] = 300, ["M"] = 900, ["L"] = 1800, ["XXL"] = 3600 };

        /// <summary>An unrecognised or missing size reads as M, the same default decodeOrder applies.</summary>
        private const string DefaultSizeId = "M";

        /// <summary>A taste, not a meal (dropXp.ts rationale 6).</summary>
        private const int QuickTasteXp = 40;

        /// <summary>Quick-taste duration, and the floor under any measured clock.</summary>
        private const int QuickSeconds = 120;

        /// <summary>An elapsed time under this share of the size's expected run settles as a taste.</summary>
        private const double ClockTrust = 0.8;

        /// <summary>From the 4th credited drop of the local day the award quarters (rationale 7).</summary>
        private const int DiminishAfter = 3;
        private const double DiminishFactor = 0.25;

        /// <summary>Host-side clock: UTC instant the bridge reported session-start, per order code.
        /// This — never the page's durationSec — is what the settlement measures elapsed time with.</summary>
        private static readonly Dictionary<string, DateTime> SessionStartUtc = new(StringComparer.Ordinal);

        // Once-per-order-ever bookkeeping lives in the CreditedOrders class (a persisted
        // ledger in the user-data folder), not a process-lifetime set: a restart must not
        // make an already-paid drop payable again.

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
                        App.Logger?.Debug("JustDrop bridge: page ready");
                        break;

                    case "session-start":
                        App.Logger?.Information("JustDrop bridge: session-start order={Order} size={Size} duration={Duration}s",
                            orderCode ?? "?", sizeId ?? "?", durationSec ?? 0);
                        // Start the HOST's clock for this order. A restart for the same code
                        // overwrites — the newest run is the one a completion will describe.
                        if (!string.IsNullOrEmpty(orderCode))
                            SessionStartUtc[orderCode] = DateTime.UtcNow;
                        break;

                    case "session-exit":
                        App.Logger?.Information("JustDrop bridge: session-exit order={Order} after {Duration}s",
                            orderCode ?? "?", durationSec ?? 0);
                        break;

                    case "session-complete":
                        App.Logger?.Information("JustDrop bridge: session-complete order={Order} size={Size} duration={Duration}s",
                            orderCode ?? "?", sizeId ?? "?", durationSec ?? 0);
                        AwardSessionComplete(orderCode, sizeId);
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
        ///
        /// <para><b>Once per order, ever.</b> This used to pay on every <c>session-complete</c> with
        /// no idea whether it was a first play or a fifth, which made a single 8-minute drop an
        /// unbounded XP faucet the moment anything could replay it. The web side already draws this
        /// line for its own currency - host-bridge.ts says plainly that replays and embedded plays
        /// never pay Pocket Money - so paying XP for them was the desktop disagreeing with the
        /// contract it was implementing. A replay still gets a toast, because silence would read as
        /// the session not having registered.</para>
        ///
        /// <para>AddXP self-guards (not logged in, idle anti-cheat, skill multipliers), so there is
        /// nothing else to pre-check - but it can still throw, and a throw from a web message must
        /// not take the door down, so each half is caught separately: a failed grant should not
        /// cost the user the toast, and a failed toast should not cost them the XP.</para>
        /// </summary>
        /// <param name="orderCode">
        /// The order this run belongs to. A missing code is treated as ALREADY CREDITED - not as a
        /// fresh one. A page that stops sending codes must not silently become a faucet, and the
        /// only run that legitimately has no code is one we cannot tell apart from a replay anyway.
        /// </param>
        private static void AwardSessionComplete(string? orderCode, string? sizeId)
        {
            var code = orderCode?.Trim();
            bool firstPlay = !string.IsNullOrEmpty(code) && CreditedOrders.TryCredit(code!);

            int xp = 0;
            if (firstPlay)
            {
                try
                {
                    // The host's clock, or no clock at all. durationSec from the payload is
                    // deliberately not consulted (see the settlement table's header note).
                    // Keyed by the RAW code — session-start stored it untrimmed.
                    double? hostElapsedSec = null;
                    if (SessionStartUtc.Remove(orderCode!, out var startedUtc))
                    {
                        var elapsed = (DateTime.UtcNow - startedUtc).TotalSeconds;
                        if (elapsed >= 0) hostElapsedSec = elapsed;
                    }

                    xp = SettleDropXp(sizeId, hostElapsedSec, out var quickTaste, out var diminished);

                    // XPSource.Other, matching Programs / Quests / Pop Quiz / the Intake. NOT
                    // XPSource.Session: that source is the session engine's, and borrowing it would
                    // make companion bonuses and quest counters read a web drop as a local session.
                    App.Progression?.AddXP(xp, XPSource.Other);
                    App.Settings?.Save();   // the daily counter must survive a crash between drops

                    App.Logger?.Information("JustDrop: drop settled — order={Order} size={Size} elapsed={Elapsed}s taste={Taste} diminished={Dim} xp={Xp}",
                        code, sizeId ?? "?", hostElapsedSec.HasValue ? (int)hostElapsedSec.Value : -1,
                        quickTaste, diminished, xp);
                }
                catch (Exception ex)
                {
                    // No toast on a failed grant: the completion toast quotes the amount paid,
                    // and quoting XP that was never granted would be worse than silence.
                    App.Logger?.Warning(ex, "JustDrop: session-complete XP failed");
                    return;
                }
            }
            else
            {
                App.Logger?.Information("JustDrop: session-complete for {Order} paid nothing (replay)",
                    string.IsNullOrEmpty(code) ? "(no code)" : code);
            }

            try
            {
                var message = firstPlay
                    ? Loc.GetF("jd_toast_session_complete", xp)
                    : Loc.Get("jd_toast_session_replayed");
                App.Notifications?.Show(message, NotificationType.Success, TimeSpan.FromSeconds(6));
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "JustDrop: session-complete toast failed"); }
        }

        /// <summary>
        /// Price one credited drop. Same order of operations as dropXp.ts settle(): taste-or-base,
        /// daily diminish, then the level multiplier last so drops scale with progression exactly
        /// like every other session award. (No item bonus on desktop — size-only fallback — so the
        /// mobile table's 2000 pre-multiplier cap can never be reached and is not re-stated here.)
        /// </summary>
        private static int SettleDropXp(string? sizeId, double? hostElapsedSec, out bool quickTaste, out bool diminished)
        {
            var size = sizeId != null && SizeBaseXp.ContainsKey(sizeId) ? sizeId : DefaultSizeId;
            var expectedSec = SizeExpectedSeconds[size];

            // No host clock is an untrusted clock (mobile treats a missing reading the same):
            // a completion we cannot time is paid as a taste, never as a full meal.
            quickTaste = hostElapsedSec is null
                         || hostElapsedSec < QuickSeconds
                         || hostElapsedSec < expectedSec * ClockTrust;

            double subtotal = quickTaste ? QuickTasteXp : SizeBaseXp[size];

            diminished = BumpDailyDropCount() >= DiminishAfter;
            if (diminished) subtotal *= DiminishFactor;

            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;

            return (int)Math.Round(subtotal * multiplier);
        }

        /// <summary>
        /// The daily-diminish counter: {dayKey, count} in AppSettings, lazy rollover on read (first
        /// credited drop of a new LOCAL day resets it). Returns how many drops were already
        /// credited today BEFORE this one, which is the figure the diminish compares — mobile's
        /// dropsCompletedToday. The caller saves settings.
        /// </summary>
        private static int BumpDailyDropCount()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return 0;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!string.Equals(settings.JustDropXpDayKey, today, StringComparison.Ordinal))
            {
                settings.JustDropXpDayKey = today;
                settings.JustDropCreditedToday = 0;
            }

            var before = Math.Max(0, settings.JustDropCreditedToday);   // hand-edited negatives read as 0
            settings.JustDropCreditedToday = before + 1;
            return before;
        }
    }
}
