using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The rank spine — the single source of truth for depth ranks. ALL progression gates
/// read <see cref="ChaosMeta.RankIndex"/> (or RunsCompleted via <see cref="For"/>); no
/// hardcoded run thresholds anywhere else.
/// </summary>
public enum ChaosRank
{
    Curious   = 0,
    Tempted   = 1,
    Slipping  = 2,
    Entranced = 3,
    Devoted   = 4,
    Claimed   = 5,
}

public static class ChaosRanks
{
    // ---- thresholds (lifetime completed descents) ----
    public static readonly int[] Thresholds = { 0, 3, 10, 25, 50, 100 };

    public static ChaosRank For(int runsCompleted)
    {
        var r = ChaosRank.Curious;
        for (int i = Thresholds.Length - 1; i >= 0; i--)
            if (runsCompleted >= Thresholds[i]) { r = (ChaosRank)i; break; }
        return r;
    }

    /// <summary>Lowercase rank word — the recap card renders this bare and huge.</summary>
    public static string NameLower(ChaosRank r) => r switch
    {
        ChaosRank.Tempted   => "tempted",
        ChaosRank.Slipping  => "slipping",
        ChaosRank.Entranced => "entranced",
        ChaosRank.Devoted   => "devoted",
        ChaosRank.Claimed   => "claimed",
        _                   => "curious",
    };

    /// <summary>Capitalized rank word for the dollhouse top bar (existing display).</summary>
    public static string Name(ChaosRank r) => r switch
    {
        ChaosRank.Tempted   => "Tempted",
        ChaosRank.Slipping  => "Slipping",
        ChaosRank.Entranced => "Entranced",
        ChaosRank.Devoted   => "Devoted",
        ChaosRank.Claimed   => "Claimed",
        _                   => "Curious",
    };

    /// <summary>[LOCKED] generic tooltip for anything visible but above the player's rank. Ships verbatim.</summary>
    public const string RankLockedTip = "she'll sell this to someone deeper.";

    /// <summary>The hover SPECIFICS for any rank gate: exact rank, exact descent count, live
    /// progress. Rides under the mystery lines so locks stay studyable without losing voice.</summary>
    /// <paramref name="have"/> is the live save's RunsCompleted (the WPF head passes ChaosMeta.State's).
    public static string RankSpecifics(ChaosRank needed, int have)
    {
        int need = Thresholds[Math.Clamp((int)needed, 0, Thresholds.Length - 1)];
        return $"unlocks at {Name(needed)}: {need} descents finished. you've finished {have}.";
    }

    /// <summary>[LOCKED] tooltip for a capstone (final) boon level before Devoted. Ships verbatim.</summary>
    public const string CapstoneLockedTip = "the last stitch is hers to give. she gives it to the devoted.";

    /// <summary>[LOCKED] one line under the bare rank word on the rank card. Ships verbatim.</summary>
    public static string Line(ChaosRank r) => r switch
    {
        ChaosRank.Tempted   => "tempted. three times down. you can stop calling it curiosity.",
        ChaosRank.Slipping  => "slipping. the climb out takes longer every time. you noticed. you came anyway.",
        ChaosRank.Entranced => "entranced. you don't fall anymore. you arrive.",
        ChaosRank.Devoted   => "devoted. the dollhouse keeps a room warm for you now. it always knew it would.",
        ChaosRank.Claimed   => "claimed. it stopped counting your visits a long time ago. so did you.",
        _                   => "",
    };
}

/// <summary>
/// Currency / stat glyphs. ✦ belongs to DROPS exclusively; gold and xp each get their own
/// mark. Never double-book a glyph in user-facing text.
/// </summary>
public static class ChaosGlyphs
{
    public const string Drops = "✦";
    public const string Gold  = "🪙";   // 🐇 stays the mode's brand mark, never a currency
    public const string Xp    = "🕰";
}
