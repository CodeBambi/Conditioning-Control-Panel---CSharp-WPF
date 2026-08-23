using System.Globalization;
using System.Text.Json;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// Slice 4 of the Arcademy board row: <c>class-ended</c> → the ONE XP table, the attendance/streak
/// write, and the <c>payout-result</c> reply (upstream
/// <c>Services/Arcademy/ArcademyHostService.OnClassEnded</c>, <c>:1330-1428</c>). Every input is
/// re-clamped: the page reports what happened, the host decides what it was worth. The tables are
/// C#-owned for exactly that reason (<c>:1332-1333</c>), and the page agrees — "XP → C# (the page
/// only reads payout-result)" (<c>arcademy/shell/shell.js:25</c>).
///
/// <para><b>THIS TYPE COMPUTES; IT DOES NOT BANK.</b> Upstream's payout ends at
/// <c>App.Progression?.AddXP(xp, XPSource.Other)</c> (<c>:1396</c>) and reads
/// <c>PlayerLevel</c> either side of it to fill <c>levelUp</c> (<c>:1390</c>, <c>:1399</c>). Both
/// halves now happen in <c>ArcademySession.ClassEnd</c>, against
/// <c>Features/Progression/ProgressionLedger</c> — at upstream's own point in the order, BEFORE the
/// <c>payout-result</c> frame goes out, which is what lets the frame's <c>levelUp</c> be a real
/// comparison instead of a constant. <see cref="Compute"/> stays pure and clock-injected so the
/// table can be checked without a ledger, and <see cref="ArcademyPayout.XpBanked"/> is the marker
/// that says which of the two you are holding.</para>
///
/// <para><b>THE TWO CLOCKS ARE NOT AN ACCIDENT — see <see cref="ArcademyClassDay"/>.</b></para>
/// </summary>
public static class ArcademyClassPayout
{
    /// <summary>The one date format on this surface, on both clocks and in the ledger keys
    /// (<c>:1377</c>, upstream <c>ArcademyMetaStore.cs:396-401</c>).</summary>
    public const string DayFormat = "yyyy-MM-dd";

    /// <summary>Base XP per grade tier (<c>XpBase</c>, <c>:1332-1337</c>, BUILD-CONTRACT §8).
    /// Playtest-tunable, and tunable HERE: the table is C#-owned so a page cannot mint its own
    /// payout.</summary>
    public static readonly IReadOnlyDictionary<int, double> XpBase = new Dictionary<int, double>
    {
        [1] = 40, [2] = 60, [3] = 85, [4] = 110,
    };

    /// <summary>Grade multiplier (<c>XpGradeMult</c>, <c>:1339-1343</c>). Zen reports <c>pass</c>
    /// and pays the B row (DECISIONS #1). Case-insensitive, upstream's own comparer.</summary>
    public static readonly IReadOnlyDictionary<string, double> XpGradeMult =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["S"] = 1.5, ["A"] = 1.25, ["B"] = 1.0, ["C"] = 0.6, ["pass"] = 1.0,
        };

    /// <summary>Ceiling on the per-game flavour bonus, so a game can season its payout without
    /// re-inventing the table (<c>FlavorXpCap</c>, <c>:1345-1347</c>, SYNTHESIS-NOTES #4).</summary>
    public const double FlavorXpCap = 15;

    /// <summary>Upstream truncates the page's game key at 64 chars (<c>:1368</c>) — the one field
    /// with no sane default, so it is bounded rather than defaulted.</summary>
    public const int MaxGameKeyChars = 64;

    /// <summary>The grade a garbled or unknown grade degrades to (<c>:1371</c>).</summary>
    public const string DefaultGrade = "C";

    /// <summary>The grade a zen run always reports, whatever it sent (<c>:1371</c>).</summary>
    public const string ZenGrade = "pass";

    /// <summary>
    /// <b>THE TWO CLOCKS, NAMED APART ON PURPOSE.</b> Upstream reads <c>DateTime.UtcNow</c> for the
    /// XP ledger day (<c>:1379</c>) and <c>DateTime.Now</c> for the attendance date (<c>:1406</c>),
    /// eleven lines apart, from the same <c>class-ended</c> handler — and the init projection does
    /// the same pair at boot (<c>utcDateSeed</c> <c>:564</c>, <c>localDate</c> <c>:566</c>). That
    /// split IS upstream's regression #978, and it is DELIBERATE at both ends: the day's timetable
    /// and its seeds are global, so every player everywhere gets the same three classes on the same
    /// UTC day (<c>:563</c>), while a streak is a thing a person feels at their own midnight, so it
    /// rolls on the local date (upstream <c>ArcademyMetaStore.cs:195-201</c>, and the page repeats the rule
    /// back at <c>core/store.js:40-41</c>: "Do not cross the wires").
    ///
    /// <para><b>THE PORT KEEPS THE SPLIT, and refuses to keep it by accident.</b> The user-visible
    /// outcome it produces is the point: a player east of UTC who finishes a class just after their
    /// local midnight is on a NEW attendance day (their streak advances) but the SAME UTC ledger
    /// day (that class has already been paid for, so the replay is free — <c>retake:true</c>).
    /// "Fixing" it to one clock would either hand out a second payout every evening or freeze a
    /// streak at the wrong hour for most of the planet. Two named fields is what stops the next
    /// reader passing the wrong one: there is no bare <c>string date</c> anywhere in this
    /// path.</para>
    /// </summary>
    /// <param name="UtcSeedDay">Seeds content, and keys the XP ledger. UTC (<c>:1379</c>).</param>
    /// <param name="LocalAttendanceDate">Rolls attendance and the streak. LOCAL (<c>:1406</c>).</param>
    public readonly record struct ArcademyClassDay(string UtcSeedDay, string LocalAttendanceDate)
    {
        /// <summary>Both halves of one instant. <see cref="DateTimeOffset"/> carries the pair the
        /// way <see cref="ArcademyAppFacts.Now"/> already does, so neither date needs a machine
        /// timezone to be checkable.</summary>
        public static ArcademyClassDay From(DateTimeOffset now) => new(
            now.UtcDateTime.ToString(DayFormat, CultureInfo.InvariantCulture),
            now.DateTime.ToString(DayFormat, CultureInfo.InvariantCulture));

        /// <summary>True when this instant falls on different days on the two clocks — the window
        /// regression #978 lives in.</summary>
        public bool ClocksDisagree =>
            !string.Equals(UtcSeedDay, LocalAttendanceDate, StringComparison.Ordinal);
    }

    /// <summary>
    /// One class's payout. Nothing <see cref="Compute"/> returns has been granted — see
    /// <see cref="XpBanked"/> and the type remarks on <see cref="ArcademyClassPayout"/>.
    /// </summary>
    public sealed record ArcademyPayout
    {
        /// <summary>The page's game key, trimmed and truncated to
        /// <see cref="MaxGameKeyChars"/> (<c>:1367-1368</c>).</summary>
        public string GameKey { get; init; } = "";

        /// <summary>1..4 (<c>:1369</c>).</summary>
        public int GradeTier { get; init; }

        /// <summary>The grade the payout was actually computed on, after the zen and
        /// unknown-grade rules (<c>:1370-1371</c>).</summary>
        public string Grade { get; init; } = "";

        /// <summary>The per-game bonus after its cap (<c>:1372</c>).</summary>
        public double FlavorXp { get; init; }

        /// <summary>Computed XP: <c>base[tier] × mult[grade] + flavour</c>, or 0 on a retake
        /// (<c>:1388</c>). NEVER granted.</summary>
        public double Xp { get; init; }

        /// <summary>The class had already been paid for on <see cref="XpLedgerUtcDay"/> — a free
        /// replay (<c>:1386</c>, <c>:1420</c>).</summary>
        public bool Retake { get; init; }

        /// <summary>The UTC day the XP ledger was billed against (<c>:1376-1385</c>).</summary>
        public string XpLedgerUtcDay { get; init; } = "";

        /// <summary>The LOCAL date attendance was credited to (<c>:1406</c>). Deliberately not the
        /// same field as <see cref="XpLedgerUtcDay"/>; see <see cref="ArcademyClassDay"/>.</summary>
        public string AttendanceLocalDate { get; init; } = "";

        /// <summary>Consecutive local days with at least one class, after this credit.</summary>
        public int Streak { get; init; }

        /// <summary>Lifetime all-three days, after this credit.</summary>
        public int PerfectAttendance { get; init; }

        /// <summary>Classes credited on <see cref="AttendanceLocalDate"/>, after this credit.</summary>
        public int ClassesToday { get; init; }

        /// <summary>Whether <see cref="Xp"/> reached the XP ledger. <b>Always false on a value that
        /// came straight out of <see cref="Compute"/></b>, which computes and banks nothing;
        /// <c>ArcademySession.ClassEnd</c> stamps the real answer on after the grant, at the point
        /// upstream calls <c>AddXP</c> (<c>:1394-1398</c>).</summary>
        public bool XpBanked { get; init; }

        /// <summary>Why <see cref="XpBanked"/> is false, carried on the value so no consumer has to
        /// guess. Empty when it is true.</summary>
        public string XpBankedReason { get; init; } = NotBankedByCompute;

        /// <summary>Upstream's <c>levelUp</c>, a real before/after comparison across the grant
        /// (<c>:1390</c>, <c>:1399</c>, frame field at <c>:1416</c>). False on a value
        /// <see cref="Compute"/> produced, for the same reason as <see cref="XpBanked"/>.</summary>
        public bool LevelUp { get; init; }
    }

    /// <summary>Why a freshly computed payout is not banked: because computing is not banking. The
    /// real refusal reasons come from <c>Progression.XpGrant.Reason</c>.</summary>
    public const string NotBankedByCompute = "computed here; ArcademySession.ClassEnd banks it";

    /// <summary>
    /// The whole of <c>OnClassEnded</c>'s decision half (<c>:1354-1409</c>), in upstream's order:
    /// read every field defensively, claim the UTC ledger day, compute, THEN credit attendance on
    /// the local date.
    ///
    /// <para><b>Order is load-bearing and so is the tolerance.</b> Upstream's own comment
    /// (<c>:1359-1366</c>) records that a single throwing field read used to abort the handler
    /// BEFORE the attendance write, so "one malformed field cost the player the day's attendance
    /// and their streak". Attendance is the thing that must not be lost: a garbled grade degrades
    /// to <see cref="DefaultGrade"/>, a garbled bonus to 0, and the credit is still written.</para>
    /// </summary>
    /// <param name="fields">The <c>class-ended</c> frame's own object — the page sends
    /// <c>gameKey</c>, <c>gradeTier</c>, <c>grade</c>, <c>zen</c>, <c>flavorXp</c> and
    /// <c>dayUtc</c> (<c>arcademy/shell/shell.js:1138-1146</c>).</param>
    /// <param name="meta">The meta store that owns the ledger and the streak. Null pays and
    /// credits nothing, which is upstream's <c>_meta?.… ?? (0, 0, 0)</c> (<c>:1407</c>).</param>
    /// <param name="now">The clock, read ONCE here so both dates come from the same instant.</param>
    public static ArcademyPayout Compute(JsonElement fields, ArcademyMetaStore? meta, DateTimeOffset now)
    {
        var day = ArcademyClassDay.From(now);

        var gameKey = (ReadString(fields, "gameKey") ?? "").Trim();
        if (gameKey.Length > MaxGameKeyChars)
        {
            gameKey = gameKey[..MaxGameKeyChars];                                   // :1368
        }

        var tier = Math.Clamp(ReadInt(fields, "gradeTier", 1), 1, 4);               // :1369
        var zen = ReadBool(fields, "zen", false);                                   // :1370
        var grade = (ReadString(fields, "grade") ?? "").Trim();
        if (zen || !XpGradeMult.ContainsKey(grade))
        {
            grade = zen ? ZenGrade : DefaultGrade;                                  // :1371
        }

        var flavor = Math.Clamp(ReadDouble(fields, "flavorXp", 0), 0, FlavorXpCap); // :1372

        // THE FARM GUARD (:1374-1386): one payout per class per UTC day. The day is RE-DERIVED
        // here when the page's dayUtc is missing or malformed — otherwise dropping the field would
        // be the bypass.
        var pageDay = (ReadString(fields, "dayUtc") ?? "").Trim();
        var ledgerDay = TryParseDay(pageDay, out _) ? pageDay : day.UtcSeedDay;

        var firstToday = meta?.TryClaimXpDay(gameKey, ledgerDay) ?? true;
        var xp = firstToday ? XpBase[tier] * XpGradeMult[grade] + flavor : 0;       // :1388

        // LOCAL date rolls attendance; the page's dayUtc only ever seeded the content (#978), so it
        // is deliberately NOT what gets written here. RecordAttendance is idempotent per
        // (day, gameKey), so a retake cannot inflate todayClasses or perfect attendance — and
        // running it unconditionally is what keeps a retake on a NEW local day (same UTC day,
        // player east of UTC) crediting the streak it has earned (:1401-1407).
        var (streak, perfect, classesToday) = meta?.RecordAttendance(day.LocalAttendanceDate, gameKey)
            ?? (0, 0, 0);

        return new ArcademyPayout
        {
            GameKey = gameKey,
            GradeTier = tier,
            Grade = grade,
            FlavorXp = flavor,
            Xp = xp,
            Retake = !firstToday,
            XpLedgerUtcDay = ledgerDay,
            AttendanceLocalDate = day.LocalAttendanceDate,
            Streak = streak,
            PerfectAttendance = perfect,
            ClassesToday = classesToday,
        };
    }

    /// <summary>Strict <c>yyyy-MM-dd</c>, invariant, trimmed (<c>:1380-1381</c>,
    /// upstream <c>ArcademyMetaStore.cs:396-401</c>). Anything else is not a day.</summary>
    public static bool TryParseDay(string? text, out DateTime day) =>
        DateTime.TryParseExact((text ?? "").Trim(), DayFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out day);

    // Field readers that DEGRADE instead of throwing (:1430-1471). Upstream's reason is Newtonsoft:
    // an explicit JToken cast throws for a type it cannot convert, inside a handler whose last act
    // is the attendance write. System.Text.Json does not throw on a shape mismatch the same way,
    // but the TOLERANCE upstream bought with those readers is real and user-visible — a page that
    // sends "3" instead of 3 still gets tier 3 — so it is ported rather than assumed away.

    /// <summary>String, or null (<c>ReadString</c>, <c>:1433-1434</c>).</summary>
    public static string? ReadString(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    /// <summary>Number or numeric string, else the fallback (<c>ReadInt</c>, <c>:1436-1448</c>).
    /// A float truncates toward zero the way <c>Convert.ToInt32</c> rounds to nearest — see the
    /// remark on <see cref="ReadDouble"/> for why the difference cannot reach a payout.</summary>
    public static int ReadInt(JsonElement o, string name, int fallback)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out var p))
        {
            return fallback;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt32(out var i) => i,
            // Convert.ToInt32 rounds half to even; a fractional gradeTier is clamped to 1..4 by the
            // caller either way, so this matches every value the field can carry.
            JsonValueKind.Number when p.TryGetDouble(out var d) && double.IsFinite(d) =>
                (int)Math.Round(d, MidpointRounding.ToEven),
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback,
        };
    }

    /// <summary>Number or numeric string, non-finite degrades to the fallback
    /// (<c>ReadDouble</c>, <c>:1450-1465</c>).</summary>
    public static double ReadDouble(JsonElement o, string name, double fallback)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out var p))
        {
            return fallback;
        }

        var value = p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(p.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback,
        };

        return double.IsFinite(value) ? value : fallback;
    }

    /// <summary>Boolean only — a string "true" is NOT a boolean here, exactly as upstream
    /// (<c>ReadBool</c>, <c>:1467-1471</c>).</summary>
    public static bool ReadBool(JsonElement o, string name, bool fallback)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out var p))
        {
            return fallback;
        }

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }
}
