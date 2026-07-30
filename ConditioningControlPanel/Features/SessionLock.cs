using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Declarative marker for "this control sets a value a running session OWNS".
    ///
    /// WHY AN ATTACHED PROPERTY AND NOT A LIST OF NAMES IN CODE. The dials that a session
    /// prescribes are spread over ~14 <c>Features/*FeatureControl.xaml</c> files and there are
    /// well over a hundred of them. A hand-maintained list of control names in a .cs file would
    /// be write-only: nobody adding a slider to VideoFeatureControl.xaml will remember to go
    /// edit MainWindow.SessionFeatureLock.cs, so the list would silently rot into a set of holes. The
    /// marker instead lives ON the control it describes, three characters from the thing a
    /// future author is already editing.
    ///
    /// THE CLASSIFICATION RULE (apply it when adding any new control):
    ///
    ///   Owned="True"  - DOSAGE. The value decides how much conditioning happens: the master
    ///                   enable, rates (per-minute / per-hour), how many at once, opacity,
    ///                   scale, speed of an effect, intensity, ramp endpoints. These are what
    ///                   a Session prescribes in <see cref="Models.SessionSettings"/> and what
    ///                   SessionEngine ramps, so letting the user move them mid-session is how
    ///                   you quietly opt out of the session you started.
    ///
    ///   unmarked      - COMFORT. The value decides how the same dose FEELS or how it is
    ///                   delivered: volumes, click-to-dismiss, hydra, "solid mode" renderer
    ///                   choice, monitor placement, which asset folder. The user keeps these
    ///                   for the whole session. Also everything remotely safety-shaped: stop,
    ///                   panic, No-Panic, strict lock, withdraw, exit.
    ///
    /// When in doubt, leave it unmarked. Over-locking takes control away from the user for no
    /// benefit; under-locking is the bug we already had, and is trivially fixed by adding one
    /// attribute later.
    ///
    /// The marker only makes a control ELIGIBLE. Whether it is actually greyed out is decided
    /// live by MainWindow.SessionFeatureLock.cs, which re-derives session state on every paint and
    /// never latches.
    /// </summary>
    public static class SessionLock
    {
        /// <summary>
        /// Set <c>features:SessionLock.Owned="True"</c> in XAML on any control whose value a
        /// running session prescribes. See the class remarks for the rule.
        /// </summary>
        public static readonly DependencyProperty OwnedProperty =
            DependencyProperty.RegisterAttached(
                "Owned",
                typeof(bool),
                typeof(SessionLock),
                new PropertyMetadata(false));

        public static void SetOwned(DependencyObject element, bool value)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            element.SetValue(OwnedProperty, value);
        }

        public static bool GetOwned(DependencyObject element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            return (bool)element.GetValue(OwnedProperty);
        }

        /// <summary>
        /// Stashes whatever a control's ToolTip was before the lock borrowed it.
        ///
        /// Needed because the lock's explanation has to go SOMEWHERE visible, and the tooltip is
        /// the only slot available - but several locked controls already own a hand-written
        /// tooltip (ChkAwarenessMaster carries a whole explanatory panel). Overwriting it and
        /// then calling ClearValue on unlock would not restore it; ClearValue erases the locally
        /// set value, which for a XAML-declared tooltip is the tooltip itself. One lock/unlock
        /// cycle would silently strip documentation the user relies on, permanently, until app
        /// restart. So the original is saved here and put back verbatim.
        /// </summary>
        internal static readonly DependencyProperty SavedToolTipProperty =
            DependencyProperty.RegisterAttached(
                "SavedToolTip", typeof(object), typeof(SessionLock), new PropertyMetadata(null));

        /// <summary>
        /// Whether <see cref="SavedToolTipProperty"/> currently holds a saved value. A separate
        /// flag rather than a null check, because "the control had no tooltip" is a real state we
        /// must restore faithfully (by clearing), and is not the same as "we saved nothing".
        /// Also makes the save idempotent: repainting a control that is already locked must not
        /// overwrite the saved original with the lock's own reason string.
        /// </summary>
        internal static readonly DependencyProperty ToolTipSavedProperty =
            DependencyProperty.RegisterAttached(
                "ToolTipSaved", typeof(bool), typeof(SessionLock), new PropertyMetadata(false));

        /// <summary>
        /// Points a control's tooltip at <paramref name="reason"/> while locked and restores the
        /// original when unlocked. Idempotent in both directions.
        ///
        /// TWO TRAPS, both found by play-test, both of which silently DESTROYED tooltips:
        ///
        /// 1. The unlocked path must touch NOTHING unless we ourselves saved something. An earlier
        ///    version called ClearValue on the tooltip in the "never locked" branch, reasoning it
        ///    was undoing its own write. It was not: for a control that was never locked, that
        ///    ClearValue erases the XAML-declared tooltip permanently. And because a successful
        ///    restore clears ToolTipSaved, the very next unlocked repaint fell into that same
        ///    branch and destroyed the tooltip it had just restored. Unlocked + nothing saved is
        ///    now a genuine no-op.
        ///
        /// 2. A tooltip set with the loc:Str markup extension is a BINDING, so
        ///    ReadLocalValue hands back a BindingExpression - and storing one of those in another
        ///    DependencyProperty throws ("Cannot set Expression. It is marked as 'NonShareable'
        ///    and has already been used"), which aborted the whole lock paint mid-way. So a bound
        ///    tooltip is captured as its reusable BindingBase and restored with SetBinding.
        /// </summary>
        internal static void ApplyLockToolTip(FrameworkElement element, bool locked, string? reason)
        {
            if (element == null) return;

            if (locked)
            {
                if (!(bool)element.GetValue(ToolTipSavedProperty))
                {
                    // Prefer the Binding over the BindingExpression: the markup object is a plain
                    // reusable object and can be re-applied later, the expression cannot even be
                    // stored (trap 2). Falls back to ReadLocalValue for a literal tooltip, which
                    // returns UnsetValue when the value comes from a style or is simply absent.
                    object? saved = BindingOperations.GetBindingBase(element, FrameworkElement.ToolTipProperty);
                    saved ??= element.ReadLocalValue(FrameworkElement.ToolTipProperty);

                    element.SetValue(SavedToolTipProperty, saved);
                    element.SetValue(ToolTipSavedProperty, true);
                }
                element.ToolTip = reason;
                // WPF suppresses tooltips on disabled controls unless this is set, and a locked
                // control is a disabled one - without it the explanation never appears at all.
                ToolTipService.SetShowOnDisabled(element, true);
                return;
            }

            // Unlocked. If we never borrowed this control's tooltip, leave it completely alone
            // (trap 1) - including ShowOnDisabled, which we also never touched.
            if (!(bool)element.GetValue(ToolTipSavedProperty)) return;

            var original = element.GetValue(SavedToolTipProperty);
            if (original is BindingBase binding)
                element.SetBinding(FrameworkElement.ToolTipProperty, binding);
            else if (original == null || original == DependencyProperty.UnsetValue)
                element.ClearValue(FrameworkElement.ToolTipProperty);
            else
                element.ToolTip = original;

            element.ClearValue(SavedToolTipProperty);
            element.ClearValue(ToolTipSavedProperty);
            element.ClearValue(ToolTipService.ShowOnDisabledProperty);
        }

        /// <summary>Depth ceiling. Purely a cycle/pathology guard - real popups nest ~8 deep.</summary>
        private const int MaxDepth = 64;

        /// <summary>
        /// Every <see cref="Control"/> under <paramref name="root"/> carrying Owned="True".
        ///
        /// Walks the LOGICAL tree first and the VISUAL tree as a fallback, unioned, because
        /// neither alone is sufficient here:
        ///
        /// - The logical tree is <see cref="Visibility"/>-independent, which matters a lot:
        ///   several popups keep whole option panels Collapsed until a parent toggle is ticked
        ///   (BubblePop's TriggerOptionsPanel, Video's attention-check block). A visual-only
        ///   walk can miss those, and a dial that is only unlocked because it happened to be
        ///   hidden when the session started is exactly the kind of hole this file exists to
        ///   close.
        /// - The visual tree catches controls that are only reachable through a template
        ///   (ScrollViewer content, ContentPresenter), which the logical walk can skip.
        ///
        /// Yielding the same control twice would be harmless (locking is idempotent), but we
        /// dedup by reference anyway so callers can count what they touched.
        /// </summary>
        public static List<Control> FindOwnedControls(DependencyObject? root)
        {
            var found = new List<Control>();
            if (root == null) return found;

            var seen = new HashSet<DependencyObject>();
            Collect(root, found, seen, 0);
            return found;
        }

        private static void Collect(DependencyObject node, List<Control> found,
                                    HashSet<DependencyObject> seen, int depth)
        {
            if (depth > MaxDepth) return;
            if (!seen.Add(node)) return;

            if (node is Control c && GetOwned(c)) found.Add(c);

            // Logical children. Visibility-independent - see the remarks above.
            try
            {
                foreach (var child in LogicalTreeHelper.GetChildren(node))
                    if (child is DependencyObject dep)
                        Collect(dep, found, seen, depth + 1);
            }
            catch
            {
                // A control mid-teardown can throw here. Keep walking what we can: a partial
                // lock beats an exception that aborts the whole paint.
            }

            // Visual children, for anything only reachable through a template. Guarded because
            // VisualTreeHelper throws on non-Visual DependencyObjects.
            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                try
                {
                    int count = VisualTreeHelper.GetChildrenCount(node);
                    for (int i = 0; i < count; i++)
                        Collect(VisualTreeHelper.GetChild(node, i), found, seen, depth + 1);
                }
                catch { /* same rationale as above */ }
            }
        }
    }
}
