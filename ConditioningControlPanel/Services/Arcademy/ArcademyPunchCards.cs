using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// <c>{punches:0..10, dates:["yyyy-MM-dd"], enrolledAt:"yyyy-MM-dd"|null, house:bool,
/// complete:bool, unlockedAt:"yyyy-MM-dd"|null}</c>. <c>punches</c> is denormalized for the page
/// but RECOMPUTED on every touch (<see cref="Normalize"/>), so a hand-edited or half-merged blob
/// heals itself rather than carrying a wrong total forever.</para>
///
/// <para>LOCAL dates throughout — never the UTC seed. Regression #978 / CLAUDE.md trap 8: the UTC
/// day seeds tonight's content, local midnight rolls every daily gate the player feels.</para>
/// </summary>
internal static class ArcademyPunchCards
{
    /// <summary>Holes on a card. Reaching it completes the semester assignment and unlocks the
    /// room permanently (PUNCHCARD §1).</summary>
    public const int Holes = 10;

    /// <summary>Field names, spelled once so the store, the host and the page cannot drift.</summary>
    public const string PunchesField = "punches";
    public const string DatesField = "dates";
    public const string EnrolledAtField = "enrolledAt";
    public const string HouseField = "house";
    public const string CompleteField = "complete";
    public const string UnlockedAtField = "unlockedAt";

    /// <summary>The outcome of one mint attempt. <paramref name="Minted"/> is false for every
    /// idempotent no-op (same day twice, already enrolled, card already full) — the caller uses it
    /// to decide whether anything needs saving or broadcasting.</summary>
    /// <param name="Minted">Did the card actually change?</param>
    /// <param name="JustUnlocked">Did THIS mint carry the card over the tenth hole?</param>
    /// <param name="Card">The card after the attempt (a live handle into the blob).</param>
    internal readonly record struct PunchMint(bool Minted, bool JustUnlocked, JObject Card);

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
    /// <para>DAY ONE IS NOT A DAILY STAMP. Enrollment mints two holes (tutorial + on the house)
    /// and that pair IS day one's punch, so a card enrolled TODAY with no dates yet is skipped —
    /// the guard for the ordering where <c>enrollment-done</c> lands before the class's own
    /// attendance credit. The other ordering (the real one: the shell posts
    /// <c>enrollment-done</c> after the ceremony, so this stamp lands first) is handled at the
    /// other end, by <see cref="Enroll"/> superseding it. Day one can never net three.</para>
    /// </summary>
    public static PunchMint Stamp(JObject cards, string gameKey, string localDate)
    {
        var card = Card(cards, gameKey);
        bool wasComplete = (bool?)card[CompleteField] ?? false;

        if (wasComplete) return new PunchMint(false, false, card);

        var dates = (JArray)card[DatesField]!;
        if (!IsDate(localDate)) return new PunchMint(false, false, card);
        if (dates.Any(d => string.Equals((string?)d, localDate, StringComparison.Ordinal)))
            return new PunchMint(false, false, card);

        // The enrollment punch is day one's punch (PUNCHCARD §2.1).
        if (dates.Count == 0
            && string.Equals((string?)card[EnrolledAtField], localDate, StringComparison.Ordinal))
        {
            return new PunchMint(false, false, card);
        }

        dates.Add(localDate);
        Normalize(card, localDate);
        return new PunchMint(true, !wasComplete && (bool?)card[CompleteField] == true, card);
    }

    /// <summary>
    /// ENROLLMENT: the first-run mint, two holes at once — the tutorial-completion punch and the
    /// "on the house" punch (PUNCHCARD §1/§4). Idempotent on <c>enrolledAt</c>, so a repeated or
    /// replayed <c>enrollment-done</c> frame is a no-op.
    ///
    /// <para>IT SUPERSEDES TODAY'S DAILY STAMP. The shell posts <c>enrollment-done</c> at the end
    /// of the enrollment ceremony, which runs AFTER the <c>class-ended</c> frame that already
    /// credited attendance and stamped — so today's date is normally sitting in <c>dates</c> when
    /// we get here, and keeping it would make day one worth three. It is removed: the enrollment
    /// pair replaces it, exactly, and the total is two.</para>
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
            return new PunchMint(false, false, card);
        if (!IsDate(localDate)) return new PunchMint(false, false, card);

        var dates = (JArray)card[DatesField]!;
        foreach (var stamp in dates.Where(d => string.Equals((string?)d, localDate, StringComparison.Ordinal)).ToList())
        {
            stamp.Remove();
        }

        card[EnrolledAtField] = localDate;
        card[HouseField] = true;
        Normalize(card, localDate);
        return new PunchMint(true, !wasComplete && (bool?)card[CompleteField] == true, card);
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

    /// <summary>
    /// SELF-HEALING. Re-derives every field from the two that are actually earned
    /// (<c>enrolledAt</c> and <c>dates</c>): junk dates are dropped, duplicates collapsed, the
    /// list sorted (yyyy-MM-dd sorts chronologically under ordinal compare) and capped,
    /// <c>house</c> is only real once enrolled, and <c>punches</c> / <c>complete</c> are counted
    /// fresh. A blob that arrives wrong — hand-edited, half-merged, or restored from a server
    /// mirror — is corrected on the next touch instead of being trusted.
    /// </summary>
    /// <param name="onUnlock">The local date to stamp as <c>unlockedAt</c> when the card
    /// completes and has no unlock date yet. Null when merely reading a card into shape.</param>
    private static void Normalize(JObject card, string? onUnlock)
    {
        var clean = new List<string>();
        if (card[DatesField] is JArray raw)
        {
            foreach (var d in raw)
            {
                var s = (d as JValue)?.Value as string;
                if (s != null && IsDate(s) && !clean.Contains(s, StringComparer.Ordinal)) clean.Add(s);
            }
        }
        clean.Sort(StringComparer.Ordinal);
        if (clean.Count > Holes) clean = clean.Take(Holes).ToList();

        var enrolled = card[EnrolledAtField] is JValue { Type: JTokenType.String } ev
                       && IsDate((string?)ev.Value)
            ? (string?)ev.Value
            : null;
        // The house punch is enrollment's second hole and cannot exist without it.
        bool house = enrolled != null && ((bool?)card[HouseField] ?? false);

        int punches = Math.Min(Holes, (enrolled != null ? 1 : 0) + (house ? 1 : 0) + clean.Count);
        bool complete = punches >= Holes;

        var unlockedAt = card[UnlockedAtField] is JValue { Type: JTokenType.String } uv
                         && IsDate((string?)uv.Value)
            ? (string?)uv.Value
            : null;
        if (!complete) unlockedAt = null;
        else unlockedAt ??= onUnlock;

        card[DatesField] = new JArray(clean);
        card[EnrolledAtField] = enrolled == null ? JValue.CreateNull() : new JValue(enrolled);
        card[HouseField] = house;
        card[PunchesField] = punches;
        card[CompleteField] = complete;
        card[UnlockedAtField] = unlockedAt == null ? JValue.CreateNull() : new JValue(unlockedAt);
    }

    private static bool IsDate(string? s) =>
        !string.IsNullOrWhiteSpace(s)
        && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);
}
