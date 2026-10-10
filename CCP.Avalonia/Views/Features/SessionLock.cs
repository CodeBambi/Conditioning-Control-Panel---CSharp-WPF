using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Features/SessionLock.cs: the XAML marker for "this
    /// control sets a value a running session OWNS" (dosage; comfort stays unmarked). See the WPF
    /// class remarks for the classification rule. MainShellWindow.SessionFeatureLock.cs sweeps it.
    /// </summary>
    public sealed class SessionLock : AvaloniaObject
    {
        public static readonly AttachedProperty<bool> OwnedProperty =
            AvaloniaProperty.RegisterAttached<SessionLock, Control, bool>("Owned");

        private static readonly AttachedProperty<object?> SavedToolTipProperty =
            AvaloniaProperty.RegisterAttached<SessionLock, Control, object?>("SavedToolTip");

        private static readonly AttachedProperty<bool> ToolTipSavedProperty =
            AvaloniaProperty.RegisterAttached<SessionLock, Control, bool>("ToolTipSaved");

        public static void SetOwned(Control element, bool value) => element.SetValue(OwnedProperty, value);
        public static bool GetOwned(Control element) => element.GetValue(OwnedProperty);

        /// <summary>WPF ApplyLockToolTip: borrow the tooltip while locked, put the original back
        /// verbatim on unlock. Idempotent both ways; unlocked with nothing saved is a no-op.
        ///
        /// <para>A locked control is a disabled one, and tooltips stay silent on disabled controls
        /// unless <c>ShowOnDisabled</c> is set: without it the explanation never appears at all
        /// (WPF ToolTipService.SetShowOnDisabled). It is set with the borrowed tip and cleared with
        /// it; a control we never borrowed from is left completely alone.</para>
        ///
        /// <para>Not ported: WPF saves the tooltip's BINDING and re-applies it, so a localized tip
        /// keeps following the language after an unlock. Avalonia hands back no reusable binding
        /// for a set value, so the tip that comes back here is the text it had at lock time.</para></summary>
        internal static void ApplyLockToolTip(Control element, bool locked, string? reason)
        {
            if (element == null) return;
            if (locked)
            {
                if (!element.GetValue(ToolTipSavedProperty))
                {
                    element.SetValue(SavedToolTipProperty, ToolTip.GetTip(element));
                    element.SetValue(ToolTipSavedProperty, true);
                }
                ToolTip.SetTip(element, reason);
                ToolTip.SetShowOnDisabled(element, true);
            }
            else if (element.GetValue(ToolTipSavedProperty))
            {
                var original = element.GetValue(SavedToolTipProperty);
                if (original == null) element.ClearValue(ToolTip.TipProperty);
                else ToolTip.SetTip(element, original);
                element.ClearValue(SavedToolTipProperty);
                element.ClearValue(ToolTipSavedProperty);
                element.ClearValue(ToolTip.ShowOnDisabledProperty);
            }
        }

        /// <summary>WPF MaxDepth: a pathological tree never stalls the paint.</summary>
        private const int MaxDepth = 64;

        /// <summary>Every marked control under <paramref name="root"/>, collapsed ones included.
        /// WPF FindOwnedControls: the LOGICAL children (so a hidden tab is still painted) plus the
        /// VISUAL children, for anything only reachable through a template; each node once, to a
        /// bounded depth, and a control mid-teardown that throws only loses its own subtree (a
        /// partial lock beats an exception that aborts the whole paint).</summary>
        internal static List<Control> FindOwnedControls(Control? root)
        {
            var found = new List<Control>();
            if (root == null) return found;
            Collect(root, found, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            return found;
        }

        private static void Collect(object node, List<Control> found, HashSet<object> seen, int depth)
        {
            if (depth > MaxDepth || !seen.Add(node)) return;
            if (node is Control c && GetOwned(c)) found.Add(c);

            if (node is ILogical logical)
            {
                try
                {
                    foreach (var child in logical.LogicalChildren)
                        Collect(child, found, seen, depth + 1);
                }
                catch (Exception) { /* keep walking what we can */ }
            }
            if (node is Visual visual)
            {
                try
                {
                    foreach (var child in visual.GetVisualChildren())
                        Collect(child, found, seen, depth + 1);
                }
                catch (Exception) { /* same rationale as above */ }
            }
        }
    }
}
