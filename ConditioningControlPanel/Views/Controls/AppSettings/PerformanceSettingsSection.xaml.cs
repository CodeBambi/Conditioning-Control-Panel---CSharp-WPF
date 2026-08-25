using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Services.UI;

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
                ChkVideoHwDecode.IsChecked = s.VideoForceHardwareDecoding;

                // MotionLevel's ordinal IS the item index (Full=0, Reduced=1, Off=2) - the same
                // contract MainWindow.Settings.cs:92 relies on. Clamped rather than trusted so a
                // settings file from a future build with a fourth level cannot throw here.
                var index = (int)s.MotionLevel;
                CmbMotionLevel.SelectedIndex =
                    index >= 0 && index < CmbMotionLevel.Items.Count ? index : 0;

                // Do-not-disturb. The textbox is a VIEW of the normalised list, so it is repainted
                // from settings rather than left holding whatever the user last typed - a cloud
                // restore or a factory reset has to be visible here like everywhere else.
                TxtDndProcesses.Text = DoNotDisturbGuard.FormatProcessList(s.DndProcessList);
                ChkDndSuppressVideos.IsChecked = s.DndSuppressVideos;
                ChkDndSuppressFlashes.IsChecked = s.DndSuppressFlashes;
            }
            finally { _isLoading = false; }
        }

        // ============================================================
        // DO NOT DISTURB
        //
        // Unlike the five controls above, these three have no MainWindow twin to forward to - the
        // feature is new and reads straight off App.Settings.Current from the guard, so the handlers
        // write settings themselves. They still save immediately (the same live-editor rule the
        // forwarding handlers follow) because nothing else will.
        // ============================================================

        /// <summary>
        /// Commits the typed list on focus loss. Parsing is the guard's, not this control's: the
        /// picker below writes through the same function, so "VLC.exe" typed by hand and "vlc"
        /// chosen from the list end up as the same stored entry. The box is then repainted from what
        /// was actually stored, which is the user's confirmation that their text was understood.
        /// </summary>
        private void TxtDndProcesses_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var parsed = DoNotDisturbGuard.ParseProcessList(TxtDndProcesses.Text);
            s.DndProcessList = parsed;
            App.Settings?.Save();

            _isLoading = true;
            try { TxtDndProcesses.Text = DoNotDisturbGuard.FormatProcessList(parsed); }
            finally { _isLoading = false; }
        }

        private void ChkDndSuppressVideos_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.DndSuppressVideos = ChkDndSuppressVideos.IsChecked == true;
            App.Settings?.Save();
        }

        private void ChkDndSuppressFlashes_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.DndSuppressFlashes = ChkDndSuppressFlashes.IsChecked == true;
            App.Settings?.Save();
        }

        /// <summary>
        /// The picker. Nobody knows that PotPlayer's executable is called "PotPlayerMini64", so the
        /// button offers every process that currently owns a visible top-level window - roughly what
        /// is in the taskbar - and appends the chosen one to the list.
        ///
        /// <para>A ContextMenu rather than a dialog on purpose: this is a one-tap choice from a
        /// short list, and a whole new Window would be a new XAML surface, a new render test and a
        /// new theming liability for something a menu does correctly.</para>
        /// </summary>
        private void BtnDndPickApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var running = DoNotDisturbGuard.RunningWindowedProcesses();
                var menu = new ContextMenu
                {
                    PlacementTarget = BtnDndPickApp,
                    Placement = PlacementMode.Bottom,
                    MaxHeight = 420
                };

                if (running.Count == 0)
                {
                    menu.Items.Add(new MenuItem
                    {
                        Header = Localization.Loc.Get("set2_dnd_pick_empty"),
                        IsEnabled = false
                    });
                }
                else
                {
                    var already = App.Settings?.Current?.DndProcessList ?? new System.Collections.Generic.List<string>();
                    foreach (var name in running)
                    {
                        var item = new MenuItem { Header = name, IsCheckable = false };
                        // Already listed: shown, but ticked and inert, so the menu reads as the
                        // state of the list rather than as a blind "add" that silently does nothing.
                        if (already.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            item.IsChecked = true;
                            item.IsEnabled = false;
                        }
                        else
                        {
                            var picked = name;
                            item.Click += (_, __) => AddDndProcess(picked);
                        }
                        menu.Items.Add(item);
                    }
                }

                menu.IsOpen = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[DND] app picker failed to open");
            }
        }

        /// <summary>Appends one picked process to the list and repaints the box.</summary>
        private void AddDndProcess(string processName)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            // Re-parse the BOX, not the stored list: an edit the user typed but has not blurred out
            // of yet would otherwise be thrown away by the pick.
            var list = DoNotDisturbGuard.ParseProcessList(TxtDndProcesses.Text);
            var name = DoNotDisturbGuard.Normalize(processName);
            if (name.Length == 0) return;
            if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);

            s.DndProcessList = list;
            App.Settings?.Save();

            _isLoading = true;
            try { TxtDndProcesses.Text = DoNotDisturbGuard.FormatProcessList(list); }
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

        private void ChkVideoHwDecode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.ChkVideoHwDecode_Changed(sender, e);
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
