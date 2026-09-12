using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The title bar's half of the startup Inbox: a glyph with a count, and the popup behind it.
    ///
    /// <para>The list itself lives on <c>App.StartupLadder</c> (the presenter owns it, and the surfaces
    /// post to it from all over the app). This file only paints: it mirrors the count onto the
    /// button, shows the button when there is something to see, and hides it again when the last
    /// row is gone.</para>
    /// </summary>
    public partial class MainWindow
    {
        private Popup? _inboxPopup;

        /// <summary>
        /// Wires the badge to the presenter's collection. Called once from the constructor; safe
        /// when there is no presenter (the badge simply never appears).
        /// </summary>
        private void InitializeInboxBadge()
        {
            try
            {
                var startup = App.StartupLadder;
                if (startup == null || BtnInbox == null) return;

                startup.Inbox.CollectionChanged += OnInboxCollectionChanged;
                RefreshInboxBadge();
            }
            catch (Exception ex) { App.Logger?.Debug("Inbox badge init: {E}", ex.Message); }
        }

        private void OnInboxCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshInboxBadge),
                    System.Windows.Threading.DispatcherPriority.Normal);
                return;
            }
            RefreshInboxBadge();
        }

        private void RefreshInboxBadge()
        {
            try
            {
                if (BtnInbox == null) return;
                var count = App.StartupLadder?.UnreadCount ?? 0;

                // Tag is what the button's template binds its badge digits to - the badge lives
                // inside a ControlTemplate, so there is no named element to reach from here.
                BtnInbox.Tag = count > 99 ? "99+" : count.ToString();
                BtnInbox.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
                BtnInbox.ToolTip = InboxStr("inbox_tooltip", "Things that were waiting for a quieter moment");

                if (count == 0) CloseInboxPopup();
            }
            catch (Exception ex) { App.Logger?.Debug("Inbox badge refresh: {E}", ex.Message); }
        }

        private void BtnInbox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_inboxPopup?.IsOpen == true) { CloseInboxPopup(); return; }

                var flyout = new InboxFlyout();
                flyout.RequestClose += CloseInboxPopup;

                // AllowsTransparency deliberately false. A layered popup shares WPF's single
                // render thread with the avatar tube, and layered windows opening and closing
                // against that animation are the documented render-thread deadlock in this app
                // (the same reason every ComboBox dropdown is de-layered in App.OnStartup).
                _inboxPopup = new Popup
                {
                    Child = flyout,
                    PlacementTarget = BtnInbox,
                    Placement = PlacementMode.Bottom,
                    HorizontalOffset = -300,
                    VerticalOffset = 6,
                    StaysOpen = false,
                    AllowsTransparency = false,
                    PopupAnimation = PopupAnimation.None,
                };
                _inboxPopup.Closed += (_, _) => _inboxPopup = null;
                _inboxPopup.IsOpen = true;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Inbox could not be opened"); }
        }

        private void CloseInboxPopup()
        {
            try
            {
                if (_inboxPopup == null) return;
                _inboxPopup.IsOpen = false;
                _inboxPopup = null;
            }
            catch (Exception ex) { App.Logger?.Debug("Inbox popup close: {E}", ex.Message); }
        }

        /// <summary>Localized string with an English fallback - a key missing from a language
        /// file renders the English, never the key.</summary>
        private static string InboxStr(string key, string english)
        {
            try
            {
                var value = Loc.Get(key);
                return string.IsNullOrWhiteSpace(value) || value == key ? english : value;
            }
            catch { return english; }
        }

        /// <summary>
        /// Hands a passive surface to the presenter: shown now if nothing is quiet, parked as an
        /// Inbox row if something is. Falls back to opening it directly when there is no presenter,
        /// so no surface can go missing because the ladder was not built.
        /// </summary>
        private static void PresentOrInbox(Services.Startup.InboxItem item)
        {
            var startup = App.StartupLadder;
            if (startup != null) { startup.PresentOrInbox(item); return; }
            try { item.Open(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Surface '{Key}' failed to open", item.Key); }
        }

        /// <summary>
        /// One line of a surface's body text for an Inbox row. A row is a list entry, not the
        /// message itself - the whole message is one click away, in the window that was always
        /// going to show it.
        /// </summary>
        private static string Summarise(string? body, int max = 90)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            var flat = body.Replace("\r", " ").Replace("\n", " ").Trim();
            while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
            return flat.Length <= max ? flat : flat.Substring(0, max - 1).TrimEnd() + "…";
        }

        /// <summary>
        /// Queues a modal startup surface on the ladder. Falls back to a plain Normal-priority
        /// post when there is no presenter.
        /// </summary>
        private void EnqueueStartupModal(string key, int priority, Action<Window?> show, Action? onAbandoned = null)
        {
            var startup = App.StartupLadder;
            if (startup != null) { startup.EnqueueModal(key, priority, show, onAbandoned); return; }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { show(this); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Startup surface '{Key}' failed", key); }
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }
    }
}
