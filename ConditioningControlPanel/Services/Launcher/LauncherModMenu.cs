using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>One row of the launcher's mod dropdown.</summary>
public sealed record LauncherModRow(string Id, string Name, bool IsBuiltIn);

/// <summary>
/// The pure half of the launcher's title-bar mod switcher: which rows the dropdown shows and in
/// what order. Same order as the panel's top-bar combo (MainWindow.InitializeModSelector): the
/// stock mods in their canonical order, then user mods alphabetically. Activation itself is not
/// here; the window hands the chosen id to MainWindow so the panel's one switching path
/// (ActivateMod + ApplyActiveModChange) stays the only one.
/// </summary>
public static class LauncherModMenu
{
    /// <summary>The stock mods, in the order the panel lists them.</summary>
    public static readonly string[] StockOrder =
    {
        BuiltInMods.CCPDefaultId,
        BuiltInMods.BambiSleepId,
        BuiltInMods.SissyHypnoId,
        BuiltInMods.DronificationId,
        BuiltInMods.LockedId,
        BuiltInMods.InfectionControlId,
    };

    public static List<LauncherModRow> Order(IEnumerable<LauncherModRow>? mods)
    {
        var rows = (mods ?? Enumerable.Empty<LauncherModRow>()).ToList();
        var ordered = new List<LauncherModRow>();

        foreach (var id in StockOrder)
        {
            var row = rows.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (row != null) ordered.Add(row);
        }

        ordered.AddRange(rows
            .Where(r => !r.IsBuiltIn && !ordered.Contains(r))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase));

        // A built-in the stock list does not know (a future one) still gets a row, after the rest.
        ordered.AddRange(rows.Where(r => !ordered.Contains(r)));
        return ordered;
    }

    /// <summary>The pill's text: "Mod: name". An empty name reads as the fallback.</summary>
    public static string Label(string format, string? name, string fallback)
    {
        var shown = string.IsNullOrWhiteSpace(name) ? fallback : name!.Trim();
        try { return string.Format(format, shown); }
        catch (FormatException) { return shown; }
    }
}
