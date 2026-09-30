using System;
using System.Collections.ObjectModel;
using Serilog;

namespace ConditioningControlPanel.Services.Startup
{
    /// <summary>
    /// The Inbox half of the startup presenter: the rows the quiet window parked, newest first, keyed
    /// once. Every head's presenter owns one and does its own thread marshalling; this class is
    /// UI-thread only and never decides routing (<see cref="StartupQueueCore.Route"/> does).
    /// </summary>
    public sealed class StartupInbox
    {
        /// <summary>Rows waiting to be read, newest first.</summary>
        public ObservableCollection<InboxItem> Items { get; } = new();

        /// <summary>How many rows are waiting. Drives the title-bar badge; hidden at zero.</summary>
        public int UnreadCount => Items.Count;

        /// <summary>Raised after a row is added, opened, dismissed or removed.</summary>
        public event Action? Changed;

        public bool Contains(string key)
        {
            foreach (var existing in Items)
                if (string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Files a row at the top. False, and nothing changes, when the key is already there.</summary>
        public bool File(InboxItem item)
        {
            if (item == null || Contains(item.Key)) return false;
            Items.Insert(0, item);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Takes a row back by key without running anything (its surface went away).</summary>
        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(Items[i].Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                Items.RemoveAt(i);
                Changed?.Invoke();
            }
        }

        /// <summary>Removes the row and runs the surface it was holding.</summary>
        public void Open(InboxItem item)
        {
            if (item == null) return;
            Items.Remove(item);
            Changed?.Invoke();
            RunSafely(item.Open, item.Key, "open");
        }

        /// <summary>Removes the row and runs the surface's own dismissal bookkeeping, if any.</summary>
        public void Dismiss(InboxItem item)
        {
            if (item == null) return;
            Items.Remove(item);
            Changed?.Invoke();
            if (item.Dismiss != null) RunSafely(item.Dismiss, item.Key, "dismiss");
        }

        public static void RunSafely(Action action, string key, string what)
        {
            try { action(); }
            catch (Exception ex) { Log.Warning(ex, "[Startup] Inbox {What} failed for '{Key}'", what, key); }
        }
    }
}
