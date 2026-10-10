// PORTED from ConditioningControlPanel/Services/Possession/Possession.cs and the visual-tree half of
// PossessionOffLimits.cs (7.1.5). A control opts in to being haunted by carrying poss:Possession.Role;
// the shell walks its visual tree and turns every tagged, visible element into a PossessionTarget.
//
// Reach = WPF 7.1.5 (owner, 10 Oct 2026; k22): display controls, plus the roles WPF tags on things the
// user acts on (rail doors, the Start button, the lockdown card, the Lockdown toggles, the timer),
// inside the owner's hard limits: PossessionTree.IsSafety is refused everywhere, a card that holds
// controls may only glow, and the Strict Lock / panic-key settings are never enrolled.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Services.Possession;

public static class Possession
{
    /// <summary>What this control IS. None (the default) = never possess me. Never inherits: tagging
    /// a card must not enrol what is inside it.</summary>
    public static readonly AttachedProperty<PossessionRole> RoleProperty =
        AvaloniaProperty.RegisterAttached<Control, PossessionRole>("Role", typeof(Possession), PossessionRole.None);

    public static void SetRole(Control element, PossessionRole value) => element?.SetValue(RoleProperty, value);
    public static PossessionRole GetRole(Control element) => element == null ? PossessionRole.None : element.GetValue(RoleProperty);

    /// <summary>The friendly name the warden says ("the title"). Lower case, with its article. Not a
    /// loc key on purpose (WPF): the bark packs drop it straight into an authored phrase.</summary>
    public static readonly AttachedProperty<string> NameProperty =
        AvaloniaProperty.RegisterAttached<Control, string>("Name", typeof(Possession), string.Empty);

    public static void SetName(Control element, string value) => element?.SetValue(NameProperty, value);
    public static string GetName(Control element) => element == null ? string.Empty : element.GetValue(NameProperty) ?? string.Empty;

    /// <summary>Never a target, and neither is anything inside (inherits DOWN).</summary>
    public static readonly AttachedProperty<bool> ExcludeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Exclude", typeof(Possession), false, inherits: true);

    public static void SetExclude(Control element, bool value) => element?.SetValue(ExcludeProperty, value);
    public static bool GetExclude(Control element) => element != null && element.GetValue(ExcludeProperty);
}

/// <summary>The tree half of the Possession rules on this head: who may be enrolled, who is off limits.</summary>
public static class PossessionTree
{
    /// <summary>WPF PossessionOffLimits.MaxDepth.</summary>
    public const int MaxDepth = 16;

    /// <summary>The roles this head enrols: things the user READS. Everything else names a control
    /// the user acts on or relies on (a button, a toggle, the timer).</summary>
    public static bool IsDisplayRole(PossessionRole role) =>
        role is PossessionRole.Card or PossessionRole.Title or PossessionRole.Label or PossessionRole.Image or PossessionRole.Progress;

    /// <summary>Anything the user presses, types in, drags or scrolls.</summary>
    public static bool IsInteractive(Visual? v) =>
        v is Button or ToggleButton or TextBox or RangeBase and not ProgressBar or SelectingItemsControl or ScrollBar
            or Thumb or MenuItem or ScrollViewer or NumericUpDown or CalendarDatePicker or ListBoxItem or TabItem;

    /// <summary>True when no effect may touch this element: null, excluded, holding anything excluded
    /// or interactive, interactive itself or sitting INSIDE an interactive control (a label that is a
    /// button's face moves the button), or named for a room the user must be able to leave.
    /// A control we cannot reason about is off limits.</summary>
    public static bool IsOffLimits(Control? el)
    {
        if (el == null) return true;
        try
        {
            if (Possession.GetExclude(el)) return true;   // inherited: covers every excluded ancestor
            if (PossessionOffLimits.IsReservedName(el.Name)) return true;
            if (IsInteractive(el)) return true;
            if (HoldsProtected(el, 0)) return true;
            foreach (var a in el.GetVisualAncestors())
                if (a is Button or ToggleButton or MenuItem or Thumb or TextBox) return true;
        }
        catch { return true; }
        return false;
    }

    /// <summary>True when anything BELOW this node is excluded, interactive or reserved by name.</summary>
    public static bool HoldsProtected(Visual? node) => HoldsProtected(node, 0);

    private static bool HoldsProtected(Visual? node, int depth)
    {
        if (node == null) return false;
        if (depth > MaxDepth) return true;   // too deep to prove safe
        foreach (var child in node.GetVisualChildren())
        {
            if (IsInteractive(child)) return true;
            if (child is Control c && (Possession.GetExclude(c) || PossessionOffLimits.IsReservedName(c.Name))) return true;
            if (HoldsProtected(child, depth + 1)) return true;
        }
        return false;
    }

    // ---- reach = WPF 7.1.5 (owner, 10 Oct 2026), inside the owner's hard limits -------------------
    // Three answers, asked at the walk, at CanApply and again in Apply:
    //   IsSafety            never, by any effect (PossessionOffLimits.IsSafetyName, Exclude, a dialog,
    //                       a Start / Stop control while anything runs).
    //   IsGuardedContainer  a tagged card that HOLDS controls (the lockdown card): it may only glow.
    //                       Nothing scales, moves or hides it, so what it holds stays where it is.
    //   MayTouch            everything else an effect asks before it takes a victim.

    /// <summary>"Is anything running", so a Start control is a STOP control right now. The shell
    /// widens it to sessions at install. Unknown counts as running.</summary>
    public static Func<bool> SomethingRunning = () => global::ConditioningControlPanel.CoreEngine.IsRunning;

    private static bool Running()
    {
        try { return SomethingRunning(); } catch { return true; }
    }

    /// <summary>The roles WPF tags that the user acts on. Each is enrolled only on its own control type.</summary>
    public static bool IsInteractiveRole(PossessionRole role) =>
        role is PossessionRole.Button or PossessionRole.TabHeader or PossessionRole.Toggle or PossessionRole.Timer;

    private static bool IsSafetyNode(Visual v)
    {
        if (v is Window { Owner: not null }) return true;   // every dialog button
        if (v is not Control c) return false;
        if (PossessionOffLimits.IsSafetyName(c.Name)) return true;
        return PossessionOffLimits.IsStartStopName(c.Name) && Running();
    }

    /// <summary>True when this control is, or sits inside, something no effect may ever touch.
    /// A control we cannot reason about is a safety control.</summary>
    public static bool IsSafety(Control? el)
    {
        if (el == null) return true;
        try
        {
            if (Possession.GetExclude(el)) return true;
            if (IsSafetyNode(el)) return true;
            foreach (var a in el.GetVisualAncestors()) if (IsSafetyNode(a)) return true;
        }
        catch { return true; }
        return false;
    }

    /// <summary>True when anything BELOW this node is a safety control (or too deep to prove not).</summary>
    public static bool HoldsSafety(Visual? node) => HoldsSafety(node, 0);

    private static bool HoldsSafety(Visual? node, int depth)
    {
        if (node == null) return false;
        if (depth > MaxDepth) return true;
        foreach (var child in node.GetVisualChildren())
        {
            if (IsSafetyNode(child)) return true;
            if (child is Control c && Possession.GetExclude(c)) return true;
            if (HoldsSafety(child, depth + 1)) return true;
        }
        return false;
    }

    private static bool InsideInteractive(Control el)
    {
        foreach (var a in el.GetVisualAncestors())
            if (a is Button or ToggleButton or MenuItem or Thumb or TextBox) return true;
        return false;
    }

    /// <summary>A tagged CARD that holds controls (and so may hold an exit). It is enrolled, and the
    /// only thing any effect may do to it is glow: no transform, no opacity, no hit-test change.</summary>
    public static bool IsGuardedContainer(Control? el)
    {
        if (el == null) return false;
        try
        {
            return Possession.GetRole(el) == PossessionRole.Card && el is Border or Panel
                   && !IsSafety(el) && !IsInteractive(el) && !InsideInteractive(el)
                   && (HoldsProtected(el) || PossessionOffLimits.IsReservedName(el.Name));
        }
        catch { return false; }
    }

    /// <summary>May an effect take this control in this role? <paramref name="takesInteractive"/>:
    /// the effect was written for controls the user acts on. <paramref name="glowOnly"/>: the effect
    /// leaves bounds and input alone, so a guarded container may have it.</summary>
    public static bool MayTouch(Control? c, PossessionRole role, bool takesInteractive, bool glowOnly = false)
    {
        if (c == null) return false;
        try
        {
            if (IsSafety(c)) return false;
            if (IsDisplayRole(role))
            {
                if (!IsOffLimits(c)) return true;
                return glowOnly && IsGuardedContainer(c);
            }
            if (!takesInteractive || !IsInteractiveRole(role) || InsideInteractive(c)) return false;
            return role switch
            {
                PossessionRole.Button or PossessionRole.TabHeader => c is Button and not ToggleButton && !HoldsSafety(c),
                PossessionRole.Toggle => c is ToggleButton && !HoldsSafety(c),
                PossessionRole.Timer => c is TextBlock,
                _ => false,
            };
        }
        catch { return false; }
    }

    /// <summary>May the walk enrol this tagged control at all?</summary>
    public static bool MayEnrol(Control? c)
    {
        if (c == null) return false;
        var role = Possession.GetRole(c);
        return role != PossessionRole.None && MayTouch(c, role, takesInteractive: true, glowOnly: true);
    }

    /// <summary>Every tagged display control under <paramref name="root"/>. Targets are cached by key
    /// so a cooldown or a live booking survives the next read; a control that left the tree drops out.</summary>
    public static IReadOnlyList<PossessionTarget> Collect(Visual? root, Dictionary<string, PossessionTarget> cache)
    {
        var list = new List<PossessionTarget>();
        if (root == null) return list;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in root.GetVisualDescendants())
        {
            if (v is not Control c) continue;
            var role = Possession.GetRole(c);
            if (role == PossessionRole.None || !MayEnrol(c)) continue;
            string display = Possession.GetName(c);
            string key = !string.IsNullOrEmpty(c.Name) ? c.Name! : role + ":" + display;
            if (!seen.Add(key)) continue;
            if (!cache.TryGetValue(key, out var t) || !ReferenceEquals(t.Element, c))
            {
                t = new PossessionTarget
                {
                    Element = c, Role = role, Key = key, DisplayName = display,
                    IsVisible = () => c.IsEffectivelyVisible && c.Bounds.Width > 0,
                };
                cache[key] = t;
            }
            list.Add(t);
        }
        return list;
    }

    /// <summary>WPF PossessionVisual.FindTextBlock: the element itself, or the one TextBlock it shows.</summary>
    public static TextBlock? FindTextBlock(object? element)
    {
        if (element is TextBlock tb) return tb;
        if (element is not Visual v) return null;
        TextBlock? found = null;
        foreach (var d in v.GetVisualDescendants())
        {
            if (d is not TextBlock t) continue;
            if (found != null) return null;   // two faces: no single text to rewrite
            found = t;
        }
        return found;
    }

    /// <summary>WPF PossessionVisual.IsRewritable: plain text of a useful length (no inline runs).</summary>
    public static bool IsRewritable(TextBlock? tb, int minLength = 3)
    {
        try
        {
            if (tb == null) return false;
            var text = tb.Text;
            if (string.IsNullOrWhiteSpace(text) || text!.Length < minLength) return false;
            return tb.Inlines == null || tb.Inlines.Count == 0;
        }
        catch { return false; }
    }
}
