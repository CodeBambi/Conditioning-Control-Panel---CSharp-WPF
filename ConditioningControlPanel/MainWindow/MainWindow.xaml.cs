using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    public partial class MainWindow : Window
    {
        // DWM API for Windows 11 rounded corners
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        // Win32 API for forcing window to foreground
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new(-2);
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        private bool _isRunning = false;
        public bool IsEngineRunning => _isRunning;
        private bool _isLoading = true;

        /// <summary>
        /// Items shown in the top-bar mod switcher ComboBox. Rebuilt by InitializeModSelector.
        /// </summary>
        public ObservableCollection<ModSelectorItem> AvailableMods { get; } = new();
        // Guards SelectionChanged from re-entering activation while we repopulate the list.
        private bool _suppressModSelectorChange;
        private BrowserService? _browser;
        private bool _browserInitialized = false;
        // _skipSiteToggleNavigation removed in #867: the site toggle handler moved from Checked
        // to Click, so setting the radios in code no longer navigates and there is nothing left
        // to suppress. A flag that is only cleared by the handler is a trap once the handler
        // stops running - the next real click gets eaten instead.
        private Window? _browserPopoutWindow = null;
        private bool _isDualMonitorPlaybackActive = false;
        private bool _isBrowserFullscreen = false;
        private bool _browserFullscreenWasPopout = false;
        private double _browserPreFullscreenZoom = 1.0;
        // W3 Piece 1 — catalogue lookup state. One CTS per in-flight lookup;
        // the navigation hook cancels the previous one when a new HT URL is
        // detected, so a slow lookup landing after the user moved on can't
        // surface a stale toast. _currentCatalogueHtVideoId is set just before
        // the toast appears and verified on action click — if it no longer
        // matches the current page, the action no-ops.
        private System.Threading.CancellationTokenSource? _catalogueLookupCts;
        private string? _currentCatalogueHtVideoId;
        // Popout pre-fullscreen state
        private WindowStyle _popoutPreFsStyle;
        private ResizeMode _popoutPreFsResize;
        private WindowState _popoutPreFsState;
        private double _popoutPreFsLeft, _popoutPreFsTop, _popoutPreFsWidth, _popoutPreFsHeight;
        private bool _popoutPreFsTopmost;
        private TrayIconService? _trayIcon;
        private GlobalKeyboardHook? _keyboardHook;
        private bool _isCapturingPanicKey = false;
        internal bool IsCapturingPanicKey => _isCapturingPanicKey;

        // Capture mode for the optional Pause key (v6.8.5). Same one-shot dance as the panic key:
        // the next key the global hook sees becomes the binding (Escape clears it instead).
        private bool _isCapturingPauseKey = false;
        private bool _exitRequested = false;
        private int _panicPressCount = 0;
        private string _leaderboardMode = "monthly";

        // Lockdown mode
        private int _lockdownTimerClickCount = 0;
        private DateTime _lockdownTimerLastClick = DateTime.MinValue;
        private Brush? _preLockdownWindowBg;
        private Brush? _preLockdownTitleBarBg;
        private bool _isStreakFixMode = false;
        // Guards the manual streak-fix spend. MessageBox.Show pumps messages, so without this a
        // double-click (or a click on a second day while the first day's server call is in flight)
        // re-enters StreakFixDay_Click and spends twice from one balance check.
        private bool _streakFixInFlight = false;
        private DispatcherTimer? _remoteNotificationTimer;
        private DispatcherTimer? _remoteSessionInfoTimer;

        // Tab animation storyboards (so they can be stopped when tab is hidden)
        private Storyboard? _seasonTitleStoryboard;
        private Storyboard? _lockdownPulseStoryboard;
        private bool _skillTreeAnimationsActive = false;

        private static readonly Dictionary<string, string> CommandLabels = new()
        {
            ["show_pink_filter"] = "cmd_pink_filter_enabled",
            ["stop_pink_filter"] = "cmd_pink_filter_disabled",
            ["show_spiral"] = "cmd_spiral_enabled",
            ["stop_spiral"] = "cmd_spiral_disabled",
            ["start_bubbles"] = "cmd_bubbles_started",
            ["stop_bubbles"] = "cmd_bubbles_stopped",
            ["trigger_video"] = "cmd_video_triggered",
            ["trigger_haptic"] = "cmd_haptic_triggered",
            ["trigger_bubble_count"] = "cmd_bubble_count_triggered",
            ["start_autonomy"] = "cmd_autonomy_enabled",
            ["stop_autonomy"] = "cmd_autonomy_disabled",
            ["start_session"] = "cmd_session_started",
            ["pause_session"] = "cmd_session_paused",
            ["resume_session"] = "cmd_session_resumed",
            ["stop_session"] = "cmd_session_stopped",
            ["enable_strict_lock"] = "cmd_strict_lock_enabled",
            ["disable_strict_lock"] = "cmd_strict_lock_disabled",
            ["disable_panic"] = "cmd_panic_key_disabled",
            ["enable_panic"] = "cmd_panic_key_enabled",
            ["trigger_panic"] = "cmd_all_effects_stopped",
        };

        private static readonly HashSet<string> SuppressedCommands = new()
        {
            "trigger_flash", "trigger_subliminal",
            "set_pink_opacity", "set_spiral_opacity",
            "duck_audio", "unduck_audio",
        };

        /// <summary>
        /// Fires when the engine is stopped (for avatar reactions)
        /// </summary>
        public event EventHandler? EngineStopped;
        private DateTime _lastPanicTime = DateTime.MinValue;
        private string? _lastKnownUnifiedId;

        /// <summary>
        /// Gets the browser WebView2 control for external access (e.g., avatar audio controls)
        /// </summary>
        public Microsoft.Web.WebView2.Wpf.WebView2? GetBrowserWebView() => _browser?.WebView;
        
        // Session Engine
        private SessionEngine? _sessionEngine;
        
        // Avatar Tube Window
        private AvatarTubeWindow? _avatarTubeWindow;
        private Services.AudioPlaybackHandle? _levelUpSoundHandle;
        private bool _avatarWasAttachedBeforeMaximize = false;
        private bool _avatarWasAttachedBeforeBrowserFullscreen = false;

        // Auto-pause state when minimized with attached avatar
        private bool _autonomyWasPausedOnMinimize = false;
        private bool _avatarWasMutedOnMinimize = false;
        private bool _wasAutonomyRunningBeforeMinimize = false;
        private bool _wasAvatarUnmutedBeforeMinimize = false;

        // Achievement tracking
        private Dictionary<string, Image> _achievementImages = new();

        // Pink Rush popup
        private PinkRushPopup? _pinkRushPopup;

        // Lucky proc toast popup
        private Window? _luckyProcPopup;
        
        // Ramp tracking
        private DispatcherTimer? _rampTimer;
        private DateTime _rampStartTime;
        private Dictionary<string, double> _rampBaseValues = new();

        // Easter egg tracking (100 clicks in 60 seconds)
        private int _easterEggClickCount = 0;
        private DateTime _easterEggFirstClick = DateTime.MinValue;
        private bool _easterEggTriggered = false;
        
        // Scheduler tracking
        private DispatcherTimer? _schedulerTimer;
        private bool _schedulerAutoStarted = false;
        private bool _manuallyStoppedDuringSchedule = false;

        // Banner rotation (cycles through 3 messages: support, welcome, thanks)
        private DispatcherTimer? _bannerRotationTimer;
        private int _bannerCurrentIndex = 0; // 0=Primary (support), 1=Secondary (welcome)
        private List<string> _bannerMessages = new();

        // Marquee animation
        private System.Windows.Media.Animation.Storyboard? _marqueeStoryboard;
        private DispatcherTimer? _marqueeRefreshTimer;
        private string _currentMarqueeMessage = "";

        // Content packs
        // PacksSection in MainWindow.xaml is currently Visibility="Collapsed" — most packs live outside the app,
        // and users are routed to Discord via BtnGetPacks. Flip this const + the two Visibility values to restore.
        private const bool PacksSectionEnabled = false;
        private ObservableCollection<ContentPack> _availablePacks = new();
        private DispatcherTimer? _packPreviewTimer;

        // Stat pills
        private DispatcherTimer? _statPillUpdateTimer;

        // Conditioning time tracker
        private DispatcherTimer? _conditioningTimeTimer;
        private DateTime _conditioningStartTime;
        private double _conditioningBaselineMinutes; // TotalConditioningMinutes at session start (avoids double-counting)
        private DispatcherTimer? _conditioningTimeSyncTimer; // Server sync every 15 minutes
        private int _conditioningTimeSecondCounter; // Count seconds for minute-based saves

        public MainWindow()
        {
            InitializeComponent();

            // Apply the user-configured chat shortcut. AvatarTubeWindow does the same
            // for itself; both windows respond to the same RoutedUICommand. We ALSO
            // register a Win32 system-wide hotkey via GlobalHotkeyService so the same
            // combo opens chat from any other app (browser, terminal, etc.) without
            // needing one of our windows to have focus.
            Loaded += (_, _) =>
            {
                AvatarTubeWindow.ApplyChatShortcutTo(this);
                RefreshChatShortcutLabel();
                ApplyGlobalChatHotkey();
                ApplyCameraShortcutTo();
                RefreshCameraShortcutLabel();
                ApplyGlobalCameraHotkey();
                // Gaze Quick Recal from anywhere (Ctrl+Alt+G). Sits next to the camera
                // shortcut on purpose - they are neighbours in the same feature but they
                // are NOT the same action, and the labels say so.
                ApplyGlobalQuickRecalHotkey();
                // EMI Desk's summon chord (default Ctrl+Alt+E). Armed here and not at service
                // construction because RegisterHotKey needs an HWND to hang off, and it refuses
                // any chord whose base key is on the modifier-blind panic/pause hook.
                App.EmiDesk?.ApplyHotkey();
                // Ctrl+K settings palette (Windows/SettingsPaletteWindow.xaml.cs). Registered
                // AFTER the camera shortcut on purpose: WPF executes the FIRST matching
                // InputBinding, so a user who rebound the camera hotkey to Ctrl+K keeps their
                // explicit choice and the palette quietly yields. In-window only by design - the
                // palette is a navigation aid for this window, not something to summon from a
                // browser, so it deliberately does not take a system-wide GlobalHotkeyService slot.
                InputBindings.Add(new KeyBinding(SettingsPaletteWindow.OpenPaletteCommand,
                                                Key.K, ModifierKeys.Control));
                CommandBindings.Add(new CommandBinding(SettingsPaletteWindow.OpenPaletteCommand,
                    (_, ce) => { SettingsPaletteWindow.Toggle(this); ce.Handled = true; }));
                HookFocusGazeService();
                HookBlinkTrainerService();
                // Tooltip hygiene: start tracking before the user can hover anything, so no tooltip
                // is ever opened untracked (the lazy hook this replaced installed its handlers on
                // the first tab switch, i.e. potentially after the first tooltip was already up).
                // See MainWindow.ToolTipHygiene.cs.
                EnsureToolTipHygiene();
                // Chrome FX (PR-1): nav hover/active glow, START breath + sheen, XP gloss.
                // After load, so every templated nav button is real before we touch it.
                InitializeChromeFx();
                // Dashboard FX (PR-2): mosaic ambient canvas, tile hover/active breath, logo
                // drift, rail hover, browser frame. Same reason for being here, and it must
                // follow InitializeChromeFx - it rides that file's loop funnel.
                InitializeDashboardFx();
                // Nav rail collapse/hover-expand. After the FX inits on purpose: it caches a
                // visual-tree walk of the rail, so every templated row has to be real first.
                InitializeNavRail();
            };
            Closing += (_, _) => Services.GlobalHotkeyService.UnregisterAll();
            // The title-bar X now MINIMIZES TO TRAY (see OnClosing) instead of quitting — users expect
            // the app to keep running in the background (#446/#438). Real exit is the tray-menu Exit and
            // the in-app Exit button, which both set _exitRequested and call Application.Current.Shutdown()
            // so the app can't linger headless behind ownerless windows (the original 3219fd01 concern).
            // Lockdown still blocks the X close in OnClosing.

            // Set version dynamically from assembly
            var version = Services.UpdateService.GetCurrentVersion();
            // Phase 8: ProgressionTab.TxtVersion is gone. The two live version readouts seed
            // themselves - Settings · Updates (UpdatesSettingsSection.xaml.cs) and the System
            // popup's AppInfoFeatureControl - alongside the three chrome labels below.
            Title = $"Conditioning Control Panel v{version}";
            TxtTitleBarVersion.Text = $"Conditioning Control Panel v{version}";
            TxtHeaderVersion.Text = $"v{version}";

            // Center on primary monitor
            CenterOnPrimaryScreen();
            
            // Load logo
            LoadLogo();

            // Initialize mod selector display
            InitializeModSelector();

            // Apply the persisted active mod to the rest of the UI. Without
            // these calls, a fresh launch keeps the XAML-default (Bambi)
            // feature card icons + accent brushes regardless of which mod
            // is actually active — the user only saw the correct theme
            // after manually re-picking the mod in the selector
            // (ApplyActiveModChange). Logo + selector chip + tube/avatar
            // already painted correctly because they were on the startup
            // path; these three were only reached through ApplyActiveModChange.
            LoadTakeoverImage();
            LoadFeatureImages();
            RefreshThemeAwareElements();

            // Initialize tray icon
            _trayIcon = new TrayIconService(this);
            // Let the bark system observe tray-driven events (e.g. "wake Bambi").
            App.Bark?.AttachTray(_trayIcon);
            _trayIcon.OnExitRequested += () =>
            {
                if (App.Lockdown?.IsActive == true) return;

                _exitRequested = true;
                if (_isRunning) StopEngine();

                // Kill all audio and effects - ensures clean exit with audio unducked
                App.KillAllAudio();

                // Explicitly dispose overlay
                try
                {
                    App.Overlay?.Dispose();
                }
                catch { }

                EnsureSessionRestoredForExit();
                SaveSettings();
                Application.Current.Shutdown();
            };
            _trayIcon.OnShowRequested += () =>
            {
                ShowAvatarTube();
            };
            _trayIcon.OnWakeBambiRequested += () =>
            {
                WakeBambiUp();
            };

            // Initialize global keyboard hook (only if panic key is enabled)
            _keyboardHook = new GlobalKeyboardHook();
            App.PanicHook = _keyboardHook;   // #875: lock cards ask this whether a panic escape really exists
            _keyboardHook.KeyPressed += OnGlobalKeyPressed;
            _keyboardHook.KeyPressedWithVkCode += (key, vkCode) => App.KeywordTriggers?.OnKeyPressed(key, vkCode);
            App.KeywordTriggers?.SetSessionActiveCallback(() => _sessionEngine?.IsRunning == true);
            if (App.Settings.Current.KeywordTriggersEnabled && KeywordTriggerService.HasAccess())
                App.KeywordTriggers?.Start();
            if (App.Settings.Current.PanicKeyEnabled || App.Settings.Current.KeywordTriggersEnabled)
            {
                _keyboardHook.Start();
            }

            // Initialize lockdown mode event handlers
            InitializeLockdown();

            // Subscribe to progression events for real-time XP updates.
            //
            // THE BANK subscribes FIRST and that is load-bearing (MainWindow.BankFx.cs): multicast
            // handlers run in subscription order, and BankFx has to arm its hold on XPChanged
            // BEFORE OnXPChanged tweens the counter it means to withhold. It also takes XPAwarded,
            // the delta-and-provenance event that XPChanged deliberately is not.
            InitializeBankFx();
            App.Progression.XPChanged += OnXPChanged;
            App.Progression.LevelUp += OnLevelUp;

            // Post-session media log: the dialog appears here for both natural completion
            // and abort. SessionEngine raises LogReady AFTER it fires SessionCompleted, so
            // OnSessionCompleted handles XP awarding only - the dialog is shown from this hook.
            if (App.SessionLog != null)
            {
                App.SessionLog.LogReady += OnSessionLogReady;
            }

            // Subscribe to companion events for real-time UI updates (v5.3)
            if (App.Companion != null)
            {
                App.Companion.XPAwarded += OnCompanionXPAwarded;
                App.Companion.CompanionLevelUp += OnCompanionLevelUp;
                App.Companion.XPDrained += OnCompanionXPDrained;
                App.Companion.CompanionSwitched += OnCompanionSwitched;
            }

            // Subscribe to cloud profile sync event to refresh UI when profile loads
            App.ProfileSync.ProfileLoaded += OnProfileLoaded;
            App.ProfileSync.SyncHealthChanged += OnSyncHealthChanged;

            LoadSettings();
            InitializePresets();
            UpdateUI();
            SetupHelpButtons();

            // Sync startup registration with settings
            StartupManager.SyncWithSettings(App.Settings.Current.RunOnStartup);

            _isLoading = false;

            // Initialize phrase count display
            UpdatePhraseCountDisplay();

            // Initialize achievement grid and subscribe to unlock events
            PopulateAchievementGrid();
            if (App.Achievements != null)
            {
                App.Achievements.AchievementUnlocked += OnAchievementUnlockedInMainWindow;
            }

            // Header profile bubble: avatar face, hover account menu, live reactions
            // (XP pulse / level-up burst / flash wobble). MainWindow.ProfileBubble.cs.
            InitializeProfileBubble();

            // THE FUSE's tease surfaces: header spark, hover readout, corner clock, chrome dimming
            // repaint. MainWindow.DescentFuse.cs. Subscribes to a service that raises nothing
            // without a cached ceremony timestamp, so this is inert on every install today.
            InitializeDescentFuse();

            // The Spiral Room's rail row: Collapsed unless this account is in the fog era or has an
            // open spiral. MainWindow.SpiralRoom.cs. Three subscriptions and one Collapsed write on
            // an account with neither, which is every install today.
            InitializeSpiralRoom();

            // Subscribe to quest events
            if (App.Quests != null)
            {
                App.Quests.QuestCompleted += OnQuestCompleted;
                App.Quests.QuestProgressChanged += OnQuestProgressChanged;
                App.Quests.QuestsRefreshed += (s, e) => Dispatcher.Invoke(() => RefreshQuestUI());
            }

            // The header's four tilted quest stamps (3 daily slots + the weekly), sitting in the
            // XP row ahead of the LVL chip. MainWindow.QuestStamps.cs owns its own subscriptions
            // and collapses itself when there is no QuestService, so the bar keeps full width.
            InitializeQuestStamps();

            // Repaint the quests tab whenever the streak-fix balance moves. StreakFixCharges is
            // written imperatively (stats tile + button caption), not bound, and it changes from four
            // places — sync adoption, the manual spend, the automatic spend and the skill purchase —
            // most of them off the UI thread. Riding the settings INPC catches all four with one
            // subscription and keeps the services out of MainWindow.
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.PropertyChanged += OnSettingsPropertyChangedForQuests;
                // The 4x4 wall's active rings ride the same INPC (resurrected 2026-08-11 with
                // the FX tiles themselves): any of the eleven *Enabled flags moving - from the
                // rack panels, a session ramp, remote control or the wall's own right-click -
                // repaints the rings, so the wall never shows yesterday's mix.
                App.Settings.Current.PropertyChanged += OnSettingsPropertyChangedForWall;
            }

            // A landed server override for the ? box repaints the wall + rail + lockbands: the
            // rail refresh is the one funnel that already fans out to all three. The Velvet
            // Vault rides the SAME event (never its own timer) so the FREE TODAY card and the
            // dashboard's ? box can never name two different features; the call no-ops until
            // the tab has been built at least once.
            if (App.DailyFree != null)
            {
                App.DailyFree.TodayChanged += () =>
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RefreshPremiumRail();
                        RefreshExclusivesTab();
                        // The Play wall rides it too, for its lockbands AND its FREE TODAY
                        // re-stamps (MainWindow.PlayTab.cs). It is not reached by the rail
                        // refresh - that funnel covers the rail and the mosaic - and the two Play
                        // cards on the wheel (For You, Remote Control) would otherwise go on
                        // wearing yesterday's answer until the user re-entered the door.
                        RefreshPlayCards();
                        // #978: and the DOORS themselves. Six feature pages carry their own
                        // translucent padlock veil, painted only by UpdatePatreonUI, which no
                        // rotation event ever reached - so a free day lifted every band on the
                        // rail and the wall and still left the destination page bolted shut.
                        RefreshEntitlementVeils();
                    }));
            }

            // Subscribe to skill tree events
            if (App.SkillTree != null)
            {
                App.SkillTree.PinkRushStarted += OnPinkRushStarted;
                App.SkillTree.PinkRushEnded += OnPinkRushEnded;
                App.SkillTree.LuckyProc += OnLuckyProc;
            }

            // Subscribe to roadmap events
            if (App.Roadmap != null)
            {
                App.Roadmap.StepCompleted += OnRoadmapStepCompleted;
                App.Roadmap.TrackUnlocked += OnRoadmapTrackUnlocked;
            }

            // Initialize Avatar tab settings
            InitializePatreonTab();

            // Initialize Exclusives section visibility for already-logged-in users
            UpdateAccountLinkingUI();

            // Initialize banner rotation
            InitializeBannerRotation();

            // Ensure all services are stopped on startup (cleanup any leftover state)
            App.BouncingText.Stop();
            App.Overlay.Stop();

            // v6.0: fresh installs land on CCP Default (neutral baseline).
            // Content packs (docs/CONTENT_PACKS_PLAN.md §4 + §5): the mod media no longer ships in the
            // installer, so the picker is back — for BOTH populations. First launch gets it as step 2
            // of the wizard below, before the tour; every ALREADY-Welcomed install gets the standalone
            // ModPickerDialog from the else branch, because the modular installer's [InstallDelete]
            // sweep just took their bundled mod audio away and they would otherwise never be offered
            // it back. The one-shot guards (ModPickerShown / ModPickerOfflineOffers / IsFullInstall /
            // null service) are the SAME rules on both paths - the wizard's mod step reuses
            // ModPickerDialog's own guard predicates rather than restating them - so each population
            // is offered exactly once and nobody who should not see it does.

            // Phase 8: one screen instead of the gauntlet. FirstRunWizard.ShouldRunAndClaim reads
            // (and latches) the same Welcomed flag WelcomeDialog.ShowIfNeeded did, at the same
            // instant, so the else branch below - What's New, season recap, the upgrader's mod
            // picker - is reached by exactly the same population as before. The wizard itself
            // owns what used to be three separate modals: the welcome card, the first-run mod
            // picker (ModPickerDialog.ShowIfNeeded's one-shot + offline guards included) and the
            // "choose a content folder" MessageBox; StartTutorial is launched from its last step.
            // ASK EMI WAVE 1: read LastSeenVersion HERE, before anything on this launch stamps it.
            // ShowWhatsNewIfNeeded (the first statement of the else branch, a few lines down) writes
            // the current version into that setting synchronously, minutes before the knock's own
            // dispatcher item runs. A late read would therefore see this build's own stamp and
            // classify every single upgrader as somebody who is owed nothing - which is the same
            // shape as the bug that showed every fresh install a migration notice for a move it
            // never witnessed. One string, captured once, handed to the knock at the far end.
            var knockSeenVersion = App.Settings?.Current?.LastSeenVersion ?? string.Empty;

            if (FirstRunWizard.ShouldRunAndClaim())
            {
                // EMI Desk (MOMENTS 4.B): a HOLD, never a line. The wizard owns the screen on a
                // first launch and she does not get to talk over someone's first thirty seconds.
                try { App.EmiDesk?.Fire("firstLaunchEver", null); } catch { }

                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    // Wait for any update dialog to be dismissed first
                    // Check every 500ms for up to 30 seconds
                    for (int i = 0; i < 60 && App.IsUpdateDialogActive; i++)
                    {
                        await Task.Delay(500);
                    }

                    // The wizard's doors step and the spotlight overlay both measure this window's
                    // controls, so neither may start against a window that hasn't loaded yet (up
                    // to 10s). This is why the wizard opens here rather than in the constructor.
                    for (int i = 0; i < 20 && !IsLoaded; i++)
                    {
                        await Task.Delay(500);
                    }

                    if (!App.IsUpdateDialogActive && IsLoaded)
                    {
                        FirstRunWizard.Run(this);
                        // The wizard is modal, so this is the far side of it: the screen is the
                        // user's again and the HOLD comes off.
                        try { App.EmiDesk?.ReleaseHold("firstLaunchEver"); } catch { }
                    }
                    else
                    {
                        // The waits above gave up (an update dialog still on screen after 30s, a
                        // window that never loaded). Hand the flags back rather than spending a
                        // first run nobody was shown - the next launch offers it properly.
                        FirstRunWizard.HandBackFirstRun(
                            App.IsUpdateDialogActive ? "update dialog still open" : "window never loaded");
                        // Nothing was shown, so nothing is owed the screen.
                        try { App.EmiDesk?.ReleaseHold("firstLaunchEver"); } catch { }
                    }

                    // THE KNOCK (Ask EMI wave 1). The far side of the wizard, on both paths: the
                    // population this is FOR is the one that pressed "explore on my own", and the
                    // hand-back path is a launch where nothing was ever shown and she is exactly as
                    // welcome. Every remaining gate - the wizard, an update dialog, a session, a
                    // tutorial overlay, a minimised window, the setting, whether she is already out
                    // - lives in EmiKnockMachine.MayKnock, so this is one call and no policy.
                    QueueEmiKnock(knockSeenVersion);
                    // Normal, NOT Loaded: this app keeps the dispatcher busy enough (compositor
                    // host + avatar animations) that Loaded-priority items are starved and never
                    // run - the first-launch tour silently never started at Loaded priority.
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            else
            {
                // Not first launch - check if we need to show "What's New" after an update
                ShowWhatsNewIfNeeded();
                TryPresentSeasonRecap();

                // Upgraders into the modular build get the SAME picker, once, at the equivalent safe
                // point: after the update dialog AND the What's New / season-recap dialogs are done,
                // and once this window has actually loaded. No tutorial follows here - existing users
                // already had it.
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        // Let the startup dialogs that were queued just above actually claim the
                        // flag before we start watching it - What's New posts itself and has not
                        // raised IsStartupDialogShowing yet at this instant.
                        await Task.Delay(1500);

                        // Same waiting idiom as the first-launch branch, plus IsStartupDialogShowing:
                        // What's New is modal and posts itself onto the dispatcher, so it can still be
                        // pending when this runs. Every modular upgrader ARRIVES with a What's New to
                        // read, so wait out minutes of reading, not seconds - at 30s a user still on
                        // the patch notes silently lost the picker until the next launch (play-test
                        // scenario C caught exactly that). Past 5 min we still defer to next launch,
                        // which ModPickerShown=false keeps armed.
                        //
                        // App.Tutorial.IsActive is in the predicate since v6.8.0: What's New clears
                        // IsStartupDialogShowing in its finally BEFORE the "Show me around (60s)"
                        // action it queued gets to run, so without this check the picker opened
                        // modally ON TOP of the running upgrade tour's spotlight (flagged in the
                        // 0812 build review). The tour is minutes at most, well inside the 5-min
                        // budget this loop already spends on the patch notes.
                        for (int i = 0; i < 600 && (App.IsUpdateDialogActive || IsStartupDialogShowing
                                                    || App.Tutorial?.IsActive == true); i++)
                        {
                            await Task.Delay(500);
                        }

                        for (int i = 0; i < 20 && !IsLoaded; i++)
                        {
                            await Task.Delay(500);
                        }

                        if (!App.IsUpdateDialogActive && !IsStartupDialogShowing
                            && App.Tutorial?.IsActive != true && IsLoaded)
                        {
                            // Pre-ticks the card for the mod they were already running, so one press
                            // restores what the installer removed.
                            ModPickerDialog.ShowIfNeeded(this, preselectActiveMod: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to offer the mod picker to an upgrading install");
                    }

                    // THE KNOCK (Ask EMI wave 1), the upgrader's half. Same call, same gates; the
                    // snapshot taken before ShowWhatsNewIfNeeded ran is what makes this population
                    // legible at all by the time we get here.
                    QueueEmiKnock(knockSeenVersion);
                    // Normal, NOT Loaded - Loaded-priority work is starved in this app and silently
                    // never runs (same reason as the first-launch branch above).
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }

            // Initialize scheduler timer (checks every 30 seconds)
            _schedulerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _schedulerTimer.Tick += SchedulerTimer_Tick;

            // Delay scheduler startup by 60 seconds to allow app to fully initialize
            // This prevents issues when restarting after an update while in a scheduled time window
            const int schedulerGracePeriodSeconds = 60;
            App.Logger?.Information("Scheduler will start after {Seconds}s grace period", schedulerGracePeriodSeconds);

            Task.Delay(TimeSpan.FromSeconds(schedulerGracePeriodSeconds)).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (Application.Current == null) return;

                    _schedulerTimer.Start();
                    CheckSchedulerOnStartup();
                    App.Logger?.Information("Scheduler grace period complete - scheduler now active");
                });
            });
            
            // Show local level/XP immediately (cloud sync may update later via ProfileLoaded)
            UpdateLevelDisplay();

            // Initialize browser when window is loaded
            Loaded += MainWindow_Loaded;

            // Phase 10: live-refresh the Deeper tab on library changes
            // (FileSystemWatcher fires through dispatcher.BeginInvoke, debounced
            // 300ms). Detached on window close so a closed window doesn't keep
            // reacting to file drops.
            if (App.EnhancementLibrary != null)
                App.EnhancementLibrary.LibraryChanged += OnDeeperLibraryChanged;

            // W3 Piece 1 — register the "open file in Deeper Player" opener so
            // the catalogue lookup service can hand a freshly-downloaded
            // enhancement straight to the runtime UI without taking a static
            // reference to MainWindow.
            //
            // NOT OpenDeeperFile (which routes to the Editor). Catalogue
            // enhancements should auto-play, matching the user's expectation
            // after clicking "Use one" / picking a row. We mirror the Editor's
            // own Preview button (DeeperEditorWindow.cs:3637) — the canonical
            // "open this .ccpenh.json into the Player" pattern uses the 4-arg
            // EnhancementPlayerWindow constructor with a source tag so the
            // discovery-source badge can show "catalogue" later if we add it.
            //
            // Returns true on successful Show(), false on any failure so the
            // service can surface the OpenError toast (which offers "Open
            // Library" as a recovery action).
            App.CatalogueLookup?.SetOpener(path =>
            {
                try
                {
                    var enhancement = App.EnhancementLibrary?.Open(path);
                    if (enhancement == null)
                    {
                        App.Logger?.Warning("[Catalogue] EnhancementLibrary.Open returned null for {Path}", path);
                        return false;
                    }
                    // captures MainWindow as owner; valid since this opener is registered during MainWindow's lifetime
                    var win = new Views.Deeper.EnhancementPlayerWindow(
                        App.DeeperPlayer, App.DeeperHost, enhancement, "catalogue") { Owner = this };
                    win.Show();
                    return true;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "[Catalogue] Player open failed for {Path}", path);
                    return false;
                }
            });

            // (The Exclusives submenu popup and its Alt+Tab / outside-click close
            // handlers were removed when the launcher became a real tab — see
            // MainWindow.Exclusives.cs.)

            // velvet-mosaic (2026-08-11 rebuild): the active-state INPC subscription and the
            // ToggleRequested handler that used to be registered here are gone with the twelve FX
            // tiles they served. The wall is eight destinations now — "on" is not a state Down the
            // Rabbit Hole has — so there is nothing to highlight and nothing to quick-toggle. The
            // gesture moved to the premium rail, where the chips genuinely are toggles, and the
            // per-feature state dots live in the Studio rack beside the dials.
            //
            // The mosaic's own repaint (tier price tags) hangs off RefreshPremiumRail instead,
            // which already carries the three triggers it needs: patron status arriving or being
            // lost, the Home door being shown, and the weekly intake pass changing.
        }

        private void OnXPChanged(object? sender, double xp)
        {
            Dispatcher.Invoke(() => UpdateLevelDisplay());
        }

        private void OnProfileLoaded(object? sender, EventArgs e)
        {
            // Cloud profile was loaded - refresh UI to show updated XP/level
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Cloud profile loaded, refreshing UI");
                UpdateLevelDisplay();
                // Also update avatar in case level changed significantly
                _avatarTubeWindow?.UpdateAvatarForLevel(App.Settings.Current.PlayerLevel);

                // THE VAT'S LATE KEY. Opening the Trainer Card fires exactly one ungated
                // Descent request (MainWindow.ProfileVat.OnProfileVatVisibilityChanged), but
                // DescentService.RefreshAsync returns false without a word when UnifiedId or
                // AuthToken is not populated yet — and auth lands well after startup (the
                // restore-session path alone sleeps 3s). Nothing then re-poked Descent: the
                // post-sync hook in ProfileSyncService is gated on HasSeenBlock, which that
                // silent miss left false, so the jar, its faucet tooltip and the XP readout
                // all stayed dark until a sign-out/in or the 60s background poll happened to
                // catch up. Profile-loaded is the app's "server data has landed" signal, so
                // ask again here. RequestRefresh is fire-and-forget and self-throttling
                // (MinFetchInterval + in-flight gate), so an already-lit vat costs nothing.
                App.Descent?.RequestRefresh("profile loaded");

                // Re-arm autonomy after profile load ONLY if the user opted into resume-on-startup
                // (same gate as App.OnStartup — Takeover otherwise always starts OFF).
                var s = App.Settings?.Current;
                if (s != null && s.AutonomyResumeOnStartup && s.AutonomyModeEnabled && s.AutonomyConsentGiven
                    && App.Autonomy?.IsEnabled != true)
                {
                    var hasAccess = App.Patreon?.HasPremiumAccess == true
                                    || App.DailyFree?.IsFreeToday("takeover") == true;
                    if (hasAccess)
                    {
                        App.Autonomy?.Start();
                        App.Logger?.Information("Re-armed Takeover after profile load (resume-on-startup opt-in)");
                    }
                }
            });
        }

        private void OnSyncHealthChanged(object? sender, int failureCount)
        {
            Dispatcher.Invoke(() =>
            {
                if (failureCount >= 3)
                {
                    App.Logger?.Warning("[SyncHealth] {Count} consecutive sync failures — notifying user", failureCount);
                    // Show a subtle notification in the title bar area
                    Title = $"Conditioning Control Panel — Cloud sync issue";
                }
                else if (failureCount == 0)
                {
                    // Restore normal title
                    Title = "Conditioning Control Panel";
                }
            });
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            Dispatcher.Invoke(() =>
            {
                // Event FX (PR-5) FIRST, while the XP bar is still standing at the cap it just
                // reached - UpdateLevelDisplay wraps it back to a sliver. Fire-and-forget.
                CelebrateLevelUp();
                UpdateLevelDisplay();
                // Show level up notification
                _trayIcon?.ShowNotification("Level Up!", $"You reached Level {newLevel}!", System.Windows.Forms.ToolTipIcon.Info);
                // Play level up sound
                PlayLevelUpSound();
                // Update avatar if level threshold reached (20, 50, 100)
                _avatarTubeWindow?.UpdateAvatarForLevel(newLevel);
            });
        }


        private void PlayLevelUpSound()
        {
            try
            {
                var soundPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "lvup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lvlup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
                };

                var soundPath = soundPaths.FirstOrDefault(File.Exists);
                if (soundPath == null)
                {
                    App.Logger?.Debug("Level up sound not found in any of: {Paths}", string.Join(", ", soundPaths));
                    return;
                }

                // Stop any previous level up sound still playing
                StopLevelUpSound();

                var masterVolume = App.Settings.Current.MasterVolume / 100f;
                var curvedVolume = (float)Math.Pow(masterVolume, 1.5) * 0.2625f;

                // AudioService owns the device + its disposal (deferred past NAudio's own unwind,
                // which is what the old "Handle is not initialized" workaround here was for), and
                // it never opens the device on the dispatcher — #778/#779.
                // Stop() on an already-finished handle is a no-op, so no completion bookkeeping is
                // needed — StopLevelUpSound above already ran before this one started.
                _levelUpSoundHandle = App.Audio?.PlayOneShot(soundPath, Math.Max(0.01f, curvedVolume), "level-up");

                App.Logger?.Debug("Level up sound played from: {Path}", soundPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to play level up sound: {Error}", ex.Message);
            }
        }

        private void StopLevelUpSound()
        {
            try
            {
                _levelUpSoundHandle?.Stop();
                _levelUpSoundHandle = null;
            }
            catch { }
        }

        private void OnGlobalKeyPressed(Key key)
        {
            // Lockdown mode: block all key handling (panic key, etc.)
            if (App.Lockdown?.IsActive == true)
                return;

            // Track Alt+Tab for achievement (Player 2 Disconnected)
            if (key == Key.Tab && (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)))
            {
                if (_isRunning)
                {
                    App.Achievements?.TrackAltTab();
                    App.Logger?.Debug("Alt+Tab detected during session");
                }
            }
            
            // Handle panic key capture mode
            if (_isCapturingPanicKey)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    App.Settings.Current.PanicKey = key.ToString();
                    _isCapturingPanicKey = false;
                    UpdatePanicKeyButton();
                    App.Logger?.Information("Panic key changed to: {Key}", key);
                    // Same precedent as ApplyGlobalCameraHotkey after a rebind: re-evaluate the
                    // Quick Recal chord so binding panic to G disarms it immediately, and binding
                    // panic away from G frees it without waiting for a restart.
                    ApplyGlobalQuickRecalHotkey();
                });
                return;
            }

            // Same capture dance for the optional Pause key (v6.8.5). Escape CLEARS the binding
            // instead of setting it: with the panic key on Escape by default, binding pause to
            // Escape too would be a dead setting, and "press Escape to unbind" is the only way out
            // of capture mode that does not require picking some key you did not want.
            if (_isCapturingPauseKey)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    App.Settings.Current.PauseKey = key == Key.Escape ? "" : key.ToString();
                    _isCapturingPauseKey = false;
                    UpdatePauseKeyButton();
                    App.Settings?.Save();
                    App.Logger?.Information("Pause key changed to: {Key}",
                        string.IsNullOrEmpty(App.Settings.Current.PauseKey) ? "(unbound)" : App.Settings.Current.PauseKey);
                    // The pause key rides the same modifier-blind hook as panic, so it is in the
                    // Quick Recal clash set too: re-evaluate the chord exactly as the panic rebind does.
                    ApplyGlobalQuickRecalHotkey();
                });
                return;
            }
            
            // Check if panic key is enabled and pressed
            var settings = App.Settings.Current;
            if (settings.PanicKeyEnabled)
            {
                var panicKey = settings.PanicKey;
                if (key.ToString() == panicKey)
                {
                    // #616/#617/#621/#622/#623 — "I pressed the panic key and nothing happened".
                    // Three things can be true and we could not tell them apart:
                    //   (a) the WH_KEYBOARD_LL callback never ran (this line missing) — either the
                    //       UI thread that owns the hook was wedged, or Windows had already dropped
                    //       our hook for exceeding LowLevelHooksTimeout during an earlier stall;
                    //   (b) the callback ran but the dispatcher never drained the BeginInvoke below
                    //       ("received"/"queued" present, "handling" missing) — a wedged UI thread;
                    //   (c) the handler ran and the teardown itself hung ("handling" but no "handled").
                    // Note this callback executes ON THE UI THREAD (LL hooks are delivered to the
                    // installing thread's message loop), so the stall column on this very line is
                    // itself evidence.
                    VideoDiag.Log("PANIC", $"panic key '{key}' RECEIVED by the global hook - queueing handler");
                    var panicOp = Dispatcher.BeginInvoke(() => HandlePanicKeyPress());
                    VideoDiag.Log("PANIC", "handler queued on the dispatcher");
                    ArmPanicWatchdog(panicOp);
                    return;
                }
            }

            // Optional Pause key (v6.8.5). PanicOverridesAll took the #735 "someone walked in"
            // grace pause off the panic key; this is where it lives now, for the people who liked
            // it. Unbound by default, so this whole branch is dead on a fresh install. Checked
            // AFTER the panic key and skipped outright when the two collide, so a shared binding
            // can only ever panic. No watchdog: parking one video is not an emergency stop.
            if (!Services.Safety.PanicPolicy.PauseKeyIsShadowedByPanicKey(
                    settings.PanicKey, settings.PanicKeyEnabled, settings.PauseKey)
                && Services.Safety.PanicPolicy.IsPauseKeyPress(settings.PauseKey, key.ToString()))
            {
                VideoDiag.Log("PANIC", $"pause key '{key}' received - queueing the video grace pause");
                Dispatcher.BeginInvoke(() =>
                {
                    try { App.Video?.TryGracePauseFromPanic(fromPanicKey: false); }
                    catch (Exception ex) { App.Logger?.Warning("Pause key: grace pause failed: {Error}", ex.Message); }
                });
            }
        }

        // #919b: the panic press is queued on the dispatcher, so a wedged UI thread swallows it
        // exactly when it matters most (reporter's trace: the hook logged the press three times,
        // the handler never ran once). If the queued handler hasn't finished inside this window,
        // an emergency teardown runs off-thread instead.
        // Coverage is bounded by where the arming happens: the WH_KEYBOARD_LL hook is installed with
        // dwThreadId=0 but delivered on the UI thread's own message pump, so the watchdog can only
        // arm while that thread is still PUMPING. That covers the two reported shapes - a handler
        // that ran and then hung, and a dispatcher queue starved by higher-priority work - but not a
        // thread that has stopped pumping altogether (there the callback never runs, Windows drops
        // the hook for exceeding LowLevelHooksTimeout, and nothing here fires at all).
        private static readonly TimeSpan PanicWatchdogTimeout = TimeSpan.FromSeconds(2);
        private static int _panicFallbackRunning;

        /// <summary>
        /// Watches a queued panic handler from a background thread. Called from the WH_KEYBOARD_LL
        /// callback, which runs on the UI thread and must return well inside LowLevelHooksTimeout —
        /// so this only ever spawns the watcher, it never waits here.
        /// </summary>
        private void ArmPanicWatchdog(DispatcherOperation? op)
        {
            if (op == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    var finished = await Task.WhenAny(op.Task, Task.Delay(PanicWatchdogTimeout))
                                             .ConfigureAwait(false);
                    if (finished == op.Task)
                    {
                        try { _ = op.Task.Exception; } catch { }   // observe a faulted handler
                        return;
                    }
                    RunEmergencyPanicTeardown();
                }
                catch (Exception ex)
                {
                    try { App.Logger?.Error(ex, "Panic watchdog failed"); } catch { }
                }
            });
        }

        /// <summary>
        /// Last-resort panic path (#919b), on a background thread, when the dispatcher never ran the
        /// queued handler. ONLY thread-safe teardown belongs here: anything that touches a WPF object
        /// throws on this thread, and the premise is that the UI thread is unavailable. Every step is
        /// guarded on its own so one failure can't starve the rest.
        /// </summary>
        private void RunEmergencyPanicTeardown()
        {
            // The panic double-press ladder ends in an app shutdown, which reliably outlives the 2s
            // deadline — without this the watchdog fires INTO the shutdown and races App disposal
            // over services it is already tearing down.
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    VideoDiag.Log("PANIC", "FALLBACK skipped — the app is already shutting down");
                    return;
                }
            }
            catch { return; }

            if (Interlocked.Exchange(ref _panicFallbackRunning, 1) == 1) return;
            try
            {
                try
                {
                    App.Logger?.Warning(
                        "PANIC FALLBACK: dispatcher did not handle the panic key within {Ms}ms — tearing down off-thread",
                        (int)PanicWatchdogTimeout.TotalMilliseconds);
                }
                catch { }
                VideoDiag.Log("PANIC", "FALLBACK firing — the UI thread never drained the queued handler");

                try { App.Haptics?.PanicStop(); }        catch (Exception ex) { LogPanicFallbackStep("haptics", ex); }
                // Conditional, matching the designed panic path in StopEverything (#668): with the
                // standalone Audio Layers master on, the bed is the user's, not the session's.
                try
                {
                    if (App.Settings?.Current?.AudioLayersEnabled != true) App.LayeredAudio?.Stop();
                }
                catch (Exception ex) { LogPanicFallbackStep("audio layers", ex); }
                // StopEmergency, not StopAndDisarm: the same runtime teardown from any thread, but
                // without persisting MantraChantEnabled=false. The real panic leaves the setting
                // alone, and a watchdog that fired only because the UI thread was slow must not
                // silently turn the user's chant off for good.
                try { App.MantraChant?.StopEmergency(); } catch (Exception ex) { LogPanicFallbackStep("mantra chant", ex); }
                // #890 rewrote both of these to be reachable from this thread. Called individually
                // rather than via App.KillAllAudio, which also touches services never audited for
                // off-thread calls.
                try { App.MindWipe?.Stop(); }            catch (Exception ex) { LogPanicFallbackStep("mind wipe", ex); }
                try { App.BrainDrain?.Stop(); }          catch (Exception ex) { LogPanicFallbackStep("brain drain", ex); }
                try { App.Audio?.ForceUnduck(); }        catch (Exception ex) { LogPanicFallbackStep("unduck", ex); }
                try { App.ScreenOcr?.Stop(); }           catch (Exception ex) { LogPanicFallbackStep("screen OCR", ex); }

                // No overlay hiding here by design. ShowWindowAsync only POSTS to the owner thread,
                // so it cannot hide anything while that thread is the wedged one — and re-showing
                // the raw-Win32 LayeredCompositorHost behind its back desyncs host.IsVisible, which
                // CompositorEngine gates every Show/Hide on: one false fire and every layer renders
                // into an invisible window for the rest of the session. Overlays are the UI thread's
                // to drop, when it comes back and runs the real handler.
                QueuePanicFallbackRecovery();

                VideoDiag.Log("PANIC", "FALLBACK complete");
            }
            catch (Exception ex)
            {
                try { App.Logger?.Error(ex, "Panic fallback teardown failed"); } catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _panicFallbackRunning, 0);
            }
        }

        private static void LogPanicFallbackStep(string step, Exception ex)
        {
            try { App.Logger?.Warning("PANIC FALLBACK: {Step} step failed: {Error}", step, ex.Message); } catch { }
            VideoDiag.Log("PANIC", $"FALLBACK step '{step}' failed: {ex.Message}");
        }

        /// <summary>
        /// Repairs the one thing the off-thread teardown does behind the settings' back: the
        /// Awareness scanner was stopped (rightly, while the UI was wedged) with nothing to restart
        /// it, leaving the checkbox reading ON over a dead scanner. Queued so it lands as soon as
        /// the UI thread drains — after the panic op the watchdog gave up on, which was posted
        /// earlier and therefore runs first. Idempotent: Start no-ops if it is already running.
        /// </summary>
        private void QueuePanicFallbackRecovery()
        {
            try
            {
                DispatcherHelper.RunOnUI(() =>
                {
                    try
                    {
                        // Same three conditions as the real start path (MainWindow.Patreon.cs):
                        // the access check matters because entitlement can have lapsed since.
                        var settings = App.Settings?.Current;
                        if (settings?.ScreenOcrEnabled == true && settings.KeywordTriggersEnabled &&
                            KeywordTriggerService.HasAccess())
                            App.ScreenOcr?.Start();   // no-ops if it is already running
                    }
                    catch (Exception ex) { LogPanicFallbackStep("screen OCR restart", ex); }

                    VideoDiag.Log("PANIC", "FALLBACK recovery ran on the UI thread");
                });
            }
            catch (Exception ex) { LogPanicFallbackStep("recovery queue", ex); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Gaze Quick Recal global hotkey (Ctrl+Alt+G)
        //
        // Quick Recal is a ~3s one-dot nudge of an EXISTING calibration
        // (Windows/WebcamQuickRecalWindow.xaml.cs). It always worked; it was just
        // unreachable at the only moment anyone wants it, because all three of its
        // entry points are buttons inside setup cards buried in tabs — so correcting
        // mid-session drift meant abandoning whatever was on screen. This registers it
        // system-wide through the existing GlobalHotkeyService lane (slot 0xB1B3)
        // alongside the chat (0xB1B1) and camera (0xB1B2) hotkeys.
        //
        // NOT to be confused with the camera shortcut (Ctrl+Alt+K by default,
        // MainWindow.SessionIO.cs ApplyGlobalCameraHotkey): that one STARTS AND STOPS
        // the tracker. This one corrects drift and leaves tracking exactly as it found
        // it. Every label that quotes one quotes the other, for exactly that reason.
        //
        // COLLISION TRAP — read before rebinding. There are two independent ways this
        // chord can be taken, and only one of them reports anything:
        //   (a) another process already holds Ctrl+Alt+G. RegisterHotKey returns false,
        //       we log a Warning naming the chord, and the app carries on with the
        //       in-app buttons intact.
        //   (b) the user's PANIC key is bound to G. The panic key does NOT ride
        //       RegisterHotKey at all — it rides the single WH_KEYBOARD_LL hook this
        //       window owns (Services/Input/GlobalKeyboardHook.cs, contract note at
        //       App.xaml.cs:571), which sees the keystroke BEFORE any WM_HOTKEY
        //       delivery and matches on the bare key with the modifiers ignored. So
        //       Ctrl+Alt+G would fire panic and Quick Recal both, and NOTHING fails to
        //       register — case (a)'s Warning cannot catch it. The hook does not eat the
        //       press either (it only returns handled inside SuppressSystemKeys), so this
        //       is not a shadowing bug where Quick Recal quietly loses: both run, and the
        //       panic teardown is the destructive half. That is why the check below runs
        //       BEFORE Register and REFUSES the binding outright instead of arming it with
        //       a warning, and why both the arm line and the refusal line are logged at
        //       Information/Warning rather than Debug: "my Quick Recal stopped working"
        //       is then a one-line diff against the user's own bindings in app.log.
        //       Fix this class on the BINDING side only. Do not make the panic comparison
        //       modifier-aware: a flustered user reaching for panic with a stray Ctrl held
        //       must still get panic.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Key half of the Quick Recal chord. Named constant on purpose — the log
        /// lines, the tooltip and the in-window hint all render from it.</summary>
        internal const Key QuickRecalHotkeyKey = Key.G;

        /// <summary>Modifier half of the Quick Recal chord. Must stay MODIFIED: bare keys and
        /// bare function keys are exactly what users rebind panic/pause to, and those ride a
        /// low-level hook that would shadow this silently (see the trap note above).</summary>
        internal const ModifierKeys QuickRecalHotkeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;

        /// <summary>"Ctrl+Alt+G" — the single source of truth for every surface that quotes it.</summary>
        internal static string QuickRecalHotkeyChord => FormatChord(QuickRecalHotkeyModifiers, QuickRecalHotkeyKey);

        /// <summary>
        /// The CAMERA start/stop chord ("Ctrl+Alt+K" by default), rendered from the user's own
        /// setting. Exposed because every surface that quotes the Quick Recal chord has to quote
        /// this one beside it: two webcam hotkeys that read as interchangeable is the defect.
        /// Mirrors FormatCameraShortcut in MainWindow.SessionIO.cs, which is private to that file.
        /// </summary>
        internal static string CameraShortcutChord
        {
            get
            {
                var s = App.Settings?.Current?.CompanionPrompt;
                var keyName = string.IsNullOrWhiteSpace(s?.CameraShortcutKey) ? "K" : s!.CameraShortcutKey;
                if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.K;

                var mods = ModifierKeys.None;
                var modsName = string.IsNullOrWhiteSpace(s?.CameraShortcutModifiers) ? "Control,Alt" : s!.CameraShortcutModifiers;
                foreach (var part in modsName.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse<ModifierKeys>(part, ignoreCase: true, out var mk)) mods |= mk;
                }
                if (mods == ModifierKeys.None) mods = ModifierKeys.Control | ModifierKeys.Alt;

                return FormatChord(mods, key);
            }
        }

        /// <summary>
        /// The one line every surface uses to teach the chord. Names BOTH webcam hotkeys and says
        /// what separates them, so "Quick Recal" and "start/stop camera" can never read as the
        /// same button with two names.
        /// </summary>
        internal static string QuickRecalHotkeyHint()
            => Localization.Loc.GetF("webcam_quick_recal_hotkey_hint", QuickRecalHotkeyChord, CameraShortcutChord);

        /// <summary>Re-entrancy guard: the handler is async void and opens a modal, so a second
        /// press while the dot is up must not stack a second dialog.</summary>
        private bool _quickRecalHotkeyBusy;

        /// <summary>Shared "Ctrl+Alt+K" renderer for chord labels.</summary>
        private static string FormatChord(ModifierKeys mods, Key key)
        {
            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        /// <summary>
        /// Arms (or, when the setting is off or a global-hook binding would clash, disarms) the
        /// system-wide Quick Recal hotkey. Failure is never fatal: any refusal leaves the three
        /// in-app entry points working and only costs a Warning line naming the reason.
        /// </summary>
        private void ApplyGlobalQuickRecalHotkey()
        {
            try
            {
                var chord = QuickRecalHotkeyChord;

                if (App.Settings?.Current?.WebcamQuickRecalHotkeyEnabled == false)
                {
                    Services.GlobalHotkeyService.Unregister(Services.GlobalHotkeyService.QuickRecalHotkeyId);
                    App.Logger?.Information("Quick Recal hotkey {Chord} not armed: disabled in settings (WebcamQuickRecalHotkeyEnabled=false).", chord);
                    return;
                }

                // Case (b) from the trap note, checked BEFORE we register. The panic key AND the
                // optional pause key ride the WH_KEYBOARD_LL hook (GlobalKeyboardHook), which
                // compares the bare key and ignores modifiers — and it does NOT consume the
                // keystroke: HookCallback only returns handled inside the SuppressSystemKeys
                // lockdown branch, so the hook path invokes KeyPressed and still falls through to
                // CallNextHookEx. RegisterHotKey therefore fires too. With a panic key of "G" the
                // chord would run Quick Recal AND tear the whole session down. That is destructive,
                // not merely noisy, so refuse the binding rather than arm it with a warning. Quick
                // Recal stays reachable from its three buttons. Fix the class here, on the binding
                // side: the hook's event is Action<Key> and carries no modifier state, and making
                // the hook modifier-aware would be wrong anyway — someone reaching for panic with a
                // stray Ctrl held must get panic. PanicPolicy.HookBoundBaseKeys is the one list of
                // what is on that hook, so a future binding joins this guard by joining that set.
                var s = App.Settings?.Current;
                if (Services.Safety.PanicPolicy.FindHookClash(
                        QuickRecalHotkeyKey.ToString(), Services.Safety.PanicPolicy.HookBoundBaseKeys(s)) is { } clash)
                {
                    Services.GlobalHotkeyService.Unregister(Services.GlobalHotkeyService.QuickRecalHotkeyId);
                    App.Logger?.Warning(
                        "Quick Recal hotkey {Chord} NOT armed: it shares its base key with the {Binding} binding ({BoundKey}), and the " +
                        "global keyboard hook ignores modifiers without consuming the press — arming it would fire that binding " +
                        "on every Quick Recal (panic tears the session down; pause parks the video). Rebind it to free {Key}. " +
                        "The Quick Recal buttons in Settings → Devices, the Blink Trainer setup card and the Deeper setup card " +
                        "are unaffected.",
                        chord, clash.Name, clash.Key, QuickRecalHotkeyKey);
                    return;
                }

                bool ok = Services.GlobalHotkeyService.Register(
                    Services.GlobalHotkeyService.QuickRecalHotkeyId, this,
                    QuickRecalHotkeyModifiers, QuickRecalHotkeyKey,
                    // Win32 hotkeys arrive on the message-pump thread — marshal to the UI thread.
                    () => Dispatcher.BeginInvoke(new Action(OpenQuickRecalFromHotkey)));

                if (!ok)
                {
                    App.Logger?.Warning(
                        "Quick Recal hotkey {Chord} could not be registered — another process already holds that combination. " +
                        "Gaze drift correction is unaffected otherwise: the Quick Recal buttons in Settings → Devices, the Blink " +
                        "Trainer setup card and the Deeper setup card all still work.", chord);
                    return;
                }

                App.Logger?.Information(
                    "Quick Recal hotkey armed: {Chord} (mods={Mods}, key={Key}, slot=0x{Id:X}) — opens one-dot gaze drift correction " +
                    "and never starts or stops tracking.",
                    chord, QuickRecalHotkeyModifiers, QuickRecalHotkeyKey, Services.GlobalHotkeyService.QuickRecalHotkeyId);

            }
            catch (Exception ex)
            {
                // Never let shortcut wiring take the window's Loaded handler down with it.
                App.Logger?.Warning(ex, "ApplyGlobalQuickRecalHotkey failed");
            }
        }

        /// <summary>
        /// True while a full calibration or a Quick Recal is already on screen. Both are
        /// borderless-maximized topmost windows that own the gaze pipeline for their duration,
        /// so a second one must never be stacked on top.
        /// </summary>
        private static bool IsGazeCalibrationSurfaceOpen()
        {
            try
            {
                var windows = Application.Current?.Windows;
                if (windows == null) return false;
                foreach (System.Windows.Window w in windows)
                {
                    if (w is WebcamQuickRecalWindow || w is WebcamCalibrationWindow) return true;
                }
            }
            catch { /* window collection is only enumerable on the UI thread; we are on it */ }
            return false;
        }

        /// <summary>
        /// The hotkey's action. Must be safe to press at ANY moment, from any app, so every
        /// unmet precondition is a silent Debug no-op — no dialog, no exception. In particular
        /// it never opens the consent dialog: a consent prompt erupting over a fullscreen game
        /// because someone fat-fingered a chord is worse than doing nothing.
        /// </summary>
        private async void OpenQuickRecalFromHotkey()
        {
            if (_quickRecalHotkeyBusy)
            {
                App.Logger?.Debug("Quick Recal hotkey ignored: a Quick Recal is already in flight.");
                return;
            }

            try
            {
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
                {
                    App.Logger?.Debug("Quick Recal hotkey ignored: the app is shutting down.");
                    return;
                }

                // Re-check the hook clash HERE and not only at registration. PanicKeyEnabled is
                // written by LockdownService (:148/:189), RemoteControlService and preset loads —
                // any of which can turn a chord that was safe to arm at Loaded into a live clash
                // without passing through ApplyGlobalQuickRecalHotkey. Same set as the arm-time
                // check (panic key + pause key). We cannot stop the hook binding from firing (it
                // rides its own hook), but we can refuse to stack a calibration window on top of
                // the teardown, which is the genuinely bad outcome.
                var cfg = App.Settings?.Current;
                if (Services.Safety.PanicPolicy.FindHookClash(
                        QuickRecalHotkeyKey.ToString(), Services.Safety.PanicPolicy.HookBoundBaseKeys(cfg)) is { } live)
                {
                    App.Logger?.Warning(
                        "Quick Recal hotkey {Chord} suppressed at invocation: {Binding} is now bound to {BoundKey}, so this " +
                        "press is already firing it on the global hook. Not opening Quick Recal on top of it.",
                        QuickRecalHotkeyChord, live.Name, live.Key);
                    Services.GlobalHotkeyService.Unregister(Services.GlobalHotkeyService.QuickRecalHotkeyId);
                    return;
                }

                var svc = App.Webcam;
                if (svc == null)
                {
                    App.Logger?.Debug("Quick Recal hotkey ignored: App.Webcam is null (service not initialized).");
                    return;
                }

                if (!WebcamTrackingService.IsConsentCurrent())
                {
                    // Deliberately silent. The buttons prompt for consent because the user
                    // just asked for the feature by name; a global chord has no such mandate.
                    App.Logger?.Debug("Quick Recal hotkey ignored: webcam consent is not current.");
                    return;
                }

                if (svc.Calibration == null)
                {
                    App.Logger?.Debug("Quick Recal hotkey ignored: no calibration loaded — Quick Recal only nudges an existing one.");
                    return;
                }

                if (IsGazeCalibrationSurfaceOpen())
                {
                    App.Logger?.Debug("Quick Recal hotkey ignored: a calibration or Quick Recal window is already showing.");
                    return;
                }

                _quickRecalHotkeyBusy = true;

                // THE POINT OF THE WHOLE FEATURE: mid-session drift. If tracking is already
                // running we must leave it running when the dialog closes — stopping it would
                // kill the very session the user pressed the key to rescue. Only a tracker WE
                // started gets stopped again, which is the same leave-it-as-you-found-it
                // contract the setup-card buttons keep.
                bool startedHere = false;
                if (!svc.IsRunning)
                {
                    if (!await StartWebcamOffUiThreadAsync(svc))
                    {
                        App.Logger?.Debug("Quick Recal hotkey ignored: tracking would not start (state={State}).", svc.State);
                        return;
                    }
                    startedHere = true;
                }

                var dlg = new WebcamQuickRecalWindow();
                // An owner that is minimized (the app lives in the tray) can drag an owned
                // window down with it, and the whole premise here is that MainWindow is NOT
                // what the user is looking at. Only parent when this window is really on screen.
                if (IsVisible && WindowState != WindowState.Minimized) dlg.Owner = this;
                App.ApplyCalibrationScreenPlacement(dlg);
                var result = dlg.ShowDialog();

                App.Logger?.Information("Quick Recal via {Chord}: {Outcome} (startedTracking={Started}).",
                    QuickRecalHotkeyChord, result == true ? "applied" : "cancelled", startedHere);

                if (startedHere) svc.Stop();

                // Cross-tab propagation, same as the button paths.
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "OpenQuickRecalFromHotkey failed");
            }
            finally
            {
                _quickRecalHotkeyBusy = false;
            }
        }

        private void HandlePanicKeyPress()
        {
            // EMI Desk (MOMENTS 4.B): FIRST LINE, before any of the ladder below. panicPressed is a
            // HOLD with a five-minute silence tail, and it has to be armed even if something further
            // down this method throws - the whole point is that she says nothing after a panic.
            try { App.EmiDesk?.Fire("panicPressed", null); } catch { }

            VideoDiag.Log("PANIC", $"handling panic press (engineRunning={_isRunning}, uiStall={VideoDiag.UiStallMs}ms)");

            // #875: an open lock card outranks every hand-off below, so it is answered FIRST. A lock
            // card can coexist with a descent, the DtRH window or the feed — AutonomyService,
            // RemoteControlService and MantraLockScreenCommand all show one with no guard against them
            // — and the card is a fullscreen HWND_TOPMOST cover that steals focus from the game's own
            // Esc ladder. With DtRH up the hand-off below returns unconditionally, so the panic key
            // never reached StopAdHocEffects and the card had no exit at all: a permanent trap. The
            // press is consumed here (like the video grace pause) and deliberately does NOT advance the
            // press ladder, so it can never be the tap that exits the app.
            // IsAnyOpen also covers a deferred show still pending; cancelling that is the right answer
            // to panic too, and DismissAll clears both, so the next press falls through normally.
            bool lockCardOpen = LockCardWindow.IsAnyOpen();

            // Rung 2, settled here for BOTH modes. Escape is the DEFAULT panic key and the LL hook
            // delivers it whatever has focus, so an Escape aimed at "close the Ctrl+K palette"
            // arrives here too. It closes the palette and NOTHING else: a user who opens the
            // quick-settings palette mid-session to nudge a slider and dismisses it the normal way
            // must not lose the session's effects, get a Relapse panic tracked and be docked 100 XP
            // for it. That is exactly what the pre-6.8.5 ladder did with this press.
            // Gated on the panic key really being Escape, so a user who rebound panic to F8 still
            // gets a real panic from F8 while the palette is open; the palette then closes itself
            // through its own Esc handler and never sees this path. TryConsumeEscape carries a
            // short grace window, so the press is claimed exactly once whichever of the two
            // deliveries (WPF KeyDown vs the hook's queued handler) lands first - and it is only
            // ASKED when no lock card is open, because asking closes the palette as a side effect.
            bool paletteClaimed = !lockCardOpen
                && string.Equals(App.Settings?.Current?.PanicKey, "Escape", StringComparison.OrdinalIgnoreCase)
                && SettingsPaletteWindow.TryConsumeEscape();

            var rung = Services.Safety.PanicPolicy.Decide(
                lockCardOpen: lockCardOpen,
                paletteClaimedPress: paletteClaimed,
                overrideAll: Services.Safety.PanicPolicy.OverrideEnabled(App.Settings?.Current));

            if (rung == Services.Safety.PanicPolicy.Rung.DismissLockCard)
            {
                VideoDiag.Log("PANIC", "dismissing the open lock card (it outranks every hand-off)");
                App.LockCard?.Stop(dismissOpenCards: true);
            }
            else if (rung == Services.Safety.PanicPolicy.Rung.DismissSettingsPalette)
            {
                VideoDiag.Log("PANIC", "press consumed by the Ctrl+K palette (palette closed, nothing stopped)");
            }

            // Both dismiss rungs answer the surface that owns the press and stop THERE: no stop
            // pass, no engine stop, no session pause and its 100 XP, no Relapse panic tracked, no
            // exit-ladder advance. That is the pre-6.8.5 behaviour for both of them, and for the
            // palette it is the whole point - Escape is the default panic key AND the universal
            // "close this popup" key.
            if (!Services.Safety.PanicPolicy.StopsSurfaces(rung)) return;

            // v6.8.5 (#1054/#1066, suggestion thread 1541736938703167550 - "panic button is panic
            // button"). With PanicOverridesAll on (the default) the press is NOT handed to whatever
            // owns the screen and is NOT spent as the #735 grace pause: every surface goes down in
            // ONE dispatcher pass, then the normal stop tail runs. Reporters were spamming the key
            // through a six-rung ladder while the screen flickered between owners.
            if (rung == Services.Safety.PanicPolicy.Rung.StopEverything)
            {
                // Sampled BEFORE the stop pass closes them. A press that takes a mini-game or the
                // feed down must not also arm the double-press "quit the app" tap: the legacy
                // ladder handed that press to the game and returned, so the counter never moved,
                // and a reflexive Esc-Esc inside the Arcademy would otherwise now exit the whole
                // app (see PanicPolicy.AdvancesExitLadder(Rung, bool)).
                bool gameOwnedTheScreen = AnyGameSurfaceOwnsTheScreen();

                VideoDiag.Log("PANIC", $"override mode - stopping every surface in one pass (gameOnScreen={gameOwnedTheScreen})");
                PanicStopEverySurface();
                RunPanicStopTail(advanceExitLadder:
                    Services.Safety.PanicPolicy.AdvancesExitLadder(rung, gameOwnedTheScreen));
                return;
            }

            // A live Rabbit Hole descent owns the panic key: the chaos key hook pauses the
            // run (and a second press surfaces it). Without this hand-off a mid-run panic
            // fell into the "not running" branch below — where a second press EXITS the app.
            if (App.Chaos?.IsDescending == true) { VideoDiag.Log("PANIC", "handed off to the Rabbit Hole descent (chaos owns the key)"); return; }

            // The web DtRH game (its own WebView2 window) owns Esc while it's up: the page
            // runs a pause → exit-fullscreen → close ladder. Swallow the panic double-tap so
            // those presses can't fall into the "not running" branch and exit the whole app.
            // Reactivates on its own once the game window closes (IsActive flips false).
            if (Services.Chaos.DtrhHostService.IsActive) { VideoDiag.Log("PANIC", "handed off to the DtRH web game window"); return; }

            // The Arcademy (its own WebView2 window) owns the panic key while it's up, same shape as
            // the DtRH hand-off above and for the same reason: without this rung, two Esc taps with
            // no session running fell straight through to the "not running" branch below and EXITED
            // THE WHOLE APP from inside a mini-game. Its own two-rung ladder is press 1 = suspend
            // (every effect dropped, the class frozen behind a Resume card) and press 2 within 2s =
            // close the Arcademy and restore the control panel, so the emergency stop still stops
            // everything and nobody is trapped. Reactivates on its own once the window closes.
            if (Services.Arcademy.ArcademyHostService.IsActive)
            {
                VideoDiag.Log("PANIC", "handed off to the Arcademy window (suspend, then close)");
                Services.Arcademy.ArcademyHostService.HandlePanicPress();
                return;
            }

            // For You feed: a two-rung ladder. Press 1 drops ghost mode if the feed is parked as a
            // see-through mirror (otherwise the user is staring at a translucent pane they cannot
            // grab — the mouse passes straight through it, its own close button included), press 2
            // closes the feed. The press is consumed either way (no fall-through into the "not
            // running" exit branch). The panic key reaches us regardless of focus: it rides the
            // WH_KEYBOARD_LL hook in OnGlobalKeyPressed, not a window-level handler.
            if (Services.Fyp.FypHostService.IsActive)
            {
                if (Services.Fyp.FypHostService.IsGhosted)
                {
                    VideoDiag.Log("PANIC", "For You feed: ghost mode dropped (press again to close)");
                    Services.Fyp.FypHostService.ExitGhost();
                    return;
                }
                if (Services.Fyp.FypHostService.RecentlyUnghosted)
                {
                    // The reflexive double-tap right after rung 1 — swallow it instead of
                    // taking the whole feed down (play-tested: one Esc-Esc closed everything).
                    VideoDiag.Log("PANIC", "For You feed: press ignored (just un-ghosted)");
                    return;
                }
                VideoDiag.Log("PANIC", "closing the For You feed window");
                Services.Fyp.FypHostService.Close();
                return;
            }

            // #735 "grace pause": while a mandatory video is really on screen, the FIRST panic press
            // pauses it behind a small Paused/Resume card instead of stopping the engine — the user
            // may be pausing because someone walked in, and a bark, an achievement track and a whole
            // session teardown are all the wrong answer to that. Deliberately placed AFTER the two
            // chaos/DtRH hand-offs (their panic behaviour must not change) and BEFORE the bark, so a
            // grace pause is silent. One per video run; press 2 falls straight through to everything
            // below, press 3 exits the app — three taps from a playing video to exit, as before + 1.
            //
            // The early return leaves _panicPressCount/_lastPanicTime untouched ON PURPOSE: the pause
            // is not a rung of the ladder, so it must not advance (or reset) it.
            if (App.Video?.TryGracePauseFromPanic() == true)
            {
                VideoDiag.Log("PANIC", "press consumed as video grace pause");
                return;
            }

            // Let the companion say a calm, persona-neutral safety line (highest priority,
            // bypasses the bark gate). Fired before the stop flow so it's not suppressed.
            App.Bark?.NotifyPanic();

            // Dismiss any open/pinned help popover so it never lingers over a panic.
            Controls.HelpPopover.CloseActive();

            // Stop standalone Lab minigames first — they run independently of
            // the main engine, so the rest of the panic flow won't touch them.
            App.BlinkTrainer?.Stop();

            RunPanicStopTail(advanceExitLadder: true);
        }

        /// <summary>
        /// TRUE while one of the surfaces that used to CONSUME a panic press on its own rung owns
        /// the screen: a live Rabbit Hole descent, the DtRH window, the Arcademy, the For You feed
        /// or Just Drop. Read once, before the stop pass closes them. Never throws - a dead host
        /// service must not be able to eat a panic press.
        /// </summary>
        private static bool AnyGameSurfaceOwnsTheScreen()
        {
            try
            {
                return App.Chaos?.IsDescending == true
                    || Services.Chaos.DtrhHostService.IsActive
                    || Services.Arcademy.ArcademyHostService.IsActive
                    || Services.Fyp.FypHostService.IsActive
                    || Services.JustDrop.JustDropHostService.IsActive;
            }
            catch (Exception ex)
            {
                try { App.Logger?.Warning("PANIC: game-surface probe failed: {Error}", ex.Message); } catch { }
                return false;
            }
        }

        /// <summary>
        /// The shared end of every panic press that is NOT consumed by a hand-off: the 2 second
        /// press counter, the running/not-running stop, and the double-press exit. Extracted in
        /// v6.8.5 so the new "panic stops everything" mode and the legacy hand-off ladder finish
        /// through exactly the same code instead of growing a second copy of it.
        /// </summary>
        /// <param name="advanceExitLadder">FALSE for a press that was spent on a surface which must
        /// never be the tap that quits the app: an open Lock Card, or (when Escape is the panic key)
        /// an Escape the Ctrl+K palette claimed. The stop still runs; only the exit counter is left
        /// alone. See <see cref="Services.Safety.PanicPolicy.AdvancesExitLadder"/>.</param>
        private void RunPanicStopTail(bool advanceExitLadder)
        {
            var now = DateTime.Now;
            var timeSinceLastPress = (now - _lastPanicTime).TotalMilliseconds;
            
            // Reset counter if more than 2 seconds since last press
            if (timeSinceLastPress > 2000)
            {
                _panicPressCount = 0;
            }
            
            if (advanceExitLadder)
            {
                _panicPressCount++;
                _lastPanicTime = now;
            }
            
            if (_isRunning)
            {
                // First press while running: stop engine, pause session if active
                App.Logger?.Information("Panic key pressed! Stopping engine...");

                // IMMEDIATELY kill ALL audio before anything else
                App.KillAllAudio();

                // Cancel any active autonomy pulses (restore original settings)
                App.Autonomy?.CancelActivePulses();

                // Track panic press for Relapse achievement (must be before stopping session)
                App.Achievements?.TrackPanicPressed();

                // Pause session if one is running (instead of stopping it)
                bool sessionWasPaused = false;
                if (_sessionEngine != null && _sessionEngine.IsRunning && !_sessionEngine.IsPaused)
                {
                    _sessionEngine.PauseSession();
                    sessionWasPaused = true;
                }

                // Remember if autonomy was running before we stop everything
                bool autonomyWasRunning = App.Autonomy?.IsEnabled == true;

                StopEngine();

                // Reset interaction queue to clear any pending queued items
                App.InteractionQueue?.ForceReset();

                // Restart autonomy if it was running — panic should skip the current action, not kill autonomy
                if (autonomyWasRunning && !sessionWasPaused)
                {
                    App.Autonomy?.Start();
                    App.Logger?.Information("Panic key: Restarted autonomy after skipping current action");
                }

                // Restore window - always show and bring to front
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;  // Temporarily topmost to ensure it's visible
                Topmost = false; // Then disable topmost
                App.Overlay?.NotifyTopWindowClosed();
                ShowAvatarTube();

                if (sessionWasPaused)
                {
                    // Update pause button to show resume icon
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "▶";
                    if (BtnPauseSession != null) BtnPauseSession.ToolTip = Loc.Get("tooltip_resume_session");
                }
            }
            else
            {
                // Engine isn't running, but ad-hoc effects (voice commands, dashboard one-shots,
                // Deeper) can still be live and were previously left untouched by a first press —
                // so Esc appeared to "do nothing" when stopping voice-triggered spiral / bouncing
                // text / pink. Tear those down on every press here (all idempotent no-ops when
                // nothing is active, so the double-press-to-exit path below is unaffected).
                StopAdHocEffects();
            }

            if (!_isRunning && _panicPressCount >= 2)
            {
                // Second press while stopped: exit application
                App.Logger?.Information("Double panic! Exiting application...");

                // IMMEDIATELY kill ALL audio before anything else
                App.KillAllAudio();

                // Stop session if one is paused before exiting
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                {
                    _sessionEngine.StopSession(completed: false);
                }

                // CRITICAL: Force close all video windows SYNCHRONOUSLY before exit
                // LibVLC windows become orphaned if we exit without proper cleanup
                App.Video?.ForceCleanup(synchronous: true);
                BubbleCountWindow.ForceCloseAll();
                BubbleCountResultWindow.ForceCloseAll();

                // Give LibVLC a moment to release native resources
                Thread.Sleep(100);

                _exitRequested = true;
                SaveSettings();
                _keyboardHook?.Dispose();
                _trayIcon?.Dispose();
                _browser?.Dispose();
                Application.Current.Shutdown();
            }

            // Panic action COMPLETED. Its absence after a "handling panic press" line is the proof
            // that the panic teardown itself hung rather than the keystroke being lost (#616-#623).
            VideoDiag.Log("PANIC", $"panic press handled (press #{_panicPressCount})");
        }

        /// <summary>
        /// v6.8.5 override mode: take EVERY live surface down in one dispatcher pass, then let
        /// <see cref="RunPanicStopTail"/> stop the engine. This is the whole point of the fix - the
        /// old ladder handed the press to whichever mini-game or video owned the screen and the
        /// engine only stopped two or three presses later.
        ///
        /// <para>Rules this body must keep:</para>
        /// <list type="bullet">
        /// <item>EVERY step gets its own try/catch. One dead service must never eat the panic.</item>
        /// <item>No grace pause. <see cref="VideoService.TryGracePauseFromPanic"/> refuses in this
        /// mode anyway (see PanicPolicy.AllowGracePauseFromPanicKey); the video is force-cleaned.</item>
        /// <item>No companion bark. The legacy ladder says a calm safety line here; in override
        /// mode the tube is one of the surfaces being silenced, so speaking and then clearing the
        /// bubble in the same pass would just be noise.</item>
        /// <item>An open Lock Card never reaches this method - it is answered one rung above and
        /// keeps its own contract untouched.</item>
        /// </list>
        ///
        /// <para>UI thread only: the panic handler is already queued on the dispatcher, and the
        /// off-thread last resort is <see cref="RunEmergencyPanicTeardown"/>, not this.</para>
        /// </summary>
        private void PanicStopEverySurface()
        {
            static void Step(string name, Action action)
            {
                try { action(); }
                catch (Exception ex)
                {
                    try { App.Logger?.Warning("PANIC stop-all: {Step} failed: {Error}", name, ex.Message); } catch { }
                    VideoDiag.Log("PANIC", $"stop-all step '{name}' failed: {ex.Message}");
                }
            }

            // --- media ---
            // ForceCleanup, not Stop: ends the run outright instead of scheduling a replacement,
            // and closes the LibVLC window. Asynchronous - a synchronous cleanup pumps the
            // dispatcher, and this pass must stay one pass.
            Step("video", () => App.Video?.ForceCleanup(synchronous: false));
            Step("video enhance bridge", () => App.VideoEnhanceBridge?.ForceUnbind());
            Step("flashes", () => App.Flash?.Stop());
            Step("bubbles", () => App.Bubbles?.Stop());
            Step("bubble count", () => App.BubbleCount?.Stop());
            Step("subliminals", () => App.Subliminal?.Stop());
            Step("bouncing text", () => App.BouncingText?.Stop());
            Step("mind wipe", () => App.MindWipe?.Stop());
            Step("brain drain", () => App.BrainDrain?.Stop());

            // --- overlays ---
            // Stop() already clears _isRunning and calls StopPinkFilter/StopSpiral/StopBrainDrainBlur,
            // and RefreshOverlays early-returns while !_isRunning, so no reconcile tick can repaint
            // them. Deliberately NOT EnablePinkFilter(false)/EnableSpiral(false): those write the
            // user's PERSISTENT feature switches, so every panic would leave Spiral and Pink Filter
            // switched off for all their later manual runs. A panic stops what is on screen; it does
            // not reconfigure the app.
            Step("spiral", () => App.Overlay?.StopSpiral());
            Step("pink filter", () => App.Overlay?.StopPinkFilter());
            Step("overlays", () => App.Overlay?.Stop());
            // The SESSION-scoped corner overlay first, because CornerGifService does not own it:
            // it is SessionEngine's own window (ticket 1539282547484139682). Without this step one
            // panic press stopped everything else on a program day and left the session spiral
            // spinning. It closes hide-only (no handback), so it cannot re-queue a standalone slot.
            Step("session corner GIF", () => SessionEngine.Active?.PanicCloseCornerGif());
            // ...and the standalone Spiral-card slots LAST, so this is the final word on the corner
            // whatever the step above did. StopAll also cancels queued realizations, so a slot that
            // was mid-stagger cannot land after the pass.
            Step("corner GIFs", () => App.CornerGif?.StopAll());

            // --- companion tube ---
            Step("tube speech", () => _avatarTubeWindow?.PanicSilence());
            Step("avatar voice", () => App.AvatarWindow?.StopVoiceLineAudio());

            // --- game / feed windows ---
            Step("chaos", () => App.Chaos?.ForceShutdown());
            Step("DtRH", () => Services.Chaos.DtrhHostService.CloseActive());
            Step("Arcademy", () => Services.Arcademy.ArcademyHostService.CloseActive());
            Step("For You feed", () => Services.Fyp.FypHostService.Close());
            Step("Just Drop", () => Services.JustDrop.JustDropHostService.CloseActive());
            Step("Lab minigames", () => App.BlinkTrainer?.Stop());

            // --- modal / topmost cards ---
            Step("pop quiz", () => App.PopQuiz?.Stop());
            Step("quiz windows", () => QuizWindow.ForceCloseAll());
            Step("pop quiz windows", () => PopQuizWindow.ForceCloseAll());
            Step("bubble count windows", () => { BubbleCountWindow.ForceCloseAll(); BubbleCountResultWindow.ForceCloseAll(); });
            Step("help popover", () => Controls.HelpPopover.CloseActive());
            // CloseIfOpen, NOT TryConsumeEscape: a press the palette claimed never reaches this
            // pass (it is its own rung and returns), so anything still open here is a palette that
            // did NOT claim the press - e.g. panic rebound to F8. Calling TryConsumeEscape here
            // would burn the Escape grace window the caller's decision depends on.
            Step("settings palette", SettingsPaletteWindow.CloseIfOpen);

            // --- audio + hardware ---
            Step("haptics", () => App.Haptics?.PanicStop());
            Step("autonomy pulses", () => App.Autonomy?.CancelActivePulses());
            Step("audio layers", () =>
            {
                // #668: a standalone Audio Layers master means the bed is the user's, not a session's.
                if (App.Settings?.Current?.AudioLayersEnabled != true) App.LayeredAudio?.Stop();
            });
            Step("audio unduck", () => App.Audio?.ForceUnduck());
            // Belt and braces LAST: KillAllAudio is a single try/catch, so anything it would have
            // skipped after an early throw has already been done individually above.
            Step("kill all audio", App.KillAllAudio);

            Step("interaction queue", () => App.InteractionQueue?.ForceReset());

            VideoDiag.Log("PANIC", "stop-all pass complete");
        }

        /// <summary>
        /// Stops every "ad-hoc" effect that can be live without the engine running — i.e. effects
        /// fired by voice commands ("She's Listening"), dashboard one-shots, or Deeper. The normal
        /// stop paths assume a running session/overlay reconcile loop; these surfaces don't, so the
        /// panic key must tear them down explicitly. Every call is idempotent, so this is a no-op
        /// when nothing is active.
        /// </summary>
        private void StopAdHocEffects()
        {
            try
            {
                App.KillAllAudio();
                App.Video?.Stop();
                App.Flash?.Stop();
                App.Subliminal?.Stop();
                App.Bubbles?.Stop();
                App.BouncingText?.Stop();
                App.BubbleCount?.Stop();
                App.MindWipe?.Stop();
                App.BrainDrain?.Stop();
                App.LockCard?.Stop(dismissOpenCards: true);   // panic: the card on screen is the point

                // Clear the settings flags so a running reconcile loop won't recreate them, then
                // stop the windows directly — voice/Deeper start spiral & pink ad-hoc (no reconcile
                // loop), so RefreshOverlays() (gated on the service's IsRunning) can't see them.
                EnablePinkFilter(false);
                EnableSpiral(false);
                App.Overlay?.RefreshOverlays();
                App.Overlay?.StopPinkFilter();
                App.Overlay?.StopSpiral();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Panic: StopAdHocEffects failed");
            }
        }

        private void UpdatePanicKeyButton()
        {
            if (AppSettingsTab.BtnPanicKey != null)
            {
                var currentKey = App.Settings.Current.PanicKey;
                AppSettingsTab.BtnPanicKey.Content = _isCapturingPanicKey ? "Press any key..." : $"🔑 {currentKey}";
            }
        }

        /// <summary>Mirrors <see cref="UpdatePanicKeyButton"/> for the optional Pause key. An empty
        /// binding reads as the localized "not set" rather than an empty button.</summary>
        internal void UpdatePauseKeyButton()
        {
            if (AppSettingsTab?.BtnPauseKey == null) return;
            var currentKey = App.Settings?.Current?.PauseKey ?? "";
            AppSettingsTab.BtnPauseKey.Content = _isCapturingPauseKey
                ? "Press any key..."
                : (string.IsNullOrEmpty(currentKey) ? Loc.Get("btn_pause_key_unbound") : $"⏸ {currentKey}");
        }

        // ---- velvet-mosaic: internal wrappers called by popup feature UserControls ----
        // These delegate complex system-level operations (assets, panic key, offline mode,
        // no-panic) to the existing private handlers so the popup doesn't duplicate logic.

        internal void RequestPickAssetsFolder()
        {
            BtnPickAssetsFolder_Click(this, new RoutedEventArgs());
        }

        internal void RequestBeginPanicKeyCapture()
        {
            BtnPanicKey_Click(this, new RoutedEventArgs());
        }

        internal void RequestToggleOfflineMode(bool enable)
        {
            // Drive the existing handler via the one live checkbox (Settings · Data since Phase 2)
            // so the two-way sync logic (UpdateOfflineModeUI, login button disable, etc.) runs
            // exactly once.
            if (AppSettingsTab.ChkOfflineMode == null) return;
            if ((AppSettingsTab.ChkOfflineMode.IsChecked ?? false) == enable) return;
            AppSettingsTab.ChkOfflineMode.IsChecked = enable;
        }

        internal void RequestToggleNoPanic(bool disablePanic)
        {
            if (AppSettingsTab.ChkNoPanic == null) return;
            if ((AppSettingsTab.ChkNoPanic.IsChecked ?? false) == disablePanic) return;
            AppSettingsTab.ChkNoPanic.IsChecked = disablePanic;
        }

        /// <summary>
        /// Applies no-panic mode change directly (for use by feature popups).
        /// Returns true if the change was applied, false if cancelled.
        /// </summary>
        internal bool ApplyNoPanic(bool disablePanic, Window dialogOwner)
        {
            if (disablePanic)
            {
                var confirmed = WarningDialog.ShowDoubleWarning(dialogOwner,
                    "Disable Panic Key",
                    "• You will have NO emergency escape option\n" +
                    "• The ONLY way to exit will be the Exit button\n" +
                    "• Combined with Strict Lock, this is VERY restrictive\n" +
                    "• Make sure you know what you're doing!");

                if (!confirmed) return false;

                if (App.Settings.Current.KeywordTriggersEnabled != true)
                    _keyboardHook?.Stop();
                App.Settings.Current.PanicKeyEnabled = false;
                App.Settings?.Save();
                App.Logger?.Information("Keyboard hook stopped - panic key disabled");
            }
            else
            {
                _keyboardHook?.Start();
                App.Settings.Current.PanicKeyEnabled = true;
                App.Settings?.Save();
                App.Logger?.Information("Keyboard hook started - panic key enabled");
            }

            // Sync MainWindow checkbox without triggering handler
            _isLoading = true;
            AppSettingsTab.ChkNoPanic.IsChecked = disablePanic;
            _isLoading = false;

            return true;
        }

        /// <summary>
        /// Applies offline mode change directly (for use by feature popups).
        /// Returns true if the change was applied, false if cancelled.
        /// </summary>
        internal bool ApplyOfflineMode(bool enable, Window dialogOwner)
        {
            if (enable)
            {
                if (string.IsNullOrWhiteSpace(App.Settings.Current.OfflineUsername))
                {
                    var dialog = new OfflineUsernameDialog();
                    dialog.Owner = dialogOwner;
                    dialog.Topmost = true;

                    if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Username))
                    {
                        App.Settings.Current.OfflineUsername = dialog.Username;
                    }
                    else
                    {
                        return false;
                    }
                }

                App.Settings.Current.OfflineMode = true;
                DisconnectNetworkServices();
                App.Logger?.Information("Offline mode enabled with username '{Username}'",
                    App.Settings.Current.OfflineUsername);
            }
            else
            {
                App.Settings.Current.OfflineMode = false;
                App.Logger?.Information("Offline mode disabled");
            }

            UpdateOfflineModeUI(enable);
            App.Settings.Save();

            // Sync the Settings · Data checkbox without triggering handler
            _isLoading = true;
            AppSettingsTab.ChkOfflineMode.IsChecked = enable;
            _isLoading = false;

            return true;
        }

        /// <summary>
        /// Syncs the keyboard hook and MainWindow NoPanic checkbox after the setting changes externally.
        /// <para>
        /// Callers are the non-UI writers of <c>PanicKeyEnabled</c>: LockdownService (activate /
        /// deactivate / crash recovery) and RemoteControlService (enable_panic / disable_panic and
        /// the two stop-effects cleanups). They must call this rather than writing the flag alone -
        /// the keyboard hook is started/stopped here, and the checkbox is the surface the user sees.
        /// </para>
        /// </summary>
        internal void SyncNoPanicState()
        {
            var panicEnabled = App.Settings.Current.PanicKeyEnabled;
            if (panicEnabled)
            {
                _keyboardHook?.Start();
                App.Logger?.Information("Keyboard hook started - panic key enabled");
            }
            else
            {
                if (App.Settings.Current.KeywordTriggersEnabled != true)
                    _keyboardHook?.Stop();
                App.Logger?.Information("Keyboard hook stopped - panic key disabled");
            }

            _isLoading = true;
            AppSettingsTab.ChkNoPanic.IsChecked = !panicEnabled;
            _isLoading = false;
        }

        /// <summary>
        /// Syncs the MainWindow offline mode UI after the setting changes externally.
        /// <para>
        /// Kept for non-UI callers. Everything that flips <c>OfflineMode</c> today goes through
        /// <see cref="ApplyOfflineMode"/> or ChkOfflineMode_Changed, both of which already do this
        /// work inline; anything new that writes the flag directly must call this instead, because
        /// SaveSettings deliberately no longer re-derives OfflineMode from the checkbox.
        /// </para>
        /// </summary>
        internal void SyncOfflineModeState()
        {
            var isOffline = App.Settings.Current.OfflineMode;
            if (isOffline)
                DisconnectNetworkServices();
            UpdateOfflineModeUI(isOffline);

            _isLoading = true;
            AppSettingsTab.ChkOfflineMode.IsChecked = isOffline;
            _isLoading = false;
        }

        internal bool RequestToggleWindowsStartup(bool enable)
        {
            // Settings · General's ChkWinStart uses a Click handler, which doesn't fire on
            // programmatic IsChecked changes — so just toggling the checkbox here would
            // silently skip StartupManager.SetStartupState and the OS shortcut would never
            // be created/removed. Drive the registration ourselves and mirror the result
            // onto the checkbox for any code that still reads it.
            if (AppSettingsTab.ChkWinStart == null) return StartupManager.IsRegistered();
            if ((AppSettingsTab.ChkWinStart.IsChecked ?? false) == enable && StartupManager.IsRegistered() == enable)
                return enable;

            if (!StartupManager.SetStartupState(enable))
            {
                MessageBox.Show(this,
                    Loc.Get("msg_failed_to_update_startup"),
                    Loc.Get("title_startup_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                var actual = StartupManager.IsRegistered();
                _isLoading = true;
                try { AppSettingsTab.ChkWinStart.IsChecked = actual; } finally { _isLoading = false; }
                App.Settings.Current.RunOnStartup = actual;
                App.Settings.Save();
                return actual;
            }

            _isLoading = true;
            try { AppSettingsTab.ChkWinStart.IsChecked = enable; } finally { _isLoading = false; }
            App.Settings.Current.RunOnStartup = enable;
            App.Settings.Save();
            return enable;
        }

        private void LoadLogo()
        {
            try
            {
                // Use mod resource resolver for logo — allows mod overrides.
                // logo.png is the Bambi-branded wordmark; logo2.png is the neutral
                // "Conditioning Control Panel" wordmark used by CCP Default and Sissy.
                var useNeutralLogo = App.Mods?.IsCCPDefault == true
                                     || App.Settings?.Current?.IsSissyMode == true;
                var logoFile = useNeutralLogo ? "logo2.png" : "logo.png";
                var image = Services.ModResourceResolver.ResolveImage(logoFile);
                if (image != null)
                    SettingsTab.ImgLogo.Source = image;
                App.Logger?.Debug("Logo loaded: {Logo}", logoFile);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load logo: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Loads the takeover feature image based on current content mode.
        /// </summary>
        private void LoadTakeoverImage()
        {
            try
            {
                // Update mod-aware takeover labels. ImgTakeover and TxtTakeoverHeader
                // were removed when the Bambi feature image moved out of the Exclusives
                // page into BambiTakeoverTab — guard the legacy element references.
                var takeoverLabel = App.Mods?.GetTakeoverLabel() ?? "Bambi Takeover";
                if (BambiTakeoverTab.TxtTakeoverLocked != null) BambiTakeoverTab.TxtTakeoverLocked.Text = $"🤖 {takeoverLabel}";
                if (BambiTakeoverTab.TxtTakeoverUnlocked != null) BambiTakeoverTab.TxtTakeoverUnlocked.Text = $"🤖 {takeoverLabel}";
                if (BambiTakeoverTab.BtnAutonomyStartStop != null)
                    BambiTakeoverTab.BtnAutonomyStartStop.ToolTip = Loc.GetF("tooltip_start_stop_takeover", takeoverLabel);
                // Phase 2: the Support Development card (and its RunPatreonFeatures inline Run)
                // moved from PatreonTab to Settings/Account.
                if (AppSettingsTab?.RunPatreonFeatures != null)
                    AppSettingsTab.RunPatreonFeatures.Text = Loc.GetF("label_patreon_features", takeoverLabel);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load takeover image: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Refreshes UI elements that need manual updates when theme changes.
        /// Updates Application.Current.Resources Color and Brush entries so all
        /// DynamicResource bindings across the app auto-update.
        /// Also updates named elements that use direct property assignment.
        /// </summary>
        private void RefreshThemeAwareElements()
        {
            try
            {
                var accentHex = App.Mods?.GetAccentColorHex() ?? "#FF69B4";
                var darkHex = App.Mods?.GetAccentDarkColorHex() ?? "#FF1493";
                var lightHex = App.Mods?.GetAccentLightColorHex() ?? "#FF8FAF";
                var secondaryHex = App.Mods?.GetSecondaryColorHex() ?? "#9B59B6";

                var accent = (Color)ColorConverter.ConvertFromString(accentHex);
                var dark = (Color)ColorConverter.ConvertFromString(darkHex);
                var light = (Color)ColorConverter.ConvertFromString(lightHex);
                var secondary = (Color)ColorConverter.ConvertFromString(secondaryHex);
                var transparent30 = Color.FromArgb(0x30, accent.R, accent.G, accent.B);
                var transparent20 = Color.FromArgb(0x20, accent.R, accent.G, accent.B);
                var accentPressed = Color.FromArgb(0xFF,
                    (byte)Math.Max(0, accent.R - 30),
                    (byte)Math.Max(0, accent.G - 30),
                    (byte)Math.Max(0, accent.B - 30));

                // === BACKGROUND COLORS (mod-customizable) ===
                var bgHex = App.Mods?.GetBackgroundColorHex() ?? "#1A1A2E";
                var panelHex = App.Mods?.GetPanelColorHex() ?? "#252542";
                var surfaceHex = App.Mods?.GetSurfaceColorHex() ?? "#1E1E3A";

                var bgColor = (Color)ColorConverter.ConvertFromString(bgHex);
                var panelColor = (Color)ColorConverter.ConvertFromString(panelHex);
                var surfaceColor = (Color)ColorConverter.ConvertFromString(surfaceHex);

                // THE FUSE'S DIMMING (CONTRACT-FUSE-0816 §2.2). In the last 24 hours before the
                // ceremony the neutral chrome walks toward the ceremony's black, one step per six
                // hours. Zero steps on every install that has no countdown — which is all of them
                // until the owner arms DESCENT_CEREMONY_AT — and DescentFuseChrome.Dim returns its
                // input unchanged at step 0, so this is a no-op with a null check in front of it.
                //
                // SEPARATE VARIABLES ON PURPOSE. Only the neutral block below sees the dimmed
                // trio; the accent-tinted backgrounds further down still blend onto the mod's TRUE
                // bgColor. That is the hard law from the contract — the fuse darkens the room, it
                // never restyles somebody's mod.
                //
                // This is also the whole restore path: the step drops to 0 (kill switch, or the
                // ceremony passing) and re-running this method repaints the original palette. There
                // is no cached "before" state to leak, because the originals are re-read from the
                // active mod on every call.
                var dimStep = Services.Descent.DescentFuseChrome.CurrentStep;
                var neutralBg = Services.Descent.DescentFuseChrome.Dim(bgColor, dimStep);
                var neutralPanel = Services.Descent.DescentFuseChrome.Dim(panelColor, dimStep);
                var neutralSurface = Services.Descent.DescentFuseChrome.Dim(surfaceColor, dimStep);

                // Auto-computed derivatives
                var panelAccentColor = LightenColor(neutralPanel, 0.15);
                var panelAccentHoverColor = LightenColor(neutralPanel, 0.25);
                var previewBgColor = DarkenColor(neutralBg, 0.15);
                var panelBgTransparent = Color.FromArgb(0xB0, neutralPanel.R, neutralPanel.G, neutralPanel.B);

                var res = Application.Current.Resources;

                // Update background Color resources
                res["DarkerBg"] = neutralBg;
                res["PanelBg"] = neutralPanel;
                res["SurfaceBg"] = neutralSurface;
                res["PanelAccent"] = panelAccentColor;
                res["PanelAccentHover"] = panelAccentHoverColor;
                res["PreviewBg"] = previewBgColor;
                res["PanelBgTransparent"] = panelBgTransparent;

                // Update background Brush resources
                res["DarkerBgBrush"] = new SolidColorBrush(neutralBg);
                res["PanelBgBrush"] = new SolidColorBrush(neutralPanel);
                res["SurfaceBgBrush"] = new SolidColorBrush(neutralSurface);
                res["PanelAccentBrush"] = new SolidColorBrush(panelAccentColor);
                res["PanelAccentHoverBrush"] = new SolidColorBrush(panelAccentHoverColor);
                res["PreviewBgBrush"] = new SolidColorBrush(previewBgColor);
                res["PanelBgTransparentBrush"] = new SolidColorBrush(panelBgTransparent);

                // Accent-tinted dark backgrounds: blend accent onto mod's background color
                byte baseR = bgColor.R, baseG = bgColor.G, baseB = bgColor.B;
                var tintedBg = Color.FromRgb(
                    (byte)(baseR + (accent.R - baseR) * 0.15),
                    (byte)(baseG + (accent.G - baseG) * 0.15),
                    (byte)(baseB + (accent.B - baseB) * 0.15));
                var tintedBgHover = Color.FromRgb(
                    (byte)(baseR + (accent.R - baseR) * 0.20),
                    (byte)(baseG + (accent.G - baseG) * 0.20),
                    (byte)(baseB + (accent.B - baseB) * 0.20));
                var midGradient = Color.FromRgb(
                    (byte)(baseR + (accent.R - baseR) * 0.10),
                    (byte)(baseG + (accent.G - baseG) * 0.10),
                    (byte)(baseB + (accent.B - baseB) * 0.10));

                var transparent40 = Color.FromArgb(0x40, accent.R, accent.G, accent.B);
                var transparent50 = Color.FromArgb(0x50, accent.R, accent.G, accent.B);

                // === UPDATE COLOR RESOURCES (drives DynamicResource brushes in Brushes.xaml) ===
                res["PinkColor"] = accent;
                res["DarkPink"] = dark;
                res["PinkButtonHovered"] = light;
                res["TransparentPink"] = transparent30;
                res["TransparentPink20"] = transparent20;
                res["TransparentPink40"] = transparent40;
                res["TransparentPink50"] = transparent50;
                res["AccentPressed"] = accentPressed;
                res["PatreonPurple"] = secondary;
                res["AccentTintedBg"] = tintedBg;
                res["AccentTintedBgHover"] = tintedBgHover;
                res["AccentMidGradient"] = midGradient;

                // === ALSO UPDATE BRUSH RESOURCES (in case any are frozen from initial load) ===
                res["PinkBrush"] = new SolidColorBrush(accent);
                res["DarkPinkBrush"] = new SolidColorBrush(dark);
                res["PinkButtonHoveredBrush"] = new SolidColorBrush(light);
                res["TransparentPinkBrush"] = new SolidColorBrush(transparent30);
                res["TransparentPink20Brush"] = new SolidColorBrush(transparent20);
                res["TransparentPink40Brush"] = new SolidColorBrush(transparent40);
                res["TransparentPink50Brush"] = new SolidColorBrush(transparent50);
                res["AccentPressedBrush"] = new SolidColorBrush(accentPressed);
                res["PatreonPurpleBrush"] = new SolidColorBrush(secondary);
                res["SecondaryBrush"] = new SolidColorBrush(secondary);
                res["AccentTintedBgBrush"] = new SolidColorBrush(tintedBg);
                res["AccentTintedBgHoverBrush"] = new SolidColorBrush(tintedBgHover);
                res["AccentMidGradientBrush"] = new SolidColorBrush(midGradient);

                // === v6 BRAND GRADIENT — anchor swap ===
                // CCP Default activates the static BrandGradient at the four brand anchors (logo, START,
                // XP bar, primary nav active). Other mods render solid SolidColorBrush(accent) so their
                // anchor pixels stay byte-identical to pre-v6 state.
                if (App.Mods?.IsCCPDefault == true && TryFindResource("BrandGradient") is Brush brandGradient)
                    res["AccentGradientBrush"] = brandGradient;
                else
                    res["AccentGradientBrush"] = new SolidColorBrush(accent);

                // === TITLE BAR (most visible — direct assignment for immediate update) ===
                // 2026-08-11: the mod accent lands on the UNDERLINE, not the fill. The bar used to
                // be a full-width slab of the accent, which made chrome the loudest thing on every
                // screen; it is a dark surface now (MainWindow.xaml) and the accent survives as the
                // 2px rule beneath it, so a re-skinned mod still recolours the title bar.
                // Lockdown still swaps Background outright - that one IS meant to shout.
                var accentBrush = new SolidColorBrush(accent);
                if (TitleBarBorder != null)
                    TitleBarBorder.BorderBrush = accentBrush;

                // === HEADER AREA ===
                if (TxtPlayerTitle != null)
                {
                    TxtPlayerTitle.Foreground = accentBrush;
                    if (TxtPlayerTitle.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
                        glow.Color = accent;
                }
                if (TxtHeaderVersion != null)
                    TxtHeaderVersion.Foreground = accentBrush;

                // === XP/LEVEL DISPLAY ===
                if (TxtLevelLabel != null)
                    TxtLevelLabel.Foreground = accentBrush;
                if (XPBar != null)
                {
                    // Anchor 3: CCP Default gets BrandGradient, every other mod gets the solid accent it always had.
                    XPBar.Background = (Brush)res["AccentGradientBrush"];
                }

                // Logo frame stays near-transparent for every mod. The Phase 3 plan put the
                // BrandGradient behind the logo as one of the four anchors, but in practice it
                // read as a glaring colored halo around the wordmark instead of a brand anchor.
                // Gradient now lives only at the START button, XP bar, and active primary nav tab.
                if (SettingsTab.LogoBrandFrame != null)
                    SettingsTab.LogoBrandFrame.Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0));

                // === BANNER AREA ===
                if (TxtBannerPrimary != null)
                    TxtBannerPrimary.Foreground = accentBrush;
                if (TxtBannerSecondary != null)
                    TxtBannerSecondary.Foreground = accentBrush;

                // Mod selector ComboBox repopulates itself in InitializeModSelector — no per-element refresh here.

                // Chrome + dashboard FX: the Fx* dynamic resources have already been rewritten by
                // FxTheme, so the XAML-bound sheen bands, tile rings and browser frame re-tint
                // themselves; the code-built glow effects (active nav button, START) hold a Color
                // and need this nudge to follow the mod. RefreshChromeFx drives both passes.
                RefreshChromeFx();

                App.Logger?.Debug("Theme-aware UI elements refreshed for mod {ModId}", App.Mods?.ActiveModId);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh some theme-aware elements");
            }
        }

        private static Color LightenColor(Color c, double amount)
        {
            return Color.FromRgb(
                (byte)Math.Min(255, c.R + (255 - c.R) * amount),
                (byte)Math.Min(255, c.G + (255 - c.G) * amount),
                (byte)Math.Min(255, c.B + (255 - c.B) * amount));
        }

        private static Color DarkenColor(Color c, double amount)
        {
            return Color.FromRgb(
                (byte)Math.Max(0, c.R * (1 - amount)),
                (byte)Math.Max(0, c.G * (1 - amount)),
                (byte)Math.Max(0, c.B * (1 - amount)));
        }

        /// <summary>
        /// Rebuilds the top-bar mod-switcher ComboBox and selects the active mod.
        /// Order: CCP Default → Bambi Sleep → Sissy Hypno → Dronification → user mods (alphabetical).
        /// </summary>
        private void InitializeModSelector()
        {
            _suppressModSelectorChange = true;
            try
            {
                AvailableMods.Clear();
                if (App.Mods != null)
                {
                    // Stock mods in a fixed canonical order.
                    var stockOrder = new[]
                    {
                        BuiltInMods.CCPDefaultId,
                        BuiltInMods.BambiSleepId,
                        BuiltInMods.SissyHypnoId,
                        BuiltInMods.DronificationId,
                        BuiltInMods.LockedId,
                    };
                    foreach (var id in stockOrder)
                    {
                        if (App.Mods.InstalledMods.TryGetValue(id, out var mod))
                            AvailableMods.Add(BuildSelectorItem(mod));
                    }
                    // User-installed mods after stock, alphabetical.
                    foreach (var mod in App.Mods.InstalledMods.Values
                        .Where(m => !m.IsBuiltIn)
                        .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        AvailableMods.Add(BuildSelectorItem(mod));
                    }

                    if (ModSelectorCombo != null)
                        ModSelectorCombo.SelectedValue = App.Mods.ActiveModId;
                }
            }
            finally
            {
                _suppressModSelectorChange = false;
            }

            // Hide BambiCloud option if mod doesn't want it — unless the user override
            // (Settings > Show BambiCloud everywhere) forces it visible.
            var modWantsBambiCloud = App.Mods?.ShowBambiCloudOption() ?? true;
            var showBambiCloud = modWantsBambiCloud || (App.Settings?.Current?.ForceShowBambiCloud ?? false);
            SettingsTab.RbBambiCloud.Visibility = showBambiCloud ? Visibility.Visible : Visibility.Collapsed;

            // #867: select the site from the mod - not only when BambiCloud is hidden. The
            // override reveals BambiCloud, it doesn't switch to it, and after an external link
            // neither radio is selected at all. SyncSiteRadiosToActiveMod covers both, and it
            // stands down while a live page owns the radios so RefreshBrowserLoadingText below
            // keeps describing the site actually on screen.
            SyncSiteRadiosToActiveMod();

            RefreshBrowserLoadingText();
        }

        private static ModSelectorItem BuildSelectorItem(ModPackage mod)
        {
            Brush accent;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(mod.Manifest.Theme?.AccentColor ?? "#E84393");
                accent = new SolidColorBrush(color);
            }
            catch
            {
                accent = new SolidColorBrush(Color.FromRgb(0xE8, 0x43, 0x93));
            }
            return new ModSelectorItem(mod.Id, mod.Name, accent);
        }

        private void RefreshBrowserLoadingText()
        {
            if (SettingsTab.BrowserLoadingText == null) return;
            // Reflect the actually-selected site radio, not just the mod's preference —
            // the BambiCloud button can now be visible via user override without being selected.
            var onBambiCloud = SettingsTab.RbBambiCloud?.IsChecked == true;
            var siteName = onBambiCloud
                ? "BambiCloud"
                : (App.Mods?.ActiveMod.Manifest.Browser?.SiteName ?? "HypnoTube");
            SettingsTab.BrowserLoadingText.Text = $"🌐 Click to connect to {siteName}";
        }

        /// <summary>
        /// Resolves a wall tile's per-mod face: a .ccpmod override of the BASE path wins
        /// outright (that is the contract every mod already relies on), then the app-shipped
        /// themed variant for the active built-in mod (features/{name}_{suffix}.png - same
        /// filename-fork mechanism as the takeover art), then the base art. Returns null only
        /// when even the base fails to resolve, in which case the XAML pack-URI face stands.
        /// </summary>
        private static System.Windows.Media.ImageSource? ModTileVariant(string baseName)
            => ModTileVariant(baseName, TileDecodeWidth);

        private static System.Windows.Media.ImageSource? ModTileVariant(string baseName, int decodeWidth)
        {
            // Decoded at a cap rather than through ResolveImage: the tile art is 1376px wide and
            // these cards render a fraction of that, so a full decode cost ~12MB of pixels each.
            // A failed decode (no such themed file) is how "the mod has no variant" is detected —
            // LoadModImageDecoded returns null for a resource that doesn't exist.
            // ResolveImage falls back to the app's own resource when the mod has no override,
            // so "did the mod override it" needs the mod folder check to be meaningful only if
            // the resolver distinguishes. It does not - so order the lookups by specificity
            // instead: themed variant first (it only exists app-side), unless the active mod is
            // a third-party one, whose base-path override must win.
            var suffix = App.Mods?.ActiveModId switch
            {
                Models.BuiltInMods.BambiSleepId => "_bambi",
                Models.BuiltInMods.SissyHypnoId => "_sissy",
                Models.BuiltInMods.DronificationId => "_drone",
                Models.BuiltInMods.LockedId => "_locked",
                _ => null,
            };
            if (suffix != null)
            {
                var themed = LoadModImageDecoded($"features/{baseName}{suffix}.png", decodeWidth);
                if (themed != null) return themed;
            }
            return LoadModImageDecoded($"features/{baseName}.png", decodeWidth);
        }

        // Dashboard tile decode caps. The mosaic lives in a 1489x901 design Viewbox, so a
        // single-column card paints ~340 DIP and the double-wide Vault ~700; these are roughly
        // 2x that, which covers a maximized window on a high-DPI display with headroom.
        private const int TileDecodeWidth = 768;
        private const int WideTileDecodeWidth = 1024;

        /// <summary>
        /// Loads feature images from mod resources (if overrides exist) or embedded resources.
        /// </summary>
        private void LoadFeatureImages()
        {
            try
            {
                // Dashboard feature cards (velvet mosaic, 4x4 hybrid wall since 2026-08-11).
                // The FX tiles are back on their ORIGINAL art paths - the ones every .ccpmod
                // override has always targeted - so the one-day 3x3 detour cost the contract
                // nothing (its dtrh/loom/deeper rows shipped in no release). NO ART PATH WAS
                // EVER RENAMED - mod contract rule 2.
                var cardMap = new (string resourcePath, Features.FeatureCard? card)[]
                {
                    ("features/flash.png", SettingsTab.CardFlash),
                    ("features/subliminal.png", SettingsTab.CardSubliminal),
                    ("features/bouncing_text.png", SettingsTab.CardBouncingText),
                    ("features/Bubble_pop.png", SettingsTab.CardBubblePop),
                    ("features/Phrase_Lock.png", SettingsTab.CardLockCard),
                    // The ? box and the Vault resolve through ModTileVariant below: built-in
                    // mods get app-shipped themed faces (features/mysterybox_bambi.png, ...)
                    // by filename convention - same mechanism as the takeover art fork - while
                    // a .ccpmod that overrides the BASE path still wins outright.
                };
                foreach (var (path, card) in cardMap)
                {
                    if (card == null) continue;
                    var image = ModResourceResolver.ResolveImage(path);
                    if (image != null)
                        card.Icon = image;
                }

                if (SettingsTab.CardJustDrop != null)
                {
                    var img = LoadModImageDecoded("features/justdrop.png", TileDecodeWidth);
                    if (img != null) SettingsTab.CardJustDrop.Icon = img;
                }
                if (SettingsTab.CardMystery != null)
                {
                    var img = ModTileVariant("mysterybox", TileDecodeWidth);
                    if (img != null) SettingsTab.CardMystery.Icon = img;
                }
                if (SettingsTab.CardVault != null)
                {
                    var img = ModTileVariant("vault", WideTileDecodeWidth);
                    if (img != null) SettingsTab.CardVault.Icon = img;
                }

                // The three diagonal tiles: per-half art through the resolver, so mods reskin
                // each half exactly as they reskinned the old single tiles for these features.
                if (SettingsTab.ComboVideoBubble != null)
                {
                    var a = ModResourceResolver.ResolveImage("features/mandatory_videos.png");
                    var b = ModResourceResolver.ResolveImage("features/Bubble_count.png");
                    if (a != null) SettingsTab.ComboVideoBubble.IconA = a;
                    if (b != null) SettingsTab.ComboVideoBubble.IconB = b;
                }
                if (SettingsTab.ComboSpiralPink != null)
                {
                    var a = ModResourceResolver.ResolveImage("features/spiral_overlay.png");
                    var b = ModResourceResolver.ResolveImage("features/Pink_filter.png");
                    if (a != null) SettingsTab.ComboSpiralPink.IconA = a;
                    if (b != null) SettingsTab.ComboSpiralPink.IconB = b;
                }
                if (SettingsTab.ComboMindDrain != null)
                {
                    var a = ModResourceResolver.ResolveImage("features/Mind_Wipers.png");
                    var b = ModResourceResolver.ResolveImage("features/brain_drain.png");
                    if (a != null) SettingsTab.ComboMindDrain.IconA = a;
                    if (b != null) SettingsTab.ComboMindDrain.IconB = b;
                }

                // PHASE 8: the eight "legacy progression tab rectangles" rows are gone with
                // ProgressionTabView. Mod art coverage is unchanged - the FX paths above are
                // the same eight resource-relative paths, including features/brain_drain.png,
                // so every one of those overrides still repaints on a mod switch. No art path was
                // renamed or dropped (mod contract rule 2).

                // Image elements in description cards + Video Haptic Sync card.
                // Takeover image is mod-specific: BambiSleep uses "bambi takeover.png",
                // other mods use the generic "takeover.png" (or override via their resources/ folder).
                var takeoverPath = App.Mods?.ActiveModId == Models.BuiltInMods.BambiSleepId
                    ? "features/bambi takeover.png"
                    : "features/takeover.png";
                var descImageMap = new (string resourcePath, System.Windows.Controls.Image? img)[]
                {
                    (takeoverPath, BambiTakeoverTab.ImgBambiTakeoverDesc),
                    ("features/vibe.png", HapticsTab.ImgHapticsVibeDesc),
                    ("features/vibe.png", HapticsTab.ImgVideoHapticSync),
                    // These three render via a hardcoded pack:// URI in XAML, so without a
                    // code override they'd always show the base (embedded) art, never a mod's.
                    ("features/awareness.png", AwarenessTab.ImgAwarenessFeature),
                    ("features/remote_control.png", RemoteControlTab.ImgRemoteControlFeature),
                    ("features/blink_trainer.png", BlinkTrainerTab.ImgBlinkTrainerFeature),
                    ("lockdown_icon.png", LockdownTab.ImgLockdownFeature),
                };
                foreach (var (path, img) in descImageMap)
                {
                    if (img == null) continue;
                    var resolved = ModResourceResolver.ResolveImage(path);
                    if (resolved != null)
                        img.Source = resolved;
                }

                // Premium quick-launch rail chip art. These live as ImageBrush resources
                // inside PremiumRail.Resources with a hardcoded pack:// UriSource, so the
                // rail was the one place on the Dashboard that kept the base art after a
                // mod switch. Mutate each brush's ImageSource in place — the chips bind to
                // them with {StaticResource}, so they all repaint from the one assignment.
                // The DecodePixelWidth values mirror the XAML: the rail only ever shows
                // these ~170px wide, and re-resolving without a decode cap would pull the
                // full-size neon PNGs into memory.
                //
                // The surfaceId on each row is what ApplyArtFraming crops against: the six
                // ordinary chips are railChip; Blink and Lockdown are the two taller launchers
                // that carry live controls over their whole face (railCard). The shapes those
                // ids stand for live in Services/ModArtFraming.cs, not here.
                var railArtMap = new (string key, string resourcePath, int decodeWidth, string surfaceId)[]
                {
                    ("ArtTakeover",  "features/takeover.png",       384, ModArtFramingRegistry.SurfaceRailChip),
                    ("ArtAwareness", "features/awareness.png",      512, ModArtFramingRegistry.SurfaceRailChip),
                    ("ArtHaptics",   "features/vibe.png",           384, ModArtFramingRegistry.SurfaceRailChip),
                    ("ArtIntake",    "features/lab_quiz_hero.png",  512, ModArtFramingRegistry.SurfaceRailChip),
                    ("ArtRemote",    "features/remote_control.png", 768, ModArtFramingRegistry.SurfaceRailChip),
                    ("ArtBlink",     "features/blink_trainer.png",  512, ModArtFramingRegistry.SurfaceRailCard),
                    ("ArtFyp",       "features/fyp.png",           512, ModArtFramingRegistry.SurfaceRailChip),
                    ("ArtLockdown",  "lockdown_icon.png",          1024, ModArtFramingRegistry.SurfaceRailCard),
                };
                var railResources = SettingsTab.PremiumRail?.Resources;
                if (railResources != null)
                {
                    foreach (var (key, path, decodeWidth, surfaceId) in railArtMap)
                    {
                        if (railResources[key] is not ImageBrush brush || brush.IsFrozen) continue;
                        var image = LoadModImageDecoded(path, decodeWidth);
                        if (image != null)
                            brush.ImageSource = image;
                        // Unconditional, and NOT inside the image != null guard: the crop has to
                        // be re-decided on every pass or a mod that overrides only some slots
                        // leaves the rest wearing whatever the previous mod was framed by, and
                        // switching back to built-in art never restores the shipped rect.
                        ApplyArtFraming(brush, path, surfaceId,
                                        image != null && ModResourceResolver.HasActiveModOverride(path));
                    }
                }

                // "Lab" hero headers (mod-sensitive): drone-mode ships green versions under
                // resources/features/lab_*_hero.png; the embedded pink ones are the fallback.
                // Only two rows left - the Lab tab's own three moved to playHeroMap below with the
                // cards they paint (Phase 6). The NAME is historical, like the filenames: the art
                // path is the mod compatibility surface and is never renamed to match the room.
                var labHeroMap = new (string resourcePath, ImageBrush? brush, string surfaceId)[]
                {
                    // Two DIFFERENT shapes, not one: the Intake header's art is a fixed 240x68 strip
                    // anchored right, while the permissions plate below is a wide 138-tall header.
                    // They shared a surface id until the review pointed out that one preview cannot
                    // be right for both.
                    ("features/lab_quiz_hero.png", GradedIntakeTab.GradedIntakeHeroBrush,
                     ModArtFramingRegistry.SurfaceIntakeStrip),
                    // Still a "lab hero" by filename — the art path is the mod compatibility
                    // surface and is never renamed — but the card it paints is the Companion
                    // door's permissions grid since Phase 5. The row travels with the brush: a
                    // dropped row does not fail the build, it just silently stops repainting on a
                    // mod switch.
                    ("features/lab_aimemory_hero.png", CompanionTab.LabAiMemoryHeroBrush,
                     ModArtFramingRegistry.SurfacePageHeader),
                };
                foreach (var (path, brush, surfaceId) in labHeroMap)
                {
                    if (brush == null || brush.IsFrozen) continue;
                    var image = ModResourceResolver.ResolveImage(path);
                    if (image != null)
                        brush.ImageSource = image;
                    ApplyArtFraming(brush, path, surfaceId,
                                    image != null && ModResourceResolver.HasActiveModOverride(path));
                }

                // Play door card heroes (UX restructure, Phase 6). The three that came off the Lab
                // tab keep their EXACT resource paths - features/lab_gaze_hero.png,
                // features/lab_focusgaze_hero.png, features/goon_game.png - because the path is the
                // mod compatibility surface and is never renamed to match the room it now hangs in
                // (same reason lab_aimemory_hero.png still says "lab" on the Companion door). The
                // four joining them are the same files their other surfaces already use.
                //
                // Unlike labHeroMap above, this block goes through LoadModImageDecoded: these are
                // 132-138px card headers, and ResolveImage decodes at full resolution, which is how
                // the rail's neon PNGs used to cost a few MB apiece for a thumbnail. The caps mirror
                // railArtMap's, and the brush's ImageSource is mutated IN PLACE - the cards bind the
                // brush itself, so replacing the brush would repaint nothing.
                var playHeroMap = new (string resourcePath, ImageBrush? brush, int decodeWidth, string surfaceId)[]
                {
                    ("features/lab_gaze_hero.png",      PlayTab?.PlayGazeHeroBrush,     512, ModArtFramingRegistry.SurfacePlayCard),
                    ("features/lab_focusgaze_hero.png", PlayTab?.PlayFocusHeroBrush,    512, ModArtFramingRegistry.SurfacePlayCard),
                    ("features/goon_game.png",          PlayTab?.PlayGoonHeroBrush,     512, ModArtFramingRegistry.SurfacePlayCardTall),
                    ("features/lab_quiz_hero.png",      PlayTab?.PlayIntakeHeroBrush,   512, ModArtFramingRegistry.SurfacePlayCard),
                    ("features/blink_trainer.png",      PlayTab?.PlayBlinkHeroBrush,    512, ModArtFramingRegistry.SurfacePlayCard),
                    // Its art plate overrides PlayCardArtPlate's 138 to 168, so it frames against
                    // the tall variant - a 138-shaped window would be re-cropped ~18% narrower.
                    ("features/remote_control.png",     PlayTab?.PlayRemoteHeroBrush,   768, ModArtFramingRegistry.SurfacePlayCardTall),
                    ("features/fyp.png",                PlayTab?.PlayFypHeroBrush,      512, ModArtFramingRegistry.SurfacePlayCard),
                    ("lockdown_icon.png",               PlayTab?.PlayLockdownHeroBrush, 1024, ModArtFramingRegistry.SurfacePlayCard),
                    // The page hero and the Loom strip. Both brushes were named and left mutable
                    // by the 0812 remake but never fed, so a .ccpmod overriding features/dtrh.png
                    // or features/loom.png repainted every OTHER surface that uses those files and
                    // left the two biggest ones on the embedded art. 1024 for the hero because it
                    // is the full-width banner at the top of the wall; 512 for the strip.
                    // dtrh is the full-width banner at the top of the wall, not a card header, so
                    // it frames against a much wider box (playHero).
                    ("features/dtrh.png",               PlayTab?.PlayDtrhHeroBrush,     1024, ModArtFramingRegistry.SurfacePlayHero),
                    // The Loom strip is a FIXED 216-wide column, not a card plate. Framed as a
                    // 2.85:1 plate, an author's 16:9 file was cropped to a 2.85 window and then
                    // re-cropped by UniformToFill to the column's 1.83 - about 64% x 62% of their
                    // image, where before framing existed a bare UniformToFill showed nearly all of
                    // it. Making mod art WORSE was the exact opposite of the point.
                    ("features/loom.png",               PlayTab?.PlayLoomHeroBrush,     512, ModArtFramingRegistry.SurfaceLoomStrip),
                    // Named and left mutable by the 0812 remake but never fed, so a .ccpmod
                    // overriding features/justdrop.png repainted the dashboard tile and left this
                    // card on the embedded art. Found by the review, which spotted that the card was
                    // offering authors a Frame button over a brush nothing wrote to.
                    ("features/justdrop.png",           PlayTab?.PlayJustDropHeroBrush, 512, ModArtFramingRegistry.SurfacePlayCard),
                };
                foreach (var (path, brush, decodeWidth, surfaceId) in playHeroMap)
                {
                    if (brush == null || brush.IsFrozen) continue;
                    var image = LoadModImageDecoded(path, decodeWidth);
                    if (image != null)
                        brush.ImageSource = image;
                    // Unconditional for the same reason as the rail — see the note there.
                    ApplyArtFraming(brush, path, surfaceId,
                                    image != null && ModResourceResolver.HasActiveModOverride(path));
                }

                // The rail chips do NOT all paint straight from the resources above: the hover
                // nudge (PrepareRailArtNudge) hands each of them a private Clone(), which stops
                // observing the resource the moment it is made. Push the freshly mutated art into
                // those clones or a runtime mod switch repaints the resource and nothing else.
                // No-op before the dashboard FX are wired (the list is empty), which is exactly
                // the startup case where the clones are made AFTER this method and are correct.
                RefreshRailArtClones();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load some feature images");
            }
        }

        /// <summary>
        /// Loads a resource image (mod override first, embedded fallback) at a capped decode
        /// size. Thin delegate to <see cref="ModResourceResolver.ResolveImageDecoded"/> since the
        /// mod-awareness sweep promoted the body into the resolver - the name stays because ~7
        /// call sites in this window and its partials read better with it, and because the
        /// resolver is now the only place that knows the cache key (which carries the width).
        /// Returns null on failure, which is also how "the mod ships no such variant" is read.
        /// </summary>
        private static ImageSource? LoadModImageDecoded(string resourcePath, int decodeWidth)
            => ModResourceResolver.ResolveImageDecoded(resourcePath, decodeWidth);

        /// <summary>
        /// Crops one art brush to the surface it paints, honouring the active mod's framing.
        ///
        /// <para><b>Why this exists.</b> The rail's <c>Art*</c> brushes carry Viewbox rects that
        /// were hand-tuned to the EMBEDDED art (they frame the illustration and push the wordmark
        /// burned into those PNGs out of the chip). Mod art used to be swapped in by mutating
        /// <c>ImageSource</c> and nothing else, so an author's picture was cropped by a window
        /// chosen for a completely different picture - which is why some slots looked right and
        /// others were nonsense. The Play cards had the mirror fault: no rect, so a blind centre
        /// crop, except goon_game.png whose one rect mod art also inherited.</para>
        ///
        /// <para>The decision itself lives in <see cref="ModArtFramingRegistry.ResolveViewbox"/>
        /// (built-in art keeps its shipped rect; mod art with framing uses it; mod art WITHOUT
        /// framing gets an honest centre crop, never ours). This method only gathers the three
        /// facts that decision needs and writes the answer.</para>
        /// </summary>
        /// <param name="modImageApplied">
        /// Whether the ACTIVE MOD's file is what actually landed on this brush. Two ways it is false
        /// while a naive "does an override exist" check would say true, and both would hand OUR art
        /// a centre crop it was never drawn for:
        /// <list type="bullet">
        /// <item>an event skin supplied the art — our own seasonal reskin of the same template, with
        /// no artFraming channel, so it keeps the shipped rect;</item>
        /// <item>the mod's file exists but failed to decode, in which case the resolver returns null
        /// and the brush is still showing the embedded bitmap.</item>
        /// </list>
        /// </param>
        private static void ApplyArtFraming(ImageBrush brush, string resourcePath, string surfaceId,
                                            bool modImageApplied)
        {
            try
            {
                if (brush.IsFrozen) return;

                // Aspect from the resolved bitmap, which is what is actually on screen - a
                // decode cap shrinks both axes, so the RATIO survives it. Width/Height are DIPs
                // rather than pixels; for a ratio that is the same number.
                var src = brush.ImageSource;
                double aspect = src != null && src.Height > 0 ? src.Width / src.Height : 0;

                bool isModSupplied = modImageApplied;
                var framing = isModSupplied ? ActiveModFraming(resourcePath, surfaceId) : null;

                // A Uniform brush letterboxes onto a backdrop plate ON PURPOSE - the Goon card's
                // square wordmark on #FF161622 is the case - so for un-framed mod art the honest
                // default is the WHOLE image, not a window of the card's aspect. Cropping a square
                // wordmark to 2.2:1 would smear it, which is the exact thing that Stretch and that
                // plate were chosen to avoid. An author who HAS framed it gets what they asked for.
                if (isModSupplied && framing == null && brush.Stretch == Stretch.Uniform)
                {
                    brush.Viewbox = new Rect(0, 0, 1, 1);
                    return;
                }

                brush.Viewbox = ModArtFramingRegistry.ResolveViewbox(
                    resourcePath, surfaceId, isModSupplied, aspect, framing);
            }
            catch (Exception ex)
            {
                // A brush that keeps its previous crop is a cosmetic miss; one that takes the
                // window down on a mod switch is not.
                App.Logger?.Debug("ApplyArtFraming({Path}, {Surface}): {E}", resourcePath, surfaceId, ex.Message);
            }
        }

        /// <summary>
        /// The active mod's framing for one (resource path, surface) pair, or null when it framed
        /// nothing for it.
        ///
        /// <para>Both keys are matched case-insensitively and path separators are normalised,
        /// because <c>mod.json</c> is hand-authored as often as it is editor-written: an author on
        /// Windows may well type <c>features\fyp.png</c>, and the resolver normalises to '/'.
        /// A path that is framed but not for THIS surface returns null, which
        /// <see cref="ModArtFramingRegistry.ResolveViewbox"/> reads as "centre-crop it".</para>
        /// </summary>
        private static ModArtFraming? ActiveModFraming(string resourcePath, string surfaceId)
        {
            var map = App.Mods?.ActiveMod?.Manifest?.ArtFraming;
            if (map == null || map.Count == 0) return null;

            foreach (var entry in map)
            {
                if (entry.Value == null) continue;
                if (!string.Equals(entry.Key.Replace('\\', '/'), resourcePath,
                                   StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var surface in entry.Value)
                {
                    if (string.Equals(surface.Key, surfaceId, StringComparison.OrdinalIgnoreCase))
                        return surface.Value;
                }
                return null;
            }
            return null;
        }

        private void BtnManageMods_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            // Refresh share badges shown in the dialog (throttled poll).
            _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindMods);

            var dialog = new ModManagerDialog { Owner = this };
            dialog.ShowDialog();

            if (dialog.ModWasChanged)
            {
                ApplyActiveModChange();
            }
        }

        private void ModSelectorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || _suppressModSelectorChange) return;
            if (ModSelectorCombo?.SelectedValue is not string newModId) return;
            if (App.Mods == null || App.Mods.ActiveModId == newModId) return;

            App.Mods.ActivateMod(newModId);
            ApplyActiveModChange();
        }

        /// <summary>
        /// Activates the mod the user chose in the first-run picker, now that its content is on disk.
        /// Deliberately routed through the SAME two steps as the top-bar combo (ActivateMod +
        /// <see cref="ApplyActiveModChange"/>) rather than a parallel switching path.
        /// Called on the UI thread by <see cref="PendingModActivation"/>; never throws back into the
        /// pack-install callback that triggered it.
        /// </summary>
        internal void ActivateChosenMod(string modId, PendingModActivation.Trigger trigger)
        {
            try
            {
                if (App.Mods == null || string.IsNullOrWhiteSpace(modId)) return;

                App.Mods.ActivateMod(modId);
                if (!string.Equals(App.Mods.ActiveModId, modId, StringComparison.OrdinalIgnoreCase))
                {
                    // ActivateMod refuses ids it doesn't know yet (registration can trail the pack).
                    // Keep the choice pending so the next availability signal retries it.
                    App.Logger?.Warning("[ModPicker] {ModId} could not be activated yet - keeping the choice pending", modId);
                    return;
                }

                ApplyActiveModChange(fromPickerChoice: true);
                PendingModActivation.Clear("activated");

                App.Logger?.Information(
                    "[ModPicker] Auto-activated the mod chosen in the first-run picker: {ModId} (trigger: {Trigger})",
                    modId, trigger);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ModPicker] Failed to activate the chosen mod {ModId}", modId);
            }
        }

        /// <summary>
        /// Centralized refresh of mod-aware UI after the active mod changes.
        /// Called by both the top-bar ComboBox and the Manage Mods dialog return path.
        /// </summary>
        /// <param name="fromPickerChoice">
        /// True only for <see cref="ActivateChosenMod"/>. Every other caller is a MANUAL switch, which
        /// outranks a first-run picker choice still waiting on its download - so the pending choice is
        /// dropped rather than yanking the user off the mod they just picked by hand.
        /// </param>
        private void ApplyActiveModChange(bool fromPickerChoice = false)
        {
            if (App.Mods == null) return;

            if (!fromPickerChoice) PendingModActivation.Clear("the user switched mods manually");

            App.Settings.Current.ActiveModId = App.Mods.ActiveModId;
            App.Settings.Current.ModChosen = true;
            App.Settings.Save();

            InitializeModSelector();
            LoadLogo();
            LoadTakeoverImage();
            LoadFeatureImages();
            RefreshThemeAwareElements();
            PopulateAchievementGrid();
            DrawSkillTree();
            // The secret-skill rail under the tree is mod-dependent on two axes (per-skill art via
            // ModResourceResolver, names via MakeModAware), so it repaints on the same signal as
            // the tree. Idempotent: it null-guards the tab and clears before rebuilding.
            PopulateSecretSkills();

            var modWantsBambiCloud = App.Mods.ShowBambiCloudOption();
            var showBambiCloud = modWantsBambiCloud || (App.Settings?.Current?.ForceShowBambiCloud ?? false);
            SettingsTab.RbBambiCloud.Visibility = showBambiCloud ? Visibility.Visible : Visibility.Collapsed;
            // #867: the selection follows the new mod's own site, not just when BambiCloud is
            // hidden - otherwise a switch could leave the radio pointing at a site this mod never
            // uses, and clicking it was the only way to find out. It leaves the radios alone
            // while they are reporting a live page; the !modWantsBambiCloud branch below is the
            // one case that overrides that, and it navigates in the same breath.
            SyncSiteRadiosToActiveMod();
            if (!modWantsBambiCloud)
            {
                // Mod doesn't want BambiCloud as its site: navigate to the mod's default even
                // if the override reveals the button.
                if (_browser != null && _browserInitialized)
                {
                    var url = App.Mods.GetDefaultBrowserUrl();
                    _browser.Navigate(url);
                }
            }

            RefreshHypnotubeLinksUI();
            _avatarTubeWindow?.UpdateQuickMenuState();

            App.Logger?.Information("Mod changed to {ModId}", App.Mods.ActiveModId);
        }

        // Live name+URL rows in the mod-aware video link pool editor.
        private readonly List<(TextBox NameBox, TextBox UrlBox)> _videoLinkRows = new();

        private void RefreshHypnotubeLinksUI()
        {
            if (CompanionTab.TxtHypnotubeModeLabel != null)
                CompanionTab.TxtHypnotubeModeLabel.Text = App.Settings?.Current?.ContentModeDisplay ?? "CCP Default";

            if (CompanionTab.VideoLinkPoolPanel == null) return;

            // Rebuild the rows from the active mod's pool (user override, else shipped links).
            _videoLinkRows.Clear();
            CompanionTab.VideoLinkPoolPanel.Children.Clear();
            if (CompanionTab.TxtNoVideoLinks != null) CompanionTab.VideoLinkPoolPanel.Children.Add(CompanionTab.TxtNoVideoLinks);

            var links = App.Mods?.GetVideoLinks();
            if (links != null)
                foreach (var kvp in links)
                {
                    // Drop non-video "browse" links (e.g. a stray /videos/ listing) — they're not
                    // videos, so they don't belong in the pool and won't be re-saved.
                    if (IsListingUrl(kvp.Value)) continue;
                    AddVideoLinkRow(kvp.Key, kvp.Value);
                }

            UpdateNoVideoLinksPlaceholder();
        }

        /// <summary>
        /// True for a HypnoTube browse/listing page (e.g. /videos/ or the site root) rather than a
        /// specific video. Deliberately narrow: a /video/... page — even a typo'd one missing .html —
        /// is still a video and stays editable.
        /// </summary>
        private static bool IsListingUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host.ToLowerInvariant();
            if (host != "hypnotube.com" && !host.EndsWith(".hypnotube.com", StringComparison.Ordinal))
                return false;
            var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            return path == "" || path == "/videos" || path == "/video";
        }

        internal void BtnAddVideoLink_Click(object sender, RoutedEventArgs e)
        {
            var row = AddVideoLinkRow("", "");
            UpdateNoVideoLinksPlaceholder();
            // Drop the cursor straight into the URL field — paste-and-go is the common case.
            row.UrlBox.Focus();
        }

        /// <summary>
        /// Builds one editable name + URL row (with a bin button) and registers it. Edits persist
        /// on focus loss; the bin removes just that row. Mirrors ModCreatorWindow.AddVideoLinkRow.
        /// </summary>
        private (TextBox NameBox, TextBox UrlBox) AddVideoLinkRow(string name, string url)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBox = MakePoolTextBox(name, isUrl: false);
            nameBox.ToolTip = Loc.Get("tooltip_video_link_name_optional");
            Grid.SetColumn(nameBox, 0);
            row.Children.Add(nameBox);

            var urlBox = MakePoolTextBox(url, isUrl: true);
            Grid.SetColumn(urlBox, 2);
            row.Children.Add(urlBox);

            // Preview: open the link externally so the user can check it (HTTPS only).
            var openBtn = new Button
            {
                Content = "", // Segoe MDL2 'OpenInNewWindow'
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 200, 255)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.Get("tooltip_preview_video_link")
            };
            openBtn.Content = ""; // Segoe MDL2 'OpenInNewWindow' (set via escape so the glyph can't be stripped)
            openBtn.Click += (_, _) =>
            {
                var u = urlBox.Text?.Trim() ?? "";
                if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "Failed to open video link preview: {Url}", u); }
                }
            };
            Grid.SetColumn(openBtn, 3);
            row.Children.Add(openBtn);

            var removeBtn = new Button
            {
                Content = "", // Segoe MDL2 'Delete' (trash can)
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.Get("tooltip_remove_video_link")
            };
            var entry = (NameBox: nameBox, UrlBox: urlBox);
            removeBtn.Click += (_, _) =>
            {
                CompanionTab.VideoLinkPoolPanel.Children.Remove(row);
                _videoLinkRows.Remove(entry);
                PersistVideoLinks();
                UpdateNoVideoLinksPlaceholder();
            };
            Grid.SetColumn(removeBtn, 4);
            row.Children.Add(removeBtn);

            // Host validation: grey the row and flip the preview glyph to a warning when the URL is
            // present but not a usable absolute http(s) link (such rows are dropped on save). The
            // preview button doubles as the status indicator, so there's no extra column.
            void UpdateRowValidity()
            {
                var u = urlBox.Text?.Trim() ?? "";
                bool empty = string.IsNullOrWhiteSpace(u);
                bool valid = !empty && Uri.TryCreate(u, UriKind.Absolute, out var uri)
                             && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                bool invalid = !empty && !valid;

                row.Opacity = invalid ? 0.55 : 1.0;
                urlBox.BorderBrush = invalid
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x5A))            // warning orange
                    : new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80));    // default
                openBtn.FontFamily = invalid ? new FontFamily("Segoe UI Symbol") : new FontFamily("Segoe MDL2 Assets");
                openBtn.Content = invalid ? "⚠" : "";    // Warning sign U+26A0 : OpenInNewWindow U+E8A7
                openBtn.Foreground = invalid
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x5A))
                    : new SolidColorBrush(Color.FromRgb(120, 200, 255));
                openBtn.ToolTip = invalid
                    ? "Not a valid http(s) link — this row won't be saved."
                    : Loc.Get("tooltip_preview_video_link");
            }
            urlBox.TextChanged += (_, _) => UpdateRowValidity();

            nameBox.LostFocus += (_, _) => PersistVideoLinks();
            urlBox.LostFocus += (_, _) => PersistVideoLinks();

            _videoLinkRows.Add(entry);
            CompanionTab.VideoLinkPoolPanel.Children.Add(row);
            UpdateRowValidity();
            return entry;
        }

        private TextBox MakePoolTextBox(string text, bool isUrl)
        {
            return new TextBox
            {
                Text = text ?? "",
                MaxLength = isUrl ? 500 : 200,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                Foreground = isUrl ? new SolidColorBrush(Color.FromRgb(120, 200, 255)) : Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
        }

        /// <summary>
        /// Collects the current rows into a name→URL pool and saves it as the active mod's
        /// override. Blank names are auto-titled from the URL (HtUrlHelper.DeriveTitleFromUrl);
        /// blank/invalid URLs are dropped; duplicate names are made unique.
        /// </summary>
        private void PersistVideoLinks()
        {
            var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (nameBox, urlBox) in _videoLinkRows)
            {
                var url = urlBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(url)) continue; // a row with no URL isn't a link yet
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    continue;

                var name = nameBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    name = HtUrlHelper.DeriveTitleFromUrl(url);

                var unique = name;
                int n = 2;
                while (pool.ContainsKey(unique) && !string.Equals(pool[unique], url, StringComparison.OrdinalIgnoreCase))
                    unique = $"{name} ({n++})";
                pool[unique] = url;
            }

            App.Mods?.SetUserVideoLinks(pool);
            App.Settings?.Save();
            // Refresh the clickable-link lookup so the companion links these titles immediately.
            AvatarTubeWindow.ReloadVideoLinks();
        }

        private void UpdateNoVideoLinksPlaceholder()
        {
            if (CompanionTab.TxtNoVideoLinks != null)
                CompanionTab.TxtNoVideoLinks.Visibility = _videoLinkRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Returns a mode-appropriate image path for quests.
        /// Supports both local cached images and embedded resources.
        /// Swaps Bambi Sleep specific images when in Sissy Hypno mode.
        /// </summary>
        private string GetModeAwareQuestImagePath(Models.QuestDefinition quest)
        {
            // Use EffectiveImagePath which prefers cached remote images over embedded
            var imagePath = quest.EffectiveImagePath;

            if (string.IsNullOrEmpty(imagePath))
                return imagePath;

            // For embedded resources, check if mod has overrides or if mode-specific swap is needed
            if (imagePath.StartsWith("pack://"))
            {
                // Extract relative path from pack URI
                var prefix = "pack://application:,,,/Resources/";
                if (imagePath.StartsWith(prefix))
                {
                    var relativePath = imagePath.Substring(prefix.Length);
                    if (Services.ModResourceResolver.HasModOverride(relativePath))
                        return Services.ModResourceResolver.ResolveUri(relativePath);
                }

                // Legacy mode-specific swaps for built-in mods
                if (App.Settings?.Current?.IsSissyMode == true)
                {
                    if (imagePath.Contains("logo.png"))
                        return "pack://application:,,,/Resources/logo2.png";
                    if (imagePath.Contains("bambi takeover.png"))
                        return "pack://application:,,,/Resources/features/mandatory_videos.png";
                }
                // CCP Default uses the same neutral wordmark as Sissy.
                if (App.Mods?.IsCCPDefault == true)
                {
                    if (imagePath.Contains("logo.png"))
                        return "pack://application:,,,/Resources/logo2.png";
                }
            }

            return imagePath;
        }

        /// <summary>
        /// Load an image from either a local file path or pack:// URI
        /// </summary>
        private System.Windows.Media.Imaging.BitmapImage? LoadQuestImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return null;

            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();

                if (imagePath.StartsWith("pack://"))
                {
                    // Embedded resource
                    bitmap.UriSource = new Uri(imagePath);
                }
                else if (System.IO.File.Exists(imagePath))
                {
                    // Local file (cached remote image)
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                }
                else
                {
                    return null;
                }

                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void CenterOnPrimaryScreen()
        {
            try
            {
                // Get the primary screen. Null (and an empty Screen.AllScreens) is a real state
                // during display-topology churn — skip the centring, but still run the per-monitor
                // fit below, which does its own guarding.
                var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                if (primaryScreen != null)
                {
                    // Get DPI scaling
                    var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                    if (dpiScale <= 0) dpiScale = 1;

                    // Calculate center position on primary screen
                    var screenWidth = primaryScreen.WorkingArea.Width / dpiScale;
                    var screenHeight = primaryScreen.WorkingArea.Height / dpiScale;
                    var screenLeft = primaryScreen.WorkingArea.Left / dpiScale;
                    var screenTop = primaryScreen.WorkingArea.Top / dpiScale;

                    // Clamp SIZE to the work area before centring. Phase 1 grew the default window
                    // to 1656x943 DIPs, which does not fit a 1080p desktop at 125% scaling
                    // (1536x816 logical) — an unclamped centre put Left at -60 and the title-bar
                    // buttons past the right edge. The root Viewbox (MainWindow.xaml, wrapping
                    // x:Name="DesignCanvas") is Stretch="Fill" over a fixed design canvas, so
                    // shrinking the window scales the content instead of clipping it.
                    //
                    // The floors have to come down FIRST or the clamp is a no-op: WPF enforces
                    // MinWidth/MinHeight over whatever we assign, and at 300% scaling the work area
                    // is smaller than both (see MainWindow.WorkAreaFit.cs). The Max() below still
                    // keeps the top-left corner on-screen in the residual case, so the window
                    // stays grabbable.
                    RelaxSizeFloorsTo(screenWidth, screenHeight);

                    if (SizeToContent != SizeToContent.Manual) SizeToContent = SizeToContent.Manual;
                    if (screenWidth > 0) Width = Math.Min(Width, screenWidth);
                    if (screenHeight > 0) Height = Math.Min(Height, screenHeight);

                    Left = Math.Max(screenLeft, screenLeft + (screenWidth - Width) / 2);
                    Top = Math.Max(screenTop, screenTop + (screenHeight - Height) / 2);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CenterOnPrimaryScreen failed");
            }

            // Primary is only a starting guess — the window may well be opening on a second monitor
            // with a different scale and a different work area. Once there is an HWND, hand off to
            // the per-monitor fit, which measures the screen we are ACTUALLY on. No-op before the
            // handle exists (the constructor call), which the SourceInitialized hook then covers.
            FitToCurrentMonitorWorkArea("center-on-primary");
        }


        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Hook window messages to intercept minimize BEFORE it happens
            var hwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource?.AddHook(WndProc);

            // Browser-header Webcam Tracking toggle: keep label/tooltip in sync
            // with the tracking service so manual starts (Lab tab, Blink Trainer,
            // etc.) reflect on this button too.
            EnsureBrowserWebcamStateSubscribed();

            // Dashboard premium quick-toggle rail: paint state + subscribe to patron changes.
            InitPremiumRail();

            // Header "Remember" button: reflect whether a setup is already saved.
            SyncRememberButton();

            // Takeover state hero + live voice panel: subscribe to speech/autonomy events.
            InitTakeoverVoiceUi();

            // Wire the in-app notification surface. Anything App.Notifications.Show()'d
            // before this point is replayed on attach.
            App.Notifications?.AttachHost(NotificationHost);

            // First-run picker: activate the mod the user chose there once its pack is on disk. Armed
            // here rather than in the ctor because the switch repaints this window's tabs, and the
            // resume case can fire the moment it is armed (download finished while the app was shut).
            PendingModActivation.Attach(this);

            // Phase 1.6: legacy calibration prompt. Pre-multi-monitor-hotfix
            // saves have MonitorBounds without DeviceName, so the runtime
            // can't pin gaze content to the calibrated screen. Show a
            // dismissable sticky toast suggesting recalibration. Placeholder
            // copy — voice-pass at ship time.
            try
            {
                var cal = App.Webcam?.Calibration;
                if (cal?.MonitorBounds != null && string.IsNullOrEmpty(cal.MonitorBounds.DeviceName))
                {
                    App.Notifications?.ShowSticky(
                        "recalibrate-multimonitor",
                        "Your calibration needs updating for multi-monitor support.",
                        Services.NotificationType.Warning,
                        actionLabel: "Recalibrate",
                        action: () =>
                        {
                            try
                            {
                                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Warning(ex, "Recalibrate toast: failed to open calibration window");
                            }
                        });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Recalibrate-suggest check failed: {Error}", ex.Message);
            }

            // One-time premium celebration for entitlements granted silently (cached state
            // restored in the ctor, the grace window, V2-linked accounts). The provider
            // TierChanged handlers cover the loud grant paths; this covers the quiet ones
            // on the next launch.
            MaybeShowPremiumCelebration();

            // Catalogue submission feedback: poll for any pending Deeper
            // submissions that have been accepted/published since last launch and
            // surface a one-time notification. Host is attached above, so a
            // sticky toast shows even though the Deeper tab hasn't been opened.
            _ = CheckDeeperSubmissionStatusesAsync(force: true);
            // Same one-time accepted feedback for shared Presets, Sessions & Mods.
            _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindPresets, force: true);
            _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindSessions, force: true);
            _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindMods, force: true);


            // Enable Windows 11 rounded corners
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
                // Silently fail on Windows 10 or earlier - they don't support this API
            }

            // Re-center after load in case DPI wasn't available in constructor
            CenterOnPrimaryScreen();

            // Update panic key button
            UpdatePanicKeyButton();

            // Title-bar camera-active indicator
            WireWebcamActivePill();

            // Title-bar microphone-active indicator (privacy parity with the camera pill)
            WireMicActivePill();

            // Movable loading splash shown while the webcam engine starts up
            InstallWebcamLoadingSplash();

            // Global 6-blink → stop-everything + recalibrate gesture
            WireRapidBlinkRecalibrateShortcut();
            SyncBlinkRecalToggles(App.Settings?.Current?.BlinkRecalibrateShortcutEnabled ?? true);

            // Load custom sessions from disk (so they persist across restarts)
            if (_sessionManager == null)
                InitializeSessionManager();

            // Initialize hypnotube links UI
            RefreshHypnotubeLinksUI();

            // Initialize Deeper "Enhance if possible" toggle from settings
            try
            {
                if (SettingsTab.ToggleEnhanceIfPossible != null)
                    SettingsTab.ToggleEnhanceIfPossible.IsChecked = App.Settings?.Current?.BrowserEnhanceIfPossible ?? true;
                if (SettingsTab.ChkForceShowBambiCloud != null)
                    SettingsTab.ChkForceShowBambiCloud.IsChecked = App.Settings?.Current?.ForceShowBambiCloud ?? false;
            }
            catch { }

            // Apply mod-aware feature names to static XAML labels
            ApplyModFeatureNames();
            if (App.Mods != null)
            {
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(ApplyModFeatureNames);
                // The Studio rack's row captions are mod-aware too (Phase 4).
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(() => StudioTab?.RepaintModAwareChrome());
                // Re-render the Remote Control QR code in the new mod's accent color
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(() =>
                {
                    var code = App.RemoteControl?.SessionCode;
                    if (!string.IsNullOrEmpty(code))
                        RefreshRemoteQrCode(BuildRemotePairingUrl(code));
                });
                // Re-load mod-aware feature images (description card images, VHS card)
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(LoadFeatureImages);
                // The nav rail's seven door medallions are mod art too (nav/door_*.png).
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(ApplyDoorArt);
                // THE ACCENT PALETTE. ApplyActiveModChange also calls this, but it is not on every
                // path that changes the active mod: uninstalling the mod you are wearing makes
                // ModService activate CCP Default by itself (ModService.UninstallMod), and that
                // route never reaches ApplyActiveModChange - so the whole app kept the uninstalled
                // mod's accent until the next restart. ModChanged is the authoritative signal, so
                // the palette hangs off it as well.
                //
                // Running twice on a manual switch is deliberate and cheap: the body is a pure
                // rewrite of ~40 Application.Resources entries (idempotent by construction), and
                // the one thing it calls out to, RefreshChromeFx, is self-guarded and re-entrant.
                // No last-applied-mod guard, because that would also suppress the legitimate
                // re-apply after a mod is reinstalled under the same id with new theme colors.
                App.Mods.ModChanged += (_, _) => Dispatcher.Invoke(RefreshThemeAwareElements);
            }

            // Re-apply code-behind strings when language changes (section headers, feature names, etc.)
            LocalizationManager.Instance.LanguageChanged += (_, _) => Dispatcher.Invoke(ApplyModFeatureNames);

            // Initialize language selector
            InitializeLanguageSelector();

            // Initialize quick login UI
            UpdateQuickLoginUI();

            // Load past quizzes list
            RefreshPastQuizzes();

            // Initialize pop quiz UI from settings
            if (GradedIntakeTab.ChkPopQuizEnabled != null)
                GradedIntakeTab.ChkPopQuizEnabled.IsChecked = App.Settings.Current.PopQuizEnabled;
            if (GradedIntakeTab.SliderPopQuizFrequency != null)
            {
                GradedIntakeTab.SliderPopQuizFrequency.Value = App.Settings.Current.PopQuizFrequency;
                if (GradedIntakeTab.TxtPopQuizFrequency != null)
                    GradedIntakeTab.TxtPopQuizFrequency.Text = $"{App.Settings.Current.PopQuizFrequency}/session hr";
            }

            // Handle start minimized (to tray) - delay briefly to let window render properly first
            if (App.Settings.Current.StartMinimized)
            {
                // Let the window fully render before minimizing to avoid black window artifacts
                await Task.Delay(100);
                _trayIcon?.MinimizeToTray();
            }

            // Handle auto-start engine
            if (App.Settings.Current.AutoStartEngine)
            {
                StartEngine();
            }

            // Handle force video on launch (after a brief delay to let things initialize)
            if (App.Settings.Current.ForceVideoOnLaunch)
            {
                await Task.Delay(200);
                TriggerStartupVideo();
            }

            // Fetch initial leaderboard data for stat pills
            if (App.Leaderboard != null && App.IsLoggedIn)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    await App.Leaderboard.RefreshAsync();

                    // Update UI after leaderboard loads
                    Dispatcher.Invoke(() => UpdateStatPills());
                });
            }

            // Start periodic stat pill update timer
            StartStatPillUpdateTimer();

            // Browser is lazy-loaded on first interaction (click radio toggle, pop-out, or external navigation)

            // Check if this is first run and prompt for assets folder
            await CheckFirstRunAssetsPromptAsync();

            // Startup ran before there was any window to ask on, so a dead end it hit (a custom
            // assets folder that has vanished — App.EffectiveAssetsPath) could only record that an
            // offer was warranted. This is the first safe moment to surface it: a window exists,
            // and the first-run prompt's own modals have closed. No-ops when nothing is parked.
            App.FlushPendingRemoteMediaOffer(this);

            // Initialize Avatar Tube Window. #888: this used to run unconditionally, so a companion
            // the user had dismissed came back on the next launch — visible but mute, because the
            // speech path gates on AvatarEnabled separately. Creation is the gate now; Wake (tray)
            // and the Companion room's toggle build it on demand.
            if (App.Settings.Current.AvatarEnabled)
            {
                InitializeAvatarTube();
            }

            // Initialize the Discord Rich Presence checkbox. Guard with _isLoading so the Changed
            // handler doesn't fire the "Discord Not Linked" MessageBox during startup for users
            // whose saved setting is enabled but who haven't linked Discord.
            // Phase 8: the dead ProgressionTab copy is gone; the live twins are the Home quick
            // toggle here and DiscordTab.ChkDiscordTabRichPresence (seeded by UpdateDiscordUI).
            _isLoading = true;
            try
            {
                SettingsTab.ChkQuickDiscordRichPresence.IsChecked = App.Settings.Current.DiscordRichPresenceEnabled;
            }
            finally { _isLoading = false; }

            // Audio-sync ENABLE moved onto the Haptics tab's routing matrix (Media > Audio sync)
            // in the Phase E rebuild, and the Haptics tab's own delay/power sliders are loaded by
            // LoadHapticsSettingsToUi(). Only the mirror pair is initialised here — it lived on
            // the dashboard's browser card until Phase 3 moved it into Settings · Audio.
            if (AppSettingsTab.SliderAudioSyncLatency != null)
            {
                AppSettingsTab.SliderAudioSyncLatency.Value = App.Settings.Current.Haptics.AudioSync.ManualLatencyOffsetMs;
                var latencyMs = App.Settings.Current.Haptics.AudioSync.ManualLatencyOffsetMs;
                var sign = latencyMs >= 0 ? "+" : "";
                AppSettingsTab.TxtAudioSyncLatency.Text = $"{sign}{latencyMs}ms";
            }
            if (AppSettingsTab.SliderAudioSyncIntensity != null)
            {
                var intensityPercent = (int)(App.Settings.Current.Haptics.AudioSync.LiveIntensity * 100);
                AppSettingsTab.SliderAudioSyncIntensity.Value = intensityPercent;
                AppSettingsTab.TxtAudioSyncIntensity.Text = $"{intensityPercent}%";
            }
            if (AppSettingsTab.AudioSyncLatencyPanel != null)
            {
                AppSettingsTab.AudioSyncLatencyPanel.Visibility = App.Settings.Current.Haptics.AudioSync.Enabled
                    ? Visibility.Visible : Visibility.Collapsed;
            }

            // Initialize Quick Links login buttons
            UpdateQuickPatreonUI();
            UpdateQuickDiscordUI();

            // Initialize scrolling marquee banner
            InitializeMarqueeBanner();

            // Ask the server whether this account has a Just Drop door. Staggered behind the
            // marquee's three checks for the same reason they are staggered behind each other -
            // see MainWindow.JustDrop.cs.
            InitializeJustDropDoor();

            // Deeper tab first-launch pulse — draw the eye to the new tab once,
            // unless the user has already opened it (HasSeenDeeperTab) or disabled it.
            var deeperSettings = App.Settings?.Current;
            if (deeperSettings != null && deeperSettings.EnableDeeper && !deeperSettings.HasSeenDeeperTab)
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await Task.Delay(1200);
                        StartDeeperTabPulse();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to start Deeper tab pulse");
                    }
                });
            }

            // Programs tab first-launch pulse — the same one-shot announcement the Deeper tab got.
            // No feature toggle to check: the Programs tab is always present, so HasSeenProgramsTab
            // is the only gate. Started independently of the Deeper pulse rather than in an else-if,
            // because a user who has already found Deeper still has to be told about this one.
            var programsSettings = App.Settings?.Current;
            if (programsSettings != null && !programsSettings.HasSeenProgramsTab)
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await Task.Delay(1200);
                        StartProgramsTabPulse();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to start Programs tab pulse");
                    }
                });
            }

            // Check if any authenticated user needs to complete registration (choose display name)
            // This handles users who had cached tokens but cancelled the registration dialog previously
            _ = CheckPendingRegistrationAsync();
        }

        /// <summary>
        /// Check if any authenticated user needs to complete registration (choose display name).
        /// This catches users who have profiles with null display_name from before the fix.
        /// </summary>
        private async Task CheckPendingRegistrationAsync()
        {
            try
            {
                // Wait a bit for background authentication to complete
                await Task.Delay(2000);

                // If user already has a UnifiedId (registered in V2 system), skip this check
                // The old /patreon/validate endpoint doesn't know about V2 users
                if (!string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId))
                {
                    App.Logger?.Debug("User already has UnifiedId, skipping pending registration check");
                    return;
                }

                // Check if user is authenticated but needs registration
                bool patreonNeedsReg = App.Patreon?.IsAuthenticated == true && App.Patreon.NeedsRegistration;
                bool discordNeedsReg = App.Discord?.IsAuthenticated == true && App.Discord.NeedsRegistration;

                if (!patreonNeedsReg && !discordNeedsReg)
                    return;

                App.Logger?.Information("User needs to complete registration: Patreon={Patreon}, Discord={Discord}",
                    patreonNeedsReg, discordNeedsReg);

                // Determine which provider to use for registration (prefer Patreon)
                string provider = patreonNeedsReg ? "patreon" : "discord";

                // Show the display name dialog (HandlePostAuthAsync gets the token internally)
                await Dispatcher.InvokeAsync(async () =>
                {
                    var success = await Services.AccountService.HandlePostAuthAsync(this, provider);
                    if (success)
                    {
                        App.Logger?.Information("Pending registration completed successfully");
                        // Refresh the profile to get updated data
                        if (App.ProfileSync != null)
                            await App.ProfileSync.LoadProfileAsync();
                        UpdateQuickPatreonUI();
                        UpdateQuickDiscordUI();
                    }
                    else
                    {
                        App.Logger?.Warning("Pending registration failed or was cancelled");
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error checking pending registration");
            }
        }

        /// <summary>
        /// Checks if this is a first run (no assets) and prompts user to choose a content folder.
        /// </summary>
        private async Task CheckFirstRunAssetsPromptAsync()
        {
            try
            {
                // Skip if custom assets path is already set
                if (!string.IsNullOrWhiteSpace(App.Settings?.Current?.CustomAssetsPath))
                    return;

                // Check if default assets folder has any content
                var defaultImagesPath = System.IO.Path.Combine(App.UserAssetsPath, "images");
                var defaultVideosPath = System.IO.Path.Combine(App.UserAssetsPath, "videos");

                int imageCount = 0;
                int videoCount = 0;

                if (System.IO.Directory.Exists(defaultImagesPath))
                {
                    imageCount = System.IO.Directory.GetFiles(defaultImagesPath, "*.*")
                        .Count(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
                }

                if (System.IO.Directory.Exists(defaultVideosPath))
                {
                    videoCount = System.IO.Directory.GetFiles(defaultVideosPath, "*.*")
                        .Count(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
                }

                // If user has content, don't bother them
                if (imageCount > 5 || videoCount > 2)
                    return;

                // Check if there's a "first run shown" flag
                if (App.Settings?.Current?.FirstRunAssetsPromptShown == true)
                    return;

                // Show first-run prompt after a brief delay
                await Task.Delay(500);

                var result = MessageBox.Show(
                    "Welcome to Conditioning Control Panel!\n\n" +
                    "Would you like to choose a custom folder for your content?\n\n" +
                    "This folder will store:\n" +
                    "  • Your images and videos\n" +
                    "  • Downloaded content packs\n\n" +
                    "Choosing a custom folder is recommended if you want to:\n" +
                    "  • Keep content on a different drive\n" +
                    "  • Preserve content across reinstalls\n\n" +
                    "Starting from nothing? People share content packs daily on our Discord\n" +
                    "(the Get Packs button in the Assets tab takes you there), or the app can\n" +
                    "stream media from Reddit instead - see \"Where your media comes from\".\n\n" +
                    "You can always change this later in Settings > Assets.",
                    "Choose Content Folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                // Mark as shown regardless of choice
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.FirstRunAssetsPromptShown = true;
                    App.Settings.Save();
                }

                if (result == MessageBoxResult.Yes)
                {
                    // Open the assets folder selection dialog
                    BtnPickAssetsFolder_Click(this, new RoutedEventArgs());
                }

                // Best moment in the whole app to offer remote media: the user has just told us
                // they have no library. After the modal chain above, never during it - the
                // coaching card dispatches at Normal priority and a MessageBox / folder browser
                // pumps its own loop, which would stack the card on top of them.
                // Someone who pointed us at a folder that already has content is not this user,
                // so re-check the effective path rather than trusting the pre-prompt count.
                if (!HasAnyLocalMedia(App.EffectiveAssetsPath))
                    App.OfferRemoteMediaSource("first-run-assets-prompt", this);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error in first-run assets prompt");
            }
        }

        /// <summary>
        /// Cheap "is there anything at all to play?" probe over the images/videos folders under
        /// <paramref name="assetsRoot"/>. Deliberately looser than the first-run prompt's typed
        /// count - this only decides whether to offer remote media, so any file at all counts and
        /// an unreadable folder counts as empty.
        /// </summary>
        private static bool HasAnyLocalMedia(string assetsRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(assetsRoot)) return false;
                foreach (var sub in new[] { "images", "videos" })
                {
                    var dir = System.IO.Path.Combine(assetsRoot, sub);
                    if (System.IO.Directory.Exists(dir) && System.IO.Directory.EnumerateFiles(dir).Any())
                        return true;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("HasAnyLocalMedia: probe of {Root} failed ({Error}) — treating as empty", assetsRoot, ex.Message);
            }
            return false;
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int WM_DPICHANGED_MAIN = 0x02E0;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Mixed-DPI drag hang (#451/#477, Application Hang 1002): a drag across monitors with
            // different scale factors delivers WM_DPICHANGED INSIDE this window's native modal move
            // loop, and WPF answers it with a synchronous CompleteRender that can only finish if the
            // process-wide layered-window render thread is idle. Bracket the whole move loop so the
            // periodic reconcilers (overlay z-order sweep, tube fullscreen tick) and the tube's
            // animation writers stand down for the duration, and defer the work-area fit to the end
            // of the drag instead of fighting the user's mouse mid-loop.
            if (msg == WM_ENTERSIZEMOVE)
            {
                try
                {
                    Services.UI.DisplayChangeCoordinator.BeginInteractiveMove();
                    App.AvatarWindow?.NotifyParentInteractiveMove(true);
                }
                catch { /* never let a hook throw */ }
                return IntPtr.Zero;
            }
            if (msg == WM_EXITSIZEMOVE)
            {
                try
                {
                    Services.UI.DisplayChangeCoordinator.EndInteractiveMove();
                    App.AvatarWindow?.NotifyParentInteractiveMove(false);
                    RunWorkAreaFitDeferredByMove();
                }
                catch { /* never let a hook throw */ }
                return IntPtr.Zero;
            }
            if (msg == WM_DPICHANGED_MAIN)
            {
                // Hooks run BEFORE WPF's HwndTarget processing, so this arms the spawn-suppression
                // window before the synchronous surface rebuild starts (OnDpiChanged re-arms it
                // afterwards — that call only ever extended the window after the fact).
                try { Services.UI.DisplayChangeCoordinator.NotifyDisplayChange("wm-dpichanged"); }
                catch { /* never let a hook throw */ }
                // handled stays false: WPF must still rescale the main UI.
                return IntPtr.Zero;
            }

            // Fix maximized window extending behind taskbar (buttons cut off)
            if (msg == WM_GETMINMAXINFO)
            {
                // Get the monitor this window is on. Screen enumeration can transiently fail
                // (see CLAUDE.md Known Issues #5) - if we can't resolve a monitor, leave the
                // struct untouched and `handled` false so Windows applies its own defaults.
                var monitor = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (monitor == null) return IntPtr.Zero;

                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var workingArea = monitor.WorkingArea;
                var bounds = monitor.Bounds;

                // Bug #620: ptMaxPosition is in coordinates RELATIVE TO THE TARGET MONITOR'S
                // ORIGIN, not virtual-desktop coordinates. Assigning workingArea.Left/Top raw
                // only happens to work on the primary monitor (origin 0,0); on a secondary
                // monitor at e.g. x=1920 it pushed the maximized window a further 1920px away
                // and the window vanished off-screen (alive in the taskbar, blank preview).
                // Subtracting the monitor's own bounds origin yields the correct offset - which
                // is normally (0,0), or non-zero only where the taskbar is docked left/top.
                // Do NOT "simplify" this back to raw working-area coordinates.
                mmi.ptMaxPosition.X = workingArea.Left - bounds.Left;
                mmi.ptMaxPosition.Y = workingArea.Top - bounds.Top;

                // Constrain maximized size to working area (excludes taskbar)
                mmi.ptMaxSize.X = workingArea.Width;
                mmi.ptMaxSize.Y = workingArea.Height;

                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            return IntPtr.Zero;
        }
















        /// <summary>
        /// ASK EMI WAVE 1 - the trigger for the dock chip's one and only knock.
        ///
        /// <para>Called at the far side of BOTH first-run branches, once per launch. It decides
        /// nothing: <see cref="Services.EmiDesk.EmiKnockMachine"/> owns the four brakes and every
        /// gate, and refuses silently, which is the answer almost every time.</para>
        ///
        /// <para><b>DispatcherPriority.Normal, never Loaded.</b> This app keeps the dispatcher busy
        /// enough (compositor host plus avatar animations) that Loaded-priority items are starved
        /// and never run at all - that is precisely what silently killed the original first-launch
        /// tour, and the comment in the wizard block above says so. The <c>IsLoaded</c> check is
        /// what Loaded priority was being used for; it is cheap and it is honest.</para>
        ///
        /// <para><paramref name="seenVersion"/> is the pre-stamp snapshot of
        /// <c>LastSeenVersion</c>. See where it is captured, above.</para>
        /// </summary>
        private void QueueEmiKnock(string seenVersion)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;

                disp.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        // SETTLED, not merely "after". MayKnock reads the screen at one instant, and
                        // the two things most likely to own it are queued rather than running: the
                        // wizard's own tour starts a beat after its last step, and What's New posts
                        // itself onto the dispatcher. So wait them out with the same idiom the mod
                        // picker uses a few hundred lines up - a beat for the queue to fill, then up
                        // to five minutes of somebody actually reading. Past that we simply do not
                        // knock; the offer has not been spent, so the next launch offers it properly.
                        await Task.Delay(1500);

                        for (int i = 0; i < 600 && (App.IsUpdateDialogActive || IsStartupDialogShowing
                                                    || App.Tutorial?.IsActive == true); i++)
                        {
                            await Task.Delay(500);
                        }

                        // The gates in MayKnock are all about "is anything else owning the screen".
                        // This one is about whether there is a screen at all yet.
                        if (!IsLoaded) return;
                        App.EmiDesk?.TryKnock(seenVersion);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug(ex, "EMI knock attempt failed");
                    }
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "EMI knock could not be queued");
            }
        }
    }

    /// <summary>Thin IWin32Window wrapper so WinForms dialogs get a proper owner handle.</summary>
    internal sealed class Win32WindowWrapper : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32WindowWrapper(IntPtr handle) => Handle = handle;
    }

    /// <summary>DTO bound to the top-bar mod-switcher ComboBox.</summary>
    public sealed class ModSelectorItem
    {
        public string Id { get; }
        public string Name { get; }
        public Brush AccentBrush { get; }

        public ModSelectorItem(string id, string name, Brush accentBrush)
        {
            Id = id;
            Name = name;
            AccentBrush = accentBrush;
        }
    }
}
