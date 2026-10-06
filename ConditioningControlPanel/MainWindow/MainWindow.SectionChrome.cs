using System;

namespace ConditioningControlPanel
{
    // Nav rework 2026-10-06, TABSTRIP lane: the section page chrome (pill strip, breadcrumb,
    // window title, last-tab memory, the "Moved" toast) that ShowTab syncs on every navigation.
    public partial class MainWindow
    {
        /// <summary>The ShowTab key on screen (after redirects). Null before the first ShowTab.</summary>
        private string? _navCurrentTab;

        /// <summary>Scroll the Play wall to a zone: "games" | "sessions" | "eyes". Implemented by
        /// the REHOME lane (PlayTab.ScrollToZone); until then the pill lands on the wall's top.</summary>
        partial void ScrollPlayZone(string zone);

        /// <summary>Sync the strip, breadcrumb, title and last-tab memory to the tab on screen.</summary>
        private void SyncSectionChrome(string tab)
        {
        }
    }
}
