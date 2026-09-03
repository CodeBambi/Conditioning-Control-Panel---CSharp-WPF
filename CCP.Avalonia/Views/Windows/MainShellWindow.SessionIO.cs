// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.SessionIO.cs (2212 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// Members dropped (93):
//   private Services.SessionManager? _sessionManager
//   private Services.SessionFileService? _sessionFileService
//   private Services.AssetImportService? _assetImportService
//   private void InitializeSessionManager(…)
//   private void OnSessionsReloaded(…)
//   public bool RegisterExternallySavedSession(…)
//   private void OnSessionAdded(…)
//   private void OnSessionRemoved(…)
//   private const string RackSourceAll
//   private const string RackSourceBuiltIn
//   private const string RackSourceYours
//   private const string RackSourceCatalogue
//   private string _rackSourceFilter
//   private string _rackSort
//   private string _rackSearch
//   private readonly HashSet<Models.SessionDifficulty> _rackDifficulties
//   private readonly List<ToggleButton> _rackSourceChips
//   private readonly List<ToggleButton> _rackDifficultyChips
//   private bool _rackToolbarBuilt
//   private bool _rackToolbarSyncing
//   internal void RepaintSessionRack(…)
//   private List<Models.Session> EnumerateRackSessions(…)
//   private bool RackAccepts(…)
//   private List<Models.Session> SortRackSessions(…)
//   private static DateTime? RackFileStamp(…)
//   private Border BuildSessionRackRow(…)
//   private TextBlock MakeRackMeta(…)
//   private static(…)
//   private static void AttachRoundedClip(…)
//   private void EnsureSessionRackToolbar(…)
//   private static string RackSourceLabel(…)
//   private static string RackSourceChipLabel(…)
//   private static string RackDifficultyLabel(…)
//   private void UpdateRackToolbarCounts(…)
//   private void RackSourceChip_Changed(…)
//   private void RackDifficultyChip_Changed(…)
//   internal void CmbRackSort_SelectionChanged(…)
//   internal void TxtRackSearch_TextChanged(…)
//   private void ClearSessionRackFilters(…)
//   private void RefreshSessionRackSelection(…)
//   private bool HasSessionRackRow(…)
//   private Border MakeRackPill(…)
//   private Button CreateSessionActionButton(…)
//   private Button CreateSessionDeleteButton(…)
//   private Button CreateSessionRowButton(…)
//   private void SelectSession(…)
//   private string GenerateSessionTimelineDescription(…)
//   internal void SessionDropZone_DragEnter(…)
//   internal void SessionDropZone_DragOver(…)
//   internal void SessionDropZone_DragLeave(…)
//   private void Window_DragEnter(…)
//   private void OpenAvatarChat_Executed(…)
//   internal void BtnChatShortcut_Click(…)
//   private void ApplyGlobalChatHotkey(…)
//   private void BringToForegroundAndOpenChat(…)
//   public void RefreshChatShortcutLabel(…)
//   public static readonly RoutedUICommand ToggleCameraCommand
//   private bool _cameraCommandBound
//   private void ApplyCameraShortcutTo(…)
//   private void ApplyGlobalCameraHotkey(…)
//   private static ModifierKeys ParseModifiers(…)
//   private static string FormatCameraShortcut(…)
//   public void RefreshCameraShortcutLabel(…)
//   internal void BtnCameraShortcut_Click(…)
//   private void Window_DragOver(…)
//   private void Window_DragLeave(…)
//   private async void Window_Drop(…)
//   private enum DropType
//   private static readonly HashSet<string> AssetVideoExtensions
//   private static readonly HashSet<string> AssetImageExtensions
//   private static readonly HashSet<string> DeeperVideoExtensions
//   private static readonly HashSet<string> DeeperAudioExtensions
//   private enum MediaDropChoice
//   private static bool IsDeeperPlayableMedia(…)
//   private static DropType DetectDropType(…)
//   private void UpdateDropOverlay(…)
//   private void HandleSessionDrop(…)
//   private async Task HandleAssetDropAsync(…)
//   private void RefreshImagesList(…)
//   private void RefreshVideosList(…)
//   internal void SessionBtn_Edit(…)
//   internal void SessionBtn_Export(…)
//   private async void SessionBtn_Share(…)
//   private void SyncCustomSessionsFromDisk(…)
//   private void SessionBtn_Delete(…)
//   private Models.Session? GetSessionById(…)
//   internal bool RevealSessionInLibrary(…)
//   internal void SessionDropZone_Drop(…)
//   private void ShowDropZoneStatus(…)
//   internal void BtnExportSession_Click(…)
//   internal void BtnCreateSession_Click(…)
//   private void SessionContextMenu_Export(…)
//   private void ExportSessionToFile(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
