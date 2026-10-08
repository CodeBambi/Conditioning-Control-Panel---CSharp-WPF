// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs (4,073 lines) — the app
// shell's main code-behind, and with its 83 sibling partials the largest view in the app
// (72,317 lines of C# plus 3,474 lines of XAML across ConditioningControlPanel/MainWindow/).
//
// The class is MainShellWindow, NOT MainWindow: this head already has a MainWindow (the
// diagnostics window RenderProof hosts views inside) and an AppShell, and the layer rules forbid
// overwriting either. Whether this becomes the startup window is a later layer's decision.
//
// WHAT THIS FILE DOES: load the XAML, wire the four window-level drag-and-drop events WPF
// declared as Window attributes, and hold the handful of members MainShellWindow.axaml
// dereferences that live in MainWindow.xaml.cs. Everything else in the WPF file reaches App.*,
// a service, a device, WebView2 or Win32 and is a stub — see the ledger at the bottom, and the
// 83 MainShellWindow.<Suffix>.cs partials for the rest.
//
// Win32 in this file (10 P/Invokes), mapped rather than copied — user32/dwmapi do not exist on
// a net10.0 head:
//   DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)
//        -> dropped. The rounded corner is the compositor's business on Linux; the accent frame
//           the app draws over it (GlassWindowEdge, CornerRadius 8) is in the XAML and unchanged.
//   SetForegroundWindow / BringWindowToTop / ShowWindow(SW_RESTORE)
//        -> Activate(), plus WindowState = Normal. All three collapse into the one call.
//   SetWindowPos(HWND_TOPMOST / HWND_NOTOPMOST, SWP_NOACTIVATE)
//        -> Window.Topmost, which Avalonia maps to _NET_WM_STATE_ABOVE. Ordering between our OWN
//           windows would be X11Overlay.RestackAbove; the shell never needs that — it is the
//           bottom of our own stack, not a member of the overlay band.
//   SetWindowPos(x, y, cx, cy) for the work-area clamp
//        -> Position/Width/Height. See MainShellWindow.WorkAreaFit.cs, which is a real port.
//   GetForegroundWindow / GetWindowThreadProcessId
//        -> dropped. "Is some OTHER process focused" has no portable answer and no Avalonia
//           equivalent; the shell used it to decide whether an ambient FX loop may run.
//           LOST BEHAVIOUR, named here so it is not lost silently.
//
// Also dropped, with nothing to map onto:
//   Window.CommandBindings + AvatarTubeWindow.OpenChatCommand ("OpenAvatarChat_Executed").
//        Avalonia has no RoutedCommand/CommandBinding; the chat window is opened by a service.
//   InputBindings (Ctrl+K palette and friends) — Avalonia uses KeyBindings; they are declared
//        in code by the WPF ctor, which is entirely App.*, so they went with it.
//
// DRAG AND DROP: WPF declared AllowDrop + Drop/DragEnter/DragOver/DragLeave as Window
// attributes. Avalonia routes those as attached events, so the XAML carries
// DragDrop.AllowDrop="True" and the four handlers are added in the constructor below.

using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// One row of the header's mod switcher. PORTED verbatim from the nested
    /// <c>ModSelectorItem</c> at the bottom of MainWindow.xaml.cs, lifted to the namespace so the
    /// ComboBox's ItemTemplate can name it. <c>Brush</c> is Avalonia's, not WPF's.
    /// </summary>
    public sealed class ModSelectorItem
    {
        public string Id { get; }
        public string Name { get; }
        public IBrush AccentBrush { get; }

        public ModSelectorItem(string id, string name, IBrush accentBrush)
        {
            Id = id;
            Name = name;
            AccentBrush = accentBrush;
        }
    }

    public partial class MainShellWindow : Window
    {
        /// <summary>
        /// The header mod switcher's rows, rebuilt from App.Mods by InitializeModSelector
        /// (MainShellWindow.ModSwitch.cs). The sample row stays only on the headless render path,
        /// which has no mod service, so the chip still draws a name and an accent dot there.
        /// </summary>
        public ObservableCollection<ModSelectorItem> AvailableMods { get; } = new()
        {
            new ModSelectorItem("default", "Default", Brushes.HotPink),
        };

        public MainShellWindow()
        {
            AvaloniaXamlLoader.Load(this);
            InitializeLanguageSelector();

            // WPF put these on the Window element itself; Avalonia routes them as attached events.
            HookResizeEdges();
            AddHandler(DragDrop.DropEvent, Window_Drop);
            AddHandler(DragDrop.DragEnterEvent, Window_DragEnter);
            AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            AddHandler(DragDrop.DragLeaveEvent, Window_DragLeave);

            // First launch? WPF claims the Welcomed latch from MainWindow's constructor
            // (MainWindow.xaml.cs:555) and opens the wizard once the window is up. Same here -
            // see MainShellWindow.FirstRun.cs, which owns both halves.
            HookFirstRun();

            // The rail's setup pass. Here, not in OnAttachedToVisualTree: a Window IS the visual
            // root, so that override never fires on it and the rail never opened.
            InitializeNavRail();
            InitializeTabHistoryInput(); // MainShellWindow.TabHistory.cs (WPF MainWindow ctor)
            // The mod switcher's rows, the saved mod's palette and the pending first-run choice
            // (MainShellWindow.ModSwitch.cs). No-op on the headless render path (no App.Mods).
            AttachModSwitch();
            HookWallRings();   // MainShellWindow.Presets.cs: wall tile rings follow the flags
            HookLevelDisplay(); // MainShellWindow.HeroFx.cs: header level/XP follow ProgressionBank
            HookAutonomy();     // MainShellWindow.Autonomy.cs: Takeover seeds; starts only on the user's switch
            InitializeDescentFuse(); // MainShellWindow.DescentFuse.cs (WPF MainWindow ctor); no-op without App.DescentCountdown
            InitializeSpiralRoom();  // MainShellWindow.SpiralRoom.cs (WPF MainWindow ctor): the rail row
            InitializeInboxBadge(); // MainShellWindow.Inbox.cs (WPF MainWindow.Inbox.cs:27)
            InitializeLockdownGreys(); // MainShellWindow.Lockdown.cs (WPF Lab.cs:612/707)
            InitializeInviteTicket(); // MainShellWindow.InviteTicket.cs (WPF MainWindow.xaml.cs, main e2d4e35ef)
            InitializeInviteEnding(); // MainShellWindow.Patreon.cs (WPF MainWindow.xaml.cs:3590, main fbe161de2)
            // WPF MainWindow.xaml.cs:3695: the tube is built on load when the companion is enabled.
            Opened += (_, _) => { if (CoreSettings.Current.AvatarEnabled) InitializeAvatarTube(); };
            Closed += (_, _) => _avatarTubeWindow?.Close();
            // WPF MainWindow.xaml.cs:674: catalogue downloads open in the player.
            App.CatalogueLookup.SetOpener(OpenCatalogueEnhancement);
            Closed += (_, _) =>
            {
                App.CatalogueLookup.ClearOpener(OpenCatalogueEnhancement);
                if (Platform.X11PanicKey.PushToTalk == OnPushToTalkKey)
                    Platform.X11PanicKey.PushToTalk = null;
            };
            // WPF MainWindow.xaml.cs:3531-3535: one forced share-status poll per launch.
            Opened += (_, _) => PollCatalogueStatuses(force: true);
        }

        /// <summary>Uses an already-loaded catalogue without making the parameterless shell open
        /// the user's profile. The child is resolved by name because this window uses
        /// AvaloniaXamlLoader.Load, which does not populate generated fields.</summary>
        internal MainShellWindow(SessionManager sessions) : this()
        {
            Named<Tabs.PresetsTabView>("PresetsTab")?.UseSessionManager(sessions);
        }

        // ---- window-level drag and drop ------------------------------------------------------
        // A single *.preset.json imports through Core's PresetFileService (WPF's DropType.Preset).
        // ponytail: every other drop type needs the asset/import pipeline (MainWindow.Assets.cs:
        // media -> Play/Edit/Library prompt, *.ccpenh.json -> Deeper library import) plus the
        // GlobalDropOverlay copy chooser. Wired when those services move to Core. The overlay
        // itself is in the XAML and still collapses/expands correctly once they are.
        private void Window_Drop(object? sender, DragEventArgs e)
        {
            if (e.DataTransfer.TryGetFiles() is { Length: 1 } files
                && files[0].TryGetLocalPath() is { } path
                && path.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase))
                Named<Tabs.PresetsTabView>("PresetsTab")?.HandlePresetDrop(path);
        }
        private void Window_DragEnter(object? sender, DragEventArgs e) { }
        private void Window_DragOver(object? sender, DragEventArgs e) { }
        private void Window_DragLeave(object? sender, RoutedEventArgs e) { }

        // ---- the two handlers MainShellWindow.axaml takes from this file ---------------------
        // WPF MainWindow.xaml.cs:2910: a switch made in the manager repaints through the one path.
        private async void BtnManageMods_Click(object? sender, RoutedEventArgs e)
        {
            // Refresh share badges shown in the dialog (throttled poll).
            _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindMods);
            try
            {
                var dialog = new Dialogs.ModManagerDialog();
                await dialog.ShowDialogSafe(this);
                // Install/uninstall of a non-active mod still changes the combo's rows.
                if (dialog.ModWasChanged) ApplyActiveModChange(); else InitializeModSelector();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[ModManager] failed to open"); }
        }

        // MainWindow.Settings.cs's BtnExit_Click: the real exit, not the X-to-tray close.
        internal void BtnExit_Click(object? sender, RoutedEventArgs e)
        {
            if (LockdownActive)   // WPF MainWindow.Settings.cs:451: say so, then refuse
            {
                _ = Dialogs.MessageDialog.ShowAsync(this, ConditioningControlPanel.Localization.Loc.Get("title_lockdown"),
                    ConditioningControlPanel.Localization.Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"));
                return;
            }
            RequestExit();
        }
    }
}

// Members of ConditioningControlPanel/MainWindow/MainWindow.xaml.cs dropped (182). Named here so
// nothing disappears silently; each one reaches App.*, a service, a device, WebView2 or Win32.
//   private static extern void DwmSetWindowAttribute(…)
//   private const int DWMWA_WINDOW_CORNER_PREFERENCE
//   private const int DWMWCP_ROUND
//   private const int DWMWCP_ROUNDSMALL
//   private static extern bool SetForegroundWindow(…)
//   private static extern bool ShowWindow(…)
//   private static extern bool BringWindowToTop(…)
//   private static extern bool SetWindowPos(…)
//   private static extern IntPtr GetForegroundWindow(…)
//   private static extern uint GetWindowThreadProcessId(…)
//   private static readonly IntPtr HWND_TOPMOST
//   private static readonly IntPtr HWND_NOTOPMOST
//   private const uint SWP_NOACTIVATE
//   private const int SW_RESTORE
//   private const int SW_SHOW
//   private bool _isRunning
//   public bool IsEngineRunning
//   private bool _isLoading
//   public ObservableCollection<ModSelectorItem> AvailableMods
//   private BrowserService? _browser
//   private bool _browserInitialized
//   private Window? _browserPopoutWindow
//   private bool _isDualMonitorPlaybackActive
//   private bool _isBrowserFullscreen
//   private bool _browserFullscreenWasPopout
//   private double _browserPreFullscreenZoom
//   private System.Threading.CancellationTokenSource? _catalogueLookupCts
//   private string? _currentCatalogueHtVideoId
//   private WindowStyle _popoutPreFsStyle
//   private ResizeMode _popoutPreFsResize
//   private WindowState _popoutPreFsState
//   private double _popoutPreFsLeft, _popoutPreFsTop, _popoutPreFsWidth, _popoutPreFsHeight
//   private bool _popoutPreFsTopmost
//   private TrayIconService? _trayIcon
//   private GlobalKeyboardHook? _keyboardHook
//   private bool _isCapturingPanicKey
//   internal bool IsCapturingPanicKey
//   private bool _isCapturingPauseKey
//   private int _panicPressCount
//   private string _leaderboardMode
//   private int _lockdownTimerClickCount
//   private DateTime _lockdownTimerLastClick
//   private Brush? _preLockdownWindowBg
//   private Brush? _preLockdownTitleBarBg
//   private bool _isStreakFixMode
//   private bool _streakFixInFlight
//   private DispatcherTimer? _remoteNotificationTimer
//   private DispatcherTimer? _remoteSessionInfoTimer
//   private Storyboard? _seasonTitleStoryboard
//   private Storyboard? _lockdownPulseStoryboard
//   private bool _skillTreeAnimationsActive
//   private static readonly Dictionary<string, string> CommandLabels
//   private static readonly HashSet<string> SuppressedCommands
//   public event EventHandler? EngineStopped
//   private DateTime _lastPanicTime
//   private string? _lastKnownUnifiedId
//   public Microsoft.Web.WebView2.Wpf.WebView2? GetBrowserWebView(…)
//   private SessionEngine? _sessionEngine
//   private AvatarTubeWindow? _avatarTubeWindow
//   private Services.AudioPlaybackHandle? _levelUpSoundHandle
//   private bool _avatarWasAttachedBeforeMaximize
//   private bool _avatarWasAttachedBeforeBrowserFullscreen
//   private bool _autonomyWasPausedOnMinimize
//   private bool _avatarWasMutedOnMinimize
//   private bool _wasAutonomyRunningBeforeMinimize
//   private bool _wasAvatarUnmutedBeforeMinimize
//   private Dictionary<string, Image> _achievementImages
//   private PinkRushPopup? _pinkRushPopup
//   private Window? _luckyProcPopup
//   private DispatcherTimer? _rampTimer
//   private DateTime _rampStartTime
//   private Dictionary<string, double> _rampBaseValues
//   private int _easterEggClickCount
//   private DateTime _easterEggFirstClick
//   private bool _easterEggTriggered
//   private DispatcherTimer? _schedulerTimer
//   private bool _schedulerAutoStarted
//   private bool _manuallyStoppedDuringSchedule
//   private DispatcherTimer? _bannerRotationTimer
//   private int _bannerCurrentIndex
//   private List<string> _bannerMessages
//   private System.Windows.Media.Animation.Storyboard? _marqueeStoryboard
//   private DispatcherTimer? _marqueeRefreshTimer
//   private string _currentMarqueeMessage
//   private const bool PacksSectionEnabled
//   private ObservableCollection<ContentPack> _availablePacks
//   private DispatcherTimer? _packPreviewTimer
//   private DispatcherTimer? _statPillUpdateTimer
//   private DispatcherTimer? _conditioningTimeTimer
//   private DateTime _conditioningStartTime
//   private double _conditioningBaselineMinutes
//   private DispatcherTimer? _conditioningTimeSyncTimer
//   private int _conditioningTimeSecondCounter
//   public MainWindow(…)
//   private void OnXPChanged(…)
//   private void OnProfileLoaded(…)
//   private void OnSyncHealthChanged(…)
//   private void OnLevelUp(…)
//   private void PlayLevelUpSound(…)
//   private void StopLevelUpSound(…)
//   private void OnGlobalKeyPressed(…)
//   private static readonly TimeSpan PanicWatchdogTimeout
//   private static int _panicFallbackRunning
//   private void ArmPanicWatchdog(…)
//   private void RunEmergencyPanicTeardown(…)
//   private static void LogPanicFallbackStep(…)
//   private void QueuePanicFallbackRecovery(…)
//   internal const Key QuickRecalHotkeyKey
//   internal const ModifierKeys QuickRecalHotkeyModifiers
//   internal static string QuickRecalHotkeyChord
//   internal static string CameraShortcutChord
//   internal static string QuickRecalHotkeyHint(…)
//   private bool _quickRecalHotkeyBusy
//   private static string FormatChord(…)
//   private void ApplyGlobalQuickRecalHotkey(…)
//   private static bool IsGazeCalibrationSurfaceOpen(…)
//   private async void OpenQuickRecalFromHotkey(…)
//   private void HandlePanicKeyPress(…)
//   private static bool AnyGameSurfaceOwnsTheScreen(…)
//   private void RunPanicStopTail(…)
//   private void PanicStopEverySurface(…)
//   private void StopAdHocEffects(…)
//   private void UpdatePanicKeyButton(…)
//   internal void UpdatePauseKeyButton(…)
//   internal void RequestPickAssetsFolder(…)
//   internal void RequestBeginPanicKeyCapture(…)
//   internal void RequestToggleOfflineMode(…)
//   internal void RequestToggleNoPanic(…)
//   internal bool ApplyNoPanic(…)
//   internal bool ApplyOfflineMode(…)
//   internal void SyncNoPanicState(…)
//   internal void SyncOfflineModeState(…)
//   internal bool RequestToggleWindowsStartup(…)
//   private void LoadLogo(…)
//   private void LoadTakeoverImage(…)
//   private void RefreshThemeAwareElements(…)
//   private static Color LightenColor(…)
//   private static Color DarkenColor(…)
//   private void RefreshBrowserLoadingText(…)
//   private static System.Windows.Media.ImageSource? ModTileVariant(…)
//   private const int TileDecodeWidth
//   private const int WideTileDecodeWidth
//   private void LoadFeatureImages(…)
//   private static ImageSource? LoadModImageDecoded(…)
//   private static void ApplyArtFraming(…)
//   private static ModArtFraming? ActiveModFraming(…)
//   private readonly List<(…)
//   private void RefreshHypnotubeLinksUI(…)
//   private static bool IsListingUrl(…)
//   internal void BtnAddVideoLink_Click(…)
//   private TextBox MakePoolTextBox(…)
//   private void PersistVideoLinks(…)
//   private void UpdateNoVideoLinksPlaceholder(…)
//   private string GetModeAwareQuestImagePath(…)
//   private System.Windows.Media.Imaging.BitmapImage? LoadQuestImage(…)
//   private void CenterOnPrimaryScreen(…)
//   private async void MainWindow_Loaded(…)
//   private async Task CheckPendingRegistrationAsync(…)
//   private async Task CheckFirstRunAssetsPromptAsync(…)
//   private static bool HasAnyLocalMedia(…)
//   private const int WM_GETMINMAXINFO
//   private const int WM_ENTERSIZEMOVE
//   private const int WM_EXITSIZEMOVE
//   private const int WM_DPICHANGED_MAIN
//   private struct POINT
//   private struct MINMAXINFO
//   private IntPtr WndProc(…)
//   private void QueueEmiKnock(…)
//   public IntPtr Handle
//   public Win32WindowWrapper(…)
//   public string Id
//   public string Name
//   public Brush AccentBrush
//   public ModSelectorItem(…)
