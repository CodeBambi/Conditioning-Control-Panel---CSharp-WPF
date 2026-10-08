// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SpiralRoom.cs (WPF 7.1.5): the fuse
// and language subscriptions the Spiral room's rail row used to repaint from. The row itself left
// the rail in the nav rework; "spiral" is a hidden tab of the You section (NavSections), reached
// through ShowTab, the palette and the barks.
//
// ponytail: DescentService (BlockChanged) is a WPF-head network service, so only the fuse and the
// language are subscribed. BeginSpiralFirstLight has no caller here (DescentShowDirector is
// WPF-only) and is not ported until it does.

using System;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _spiralRoomWired;

        /// <summary>WPF InitializeSpiralRoom: subscriptions plus one catch-up paint. Idempotent.</summary>
        internal void InitializeSpiralRoom()
        {
            if (_spiralRoomWired) return;
            _spiralRoomWired = true;
            try
            {
                // Both sources outlive the window (a static and an app service), so the handlers
                // go when it closes or every test shell would stay subscribed (P41).
                var fuse = App.DescentCountdown;
                if (fuse != null) fuse.PhaseChanged += OnSpiralRoomPhaseChanged;
                LocalizationManager.Instance.LanguageChanged += OnSpiralRoomLanguageChanged;
                Closed += (_, _) =>
                {
                    if (fuse != null) fuse.PhaseChanged -= OnSpiralRoomPhaseChanged;
                    LocalizationManager.Instance.LanguageChanged -= OnSpiralRoomLanguageChanged;
                };
                RefreshSpiralRailEntry();
            }
            catch (Exception ex) { Log.Debug("[Spiral] room rail could not be wired: {E}", ex.Message); }
        }

        private void OnSpiralRoomPhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e) => RefreshSpiralRailEntry();

        private void OnSpiralRoomLanguageChanged(object? sender, EventArgs e) => RefreshSpiralRailEntry();

        /// <summary>WPF RefreshSpiralRailEntry. Nav rework (2026-10-06): the Spiral row left the
        /// rail; the You strip lists "spiral" as a hidden tab (NavSections), so there is no row to
        /// paint. Kept, as 7.1.5 keeps it, as the one place the fuse events land, so a fog-era pill
        /// reveal can hook in here.</summary>
        internal void RefreshSpiralRailEntry()
        {
        }

        /// <summary>The rail row's click. The tab does all the deciding.</summary>
        private void BtnNavSpiral_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab(SpiralRoom.TabKey);
    }
}
