using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>What asked the launcher to look at its tiles again.</summary>
public enum LauncherTileTrigger
{
    /// <summary>The active mod changed, from any path (launcher pill, panel combo, Mod Manager).</summary>
    ModChanged,

    /// <summary>A prize grant came or went (sync, counter purchase, logout).</summary>
    GrantsChanged,
}

/// <summary>
/// The pure half of the launcher's tile refresh: when an event that can change a tile should
/// redraw the open launcher. A hidden launcher never redraws here, because <c>OnShown</c> builds
/// the tiles from scratch on every show.
///
/// <list type="bullet">
/// <item>A mod switch always redraws: every tile's art, and possibly its name, goes through the
/// mod's resources (ccp-bugs #1292, the cards kept the old mod's art until the next show).</item>
/// <item>A grant change redraws only when a tile's reveal flipped, so a sync that brings the
/// same grants back does not restage the whole grid (ccp-bugs #1305).</item>
/// </list>
/// </summary>
public static class LauncherTileRefresh
{
    /// <param name="trigger">What happened.</param>
    /// <param name="visible">Whether the launcher is on screen.</param>
    /// <param name="revealedAtBuild">Each tile's reveal as it was drawn, by tile id.</param>
    /// <param name="revealedNow">Each available tile's reveal as the catalogue reads it now.</param>
    public static bool ShouldRebuild(
        LauncherTileTrigger trigger,
        bool visible,
        IReadOnlyDictionary<string, bool> revealedAtBuild,
        IEnumerable<KeyValuePair<string, bool>> revealedNow)
    {
        if (!visible) return false;
        if (trigger == LauncherTileTrigger.ModChanged) return true;

        foreach (var (id, revealed) in revealedNow)
        {
            if (!revealedAtBuild.TryGetValue(id, out var was) || was != revealed) return true;
        }
        return false;
    }
}
