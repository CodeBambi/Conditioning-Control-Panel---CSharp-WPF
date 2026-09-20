using System;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// The second-instance handoff for launcher surfaces. A second process that names a surface on
/// its command line (a game shortcut, the launcher shortcut, the panel shortcut) writes one line
/// through the existing "Open with CCP" handoff file and the primary routes it. Pure string
/// mapping in both directions so the whole table is a unit test.
///
/// Payloads: <c>panel</c>, <c>launcher</c>, <c>game:&lt;id&gt;</c>. Precedence matches
/// <see cref="LauncherBoot.Decide"/>: <c>--panel</c> wins, then <c>--game</c>, then the launcher.
/// </summary>
public static class LauncherHandoff
{
    /// <summary>The handoff-file action that carries a surface payload.</summary>
    public const string Action = "surface";

    public const string PanelKind = "panel";
    public const string LauncherKind = "launcher";
    public const string GameKind = "game";

    /// <summary>The payload for these args, or null when they name no surface.</summary>
    public static string? Encode(string[]? args)
    {
        if (!LauncherBoot.NamesASurface(args)) return null;
        if (LauncherBoot.Has(args, LauncherBoot.PanelFlag)) return PanelKind;
        var game = LauncherBoot.GameArg(args);
        if (game != null) return GameKind + ":" + game;
        return LauncherKind;
    }

    /// <summary>
    /// The surface a payload asks for. Anything unreadable decodes to the launcher: the other
    /// process asked for a surface, and the tiles are the harmless answer when the exact one is
    /// lost.
    /// </summary>
    public static (string kind, string? id) Decode(string? payload)
    {
        var p = (payload ?? string.Empty).Trim();
        if (string.Equals(p, PanelKind, StringComparison.OrdinalIgnoreCase)) return (PanelKind, null);
        if (p.StartsWith(GameKind + ":", StringComparison.OrdinalIgnoreCase))
        {
            var id = p.Substring(GameKind.Length + 1).Trim().ToLowerInvariant();
            if (id.Length > 0) return (GameKind, id);
        }
        return (LauncherKind, null);
    }
}
