using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>The panel pages the companion may suggest (the page half of WPF CompanionActivities.Current).
/// Each head supplies whether its panel is up and how it opens a palette destination.</summary>
internal static class CompanionPages
{
    private static readonly (string Tab, string Description)[] Rows =
    {
        ("studio", "Effects options: flashes, bubbles, spiral, video and audio. Opening does not start effects."),
        ("presets", "Browse and configure session presets. Opening does not start a session."),
        ("quests", "Review current daily and weekly quests. Do not invent quest progress or rewards."),
        ("assets", "Manage the user's media library. No claim about its contents without supplied context."),
    };

    public static IEnumerable<CompanionActivity> Current(Func<bool> panelUp, Func<SettingsPaletteEntry, bool> open)
    {
        foreach (var (tab, description) in Rows)
        {
            var entry = SettingsPaletteIndex.All.First(e => e.Id == "tab." + tab);
            yield return new("page." + tab, entry.Label, description,
                () => entry.Available && !PageLocked(tab) && panelUp(),
                () => open(entry));
        }
    }

    /// <summary>A launcher game is suggested only when its tile would really open it: present, unlocked,
    /// revealed, nobody needs to sign in, and not an audio-only session.</summary>
    public static bool CanOfferGame(bool available, bool locked, bool revealed, bool needsAccount, bool audioOnly) =>
        available && !locked && revealed && !needsAccount && !audioOnly;

    /// <summary>A page behind a tier or pass (an ExclusiveFeature roster row) is never offered while it is shut.
    /// Same three questions the rail's premium star asks: roster row, gate state, daily free rotation.</summary>
    public static bool PageLocked(string tab)
    {
        try
        {
            var feature = Models.ExclusiveFeature.All.FirstOrDefault(f => string.Equals(f.Key, tab, StringComparison.Ordinal));
            if (feature == null || feature.GateState() != Models.ExclusiveGateState.Locked) return false;
            return !(feature.DailyFreeKey != null && CoreEntitlement.IsFreeToday(feature.DailyFreeKey));
        }
        catch { return true; }
    }
}
