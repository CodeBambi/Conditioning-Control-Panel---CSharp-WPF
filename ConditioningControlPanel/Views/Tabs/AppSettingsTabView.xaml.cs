using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// Opt-in refresh hook for a Settings section. <see cref="Views.Tabs.AppSettingsTabView"/>
    /// calls <see cref="OnSectionShown"/> on every section that implements this whenever the
    /// Settings door is opened, so sections that have to re-read live state (device lists,
    /// login cards, update status) get a seam without ShowTab knowing their names.
    ///
    /// Deliberately optional: a section that just binds settings implements nothing and is
    /// skipped. Declared here rather than in a section file so no section agent owns it.
    /// </summary>
    public interface IAppSettingsSection
    {
        /// <summary>Called on the UI thread each time the Settings door becomes visible.</summary>
        void OnSectionShown();
    }
}

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Settings door (tab key <c>appsettings</c>). A single scrolling page of eight
    /// sections plus a left mini-rail that acts as its table of contents.
    ///
    /// <para><b>This file is the host only.</b> It owns the rail, the scroll, and
    /// <see cref="FocusSection"/>. It deliberately owns no settings logic: every control lives
    /// in its section UserControl under <c>Views/Controls/AppSettings/</c>, and every handler
    /// still delegates to the MainWindow partial that always owned it, through the same
    /// <c>Window.GetWindow(this) is MainWindow mw</c> idiom the other tab views use.</para>
    ///
    /// <para><b>Passthroughs go in partial files, not here.</b> Eight agents build eight
    /// sections; if all eight added their compat properties to this file they would collide on
    /// every line. Each section instead contributes
    /// <c>Views/Tabs/AppSettingsTabView.&lt;Section&gt;.cs</c> — a partial of this class holding
    /// nothing but <c>internal &lt;Type&gt; Name =&gt; SectionAudio.Name;</c> style passthroughs
    /// for the x:Names its MainWindow partials read and write (Companion Workshop-cell
    /// precedent, <see cref="CompanionTabView"/>).</para>
    /// </summary>
    public partial class AppSettingsTabView : UserControl
    {
        /// <summary>Rail order, and the only section keys <see cref="FocusSection"/> answers to.</summary>
        internal static readonly string[] SectionKeys =
        {
            "general", "audio", "devices", "performance",
            "notifications", "account", "data", "updates",
        };

        /// <summary>Guards the scroll-spy against re-checking a pill that is mid-click.</summary>
        private bool _syncingPills;

        public AppSettingsTabView()
        {
            InitializeComponent();
        }

        // =====================================================================================
        //  section lookup
        // =====================================================================================

        private FrameworkElement? SectionElementFor(string? key) => (key ?? string.Empty).ToLowerInvariant() switch
        {
            "general" => SectionGeneral,
            "audio" => SectionAudio,
            "devices" => SectionDevices,
            "performance" => SectionPerformance,
            "notifications" => SectionNotifications,
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
            "account" => SectionPillAccount,
            "data" => SectionPillData,
            "updates" => SectionPillUpdates,
            _ => null,
        };

        // =====================================================================================
        //  public surface — what MainWindow calls
        // =====================================================================================

        /// <summary>
        /// Scrolls the page to a section and lights its pill. The account chip, the retired
        /// <c>ShowTab("patreon")</c> redirect, TierGate's "see tiers" action and the Ctrl+K
        /// palette all land here; an unknown key is a no-op rather than a throw, because every
        /// one of those callers is a navigation and none of them should be able to break one.
        /// Valid keys: <see cref="SectionKeys"/>.
        /// </summary>
        internal void FocusSection(string? sectionKey)
        {
            try
            {
                var key = (sectionKey ?? string.Empty).ToLowerInvariant();
                var target = SectionElementFor(key);
                if (target == null || SectionScroll == null) return;

                CheckPill(key);

                // Layout first. ShowTab flips Visibility and calls straight through, so on the
                // first ever open the stack has never been measured and every section would
                // transform to offset 0. UpdateLayout is synchronous; the deferred retry below
                // covers the case where the view is still collapsed (nothing to measure yet).
                if (!TryScrollTo(target))
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        try { TryScrollTo(SectionElementFor(key)); }
                        catch (Exception ex) { App.Logger?.Debug("FocusSection retry: {E}", ex.Message); }
                    }));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FocusSection({Key}) failed: {E}", sectionKey, ex.Message);
            }
        }

        /// <summary>
        /// Per-open refresh: hands every section that implements
        /// <c>IAppSettingsSection</c> a chance to re-read live state.
        /// Called from ShowTab's <c>appsettings</c> case. One section throwing must not stop the
        /// rest, so each call is guarded individually.
        /// </summary>
        internal void RefreshSections()
        {
            if (SectionStack == null) return;
            foreach (var section in SectionStack.Children
                                                .OfType<ConditioningControlPanel.Views.Controls.AppSettingsSections.IAppSettingsSection>()
                                                .ToList())
            {
                try { section.OnSectionShown(); }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "AppSettings section refresh failed: {Section}",
                                        section.GetType().Name);
                }
            }
        }

        // =====================================================================================
        //  rail behaviour
        // =====================================================================================

        private bool TryScrollTo(FrameworkElement? target)
        {
            if (target == null || SectionScroll == null || SectionStack == null) return false;

            SectionScroll.UpdateLayout();
            if (SectionStack.ActualHeight <= 0) return false;

            var y = target.TransformToAncestor(SectionStack).Transform(new Point(0, 0)).Y;
            SectionScroll.ScrollToVerticalOffset(Math.Max(0, y - 4));
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

        private void SectionPill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.Tag is not string key) return;
            FocusSection(key);
        }

        /// <summary>
        /// Scroll spy: the rail follows the reader. No animation, no clock - it only ever moves
        /// a checked state, which is why this is allowed on a quiet surface.
        /// </summary>
        private void SectionScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingPills || Math.Abs(e.VerticalChange) < 0.5) return;
            if (SectionScroll == null || SectionStack == null) return;
            try
            {
                var offset = SectionScroll.VerticalOffset + 24;
                string? current = null;
                foreach (var key in SectionKeys)
                {
                    var el = SectionElementFor(key);
                    if (el == null || el.Visibility != Visibility.Visible) continue;
                    var y = el.TransformToAncestor(SectionStack).Transform(new Point(0, 0)).Y;
                    if (y <= offset) current = key; else break;
                }
                if (current != null) CheckPill(current);
            }
            catch (Exception ex) { App.Logger?.Debug("Settings scroll-spy: {E}", ex.Message); }
        }
    }
}
