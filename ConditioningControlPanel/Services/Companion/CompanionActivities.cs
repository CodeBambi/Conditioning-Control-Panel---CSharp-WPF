using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel.Services.Companion;

internal sealed record CompanionActivity(string Id, string Label, string Description,
    Func<bool> CheckAccess, Func<bool> Open)
{
    internal bool Allowed { get { try { return CheckAccess(); } catch { return false; } } }
    internal bool TryOpen() => Allowed && Open();
}

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
        AddPage("studio", "Effects options: flashes, bubbles, spiral, video and audio. Opening does not start effects.");
        AddPage("presets", "Browse and configure session presets. Opening does not start a session.");
        AddPage("quests", "Review current daily and weekly quests. Do not invent quest progress or rewards.");
        AddPage("assets", "Manage the user's media library. No claim about its contents without supplied context.");
        return result;

        void AddPage(string tab, string description)
        {
            var entry = SettingsPaletteIndex.All.First(e => e.Id == "tab." + tab);
            result.Add(new("page." + tab, entry.Label, description,
                () => entry.Available && App.MainWindowRef != null,
                () => { if (App.MainWindowRef is not { } window) return false; window.OpenDestination(entry); return true; }));
        }
    }

    internal static bool CanOffer(LauncherEntry game, bool needsAccount, bool audioOnly) =>
        game.Available && !game.Locked && game.Revealed && !needsAccount && !audioOnly;

    internal static CompanionActivity? Find(string id) => Current().FirstOrDefault(a => a.Id == id);
    internal static string ButtonLabel(string label) => Loc.GetF("companion_v2_open_activity", label);
}
