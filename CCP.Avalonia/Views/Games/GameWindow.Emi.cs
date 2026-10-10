using System;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// EMI's game moments: WPF ArcademyHostService.cs :193 / :6018, DtrhHostService.cs :101 / :1065,
    /// FypHostService.cs :80 / :152. One fire when the window opens, one when it closes, with the
    /// minutes it was up. Closed is the one funnel every exit reaches, and the stamp is cleared as
    /// it is read so a second pass says nothing. The other games have no moment in WPF either.
    /// </summary>
    internal sealed partial class GameWindow
    {
        private DateTime _emiOpenedUtc = DateTime.MinValue;

        /// <summary>The moment stem for this game, or null when WPF fires nothing for it.</summary>
        internal static string? EmiMomentStem(string id) => id switch
        {
            "arcademy" => "arcademy",
            "dtrh" => "dtrh",
            FypId => "fyp",
            _ => null,
        };

        private void EmiGameOpened()
        {
            if (EmiMomentStem(Spec.Id) is not { } stem) return;
            _emiOpenedUtc = DateTime.UtcNow;
            // WPF's farewell claims her voice for that window: the last thing you get is the bye.
            if (_arcFarewellClaimed) return;
            EmiDeskBus.Fire(stem + "Opened");
        }

        private void EmiGameClosed()
        {
            if (_emiOpenedUtc == DateTime.MinValue || EmiMomentStem(Spec.Id) is not { } stem) return;
            int minutes = Math.Max(0, (int)(DateTime.UtcNow - _emiOpenedUtc).TotalMinutes);
            _emiOpenedUtc = DateTime.MinValue;
            EmiDeskBus.Fire(stem + "Closed", new { minutes });
        }
    }
}
