// PORTED from ConditioningControlPanel/Services/Possession/PossessionOffLimits.cs (7.1.5): the pure
// half of "may an effect take this away from the user?". The visual-tree half (an element that IS
// excluded, CONTAINS anything excluded, or sits under a reserved name) stays with a head that walks
// a tree; a head with an explicit registry applies the name rule to every entry.

using System;

namespace ConditioningControlPanel.Services.Possession;

public static class PossessionOffLimits
{
    /// <summary>The rooms the user must always be able to leave. Matched case-insensitively against
    /// the control name, so LockdownCardBorder, BtnEmergencyExit and TxtSecretExit all answer to it.</summary>
    private static readonly string[] _reservedNameTokens = { "lockdown", "emergency", "secret" };

    /// <summary>The name half of the rule, pure so it can be pinned by a test.</summary>
    public static bool IsReservedName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var token in _reservedNameTokens)
        {
            if (name!.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    /// <summary>SAFETY controls (owner, 10 Oct 2026): never moved, hidden, relabelled, dodged, robbed of
    /// input or made harder to hit by any effect, at any frame. Matched case-insensitively against the
    /// control's own name and every ancestor's: the Emergency Exit, the exit phrase and its input, any
    /// panic affordance, a Stop / pause / cancel / close control, the leash cut, tray items, dialogs,
    /// the Strict Lock and panic-key settings, and the two switches that end or soften the haunt itself
    /// (the Possession switch, photosensitive-safe).</summary>
    private static readonly string[] _safetyNameTokens =
    {
        "emergency", "secret", "exit", "panic", "strict", "stop", "pause", "cancel", "close", "quit",
        "leash", "safeword", "tray", "dialog", "possessionenabled", "photosafe",
    };

    /// <summary>True for a name no effect may ever touch, tagged or not.</summary>
    public static bool IsSafetyName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var token in _safetyNameTokens)
        {
            if (name!.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    /// <summary>A Start / Stop control: a start while nothing runs, a STOP (and so a safety control)
    /// the moment anything does. The head answers "is anything running".</summary>
    public static bool IsStartStopName(string? name) =>
        !string.IsNullOrEmpty(name) && name!.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0;
}
