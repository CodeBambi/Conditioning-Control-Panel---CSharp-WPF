using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class BambiTakeoverTabView : UserControl
    {
        public BambiTakeoverTabView()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadMantraChant();
        }

        // ── Mantra Chant (suggestion #653) ────────────────────────────────────────
        // Unique to this tab (no Takeover twin to mirror), so its controls are wired directly here
        // instead of delegating to MainWindow like the shared voice toggles above.

        private bool _mantraChantLoading;

        /// <summary>Populate the chant controls from settings + refresh the voiced-content note. Runs on tab show.</summary>
        private void LoadMantraChant()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _mantraChantLoading = true;
            try
            {
                ChkMantraChant.IsChecked = s.MantraChantEnabled;
                SldMantraChantVolume.Value = s.MantraChantVolume;
                TxtMantraChantVolume.Text = $"{(int)Math.Round(s.MantraChantVolume)}%";
                SldMantraChantGap.Value = s.MantraChantGapSeconds;
                TxtMantraChantGap.Text = $"{s.MantraChantGapSeconds}s";
                RefreshMantraChantHint();
            }
            finally { _mantraChantLoading = false; }
        }

        /// <summary>A mod with no voiced mantras can't chant, so say so rather than looping silence.</summary>
        private void RefreshMantraChantHint()
        {
            if (TxtMantraChantHint == null) return;
            var voiced = App.MantraChant?.CanChant() == true;
            TxtMantraChantHint.Text = Loc.Get(voiced ? "desc_mantra_chant" : "desc_mantra_chant_none");
        }

        private void ChkMantraChant_Changed(object sender, RoutedEventArgs e)
        {
            if (_mantraChantLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MantraChantEnabled = ChkMantraChant.IsChecked == true;
            App.Settings?.Save();
            if (s.MantraChantEnabled) App.MantraChant?.Start();
            else App.MantraChant?.Stop();
            RefreshMantraChantHint();
        }

        private void SldMantraChantVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mantraChantLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MantraChantVolume = e.NewValue;
            App.Settings?.Save();
            if (TxtMantraChantVolume != null) TxtMantraChantVolume.Text = $"{(int)Math.Round(e.NewValue)}%";
            App.MantraChant?.ApplyVolume(); // live-apply to a clip that's already playing
        }

        private void SldMantraChantGap_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mantraChantLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MantraChantGapSeconds = (int)Math.Round(e.NewValue);
            App.Settings?.Save();
            if (TxtMantraChantGap != null) TxtMantraChantGap.Text = $"{s.MantraChantGapSeconds}s";
        }

        private void BtnAutonomyStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAutonomyStartStop_Click(sender, e);
        }
        private void BtnForceStartAutonomy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnForceStartAutonomy_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void BtnTestAutonomy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestAutonomy_Click(sender, e);
        }
        private void BtnTestVoice_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestVoice_Click(sender, e);
        }
        private void ChkAutonomyVoice_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyVoice_Changed(sender, e);
        }
        private void ChkAutonomyResume_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyResume_Changed(sender, e);
        }
        private void ChkShowTakeoverCountdown_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkShowTakeoverCountdown_Changed(sender, e);
        }
        private void ChkSpeechWakeWord_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkSpeechWakeWord_Changed(sender, e);
        }
        private void TxtSpeechWakeWords_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtSpeechWakeWords_LostFocus(sender, e);
        }
        private void ChkSpeechPushToTalk_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkSpeechPushToTalk_Changed(sender, e);
        }
        private void BtnSetPttKey_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSetPttKey_Click(sender, e);
        }
        private void ChkAutonomyBehavior_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyBehavior_Changed(sender, e);
        }
        private void ChkAutonomyEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyEnabled_Changed(sender, e);
        }
        private void ChkAutonomyIdle_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyIdle_Changed(sender, e);
        }
        private void ChkAutonomyRandom_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyRandom_Changed(sender, e);
        }
        private void ChkAutonomyTimeAware_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutonomyTimeAware_Changed(sender, e);
        }
        private void SliderAutonomyAnnounce_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyAnnounce_Changed(sender, e);
        }
        private void SliderAutonomyCooldown_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyCooldown_Changed(sender, e);
        }
        private void SliderAutonomyIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyIntensity_Changed(sender, e);
        }
        private void SliderAutonomyInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAutonomyInterval_Changed(sender, e);
        }
    }
}
