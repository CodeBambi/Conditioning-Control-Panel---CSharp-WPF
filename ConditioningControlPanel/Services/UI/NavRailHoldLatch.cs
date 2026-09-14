using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The nav rail's "stay open" claims, held BY OWNER rather than counted.
    ///
    /// <para><b>Why this exists (v6.9.5 bug, "the rail stops minimizing").</b> The rail used to
    /// keep a bare <c>int</c>: every caller that wanted the flyout pinned open incremented it and
    /// promised to decrement it again. That promise is only as good as the event pair behind it,
    /// and the pin menu the Favorites rail added in 6.9.4 took its claim on
    /// <c>ContextMenuOpening</c> - a REQUEST to show a menu - while releasing it on the menu's
    /// <c>Closed</c>, which is only ever raised for a menu that actually opened. Any request that
    /// did not end in an open (WPF no-ops <c>IsOpen = true</c> on a menu that is already open, and
    /// a handler or ContextMenuService can suppress the show outright) left the count one above
    /// zero for the rest of the process. The rail then ignored every collapse trigger until the
    /// app was restarted, which is exactly how the field reported it.</para>
    ///
    /// <para>Keyed claims make the arithmetic unable to drift: a second claim from the same owner
    /// is the same claim, a release from an owner that holds nothing is a no-op, and neither can
    /// take the set below empty. Counting is still what the rail asks it (<see cref="Held"/>) -
    /// two owners can pin the rail at once and the first one out must not free the other - but
    /// the count is now DERIVED from who is actually holding, not from how many times someone
    /// remembered to call.</para>
    ///
    /// <para>Not thread-safe, and deliberately so: every caller is on the UI thread, and a lock
    /// here would only hide a caller that is not.</para>
    /// </summary>
    internal sealed class NavRailHoldLatch
    {
        /// <summary>Reference identity, not Equals: an owner is a live object (a ContextMenu, a
        /// tutorial step), and two distinct menus that happen to compare equal are still two
        /// claims.</summary>
        private readonly HashSet<object> _owners = new(ReferenceEqualityComparer.Instance);

        /// <summary>How many owners are holding the rail open.</summary>
        internal int Count => _owners.Count;

        /// <summary>True while anything at all is holding the rail open.</summary>
        internal bool Held => _owners.Count > 0;

        /// <summary>
        /// Registers <paramref name="owner"/>'s claim. Returns true only when this is a NEW claim,
        /// so the caller can raise the rail once rather than on every repeat.
        /// </summary>
        internal bool Take(object? owner) => owner != null && _owners.Add(owner);

        /// <summary>
        /// Drops <paramref name="owner"/>'s claim. Returns true only when that was the LAST claim
        /// standing, i.e. when the rail should go back to following the pointer. An owner that
        /// holds nothing releases nothing - it cannot free somebody else's hold, and it cannot
        /// take the set below empty.
        /// </summary>
        internal bool Release(object? owner)
            => owner != null && _owners.Remove(owner) && _owners.Count == 0;

        /// <summary>True when <paramref name="owner"/> currently holds a claim.</summary>
        internal bool Holds(object? owner) => owner != null && _owners.Contains(owner);

        /// <summary>
        /// Drops every claim and reports how many were still standing. This is the watchdog's
        /// hammer, so the number it returns is the interesting one: anything above zero is a
        /// claim whose owner never released it, which is a bug worth a log line.
        /// </summary>
        internal int Clear()
        {
            int standing = _owners.Count;
            _owners.Clear();
            return standing;
        }
    }

    /// <summary>
    /// The one decision the nav rail's stuck-open watchdog makes, pulled out of MainWindow so it
    /// can be tested without realizing a window.
    /// </summary>
    internal static class NavRailWatchdogRule
    {
        /// <summary>
        /// Should the watchdog stop believing whatever is holding the rail open and collapse it?
        ///
        /// <para>Every clause is a reason NOT to fire, and they are all "somebody might still be
        /// looking at this": the rail never finished wiring itself, it is already shut, one of its
        /// own popups is genuinely on screen (the pin menu IS why the rail is up), or the pointer
        /// is still on it. Only a rail that is open, unattended and popup-free for the whole grace
        /// window is a rail that is stuck.</para>
        /// </summary>
        /// <param name="ready">The rail finished <c>InitializeNavRail</c>.</param>
        /// <param name="expanded">The rail is currently out over the page.</param>
        /// <param name="popupOpen">One of the rail's own context menus is really open - read off
        /// the menu's <c>IsOpen</c>, never off a counter, so a menu that never raised Closed
        /// cannot disable the watchdog forever.</param>
        /// <param name="pointerAway">The OS cursor is outside the flyout's screen rect.</param>
        /// <param name="awayFor">How long <paramref name="pointerAway"/> has been true.</param>
        /// <param name="grace">How long that has to hold before the rail is declared stuck.</param>
        internal static bool ShouldForceCollapse(
            bool ready, bool expanded, bool popupOpen, bool pointerAway, TimeSpan awayFor, TimeSpan grace)
            => ready && expanded && !popupOpen && pointerAway && awayFor >= grace;
    }
}
