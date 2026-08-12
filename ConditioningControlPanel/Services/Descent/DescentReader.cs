using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    // ============================================================================
    // THE DESCENT — the only thing allowed to trust an unknown payload.
    //
    // Ported field-for-field from the web client's reader
    // (cclabs-web src/lib/descent/data.ts), because three clients rendering one
    // wire contract must degrade identically. Where the two differ, that is a bug
    // in one of them, not a platform difference.
    //
    // TWO OUTCOMES, NOT THREE. A well-formed block parses; ANYTHING else — no key,
    // wrong type, junk numbers — returns null, and every Descent surface renders
    // nothing. There is deliberately no desktop demo dataset: the server withholds
    // the key from every account outside the rollout width, and withholding it is
    // precisely how a staged rollout is meant to be invisible.
    // ============================================================================
    public static class DescentReader
    {
        /// <summary>20% of cap banks the day (planning/one-descent DECISIONS.md 2026-08-11).</summary>
        public const double BankThresholdPct = 20.0;

        /// <summary>100% = the level-scaled daily cap.</summary>
        public const double CapPct = 100.0;

        /// <summary>
        /// The overflow lip WHEN THE SERVER NAMES NONE. The lip is dynamic
        /// (120 base, 125 at stage 4+, 130 at stage 6+), so this is the floor of
        /// that range and the only legal default — never a constant to draw against.
        /// </summary>
        public const double DefaultLipPct = 120.0;

        /// <summary>
        /// The highest lip any surface will draw. The ladder's top perk is 130; this
        /// exists only so a corrupt number cannot rescale the jar until the CAP line
        /// sits on the glass floor.
        /// </summary>
        public const double MaxLipPct = 200.0;

        /// <summary>Longest ladder this client can draw (seven rungs are shipped).</summary>
        public const int MaxStage = 7;

        /// <summary>
        /// Parse a wire payload with DATE COERCION OFF, which this reader requires.
        ///
        /// `JObject.Parse` runs with `DateParseHandling.DateTime`, so Newtonsoft
        /// silently rewrites any ISO-8601-shaped STRING into a JTokenType.Date. Every
        /// date-ish field the descent block carries is exactly that shape —
        /// `devotion_last_day` and `history_from` ("2026-08-12"), `day_fill_start`,
        /// `relapse.surge_ends_at` — so a strict `Type == String` reader (which is
        /// what the web client is, and what this one has to match) reads them all as
        /// ABSENT. The bug is silent in the worst way: the vat still draws, the
        /// stage still resolves, and only the dates quietly vanish.
        ///
        /// Coercion also destroys what the wire said. A day string round-trips out of
        /// a DateTime as a full instant with an invented midnight and an invented
        /// offset, so there is no repair on the reading side — the fix has to be at
        /// the parse boundary, which is here. One definition, used by the fetch path
        /// and by the tests that pin it.
        /// </summary>
        public static JObject? ParseWire(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                };
                return JObject.Load(reader);
            }
            catch (Exception ex)
            {
                // The TYPE and MESSAGE only — never the payload. This parses the whole
                // /v2/user/profile response, which carries the account's identifiers,
                // and a log line is the one place they must not end up. Debug because
                // an unparseable body is a server or transport problem that the caller
                // already degrades correctly for (null block => render nothing);
                // without this line it degraded SILENTLY, which is indistinguishable
                // from "this account has no key".
                Log.Debug("[Descent] wire parse failed: {Type}: {Message}",
                    ex.GetType().Name, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Parse the `descent` block off the `user` node of a GET /v2/user/profile
        /// payload. Returns null for every unusable shape, including a payload with
        /// no `descent` key at all.
        /// </summary>
        public static DescentBlock? ParseFromUserNode(JToken? userNode)
        {
            if (userNode is not JObject user) return null;
            return Parse(user["descent"]);
        }

        /// <summary>
        /// Parse a `descent` block. Null for anything unusable — see the tri-state
        /// law at the top of DescentModels.cs.
        /// </summary>
        public static DescentBlock? Parse(JToken? raw)
        {
            if (raw is not JObject block) return null;

            // devotion_days is THE authority for how far the descent has turned. A
            // block without it is not a block; it is a shape that happens to be an
            // object, and rendering it would be inventing a record.
            double? devotion = Num(block["devotion_days"]);
            if (devotion is null || devotion < 0) return null;
            int days = (int)Math.Floor(devotion.Value);

            var dayFill = ReadDayFill(block["day_fill"]);

            return new DescentBlock
            {
                DevotionDays = days,
                DevotionLastDay = Str(block["devotion_last_day"]),
                Stage = ReadStage(block["stage"], days),
                Relapse = ReadRelapse(block["relapse"]),
                Vat = ReadVat(block["vat"]),
                DayFill = dayFill,
                DayFillStart = Str(block["day_fill_start"]),
                DayFillStartDayN = ReadStartDayN(block["day_fill_start_day_n"], dayFill.Count, days),
                HistoryFrom = Str(block["history_from"]),
            };
        }

        // ---------------------------------------------------------------- readers

        /// <summary>
        /// The vat, or null when the server shipped it dark. A cap that floors to
        /// less than 1 is the documented dark state and is NOT clamped up to
        /// something drawable.
        /// </summary>
        internal static DescentVat? ReadVat(JToken? raw)
        {
            if (raw is not JObject v) return null;

            // THE GUARD IS ON THE NUMBER THIS ACTUALLY SHIPS. `cap` is floored to an
            // int below, so testing the raw double for `> 0` would let a cap of 0.4
            // through as Cap = 0 — a "live" vat with a zero cap, which is exactly the
            // dark state this branch exists to catch. Floor first, judge the result.
            double? rawCap = Num(v["cap"]);
            if (rawCap is null) return null;
            int cap = (int)Math.Floor(rawCap.Value);
            if (cap < 1) return null;

            double today = Math.Max(0, Num(v["today_xp"]) ?? 0);
            double? pct = Num(v["fill_pct"]);

            // The lip is the ladder's, not a constant. Clamped to [CapPct, MaxLipPct]:
            // the floor because a lip under the cap would put MAX below CAP and invert
            // the meter, the ceiling because a runaway number would scale the jar until
            // the cap line sat on the floor of the glass. Both are absurd-input guards.
            // A LIP OF EXACTLY 100 IS LEGAL and means "no lip" (see DescentVat.HasLip).
            double? lip = Num(v["fill_lip_pct"]);

            return new DescentVat
            {
                Cap = cap,
                TodayXp = (int)Math.Floor(today),
                FillPct = pct is not null ? Math.Max(0, pct.Value) : (today / cap) * 100.0,
                FillLipPct = lip is not null
                    ? Math.Max(CapPct, Math.Min(MaxLipPct, lip.Value))
                    : DefaultLipPct,
            };
        }

        internal static DescentStage? ReadStage(JToken? raw, int devotionDays)
        {
            if (raw is not JObject s) return null;
            double? n = Num(s["n"]);
            if (n is null) return null;

            int rung = (int)Math.Max(0, Math.Min(MaxStage, Math.Floor(n.Value)));
            return new DescentStage
            {
                N = rung,
                Key = Str(s["key"]) ?? $"stage_{rung}",
                BankedDays = (int)Math.Max(0, Math.Floor(Num(s["banked_days"]) ?? devotionDays)),
                NextAt = IntOrNull(s["next_at"]),
                DaysToNext = IntOrNull(s["days_to_next"]),
                Thresholds = ReadThresholds(s["thresholds"]),
            };
        }

        /// <summary>
        /// The whole active ladder, when the server states it. Validated to the rule
        /// the server validates its own env override against: 1..MaxStage positive
        /// integers, STRICTLY INCREASING. Anything else is null and the caller falls
        /// back to its local table — a half-read ladder ranks people against days
        /// nobody is ranked against, which is worse than being out of date.
        /// </summary>
        internal static IReadOnlyList<int>? ReadThresholds(JToken? raw)
        {
            if (raw is not JArray arr || arr.Count == 0 || arr.Count > MaxStage) return null;
            var outList = new List<int>(arr.Count);
            int previous = 0;
            foreach (var item in arr)
            {
                double? n = Num(item);
                if (n is null) return null;
                if (n.Value != Math.Floor(n.Value)) return null;
                int i = (int)n.Value;
                if (i <= previous) return null;
                previous = i;
                outList.Add(i);
            }
            return outList;
        }

        internal static DescentRelapse? ReadRelapse(JToken? raw)
        {
            if (raw is not JObject r) return null;
            double? m = Num(r["multiplier"]);
            if (m is null) return null;
            return new DescentRelapse
            {
                Multiplier = Math.Max(1.0, m.Value),
                DaysAway = (int)Math.Max(0, Math.Floor(Num(r["days_away"]) ?? 0)),
                SurgeActive = r["surge_active"]?.Type == JTokenType.Boolean && r["surge_active"]!.Value<bool>(),
                SurgeEndsAt = Str(r["surge_ends_at"]),
                SurgeMultiplier = Math.Max(1.0, Num(r["surge_multiplier"]) ?? 1.0),
            };
        }

        /// <summary>
        /// Per-BANKED-day fill buckets, oldest first. NOT padded to devotion_days —
        /// the server's array is short at the FRONT (history begins where its store
        /// was switched on) and the ordinal of index 0 arrives separately.
        /// </summary>
        internal static IReadOnlyList<int> ReadDayFill(JToken? raw)
        {
            if (raw is not JArray arr) return Array.Empty<int>();
            var outList = new List<int>(arr.Count);
            foreach (var item in arr)
            {
                double? n = Num(item);
                outList.Add(n is null
                    ? (int)BankThresholdPct
                    : (int)Math.Max(0, Math.Round(n.Value, MidpointRounding.AwayFromZero)));
            }
            return outList;
        }

        /// <summary>
        /// Banked-day ordinal of day_fill[0]. The server states it and counts BACK
        /// from devotion_days, so a veteran with 300 banked days and 40 recorded ones
        /// gets 261. The same rule is applied locally only when it is absent.
        /// </summary>
        internal static int ReadStartDayN(JToken? raw, int fillLength, int days)
        {
            double? stated = Num(raw);
            if (stated is not null && stated >= 0) return (int)Math.Floor(stated.Value);
            if (fillLength == 0) return 0;
            return Math.Max(1, days - fillLength + 1);
        }

        // ------------------------------------------------------------- primitives

        /// <summary>
        /// A finite JSON NUMBER, or null. Numeric strings are deliberately rejected:
        /// the web reader tests `typeof v === 'number'` and a client that is more
        /// permissive than its siblings is a client that renders a different vat.
        /// </summary>
        internal static double? Num(JToken? t)
        {
            if (t is null) return null;
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) return null;
            double d;
            try { d = t.Value<double>(); }
            catch { return null; }
            return double.IsFinite(d) ? d : null;
        }

        internal static int? IntOrNull(JToken? t)
        {
            double? n = Num(t);
            return n is null ? null : (int)Math.Floor(n.Value);
        }

        internal static string? Str(JToken? t)
        {
            if (t is null || t.Type != JTokenType.String) return null;
            string? s = t.Value<string>();
            return string.IsNullOrEmpty(s) ? null : s;
        }
    }
}
