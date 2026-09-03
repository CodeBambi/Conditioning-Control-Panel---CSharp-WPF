// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.Takeaway.cs (670 lines) - the
// Presets-tab receipt strip for Just Drop orders.
//
// ONE BLOCKER, hard rather than pending: every member takes or returns a
// JustDropOrdersService.Order. That type is
// ConditioningControlPanel/Services/JustDrop/JustDropOrdersService.cs, an internal static class
// that reads the server's order drawer live over the device-token door and keeps nothing locally.
// It is not in Core and CANNOT be seeded here the way CoreSession or CoreModArt can: the drawer
// needs the device token, the token seam is CoreSecrets, and unseeded CoreSecrets means NO STORE,
// never a token in the clear. No token, no drawer, no orders to format or paint.
//
// That reaches even the pure-looking helpers: FormatTakeawayMeta / Date / Minutes / Age and
// SafeOrderName are all `static string F(JustDropOrdersService.Order)`. Their bodies are portable
// string work, but a formatter for a type this head cannot name is not restorable in isolation -
// it would need rewriting against a Core order shape that does not exist.
//
// The strip is not missing: CCP.Avalonia/Views/Tabs/PresetsTabView.axaml.cs already paints a
// placeholder shelf into TakeawayShelf/TakeawayTray with the real chrome, so the tab renders
// finished rather than gapped. What it has no way to get is rows.
//
// Members, grouped by what each needs:
//   Load + repaint (JustDropOrdersService list read):
//     RefreshTakeawayShelf, LoadTakeawayShelfAsync, PaintTakeawayShelf,
//     const TakeawayShelfCap (3 pinned before the "+n more" toggle), _takeawayLoading.
//   Chip/row construction, all taking an Order:
//     BuildTakeawayDropChip, BuildTakeawayMoreChip, BuildTakeawayTrayRow, MakeTakeawayRowMeta,
//     BuildTakeawayCopyChip, BuildTakeawayDoorChip.
//   Formatters - portable bodies, unportable parameter (above):
//     FormatTakeawayMeta, FormatTakeawayDate, FormatTakeawayMinutes, FormatTakeawayAge,
//     SafeOrderName.
//   Overflow tray - no service dependency at all, and dead without rows to expand:
//     _takeawayTrayOpen, _takeawayMoreLabel, _takeawayMoreCount, SetTakeawayTrayOpen,
//     TakeawayMoreText, TakeawayMore_Click.
//   Actions, each a different missing head capability:
//     TakeawayCard_Click     JustDropHostService.LaunchReplay - the WebView2 shop host (bucket D).
//     TakeawayDoor_Click     JustDropService.DoorAvailable + LaunchShop, same host.
//     TakeawayCopyLink_Click System.Windows.Clipboard (Avalonia's TopLevel.Clipboard covers it)
//                            plus JustDropService.TasteUrl for the link and App.Notifications for
//                            the toast. The clipboard is the available third.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose. Shelf chrome: CCP.Avalonia/Views/Tabs/PresetsTabView.axaml.cs.
        // Rows need an order-drawer seam, which needs a seeded CoreSecrets first.
    }
}
