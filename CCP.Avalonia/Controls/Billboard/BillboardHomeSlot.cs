using System;
using Avalonia;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// The Home cell the Tonight Board lives in (SettingsTabView's centre slot). It only announces
    /// itself: the first time it joins a window, it asks that window to host the deck
    /// (<see cref="Views.Windows.MainShellWindow"/>'s DashboardBillboard partial). This keeps the
    /// shell's lifetime hooks (OnLoaded, OnOpened) untouched. It sits in the Controls namespace
    /// because Home's XAML may only name namespaces its root declares (fx: = Controls).
    /// </summary>
    public sealed class BillboardHomeSlot : Grid
    {
        /// <summary>Raised once per window the slot joins. Tests listen here.</summary>
        public static event Action<BillboardHomeSlot, TopLevel>? Joined;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            try
            {
                if (TopLevel.GetTopLevel(this) is not { } top) return;
                if (top is Views.Windows.MainShellWindow shell) shell.AttachDashboardBillboard(this);
                Joined?.Invoke(this, top);
            }
            catch (Exception ex) { Log.Warning(ex, "Dashboard billboard: slot attach failed"); }
        }
    }
}
