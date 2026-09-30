using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// What a launcher tile IS, with no way to start it: the id, its face and its account rule. Every
/// head builds its tiles from <see cref="LauncherCards.All"/> and attaches its own launch, so the
/// order, the art and the names are written once.
/// </summary>
/// <param name="Id">Stable id: the <c>--game &lt;id&gt;</c> and shortcut argument. Never rename.</param>
/// <param name="ArtPath">Resource-relative art, mod-resolvable. Null paints the hue plate with <paramref name="Glyph"/>.</param>
/// <param name="Rgb">Tile hue as 0xRRGGBB.</param>
/// <param name="RequiresAccount">False only for entries that explicitly allow signed-out play.</param>
/// <param name="IsNew">Shows the NEW badge.</param>
public sealed record LauncherCard(string Id, string? ArtPath, string Glyph, uint Rgb,
    bool RequiresAccount = true, bool IsNew = false)
{
    public string TitleKey => "launcher_game_" + Id + "_title";
    public string BlurbKey => "launcher_game_" + Id + "_blurb";
    public byte R => (byte)(Rgb >> 16);
    public byte G => (byte)(Rgb >> 8);
    public byte B => (byte)Rgb;
}

/// <summary>
/// The launcher's tiles in launcher order: the newest, loudest room first, the quiet ones last.
/// Remote, Companion and sessions are panel features and have no tile (Sep 18 2026 owner
/// decision: the split is games vs CCP). The Graded Intake is the one panel tab with a tile.
/// </summary>
public static class LauncherCards
{
    public static readonly IReadOnlyList<LauncherCard> All = new LauncherCard[]
    {
        new("backroom", "features/backroom.png", "♦", 0xB95CD8),
        new("breakoutdemo", null, "●", 0x8CF5C8, RequiresAccount: false, IsNew: true),
        new("breakout", null, "●", 0x55B7FF, IsNew: true),
        // Piece by Piece: free for everyone (owner, 2026-09-27); its lobby asks a signed-out player.
        new("piecebypiece", "features/piecebypiece.png", "♟", 0x7B5CFF, RequiresAccount: false, IsNew: true),
        new("race", "features/race.png", "☕", 0xFFB36B),
        new("dtrh", "features/dtrh.png", "▼", 0x8CF5C8),
        new("arcademy", "features/arcademy.png", "★", 0xFF69B4),
        new("goon", "features/goon_game_tile.png", "●", 0x76C893),
        new("intake", "features/lab_quiz_hero.png", "❓", 0x8E7CF2),
    };

    public static LauncherCard? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = id.Trim();
        return All.FirstOrDefault(c => string.Equals(c.Id, key, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>What the launcher's close button does.</summary>
public enum LauncherCloseOutcome
{
    /// <summary>Lockdown: the launcher is never a way around the veil.</summary>
    Veto,
    /// <summary>Something runs behind it, or the panel is up: hide, the panel/tray is the way back.</summary>
    Hide,
    /// <summary>Nothing running and the panel tucked away: closing the launcher means leaving.</summary>
    Exit,
}

/// <summary>What a tile's Play does, in the order the checks run.</summary>
public enum LauncherGameStep
{
    /// <summary>Nobody signed in and the entry needs an account: the launcher's sign-in flow.</summary>
    SignIn,
    /// <summary>Tier-locked: the host's own refusal toast, the launcher stays up behind it.</summary>
    Refuse,
    /// <summary>A leash punishment is pending: the panel comes up with the gate.</summary>
    Leash,
    /// <summary>Start it and hide the launcher.</summary>
    Launch,
}

/// <summary>
/// The launch-handoff and panel-to-tray lifecycle rules, pure so each head's host only acts on
/// the answer (WPF <c>LauncherHost</c>, Avalonia <c>LauncherWindow</c>).
/// </summary>
public static class LauncherRules
{
    /// <summary>The longest the window may hold its exit beat before it hides, in ms.</summary>
    public const int MaxHideDelayMs = 1500;

    /// <summary>How long the launcher takes to fade to nothing before it hides, in ms.</summary>
    public const int FadeOutMs = 220;

    public static int ClampHideDelay(int ms) => Math.Clamp(ms, 0, MaxHideDelayMs);

    /// <summary>The fade is the tail of an exit beat, never added after it: with a 450 ms beat
    /// and a 220 ms fade, the fade starts at 230. A beat shorter than the fade fades at once.</summary>
    public static int FadeLeadMs(int delayMs) => Math.Max(0, ClampHideDelay(delayMs) - FadeOutMs);

    /// <param name="panelExists">False when there is no panel to exit through; then only hide.</param>
    public static LauncherCloseOutcome Close(bool lockdown, bool somethingRunning, bool panelOnScreen, bool panelExists)
    {
        if (lockdown) return LauncherCloseOutcome.Veto;
        if (somethingRunning || panelOnScreen || !panelExists) return LauncherCloseOutcome.Hide;
        return LauncherCloseOutcome.Exit;
    }

    public static LauncherGameStep Game(bool needsAccount, bool locked, bool leashBlocks)
    {
        if (needsAccount) return LauncherGameStep.SignIn;
        if (locked) return LauncherGameStep.Refuse;
        if (leashBlocks) return LauncherGameStep.Leash;
        return LauncherGameStep.Launch;
    }
}
