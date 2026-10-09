// PORTED from ConditioningControlPanel/MainWindow/MainWindow.CompanionFx.cs (95 lines).
//
// The WPF file has no ambient effect left: the hero disc's breathe belongs to
// CompanionHeroCard.StartAmbientLoop (parked by CompanionRoomView off the tab's own visibility) and
// the connect sheen was deleted with the AI-brain card it swept. What remains, and is ported here:
//   * the tab's visibility hook - leaving writes the two drawers' open/closed state, entering
//     restores it (WPF restores once at startup, InitializePatreonTab -> RestoreCompanionSectionStates,
//     MainWindow.Patreon.cs:1266; here on every show, same result since each hide writes first) and offers an upgrader
//     the Awareness v2 consent dialog (WPF EnsureAwarenessV2Consent, MainShellWindow.CompanionRoom.cs);
//   * the mod repaint - CoreMods.ModChanged re-reads the roster (WPF UpdateCompanionCardsUI). The
//     hero re-reads its own name/portrait off the same event (CompanionHeroCard.OnModChanged).
// WPF's UpdateCompanionPromptLabels half has nothing to repaint: the per-card assigned-prompt
// labels wait on CompanionService (row shell-companion-tab).
// WPF's IsIncomingTab guard is not needed: this head's ShowTab has no fade-out re-show.

using System;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using Avalonia.Controls;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _companionFxInitialized;

        /// <summary>WPF MainWindow.CompanionFx.cs:32, called from CompanionTabView on a real
        /// visibility edge of the room.</summary>
        internal void OnCompanionTabVisibilityChanged(bool visible, CompanionRoomView room)
        {
            try
            {
                var map = CoreSettings.Current?.CompanionSectionOpen;
                if (!visible)
                {
                    // Per-toggle would be a settings save on every click of a rarely-opened drawer.
                    if (map != null) { room.PersistDrawerStates(map); CoreSettings.Save(); }
                }
                else if (map != null) room.RestoreDrawerStates(map);
                // Under v2 the room itself stays collapsed (its cells live on the Companion pages),
                // so its own show edge never fires: re-read the zones and the roster here (WPF
                // SyncCompanionTabUI -> CompanionRoom.Sync + UpdateCompanionCardsUI on every show).
                if (visible) room.SyncData();

                InitializeCompanionFx(room);

                if (visible) EnsureAwarenessV2Consent();
            }
            catch (Exception ex) { Log.Debug("OnCompanionTabVisibilityChanged: {E}", ex.Message); }
        }

        /// <summary>WPF WindowChrome.cs:176: quitting while still on the tab is not a hide edge.</summary>
        private void PersistCompanionDrawerStatesOnExit()
        {
            var map = CoreSettings.Current?.CompanionSectionOpen;
            var room = Named<Tabs.CompanionTabView>("CompanionTab")?.FindControl<CompanionRoomView>("Room");
            if (map == null || room == null) return;
            room.PersistDrawerStates(map);
            CoreSettings.Save();
        }

        private void InitializeCompanionFx(CompanionRoomView room)
        {
            if (_companionFxInitialized) return;
            _companionFxInitialized = true;
            void OnModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(room.RefreshRoster);
            CoreMods.ModChanged += OnModChanged;
            // A static event must not pin a closed shell (P41).
            Closed += (_, _) => CoreMods.ModChanged -= OnModChanged;
        }
    }
}
