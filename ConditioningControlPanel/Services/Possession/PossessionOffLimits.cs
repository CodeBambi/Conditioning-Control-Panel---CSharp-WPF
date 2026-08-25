using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  POSSESSION - the one answer to "may an effect take this away from the user?".
//  Read Services/Possession/POSSESSION.md ("Hard rules") first.
//
//  poss:Possession.Exclude keeps an element and its SUBTREE out of the deck, and that is the whole
//  story for a victim that is a leaf. It is not the whole story for a CONTAINER: Exclude inherits
//  DOWN, never UP, so a card is perfectly enrollable while the Emergency Exit button and the secret
//  exit box sit inside it. An effect that drops that card off-screen, hides it or turns its hit-
//  testing off takes the exits with it - for 45 s (fall) or two minutes (stealcard) - which is the
//  one thing POSSESSION.md says may never happen.
//
//  So: an element is off limits when it IS excluded, when it CONTAINS anything excluded, or when it
//  (or any ancestor) is named for a room the user has to be able to leave. StealCardEffect has
//  carried this rule privately since wave 2; this is the same rule, shared, so the next effect that
//  hides a victim does not have to rediscover it.
// =====================================================================================================

public static class PossessionOffLimits
{
    /// <summary>Same ceiling the other visual-tree walks in this folder use: a card is a handful of
    /// levels deep, and an unbounded walk on every CanApply is a per-tick cost nobody notices until
    /// the room is full.</summary>
    public const int MaxDepth = 16;

    /// <summary>The rooms the user must always be able to leave. Matched case-insensitively against
    /// x:Name, so LockdownCardBorder, BtnEmergencyExit and TxtSecretExit all answer to it.</summary>
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

    /// <summary>
    /// True when no effect may drop, hide or hit-test-kill this element (or anything it contains).
    /// A null element, or one we cannot reason about, is off limits: the failure mode of guessing
    /// wrong here is a user who cannot end their own lockdown.
    /// </summary>
    public static bool IsOffLimits(FrameworkElement? el)
    {
        if (el == null) return true;
        try
        {
            if (Possession.GetExclude(el)) return true;
            if (ContainsExcluded(el)) return true;

            for (DependencyObject? node = el; node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement fe) continue;
                if (Possession.GetExclude(fe)) return true;
                if (IsReservedName(fe.Name)) return true;
            }
        }
        catch { return true; }
        return false;
    }

    /// <summary>True when anything BELOW this node carries Possession.Exclude. The node itself does
    /// not count (that is the caller's own check) so a hand-excluded victim and a container full of
    /// them stay two separate, readable answers.</summary>
    public static bool ContainsExcluded(DependencyObject? node) => ContainsExcluded(node, 0);

    private static bool ContainsExcluded(DependencyObject? node, int depth)
    {
        if (node == null || depth > MaxDepth) return false;
        try
        {
            if (depth > 0 && node is FrameworkElement fe && Possession.GetExclude(fe)) return true;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                if (ContainsExcluded(VisualTreeHelper.GetChild(node, i), depth + 1)) return true;
            }
        }
        catch { }
        return false;
    }
}
