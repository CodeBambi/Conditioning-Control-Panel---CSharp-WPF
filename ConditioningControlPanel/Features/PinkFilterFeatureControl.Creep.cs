using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Super Creep's two sub-settings (speed, fog cap) on the pink filter page. The card only shows
    /// while Creep is unlocked and switched on; the switch itself belongs to the scaffold lane.
    /// Kept in its own partial so the pink filter's own code-behind stays untouched.
    /// </summary>
    public partial class PinkFilterFeatureControl
    {
        private bool _creepLoading;

        private void CreepCard_Loaded(object sender, RoutedEventArgs e)
        {
            SuperAccess.Changed += OnSuperChanged;
            LoadCreep();
        }

        private void CreepCard_Unloaded(object sender, RoutedEventArgs e)
            => SuperAccess.Changed -= OnSuperChanged;

        private void OnSuperChanged(SuperEffect effect)
        {
            if (effect == SuperEffect.Creep) Dispatcher.BeginInvoke(new Action(LoadCreep));
        }

        private void LoadCreep()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _creepLoading = true;
            try
            {
                CreepCard.Visibility = SuperAccess.IsSelected(SuperEffect.Creep) ? Visibility.Visible : Visibility.Collapsed;
                if (CmbCreepSpeed.Items.Count == 0)
                {
                    // DarkComboBoxStyle has a dark popup, so the items take the light brush.
                    var fg = (System.Windows.Media.Brush)FindResource("TextLightBrush");
                    foreach (var (speed, key) in new[]
                             {
                                 (CreepSpeed.Slow, "super_creep_speed_slow"),
                                 (CreepSpeed.Normal, "super_creep_speed_normal"),
                                 (CreepSpeed.Fast, "super_creep_speed_fast"),
                             })
                        CmbCreepSpeed.Items.Add(new ComboBoxItem { Content = Loc.Get(key), Tag = speed, Foreground = fg });
                }
                foreach (ComboBoxItem item in CmbCreepSpeed.Items)
                    if ((CreepSpeed)item.Tag == s.SuperCreepSpeed) CmbCreepSpeed.SelectedItem = item;
                var cap = CreepFog.ClampCap(s.SuperCreepCap);
                SliderCreepCap.Value = Math.Round(cap * 100);
                TxtCreepCap.Text = $"{Math.Round(cap * 100)}%";
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PinkFilter Creep card: {E}", ex.Message);
            }
            finally { _creepLoading = false; }
        }

        private void CmbCreepSpeed_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_creepLoading || CmbCreepSpeed.SelectedItem is not ComboBoxItem { Tag: CreepSpeed speed }) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SuperCreepSpeed = speed;
            App.Settings?.Save();
        }

        private void SliderCreepCap_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_creepLoading || TxtCreepCap == null) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = Math.Round(e.NewValue);
            TxtCreepCap.Text = $"{v}%";
            s.SuperCreepCap = CreepFog.ClampCap(v / 100.0);
            App.Settings?.Save();
        }
    }
}
