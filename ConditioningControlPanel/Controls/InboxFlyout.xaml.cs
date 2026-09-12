using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The panel behind the title-bar Inbox glyph: the rows the quiet window collected, each with
    /// the surface it is holding.
    ///
    /// <para>It binds straight to <c>App.StartupLadder.Inbox</c> - an ObservableCollection the presenter
    /// owns - so opening or dismissing a row anywhere updates this list and the badge without a
    /// refresh call. The control never decides anything: Open and Dismiss both hand back to the
    /// presenter, which is where the surface's own bookkeeping lives.</para>
    /// </summary>
    public partial class InboxFlyout : UserControl
    {
        /// <summary>Raised when a row was acted on, so the host can close the popup around it.</summary>
        public event Action? RequestClose;

        /// <summary>Row verbs. Bound from inside the item template via RelativeSource, which is
        /// the only way to reach a localized string from a DataTemplate without a converter.</summary>
        public string OpenLabel { get; } = Str("inbox_open", "Open");

        /// <inheritdoc cref="OpenLabel"/>
        public string DismissLabel { get; } = Str("inbox_dismiss", "Dismiss");

        public InboxFlyout()
        {
            InitializeComponent();

            TxtInboxTitle.Text = Str("inbox_title", "Inbox");
            TxtInboxEmpty.Text = Str("inbox_empty", "Nothing waiting.");

            var inbox = App.StartupLadder?.Inbox;
            if (inbox != null)
            {
                ItemsInbox.ItemsSource = inbox;
                inbox.CollectionChanged += OnInboxChanged;
                Unloaded += (_, _) => inbox.CollectionChanged -= OnInboxChanged;
            }

            RefreshEmptyState();
        }

        private void OnInboxChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshEmptyState();

        private void RefreshEmptyState()
        {
            var count = App.StartupLadder?.UnreadCount ?? 0;
            TxtInboxEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void InboxOpen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.Tag is not InboxItem item) return;
                // Close FIRST. Several of these surfaces are modal, and a ShowDialog opened from
                // inside a still-open Popup leaves the popup floating above the dialog.
                RequestClose?.Invoke();
                App.StartupLadder?.OpenItem(item);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Inbox row could not be opened"); }
        }

        private void InboxDismiss_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.Tag is not InboxItem item) return;
                App.StartupLadder?.DismissItem(item);
                if ((App.StartupLadder?.UnreadCount ?? 0) == 0) RequestClose?.Invoke();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Inbox row could not be dismissed"); }
        }

        /// <summary>Localized string with an English fallback, the wizard's Str() pattern: a key
        /// that is missing from a language file renders the English rather than the key.</summary>
        private static string Str(string key, string english)
        {
            try
            {
                var value = Loc.Get(key);
                return string.IsNullOrWhiteSpace(value) || value == key ? english : value;
            }
            catch { return english; }
        }
    }
}
