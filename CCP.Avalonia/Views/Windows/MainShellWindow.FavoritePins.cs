// PORTED from WPF 7.1.5 MainWindow/MainWindow.FavoritesRail.cs FavoritePinMap + WireFavoritePinMenus
// (:63-82, :439-460) and MainWindow.SectionChrome.cs PillCreated -> AttachPinMenu + PinIdForPill
// (:223-227, :290-296). Parity ledger shell#23: right-click a rail section row, a strip pill or a
// named Play card to "Pin to favorites"; before this only a drawer chip had a menu (unpin only).
//
// The 7.1.5 rail is always labelled (no flyout), so WPF's holdRail branch (HoldNavRailOpen while
// a pin menu is up) has nothing to hold here and is dropped.

using System;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _favoritePinMenusWired;

        /// <summary>WPF FavoritePinMap verbatim: the rail's section rows and the named Play cards.</summary>
        internal static readonly (string Element, string Id)[] FavoritePinMap =
        {
            ("DoorHome", "door.home"), ("DoorStudio", "door.studio"), ("DoorCompanion", "door.companion"),
            ("DoorPlay", "door.play"), ("DoorYou", "door.you"), ("DoorSocial", "door.social"), ("DoorLibrary", "door.library"),
            ("DoorSettings", "door.settings"),
            ("BtnPlayRemoteControl", "tab.remotecontrol"), ("BtnPlayBlinkTrainer", "tab.blinktrainer"),
            ("BtnPlayGradedIntake", "tab.gradedintake"), ("BtnPlayFyp", "tab.fyp"),
            ("BtnPlayLockdown", "tab.lockdown"),
            ("BtnPlayBackRoom", "game.backroom"), ("BtnPlayDtrh", "game.dtrh"),
            ("BtnPlayArcademy", "game.arcademy"), ("BtnPlayRacingThoughts", "game.race"),
        };

        /// <summary>Once: a pin menu on every mapped element that resolves (a missing name is logged
        /// and skipped), and the strip's PillCreated hook so every pill built from now on gets one.</summary>
        internal void WireFavoritePinMenus()
        {
            if (_favoritePinMenusWired) return;
            _favoritePinMenusWired = true;
            var play = Named<Tabs.PlayTabView>("PlayTab");
            foreach (var (element, id) in FavoritePinMap)
            {
                try
                {
                    var target = element.StartsWith("BtnPlay", StringComparison.Ordinal)
                        ? play?.FindControl<Control>(element)
                        : Named<Control>(element);
                    if (target == null) { Log.Debug("Favorites rail: no element named {Name} to pin", element); continue; }
                    AttachPinMenu(target, id);
                }
                catch (Exception ex) { Log.Debug("WireFavoritePinMenus({Name}): {E}", element, ex.Message); }
            }
        }

        private void OnSectionPillCreated(NavTab tab, Control pill)
        {
            var id = PinIdForPill(tab);
            if (id != null) AttachPinMenu(pill, id);
        }

        /// <summary>The Favourites destination a strip pill pins as, or null when it has no Ctrl+K
        /// row (zone pills without a row, hidden pills). WPF SectionChrome.PinIdForPill.</summary>
        internal static string? PinIdForPill(NavTab tab)
        {
            var id = tab.Kind == NavTabKind.Launcher ? "launch." + tab.Key
                   : tab.Key == "justdrop" ? "door.justdrop"
                   : "tab." + tab.Key;
            return FavoritesRailRule.IsDestination(id) && SettingsPaletteIndex.All.Any(e => e.Id == id) ? id : null;
        }
    }
}
