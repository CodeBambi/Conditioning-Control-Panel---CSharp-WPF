using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS ▸ PERFORMANCE. Rebuilt from the unreachable LegacyDashboardHost copies
    /// (gap-report G12: "must be rebuilt, not moved"). Pure forwarding, like every other
    /// re-parented cell — the four writes still happen in MainWindow.UiUpdates.cs, this
    /// control only gives them a reachable surface again.
    ///
    /// Two things that are easy to get wrong and silent when you do:
    ///
    /// 1. <b>Seeding must not look like a user edit.</b> The section is created with the rest of
    ///    MainWindow, before <c>App.Settings</c> is guaranteed to exist, and it re-seeds every time
    ///    the Settings door opens. Assigning IsChecked / SelectedIndex raises the same events a
    ///    click does, so every assignment happens inside the <c>_isLoading</c> guard — the same
    ///    idiom Features/SystemFeatureControl.xaml.cs uses. <c>_isLoading</c> starts <c>true</c> so
    ///    nothing can fire between InitializeComponent and the first seed.
    ///
    /// 2. <b>These handlers do not persist by themselves.</b> The MainWindow methods write
    ///    <c>App.Settings.Current</c> and stop there — they were written for controls that only
    ///    ever got their values from <c>LoadSettings</c>, so <c>SaveSettings</c> did the writing.
    ///    A live editor has to save (the "velvet-mosaic" popup rule, see the comment at
    ///    MainWindow.Settings.cs:388 and every handler in SystemFeatureControl), hence the
    ///    <c>App.Settings?.Save()</c> after each forward.
    ///
    /// Until Phase 8 deletes LegacyDashboardHost, the dead twins still mirror these four values
    /// through LoadSettings/SaveSettings. That is safe in this direction: <c>SaveSettings</c> calls
    /// <c>LoadSettings</c> first (MainWindow.Settings.cs:395), so the stale twin is re-seeded from
    /// settings before it is read back and cannot clobber an edit made here.
    /// </summary>
    public partial class PerformanceSettingsSection : UserControl
    {
        private bool _isLoading = true;

        public PerformanceSettingsSection()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings != null) App.Settings.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings != null) App.Settings.CurrentReplaced -= OnCurrentReplaced;
        }

        /// <summary>
        /// A cloud restore swaps the whole <c>Current</c> instance out from under us. Every tab view
        /// stays alive for the app's lifetime, so without this the page would keep showing the
        /// pre-restore values until the next time it became visible.
        /// </summary>
        private void OnCurrentReplaced()
        {
            // DispatcherPriority.Normal (the default) on purpose - Loaded priority gets starved.
            Dispatcher.BeginInvoke(new Action(SyncFromSettings));
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible && visible) SyncFromSettings();
        }

        /// <summary>
        /// Paints the four controls from settings without raising an edit. Safe to call at any
        /// time, including before <c>App.Settings</c> exists (render tests, early startup).
        /// </summary>
        internal void SyncFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            _isLoading = true;
            try
            {
                ChkPerformanceMode.IsChecked = s.PerformanceMode;
                ChkAutoPerformance.IsChecked = s.AutoPerformanceMode;
                ChkUnifiedOverlay.IsChecked = s.UnifiedOverlayHost;

                // MotionLevel's ordinal IS the item index (Full=0, Reduced=1, Off=2) - the same
                // contract MainWindow.Settings.cs:92 relies on. Clamped rather than trusted so a
                // settings file from a future build with a fourth level cannot throw here.
                var index = (int)s.MotionLevel;
                CmbMotionLevel.SelectedIndex =
                    index >= 0 && index < CmbMotionLevel.Items.Count ? index : 0;
            }
            finally { _isLoading = false; }
        }

        private void ChkPerformanceMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.ChkPerformanceMode_Changed(sender, e);
                App.Settings?.Save();
            }
        }

        private void ChkAutoPerformance_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.ChkAutoPerformance_Changed(sender, e);
                App.Settings?.Save();
            }
        }

        private void ChkUnifiedOverlay_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.ChkUnifiedOverlay_Changed(sender, e);
                App.Settings?.Save();
            }
        }

        /// <summary>
        /// The motion kill-switch. MainWindow.CmbMotionLevel_SelectionChanged is what actually
        /// stops the running ambient loops when the level drops - without this shim the rebuilt
        /// combo would look alive and do nothing.
        /// </summary>
        private void CmbMotionLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.CmbMotionLevel_SelectionChanged(sender, e);
                App.Settings?.Save();
            }
        }
    }
}
