// PORTED from ConditioningControlPanel/Services/Possession/Possession.cs and the visual-tree half of
// PossessionOffLimits.cs (7.1.5). A control opts in to being haunted by carrying poss:Possession.Role;
// the shell walks its visual tree and turns every tagged, visible element into a PossessionTarget.
//
// Narrower than WPF on purpose (HB13, kept by k18): this head enrols DISPLAY controls only. A tagged
// element that IS or HOLDS anything the user presses, types in or drags is refused, and so is every
// role that names one (Button, Toggle, Timer, Slider, Combo, TextBox, TabHeader, Scroll). WPF enrols
// the rail doors, the Start button, the lockdown card and the safety toggles; widening is an owner call.

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
            or Thumb or MenuItem or ScrollViewer or NumericUpDown or CalendarDatePicker;

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

    /// <summary>May the walk enrol this tagged control at all?</summary>
    public static bool MayEnrol(Control? c) => c != null && IsDisplayRole(Possession.GetRole(c)) && !IsOffLimits(c);

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
