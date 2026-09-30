using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The panel behind the title-bar Inbox glyph. PORTED from ConditioningControlPanel/Controls/InboxFlyout.xaml.cs:
    /// binds straight to the head's <see cref="StartupLadder.Inbox"/>; Open and Dismiss hand back to it.
    /// </summary>
    public partial class InboxFlyout : UserControl
    {
        /// <summary>Raised when a row was acted on, so the host can close the popup around it.</summary>
        public event Action? RequestClose;

        public string OpenLabel { get; } = Str("inbox_open", "Open");
        public string DismissLabel { get; } = Str("inbox_dismiss", "Dismiss");

        public InboxFlyout()
        {
            InitializeComponent();

            TxtInboxTitle.Text = Str("inbox_title", "Inbox");
            TxtInboxEmpty.Text = Str("inbox_empty", "Nothing waiting.");

            var inbox = StartupLadder.Inbox.Items;
            ItemsInbox.ItemsSource = inbox;
            inbox.CollectionChanged += OnInboxChanged;
            Unloaded += (_, _) => inbox.CollectionChanged -= OnInboxChanged;

            RefreshEmptyState();
        }

        private void OnInboxChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshEmptyState();

        private void RefreshEmptyState() => TxtInboxEmpty.IsVisible = StartupLadder.Inbox.UnreadCount == 0;

        private void InboxOpen_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as Control)?.Tag is not InboxItem item) return;
                // Close FIRST: a dialog opened from inside a still-open popup leaves the popup above it.
                RequestClose?.Invoke();
                StartupLadder.Inbox.Open(item);
            }
            catch (Exception ex) { Log.Warning(ex, "Inbox row could not be opened"); }
        }

        private void InboxDismiss_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as Control)?.Tag is not InboxItem item) return;
                StartupLadder.Inbox.Dismiss(item);
                if (StartupLadder.Inbox.UnreadCount == 0) RequestClose?.Invoke();
            }
            catch (Exception ex) { Log.Warning(ex, "Inbox row could not be dismissed"); }
        }

        /// <summary>Localized string with an English fallback: a missing key renders the English, never the key.</summary>
        internal static string Str(string key, string english)
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
