using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// The tray's side of "the leashed side can cut from every surface".
///
/// <para>Two facts about the tray (Services/Notifications/TrayIconService.cs) drive this:</para>
/// <list type="bullet">
/// <item>The icon only exists while the panel is tucked into the tray: <c>ShowWindow</c> hides it
/// and only <c>MinimizeToTray</c> shows it. A leashed user with the panel open had NO tray icon,
/// so a tray "Cut leash" would have been unreachable. <see cref="KeepIconVisible"/> keeps it up
/// while leashed.</item>
/// <item>The only tray item ever greyed is "Back to CC Labs", under Lockdown. Anything that ever
/// greys or hides tray items for Lockdown / Strict Lock must ask <see cref="IsAlwaysAllowed"/>
/// first: the cut is on that list and stays live whatever the lock state.</item>
/// </list>
/// </summary>
public static class LeashTrayRule
{
    /// <summary>The id the UI lane's tray "Cut leash" item uses.</summary>
    public const string CutActionId = "leash_cut";

    private static readonly HashSet<string> AlwaysAllowed = new(StringComparer.Ordinal)
    {
        CutActionId,
    };

    /// <summary>Tray actions no Lockdown, Strict Lock or mandatory video may grey out or hide.</summary>
    public static bool IsAlwaysAllowed(string? actionId) =>
        actionId != null && AlwaysAllowed.Contains(actionId);

    /// <summary>Whether a tray item should be enabled. <paramref name="lockedOut"/> is whatever
    /// the caller would otherwise grey it for (Lockdown active, and so on).</summary>
    public static bool ItemEnabled(string? actionId, bool lockedOut) =>
        IsAlwaysAllowed(actionId) || !lockedOut;

    /// <summary>Keep the tray icon on screen while the panel is showing. True only while leashed.</summary>
    public static bool KeepIconVisible(bool leashed) => leashed;
}
