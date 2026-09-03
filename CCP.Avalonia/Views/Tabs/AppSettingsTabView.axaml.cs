using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// Opt-in refresh hook for a Settings section. <see cref="Views.Tabs.AppSettingsTabView"/>
    /// calls <see cref="OnSectionShown"/> on every section that implements this whenever the
    /// Settings door is opened, so sections that have to re-read live state (device lists,
    /// login cards, update status) get a seam without ShowTab knowing their names.
    ///
    /// ponytail: no ported section declares this interface yet - they carry a public
    /// OnSectionShown() but not the `: IAppSettingsSection` - and a port may only add files
    /// under Views/. So <see cref="Views.Tabs.AppSettingsTabView.RefreshSections"/> is a no-op
    /// today; it starts working the moment a section adds the interface, with no change here.
    /// </summary>
    public interface IAppSettingsSection
    {
        /// <summary>Called on the UI thread each time the Settings door becomes visible.</summary>
        void OnSectionShown();
    }
}

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The Settings door (tab key <c>appsettings</c>), PORTED from
    /// ConditioningControlPanel/Views/Tabs/AppSettingsTabView.xaml.cs. A single scrolling page of
    /// nine sections plus a left mini-rail that acts as its table of contents.
    ///
    /// <para><b>This file is the host only.</b> It owns the rail, the scroll, and
    /// <see cref="FocusSection"/> - all view state, all ported for real. It owns no settings
    /// logic; every control lives in its section UserControl under
    /// <c>Views/Controls/AppSettings/</c>, already on this head.</para>
    ///
    /// <para>WPF -&gt; Avalonia API mapping in this file:
    /// <c>TransformToAncestor(a).Transform(p).Y</c> -&gt; <c>TranslatePoint(p, a)?.Y</c>,
    /// <c>ScrollToVerticalOffset(y)</c> -&gt; <c>Offset.WithY(y)</c>,
    /// <c>ActualHeight</c> -&gt; <c>Bounds.Height</c>,
    /// <c>ScrollChangedEventArgs.VerticalChange</c> -&gt; <c>OffsetDelta.Y</c>,
    /// <c>Dispatcher.BeginInvoke</c> -&gt; <c>Dispatcher.UIThread.Post</c>,
    /// <c>Visibility != Visible</c> -&gt; <c>!IsVisible</c>.
    /// The WPF <c>App.Logger</c> calls are dropped: the logger is not on this head and the
    /// catches exist to swallow, not to report.</para>
    /// </summary>
    public partial class AppSettingsTabView : UserControl
    {
        /// <summary>Rail order, and the only section keys <see cref="FocusSection"/> answers to.</summary>
        internal static readonly string[] SectionKeys =
        {
            "general", "audio", "devices", "performance",
            "notifications", "emidesk", "account", "data", "updates",
        };

        /// <summary>Guards the scroll-spy against re-checking a pill that is mid-click.</summary>
        private bool _syncingPills;

        public AppSettingsTabView()
        {
            InitializeComponent();

            foreach (var key in SectionKeys)
            {
                var pill = PillFor(key);
                if (pill != null) pill.Click += SectionPill_Click;
            }
            SectionScroll.ScrollChanged += SectionScroll_ScrollChanged;
        }

        // =====================================================================================
        //  section lookup
        // =====================================================================================

        private Control? SectionElementFor(string? key) => (key ?? string.Empty).ToLowerInvariant() switch
        {
            "general" => SectionGeneral,
            "audio" => SectionAudio,
            "devices" => SectionDevices,
            "performance" => SectionPerformance,
            "notifications" => SectionNotifications,
            "emidesk" => SectionEmidesk,
            "account" => SectionAccount,
            "data" => SectionData,
            "updates" => SectionUpdates,
            _ => null,
        };

        private RadioButton? PillFor(string? key) => (key ?? string.Empty).ToLowerInvariant() switch
        {
            "general" => SectionPillGeneral,
            "audio" => SectionPillAudio,
            "devices" => SectionPillDevices,
            "performance" => SectionPillPerformance,
            "notifications" => SectionPillNotifications,
            "emidesk" => SectionPillEmidesk,
            "account" => SectionPillAccount,
            "data" => SectionPillData,
            "updates" => SectionPillUpdates,
            _ => null,
        };

        // =====================================================================================
        //  public surface — what the shell calls
        // =====================================================================================

        /// <summary>
        /// Scrolls the page to a section and lights its pill. An unknown key is a no-op rather
        /// than a throw, because every caller is a navigation and none of them should be able to
        /// break one. Valid keys: <see cref="SectionKeys"/>.
        /// </summary>
        internal void FocusSection(string? sectionKey)
        {
            try
            {
                var key = (sectionKey ?? string.Empty).ToLowerInvariant();
                var target = SectionElementFor(key);
                if (target == null || SectionScroll == null) return;

                CheckPill(key);

                // Layout first. On the first ever open the stack has never been measured and every
                // section would transform to offset 0. UpdateLayout is synchronous; the deferred
                // retry covers the case where the view is still hidden (nothing to measure yet).
                if (!TryScrollTo(target))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { TryScrollTo(SectionElementFor(key)); }
                        catch { /* the retry is best-effort; a navigation must not throw */ }
                    }, DispatcherPriority.Normal);
                }
            }
            catch { /* see above */ }
        }

        /// <summary>
        /// Per-open refresh: hands every section that implements <c>IAppSettingsSection</c> a
        /// chance to re-read live state. One section throwing must not stop the rest, so each
        /// call is guarded individually. See the interface's ponytail note: no ported section
        /// declares it yet, so this is currently a no-op.
        /// </summary>
        internal void RefreshSections()
        {
            if (SectionStack == null) return;
            foreach (var section in SectionStack.Children
                                                .OfType<Controls.AppSettings.IAppSettingsSection>()
                                                .ToList())
            {
                try { section.OnSectionShown(); }
                catch { /* one bad section must not stop the other eight */ }
            }
        }

        // =====================================================================================
        //  rail behaviour
        // =====================================================================================

        private bool TryScrollTo(Control? target)
        {
            if (target == null || SectionScroll == null || SectionStack == null) return false;

            SectionScroll.UpdateLayout();
            if (SectionStack.Bounds.Height <= 0) return false;

            var y = target.TranslatePoint(new Point(0, 0), SectionStack)?.Y;
            if (y == null) return false;
            SectionScroll.Offset = SectionScroll.Offset.WithY(Math.Max(0, y.Value - 4));
            return true;
        }

        private void CheckPill(string key)
        {
            var pill = PillFor(key);
            if (pill == null) return;
            _syncingPills = true;
            try { pill.IsChecked = true; }
            finally { _syncingPills = false; }
        }

        private void SectionPill_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.Tag is not string key) return;
            FocusSection(key);
        }

        /// <summary>
        /// Scroll spy: the rail follows the reader. No animation, no clock - it only ever moves
        /// a checked state, which is why this is allowed on a quiet surface.
        /// </summary>
        private void SectionScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_syncingPills || Math.Abs(e.OffsetDelta.Y) < 0.5) return;
            if (SectionScroll == null || SectionStack == null) return;
            try
            {
                var offset = SectionScroll.Offset.Y + 24;
                string? current = null;
                foreach (var key in SectionKeys)
                {
                    var el = SectionElementFor(key);
                    if (el == null || !el.IsVisible) continue;
                    var y = el.TranslatePoint(new Point(0, 0), SectionStack)?.Y;
                    if (y == null) continue;
                    if (y <= offset) current = key; else break;
                }
                if (current != null) CheckPill(current);
            }
            catch { /* the spy is cosmetic; never let it break a scroll */ }
        }
    }
}
