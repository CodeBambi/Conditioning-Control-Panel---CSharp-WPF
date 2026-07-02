using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Commands;
using Serilog;

using AppUpdateInfo = ConditioningControlPanel.Models.UpdateInfo;

namespace ConditioningControlPanel
{
    public partial class App : Application
    {
        /// <summary>
        /// Custom entry point. Originally added for Velopack's update hooks; kept after
        /// Velopack removal (v5.8.4) so we still control startup ordering explicitly.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

// Single instance mutex
        private static Mutex? _mutex;
        private static bool _mutexOwned = false;
        private const string MutexName = "ConditioningControlPanel_SingleInstance_Mutex";
        private const string ShowSignalName = "ConditioningControlPanel_ShowWindow_Signal";
        // Acknowledgment gate for the single-instance handshake. A second instance sets the
        // show-signal, then waits on this for the primary to confirm — from its UI thread — that
        // it actually surfaced a window. A wedged (render-thread deadlock) or headless primary
        // never runs the dispatcher callback, so it never acks; the second instance then presumes
        // the primary is dead, kills it, and takes over instead of silently exiting. Without this
        // ack, a zombie process kept the mutex alive and every relaunch quietly Shutdown()'d,
        // which is the "app freezes on startup, have to relaunch several times" report.
        private const string ShowAckSignalName = "ConditioningControlPanel_ShowAck_Signal";
        // Generous: exceeds a normal cold start so a healthy but still-initializing primary
        // (whose dispatcher isn't pumping yet) acks in time and is never mistaken for wedged.
        private const int ShowAckTimeoutMs = 10000;
        private static EventWaitHandle? _showSignal;
        private static EventWaitHandle? _showAckSignal;
        private static bool _recoveredFromStaleInstance;
        private SplashScreen? _splash;
        private static Thread? _showSignalThread;
        private readonly TaskCompletionSource _patreonInitDone = new();

        // "Open with CCP" handoff: --play / --edit args parsed at startup.
        // First instance routes directly after MainWindow loads; second instance
        // writes a handoff file at %LOCALAPPDATA%\ConditioningControlPanel\fileopen.pending
        // before signaling, which the listener reads and replays on the dispatcher.
        private static string? _pendingFileOpenAction;
        private static string? _pendingFileOpenPath;
        private static string FileOpenHandoffPath => Path.Combine(UserDataPath, "fileopen.pending");

        private static readonly HashSet<string> FileOpenAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v",
            ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
        };

        private static (string? action, string? path) ParseFileOpenArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                var a = args[i];
                if (a == "--play" || a == "--edit")
                {
                    var validated = ValidateMediaArgPath(args[i + 1]);
                    if (validated == null) return (null, null);
                    return (a == "--play" ? "play" : "edit", validated);
                }
            }
            return (null, null);
        }

        private static string? ValidateMediaArgPath(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            // Reject UNC and extended-length prefixes — only local file paths allowed.
            if (raw.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            if (raw.StartsWith(@"\\?\", StringComparison.Ordinal)) return null;
            string full;
            try { full = Path.GetFullPath(raw); }
            catch { return null; }
            if (!Path.IsPathRooted(full)) return null;
            if (!File.Exists(full)) return null;
            var ext = Path.GetExtension(full);
            if (!FileOpenAllowedExtensions.Contains(ext)) return null;
            return full;
        }

        private static void WriteFileOpenHandoff(string action, string path)
        {
            try
            {
                Directory.CreateDirectory(UserDataPath);
                File.WriteAllText(FileOpenHandoffPath, action + "\n" + path);
            }
            catch { /* best effort — failure just means second instance has no handoff */ }
        }

        private static (string? action, string? path) ConsumeFileOpenHandoff()
        {
            try
            {
                var p = FileOpenHandoffPath;
                if (!File.Exists(p)) return (null, null);
                var lines = File.ReadAllText(p).Split('\n');
                try { File.Delete(p); } catch { }
                if (lines.Length < 2) return (null, null);
                var action = lines[0].Trim();
                var path = ValidateMediaArgPath(lines[1].Trim());
                if (path == null) return (null, null);
                if (action != "play" && action != "edit") return (null, null);
                return (action, path);
            }
            catch { return (null, null); }
        }

        /// <summary>
        /// User data folder path in LocalAppData - persists across updates
        /// </summary>
        public static string UserDataPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel");

        /// <summary>
        /// User assets folder path - for user-added content that persists across updates
        /// </summary>
        public static string UserAssetsPath => Path.Combine(UserDataPath, "assets");

        /// <summary>
        /// Base URL for hosted tutorial pages. "Watch full tutorial" links in the
        /// video help system resolve against this. Placeholder - confirm before release.
        /// </summary>
        public const string TutorialBaseUrl = "https://cclabs.app/docs/tutorials/";

        /// <summary>
        /// Effective assets path - returns custom path if set, otherwise default UserAssetsPath.
        /// Use this for all asset loading (images, videos).
        /// </summary>
        public static string EffectiveAssetsPath
        {
            get
            {
                var customPath = Settings?.Current?.CustomAssetsPath;
                if (!string.IsNullOrWhiteSpace(customPath))
                {
                    if (Directory.Exists(customPath))
                    {
                        return customPath;
                    }
                    // A custom path is configured but its folder is gone (e.g. unplugged
                    // drive). Falling back to the default location is silent data desync —
                    // surface it once so it's diagnosable in the log (#391).
                    if (!_warnedMissingCustomAssetsPath)
                    {
                        _warnedMissingCustomAssetsPath = true;
                        Logger?.Warning("CustomAssetsPath '{Path}' does not exist — falling back to default assets folder. Imports/extractions will go to the default location.", customPath);
                    }
                }
                return UserAssetsPath;
            }
        }
        private static bool _warnedMissingCustomAssetsPath;

        /// <summary>
        /// Returns a temp directory for media files (decrypted packs, video downloads, etc.)
        /// located inside the effective assets path so it lives on the same drive as assets.
        /// Falls back to system temp if the assets path isn't available yet.
        /// </summary>
        public static string GetMediaTempPath()
        {
            try
            {
                var assetsPath = EffectiveAssetsPath;
                if (!string.IsNullOrEmpty(assetsPath))
                {
                    var tempDir = Path.Combine(assetsPath, ".temp");
                    Directory.CreateDirectory(tempDir);
                    return tempDir;
                }
            }
            catch (Exception ex)
            {
                Logger?.Debug("GetMediaTempPath: Could not use assets temp dir, falling back to system temp: {Error}", ex.Message);
            }
            return Path.GetTempPath();
        }

        /// <summary>
        /// Cleans up stale temp files from previous sessions (crash recovery).
        /// Deletes ccp_temp_* and haptic_video_* files from both the assets temp dir and system temp.
        /// </summary>
        public static void CleanupStaleTempFiles()
        {
            var dirsToClean = new List<string>();

            // Assets temp dir
            try
            {
                var assetsPath = EffectiveAssetsPath;
                if (!string.IsNullOrEmpty(assetsPath))
                {
                    var tempDir = Path.Combine(assetsPath, ".temp");
                    if (Directory.Exists(tempDir))
                        dirsToClean.Add(tempDir);
                }
            }
            catch { }

            // System temp (fallback path)
            try
            {
                dirsToClean.Add(Path.GetTempPath());
            }
            catch { }

            int deleted = 0;
            foreach (var dir in dirsToClean)
            {
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "ccp_temp_*"))
                    {
                        try { File.Delete(file); deleted++; }
                        catch { }
                    }
                    foreach (var file in Directory.GetFiles(dir, "haptic_video_*"))
                    {
                        try { File.Delete(file); deleted++; }
                        catch { }
                    }
                }
                catch { }
            }

            // Clean up old installer downloads (each version has a different filename so they pile up)
            try
            {
                var updateDir = Path.Combine(Path.GetTempPath(), "ConditioningControlPanel_Update");
                if (Directory.Exists(updateDir))
                {
                    Directory.Delete(updateDir, true);
                    deleted++;
                }
            }
            catch { }

            if (deleted > 0)
                Logger?.Information("Cleaned up {Count} stale temp files/folders from previous session", deleted);
        }

        // Static service references
        public static ILogger Logger { get; private set; } = null!;
        public static SettingsService Settings { get; private set; } = null!;

        // Transient feed of recent AI-driven effect actions, surfaced in the Companion tab's
        // "Live actions" panel. Populated by the upcoming local-LLM effect controller; not persisted.
        public static ObservableCollection<string> AiLiveActions { get; } = new();

        public static FlashService Flash { get; private set; } = null!;
        public static VideoService Video { get; private set; } = null!;
        public static AudioService Audio { get; private set; } = null!;
        public static SessionLogService SessionLog { get; private set; } = null!;
        public static ProgressionService Progression { get; private set; } = null!;
        public static SubliminalService Subliminal { get; private set; } = null!;
        public static OverlayService Overlay { get; private set; } = null!;
        public static ScreenShakeService ScreenShake { get; private set; } = null!;
        public static BubbleService Bubbles { get; private set; } = null!;
        public static Services.Chaos.ChaosModeService Chaos { get; private set; } = null!;
        public static LockCardService LockCard { get; private set; } = null!;
        public static PopQuizService PopQuiz { get; private set; } = null!;
        public static BubbleCountService BubbleCount { get; private set; } = null!;
        public static BouncingTextService BouncingText { get; private set; } = null!;
        public static MindWipeService MindWipe { get; private set; } = null!;
        public static BrainDrainService BrainDrain { get; private set; } = null!;
        public static AchievementService Achievements { get; private set; } = null!;
        public static GamificationBridge? Gamification { get; private set; }
        public static BarkService? Bark { get; private set; }
        public static QuestDefinitionService QuestDefinitions { get; private set; } = null!;
        public static QuestService Quests { get; private set; } = null!;
        public static TutorialService Tutorial { get; private set; } = null!;
        public static IAiService Ai { get; private set; } = null!;
        public static IAiCommandService Commands { get; private set; } = null!;
        public static Services.Moderation.IModerationGuard ModerationGuard { get; private set; } = null!;
        public static Services.Moderation.ModerationLog ModerationLog { get; private set; } = null!;
        public static Services.Moderation.ModerationSession ModerationSession { get; private set; } = null!;
        public static Services.Moderation.IPromptValidator PromptValidator { get; private set; } = null!;
        public static Services.Moderation.IModerationCounter ModerationCounter { get; private set; } = null!;
        public static WindowAwarenessService WindowAwareness { get; private set; } = null!;
        public static PatreonService Patreon { get; private set; } = null!;
        public static SubscribeStarService SubscribeStar { get; private set; } = null!;
        public static UpdateService Update { get; private set; } = null!;
        public static ProfileSyncService ProfileSync { get; private set; } = null!;
        public static LeaderboardService Leaderboard { get; private set; } = null!;
        public static HapticService Haptics { get; private set; } = null!;
        public static AudioSyncService? AudioSync { get; private set; }
        public static DiscordRichPresenceService DiscordRpc { get; private set; } = null!;
        public static DiscordService Discord { get; private set; } = null!;
        public static DualMonitorVideoService DualMonitorVideo { get; private set; } = null!;
        public static ScreenMirrorService ScreenMirror { get; private set; } = null!;
        public static AutonomyService Autonomy { get; private set; } = null!;
        /// <summary>Offline speech recognition (Takeover "repeat after me"). May be unavailable (no model/mic); callers check IsAvailable.</summary>
        public static Services.Speech.SpeechService Speech { get; private set; } = null!;
        /// <summary>Offline "Hey Bambi" wake-word spotter (sherpa-onnx KWS, no key). Unavailable until the model is dropped into Resources\Models\sherpa-kws\; the wake loop falls back to Vosk when so.</summary>
        public static Services.Speech.SherpaWakeService WakeWord { get; private set; } = null!;
        public static InteractionQueueService InteractionQueue { get; private set; } = null!;
        public static ContentPackService ContentPacks { get; private set; } = null!;
        public static CompanionService Companion { get; private set; } = null!;
        public static CommunityPromptService CommunityPrompts { get; private set; } = null!;
        public static PersonalityService Personality { get; private set; } = null!;
        public static RoadmapService Roadmap { get; private set; } = null!;
        public static SkillTreeService SkillTree { get; private set; } = null!;
        public static KeywordTriggerService KeywordTriggers { get; private set; } = null!;
        public static KeywordTriggerPresetService KeywordPresets { get; private set; } = null!;
        public static ScreenOcrService ScreenOcr { get; private set; } = null!;
        public static KeywordHighlightService? KeywordHighlight { get; private set; }
        public static ActivityTracker ActivityTracker { get; private set; } = null!;
        public static RemoteControlService RemoteControl { get; private set; } = null!;
        public static AvailableSubjectsService AvailableSubjects { get; private set; } = null!;
        public static CompanionPhraseService CompanionPhrases { get; private set; } = null!;
        public static CatalogueService Catalogue { get; private set; } = null!;
        public static CatalogueLookupService CatalogueLookup { get; private set; } = null!;
        public static LockdownService Lockdown { get; private set; } = null!;
        public static MantraService Mantra { get; private set; } = null!;
        public static MantraVoiceService MantraVoice { get; private set; } = null!;
        public static ModService Mods { get; private set; } = null!;
        public static BugReportService BugReport { get; private set; } = null!;
        public static WallpaperService? Wallpaper { get; private set; }
        public static WebcamTrackingService Webcam { get; private set; } = null!;
        public static NotificationService Notifications { get; private set; } = null!;
        public static AttentionCheckService AttentionCheck { get; private set; } = null!;
        public static FocusGameService FocusGame { get; private set; } = null!;
        public static GazeFocusService GazeFocus { get; private set; } = null!;
        public static GazeDebugCursorService GazeCursor { get; private set; } = null!;
        public static GazeDriftCorrectionService GazeDrift { get; private set; } = null!;
        public static BlinkTrainerService BlinkTrainer { get; private set; } = null!;
        public static Services.Deeper.EnhancementLibrary EnhancementLibrary { get; private set; } = null!;
        public static Services.Deeper.EnhancementAudioPlayer DeeperPlayer { get; private set; } = null!;
        public static Services.Deeper.EnhancementHostService DeeperHost { get; private set; } = null!;
        public static Services.Deeper.EnhancementFetcher DeeperFetcher { get; private set; } = null!;
        public static Services.Deeper.BrowserAutoDiscovery DeeperBrowserDiscovery { get; private set; } = null!;
        // Bridge that ties dashboard browser navigation to the local enhancement
        // library; created lazily by MainWindow when the WebView2 spins up.
        public static Services.Deeper.BrowserEnhancementBridge? BrowserEnhanceBridge { get; set; }
        // Bridge that ties VideoService playback (mandatory + asset-folder videos)
        // to the enhancement runtime. Owns its own host; gated by
        // AppSettings.VideoEnhanceIfPossible (default off).
        public static Services.Deeper.VideoEnhancementBridge? VideoEnhanceBridge { get; private set; }

        /// <summary>
        /// Whether user is logged in with Patreon, Discord, or email (required for progression tracking).
        /// HasCloudIdentity covers email login (has UnifiedId) and restored sessions.
        /// </summary>
        public static bool IsLoggedIn => (Patreon?.IsAuthenticated == true) || (Discord?.IsAuthenticated == true) || (SubscribeStar?.IsAuthenticated == true) || HasCloudIdentity;

        /// <summary>
        /// Whether a conditioning session is currently running. Set by MainWindow.
        /// </summary>
        public static bool IsSessionRunning { get; set; }

        /// <summary>
        /// Whether the main engine is running (toggle-driven services should start/stop live).
        /// True for both plain engine runs and AI sessions. Set by MainWindow.StartEngine/StopEngine.
        /// </summary>
        public static bool IsEngineRunning { get; set; }

        /// <summary>
        /// Direct reference to the MainWindow instance. Use this instead of
        /// Application.Current.MainWindow — the latter returns null when the window
        /// is hidden to tray.
        /// </summary>
        public static MainWindow? MainWindowRef { get; set; }

        /// <summary>
        /// Unified user ID that links Patreon and Discord accounts together
        /// </summary>
        public static string? UnifiedUserId { get; set; }

        /// <summary>
        /// Snapshot of the UnifiedUserId as restored from settings at startup, captured
        /// BEFORE session validation can null it out on a token/session failure. Used by
        /// the re-login flow to tell "same account re-login" from "different account" even
        /// after an expired session cleared the live UnifiedUserId — so re-logging into the
        /// same account never gets misclassified as a new account and wipes progression.
        /// </summary>
        public static string? StartupUnifiedId { get; private set; }

        /// <summary>
        /// User identifier for server communication. Only the unified ID is valid —
        /// fallback IDs like "patreon:email" don't match any server key.
        /// </summary>
        public static string? EffectiveUserId => UnifiedUserId;

        /// <summary>
        /// Whether the user has a cloud identity (unified ID) for server features
        /// like remote control, leaderboard, and profile sync.
        /// </summary>
        public static bool HasCloudIdentity => !string.IsNullOrEmpty(UnifiedUserId);

        /// <summary>
        /// Get the user's display name. In offline mode, returns the offline username.
        /// Otherwise returns Patreon or Discord display name.
        /// </summary>
        public static string? UserDisplayName
        {
            get
            {
                // In offline mode with a username set, use that
                if (Settings?.Current?.OfflineMode == true &&
                    !string.IsNullOrWhiteSpace(Settings?.Current?.OfflineUsername))
                {
                    return Settings.Current.OfflineUsername;
                }

                // Prioritize V2 unified display name (leaderboard name), then fall back to provider names
                return Settings?.Current?.UserDisplayName
                    ?? Patreon?.DisplayName
                    ?? Discord?.CustomDisplayName
                    ?? Discord?.DisplayName
                    ?? SubscribeStar?.DisplayName;
            }
        }

        /// <summary>
        /// Reference to the avatar companion window (set by MainWindow)
        /// </summary>
        public static AvatarTubeWindow? AvatarWindow { get; set; }

        // Screen enumeration cache
        private static System.Windows.Forms.Screen[]? _cachedScreens;
        private static DateTime _screenCacheTime = DateTime.MinValue;
        private static readonly TimeSpan ScreenCacheDuration = TimeSpan.FromSeconds(5);
        private static readonly object _screenCacheLock = new();

        /// <summary>
        /// Gets all screens with caching to reduce expensive Win32 calls.
        /// Cache is valid for 5 seconds - long enough to avoid repeated calls in tight loops,
        /// short enough to detect monitor changes.
        /// </summary>
        public static System.Windows.Forms.Screen[] GetAllScreensCached()
        {
            lock (_screenCacheLock)
            {
                if (_cachedScreens == null || DateTime.Now - _screenCacheTime > ScreenCacheDuration)
                {
                    try
                    {
                        _cachedScreens = System.Windows.Forms.Screen.AllScreens;
                        _screenCacheTime = DateTime.Now;
                        Logger?.Debug("Screen enumeration: {Count} monitors detected: {Names}",
                            _cachedScreens.Length,
                            string.Join(", ", _cachedScreens.Select(s => $"{s.DeviceName} ({s.Bounds.Width}x{s.Bounds.Height})")));
                    }
                    catch (Exception ex)
                    {
                        Logger?.Debug("Failed to enumerate screens: {Error}", ex.Message);
                        // Return empty array if enumeration fails (can happen during certain system states)
                        return _cachedScreens ?? Array.Empty<System.Windows.Forms.Screen>();
                    }
                }
                return _cachedScreens ?? Array.Empty<System.Windows.Forms.Screen>();
            }
        }

        /// <summary>
        /// Invalidates the screen cache, forcing the next call to re-enumerate.
        /// Call this when monitor configuration might have changed.
        /// </summary>
        public static void InvalidateScreenCache()
        {
            lock (_screenCacheLock)
            {
                _cachedScreens = null;
                _screenCacheTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Monitor topology / resolution / DPI changed. Refresh the screen cache and quiesce the
        /// layered-window spawn paths for a beat (WPF is rebuilding composition surfaces; adding new
        /// ones now risks desktop-heap/GPU-surface exhaustion — the freeze cluster). Fires on the UI
        /// thread via SystemEvents' WPF message pump.
        /// </summary>
        private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            InvalidateScreenCache();
            Services.UI.DisplayChangeCoordinator.NotifyDisplayChange("display-settings");
        }

        /// <summary>
        /// Returns the monitor the user picked for webcam calibration / Quick
        /// Recal / Tracker Test, falling back to the primary screen if their
        /// saved choice is "Primary" or no longer present (monitor unplugged
        /// or device-name-renamed). Never returns null on a working system —
        /// callers can assume a valid Screen comes back.
        /// </summary>
        public static System.Windows.Forms.Screen? GetWebcamCalibrationScreen()
        {
            try
            {
                var name = Settings?.Current?.WebcamCalibrationScreen;
                var screens = GetAllScreensCached();
                if (screens.Length == 0) return System.Windows.Forms.Screen.PrimaryScreen;
                if (string.IsNullOrEmpty(name) || string.Equals(name, "Primary", StringComparison.OrdinalIgnoreCase))
                    return System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
                foreach (var s in screens)
                {
                    if (string.Equals(s.DeviceName, name, StringComparison.OrdinalIgnoreCase))
                        return s;
                }
                Logger?.Debug("GetWebcamCalibrationScreen: saved monitor {Name} not found, falling back to Primary", name);
                return System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
            }
            catch (Exception ex)
            {
                Logger?.Debug("GetWebcamCalibrationScreen failed: {Error}", ex.Message);
                return System.Windows.Forms.Screen.PrimaryScreen;
            }
        }

        /// <summary>
        /// Positions a borderless WPF window so it maximizes on the user's
        /// chosen calibration monitor. Must be called BEFORE Show()/ShowDialog().
        /// Safe no-op if the screen lookup fails.
        /// </summary>
        public static void ApplyCalibrationScreenPlacement(System.Windows.Window window)
        {
            if (window == null) return;
            try
            {
                var screen = GetWebcamCalibrationScreen();
                if (screen == null) return;
                // For Maximized + WindowStartupLocation=Manual, WPF picks the
                // monitor containing (Left, Top) and maximizes there. Setting
                // these in physical pixels is fine — the position only needs
                // to land somewhere inside the target screen's pixel rect, and
                // a pixel offset stays inside the same monitor.
                window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                window.Left = screen.Bounds.Left;
                window.Top = screen.Bounds.Top;
            }
            catch (Exception ex)
            {
                Logger?.Debug("ApplyCalibrationScreenPlacement failed: {Error}", ex.Message);
            }
        }

        // --- CCP window rect cache (used by Awareness Engine self-exclusion) ---
        private static System.Drawing.Rectangle[]? _cachedCcpWindowRects;
        private static DateTime _ccpWindowRectsCacheTime = DateTime.MinValue;
        private static readonly TimeSpan CcpWindowRectsCacheDuration = TimeSpan.FromMilliseconds(250);
        private static readonly object _ccpWindowRectsLock = new();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out CcpRect lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct CcpRect { public int Left, Top, Right, Bottom; }

        /// <summary>
        /// Returns screen rectangles of all currently visible CCP-owned windows
        /// (MainWindow, avatar, overlays, dialogs) in PHYSICAL pixels on the
        /// virtual desktop. Used by ScreenOcrService to drop OCR word hits that
        /// fall inside our own UI, preventing feedback loops.
        ///
        /// Uses Win32 <c>GetWindowRect</c> directly rather than WPF Window.Left/Top
        /// multiplied by CompositionTarget scale — the latter is unreliable on
        /// PerMonitorV2 + multi-monitor setups because Left/Top is anchored to
        /// primary's DIP space while the scale is the current window's monitor
        /// scale, producing oversized rects that incorrectly swallow external
        /// OCR hits. <c>GetWindowRect</c> returns physical virtual-desktop pixels
        /// in one call, which is what OCR hits are already expressed in.
        ///
        /// Cached for a short interval to stay cheap under per-scan filtering.
        /// </summary>
        public static System.Drawing.Rectangle[] GetCcpWindowRectsCached()
        {
            lock (_ccpWindowRectsLock)
            {
                if (_cachedCcpWindowRects != null &&
                    DateTime.Now - _ccpWindowRectsCacheTime <= CcpWindowRectsCacheDuration)
                {
                    return _cachedCcpWindowRects;
                }

                var rects = new System.Collections.Generic.List<System.Drawing.Rectangle>();
                try
                {
                    // Must run on the UI thread to enumerate Application.Current.Windows safely.
                    var dispatcher = Current?.Dispatcher;
                    if (dispatcher == null || dispatcher.HasShutdownStarted)
                    {
                        _cachedCcpWindowRects = Array.Empty<System.Drawing.Rectangle>();
                        _ccpWindowRectsCacheTime = DateTime.Now;
                        return _cachedCcpWindowRects;
                    }

                    // Collect the HWNDs on the UI thread, then call GetWindowRect
                    // outside the dispatcher lock — GetWindowRect is a thread-safe
                    // Win32 call and doesn't need dispatcher affinity.
                    var hwnds = new System.Collections.Generic.List<IntPtr>();
                    System.Drawing.Rectangle[] bouncingRects = Array.Empty<System.Drawing.Rectangle>();
                    System.Drawing.Rectangle[] subliminalRects = Array.Empty<System.Drawing.Rectangle>();
                    dispatcher.Invoke(() =>
                    {
                        foreach (var w in Current!.Windows.OfType<Window>())
                        {
                            try
                            {
                                if (!w.IsVisible) continue;
                                if (w.WindowState == WindowState.Minimized) continue;

                                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                                if (hwnd != IntPtr.Zero) hwnds.Add(hwnd);
                            }
                            catch { /* skip malformed window */ }
                        }

                        // Bouncing text lives in a full-screen overlay window that
                        // the per-monitor span filter below drops, so its small
                        // moving text rect would otherwise be read back by the
                        // awareness OCR (#287). Capture it here on the UI thread.
                        bouncingRects = BouncingText?.GetActiveTextScreenRects()
                                        ?? Array.Empty<System.Drawing.Rectangle>();

                        // Subliminal cards are full-screen keep-alive overlays (dropped by the
                        // span filter below) but are now intentionally left in screen capture so
                        // they record. To still keep them out of the awareness OCR, exclude just
                        // the centered text rect of any subliminal currently flashing (#287 pattern).
                        subliminalRects = Subliminal?.GetActiveTextScreenRects()
                                          ?? Array.Empty<System.Drawing.Rectangle>();
                    });

                    // Per-monitor span filter: any CCP window whose rect fully
                    // covers any single screen is a full-screen overlay
                    // container (flash/gaze/bubble surfaces, blur overlays,
                    // and the BouncingText overlay). Those carry no readable
                    // text at the window level but spanned monitor-sized
                    // exclusion rects were swallowing every OCR'd word in
                    // multi-monitor setups (#273). Sized windows like
                    // AvatarTube, MantraWindow, LockCard, subliminal popups,
                    // etc. fall well below per-monitor bounds and stay in the
                    // exclusion list where they belong. BouncingText IS
                    // full-screen, so its actual text rect is added separately
                    // below (#287) rather than excluding the whole monitor.
                    var screens = GetAllScreensCached();

                    foreach (var hwnd in hwnds)
                    {
                        if (!IsWindowVisible(hwnd)) continue;
                        if (!GetWindowRect(hwnd, out var r)) continue;

                        int w = r.Right - r.Left;
                        int h = r.Bottom - r.Top;
                        if (w <= 0 || h <= 0) continue;

                        if (SpansAnyMonitor(w, h, screens)) continue;

                        rects.Add(new System.Drawing.Rectangle(r.Left, r.Top, w, h));
                    }

                    // Add the bouncing-text rect (captured on the UI thread above).
                    // It rode through the span filter as a full-screen window, so
                    // only its small moving text region is excluded — not the
                    // whole monitor (which would regress #273).
                    foreach (var br in bouncingRects)
                    {
                        if (br.Width > 0 && br.Height > 0) rects.Add(br);
                    }

                    // Subliminal text rects (captured on the UI thread above), same rationale as
                    // bouncing text: the full-screen window was span-filtered out, so only the small
                    // visible text region is excluded — not the whole monitor.
                    foreach (var sr in subliminalRects)
                    {
                        if (sr.Width > 0 && sr.Height > 0) rects.Add(sr);
                    }
                }
                catch (Exception ex)
                {
                    Logger?.Debug("GetCcpWindowRectsCached failed: {Error}", ex.Message);
                }

                _cachedCcpWindowRects = rects.ToArray();
                _ccpWindowRectsCacheTime = DateTime.Now;
                return _cachedCcpWindowRects;
            }
        }

        /// <summary>
        /// Force the next <see cref="GetCcpWindowRectsCached"/> call to rebuild instead of
        /// returning the 250ms-stale cache. Called when a transient overlay appears (e.g. a
        /// subliminal flash) so its text rect is folded into the OCR self-exclusion set before
        /// the awareness OCR can read it, rather than waiting out the cache window.
        /// </summary>
        public static void InvalidateCcpWindowRectsCache()
        {
            lock (_ccpWindowRectsLock)
            {
                _ccpWindowRectsCacheTime = DateTime.MinValue;
            }
        }

        // True if the window covers any single screen in full. 4px tolerance
        // absorbs chrome / DPI rounding so legitimately-fullscreen windows
        // still classify as monitor-spanning. Sized utility windows
        // (AvatarTube, MantraWindow, LockCard) are well below per-monitor
        // bounds and pass through to the exclusion list. Full-screen overlay
        // windows (flash/bubble surfaces, BouncingText, subliminal cards) are
        // dropped here and instead contribute only their small text rects via
        // GetActiveTextScreenRects so they don't swallow every OCR'd word.
        private static bool SpansAnyMonitor(int width, int height, System.Windows.Forms.Screen[] screens)
        {
            if (screens == null || screens.Length == 0) return false;
            const int tolerancePx = 4;
            foreach (var s in screens)
            {
                var b = s.Bounds;
                if (width >= b.Width - tolerancePx && height >= b.Height - tolerancePx) return true;
            }
            return false;
        }

        /// <summary>
        /// Flag to indicate if an update dialog is currently being shown.
        /// Used to delay tutorial until update is handled.
        /// </summary>
        public static bool IsUpdateDialogActive { get; set; } = false;

        /// <summary>
        /// Flag to prevent concurrent update checks
        /// </summary>
        private static bool _isCheckingForUpdates = false;

        /// <summary>
        /// Immediately kills ALL audio and visual effects across all services.
        /// Used for panic exit and application shutdown to ensure clean state.
        /// </summary>
        public static void KillAllAudio()
        {
            try
            {
                // Stop subliminal whispers
                Subliminal?.Stop();

                // Stop flash sounds and images
                Flash?.Stop();

                // Stop mind wipe audio
                MindWipe?.Stop();

                // Stop brain drain audio
                BrainDrain?.Stop();

                // Stop video audio (closes video windows)
                Video?.Stop();

                // Stop bubble pop sounds and visuals
                Bubbles?.Stop();

                // Stop bubble count game
                BubbleCount?.Stop();

                // Stop bouncing text overlay
                BouncingText?.Stop();

                // Stop all visual overlays (spiral, pink filter, etc.)
                Overlay?.Stop();

                // Stop lock card and pop quiz if active
                LockCard?.Stop();
                PopQuiz?.Stop();

                // Stop mantra lab audio
                Mantra?.Dispose();

                // Stop autonomy mode
                Autonomy?.Stop();

                // Restore wallpaper
                Wallpaper?.Deactivate();

                // Stop avatar voice lines
                AvatarWindow?.StopVoiceLineAudio();

                // Reset audio ducking - CRITICAL for clean exit
                Audio?.ForceUnduck();

                Logger?.Debug("KillAllAudio: All audio and effects stopped");
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Error in KillAllAudio");
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Render-thread deadlock guard (see avatar-tube-render-deadlock memory): the avatar
            // tube is a layered window sharing WPF's single render thread; a layered ComboBox
            // dropdown resizing/closing while the tube animates can wedge that thread
            // (Application Hang 1002 — reproduced 4x on 2026-06-10 around mod switches).
            // De-layer every ComboBox popup so the dropdown is a plain window: square corners,
            // no shadow, no deadlock. Tooltips were de-layered the same way in App.xaml.
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ComboBox), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) =>
                {
                    try
                    {
                        if (s is System.Windows.Controls.ComboBox cb &&
                            cb.Template?.FindName("PART_Popup", cb) is System.Windows.Controls.Primitives.Popup p &&
                            !p.IsOpen)
                        {
                            p.AllowsTransparency = false;
                            p.PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None;
                        }
                    }
                    catch { }
                }));

            // Show splash screen IMMEDIATELY - before anything else
            // This ensures users see feedback right away after update/launch
            _splash = new SplashScreen();
            var splash = _splash;
            splash.Show();
            splash.SetProgress(0.0, "Starting...");

            // Force the splash to render before continuing with any initialization
            splash.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));

            // Parse "Open with CCP" args. Done before single-instance check so a
            // second-instance launch can write its handoff file before signaling.
            (_pendingFileOpenAction, _pendingFileOpenPath) = ParseFileOpenArgs(e.Args);

            // Check for single instance
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            _mutexOwned = createdNew; // Track if we actually own the mutex
            if (!createdNew)
            {
                // Another instance holds the single-instance mutex. Ask it to show its window —
                // but confirm it's actually alive before we bow out. A wedged/headless primary
                // keeps the mutex forever, and the old "signal then Shutdown()" left every
                // relaunch a silent no-op until the zombie finally died. Now we wait for an ack
                // and, if none comes, kill the stale process and take over as primary.

                // Write the "Open with CCP" handoff BEFORE signaling so a live primary can read it.
                if (_pendingFileOpenAction != null && _pendingFileOpenPath != null)
                {
                    try { WriteFileOpenHandoff(_pendingFileOpenAction, _pendingFileOpenPath); } catch { }
                }

                EventWaitHandle? ackWait = null;
                try { ackWait = EventWaitHandle.OpenExisting(ShowAckSignalName); } catch { ackWait = null; }

                if (ackWait == null)
                {
                    // Primary predates the ack handshake (mid-upgrade) or its kernel objects are
                    // gone. Preserve the legacy behavior: poke it to show, then exit quietly —
                    // never kill a process we can't positively identify as wedged.
                    try
                    {
                        var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                        signal.Set();
                        signal.Dispose();
                    }
                    catch { }

                    splash.Close();
                    Shutdown();
                    return;
                }

                // Clear any stale ack from a prior handshake, poke the primary, then wait for it
                // to confirm liveness from its UI thread.
                try { ackWait.Reset(); } catch { }
                try
                {
                    var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                    signal.Set();
                    signal.Dispose();
                }
                catch { }

                bool acknowledged = false;
                try { acknowledged = ackWait.WaitOne(ShowAckTimeoutMs); } catch { }
                try { ackWait.Dispose(); } catch { }

                if (acknowledged)
                {
                    // Primary is alive and surfaced its window — nothing more to do.
                    splash.Close();
                    Shutdown();
                    return;
                }

                // No acknowledgment within the window: the primary is wedged or headless. Kill it
                // and take over. (Logger isn't up yet here; we record the recovery once it is.)
                _recoveredFromStaleInstance = true;
                KillStaleInstances();

                // Claim single-instance ownership now that the zombie is gone. If it died holding
                // the mutex, WaitOne throws AbandonedMutexException but we DO acquire it.
                try { if (_mutex!.WaitOne(TimeSpan.FromSeconds(3))) _mutexOwned = true; }
                catch (AbandonedMutexException) { _mutexOwned = true; }
                catch { }

                // We kept the parsed _pendingFileOpen* fields and will fulfill them ourselves, so
                // drop any on-disk handoff to avoid a spurious re-open on a later signal.
                try { ConsumeFileOpenHandoff(); } catch { }

                // Fall through — this instance is now the primary.
            }

            // Create signal for other instances to request showing our window, plus the ack gate
            // a second instance waits on to prove this instance's UI thread is responsive.
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            // ManualReset: a second instance Reset()s it just before signaling, so a leftover
            // ack from a prior handshake can't be mistaken for a fresh one.
            _showAckSignal = new EventWaitHandle(false, EventResetMode.ManualReset, ShowAckSignalName);
            _showSignalThread = new Thread(() =>
            {
                while (_showSignal != null)
                {
                    try
                    {
                        if (_showSignal.WaitOne(1000))
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                // Use the stable static ref: Application.Current.MainWindow
                                // is null while the app is minimized to the tray, which is
                                // exactly when "Open with CCP" needs to wake it — using the
                                // instance property there silently dropped the file handoff.
                                var mainWin = MainWindowRef ?? (MainWindow as MainWindow);
                                if (mainWin != null)
                                {
                                    try { mainWin.ShowFromTray(); }
                                    catch (Exception ex) { Logger?.Warning(ex, "ShowFromTray failed"); }
                                    var (action, path) = ConsumeFileOpenHandoff();
                                    if (action != null && path != null)
                                    {
                                        try { mainWin.HandlePendingFileOpen(action, path); }
                                        catch (Exception ex) { Logger?.Warning(ex, "HandlePendingFileOpen failed"); }
                                    }
                                }

                                // Reaching here means the UI thread is alive and pumping — ack the
                                // waiting second instance so it exits instead of killing us. Sent
                                // even when mainWin is null (still starting): a responsive
                                // dispatcher is proof enough that we are not wedged.
                                try { _showAckSignal?.Set(); } catch { }
                            });
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "ShowWindowSignalListener"
            };
            _showSignalThread.Start();

            base.OnStartup(e);

            // Cap all WPF animations to 30 FPS (default 60) to reduce idle CPU usage.
            // Decorative animations (glows, shimmers, particles) look identical at 30 FPS.
            // Feature animations using DispatcherTimers are unaffected.
            System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(System.Windows.Media.Animation.Timeline),
                new FrameworkPropertyMetadata(30));

            splash.SetProgress(0.05, "Initializing logging...");

            // Setup logging - use UserDataPath (writable) instead of BaseDirectory (may be in Program Files)
            string logPath;
            try
            {
                logPath = Path.Combine(UserDataPath, "logs");
                Directory.CreateDirectory(logPath);
            }
            catch
            {
                // Last resort fallback to temp directory if even UserDataPath fails
                logPath = Path.Combine(Path.GetTempPath(), "ConditioningControlPanel", "logs");
                try { Directory.CreateDirectory(logPath); } catch { }
            }

            Logger = new LoggerConfiguration()
                .MinimumLevel.Information() // Security: Changed from Debug to avoid exposing sensitive data in logs
                .WriteTo.File(Path.Combine(logPath, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    // Force a disk flush each second so the LAST lines survive a hard process death
                    // (a native OOM kills the process with no managed unwind — see chaos OOM telemetry).
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();

            // Route the shared (Core) display-change spawn-suppress diagnostic through our logger.
            Services.UI.DisplayChangeCoordinator.DebugLog = msg => Logger.Debug(msg);

            // Log the RUNTIME version (not just the source constant) + memory baseline. A stale
            // publish can ship old code under a new label; this line is how we catch that, and the
            // working-set baseline anchors the chaos OOM telemetry.
            Logger.Information("Application starting v{Version} | workingSet {WS}MB",
                Services.UpdateService.AppVersion, Environment.WorkingSet / (1024 * 1024));

            // Rotate crash.log so a bug report only carries crashes from THIS build. The log is
            // append-only, so without this it accumulates months of old crashes and the reporter
            // ships them all (the last 120KB), burying the real failure and polluting triage.
            RotateCrashLogForVersion(logPath);

            // Surface a single-instance takeover (a prior wedged/headless process was killed so
            // this launch could proceed). Recorded here because Logger isn't up during the handshake.
            if (_recoveredFromStaleInstance)
                Logger.Warning("[LIFECYCLE] Previous instance was unresponsive (no show-ack within {Ms}ms) — killed it and took over as primary", ShowAckTimeoutMs);

            // If a Rabbit Hole run was live when the process last died, the native vanish left nothing
            // in crash.log — but the chaos sentinel file is still on disk. Report+consume it so the
            // crash self-documents (with last-known context) in this session's log.
            Services.Chaos.ChaosCrashSentinel.ConsumeAndReport(Logger);

            // React to monitor topology / resolution changes (unplug, res change, DPI): drop the stale
            // screen cache AND pause layered-window spawns briefly so we don't create fresh surfaces
            // during the composition rebuild storm (freeze cluster — see DisplayChangeCoordinator).
            try { SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged; } catch { }

            // Hang forensics: the recurring freezes are render-thread deadlocks (Application
            // Hang 1002, nothing in crash.log). The watchdog writes one minidump per session
            // to the logs folder when the dispatcher stops responding for 10s.
            Services.UiHangWatchdog.Start(Dispatcher);

            splash.SetProgress(0.1, "Initializing...");

            // Global exception handlers to catch and log crashes instead of hard crashing
            bool errorDialogShown = false;
            int exitInProgress = 0;
            DispatcherUnhandledException += (s, args) =>
            {
                LogCrashDetails("DISPATCHER", args.Exception);

                // GDI / desktop-heap quota exhaustion while WPF shows a layered window
                // (heavy effect load, esp. many full-screen subliminal/flash surfaces on a
                // multi-monitor setup). This is RECOVERABLE — the failed window-show just
                // drops a frame. Swallow it instead of crashing or wedging the UI.
                // (#394/#395: "Not enough quota is available to process this command",
                //  ERROR_NOT_ENOUGH_QUOTA 1816 / ERROR_NO_SYSTEM_RESOURCES 1450.)
                if (args.Exception is System.ComponentModel.Win32Exception quotaEx &&
                    (quotaEx.NativeErrorCode == 1816 || quotaEx.NativeErrorCode == 1450 ||
                     quotaEx.Message.Contains("Not enough quota")))
                {
                    try { Logger?.Warning("Window-show quota exhausted (GDI/desktop heap) — dropped an effect frame: {Msg}", quotaEx.Message); } catch { }
                    args.Handled = true;
                    return;
                }

                // Check for rendering thread failure - this is unrecoverable and can cause dialog loops
                var isRenderFailure = args.Exception.Message.Contains("RENDER") ||
                                      args.Exception.Message.Contains("0x88980406") ||
                                      args.Exception.HResult == unchecked((int)0x88980406) ||
                                      args.Exception is OutOfMemoryException;

                // Render-thread failure / OOM in the composition channel is unrecoverable.
                // Exit IMMEDIATELY, before any UI attempt - MessageBox.Show runs a nested
                // dispatcher pump and the render thread keeps crashing inside that pump,
                // so we can't safely show a dialog. (See 2026-05-25 crash storm:
                // 10,251 cascading reports because the exit branch was gated behind a
                // blocking MessageBox.Show that never returned.)
                if (isRenderFailure)
                {
                    if (Interlocked.Exchange(ref exitInProgress, 1) == 0)
                    {
                        try { Logger?.Error("Render thread failure / OOM - hard exit to prevent cascade"); } catch { }
                        Environment.Exit(1);
                    }
                    args.Handled = true;
                    return;
                }

                // Only show error dialog once to prevent multiplying dialogs
                if (!errorDialogShown)
                {
                    errorDialogShown = true;

                    // Close splash screen if still open so error dialog is visible
                    try { _splash?.Close(); } catch { }
                    _splash = null;

                    try
                    {
                        MessageBox.Show($"An error occurred:\n\n{args.Exception.Message}\n\nDetails logged to crash log.",
                            "Error - Please report this", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch { /* MessageBox may fail during shutdown */ }
                }

                args.Handled = true; // Prevent crash, just log
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                LogCrashDetails("DOMAIN", ex);
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogCrashDetails("TASK", args.Exception);
                args.SetObserved();
            };

            // Clean up old update packages in background (don't block startup)
            _ = Task.Run(() =>
            {
                try
                {
                    UpdateService.CleanupOldPackages();
                }
                catch (Exception ex)
                {
                    Logger?.Warning(ex, "Background cleanup of old packages failed");
                }
            });

            splash.SetProgress(0.1, "Creating directories...");

            // Create user assets directories in LocalAppData (persists across updates)
            Directory.CreateDirectory(Path.Combine(UserAssetsPath, "images"));
            Directory.CreateDirectory(Path.Combine(UserAssetsPath, "videos"));
            Directory.CreateDirectory(Path.Combine(UserAssetsPath, "wallpapers"));
            Directory.CreateDirectory(Path.Combine(UserDataPath, "Spirals"));

            // Create Resources directories (these are bundled with app, not user content)
            var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
            Directory.CreateDirectory(resourcesPath);
            Directory.CreateDirectory(Path.Combine(resourcesPath, "sub_audio"));
            Directory.CreateDirectory(Path.Combine(resourcesPath, "sounds", "mindwipe"));

            splash.SetProgress(0.2, "Loading settings...");

            // Initialize services
            Settings = new SettingsService();

            // One-shot settings migrations. Must run before anything reads
            // the migrated fields (Flash UI, GazeFocusService, etc.).
            try
            {
                if (Settings.Current != null)
                {
                    Settings.Current.RunFlashClickableDecouplingMigration();
                    Settings.Save();
                }
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Settings migration failed (non-fatal, defaults apply)");
            }

            // Migrate assets from old location (install dir) to new location (user data) in background.
            // MUST run after Settings is initialized: the migration both READS the "already migrated"
            // guard and WRITES the completion flag, and if Settings.Current is still null (as it was
            // when this fired at the top of OnStartup, before `Settings = new SettingsService()`), the
            // flag never persists and the whole library gets re-copied to the system drive every launch.
            _ = Task.Run(MigrateAssetsToUserFolder);

            // Restore UnifiedUserId from settings (persisted from previous session)
            if (!string.IsNullOrEmpty(Settings?.Current?.UnifiedId))
            {
                UnifiedUserId = Settings.Current.UnifiedId;
                // Persist a snapshot for the re-login same-account check. ValidateRestoredSessionAsync
                // (below) may null UnifiedUserId + settings.UnifiedId on a token/session failure, which
                // would otherwise make a later re-login of THIS SAME account look brand new and wipe
                // local progression back to level 1.
                StartupUnifiedId = UnifiedUserId;
                Logger?.Information("Restored UnifiedUserId from settings: {Id}", UnifiedUserId);
            }

            // Check if installer set an assets path in registry
            ApplyInstallerAssetsPath();

            // Ensure the custom assets folder + standard subdirs exist. Without this a
            // configured CustomAssetsPath whose folder is missing makes EffectiveAssetsPath
            // silently fall back to the default AppData location, so pack extraction and
            // drag-drop imports land in the wrong place (#391).
            EnsureCustomAssetsDirectories();

            // Clean up stale temp files from previous sessions (crash recovery, leaked files)
            CleanupStaleTempFiles();

            // Initialize localization (must be after settings, before UI)
            LocalizationManager.Instance.Initialize(Settings?.Current?.Language ?? "en");

            // Initialize mod system (must be after settings, before services that use content config)
            Mods = new ModService();
            Mods.Initialize(Settings?.Current?.ActiveModId);

            // Mod-coded title bars: tint every window's OS caption with the active mod accent.
            // One class handler covers all windows (current + future) with no per-window code;
            // chromeless/transparent windows are unaffected (DWM caption attrs no-op there).
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => Services.WindowChromeHelper.ApplyDarkTitleBar((Window)s)));
            // Recolor the Season Recap card palette from the active mod.
            Services.RecapTheme.ApplyForActiveMod();
            // On mod switch: re-tint open window title bars and re-skin the recap palette (UI thread).
            Mods.ModChanged += (_, __) =>
            {
                void Recolor()
                {
                    Services.RecapTheme.ApplyForActiveMod();
                    foreach (Window w in Current.Windows)
                        Services.WindowChromeHelper.ApplyDarkTitleBar(w);
                }
                if (Current?.Dispatcher?.CheckAccess() == true) Recolor();
                else Current?.Dispatcher?.Invoke(Recolor);
            };

            splash.SetProgress(0.3, "Initializing audio...");
            Audio = new AudioService();
            Audio.RunStartupDiagnostics();

            splash.SetProgress(0.4, "Initializing flash service...");
            Flash = new FlashService();

            splash.SetProgress(0.5, "Initializing video service...");
            Video = new VideoService();
            Video.PreloadLibVLC(); // Pre-load LibVLC in background for faster first video

            // Session media log - must be after Flash and Video so it can subscribe to their events.
            SessionLog = new SessionLogService();

            splash.SetProgress(0.6, "Initializing effects...");
            Progression = new ProgressionService();
            ActivityTracker = new ActivityTracker();

            // Initialize companion leveling system (v5.3) - migrate existing users if needed
            CompanionService.MigrateFromLegacy(Settings.Current);
            Companion = new CompanionService();
            CommunityPrompts = new CommunityPromptService();

            // Initialize personality preset system (v5.5) - migrate from legacy SlutModeEnabled
            Personality = new PersonalityService();
            Personality.MigrateFromLegacy(Settings.Current);

            Subliminal = new SubliminalService();
            Overlay = new OverlayService();
            ScreenShake = new ScreenShakeService();
            Bubbles = new BubbleService();
            Services.Chaos.ChaosMeta.Init();   // load persistent Chaos meta-progression before the run service
            Chaos = new Services.Chaos.ChaosModeService();
            InteractionQueue = new InteractionQueueService();
            LockCard = new LockCardService();
            PopQuiz = new PopQuizService();
            BubbleCount = new BubbleCountService();
            BouncingText = new BouncingTextService();
            MindWipe = new MindWipeService();
            BrainDrain = new BrainDrainService();

            splash.SetProgress(0.75, "Loading achievements...");
            Achievements = new AchievementService();
            // Single seam between feature events and achievement tracking. Constructed
            // here; Start() is called later in OnStartup once feature services exist.
            Gamification = new GamificationBridge();
            // Reactive companion-dialogue ("bark") seam. Like GamificationBridge it is
            // constructed here and Start()ed later once feature services exist.
            Bark = new BarkService();
            QuestDefinitions = new QuestDefinitionService();
            _ = QuestDefinitions.InitializeAsync(); // Fire and forget - will load from cache first
            Quests = new QuestService();
            QuestDefinitions.QuestDefinitionsUpdated += () =>
            {
                // When server definitions change, re-check quests (regenerates if definition was removed)
                Quests?.CheckAndGenerateQuests();
            };
            Roadmap = new RoadmapService();
            SkillTree = new SkillTreeService();
            Tutorial = new TutorialService();

            // Award daily streak bonus now that SkillTree is available
            // (AchievementService runs UpdateDailyStreak in its constructor before SkillTree exists)
            Achievements?.Progress?.AwardDeferredStreakBonus();

            splash.SetProgress(0.85, "Initializing companion...");
            // Moderation guard + log: substantive content moderation that runs in C# code
            // OUTSIDE the LLM prompt. User-editable prompt sections (Personality,
            // SlutModePersonality, CompanionPrompt, custom Awareness templates, etc.) cannot
            // bypass these — the wordlist is hardcoded in ModerationGuard and applies to
            // every input that goes to an LLM and every output that comes back. See
            // AI_AUDIT.md §15 and §13 P1 for the CCBill rationale. Must be initialized
            // BEFORE the AI services so AiService / LocalAiService can read App.ModerationGuard.
            ModerationSession = new Services.Moderation.ModerationSession();
            ModerationLog = new Services.Moderation.ModerationLog(ModerationSession);
            ModerationGuard = new Services.Moderation.ModerationGuard();
            // PromptValidator (P1.3) is a soft validator that runs on the prompt-editor
            // surfaces (CompanionPromptEditorDialog, AwarenessPresetDetailDialog,
            // QuizCategoryEditorWindow). Hits warn the user and log to moderation.log;
            // they do NOT block save. ModerationGuard is the load-bearing layer.
            PromptValidator = new Services.Moderation.PromptValidator();
            // ModerationCounter (P1.4) sliding-window counter that escalates: 3 hits in
            // 10 min raises a warning modal once; 5 hits engages a 5-min chat cooldown.
            // RecordHit is called from each ModerationGuard refusal site (AiService,
            // LocalAiService, KeywordTriggerService, QuizService).
            ModerationCounter = new Services.Moderation.ModerationCounter();
            // P2-H8: hydrate counter + cooldown from disk so a restart doesn't bypass
            // an in-flight cooldown. Best-effort; logs nothing on a missing file.
            try { ModerationCounter.LoadFromDisk(); }
            catch (Exception ex) { Logger?.Debug("ModerationCounter.LoadFromDisk failed: {Error}", ex.Message); }

            Ai = new AiServiceStrategy();
            Commands = new AiCommandService();

            // If local Ollama is the active provider, kick off a background warm-up so
            // the model is hot in memory by the time the user sends their first chat.
            // No-op for cloud users; silent on failure (Ollama may not be running).
            if (Ai is AiServiceStrategy aiStrategy)
            {
                _ = Task.Run(async () => { try { await aiStrategy.WarmUpLocalAsync(); } catch { } });
            }

            WindowAwareness = new WindowAwarenessService();
            Patreon = new PatreonService();
            SubscribeStar = new SubscribeStarService();
            ProfileSync = new ProfileSyncService();
            Leaderboard = new LeaderboardService();
            Haptics = new HapticService(Settings.Current.Haptics);
            AudioSync = new AudioSyncService(Haptics, Settings.Current.Haptics.AudioSync);
            KeywordTriggers = new KeywordTriggerService();
            KeywordPresets = new KeywordTriggerPresetService();

            // Drain any preset re-installs queued by SettingsService.MergeBuiltInAwarenessPresets
            // when a built-in preset's version was bumped on this launch. This re-clones the
            // new triggers into KeywordTriggers so version bumps actually reach the live list
            // instead of only refreshing card metadata.
            if (Settings?.PendingPresetReinstalls.Count > 0)
            {
                foreach (var presetId in Settings.PendingPresetReinstalls.ToList())
                {
                    try
                    {
                        KeywordPresets.InstallPreset(presetId);
                    }
                    catch (Exception ex)
                    {
                        Logger?.Warning("Pending preset re-install failed for {Id}: {Error}", presetId, ex.Message);
                    }
                }
                Settings.PendingPresetReinstalls.Clear();
            }
            ScreenOcr = new ScreenOcrService();
            KeywordHighlight = new KeywordHighlightService();
            RemoteControl = new RemoteControlService();
            // Quest credit: each remote-control command received (Patreon-exclusive quest category).
            RemoteControl.CommandReceived += (_, _) => { try { Quests?.TrackRemoteCommand(); } catch { } };
            AvailableSubjects = new AvailableSubjectsService();
            CompanionPhrases = new CompanionPhraseService();
            Catalogue = new CatalogueService();
            CatalogueLookup = new CatalogueLookupService();

            // Auto-connect haptics if enabled (runs in background)
            if (Settings.Current.Haptics.AutoConnect && Settings.Current.Haptics.Provider != HapticProviderType.Mock)
            {
                _ = AutoConnectHapticsAsync();
            }

            // Initialize Discord Rich Presence (only if Discord is linked — prevents
            // accidental exposure for users who chose anonymous invite-code accounts)
            DiscordRpc = new DiscordRichPresenceService();
            if (Settings.Current.DiscordRichPresenceEnabled && Settings.Current.HasLinkedDiscord)
            {
                DiscordRpc.IsEnabled = true;
            }

            // Initialize Discord OAuth service
            Discord = new DiscordService();

            // Initialize dual monitor video service for Hypnotube playback
            DualMonitorVideo = new DualMonitorVideoService();
            ScreenMirror = new ScreenMirrorService();

            // Initialize autonomy service (companion autonomous behavior - Level 100+)
            Autonomy = new AutonomyService();

            // Initialize offline speech recognition (Takeover "repeat after me").
            // Constructor is a no-op; the mic only opens during an explicit listen window, and the
            // service reports IsAvailable=false (no model on disk / no capture device) instead of throwing.
            Speech = new Services.Speech.SpeechService();

            // Initialize the sherpa-onnx wake-word spotter ("Hey Bambi"). No-op ctor; reports
            // IsAvailable=false until the KWS model is dropped into Resources\Models\sherpa-kws\,
            // in which case the wake loop prefers it over the Vosk free-recognizer path. No API key.
            WakeWord = new Services.Speech.SherpaWakeService();

            // Initialize content packs service
            ContentPacks = new ContentPackService();

            // Initialize webcam tracking + focus game services (Lab — gated by consent dialog).
            // Constructors are no-ops; the camera handle only opens after explicit user consent.
            Webcam = new WebcamTrackingService();
            FocusGame = new FocusGameService();
            GazeCursor = new GazeDebugCursorService();
            GazeFocus = new GazeFocusService();
            // Click-driven implicit recal — installs its mouse hook only while
            // tracking runs with a calibration loaded (and the setting is on).
            GazeDrift = new GazeDriftCorrectionService();
            BlinkTrainer = new BlinkTrainerService();

            // In-app non-blocking notifications. Host attachment is deferred to
            // MainWindow.Loaded — calls before then enqueue and replay once
            // attached.
            Notifications = new NotificationService();

            // Phase 4 Attention-Check mechanic: scrapped pre-ship per design call.
            // Service is still constructed so the AttentionCheckSettingsDialog
            // and AttentionCheckFeatureControl files compile (they hold the
            // mechanic's design intact for future revival), but it's never
            // Start()'d and the default for AttentionCheckEnabled is false.
            // To revive in a later release, restore the OnPass/OnFail handler
            // wiring, the PropertyChanged subscription, the Start() call, and
            // the no-webcam sticky — all of which were here at HEAD before
            // this commit. The Lab UI surface and intro sticky also need to
            // come back; see MainWindow for those touchpoints.
            AttentionCheck = new AttentionCheckService();

            // Deeper enhancement library — file ops, recent files, library scan.
            // Eager-init: lightweight, just creates the folder and reads recent files
            // from settings.
            EnhancementLibrary = new Services.Deeper.EnhancementLibrary();

            // Deeper end-user runtime: long-form audio player + host orchestrator
            // (Phase 8). Both are cheap to construct; resources only open on
            // first Play / first Bind.
            DeeperPlayer = new Services.Deeper.EnhancementAudioPlayer();
            DeeperHost = new Services.Deeper.EnhancementHostService();

            // Phase 9: HT description auto-discovery. Fetcher caches in-memory
            // per session; browser discovery wires onto the WebView2 once
            // MainWindow creates the browser.
            DeeperFetcher = new Services.Deeper.EnhancementFetcher();
            DeeperBrowserDiscovery = new Services.Deeper.BrowserAutoDiscovery(DeeperFetcher, DeeperHost);

            // Mandatory + asset-folder video enhancement runtime. Subscribes to
            // VideoService start/end and binds the engine to the primary player
            // when the played file has a matching .ccpenh.json. Owns its own host
            // (no conflict with the Deeper player on DeeperHost). No-ops unless
            // AppSettings.VideoEnhanceIfPossible is on (default off).
            VideoEnhanceBridge = new Services.Deeper.VideoEnhancementBridge(Video);

            // Initialize lockdown service (ephemeral — not persisted). Recover from a
            // prior run that was killed mid-lockdown so the panic key isn't stuck off.
            LockdownService.RecoverIfNeeded();
            Lockdown = new LockdownService();
            // Quest credit: each completed lockdown (Patreon-exclusive quest category).
            Lockdown.LockdownDeactivated += () => { try { Quests?.TrackLockdownCompleted(); } catch { } };

            // Initialize mantra lab service
            Mantra = new MantraService();

            // Spoken Mantras (Takeover voice mechanic) — loads per-mod mantras.json on demand.
            MantraVoice = new MantraVoiceService();

            // Initialize wallpaper override service
            Wallpaper = new WallpaperService();

            // Initialize Patreon (validate subscription in background)
            // Then load cloud profile if authenticated
            _ = InitializePatreonAndSyncAsync();

            // Initialize SubscribeStar (validate subscription in background). Shares
            // the unified account + premium gate with Patreon (see PatreonService gate).
            _ = SubscribeStar.InitializeAsync();

            // Initialize Discord OAuth (validate session in background)
            _ = InitializeDiscordAsync();

            // Validate restored session (if we have a cached UnifiedUserId but no provider authenticated yet)
            _ = ValidateRestoredSessionAsync();

            // Check if this is a fresh install and offer cloud settings restore
            _ = CheckCloudSettingsRestoreAsync();

            // Initialize Update service and check for updates in background
            Update = new UpdateService();
            _ = CheckForUpdatesInBackgroundAsync();

            // Initialize bug report service (stateless, just holds an HttpClient)
            BugReport = new BugReportService();

            // Wire up achievement popup BEFORE checking any achievements
            Achievements.AchievementUnlocked += OnAchievementUnlocked;
            
            // Now check initial achievements (so popup can show)
            Achievements.CheckLevelAchievements(Settings.Current.PlayerLevel);
            Logger.Information("Checked level achievements for level {Level}", Settings.Current.PlayerLevel);
            
            // Check daily maintenance achievement (7 days streak)
            Achievements.CheckDailyMaintenance();
            Logger.Information("Checked daily maintenance achievement");

            // Start the gamification bridge now that all feature services it subscribes
            // to (Mods, Companion, KeywordTriggers, RemoteControl, Webcam, BlinkTrainer,
            // Lockdown) have been constructed above.
            Gamification?.Start();

            // Start the bark system (loads rule manifests, wires its own direct event
            // subscriptions). SessionEngine/TrayIcon are attached later by MainWindow.
            Bark?.Start();

            // Update quest streak tracking
            Quests?.TrackStreak(Achievements.Progress.ConsecutiveDays);

            Logger.Information("Services initialized");

            splash.SetProgress(0.95, "Opening main window...");

            // Show main window — wrapped in try-catch to ensure splash closes on failure
            MainWindow mainWindow;
            try
            {
                mainWindow = new MainWindow();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to create main window");
                try { splash.Close(); } catch { }
                _splash = null;
                throw; // Re-throw to let DispatcherUnhandledException show the error
            }

            // Give RemoteControlService a direct reference (Application.Current.MainWindow is null when hidden to tray)
            if (RemoteControl != null) RemoteControl.MainWindowRef = mainWindow;
            // Same problem hits anywhere code does `Application.Current.MainWindow as MainWindow`
            // — popups, feature controls, etc. Expose a stable static reference.
            MainWindowRef = mainWindow;

            // Arm the offline mic features (wake word / push-to-talk) at startup if the user left them
            // on. They're decoupled from Takeover ("She's Listening" owns them), so they no longer wait
            // for Takeover to start. No-op unless consent is given and the speech engine is available.
            //
            // LAZY/DEFERRED: the offline speech models are heavy to load (Vosk small ~0.5s on the UI
            // thread; the sherpa-onnx KWS transducer trio several seconds), and arming inline here made
            // the very first IsAvailable query load Vosk ON the startup UI thread. Defer the whole arm-up
            // to ApplicationIdle (after the window has rendered and is interactive), and warm BOTH models
            // on a background thread first — so neither load ever blocks startup. RefreshVoiceInputModes
            // then runs back on the UI thread (it touches the LL keyboard hook / message pump) with the
            // models already warm, so it returns instantly.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Task.Run(() =>
                {
                    try { _ = Speech?.IsAvailable; } catch { }                                   // warm Vosk off-UI
                    try { if (Settings?.Current?.SpeechWakeWordEnabled == true) _ = WakeWord?.IsAvailable; } catch { } // warm KWS off-UI
                }).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { Autonomy?.RefreshVoiceInputModes(); }
                        catch (Exception ex) { Logger?.Warning(ex, "Deferred RefreshVoiceInputModes failed"); }
                    }));
                });
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // First-instance "Open with CCP" dispatch: replay parsed --play/--edit
            // args once MainWindow is fully loaded so the player/editor windows
            // can use it as their Owner.
            if (_pendingFileOpenAction != null && _pendingFileOpenPath != null)
            {
                var action = _pendingFileOpenAction;
                var path = _pendingFileOpenPath;
                _pendingFileOpenAction = null;
                _pendingFileOpenPath = null;
                // Window was Show()n above; if its Loaded already fired, hooking it now
                // would never run — dispatch immediately in that case, else wait for load.
                Action dispatch = () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { mainWindow.HandlePendingFileOpen(action, path); }
                    catch (Exception ex) { Logger?.Warning(ex, "HandlePendingFileOpen failed"); }
                }), System.Windows.Threading.DispatcherPriority.Background);
                if (mainWindow.IsLoaded) dispatch();
                else mainWindow.Loaded += (_, _) => dispatch();
            }

            // Close splash screen with fade animation
            // Drop Topmost FIRST so deferred dialogs (What's New, Age Verification) aren't hidden behind it
            splash.Topmost = false;
            splash.SetProgress(1.0, "Ready!");

            // Activate the main window before AND after the splash fades. Show()
            // alone doesn't reliably foreground the window because the splash
            // was Topmost during init and Windows can give focus to whatever
            // was foreground before launch (Explorer, prior app) when the
            // splash closes. Topmost-pulse is the standard WPF workaround for
            // ForegroundLockTimeout blocking Activate().
            ForceWindowToFront(mainWindow);
            splash.Closed += (_, _) => ForceWindowToFront(mainWindow);

            splash.FadeOutAndClose();
            _splash = null;

            // Age verification gate (first launch only, deferred to ensure splash is fully closed)
            if (Settings?.Current?.HasAcceptedAgeVerification != true)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var result = MessageBox.Show(mainWindow,
                        "This application contains adult content intended for users aged 18 and older.\n\n" +
                        "By clicking \"Yes\", you confirm that you are at least 18 years old and that viewing adult content is legal in your jurisdiction.\n\n" +
                        "Do you wish to continue?",
                        "Age Verification",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);

                    if (result != MessageBoxResult.Yes)
                    {
                        Shutdown();
                        return;
                    }

                    Settings.Current.HasAcceptedAgeVerification = true;
                    Settings.Save();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        // Terminate any OTHER running CCP process that shares our executable path. Called from the
        // single-instance handshake only after the existing instance failed to acknowledge within
        // ShowAckTimeoutMs — i.e. it is wedged (render-thread deadlock) or headless and is keeping
        // the single-instance mutex alive. Matching on the full exe path avoids nuking an unrelated
        // same-named process or a separate install; ProcessName is a best-effort fallback when the
        // other process's MainModule can't be read (access denied). Runs before Logger init.
        private static void KillStaleInstances()
        {
            try
            {
                int selfId = Environment.ProcessId;
                string? selfPath = null;
                try { selfPath = Process.GetCurrentProcess().MainModule?.FileName; } catch { }
                string selfName = Process.GetCurrentProcess().ProcessName;

                foreach (var proc in Process.GetProcessesByName(selfName))
                {
                    try
                    {
                        if (proc.Id == selfId) continue;

                        // Prefer an exact executable-path match; fall back to name-only if the
                        // path is unreadable (e.g. the target is elevated).
                        bool pathMatches = true;
                        if (selfPath != null)
                        {
                            string? otherPath = null;
                            try { otherPath = proc.MainModule?.FileName; } catch { otherPath = null; }
                            if (otherPath != null)
                                pathMatches = string.Equals(otherPath, selfPath, StringComparison.OrdinalIgnoreCase);
                        }
                        if (!pathMatches) continue;

                        proc.Kill();
                        proc.WaitForExit(5000);
                    }
                    catch { /* process may have exited on its own, or we lack rights — skip it */ }
                    finally { try { proc.Dispose(); } catch { } }
                }
            }
            catch { /* enumeration failed — takeover still proceeds; the mutex re-acquire is best-effort */ }
        }

        // Standard WPF "bring to front" sequence. Activate() alone is silently
        // ignored when Windows' ForegroundLockTimeout is active (e.g. another
        // app was foregrounded recently). Pulsing Topmost true→false is the
        // documented workaround — it bypasses the lock without leaving the
        // window stuck on top.
        private static void ForceWindowToFront(Window window)
        {
            try
            {
                if (window == null) return;
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;
                window.Activate();
                bool wasTopmost = window.Topmost;
                window.Topmost = true;
                window.Topmost = wasTopmost;
                window.Focus();

                // Topmost-pulse on main moves it to the top of the regular
                // z-band, which can leave the avatar tube buried behind it
                // (tube was Show()'n above main but the pulse rearranges).
                // Raise the tube too so the attached pair stays paired.
                AvatarWindow?.RaiseAttachedTubeAboveOwner();
            }
            catch (Exception ex) { Logger?.Debug("ForceWindowToFront failed: {Error}", ex.Message); }
        }

        private void OnAchievementUnlocked(object? sender, Models.Achievement achievement)
        {
            Logger.Information("OnAchievementUnlocked handler called for: {Name}", achievement.Name);

            // Show achievement popup
            try
            {
                var popup = new AchievementPopup(achievement);
                popup.Show();
                Logger.Information("Achievement popup shown for: {Name}", achievement.Name);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to show achievement popup for: {Name}", achievement.Name);
            }

            // Play achievement sound
            PlayAchievementSound();

            // Send Discord webhook if enabled (fire and forget)
            // Always use CustomDisplayName for privacy - never expose real Discord/Patreon names
            if (Settings?.Current?.DiscordShareAchievements == true)
            {
                var displayName = Discord?.CustomDisplayName ?? Patreon?.DisplayName ?? "Someone";
                _ = Discord?.SendAchievementWebhookAsync(achievement, displayName);
            }
        }
        
        /// <summary>
        /// Initialize Patreon and load cloud profile if authenticated
        /// </summary>
        private async Task InitializePatreonAndSyncAsync()
        {
            try
            {
                // Initialize Patreon authentication
                await Patreon.InitializeAsync();

                // If authenticated, load cloud profile and start heartbeat
                if (Patreon.IsAuthenticated)
                {
                    // Auto-upgrade: if Patreon is authenticated but no V2 identity, migrate via /v2/auth/patreon
                    if (string.IsNullOrEmpty(UnifiedUserId) || string.IsNullOrEmpty(Settings?.Current?.AuthToken))
                    {
                        try
                        {
                            var accessToken = Patreon.GetAccessToken();
                            if (!string.IsNullOrEmpty(accessToken))
                            {
                                Logger?.Information("Auto-upgrading Patreon user to V2...");
                                var v2Auth = new V2AuthService();
                                var result = await v2Auth.AuthenticateWithPatreonAsync(accessToken);
                                if (result.Success && result.User != null)
                                {
                                    v2Auth.ApplyUserDataToSettings(result.User, result.AuthToken);
                                    UnifiedUserId = result.User.UnifiedId;
                                    Logger?.Information("Auto-upgrade complete: {Id}", UnifiedUserId);
                                }
                                else if (result.NeedsRegistration)
                                {
                                    Logger?.Information("Patreon auto-upgrade: needs registration (new user), skipping");
                                }
                                else
                                {
                                    Logger?.Warning("Patreon auto-upgrade failed: {Error}", result.Error);
                                }
                            }
                        }
                        catch (Exception upgradeEx)
                        {
                            Logger?.Warning(upgradeEx, "Patreon auto-upgrade failed (non-fatal, will retry next launch)");
                        }
                    }

                    Logger?.Information("Patreon authenticated, loading cloud profile...");
                    await ProfileSync.LoadProfileAsync();
                    ProfileSync.StartHeartbeat();
                }

                // Re-arm Takeover on launch ONLY if the user opted in (AutonomyResumeOnStartup).
                // Takeover now always starts OFF by default — this fixes "it stays on after a restart".
                // The enabled+consent flags persist so the toggle remembers its label, but the service
                // does not auto-run unless resume-on-startup is explicitly turned on.
                var s = Settings?.Current;
                if (s != null && s.AutonomyResumeOnStartup && s.AutonomyModeEnabled && s.AutonomyConsentGiven)
                {
                    var hasPatreonAccess = s.PatreonTier >= 1 || Patreon?.IsWhitelisted == true;
                    if (hasPatreonAccess && Autonomy?.IsEnabled != true)
                    {
                        Autonomy?.Start();
                        Logger?.Information("Re-armed Takeover on startup (AutonomyResumeOnStartup opt-in)");
                    }
                }
                else if (s != null && s.AutonomyModeEnabled && !s.AutonomyResumeOnStartup)
                {
                    // Clear the stale "enabled" flag so the UI shows OFF on a fresh launch and a
                    // mid-pulse Stop() from a previous run can't leave anything armed.
                    s.AutonomyModeEnabled = false;
                    Logger?.Information("Takeover left OFF on startup (resume-on-startup not opted in)");
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to initialize Patreon and sync profile");
            }
            finally
            {
                _patreonInitDone.TrySetResult();
            }
        }

        /// <summary>
        /// Initialize Discord OAuth and validate session
        /// </summary>
        private async Task InitializeDiscordAsync()
        {
            try
            {
                await Discord.InitializeAsync();

                if (Discord.IsAuthenticated)
                {
                    Logger?.Information("Discord authenticated: {Id}", Discord.UserId);

                    // Auto-upgrade: if Discord is authenticated but no V2 identity OR no auth token, migrate via /v2/auth/discord
                    // (legacy users created before Feb 2026 may have a UnifiedUserId but no auth_token_hash on the server —
                    // re-running /v2/auth/discord bootstraps a fresh token for them)
                    if (string.IsNullOrEmpty(UnifiedUserId) || string.IsNullOrEmpty(Settings?.Current?.AuthToken))
                    {
                        try
                        {
                            var accessToken = Discord.GetAccessToken();
                            if (!string.IsNullOrEmpty(accessToken))
                            {
                                Logger?.Information("Auto-upgrading Discord user to V2...");
                                var v2Auth = new V2AuthService();
                                var result = await v2Auth.AuthenticateWithDiscordAsync(accessToken);
                                if (result.Success && result.User != null)
                                {
                                    v2Auth.ApplyUserDataToSettings(result.User, result.AuthToken);
                                    UnifiedUserId = result.User.UnifiedId;
                                    Logger?.Information("Auto-upgrade complete: {Id}", UnifiedUserId);
                                }
                                else if (result.NeedsRegistration)
                                {
                                    Logger?.Information("Discord auto-upgrade: needs registration (new user), skipping");
                                }
                                else
                                {
                                    Logger?.Warning("Discord auto-upgrade failed: {Error}", result.Error);
                                }
                            }
                        }
                        catch (Exception upgradeEx)
                        {
                            Logger?.Warning(upgradeEx, "Discord auto-upgrade failed (non-fatal, will retry next launch)");
                        }
                    }

                    // Wait for Patreon init to finish (up to 10s) before deciding whether to load profile
                    // This prevents a race where Discord init finishes first and calls LoadProfileAsync
                    // while Patreon is still initializing — causing duplicate profile loads
                    await Task.WhenAny(_patreonInitDone.Task, Task.Delay(10_000));

                    // If not already syncing via Patreon, load cloud profile and start heartbeat for Discord-only users
                    if (Patreon?.IsAuthenticated != true && ProfileSync != null)
                    {
                        Logger?.Information("Discord-only user, loading cloud profile...");
                        await ProfileSync.LoadProfileAsync();
                        ProfileSync.StartHeartbeat();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to initialize Discord");
            }
        }

        /// <summary>
        /// Validates a restored UnifiedUserId against the server.
        /// If no provider has authenticated, calls /v2/auth/restore-session to confirm the ID is still valid.
        /// On 404, clears the cached ID. On network error, keeps cached state (offline-tolerant).
        /// </summary>
        private async Task ValidateRestoredSessionAsync()
        {
            try
            {
                // No cached ID — nothing to validate
                if (string.IsNullOrEmpty(UnifiedUserId)) return;

                // Wait a bit for provider auth to complete
                await Task.Delay(3000);

                // If a provider already authenticated, they validated the session — skip
                if (Patreon?.IsAuthenticated == true || Discord?.IsAuthenticated == true) return;

                // If offline mode, trust the cache
                if (Settings?.Current?.OfflineMode == true) return;

                Logger?.Information("Validating restored session for {Id}...", UnifiedUserId);

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var storedToken = Settings?.Current?.AuthToken;
                if (!string.IsNullOrEmpty(storedToken))
                    http.DefaultRequestHeaders.Add("X-Auth-Token", storedToken);
                var body = new Newtonsoft.Json.Linq.JObject
                {
                    ["unified_id"] = UnifiedUserId,
                    ["client_version"] = UpdateService.AppVersion
                };
                var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var response = await http.PostAsync("https://codebambi-proxy.vercel.app/v2/auth/restore-session", content);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Logger?.Warning("Restored session invalid (user not found on server). Clearing UnifiedUserId.");
                    UnifiedUserId = null;
                    if (Settings?.Current != null)
                    {
                        Settings.Current.UnifiedId = null;
                        Settings.Save();
                    }
                }
                else if (response.IsSuccessStatusCode)
                {
                    // Parse and store auth token from restore-session response
                    try
                    {
                        var responseJson = await response.Content.ReadAsStringAsync();
                        var responseObj = Newtonsoft.Json.Linq.JObject.Parse(responseJson);
                        var authToken = responseObj["auth_token"]?.ToString();
                        if (!string.IsNullOrEmpty(authToken) && Settings?.Current != null)
                        {
                            Settings.Current.AuthToken = authToken;
                            Settings.Save();
                            Logger?.Information("Stored auth token from restore-session.");
                        }
                    }
                    catch (Exception parseEx)
                    {
                        Logger?.Debug("Failed to parse restore-session auth token: {Error}", parseEx.Message);
                    }
                    Logger?.Information("Restored session validated successfully.");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Check if this is a legacy user that needs full re-auth
                    var errorJson = await response.Content.ReadAsStringAsync();
                    var isLegacyReauth = errorJson.Contains("legacy_user_reauth_required");

                    if (isLegacyReauth)
                    {
                        Logger?.Warning("Restored session rejected (legacy user, no token ever issued). Clearing all auth state — user must re-login via OAuth.");
                        UnifiedUserId = null;
                        if (Settings?.Current != null)
                        {
                            Settings.Current.UnifiedId = null;
                            Settings.Current.AuthToken = null;
                            Settings.Save(suppressCloudBackup: true);
                        }
                    }
                    else
                    {
                        Logger?.Warning("Restored session rejected (invalid token). Clearing auth token.");
                        if (Settings?.Current != null)
                        {
                            Settings.Current.AuthToken = null;
                            Settings.Save(suppressCloudBackup: true);
                        }
                    }
                }
                else
                {
                    Logger?.Warning("Session validation returned {Status} — keeping cached state.", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                // Network error — keep cached state (offline-tolerant)
                Logger?.Warning(ex, "Session validation failed (network error) — keeping cached state.");
            }
        }

        /// <summary>
        /// On fresh install, check if a cloud settings backup exists and offer to restore it.
        /// Waits for authentication to complete before checking.
        /// </summary>
        private async Task CheckCloudSettingsRestoreAsync()
        {
            try
            {
                // Only run on fresh installs (no settings file existed)
                if (Settings?.WasSettingsFileMissing != true) return;

                // Wait for provider auth to complete
                await Task.Delay(5000);

                // Need a cloud identity to check for backup
                if (!HasCloudIdentity) return;
                if (ProfileSync == null) return;

                Logger?.Information("Fresh install detected with cloud identity — checking for settings backup...");

                var backupInfo = await ProfileSync.GetSettingsBackupInfoAsync();
                if (backupInfo == null)
                {
                    Logger?.Information("No cloud settings backup found");
                    return;
                }

                Logger?.Information("Cloud settings backup found (v{Version}, {Date})",
                    backupInfo.AppVersion, backupInfo.BackedUpAt);

                // Ask user on UI thread
                await Current.Dispatcher.InvokeAsync(async () =>
                {
                    var dateStr = backupInfo.BackedUpAt?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "unknown date";
                    var result = System.Windows.MessageBox.Show(
                        $"A cloud backup of your settings was found!\n\n" +
                        $"Backed up: {dateStr}\n" +
                        $"App version: {backupInfo.AppVersion}\n\n" +
                        $"Would you like to restore your settings from this backup?",
                        "Restore Settings from Cloud",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (result != System.Windows.MessageBoxResult.Yes) return;

                    var restored = await ProfileSync.RestoreSettingsFromCloudAsync();
                    if (restored == null)
                    {
                        System.Windows.MessageBox.Show(
                            "Failed to restore settings from cloud.",
                            "Restore Failed",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        return;
                    }

                    ApplyRestoredSettings(restored);

                    System.Windows.MessageBox.Show(
                        "Settings restored from cloud! Some UI changes may require a restart to take full effect.",
                        "Settings Restored",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Cloud settings restore check failed");
            }
        }

        /// <summary>
        /// Apply restored settings while preserving identity and progression fields
        /// (those are server-authoritative and should not be overwritten from backup).
        /// </summary>
        private void ApplyRestoredSettings(Models.AppSettings restored)
        {
            var current = Settings?.Current;
            if (current == null || Settings == null) return;

            // Preserve identity/progression fields from current settings
            restored.UnifiedId = current.UnifiedId;
            restored.PlayerLevel = current.PlayerLevel;
            restored.PlayerXP = current.PlayerXP;
            restored.SkillPoints = current.SkillPoints;
            restored.UnlockedSkills = current.UnlockedSkills;
            restored.HighestLevelEver = current.HighestLevelEver;
            restored.IsSeason0Og = current.IsSeason0Og;
            restored.CurrentSeason = current.CurrentSeason;
            restored.PendingSkillsResetAck = current.PendingSkillsResetAck;
            restored.UserDisplayName = current.UserDisplayName;
            restored.PatreonTier = current.PatreonTier;
            restored.PatreonPremiumValidUntil = current.PatreonPremiumValidUntil;
            restored.LastPatreonVerification = current.LastPatreonVerification;
            restored.OpenRouterApiKey = current.OpenRouterApiKey;

            // Preserve lifetime stats — take higher value (current may have server-synced data)
            restored.TotalConditioningMinutes = Math.Max(current.TotalConditioningMinutes, restored.TotalConditioningMinutes);

            // Preserve companion progress — per-companion, take higher level
            foreach (var (id, currentProgress) in current.CompanionProgressData)
            {
                if (restored.CompanionProgressData.TryGetValue(id, out var restoredProgress))
                {
                    if (currentProgress.Level > restoredProgress.Level ||
                        (currentProgress.Level == restoredProgress.Level && currentProgress.TotalXPEarned > restoredProgress.TotalXPEarned))
                    {
                        restored.CompanionProgressData[id] = currentProgress;
                    }
                }
                else
                {
                    restored.CompanionProgressData[id] = currentProgress;
                }
            }

            Settings.RestoreFrom(restored);

            // Refresh UI if MainWindow is loaded
            if (MainWindow is MainWindow mw)
            {
                mw.ApplySessionSettings();
            }

            Logger?.Information("Applied restored cloud settings (identity/progression fields preserved)");
        }

        /// <summary>
        /// Check for updates in the background after a short delay
        /// </summary>
        private async Task CheckForUpdatesInBackgroundAsync()
        {
            try
            {
                // Brief delay to let app load before checking updates
                await Task.Delay(500);

                Logger?.Information("Background update check starting...");
                var updateInfo = await Update.CheckForUpdatesAsync();
                Logger?.Information("Background update check completed, IsNewer={IsNewer}", updateInfo?.IsNewer);

                if (updateInfo?.IsNewer == true)
                {
                    // First, show the update button immediately (this always works)
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var mainWindow = Application.Current.MainWindow as MainWindow;
                            if (mainWindow != null)
                            {
                                var btn = mainWindow.FindName("BtnUpdateAvailable") as System.Windows.Controls.Button;
                                if (btn != null)
                                {
                                    btn.Tag = "UpdateAvailable";
                                    btn.Content = "UPDATE";
                                    btn.ToolTip = "Update Available - Click to install!";
                                    Logger?.Information("Update button configured successfully");
                                }
                            }
                        }
                        catch (Exception btnEx)
                        {
                            Logger?.Warning(btnEx, "Failed to configure update button");
                        }
                    });

                    // Wait for any startup dialogs (What's New) to be dismissed
                    // Check every 500ms for up to 30 seconds
                    Logger?.Information("Waiting for startup dialogs to close before showing update popup...");
                    for (int i = 0; i < 60; i++)
                    {
                        if (!ConditioningControlPanel.MainWindow.IsStartupDialogShowing)
                        {
                            Logger?.Information("No startup dialog showing, proceeding with update popup");
                            break;
                        }
                        Logger?.Information("Startup dialog still showing, waiting... ({Attempt}/60)", i + 1);
                        await Task.Delay(500);
                    }

                    // Additional small delay after dialog closes to let UI settle
                    await Task.Delay(500);

                    // Now show the update dialog on UI thread
                    Logger?.Information("Attempting to show update dialog on UI thread...");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Double-check no modal dialog is showing
                            if (ConditioningControlPanel.MainWindow.IsStartupDialogShowing)
                            {
                                Logger?.Warning("Startup dialog still showing after wait, skipping auto-popup");
                                return;
                            }

                            Logger?.Information("Inside Dispatcher.Invoke - getting MainWindow");
                            var mainWindow = Application.Current.MainWindow as MainWindow;

                            if (mainWindow == null)
                            {
                                Logger?.Warning("MainWindow is null, cannot show update dialog");
                                return;
                            }

                            Logger?.Information("MainWindow found, IsLoaded={IsLoaded}, IsVisible={IsVisible}",
                                mainWindow.IsLoaded, mainWindow.IsVisible);

                            // Show the update notification dialog
                            Logger?.Information("Calling ShowUpdateNotification...");
                            ShowUpdateNotification(updateInfo, mainWindow);
                            Logger?.Information("ShowUpdateNotification returned");
                        }
                        catch (Exception innerEx)
                        {
                            Logger?.Error(innerEx, "Exception inside Dispatcher.Invoke for update dialog");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Background update check failed");
                // Silently fail - don't disrupt user
            }
        }

        /// <summary>
        /// Auto-connect to haptics device on startup if enabled
        /// </summary>
        private async Task AutoConnectHapticsAsync()
        {
            try
            {
                // Short delay to let app fully initialize
                await Task.Delay(2000);

                Logger?.Information("Auto-connecting haptics: Provider={Provider}", Settings.Current.Haptics.Provider);

                var connected = await Haptics.ConnectAsync();

                if (connected)
                {
                    Logger?.Information("Haptics auto-connected successfully to {Provider}", Haptics.ProviderName);
                }
                else
                {
                    Logger?.Warning("Haptics auto-connect failed for {Provider}", Settings.Current.Haptics.Provider);
                }
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Haptics auto-connect error");
                // Silently fail - user can manually connect later
            }
        }

        /// <summary>
        /// Show update notification dialog and handle user response
        /// </summary>
        private void ShowUpdateNotification(AppUpdateInfo updateInfo, Window owner)
        {
            try
            {
                Logger?.Information("Showing update notification dialog for version {Version}", updateInfo.Version);
                IsUpdateDialogActive = true;

                owner.Activate();
                owner.Focus();

                var dialog = new UpdateNotificationDialog(updateInfo)
                {
                    Owner = owner,
                    Topmost = true,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.Loaded += (s, e) =>
                {
                    dialog.Activate();
                    dialog.Focus();
                };

                var installRequested = dialog.ShowDialog() == true && dialog.InstallRequested;
                Logger?.Information("Update dialog closed, install requested: {InstallRequested}", installRequested);

                if (installRequested)
                {
                    DownloadAndRunInstallerAsync(owner);
                }
                else
                {
                    IsUpdateDialogActive = false;
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Error showing update notification dialog");
                IsUpdateDialogActive = false;
            }
        }

        /// <summary>
        /// Download the installer and run it for fresh install updates (5.1+)
        /// </summary>
        private async void DownloadAndRunInstallerAsync(Window owner)
        {
            UpdateProgressDialog? progressDialog = null;
            EventHandler<int>? progressHandler = null;

            try
            {
                Logger?.Information("Starting fresh install update - downloading installer...");

                // Hide the main window during update for cleaner experience
                var mainWindow = Current.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.Hide();
                    Logger?.Information("Main window hidden for update");
                }

                // Also hide the avatar tube window if it exists
                try
                {
                    var avatarWindow = Current.Windows.OfType<Window>().FirstOrDefault(w => w.GetType().Name == "AvatarTubeWindow");
                    avatarWindow?.Hide();
                }
                catch { }

                progressDialog = new UpdateProgressDialog();
                progressDialog.Topmost = true;
                progressDialog.Show();

                await Task.Delay(100);

                progressHandler = (s, progress) =>
                {
                    try
                    {
                        var dialog = progressDialog;
                        if (dialog == null) return;

                        dialog.Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                if (dialog.IsVisible)
                                {
                                    dialog.SetProgress(progress);
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }
                };

                Update.DownloadProgressChanged += progressHandler;

                var installerPath = await Update.DownloadInstallerAsync();

                progressDialog.Close();
                progressDialog = null;

                if (string.IsNullOrEmpty(installerPath))
                {
                    throw new InvalidOperationException("Failed to download installer");
                }

                // Check if this is an Inno Setup installation - if so, use silent update
                var isInnoSetupInstall = UpdateService.IsInstalledViaInstaller;

                if (isInnoSetupInstall)
                {
                    // Silent update for Inno Setup installations
                    var result = MessageBox.Show(
                        owner,
                        "Update downloaded successfully!\n\n" +
                        "The app will now close and update automatically.\n" +
                        "It will restart when complete.\n\n" +
                        "Continue?",
                        "Ready to Update",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Logger?.Information("Starting silent update for Inno Setup installation");
                        Update.RunInstallerSilentlyAndExit(installerPath);
                    }
                    else
                    {
                        // User cancelled - restore windows
                        RestoreHiddenWindows();
                    }
                }
                else
                {
                    // Fresh install flow - show installer UI
                    var result = MessageBox.Show(
                        owner,
                        "Installer downloaded successfully.\n\n" +
                        "The app will now close and the installer will start.\n" +
                        "Please follow the installer prompts to complete the update.\n\n" +
                        "Continue?",
                        "Ready to Install",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Update.RunInstallerAndExit(installerPath);
                    }
                    else
                    {
                        // User cancelled - restore windows
                        RestoreHiddenWindows();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to download installer for fresh install");

                try { progressDialog?.Close(); } catch { }

                // Restore the main window if update failed
                RestoreHiddenWindows();

                // Show error message - handle null owner
                if (owner != null && owner.IsLoaded)
                {
                    MessageBox.Show(
                        owner,
                        $"Failed to download installer: {ex.Message}",
                        "Update Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to download installer: {ex.Message}",
                        "Update Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                if (progressHandler != null)
                {
                    Update.DownloadProgressChanged -= progressHandler;
                }
                IsUpdateDialogActive = false;
            }
        }

        /// <summary>
        /// Restores windows that were hidden during the update process.
        /// </summary>
        private void RestoreHiddenWindows()
        {
            try
            {
                var mainWindow = Current.MainWindow;
                if (mainWindow != null && !mainWindow.IsVisible)
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                }
                var avatarWindow = Current.Windows.OfType<Window>().FirstOrDefault(w => w.GetType().Name == "AvatarTubeWindow");
                avatarWindow?.Show();
                Logger?.Information("Restored hidden windows after update cancelled/failed");
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Failed to restore hidden windows");
            }
        }

        /// <summary>
        /// Manually check for updates (called from MainWindow)
        /// </summary>
        public static async Task<bool> CheckForUpdatesManuallyAsync(Window owner)
        {
            // Prevent concurrent update checks
            if (_isCheckingForUpdates || IsUpdateDialogActive)
            {
                Logger?.Information("Update check already in progress, skipping");
                return false;
            }

            _isCheckingForUpdates = true;

            try
            {
                // Force check bypasses the 24-hour skip logic since user manually requested
                var updateInfo = await Update.CheckForUpdatesAsync(forceCheck: true);

                if (updateInfo?.IsNewer == true)
                {
                    IsUpdateDialogActive = true;

                    owner.Activate();
                    owner.Focus();

                    var dialog = new UpdateNotificationDialog(updateInfo)
                    {
                        Owner = owner,
                        Topmost = true,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    dialog.Loaded += (s, e) =>
                    {
                        dialog.Activate();
                        dialog.Focus();
                    };

                    var installRequested = dialog.ShowDialog() == true && dialog.InstallRequested;

                    if (installRequested)
                    {
                        ((App)Current).DownloadAndRunInstallerAsync(owner);
                    }
                    else
                    {
                        IsUpdateDialogActive = false;
                    }
                    return true;
                }
                else
                {
                    // Check if server banner indicated an update but our check failed
                    // This can happen with Inno Setup installations or network issues
                    var mainWindow = owner as MainWindow;
                    var serverIndicatedUpdate = mainWindow?.BtnUpdateAvailable?.Tag?.ToString() == "UrgentUpdate";

                    if (serverIndicatedUpdate)
                    {
                        // Offer to open releases page as fallback
                        Logger?.Warning("Update check returned no update, but server banner indicated update available. Offering browser fallback.");
                        var result = MessageBox.Show(
                            owner,
                            "The automatic update check couldn't find the update, but our server indicates a new version is available.\n\n" +
                            "This can happen with certain installation types. Would you like to open the releases page to download manually?\n\n" +
                            "After this update, automatic updates should work normally.",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/latest",
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger?.Error(ex, "Failed to open releases page");
                            }
                        }
                        return false;
                    }

                    // Hide the update button since we're on latest
                    mainWindow?.ShowUpdateAvailableButton(false);

                    MessageBox.Show(
                        owner,
                        $"You're running the latest version ({UpdateService.GetCurrentVersion()}).",
                        "No Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Manual update check failed");

                // Even on error, check if server indicated an update and offer browser fallback
                var mainWindow = owner as MainWindow;
                var serverIndicatedUpdate = mainWindow?.BtnUpdateAvailable?.Tag?.ToString() == "UrgentUpdate";

                if (serverIndicatedUpdate)
                {
                    var result = MessageBox.Show(
                        owner,
                        $"Update check failed: {ex.Message}\n\n" +
                        "However, our server indicates a new version is available. Would you like to open the releases page to download manually?",
                        "Update Check Failed",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/latest",
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                    return false;
                }

                MessageBox.Show(
                    owner,
                    $"Failed to check for updates: {ex.Message}",
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                _isCheckingForUpdates = false;
            }
        }

        /// <summary>
        /// Play the achievement notification sound
        /// </summary>
        private void PlayAchievementSound()
        {
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch
            {
                // Ignore if sound fails
            }
        }

        /// <summary>
        /// Rotate <c>crash.log</c> at startup so a bug report only carries crashes from the running
        /// build. crash.log is append-only (see <see cref="LogCrashDetails"/>) and the bug reporter
        /// attaches its tail, so left alone it drags months of unrelated crashes into every report.
        /// We rotate when the app version changes (each entry is version-tagged) and cap runaway
        /// growth within a single version. Best-effort: any failure is swallowed. Keeps ONE archive
        /// (<c>crash.log.prev</c>) for local post-mortems.
        /// </summary>
        private static void RotateCrashLogForVersion(string logDir)
        {
            const long MaxCrashLogBytes = 512 * 1024; // per-version runaway guard
            try
            {
                var crashLogPath = Path.Combine(logDir, "crash.log");
                if (!File.Exists(crashLogPath)) return;

                var markerPath = Path.Combine(logDir, "crash.log.version");
                string current = Services.UpdateService.AppVersion;
                string previous = "";
                try { if (File.Exists(markerPath)) previous = File.ReadAllText(markerPath).Trim(); } catch { }

                long size = 0;
                try { size = new FileInfo(crashLogPath).Length; } catch { }

                bool versionChanged = !string.Equals(previous, current, StringComparison.Ordinal);
                if (versionChanged || size > MaxCrashLogBytes)
                {
                    var archive = Path.Combine(logDir, "crash.log.prev");
                    try { if (File.Exists(archive)) File.Delete(archive); } catch { }
                    try
                    {
                        File.Move(crashLogPath, archive);
                    }
                    catch
                    {
                        // Locked/denied — truncate in place so we still drop the stale entries.
                        try { File.WriteAllText(crashLogPath, string.Empty); } catch { }
                    }
                    Logger?.Information("[CRASHLOG] rotated crash.log (versionChanged={V}, prevVer={P}, size={S}KB)",
                        versionChanged, string.IsNullOrEmpty(previous) ? "(none)" : previous, size / 1024);
                }

                try { File.WriteAllText(markerPath, current); } catch { }
            }
            catch (Exception ex)
            {
                Logger?.Debug("[CRASHLOG] rotation skipped: {Msg}", ex.Message);
            }
        }

        /// <summary>
        /// Log detailed crash information to both main log and a dedicated crash log file.
        /// This helps debug random crashes by capturing full context.
        /// </summary>
        private static void LogCrashDetails(string source, Exception? ex)
        {
            if (ex == null) return;

            try
            {
                // Log to main logger
                Logger?.Error(ex, "UNHANDLED {Source} EXCEPTION: {Message}", source, ex.Message);

                // Also write to dedicated crash log with full details
                var crashLogPath = Path.Combine(UserDataPath, "logs", "crash.log");
                var crashInfo = $@"
================================================================================
CRASH REPORT - {DateTime.Now:yyyy-MM-dd HH:mm:ss}
================================================================================
App Version: {Services.UpdateService.AppVersion}
Source: {source}
Exception Type: {ex.GetType().FullName}
Message: {ex.Message}

Stack Trace:
{ex.StackTrace}

Inner Exception: {(ex.InnerException != null ? ex.InnerException.Message : "None")}
{(ex.InnerException?.StackTrace != null ? $"Inner Stack Trace:\n{ex.InnerException.StackTrace}" : "")}

Application State:
- IsRunning: {Current != null}
- Dispatcher Shutdown: {(Current?.Dispatcher?.HasShutdownStarted ?? true)}
================================================================================
";
                File.AppendAllText(crashLogPath, crashInfo);
            }
            catch
            {
                // Can't log the crash - last resort
            }
        }

        /// <summary>
        /// Migrate user assets from old install directory location to persistent user data folder.
        /// This ensures user content survives app updates.
        /// </summary>
        private static void MigrateAssetsToUserFolder()
        {
            try
            {
                // One-time migration only. Once it has run, never copy again — otherwise a user
                // who keeps their library in the install dir (on another drive) and deletes the
                // %APPDATA% copy to reclaim space gets the whole ~10GB re-copied to the system
                // drive on every launch, since the per-file "destination exists?" guard passes
                // for freshly-deleted files. (asset re-copy / disk-fill bug)
                if (Settings?.Current?.HasMigratedAssetsToUserFolder == true)
                {
                    Logger?.Information("Asset migration skipped — already completed once.");
                    return;
                }

                // If the user has chosen a custom assets folder, they manage their own
                // assets — don't keep copying files into the default AppData location on
                // every launch (bug #227). The migration only ever exists to rescue
                // assets the user hasn't explicitly relocated.
                var customPath = Settings?.Current?.CustomAssetsPath;
                if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                {
                    Logger?.Debug("Asset migration skipped — user has a custom assets path: {Path}", customPath);
                    return;
                }

                var migratedCount = 0;

                // 1. Migrate from current app directory (standard migration)
                migratedCount += MigrateAssetsFromPath(AppDomain.CurrentDomain.BaseDirectory);

                // 2. Also check old version folders in the Velopack app root
                // This rescues assets from old app-X.X.X folders that might still exist
                // Critical for users updating from versions that stored assets in the app folder
                try
                {
                    var appRoot = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
                    if (!string.IsNullOrEmpty(appRoot))
                    {
                        foreach (var dir in Directory.GetDirectories(appRoot, "app-*"))
                        {
                            // Skip current directory to avoid double-processing
                            if (dir.Equals(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                                continue;

                            migratedCount += MigrateAssetsFromPath(dir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger?.Debug("Could not check old version folders: {Error}", ex.Message);
                }

                if (migratedCount > 0)
                {
                    Logger?.Information("Migrated {Count} asset files to user data folder", migratedCount);
                }

                // Mark migration complete so it never runs again. This is what stops the
                // repeated full re-copy after the user deletes the %APPDATA% copy to free space.
                if (Settings?.Current != null)
                {
                    Settings.Current.HasMigratedAssetsToUserFolder = true;
                    Settings.Save();
                    Logger?.Information("Asset migration complete ({Count} files copied) — flag set, will not run again.", migratedCount);
                }
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Asset migration failed");
            }
        }

        /// <summary>
        /// Migrates assets from a specific path (old app directory or old version folder).
        /// Returns the number of files migrated.
        /// </summary>
        private static int MigrateAssetsFromPath(string basePath)
        {
            var migratedCount = 0;

            try
            {
                var oldAssetsPath = Path.Combine(basePath, "assets");

                // Map old folder names to new folder names (startle_videos -> videos)
                var foldersToMigrate = new[] { ("images", "images"), ("startle_videos", "videos"), ("videos", "videos") };

                if (Directory.Exists(oldAssetsPath))
                {
                    foreach (var (oldName, newName) in foldersToMigrate)
                    {
                        var oldFolder = Path.Combine(oldAssetsPath, oldName);
                        var newFolder = Path.Combine(UserAssetsPath, newName);

                        if (!Directory.Exists(oldFolder)) continue;

                        Directory.CreateDirectory(newFolder);

                        foreach (var file in Directory.GetFiles(oldFolder))
                        {
                            var fileName = Path.GetFileName(file);
                            var destFile = Path.Combine(newFolder, fileName);

                            // Don't overwrite existing files in user folder
                            if (File.Exists(destFile)) continue;

                            try
                            {
                                File.Copy(file, destFile);
                                migratedCount++;
                                Logger?.Debug("Migrated asset: {File} from {Source}", fileName, basePath);
                            }
                            catch (Exception ex)
                            {
                                Logger?.Warning("Failed to migrate {File}: {Error}", fileName, ex.Message);
                            }
                        }
                    }
                }

                // Also migrate Spirals folder
                var oldSpirals = Path.Combine(basePath, "Spirals");
                var newSpirals = Path.Combine(UserDataPath, "Spirals");
                if (Directory.Exists(oldSpirals))
                {
                    Directory.CreateDirectory(newSpirals);

                    foreach (var file in Directory.GetFiles(oldSpirals))
                    {
                        var fileName = Path.GetFileName(file);
                        var destFile = Path.Combine(newSpirals, fileName);
                        if (!File.Exists(destFile))
                        {
                            try
                            {
                                File.Copy(file, destFile);
                                migratedCount++;
                                Logger?.Debug("Migrated spiral: {File} from {Source}", fileName, basePath);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Debug("Could not migrate from {Path}: {Error}", basePath, ex.Message);
            }

            return migratedCount;
        }

        /// <summary>
        /// Ensures a configured custom assets folder and its standard subfolders
        /// (images/videos/wallpapers) exist. The default UserAssetsPath subdirs are
        /// created unconditionally at startup, but a custom path is only known after
        /// settings load — and if its folder is missing, EffectiveAssetsPath silently
        /// falls back to the default location, sending imports/extractions to the wrong
        /// place even though settings show the custom path (#391).
        /// </summary>
        internal static void EnsureCustomAssetsDirectories()
        {
            var customPath = Settings?.Current?.CustomAssetsPath;
            if (string.IsNullOrWhiteSpace(customPath)) return;

            try
            {
                // CreateDirectory creates the parent customPath too if absent.
                Directory.CreateDirectory(Path.Combine(customPath, "images"));
                Directory.CreateDirectory(Path.Combine(customPath, "videos"));
                Directory.CreateDirectory(Path.Combine(customPath, "wallpapers"));
                Logger?.Information("Ensured custom assets directories at {Path}", customPath);
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Could not create custom assets directories at {Path} — EffectiveAssetsPath will fall back to the default location", customPath);
            }
        }

        /// <summary>
        /// Check if the installer set a custom assets path in the registry and apply it.
        /// This allows users to confirm/change their assets folder during installation.
        /// </summary>
        private static void ApplyInstallerAssetsPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\CodeBambi\Conditioning Control Panel", writable: true);
                if (key == null) return;

                var installerAssetsPath = key.GetValue("AssetsPath") as string;
                if (string.IsNullOrWhiteSpace(installerAssetsPath)) return;

                // Check if this path differs from default and exists
                if (Directory.Exists(installerAssetsPath))
                {
                    var defaultPath = UserAssetsPath;

                    // If the installer-selected path is different from default, apply it
                    // But only if settings don't already have a custom path set
                    if (!string.Equals(installerAssetsPath, defaultPath, StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(Settings?.Current?.CustomAssetsPath))
                    {
                        if (Settings?.Current != null)
                        {
                            Settings.Current.CustomAssetsPath = installerAssetsPath;
                            Settings.Save();
                            Logger?.Information("Applied installer assets path: {Path}", installerAssetsPath);
                        }
                    }
                }

                // Remove the registry value after processing (one-time operation)
                key.DeleteValue("AssetsPath", throwOnMissingValue: false);
                Logger?.Debug("Cleared installer AssetsPath registry value");
            }
            catch (Exception ex)
            {
                Logger?.Warning(ex, "Failed to apply installer assets path from registry");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger?.Information("Application shutting down...");

            try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }

            // A clean shutdown — even mid-run — is NOT a crash. Clear the chaos sentinel so the next
            // launch doesn't false-report it as an abnormal exit.
            try { Services.Chaos.ChaosCrashSentinel.Clear(); } catch { }

            // If the companion is on its own UI thread (AvatarOwnThread), shut its Dispatcher down so the
            // STA thread's Dispatcher.Run() returns and the thread exits cleanly. Background thread, so it
            // wouldn't block process exit, but shut it down explicitly. No-op when the avatar shares the
            // main dispatcher (the guard skips it when avatarDispatcher == the main dispatcher).
            try
            {
                var avatarDispatcher = AvatarWindow?.Dispatcher;
                if (avatarDispatcher != null && avatarDispatcher != Current?.Dispatcher
                    && !avatarDispatcher.HasShutdownStarted)
                {
                    avatarDispatcher.InvokeShutdown();
                }
            }
            catch (Exception ex) { Logger?.Warning(ex, "Avatar own-thread dispatcher shutdown failed"); }

            // Save settings FIRST (before cloud sync) to persist the user's current local state.
            // This prevents cloud sync from overwriting local values with stale data before save.
            // Use SaveImmediate to flush any pending debounced writes and ensure final state is on disk.
            Settings?.SaveImmediate();

            // Sync profile to cloud on exit (short timeout to avoid blocking shutdown)
            if (ProfileSync?.IsSyncEnabled == true)
            {
                try
                {
                    Logger?.Information("Syncing profile to cloud before exit...");
                    ProfileSync.SyncProfileAsync().Wait(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    Logger?.Warning(ex, "Failed to sync profile on exit");
                }
            }

            // Dispose trigger sources FIRST so no new effects get queued during shutdown
            RemoteControl?.Dispose();
            ScreenOcr?.Dispose();
            KeywordTriggers?.Dispose();
            KeywordHighlight?.Dispose();

            SessionLog?.Dispose();
            Flash?.Dispose();
            // Dispose the enhancement bridge BEFORE the VideoService it subscribes to,
            // so it unsubscribes (VideoStarted/VideoEnded/time-source) and tears down its
            // host/engine + webcam handlers while VideoService is still alive. Disposing
            // Video first would leave those subscriptions dangling against a dead player.
            VideoEnhanceBridge?.Dispose();
            Video?.Dispose();
            Subliminal?.Dispose();
            Overlay?.Dispose();
            ScreenShake?.Dispose();
            try { Chaos?.ForceShutdown(); } catch { }
            Bubbles?.Dispose();
            LockCard?.Dispose();
            PopQuiz?.Dispose();
            BubbleCount?.Dispose();
            BouncingText?.Dispose();
            MindWipe?.Dispose();
            BrainDrain?.Dispose();
            Achievements?.Dispose();
            WindowAwareness?.Dispose();
            Ai?.Dispose();
            Patreon?.Dispose();
            Update?.Dispose();
            ProfileSync?.Dispose();
            Leaderboard?.Dispose();
            DiscordRpc?.Dispose();
            Discord?.Dispose();
            DualMonitorVideo?.Dispose();
            ScreenMirror?.Dispose();
            Autonomy?.Dispose();
            Wallpaper?.Dispose();
            BlinkTrainer?.Dispose();
            GazeFocus?.Dispose();
            GazeCursor?.Dispose();
            GazeDrift?.Dispose();
            Webcam?.Dispose();
            FocusGame?.Dispose();
            ContentPacks?.Dispose();
            Roadmap?.Dispose();
            SkillTree?.Dispose();
            QuestDefinitions?.Dispose();
            Quests?.Dispose();
            Companion?.Dispose();
            CommunityPrompts?.Dispose();
            ActivityTracker?.Dispose();
            Haptics?.Dispose();
            AudioSync?.Dispose();
            Audio?.Dispose();
            // Deeper singletons (reverse init order). The bridge holds the
            // browser/host pair; discovery owns a CTS + WebView2 nav handler;
            // host owns the engine-bind state; player owns NAudio handles;
            // fetcher owns an HttpClient; library owns a FileSystemWatcher.
            BrowserEnhanceBridge?.Dispose();
            DeeperBrowserDiscovery?.Dispose();
            DeeperHost?.Dispose();
            DeeperPlayer?.Dispose();
            DeeperFetcher?.Dispose();
            EnhancementLibrary?.Dispose();

            // Terminate any `ollama serve` we spawned so it doesn't outlive the app.
            // (Servers started by the Ollama installer's auto-start or the user's tray
            // app are untouched — only the process we explicitly launched.)
            try { Services.AIService.OllamaSetupService.StopSpawnedServer(); }
            catch (Exception ex) { Logger?.Warning(ex, "Failed to stop spawned Ollama server"); }

            // Clear in-memory secrets before exit to reduce memory exposure
            SecureAuthTokenStore.ClearMemoryCache();
            SecureApiKeyStore.ClearMemoryCache();

            // Close and flush the logger
            Log.CloseAndFlush();

            // Dispose show-window signal
            var signal = _showSignal;
            _showSignal = null;
            signal?.Set(); // Unblock the listener thread
            signal?.Dispose();

            // Dispose the single-instance ack gate
            var ackSignal = _showAckSignal;
            _showAckSignal = null;
            try { ackSignal?.Dispose(); } catch { }

            // Release single instance mutex (only if we own it)
            if (_mutexOwned && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex was not owned by this thread - ignore
                }
            }
            _mutex?.Dispose();

            base.OnExit(e);

            // Force exit to ensure no background threads keep process alive
            Environment.Exit(0);
        }
    }
}
