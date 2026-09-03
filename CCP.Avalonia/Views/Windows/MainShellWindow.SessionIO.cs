// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.SessionIO.cs (2212 lines).
// Sorted member by member against the fifteen Core seams. Nothing here is restored, and the two
// notes below exist so the next pass does not repeat the checks.
//
// FIRST, A LEAD THAT DOES NOT APPLY TO THIS HEAD. MainShellWindow.CatalogueSubmissions.cs restored
// its read half (CatalogueKindSessions, CanonicalCataloguePathKey, GetCatalogueRecord,
// IsCatalogueAcceptedStatus) and named "the session rack pill" here as one of three surfaces it
// unblocked. On WPF that pill is four lines inside BuildSessionRackRow
// (MainWindow.SessionIO.cs:517-522). There is no pill to wire HERE, because there is no row: the
// whole rack builder is unported, and the badge factory those four lines call,
// CreateCatalogueStatusBadge, lives in MainShellWindow.PresetIO.cs, which this layer does not own.
// The lead is correct about the DEPENDENCY and wrong about the work being small. It becomes true
// the moment BuildSessionRackRow exists.
//
// SECOND, THE REAL BLOCKER: Services.SessionManager
// (ConditioningControlPanel/Services/Session/SessionManager.cs) and its two companions,
// SessionFileService (same folder) and AssetImportService. SessionManager owns AllSessions, the
// reload and add/remove events, DeleteSession's refusal to touch a built-in, and the disk sync;
// every member below either reads it or repaints something it changed. Models.Session IS in Core,
// which is why the FILTER half looks portable - see the next paragraph for why it is still out.
//
//   The rack - RepaintSessionRack, EnumerateRackSessions, BuildSessionRackRow, MakeRackMeta,
//   MakeRackPill, AttachRoundedClip, CreateSessionActionButton, CreateSessionDeleteButton,
//   CreateSessionRowButton, SelectSession, RefreshSessionRackSelection, HasSessionRackRow,
//   GenerateSessionTimelineDescription, RevealSessionInLibrary. The host panel for these rows
//   already exists on this head (Views/Tabs/PresetsTabView.axaml:653 - "ONE panel.
//   RepaintSessionRack clears and refills it"), so this is a real, bounded next layer.
//
//   The rack toolbar - EnsureSessionRackToolbar, RackSourceChip_Changed, RackDifficultyChip_Changed,
//   CmbRackSort_SelectionChanged, TxtRackSearch_TextChanged, ClearSessionRackFilters,
//   UpdateRackToolbarCounts, RackSourceLabel, RackSourceChipLabel, RackDifficultyLabel, the four
//   RackSource* constants and the six filter fields.
//
//   RackAccepts, SortRackSessions and RackFileStamp are the exception worth calling out: all three
//   are pure over Models.Session and System.IO and would compile here today. They are held back
//   because they are the filter engine for rows that do not exist - restoring a sorter with nothing
//   to sort is padding, and splitting the rack across two layers is how the filters and the rows
//   drift apart. They come back in the same layer as RepaintSessionRack, which is their only caller.
//
//   Session lifecycle - InitializeSessionManager, OnSessionsReloaded, OnSessionAdded,
//   OnSessionRemoved, RegisterExternallySavedSession, SyncCustomSessionsFromDisk, GetSessionById,
//   SessionBtn_Edit, SessionBtn_Export, SessionBtn_Delete, SessionBtn_Share,
//   SessionContextMenu_Export, ExportSessionToFile, BtnExportSession_Click, BtnCreateSession_Click.
//   SessionBtn_Share also needs the catalogue client (App.Catalogue) and the WRITE half of
//   MainShellWindow.CatalogueSubmissions.cs, which is out for its head-only SubmissionResult type.
//
//   Drag and drop - Window_DragEnter, Window_DragOver, Window_DragLeave, Window_Drop,
//   SessionDropZone_DragEnter / DragOver / DragLeave / Drop, DetectDropType, UpdateDropOverlay,
//   HandleSessionDrop, HandleAssetDropAsync, ShowDropZoneStatus, IsDeeperPlayableMedia, DropType,
//   MediaDropChoice and the four extension sets. The extension sets and DetectDropType are pure;
//   the drop itself needs AssetImportService, and Avalonia's DragDrop is a different API from WPF's
//   (DataFormats.FileNames -> DataFormats.Files, IDataObject -> IStorageItem), so this is a rewrite.
//
//   The two global hotkeys - ApplyGlobalChatHotkey, ApplyGlobalCameraHotkey, ApplyCameraShortcutTo,
//   ParseModifiers, FormatCameraShortcut, RefreshChatShortcutLabel, RefreshCameraShortcutLabel,
//   BtnChatShortcut_Click, BtnCameraShortcut_Click, ToggleCameraCommand, OpenAvatarChat_Executed,
//   BringToForegroundAndOpenChat, _cameraCommandBound. RoutedUICommand and ModifierKeys are WPF
//   types, and a DESKTOP-WIDE hotkey is a Win32 RegisterHotKey - the bucket-E problem CLAUDE.md
//   describes, not a port. ToggleCameraCommand is additionally a camera control: see the refusal
//   already recorded for the two device pills in MainShellWindow.LabTab.cs.
//
//   RefreshImagesList / RefreshVideosList - the assets tab's lists, blocked with the asset tree in
//   MainShellWindow.Assets.cs.
//
// Checked and NOT the blocker: CoreSession. It answers the RUNNING session (start, stop, current
// state); this file is the session LIBRARY - files on disk, the rack that lists them, import and
// export. They share the word and nothing else.
//
// No member of this partial is referenced from MainShellWindow.axaml.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
