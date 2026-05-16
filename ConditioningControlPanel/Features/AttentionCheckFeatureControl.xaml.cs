using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Settings UI for the Attention-Check mechanic. Self-contained
    /// UserControl — drop into the Settings tab (or wherever placement
    /// lands during voice-pass). Bindings follow the same pattern as
    /// FlashFeatureControl / VideoFeatureControl.
    /// </summary>
    public partial class AttentionCheckFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public AttentionCheckFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadFromSettings();
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.AttentionCheckEnabled;
                SliderMin.Value = s.AttentionCheckMinPerSession;
                TxtMin.Text = s.AttentionCheckMinPerSession.ToString();
                SliderMax.Value = s.AttentionCheckMaxPerSession;
                TxtMax.Text = s.AttentionCheckMaxPerSession.ToString();
                SliderGrace.Value = s.AttentionCheckGraceMs;
                TxtGrace.Text = $"{s.AttentionCheckGraceMs} ms";
                CmbFailMode.SelectedIndex = s.AttentionCheckFailMode switch
                {
                    AppSettings.AttentionCheckFailModeKind.XpPenalty => 0,
                    AppSettings.AttentionCheckFailModeKind.LockCard => 1,
                    _ => 2,
                };
                CmbScope.SelectedIndex = s.AttentionCheckScope == AppSettings.AttentionCheckScopeKind.Always ? 0 : 1;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AppSettings.AttentionCheckEnabled)
                or nameof(AppSettings.AttentionCheckMinPerSession)
                or nameof(AppSettings.AttentionCheckMaxPerSession)
                or nameof(AppSettings.AttentionCheckGraceMs)
                or nameof(AppSettings.AttentionCheckFailMode)
                or nameof(AppSettings.AttentionCheckScope))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AttentionCheckEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtMin.Text = v.ToString();
            s.AttentionCheckMinPerSession = v;
            // Auto-bump max so the random-interval math doesn't end up with min > max.
            if (s.AttentionCheckMaxPerSession < v)
            {
                s.AttentionCheckMaxPerSession = v;
                SliderMax.Value = v;
                TxtMax.Text = v.ToString();
            }
            App.Settings?.Save();
        }

        private void SliderMax_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtMax.Text = v.ToString();
            s.AttentionCheckMaxPerSession = v;
            if (s.AttentionCheckMinPerSession > v)
            {
                s.AttentionCheckMinPerSession = v;
                SliderMin.Value = v;
                TxtMin.Text = v.ToString();
            }
            App.Settings?.Save();
        }

        private void SliderGrace_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtGrace.Text = $"{v} ms";
            s.AttentionCheckGraceMs = v;
            App.Settings?.Save();
        }

        private void CmbFailMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AttentionCheckFailMode = CmbFailMode.SelectedIndex switch
            {
                1 => AppSettings.AttentionCheckFailModeKind.LockCard,
                2 => AppSettings.AttentionCheckFailModeKind.None,
                _ => AppSettings.AttentionCheckFailModeKind.XpPenalty,
            };
            App.Settings?.Save();
        }

        private void CmbScope_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AttentionCheckScope = CmbScope.SelectedIndex == 1
                ? AppSettings.AttentionCheckScopeKind.DuringSessionsOnly
                : AppSettings.AttentionCheckScopeKind.Always;
            App.Settings?.Save();
        }

    }
}
