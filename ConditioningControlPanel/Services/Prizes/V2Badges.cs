using System;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// Which Back Room v2 families a surface fronts, so a caller can ask about one without naming
/// the grant ids itself.
/// </summary>
[Flags]
public enum V2Family
{
    None = 0,
    /// <summary>The flash styles: Drift &amp; Bounce, Pendulum, and Jackpot Remix (a flash prize too).</summary>
    Flash = 1,
    /// <summary>The ambient bubble motions: Rain and Spiral In.</summary>
    Bubble = 2,
}

/// <summary>
/// THE ONE PLACE that decides whether a surface wears the champagne "v2" pill.
///
/// <para>Three surfaces ask the same two questions and used to answer them separately: the
/// dashboard mosaic's Flashes tile (SettingsTabView.RefreshV2Badges), its Bubble Pop tile
/// (MainWindow.Presets.RefreshMosaicTierBadges) and, since this lane, the side rail's chips.
/// Three copies of "which grants count as flashes v2" is three chances for the wall and the rail
/// to disagree about one account, so the rule lives here and every surface reads it.</para>
///
/// <para>Ownership is <see cref="PrizeGrants"/> and never settings, exactly as the tiles already
/// had it: a synced profile carrying a style the account does not own lights nothing. Each rule
/// comes in two shapes - a PURE one taking the grant booleans, which is what the tests exercise,
/// and a live wrapper that reads the grants. Nothing here touches WPF.</para>
/// </summary>
public static class V2Badges
{
    // ---- pure rules -------------------------------------------------------------------

    /// <summary>Flashes v2: any of the two motion styles, or Jackpot Remix.</summary>
    public static bool FlashRule(bool driftBounce, bool pendulum, bool jackpotRemix)
        => driftBounce || pendulum || jackpotRemix;

    /// <summary>Bubbles v2: either ambient motion.</summary>
    public static bool BubbleRule(bool rain, bool spiralIn) => rain || spiralIn;

    /// <summary>
    /// Which families a Ctrl+K palette destination (a side-rail chip) fronts.
    ///
    /// <para>Flashes and Bubble Pop are RACK MODULES, not palette rows of their own: both tiles
    /// open <c>OpenStudioModule</c> and land on the Studio effects rack, so the one chip a user
    /// can pin for either is the Studio destination - and it is the same room a themed door row
    /// (<c>door.studio</c>) and its tab twin (<c>tab.studio</c>) lead to, which is why both ids
    /// are listed. The day the palette grows a Flashes or a Bubbles row this table gets it its
    /// own line and the chip stops speaking for two features at once.</para>
    /// </summary>
    public static V2Family FamiliesFor(string? paletteId) => paletteId switch
    {
        "door.studio" or "tab.studio" => V2Family.Flash | V2Family.Bubble,
        _ => V2Family.None,
    };

    /// <summary>Pure: would this chip wear the pill with these two answers?</summary>
    public static bool ChipWearsV2Pill(string? paletteId, bool flashOwned, bool bubbleOwned)
    {
        var families = FamiliesFor(paletteId);
        return (families.HasFlag(V2Family.Flash) && flashOwned)
            || (families.HasFlag(V2Family.Bubble) && bubbleOwned);
    }

    // ---- live reads (UI thread, at repaint) -------------------------------------------

    /// <summary>True while the account owns any flashes v2 prize.</summary>
    public static bool FlashOwned() => FlashRule(
        PrizeGrants.IsGranted(PrizeGrants.FlashDriftBounce),
        PrizeGrants.IsGranted(PrizeGrants.FlashPendulum),
        PrizeGrants.IsGranted(PrizeGrants.JackpotRemix));

    /// <summary>True while the account owns any bubbles v2 prize.</summary>
    public static bool BubbleOwned() => BubbleRule(
        PrizeGrants.IsGranted(PrizeGrants.BubbleRain),
        PrizeGrants.IsGranted(PrizeGrants.BubbleSpiralIn));

    /// <summary>True while the chip for this destination should wear the pill.</summary>
    public static bool ChipWearsV2Pill(string? paletteId)
        => ChipWearsV2Pill(paletteId, FlashOwned(), BubbleOwned());
}
