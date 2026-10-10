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
}
