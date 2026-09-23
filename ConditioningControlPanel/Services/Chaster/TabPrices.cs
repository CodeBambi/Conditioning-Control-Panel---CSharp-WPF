using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>Who may switch a price on. Mirrors where the feature itself is gated; a price can
/// never be a way into a feature the account does not have.</summary>
public enum TabPriceGate { Free, Tier1, Tier2, Sparkles }

/// <param name="Seconds">Signed default: positive costs, negative earns back.</param>
/// <param name="PerUnit">The caller multiplies (a broken mantra streak pays per rep in it).</param>
public sealed record TabPrice(string Id, int Seconds, TabPriceGate Gate, bool PerUnit = false);

/// <summary>
/// The price table: every event that can touch the tab, what it costs by default, and which tier
/// owns the feature behind it. EVERY PRICE IS OPT-IN. A row here is only an offer; nothing books
/// until the player has switched that row on, and the ids listed in <see cref="NeverPriced"/>
/// cannot be given a row at all.
///
/// <para>Ids and amounts are the owner's price table (the "Circe's Tab" picker, Sep 21 2026).
/// Rows that need Chaster's Extensions API (curses, the pillory) or a server-side stake
/// (roulette, blackjack, the wheel slice, the pardon, duel pots) are not here yet: they have no
/// fixed price to list.</para>
/// </summary>
public static class TabPrices
{
    public static readonly IReadOnlyList<TabPrice> All = new[]
    {
        // The panel
        // Booked by the service itself on the day the player comes back (CircesMisses), never
        // through Resolve: 5:00 is only the first day's figure, the one the page shows.
        new TabPrice(CircesMisses.EventId, CircesMisses.FirstDaySeconds, TabPriceGate.Free),
        new TabPrice("typo", 15, TabPriceGate.Free, PerUnit: true),
        new TabPrice("lockcard", -30, TabPriceGate.Free),
        new TabPrice("attention", 120, TabPriceGate.Free),
        // About one bubble in ten and one flash in ten wears red; that bubble popped, or that
        // flash seen, is 3:00. The roll and the cue live in NatashasFavourite.
        new TabPrice(NatashasFavourite.EventId, NatashasFavourite.Seconds, TabPriceGate.Free),
        new TabPrice("mantra", 10, TabPriceGate.Free, PerUnit: true),
        new TabPrice("session", -600, TabPriceGate.Free),
        new TabPrice("quest", -300, TabPriceGate.Free),
        new TabPrice("quest_weekly", -1200, TabPriceGate.Free),
        new TabPrice("video", -300, TabPriceGate.Free),
        new TabPrice("levelup", -900, TabPriceGate.Free),
        new TabPrice("program_done", -600, TabPriceGate.Free),
        new TabPrice("program_skipped", 900, TabPriceGate.Free),
        new TabPrice("remote_media", 30, TabPriceGate.Tier1),
        new TabPrice("remote_video", 120, TabPriceGate.Tier1),
        new TabPrice("escape", 180, TabPriceGate.Tier1),
        new TabPrice("watcher", 300, TabPriceGate.Tier1),
        // The Back Room
        new TabPrice("melt", 120, TabPriceGate.Free),
        // The games
        new TabPrice("bubbles", 60, TabPriceGate.Tier2),
        new TabPrice("ball", 20, TabPriceGate.Tier2),
        new TabPrice("wall", -120, TabPriceGate.Tier2),
        new TabPrice("padlock", 30, TabPriceGate.Sparkles),
        new TabPrice("crash", 5, TabPriceGate.Sparkles),
    };

    /// <summary>The way out never costs. These ids are refused even if a settings file names
    /// them, so no preset, mod or hand edit can put a price on leaving.</summary>
    public static readonly IReadOnlySet<string> NeverPriced =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "panic", "emergency_exit", "safeword", "unlink" };

    private static readonly Dictionary<string, TabPrice> ById =
        All.ToDictionary(p => p.Id, StringComparer.Ordinal);

    public static TabPrice? Find(string id) =>
        id != null && ById.TryGetValue(id, out var p) ? p : null;

    /// <summary>The seconds to book for one event, or 0 when it books nothing: unknown id, a
    /// row the player has not switched on, or a way-out id. <paramref name="units"/> only counts
    /// on per-unit rows.</summary>
    public static int Resolve(string id, ISet<string>? enabledIds, int units = 1)
    {
        if (string.IsNullOrEmpty(id) || NeverPriced.Contains(id)) return 0;
        var price = Find(id);
        if (price == null || enabledIds == null || !enabledIds.Contains(id)) return 0;
        var n = price.PerUnit ? Math.Clamp(units, 0, 1000) : 1;
        return (int)Math.Clamp((long)price.Seconds * n, -TabLimits.MaxDailySeconds, TabLimits.MaxDailySeconds);
    }
}
