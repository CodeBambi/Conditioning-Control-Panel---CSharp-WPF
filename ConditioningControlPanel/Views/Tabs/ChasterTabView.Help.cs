using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The page says what it does (tester feedback 2026-09-29: "the page is not clear at all").
    /// A "How it works" card behind a link, a live line over the menu that says how to change a
    /// time or why it cannot move right now, a reset for edited times, what a key's month figure
    /// means, and what the red flash switch does.
    /// </summary>
    public partial class ChasterTabView
    {
        private void BtnHowItWorks_Click(object sender, RoutedEventArgs e) =>
            HowItWorksCard.Visibility = HowItWorksCard.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>The line over the menu, the reset link and the red flash line. Cheap; runs
        /// whenever the stamps or the rows change.</summary>
        internal void PaintMenuHelp()
        {
            var open = FiguresEditable();
            TxtMenuHint.Text = Loc.Get(open ? "chaster_menu_hint_edit" : "chaster_menu_hint_locked");
            var edited = App.Settings?.Current?.ChasterPriceOverrides is { Count: > 0 } o
                         && o.Keys.Any(id => TabPriceEdit.IsEdited(id, o));
            BtnResetPrices.Visibility = open && edited ? Visibility.Visible : Visibility.Collapsed;

            // A red flash books Natasha's row, so it follows that row's figure and needs it on.
            var natashaOn = App.Settings?.Current?.ChasterPrices?.Contains(NatashasFavourite.EventId) == true;
            TxtFlashDodgeSub.Text = natashaOn
                ? Loc.GetF("chaster_flash_dodge_sub", NatashasFavourite.DodgeMs / 1000,
                    CircesTab.Format(ShownSeconds(NatashasFavourite.EventId), signed: false))
                : Loc.Get("chaster_flash_dodge_needs");
            FlashDodgeRow.Opacity = natashaOn ? 1 : 0.7;
        }

        /// <summary>Every edited time back to the table's figure. Only while no lock runs.</summary>
        private void BtnResetPrices_Click(object sender, RoutedEventArgs e)
        {
            if (!FiguresEditable() || App.Settings?.Current is not { } settings) return;
            if (_editingId != null) CancelPriceEdit();
            settings.ChasterPriceOverrides = new();
            App.Settings?.Save();
            App.Logger?.Information("[Chaster] prices reset to the table by the player");
            PaintStamps();
            if (_trailerShown && _trailerRow != null) OpenTrailer(_trailerRow);
        }

        /// <summary>"Up to +4 days a month" on a key, and its tooltip: the daily limit behind it.</summary>
        private static void Stakes(TextBlock line, string presetId)
        {
            if (TabPresets.Find(presetId) is not { } preset) return;
            var (value, days) = TabPresets.WorstMonth(preset);
            line.Text = Loc.GetF(days ? "chaster_preset_stakes_days" : "chaster_preset_stakes_hours", value);
            line.ToolTip = Loc.GetF("chaster_preset_stakes_tip",
                CircesTab.Format(preset.DayMinutes * 60, signed: false), TabPresets.MonthDays);
        }
    }
}
