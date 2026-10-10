// The section page chrome itself (pill strip, breadcrumb, last-tab memory, pill routing, wash, ink,
// title crumb, "Moved" note) lives in MainShellWindow.TabNavigation.cs / NavRail.cs / SectionEdge.cs
// on the 7.1.5 parity nav (Controls/NavRail/SectionTabStrip, Core ConditioningControlPanel.Nav).
// What is left here is the one piece of the sync6 nav port that the parity nav did not have:
// the window-level Ctrl+K palette shortcut (WPF EnsurePaletteShortcut, d858d6108).

using Avalonia.Interactivity;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Ctrl+K from every page (WPF EnsurePaletteShortcut, d858d6108). WPF reads the raw
        /// keystroke before routing so no focused control can swallow it; the window's Tunnel pass
        /// is that point here, and handledEventsToo keeps it whatever a child marks. Ctrl alone
        /// (Ctrl+Alt+K is the camera). ponytail: Avalonia reports no key-repeat flag, so a held chord
        /// re-toggles where WPF took the first press only. The palette has its own Ctrl+K toggle.
        /// </summary>
        private void InitializePaletteShortcut() =>
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != global::Avalonia.Input.Key.K || e.KeyModifiers != global::Avalonia.Input.KeyModifiers.Control) return;
                e.Handled = true;
                SettingsPaletteWindow.Toggle(this);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}
