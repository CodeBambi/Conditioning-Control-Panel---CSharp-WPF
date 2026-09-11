using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models.Dashboard;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Dashboard
{
    /// <summary>What the rolodex page just said. Anything else it says is <see cref="Unknown"/>,
    /// which the host drops on the floor.</summary>
    public enum RolodexMessageKind { Unknown, Ready, Pick, TourDone, Close, Log }

    /// <summary>
    /// One parsed page message. Every field is optional because every field belongs to one kind:
    /// <c>Key</c> to a pick, <c>Keys</c> to a tour, <c>Level</c>/<c>Text</c> to a log line.
    /// </summary>
    public sealed record RolodexMessage(
        RolodexMessageKind Kind,
        string? Key = null,
        IReadOnlyList<string>? Keys = null,
        string? Level = null,
        string? Text = null);

    /// <summary>
    /// THE PAGE SIDE OF THE ROLODEX, WITH NO BROWSER IN IT: what an incoming message means, where
    /// the tour's picks land, and whether the 3D picker is still on the table this session.
    ///
    /// <para>The embed view holds the WebView2 and the events; everything it DECIDES is here, so
    /// the decisions can be tested without an STA thread, a runtime or a window - the same split
    /// <see cref="DashboardPickerRule"/> makes for the flat picker.</para>
    /// </summary>
    public static class RolodexBridgeRule
    {
        /// <summary>A tour asks for three, and these are the three cells it fills: the top row's
        /// singles, left to right, skipping the one the shipped wall gives to a split tile. Slot 1
        /// is video|bubblecount out of the box and a tour that took it would silently drop a
        /// feature the user never touched (plan 11.2, owner default).</summary>
        public static readonly IReadOnlyList<int> TourSlots = new[] { 0, 2, 3 };

        /// <summary>How many faces the tour collects. The page draws its own counter from this.</summary>
        public const int TourPickCount = 3;

        /// <summary>
        /// Parse one <c>{type:...}</c> envelope. Malformed JSON, a missing type and a type nobody
        /// here knows all answer <see cref="RolodexMessageKind.Unknown"/>: this runs on the
        /// browser's message loop, where a throw is a crash and a page that has learned a new word
        /// is not an error.
        /// </summary>
        public static RolodexMessage Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new RolodexMessage(RolodexMessageKind.Unknown);

            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception) { return new RolodexMessage(RolodexMessageKind.Unknown); }

            var type = ((string?)o["type"])?.Trim().ToLowerInvariant();
            switch (type)
            {
                case "ready":
                    return new RolodexMessage(RolodexMessageKind.Ready);

                case "pick":
                    var key = CleanKey((string?)o["key"]);
                    // A pick with no key is not a pick. Closing on it would take the picker away
                    // and leave the slot as it was, with nothing said.
                    return key == null
                        ? new RolodexMessage(RolodexMessageKind.Unknown)
                        : new RolodexMessage(RolodexMessageKind.Pick, Key: key);

                case "tourdone":
                    return new RolodexMessage(RolodexMessageKind.TourDone, Keys: ReadKeys(o["keys"]));

                case "close":
                    return new RolodexMessage(RolodexMessageKind.Close);

                case "log":
                    return new RolodexMessage(RolodexMessageKind.Log,
                        Level: (string?)o["level"], Text: (string?)o["msg"]);

                default:
                    return new RolodexMessage(RolodexMessageKind.Unknown);
            }
        }

        /// <summary>The page's <c>level</c> mapped onto Serilog's, through the one mapping every
        /// other bridge in the app already uses. Absent is chatter, never loud.</summary>
        public static Serilog.Events.LogEventLevel LogLevel(string? level)
            => ChaosWebViewHost.PageLogLevel(level);

        /// <summary>
        /// Where a finished tour's picks go. In order, one per <see cref="TourSlots"/> entry, and
        /// short lists simply fill fewer cells - a user who pressed Done with two is not owed a
        /// third tile they did not choose. Keys the catalog does not know are dropped here rather
        /// than refused later, so a stale page cannot leave a gap in the middle of the run.
        /// </summary>
        public static IReadOnlyList<(int Slot, string Key)> TourPlacements(IReadOnlyList<string>? keys)
        {
            var placements = new List<(int, string)>();
            if (keys == null) return placements;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in keys)
            {
                if (placements.Count >= TourSlots.Count) break;
                var row = FeatureCatalog.Find(CleanKey(raw));
                if (row == null || !seen.Add(row.Key)) continue;
                placements.Add((TourSlots[placements.Count], row.Key));
            }
            return placements;
        }

        private static string? CleanKey(string? raw)
        {
            var key = raw?.Trim();
            return string.IsNullOrEmpty(key) ? null : key;
        }

        private static IReadOnlyList<string> ReadKeys(JToken? token)
        {
            var keys = new List<string>();
            if (token is not JArray array) return keys;
            foreach (var item in array)
            {
                var key = CleanKey((string?)item);
                if (key != null) keys.Add(key);
            }
            return keys;
        }
    }

    /// <summary>
    /// ONE GIVE-UP FLAG FOR THE SESSION. A machine with no WebView2 runtime, or one whose browser
    /// will not start, must not pay for the attempt nine times - so the first failure latches and
    /// every later pencil opens the flat picker instead, with no second probe and no second log
    /// line.
    ///
    /// <para>It never un-latches. A runtime that was missing at 10:00 is missing at 10:05, and the
    /// flat picker is not a degraded experience that owes anyone a retry: it is the picker, with
    /// fewer polygons.</para>
    /// </summary>
    public static class RolodexAvailability
    {
        private static bool _givenUp;

        /// <summary>True once the rolodex has failed to start. The host reads this before it
        /// builds anything.</summary>
        public static bool GivenUp => _givenUp;

        /// <summary>Latch it. Idempotent, and only the first call is logged.</summary>
        public static void GiveUp(string reason)
        {
            if (_givenUp) return;
            _givenUp = true;
            App.Logger?.Information("[Rolodex] unavailable this session ({Reason}); the flat picker has it", reason);
        }

        /// <summary>Tests only. There is no product path back from a give-up.</summary>
        internal static void ResetForTests() => _givenUp = false;
    }
}
