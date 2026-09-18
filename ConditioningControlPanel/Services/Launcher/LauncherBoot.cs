using System;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>Which window the process opens with.</summary>
public enum BootSurface
{
    /// <summary>The Conditioning Control Panel, as before the launcher existed.</summary>
    Panel,
    /// <summary>The CC Labs launcher, with the panel tucked in the tray behind it.</summary>
    Launcher,
    /// <summary>A game straight away, with the panel tucked in the tray and the launcher ready to
    /// come back when the game closes.</summary>
    Game,
}

public readonly record struct BootDecision(BootSurface Surface, string? GameId)
{
    public static BootDecision PanelFirst => new(BootSurface.Panel, null);
    public static BootDecision LauncherFirst => new(BootSurface.Launcher, null);
    public static BootDecision GameFirst(string id) => new(BootSurface.Game, id);
}

/// <summary>
/// Decides the boot surface from the command line and two settings. Pure, so the whole matrix is
/// a unit test.
///
/// Rules, in order:
/// <list type="number">
/// <item><c>--panel</c> always wins: the panel shortcut and the Windows-startup shortcut
/// (<c>--startup</c>, written by <c>StartupManager</c>) both mean "the classic app, now".</item>
/// <item>A fresh install (not welcomed, or 18+ not accepted) boots the panel, because the first-run
/// wizard and the age gate ride the panel's constructor. The launcher never appears before them.</item>
/// <item><c>--game &lt;id&gt;</c> (or <c>--game=&lt;id&gt;</c>) boots that game. An unknown id falls
/// back to the launcher so the user sees the tiles instead of nothing.</item>
/// <item><c>--launcher</c> (alias <c>--client</c>) boots the launcher.</item>
/// <item>A bare launch boots the launcher unless the user ticked "open the panel directly".</item>
/// </list>
/// </summary>
public static class LauncherBoot
{
    public const string PanelFlag = "--panel";
    public const string LauncherFlag = "--launcher";
    public const string ClientFlag = "--client";
    public const string GameFlag = "--game";
    public const string StartupFlag = "--startup";

    public static BootDecision Decide(string[]? args, bool welcomed, bool ageAccepted, bool skipToPanel,
        Func<string, bool>? knownGame = null)
    {
        args ??= Array.Empty<string>();
        knownGame ??= id => LauncherCatalogue.Find(id) != null;

        if (Has(args, PanelFlag) || Has(args, StartupFlag)) return BootDecision.PanelFirst;
        if (!welcomed || !ageAccepted) return BootDecision.PanelFirst;

        var game = GameArg(args);
        if (game != null)
            return knownGame(game) ? BootDecision.GameFirst(game) : BootDecision.LauncherFirst;

        if (Has(args, LauncherFlag) || Has(args, ClientFlag)) return BootDecision.LauncherFirst;

        return skipToPanel ? BootDecision.PanelFirst : BootDecision.LauncherFirst;
    }

    /// <summary>The id after <c>--game</c>, in either the two-token or the <c>=</c> form. Null when absent.</summary>
    public static string? GameArg(string[]? args)
    {
        if (args == null) return null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.Equals(a, GameFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return Clean(args[i + 1]);
                return null;
            }
            if (a.StartsWith(GameFlag + "=", StringComparison.OrdinalIgnoreCase))
                return Clean(a.Substring(GameFlag.Length + 1));
        }
        return null;
    }

    /// <summary>True when the args name any launcher surface at all (panel, launcher, or a game).</summary>
    public static bool NamesASurface(string[]? args) =>
        Has(args, PanelFlag) || Has(args, LauncherFlag) || Has(args, ClientFlag) || GameArg(args) != null;

    private static bool Has(string[]? args, string flag)
    {
        if (args == null) return false;
        foreach (var a in args)
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string? Clean(string raw)
    {
        var s = raw.Trim().Trim('"');
        return s.Length == 0 ? null : s.ToLowerInvariant();
    }
}
