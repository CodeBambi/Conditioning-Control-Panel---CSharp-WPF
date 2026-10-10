using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// WPF 7.1.5 Services/JustDrop/JustDropService.cs: whether the Just Drop door exists for this
    /// launch, and the page's bridge (session start / exit / complete and the drop's XP).
    ///
    /// <para><b>Withheld by default.</b> The door appears only when the server says so:
    /// <see cref="DoorAvailable"/> is a per-launch GET with a false default and no persistence, so
    /// nothing in settings.json can open it. Just Drop is for everyone (Tier 0): there is no tier
    /// check here and never a Prime badge on its card.</para>
    ///
    /// <para>Everything about a drop lives in the web app. This type opens nothing itself; the
    /// window is the head's (CCP.Avalonia Views/Games/GameWindow.JustDrop.cs) and the start url and
    /// sign-in handoff rule are <see cref="JustDropHostService"/>.</para>
    /// </summary>
    internal static class JustDropService
    {
        public const string ExpressUrl = "https://app.cclabs.app/dashboard/express";

        private const string TasteBaseUrl = "https://cclabs.app/taste";

        public static string TasteUrl(string orderCode) =>
            $"{TasteBaseUrl}/{Uri.EscapeDataString(orderCode ?? string.Empty)}";

        private const string BridgeSource = "justdrop";

        private const int BridgeVersion = 1;

        // ============================== the gate ==============================

        /// <summary>The local kill switch. False = the server decides.</summary>
        public static readonly bool Withheld = false;

        private static volatile bool ServerEnabled;

        public static bool DoorAvailable => !Withheld && ServerEnabled;

        /// <summary>Raised on the head's UI thread (CoreDispatch) when the flag changes.</summary>
        public static event EventHandler? AvailabilityChanged;

        internal const string ConfigUrl = "https://codebambi-proxy.vercel.app/config/justdrop";

        /// <summary>Test seam: the GET of <see cref="ConfigUrl"/> (status ok, body).</summary>
        internal static Func<Task<(bool Ok, string? Body)>>? ConfigFetchOverride;

        /// <summary>Test seam: sets the server verdict as a fetch would, raising the event on a change.</summary>
        internal static void SetServerEnabledForTests(bool enabled)
        {
            if (enabled == ServerEnabled) return;
            ServerEnabled = enabled;
            RaiseAvailabilityChanged();
        }

        /// <summary>One read of the server flag. Any failure leaves the door as it was (hidden on a cold start).</summary>
        public static async Task RefreshAvailabilityAsync()
        {
            if (Withheld) return;   // nothing the server can say would matter

            try
            {
                var (ok, body) = await (ConfigFetchOverride ?? FetchConfigAsync)().ConfigureAwait(false);
                if (!ok) return;
                var enabled = ParseEnabled(body);

                if (enabled == ServerEnabled) return;
                ServerEnabled = enabled;
                Log.Information("JustDrop: server flag = {Enabled}", enabled);
                RaiseAvailabilityChanged();
            }
            catch (Exception ex)
            {
                // Debug, not Warning: a shop that does not answer is the normal case for most launches.
                Log.Debug("JustDrop: availability check failed ({Error}); door stays hidden", ex.Message);
            }
        }

        /// <summary>The body's verdict: only a literal <c>"enabled": true</c> opens the door.</summary>
        internal static bool ParseEnabled(string? body)
        {
            JObject? parsed = null;
            try { if (!string.IsNullOrWhiteSpace(body)) parsed = JObject.Parse(body!); } catch { }
            try { return parsed?["enabled"]?.Type == JTokenType.Boolean && parsed["enabled"]!.Value<bool>(); }
            catch { return false; }
        }

        private static async Task<(bool Ok, string? Body)> FetchConfigAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await http.GetAsync(ConfigUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug("JustDrop: /config/justdrop returned {Status}; door stays hidden", (int)response.StatusCode);
                return (false, null);
            }
            return (true, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        private static void RaiseAvailabilityChanged()
        {
            void Raise()
            {
                try { AvailabilityChanged?.Invoke(null, EventArgs.Empty); }
                catch (Exception ex) { Log.Warning(ex, "JustDrop: AvailabilityChanged handler threw"); }
            }
            var post = CoreDispatch.PostProvider;
            if (post == null) { Raise(); return; }
            try { post(Raise); }
            catch (Exception ex) { Log.Debug("JustDrop: could not reach the UI thread: {E}", ex.Message); }
        }

        // ============================== the bridge ==============================
        //
        // The settlement table mirrors the phone host's size-only fallback: size base, a quick-taste
        // floor under any clock we do not trust, the 4th-drop-of-the-day quarter, then the level
        // multiplier. The page's durationSec is browser-authored and NEVER trusted: the host runs its
        // own clock from session-start, and a completion with no host-measured start settles as a taste.

        private static readonly IReadOnlyDictionary<string, int> SizeBaseXp =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["S"] = 300, ["M"] = 650, ["L"] = 1000, ["XXL"] = 1400 };

        private static readonly IReadOnlyDictionary<string, int> SizeExpectedSeconds =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["S"] = 300, ["M"] = 900, ["L"] = 1800, ["XXL"] = 3600 };

        private const string DefaultSizeId = "M";
        internal const int QuickTasteXp = 40;
        private const int QuickSeconds = 120;
        private const double ClockTrust = 0.8;
        private const int DiminishAfter = 3;
        private const double DiminishFactor = 0.25;

        private static readonly Dictionary<string, DateTime> SessionStartUtc = new(StringComparer.Ordinal);

        /// <summary>Test seam: the host's clock.</summary>
        internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

        /// <summary>The last grant <see cref="HandleWebMessage"/> made (0 = a replay or a refusal); tests read it.</summary>
        internal static int LastAwardedXp { get; private set; }

        /// <summary>One raw page message. Foreign sources and other versions are dropped quietly.</summary>
        public static void HandleWebMessage(string? json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;

                JObject envelope;
                try { envelope = JObject.Parse(json!); }
                catch (Exception ex)
                {
                    Log.Debug("JustDrop bridge: unparseable message dropped ({Error})", ex.Message);
                    return;
                }

                // Not ours: the page shares its web view with whatever the site itself posts.
                if (!string.Equals((string?)envelope["source"], BridgeSource, StringComparison.Ordinal)) return;

                var version = envelope["v"]?.Value<int?>() ?? 0;
                if (version != BridgeVersion)
                {
                    Log.Debug("JustDrop bridge: envelope v{V} ignored (host speaks v{Ours})", version, BridgeVersion);
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
                        Log.Debug("JustDrop bridge: page ready");
                        break;

                    case "session-start":
                        Log.Information("JustDrop bridge: session-start order={Order} size={Size} duration={Duration}s",
                            orderCode ?? "?", sizeId ?? "?", durationSec ?? 0);
                        // The HOST's clock for this order. A restart for the same code overwrites.
                        if (!string.IsNullOrEmpty(orderCode))
                            lock (SessionStartUtc) SessionStartUtc[orderCode!] = UtcNow();
                        break;

                    case "session-exit":
                        Log.Information("JustDrop bridge: session-exit order={Order} after {Duration}s",
                            orderCode ?? "?", durationSec ?? 0);
                        break;

                    case "session-complete":
                        Log.Information("JustDrop bridge: session-complete order={Order} size={Size} duration={Duration}s",
                            orderCode ?? "?", sizeId ?? "?", durationSec ?? 0);
                        AwardSessionComplete(orderCode, sizeId);
                        break;

                    default:
                        // A page shipped ahead of the host: name it for the next reader, do nothing.
                        Log.Debug("JustDrop bridge: unknown type '{Type}' ignored", type ?? "(null)");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "JustDrop bridge: message handling failed");
            }
        }

        private static void AwardSessionComplete(string? orderCode, string? sizeId)
        {
            LastAwardedXp = 0;
            var code = orderCode?.Trim();
            bool firstPlay = !string.IsNullOrEmpty(code) && CreditedOrders.TryCredit(code!);

            int xp = 0;
            if (firstPlay)
            {
                try
                {
                    // The host's clock, or no clock at all. Keyed by the RAW code, as session-start stored it.
                    double? hostElapsedSec = null;
                    DateTime startedUtc;
                    bool had;
                    lock (SessionStartUtc) had = SessionStartUtc.Remove(orderCode!, out startedUtc);
                    if (had)
                    {
                        var elapsed = (UtcNow() - startedUtc).TotalSeconds;
                        if (elapsed >= 0) hostElapsedSec = elapsed;
                    }

                    xp = SettleDropXp(sizeId, hostElapsedSec, BumpDailyDropCount(),
                        CoreSettings.Current.PlayerLevel, out var quickTaste, out var diminished);

                    // XPSource.Other, as Programs / Quests / Pop Quiz / the Intake: never Session, or
                    // companion bonuses and quest counters would read a web drop as a local session.
                    CoreProgression.AddXP(xp, "Other");
                    CoreSettings.Save();   // the daily counter must survive a crash between drops
                    LastAwardedXp = xp;

                    Log.Information("JustDrop: drop settled - order={Order} size={Size} elapsed={Elapsed}s taste={Taste} diminished={Dim} xp={Xp}",
                        code, sizeId ?? "?", hostElapsedSec.HasValue ? (int)hostElapsedSec.Value : -1, quickTaste, diminished, xp);
                }
                catch (Exception ex)
                {
                    // No toast on a failed grant: quoting XP that was never granted is worse than silence.
                    Log.Warning(ex, "JustDrop: session-complete XP failed");
                    return;
                }
            }
            else
            {
                Log.Information("JustDrop: session-complete for {Order} paid nothing (replay)",
                    string.IsNullOrEmpty(code) ? "(no code)" : code);
            }

            try
            {
                var message = firstPlay
                    ? Loc.GetF("jd_toast_session_complete", xp)
                    : Loc.Get("jd_toast_session_replayed");
                CoreProgram.Notify(message, "Success", TimeSpan.FromSeconds(6));
            }
            catch (Exception ex) { Log.Warning(ex, "JustDrop: session-complete toast failed"); }
        }

        /// <summary>
        /// The drop's XP: size base (unknown size = M), a taste (40) when the host has no clock, under
        /// two minutes, or under 80% of the size's length; a quarter from the 4th drop of the local
        /// day (<paramref name="creditedBefore"/> = drops already paid today); then the level multiplier.
        /// </summary>
        internal static int SettleDropXp(string? sizeId, double? hostElapsedSec, int creditedBefore, int level,
            out bool quickTaste, out bool diminished)
        {
            var size = sizeId != null && SizeBaseXp.ContainsKey(sizeId) ? sizeId : DefaultSizeId;
            var expectedSec = SizeExpectedSeconds[size];

            quickTaste = hostElapsedSec is null
                         || hostElapsedSec < QuickSeconds
                         || hostElapsedSec < expectedSec * ClockTrust;

            double subtotal = quickTaste ? QuickTasteXp : SizeBaseXp[size];

            diminished = creditedBefore >= DiminishAfter;
            if (diminished) subtotal *= DiminishFactor;

            return (int)Math.Round(subtotal * SessionXp.Multiplier(level));
        }

        /// <summary>Counts this drop against the local day and returns how many were paid before it.</summary>
        private static int BumpDailyDropCount()
        {
            var settings = CoreSettings.Current;

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
