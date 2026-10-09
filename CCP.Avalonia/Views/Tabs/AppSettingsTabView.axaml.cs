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
    /// General and Account (Account &amp; Plans) declare it; the host calls it each time the page
    /// becomes visible, as WPF ShowTab's "appsettings" case does.
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
    /// ten sections plus a left mini-rail that acts as its table of contents.
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
    /// The WPF <c>App.Logger</c> calls are dropped because the catches exist to swallow, not to
    /// report - NOT because there is no logger: Serilog's static <c>Log</c> is this head's
    /// replacement for <c>App.Logger</c> and every other file here uses it.</para>
    /// </summary>
    public partial class AppSettingsTabView : UserControl
    {
        /// <summary>Rail order, and the only section keys <see cref="FocusSection"/> answers to.</summary>
        internal static readonly string[] SectionKeys =
        {
            "general", "audio", "devices", "monitors", "performance",
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
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                // WPF ShowTab's "appsettings" case: sections that re-read live state get their seam.
                if (IsVisible) RefreshSections();
                SyncPlansMotion();
            };
        }

        // =====================================================================================
        //  section lookup
        // =====================================================================================

        private Control? SectionElementFor(string? key) => (key ?? string.Empty).ToLowerInvariant() switch
        {
            "general" => SectionGeneral,
            "audio" => SectionAudio,
            "devices" => SectionDevices,
            "monitors" => SectionMonitors,
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
            "monitors" => SectionPillMonitors,
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
                // Even a "successful" first try can be clamped back to 0: a page that has just become
                // visible reports its old (empty) extent until the next layout pass, and the ScrollViewer
                // coerces the offset into it (desk 2026-10-09: Sign in landed on General with the
                // Account pill lit). Re-apply after layout and once more after the late sections grow.
                TryScrollTo(target);
                foreach (var delay in new[] { 0, 250 })
                {
                    DispatcherTimer.RunOnce(() =>
                    {
                        try { TryScrollTo(SectionElementFor(key)); }
                        catch { /* the retry is best-effort; a navigation must not throw */ }
                    }, TimeSpan.FromMilliseconds(delay), DispatcherPriority.Background);
                }
            }
            catch { /* see above */ }
        }

        /// <summary>
        /// Per-open refresh: hands every section that implements <c>IAppSettingsSection</c> a
        /// chance to re-read live state. One section throwing must not stop the rest, so each
        /// call is guarded individually.
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

        /// <summary>The section the lit pill names: the one the reader is on (scroll spy or click).
        /// WPF 7.1.5: Settings is ONE scrolling page, so anything gated on a section's IsVisible is
        /// on for every section; gate on this instead.</summary>
        internal string? CurrentSectionKey { get; private set; } = "general";

        private void CheckPill(string key)
        {
            var pill = PillFor(key);
            if (pill == null) return;
            _syncingPills = true;
            try { pill.IsChecked = true; }
            finally { _syncingPills = false; }

            // Account & Plans runs the vault's ambient motion, and Settings is ONE scrolling page:
            // the plans copy reads "visible" on every section. So the motion follows the lit pill
            // instead (WPF 7.1.5 review fix): on while the reader is on Account, parked elsewhere.
            var was = CurrentSectionKey;
            CurrentSectionKey = key;
            if ((was == "account") != (key == "account")) SyncPlansMotion();
        }

        /// <summary>Starts the Account &amp; Plans room while its pill is lit and the page is
        /// on screen, parks it otherwise. Called on a pill change and on every page show/hide.</summary>
        internal void SyncPlansMotion()
        {
            try { SectionAccount?.SetPlansMotion(IsVisible && CurrentSectionKey == "account"); }
            catch { /* cosmetic: a navigation must never throw */ }
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
