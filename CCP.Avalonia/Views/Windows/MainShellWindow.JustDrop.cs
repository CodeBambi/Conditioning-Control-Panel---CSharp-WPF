// STILL A STUB from ConditioningControlPanel/MainWindow/MainWindow.JustDrop.cs (138 lines), and
// head-side for one reason: every line of it fans out ONE flag that this head cannot obtain.
//
// JustDropService (ConditioningControlPanel/Services/JustDrop/JustDropService.cs) is not in Core.
// DoorAvailable is a per-launch server GET with a false default and no persistence, and
// AvailabilityChanged is the static event this file subscribes to; without them there is nothing to
// announce and nothing to announce it to. Faking the flag would open a door to a shop window that
// is not on this head either ("justdrop" is a WindowKeys no-op in MainShellWindow.TabNavigation.cs).
//
// Its three fan-out targets are also stubs here, so restoring this file first would fan out to
// nothing: ApplyTeaseCard is real as of this layer (MainShellWindow.TeaseCard.cs) but pins the tile
// to the teased state deliberately, while RefreshPlayCards (MainShellWindow.PlayTab.cs) and
// RefreshTakeawayShelf (MainShellWindow.Takeaway.cs) are both still notes.
//
// Members still absent (5): JustDropCheckDelayMs, _justDropDoorHooked, InitializeJustDropDoor,
// OnJustDropAvailabilityChanged, ApplyJustDropDoorVisibility.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
