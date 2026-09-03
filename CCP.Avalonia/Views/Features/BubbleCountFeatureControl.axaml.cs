using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Bubble Count settings panel, ported from the WPF head. Every editor reads and writes
    /// <see cref="CoreSettings.Current"/> and persists through <see cref="CoreSettings.Save"/>,
    /// which is the <c>App.Settings.Current</c> / <c>App.Settings.Save()</c> pair the WPF
    /// code-behind used, one for one.
    ///
    /// <para>The WPF <c>SettingsHook</c> / <c>ISettingsRebindable</c> pair is inlined here rather
    /// than shared: that interface lives in the WPF head and minting a twin is not this file's
    /// job. It buys the same thing it bought there - a cloud restore SWAPS the settings instance,
    /// and this control is rack-mounted for the whole session, so the hook must follow the swap or
    /// the panel edits a discarded object. <see cref="RebindToCurrentSettings"/> stays public so
    /// the rack can fan a restore out over it.</para>
    ///
    /// <para>Still head-side: the BubbleCount service (start/stop/reschedule/trigger) and the
    /// mod-aware feature art. Both are named at their call sites.</para>
    /// </summary>
    public partial class BubbleCountFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private AppSettings? _hooked;

        public BubbleCountFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderFreq.ValueChanged += SliderFreq_Changed;
            CmbDifficulty.SelectionChanged += CmbDifficulty_Changed;
            ChkStrict.IsCheckedChanged += ChkStrict_Changed;
            BtnTest.Click += BtnTest_Click;

            Loaded += (_, _) => RebindToCurrentSettings();
            Unloaded += (_, _) => Unhook();

            // ponytail: WPF also repaints the hero and side plates on ModChanged through
            // Services/ModResourceResolver.cs (WPF head) from Resources/features/Bubble_count.png.
            // The Avalonia .axaml drops both plates deliberately, so nothing here to repaint yet.

            RebindToCurrentSettings();
        }

        /// <summary>Re-points the settings hook at the live instance and repaints from it.</summary>
        public void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BubbleCountEnabled;
                SliderFreq.Value = s.BubbleCountFrequency;
                TxtFreq.Text = s.BubbleCountFrequency.ToString();
                // Select matching ComboBoxItem by Tag
                foreach (var obj in CmbDifficulty.Items)
                {
                    if (obj is ComboBoxItem item && item.Tag is string tag &&
                        int.TryParse(tag, out var val) && val == s.BubbleCountDifficulty)
                    {
                        CmbDifficulty.SelectedItem = item;
                        break;
                    }
                }
                ChkStrict.IsChecked = s.BubbleCountStrictLock;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.BubbleCountEnabled) ||
                e.PropertyName == nameof(AppSettings.BubbleCountFrequency) ||
                e.PropertyName == nameof(AppSettings.BubbleCountDifficulty) ||
                e.PropertyName == nameof(AppSettings.BubbleCountStrictLock))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkEnable.IsChecked ?? false;
            if (s.BubbleCountEnabled == on) return;   // a seeding echo, not a user edit
            s.BubbleCountEnabled = on;
            CoreSettings.Save();

            // ponytail: WPF live-applies here - App.BubbleCount.Start()/Stop(), gated on
            // App.IsEngineRunning. BubbleCountService (ConditioningControlPanel/Services/) and
            // App.IsEngineRunning (ConditioningControlPanel/App.xaml.cs) are both still head-side.
        }

        private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            if (s.BubbleCountFrequency == v) return;
            s.BubbleCountFrequency = v;
            // ponytail: WPF then calls App.BubbleCount.RefreshSchedule() so a live schedule picks
            // the new frequency up - BubbleCountService, still in the WPF head.
            CoreSettings.Save();
        }

        private void CmbDifficulty_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbDifficulty.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out var difficulty))
            {
                var s = CoreSettings.Current;
                if (s.BubbleCountDifficulty == difficulty) return;
                s.BubbleCountDifficulty = difficulty;
                CoreSettings.Save();
            }
        }

        /// <summary>
        /// Strict mode is one of the restrictive locks, so switching it ON has to clear the
        /// acknowledgement-gated double warning first. A decline puts the box back rather than
        /// leaving it visually on while the setting says off.
        /// </summary>
        private async void ChkStrict_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkStrict.IsChecked ?? false;
            if (s.BubbleCountStrictLock == on) return;

            if (on)
            {
                // Avalonia's ShowDialog is async and needs a real owner Window. With no owner the
                // honest answer is "not confirmed", so the box reverts rather than arming a lock
                // the user never acknowledged.
                var owner = TopLevel.GetTopLevel(this) as Window;
                var confirmed = owner != null && await Dialogs.WarningDialog.ShowDoubleWarningAsync(owner,
                    "Strict Bubble Count",
                    "• You will NOT be able to skip the bubble count challenge\n" +
                    "• You MUST answer correctly to dismiss\n" +
                    "• Wrong answers force you to REWATCH the video\n" +
                    "• Mercy system grants escape after 3 retries (if enabled)\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    // Back on the UI thread after the await, so WPF's BeginInvoke hop is gone.
                    _isLoading = true;
                    ChkStrict.IsChecked = false;
                    _isLoading = false;
                    return;
                }
            }

            s.BubbleCountStrictLock = on;
            CoreSettings.Save();
            Log.Information("Bubble count strict lock set to {Enabled}", on);
        }

        private void BtnTest_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.BubbleCount.TriggerGame(forceTest: true) - BubbleCountService
            // (ConditioningControlPanel/Services/), still in the WPF head.
        }
    }
}
