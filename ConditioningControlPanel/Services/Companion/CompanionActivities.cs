using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Small, current capability list. Launcher delegates own tier, passes and prize unlocks.</summary>
internal static class CompanionActivities
{
    internal static IReadOnlyList<CompanionActivity> Current()
    {
        var result = new List<CompanionActivity>();
        foreach (var game in LauncherCatalogue.Games)
            result.Add(new("game." + game.Id, game.Title, game.Blurb,
                () => CanOffer(game, game.NeedsAccount, App.Settings?.Current?.AudioOnlySession == true),
                () => LauncherHost.LaunchGame(game.Id)));
        result.AddRange(CompanionPages.Current(() => App.MainWindowRef != null,
            entry => { if (App.MainWindowRef is not { } window) return false; window.OpenDestination(entry); return true; }));
        return result;
    }

    internal static bool CanOffer(LauncherEntry game, bool needsAccount, bool audioOnly) =>
        CompanionPages.CanOfferGame(game.Available, game.Locked, game.Revealed, needsAccount, audioOnly);

    internal static CompanionActivity? Find(string id) => Current().FirstOrDefault(a => a.Id == id);
    internal static string ButtonLabel(string label) => Loc.GetF("companion_v2_open_activity", label);
}
