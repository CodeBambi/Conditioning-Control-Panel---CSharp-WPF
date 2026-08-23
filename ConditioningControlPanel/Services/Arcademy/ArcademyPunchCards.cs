using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// THE PUNCH-CARD MATH, pure (PUNCHCARD.md §2/§3). One card per class, ten holes; the card
/// completes into a permanent unlock for that room.
///
/// <para>Deliberately free of <see cref="App"/>, of the store's lock and of WPF: it takes the
/// <c>punchCards</c> blob and a local date, mutates the one card in place and reports what
/// happened. <see cref="ArcademyMetaStore"/> owns persistence and logging;
/// <see cref="ArcademyHostService"/> owns the frames. Same split the page draws between
/// <c>core/grades.js</c> (pure rubric) and the shell that runs it — and the reason this half is
/// testable at all, since neither host file has a test host of its own (CLAUDE.md trap 31's
/// note).</para>
///
/// <para>SHAPE, per game key:
/// <c>{punches:0..10, dates:["yyyy-MM-dd"], sDates:["yyyy-MM-dd"], enrolledAt:"yyyy-MM-dd"|null,
/// house:bool, complete:bool, unlockedAt:"yyyy-MM-dd"|null}</c>. <c>punches</c> is denormalized
/// for the page but RECOMPUTED on every touch (<see cref="Normalize"/>), so a hand-edited or
/// half-merged blob heals itself rather than carrying a wrong total forever.</para>
///
/// <para>THE PACE, and THE FORMULA (owner ruling 2026-08-23). Three levers shortened the time to
/// master a card, and only the last two are visible here:
/// <list type="bullet">
///   <item>enrolling is worth <see cref="EnrollPunches"/> = 3 holes, not two;</item>
///   <item>a stamped day the class graded S is worth <see cref="SPunches"/> = 2, not one —
///         which is all <c>sDates</c> is, the subset of <c>dates</c> that graded S;</item>
///   <item>(the third lever is four classes a day, which lives in the timetable.)</item>
/// </list>
/// So: <c>punches = min(10, (enrolledAt != null ? 3 : 0) + dates.Count + sDates.Count)</c>, and
/// <c>core/store.js punchCard()</c> computes that same line character for character. The two MUST
/// agree: the page re-derives every total it draws, so a formula that drifts here draws a hole the
/// host does not hold.</para>
///
/// <para>OLD BLOBS ARE PROMOTED, DELIBERATELY. A card enrolled under the two-punch rule carries no
/// marker of which rule it was minted under — <c>enrolledAt</c> is the only earned field — so the
/// re-derivation simply pays it three. Everybody who enrolled before today gains one hole on the
/// next touch. That is the generous reading and it cannot overflow: the total is capped at
/// <see cref="Holes"/>, and a card already complete stays complete.</para>
///
/// <para>LOCAL dates throughout — never the UTC seed. Regression #978 / CLAUDE.md trap 8: the UTC
/// day seeds tonight's content, local midnight rolls every daily gate the player feels.</para>
/// </summary>
internal static class ArcademyPunchCards
{
    /// <summary>Holes on a card. Reaching it completes the semester assignment and unlocks the
    /// room permanently (PUNCHCARD §1).</summary>
    public const int Holes = 10;

    /// <summary>Holes the first-run mint is worth, all at once (owner ruling 2026-08-23: three —
    /// the tutorial punch, the one on the house, and one for signing on). Mirrored by
    /// <c>core/store.js ENROLL_PUNCHES</c> and <c>shell/punchcard.js ENROLL_PUNCHES</c>.</summary>
    public const int EnrollPunches = 3;

    /// <summary>Holes a stamped day is worth when the class graded S (an ordinary day is one).
    /// Mirrored by <c>core/store.js S_DAY_PUNCHES</c>.</summary>
    public const int SPunches = 2;

    /// <summary>Field names, spelled once so the store, the host and the page cannot drift.</summary>
    public const string PunchesField = "punches";
    public const string DatesField = "dates";
    /// <summary>The subset of <see cref="DatesField"/> whose class graded S — each is worth a
    /// SECOND hole. Always intersected with <c>dates</c> on the way out, so a stray entry can
    /// never buy a hole on its own.</summary>
    public const string SDatesField = "sDates";
    public const string EnrolledAtField = "enrolledAt";
    public const string HouseField = "house";
    public const string CompleteField = "complete";
    public const string UnlockedAtField = "unlockedAt";

    /// <summary>The outcome of one mint attempt. <paramref name="Minted"/> is false for every
    /// idempotent no-op (same day twice, already enrolled, card already full) — the caller uses it
    /// to decide whether anything needs saving or broadcasting.</summary>
    /// <param name="Minted">Did the card actually change?</param>
    /// <param name="Punches">How many holes this mint bought: 3 for an enrollment, 2 for a day the
    /// class graded S, 1 for an ordinary day, 0 for every no-op. This is the number that rides
    /// <c>punchcard-result.minted</c>, because the ceremony has to know how many beats to play —
    /// it is a COUNT on the wire, not a flag (0 is still falsy, so the shell's "no ceremony for a
    /// no-op" test is unchanged).</param>
    /// <param name="JustUnlocked">Did THIS mint carry the card over the tenth hole?</param>
    /// <param name="Card">The card after the attempt (a live handle into the blob).</param>
    internal readonly record struct PunchMint(bool Minted, int Punches, bool JustUnlocked, JObject Card);

    /// <summary>
    /// A DAILY STAMP: one per (local day, game), earned by any graded finish. Called from the
    /// attendance path, which only ever runs on <c>class-ended</c> — so Esc-leave (no grade, no
    /// frame) and Free Swim (the shell returns before it sends one) can never reach here.
    ///
    /// <para>Idempotence is keyed on the card's OWN <c>dates</c> rather than on the attendance
    /// key's <c>todayClasses</c>: the two agree, but <c>todayClasses</c> is reset every local day
    /// and is sheddable by salvage, and a stamp that survives on its own terms is one less way to
    /// lose a card.</para>
    ///
    /// <para>DAY ONE IS NOT A DAILY STAMP. Enrollment mints <see cref="EnrollPunches"/> holes and
    /// that grant IS day one's punch, so a card enrolled TODAY with no dates yet is skipped —
    /// the guard for the ordering where <c>enrollment-done</c> lands before the class's own
    /// attendance credit. The other ordering (the real one: the shell posts
    /// <c>enrollment-done</c> after the ceremony, so this stamp lands first) is handled at the
    /// other end, by <see cref="Enroll"/> superseding it. Day one can never net four.</para>
    ///
    /// <para>AN S DAY IS WORTH TWO (owner ruling 2026-08-23). The grade is known only at the
    /// <c>class-ended</c> frame, so the HOST decides it here and the page is merely told: the date
    /// also lands in <c>sDates</c> and the mint reports <see cref="SPunches"/>. The FIRST graded
    /// finish of the day is the one that decides — a later run the same day is a retake, the date
    /// guard below refuses it, and it can never upgrade a day that already stamped at one (trap
    /// 23's shape: a retake mints nothing, whatever it grades).</para>
    /// </summary>
    /// <param name="gradedS">Did this finish grade S? Only ever acted on when the day is actually
    /// about to stamp — a retake never gets past the date guard.</param>
    public static PunchMint Stamp(JObject cards, string gameKey, string localDate, bool gradedS = false)
    {
        var card = Card(cards, gameKey);
        bool wasComplete = (bool?)card[CompleteField] ?? false;

        if (wasComplete) return new PunchMint(false, 0, false, card);

        var dates = (JArray)card[DatesField]!;
        if (!IsDate(localDate)) return new PunchMint(false, 0, false, card);
        if (dates.Any(d => string.Equals((string?)d, localDate, StringComparison.Ordinal)))
            return new PunchMint(false, 0, false, card);

        // The enrollment punch is day one's punch (PUNCHCARD §2.1). Keyed on the DATE rather
        // than on `dates` being empty: the enrollment day is folded out of `dates` (see
        // Normalize), so a stamp for that day can never be worth a hole no matter how many later
        // days sit on the card, and minting one would only thud at the shell for nothing.
        if (string.Equals((string?)card[EnrolledAtField], localDate, StringComparison.Ordinal))
        {
            return new PunchMint(false, 0, false, card);
        }

        int before = (int?)card[PunchesField] ?? 0;
        dates.Add(localDate);
        if (gradedS) ((JArray)card[SDatesField]!).Add(localDate);
        Normalize(card, localDate);
        // The COUNT is MEASURED, never assumed. The cap is what decides whether an S day's second
        // hole actually existed: a card sitting on nine holes that grades S gains one, not two, and
        // the ceremony is told one so it does not animate a hole nobody owns.
        int after = (int?)card[PunchesField] ?? before;
        return new PunchMint(true, Math.Max(0, after - before),
            !wasComplete && (bool?)card[CompleteField] == true, card);
    }

    /// <summary>
    /// ENROLLMENT: the first-run mint, <see cref="EnrollPunches"/> holes at once — the
    /// tutorial-completion punch, the "on the house" punch and the sign-on punch (PUNCHCARD §1/§4,
    /// owner ruling 2026-08-23). Idempotent on <c>enrolledAt</c>, so a repeated or replayed
    /// <c>enrollment-done</c> frame is a no-op.
    ///
    /// <para>IT SUPERSEDES TODAY'S DAILY STAMP. The shell posts <c>enrollment-done</c> at the end
    /// of the enrollment ceremony, which runs AFTER the <c>class-ended</c> frame that already
    /// credited attendance and stamped — so today's date is normally sitting in <c>dates</c> (and,
    /// on an S first night, in <c>sDates</c> as well) when we get here, and keeping it would make
    /// day one worth four or five. It is removed from BOTH lists: the enrollment grant replaces
    /// it, exactly, and the total is three.</para>
    ///
    /// <para>Superseding rather than suppressing is also what keeps a host running ahead of the
    /// shell honest. Until the shell learns to send <c>enrollment-done</c> at all, cards still
    /// accrue ordinary daily stamps and nothing is lost; the first enrollment frame to arrive
    /// then folds that day in instead of double-counting it.</para>
    /// </summary>
    public static PunchMint Enroll(JObject cards, string gameKey, string localDate)
    {
        var card = Card(cards, gameKey);
        bool wasComplete = (bool?)card[CompleteField] ?? false;

        if (card[EnrolledAtField] is JValue { Type: JTokenType.String })
            return new PunchMint(false, 0, false, card);
        if (!IsDate(localDate)) return new PunchMint(false, 0, false, card);

        int before = (int?)card[PunchesField] ?? 0;
        foreach (var field in new[] { DatesField, SDatesField })
        {
            var list = (JArray)card[field]!;
            foreach (var stamp in list
                         .Where(d => string.Equals((string?)d, localDate, StringComparison.Ordinal))
                         .ToList())
            {
                stamp.Remove();
            }
        }

        card[EnrolledAtField] = localDate;
        card[HouseField] = true;
        Normalize(card, localDate);
        int after = (int?)card[PunchesField] ?? before;
        return new PunchMint(true, Math.Max(0, after - before),
            !wasComplete && (bool?)card[CompleteField] == true, card);
    }

    /// <summary>The card for one game, created (normalized and empty) when absent. A live handle
    /// into <paramref name="cards"/> — the caller is already holding the store's lock.</summary>
    public static JObject Card(JObject cards, string gameKey)
    {
        if (cards[gameKey] is not JObject card)
        {
            card = new JObject();
            cards[gameKey] = card;
        }
        Normalize(card, null);
        return card;
    }

    /// <summary>Game keys whose card is complete — what the shell turns into "this room offers
    /// Begin every night" (PUNCHCARD §2.3).</summary>
    public static IReadOnlyList<string> UnlockedKeys(JObject? cards)
    {
        if (cards == null) return Array.Empty<string>();
        return cards.Properties()
            .Where(p => p.Value is JObject c && (bool?)c[CompleteField] == true)
            .Select(p => p.Name)
            .ToList();
    }

    /// <summary>One date list, cleaned: non-dates dropped, duplicates collapsed, sorted
    /// (yyyy-MM-dd sorts chronologically under ordinal compare). Shared by <c>dates</c> and
    /// <c>sDates</c> so the two can never be cleaned by two slightly different rules.</summary>
    private static List<string> CleanDates(JToken? token)
    {
        var clean = new List<string>();
        if (token is JArray raw)
        {
            foreach (var d in raw)
            {
                var s = (d as JValue)?.Value as string;
                if (s != null && IsDate(s) && !clean.Contains(s, StringComparer.Ordinal)) clean.Add(s);
            }
        }
        clean.Sort(StringComparer.Ordinal);
        return clean;
    }

    /// <summary>
    /// SELF-HEALING. Re-derives every field from the THREE that are actually earned
    /// (<c>enrolledAt</c>, <c>dates</c> and <c>sDates</c>): junk dates are dropped, duplicates
    /// collapsed, the lists sorted (yyyy-MM-dd sorts chronologically under ordinal compare) and
    /// capped, <c>sDates</c> intersected with <c>dates</c>, <c>house</c> is only real once
    /// enrolled, and <c>punches</c> / <c>complete</c> are counted fresh. A blob that arrives
    /// wrong — hand-edited, half-merged, or restored from a server mirror — is corrected on the
    /// next touch instead of being trusted.
    ///
    /// <para>THE FORMULA (see the type header): <c>punches = min(Holes, (enrolled ? EnrollPunches
    /// : 0) + dates.Count + sDates.Count)</c>. <c>core/store.js punchCard()</c> is the same line
    /// in JavaScript and the two must never drift.</para>
    /// </summary>
    /// <param name="onUnlock">The local date to stamp as <c>unlockedAt</c> when the card
    /// completes and has no unlock date yet. Null when merely reading a card into shape.</param>
    private static void Normalize(JObject card, string? onUnlock)
    {
        var clean = CleanDates(card[DatesField]);
        if (clean.Count > Holes) clean = clean.Take(Holes).ToList();

        var enrolled = card[EnrolledAtField] is JValue { Type: JTokenType.String } ev
                       && IsDate((string?)ev.Value)
            ? (string?)ev.Value
            : null;
        // THE ENROLLMENT DAY IS NOT A DAILY STAMP. `enrolledAt` is already worth two holes, so
        // that date sitting in `dates` as well would read as three for day one. Enroll() folds it
        // out at the mint; folding it out HERE too is what makes a card arriving from anywhere
        // else - the mirror, a hand edit, a half-merge - land on the total the server derived it
        // as (arcademy-cards-api.md: "the enrollment day is folded out of dates").
        if (enrolled != null) clean.Remove(enrolled);

        // THE S DAYS. A subset of `dates`, never a list in its own right: intersected here so an
        // sDate for a day that never stamped (a hand edit, a half-merge, a mirror that carried the
        // S list but not the day) is worth exactly nothing. A card heals DOWN, never up.
        var byDate = new HashSet<string>(clean, StringComparer.Ordinal);
        var sClean = CleanDates(card[SDatesField]).Where(byDate.Contains).ToList();

        // The house punch is enrollment's second hole: granted WITH the enrollment, never apart
        // from it, so it is derived rather than believed - the same identity the mirror uses
        // (`house = enrolledAt != null`), which is what keeps both ends counting a restored card
        // the same way.
        bool house = enrolled != null;

        // THE FORMULA. Mirrored character for character by core/store.js punchCard(); the page
        // re-derives every total it draws, so a drift here draws a hole the host does not hold.
        // An OLD blob (enrolled under the two-punch rule) is promoted to three by this line alone
        // — enrolledAt is the only marker there is, and paying it the current rate is both the
        // simplest reading and the generous one. It cannot overflow: the total is capped.
        int punches = Math.Min(Holes,
            (enrolled != null ? EnrollPunches : 0) + clean.Count + sClean.Count);
        bool complete = punches >= Holes;

        var unlockedAt = card[UnlockedAtField] is JValue { Type: JTokenType.String } uv
                         && IsDate((string?)uv.Value)
            ? (string?)uv.Value
            : null;
        if (!complete) unlockedAt = null;
        else unlockedAt ??= onUnlock;

        card[DatesField] = new JArray(clean);
        card[SDatesField] = new JArray(sClean);
        card[EnrolledAtField] = enrolled == null ? JValue.CreateNull() : new JValue(enrolled);
        card[HouseField] = house;
        card[PunchesField] = punches;
        card[CompleteField] = complete;
        card[UnlockedAtField] = unlockedAt == null ? JValue.CreateNull() : new JValue(unlockedAt);
    }

    // ======================= the server mirror (PUNCHCARD §5) =======================
    //
    // The host is the authority and the mirror is best-effort restore; everything below is the
    // pure half of that traffic, kept here (rather than in the sync service) so it is testable
    // without WPF, App or a network - the same reason the mint math lives here.

    /// <summary>
    /// Fold a merged reply from the mirror into the local cards, MONOTONICALLY: dates are unioned,
    /// <c>enrolledAt</c> and <c>unlockedAt</c> are earliest-wins, and nothing local is ever
    /// dropped. A card the reply does not mention is left exactly as it is (the same promise the
    /// server makes about a push that does not mention one), and a card only the reply knows is
    /// CREATED - that is the whole point on a fresh install.
    ///
    /// <para>Every touched card goes back out through <see cref="Normalize"/>, so the derived four
    /// (<c>punches</c>/<c>house</c>/<c>complete</c>/<c>unlockedAt</c>) are re-counted here from the
    /// only three earned fields exactly as the server re-derives them on the way in. A reply is
    /// therefore never trusted for a total - only for the dates and the enrollment behind it - so
    /// a mirror that had somehow been talked into nonsense cannot spend it here.</para>
    /// </summary>
    /// <returns>The game keys this reply actually changed - empty when the mirror had nothing the
    /// host did not already know, which is the common case and the one that must not cost a
    /// save.</returns>
    public static IReadOnlyList<string> ApplyServer(JObject cards, JObject? serverCards)
    {
        var changed = new List<string>();
        if (serverCards == null) return changed;

        foreach (var prop in serverCards.Properties())
        {
            if (prop.Value is not JObject remote) continue;
            var key = prop.Name;
            if (string.IsNullOrWhiteSpace(key)) continue;

            // Snapshot BEFORE Card(), which creates and normalizes: a card that merely heals is
            // still a change worth saving, and one that does nothing at all must not be.
            var before = cards[key]?.ToString(Formatting.None);
            var card = Card(cards, key);

            // Both date lists are unioned the same way. sDates is a subset of dates by
            // construction, and Normalize re-intersects them below, so a remote S day whose stamp
            // this machine never saw arrives on the DATES pass and is only then worth its second
            // hole - the union can add a hole, never invent one.
            foreach (var field in new[] { DatesField, SDatesField })
            {
                var local = (JArray)card[field]!;
                var have = new HashSet<string>(
                    local.Select(d => (string?)d ?? string.Empty), StringComparer.Ordinal);
                if (remote[field] is not JArray remoteList) continue;
                foreach (var d in remoteList)
                {
                    var s = (d as JValue)?.Value as string;
                    if (s != null && IsDate(s) && have.Add(s)) local.Add(s);
                }
            }

            // Earliest enrollment wins, and a missing one can never clear the one we hold: the
            // mirror is colder than this machine every time until the first push lands.
            var remoteEnrolled = DateOf(remote[EnrolledAtField]);
            var localEnrolled = DateOf(card[EnrolledAtField]);
            if (remoteEnrolled != null
                && (localEnrolled == null || string.CompareOrdinal(remoteEnrolled, localEnrolled) < 0))
            {
                card[EnrolledAtField] = remoteEnrolled;
            }

            // unlockedAt is the one derived field CARRIED rather than recomputed - the day a card
            // filled is witnessed once, and not necessarily on this machine. Earliest wins;
            // Normalize still drops it if the card is not actually complete.
            var remoteUnlocked = DateOf(remote[UnlockedAtField]);
            var localUnlocked = DateOf(card[UnlockedAtField]);
            if (remoteUnlocked != null
                && (localUnlocked == null || string.CompareOrdinal(remoteUnlocked, localUnlocked) < 0))
            {
                card[UnlockedAtField] = remoteUnlocked;
            }

            Normalize(card, null);
            if (!string.Equals(before, card.ToString(Formatting.None), StringComparison.Ordinal))
                changed.Add(key);
        }

        return changed;
    }

    /// <summary>
    /// The push payload: a normalized clone of the cards worth mirroring. A card with nothing
    /// EARNED on it (no enrollment, no dates) is left out - it carries nothing the merge could
    /// use, and a push of only those is a wasted request.
    /// </summary>
    public static JObject Export(JObject? cards)
    {
        var payload = new JObject();
        if (cards == null) return payload;

        foreach (var prop in cards.Properties().ToList())
        {
            if (prop.Value is not JObject) continue;
            var card = (JObject)Card(cards, prop.Name).DeepClone();
            bool earned = card[EnrolledAtField] is JValue { Type: JTokenType.String }
                          || (card[DatesField] as JArray)?.Count > 0;
            if (earned) payload[prop.Name] = card;
        }
        return payload;
    }

    /// <summary>
    /// Does this machine hold anything the mirror does not? True for a date or an earlier
    /// enrollment the reply did not carry - the launch-time answer to "did a push fail while we
    /// were offline", asked of the GET we already made rather than of a flag a crash could lose.
    /// </summary>
    public static bool HasUnmirrored(JObject? cards, JObject? serverCards)
    {
        if (cards == null) return false;
        foreach (var prop in cards.Properties())
        {
            if (prop.Value is not JObject local) continue;
            var remote = serverCards?[prop.Name] as JObject;

            var localEnrolled = DateOf(local[EnrolledAtField]);
            if (localEnrolled != null)
            {
                var remoteEnrolled = DateOf(remote?[EnrolledAtField]);
                if (remoteEnrolled == null
                    || string.CompareOrdinal(localEnrolled, remoteEnrolled) < 0) return true;
            }

            // BOTH lists. An S day whose date the mirror already holds but whose S the mirror does
            // not is a hole this machine owns and the mirror cannot pay back, so it counts as
            // unmirrored exactly like a missing date would.
            foreach (var field in new[] { DatesField, SDatesField })
            {
                if (local[field] is not JArray localDates || localDates.Count == 0) continue;
                var mirrored = new HashSet<string>(StringComparer.Ordinal);
                if (remote?[field] is JArray remoteDates)
                {
                    foreach (var d in remoteDates)
                    {
                        if ((d as JValue)?.Value is string s) mirrored.Add(s);
                    }
                }
                foreach (var d in localDates)
                {
                    var s = (d as JValue)?.Value as string;
                    if (s != null && IsDate(s) && !mirrored.Contains(s)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>A token read as a calendar date, or null for anything that is not one.</summary>
    private static string? DateOf(JToken? t) =>
        t is JValue { Type: JTokenType.String } v && IsDate((string?)v.Value) ? (string?)v.Value : null;

    private static bool IsDate(string? s) =>
        !string.IsNullOrWhiteSpace(s)
        && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);
}
