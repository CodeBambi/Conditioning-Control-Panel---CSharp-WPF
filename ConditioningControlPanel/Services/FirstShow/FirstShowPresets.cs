using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Services.FirstShow;

internal static class FirstShowPresets
{
    internal static readonly string[] All = { "EroticHypnosis", "HypnoGoneWild", "sissyhypno", "bimbofication", "cosplay", "censored" };
    internal static string[] Sources(string preset) => preset == "censored"
        ? FypOnlineCoordinator.Catalog.First(n => n.Id == "censored").Subs
        : new[] { preset };
}
