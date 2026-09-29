using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;

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
        /// verbatim on unlock. Idempotent both ways; unlocked with nothing saved is a no-op.</summary>
        internal static void ApplyLockToolTip(Control element, bool locked, string? reason)
        {
            if (locked)
            {
                if (!element.GetValue(ToolTipSavedProperty))
                {
                    element.SetValue(SavedToolTipProperty, ToolTip.GetTip(element));
                    element.SetValue(ToolTipSavedProperty, true);
                }
                ToolTip.SetTip(element, reason);
            }
            else if (element.GetValue(ToolTipSavedProperty))
            {
                ToolTip.SetTip(element, element.GetValue(SavedToolTipProperty));
                element.ClearValue(SavedToolTipProperty);
                element.ClearValue(ToolTipSavedProperty);
            }
        }

        /// <summary>Every marked control under <paramref name="root"/>, collapsed ones included
        /// (logical tree, so a hidden tab is still painted - WPF FindOwnedControls).</summary>
        internal static IEnumerable<Control> FindOwnedControls(Control root) =>
            root.GetLogicalDescendants().OfType<Control>().Where(GetOwned);
    }
}
