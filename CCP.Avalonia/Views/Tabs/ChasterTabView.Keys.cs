// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: 4. the keys, Preset_Click
// and RefreshPresets (:1075-1124), Stakes (ChasterTabView.Help.cs:52), on Core TabPresets.
// A key rewrites the price list and requests the preset's limits, as WPF; it never touches the lock
// itself (prices only book when an event happens), so it does not go through the import confirm.
// The click also pushes the new set onto the menu rows and the limits card, as WPF.
// ponytail: FxPreset/FxKeyTurned (Fx.cs) are not ported.
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private void KeysInit()
        {
            foreach (var key in new[] { BtnPresetGentle, BtnPresetStrict, BtnPresetCirce, BtnPresetCustom })
                key.Click += Preset_Click;
            RefreshPresets();
        }

        private void Preset_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as ToggleButton)?.Tag is not string id) return;
            // The fourth key mirrors a hand-built set; pressing it rewrites nothing.
            if (id != TabPresets.Custom && TabPresets.Apply(id) is { Count: > 0 } ids)
            {
                var settings = CoreSettings.Current;
                settings.ChasterPrices = new List<string>(ids);
                // The preset sets the stakes too: a lower limit now, a higher one after its day.
                if (TabPresets.Find(id) is { } preset)
                    (settings.ChasterDayLimit, settings.ChasterBacklogLimit) =
                        TabPresets.RequestLimits(preset, settings.ChasterDayLimit, settings.ChasterBacklogLimit, DateTime.UtcNow);
                CoreSettings.Save();
                ApplyPriceToggles();
                RefreshLimits();
                RefreshNumbers();
            }
            RefreshPresets();
        }

        /// <summary>Light the key for the set that is on. The fourth key lights for a hand-built set.</summary>
        internal void RefreshPresets()
        {
            var match = TabPresets.Match(CoreSettings.Current.ChasterPrices);
            BtnPresetGentle.IsChecked = match == TabPresets.Gentle;
            BtnPresetStrict.IsChecked = match == TabPresets.Strict;
            BtnPresetCirce.IsChecked = match == TabPresets.Circe;
            BtnPresetCustom.IsChecked = match == TabPresets.Custom;
            Stakes(TxtStakesGentle, TabPresets.Gentle);
            Stakes(TxtStakesStrict, TabPresets.Strict);
            Stakes(TxtStakesCirce, TabPresets.Circe);
            RefreshMood(ChasterHead.Service); // WPF :1123, the heat row may have just gone on or off
            PaintMenuHelp(); // Natasha's row may have just gone on or off
        }

        private static void Stakes(TextBlock line, string presetId)
        {
            if (TabPresets.Find(presetId) is not { } preset) return;
            var (value, days) = TabPresets.WorstMonth(preset);
            line.Text = Loc.GetF(days ? "chaster_preset_stakes_days" : "chaster_preset_stakes_hours", value);
            ToolTip.SetTip(line, Loc.GetF("chaster_preset_stakes_tip", CircesTab.Format(preset.DayMinutes * 60, signed: false), TabPresets.MonthDays));
        }
    }
}
