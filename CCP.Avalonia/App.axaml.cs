using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia
{
    public partial class App : Application
    {
        /// <summary>The settings service, or null on the headless render path.</summary>
        public static SettingsService? Settings { get; private set; }

        /// <summary>In-app corner toasts (WPF App.Notifications). Queues until the shell attaches it.</summary>
        public static Helpers.NotificationService Notifications { get; } = new();

        /// <summary>Weekly free-tier pass for the Graded Intake (WPF App.IntakePass).</summary>
        public static IntakePassService IntakePass { get; } = new();

        /// <summary>HT-URL lookup + download (WPF App.CatalogueLookup). The shell registers the opener.</summary>
        internal static CatalogueLookup CatalogueLookup { get; set; } = new(
            () => Services.Deeper.DeeperLocalLibrary.DefaultFolder, CoreReleaseContent.AppVersion, OnUiThread);

        /// <summary>The lookup resumes off the UI thread (ConfigureAwait(false)); its opener builds a window.</summary>
        internal static Task<bool> OnUiThread(Func<bool> open) => Dispatcher.UIThread.InvokeAsync(open).GetTask();

        /// <summary>The achievement engine (achievements.json), or null on the headless render path.
        /// Local-only: no sync, no streak writes, no ResetProgress on this head (oracle-achievements.md).</summary>
        internal static AchievementEngine? Achievements { get; private set; }

        /// <summary>WPF App.WindowAwareness (App.xaml.cs:2674), the legacy title observer. X11/XWayland
        /// titles only; unlike WPF, the privacy rules run on each title first (docs/avalonia-decisions.md).</summary>
        internal static WindowAwarenessService WindowAwareness { get; } =
            new(Platform.X11ActiveWindow.ReadTitle, WindowAwarenessService.PassesPrivacyRules);

        /// <summary>The one session runner (WPF MainWindow._sessionEngine), or null on the headless render path.</summary>
        internal static SessionRunner? Sessions { get; set; }
        /// <summary>The app-lifetime media recap (WPF App.MediaHistory), or null on the headless render path.</summary>
        internal static MediaHistoryService? MediaHistory { get; set; }
        /// <summary>THE FUSE (WPF App.DescentCountdown). Built before the shell so its spark can subscribe.</summary>
        internal static Services.Descent.DescentCountdownService? DescentCountdown { get; set; }

        /// <summary>The Core quest board (WPF App.Quests), built by StartQuests before the shell.</summary>
        internal static QuestService? Quests { get; set; }

        /// <summary>The typed mantra game (WPF App.Mantra, built unconditionally at App.xaml.cs:3260).</summary>
        internal static MantraService Mantra { get; } = new();
        /// <summary>WPF App.MantraVoice: the active mod's Spoken Mantras (mantras.json).</summary>
        internal static MantraVoiceService MantraVoice { get; } = new();

        /// <summary>WPF App.xaml.cs:2527-2536 plus the CoreQuests seeds of :398-421. Seeded where this
        /// head has the service. The streak shield is WPF SkillTreeService.UseStreakShield (:378) over
        /// Core settings; perfect-week bonus and Programs (TrackVerifier) stay unseeded: no bonus,
        /// no program tracking - the WPF "service is null" answers.</summary>
        private static void StartQuests()
        {
            CoreQuests.PatreonVerifyingProvider = () => Platform.AccountSeed.Patreon?.IsVerifying;
            CoreQuests.SubscribeStarVerifyingProvider = () => Platform.AccountSeed.SubscribeStar?.IsVerifying == true;
            CoreQuests.HasStreakShieldProvider = () => CoreSettings.Service?.Current is { } s
                && Models.SkillTreeRules.HasSkill(s, "good_girl_streak") && s.StreakShieldsRemaining > 0;
            CoreQuests.UseStreakShieldProvider = () =>
            {
                if (CoreQuests.HasStreakShieldProvider?.Invoke() != true) return false;
                CoreSettings.Current.StreakShieldsRemaining--;
                CoreSettings.Save();
                return true;
            };
            CoreQuests.PlayCompletionEffectsProvider = PlayQuestCompletionEffects;
            // Real probes, not the fail-open default (which reads present + resolved). A throw
            // (no pactl) reaches the gate's CachedProbe and still fails open, as WPF's strict pair does.
            CoreQuests.CameraProbe = () => System.IO.Directory.EnumerateFiles("/dev", "video*").Any();
            CoreQuests.MicrophoneProbe = () => Platform.PulseMicSource.ParseSources(Platform.LibVlcAudio.Pactl("list short sources")).Count > 1;

            var definitions = new QuestDefinitionService();
            _ = definitions.InitializeAsync(); // cache first, then the server, as WPF
            Quests = new QuestService(definitions);
            definitions.QuestDefinitionsUpdated += () => Quests?.CheckAndGenerateQuests();
            // WPF ProgressionService.AddXP:120 feeds every award to the "earn X XP" quests.
            ProgressionBank.Awarded += (amount, _) => Quests?.TrackXPEarned((int)amount);
        }

        /// <summary>WPF App.xaml.cs:414. WPF plays SystemSounds.Exclamation; Linux has no stock
        /// equivalent, so a bundled chime through the head audio. Then ONE QuestComplete haptic post:
        /// the Haptics tab's "Quest complete" routing row decides how it feels.</summary>
        internal static void PlayQuestCompletionEffects()
        {
            try
            {
                CoreAudio.PlayOneShot(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "chime1.mp3"),
                    Math.Clamp(CoreSettings.Current.MasterVolume / 100f, 0f, 1f), "quest-complete");
            }
            catch (Exception ex) { Serilog.Log.Debug("Quest chime failed: {E}", ex.Message); } // the haptic still posts
            _ = CoreHaptics.Service?.PostEvent(ConditioningControlPanel.Services.Haptics.Core.HapticEventKind.QuestComplete);
        }

        /// <summary>The mod service (WPF App.Mods), or null on the headless render path.</summary>
        internal static ModService? Mods { get; private set; }

        /// <summary>
        /// WPF App.OnStartup's mod block (App.xaml.cs, "Initialize mod system"): one service, seeded
        /// into CoreMods, initialised from the saved ActiveModId. Desktop lifetime only, so a headless
        /// render never reads or writes a profile. Initialize restores the per-mod pools and runs the
        /// one-shot Hypnotube migration on CoreSettings.Current; with KnownVideoLinksProvider seeded
        /// below from the same Core title table WPF's AvatarTubeWindow.KnownVideoLinks starts from,
        /// that is the write WPF makes at launch (StartModsTests pins it against the Core golden).
        /// The other CoreModsHooks stay unseeded: each targets a WPF service this head does not have
        /// yet (Brain, Bark, Companion, DTRH/Arcademy hosts, ModResourceResolver's cache, the portrait
        /// loader, the voice-line index, BambiSprite), and unseeded means "nothing to invalidate".
        /// Seed each when its counterpart lands here.
        /// </summary>
        internal static void StartMods()
        {
            // WPF seeds this with AvatarTubeWindow.KnownVideoLinks, which at startup is a copy of
            // the same Core table; this head has no per-mod link reload, so the table itself.
            CoreModsHooks.KnownVideoLinksProvider = () => HypnotubeDefaultLinks.KnownVideoTitles;
            Mods = new ModService();
            CoreMods.Attach(Mods);
            Mods.Initialize(CoreSettings.Current.ActiveModId);
        }

        /// <summary>The cclabs catalogue client (WPF App.Catalogue): the same Core client and inputs as WPF's
        /// CatalogueService. Settable so a test can hand in a fake-handler client.</summary>
        internal static CatalogueClient Catalogue { get; set; } = new(
            () => CoreSettings.Current.AuthToken, () => CoreAccount.UnifiedUserId, CoreReleaseContent.AppVersion);

        /// <summary>The cloud companion AI (WPF App.Ai), the same Core AiService. Its base URL follows
        /// AiService.ResolveBaseUrl: a sandbox without a loopback CCP_AI_BASE_URL never sends.
        /// Settable so a test can hand in a fake-endpoint instance.</summary>
        /// <para>local-providers: WPF's router (App.xaml.cs:2736) now in Core, so Settings → Local / OpenAI-compatible
        /// reaches the user's own server exactly as WPF; the cloud leg is the same AiService as before.</para>
        internal static ConditioningControlPanel.Services.AIService.IAiService? Ai { get; set; } =
            new ConditioningControlPanel.Services.AIService.AiServiceStrategy();

        /// <summary>The companion's conversational spine (WPF App.Brain, App.xaml.cs:2690): the same Core
        /// CompanionBrain over <see cref="Ai"/>, so history and memory live where WPF keeps them
        /// (CorePaths.UserData/companion). Null when construction failed or on the headless render path,
        /// which sends through the stateless call exactly as WPF's kill-switch-off path.</summary>
        internal static ConditioningControlPanel.Services.Companion.Brain.CompanionBrain? Brain { get; set; }

        /// <summary>
        /// WPF App.xaml.cs SeedAskSeams + NoticeSurface: the asks service and the oversize-prompt toast.
        /// ponytail: SessionOptions/StartSession stay unseeded (no session launcher on this head, so no
        /// Session cards), Busy knows a session, video, lock card, bubble count and pop quiz (not grace pause), and a Watch link opens in the external
        /// browser - WPF's own fallback when its embedded browser cannot take it.
        /// </summary>
        internal static void SeedCompanionTubeSeams()
        {
            ConditioningControlPanel.Services.Companion.Brain.PromptAssembler.NoticeSurface = () =>
                message => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { Notifications.Show(message, Helpers.NotificationType.Warning, TimeSpan.FromSeconds(12)); }
                    catch (Exception ex) { Serilog.Log.Debug("PromptAssembler: oversize notice failed to show: {Error}", ex.Message); }
                });
            // Only when a card can really follow here (no session launcher: Session cards never build).
            ConditioningControlPanel.Services.Companion.ConversationDelivery.AskCardsShown =
                ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.Instance.CanOfferFor;
            ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.BrainProvider = () => Brain;
            ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.ShowCardSurface = card =>
                Views.AvatarTube.AvatarTubeWindow.Live?.RunOnAvatar(() => Views.AvatarTube.AvatarTubeWindow.Live?.ShowAskCard(card));
            ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.SaySurface = text =>
                Views.AvatarTube.AvatarTubeWindow.Live?.RunOnAvatar(() => Views.AvatarTube.AvatarTubeWindow.Live?.GigglePriority(text, aiGenerated: false));
            ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.BusyProvider = () => CoreSession.IsSessionRunning
                || CoreEngine.Video?.IsPlaying == true || Views.Windows.LockCardWindow.IsAnyOpen()
                || Views.Windows.BubbleCountWindow.IsAnyOpen() || Views.Windows.PopQuizWindow.IsAnyOpen();
            ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.OpenLink = url =>
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                    _ = Platform.AppUpdater.OpenUrl(Views.AvatarTube.AvatarTubeWindow.Live, url!);
                else Serilog.Log.Warning("Companion watch chip refused a non-https link");
            };
            ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.Instance.Start();
        }

        /// <summary>WPF CompanionService.UserMessageSent (the "user just talked to her" signal).</summary>
        internal static event Action? UserMessageSent;
        internal static void NotifyUserMessageSent() => UserMessageSent?.Invoke();

        /// <summary>WPF FlashDisplayed / SubliminalDisplayed / OnBubblePopped, as a MemorySignalWriter feature id.</summary>
        internal static event Action<string>? FeatureUsed;
        internal static void NoteFeatureUsed(string feature) => FeatureUsed?.Invoke(feature);

        /// <summary>
        /// WPF App.xaml.cs SignalMirrorFactory + WireMemorySignalSources: the brain's memory profile gets
        /// level, streak, sessions, archetype (CoreSettings), level-ups, favourite features and per-mod
        /// chat turns and mantra reps (WPF's DeferredSourcesHook; App.Mantra exists from the start here).
        /// ponytail: no brain drain / mind wipe on this head, so those two favourites never count.
        /// </summary>
        internal static void SeedMemorySignals()
        {
            ConditioningControlPanel.Services.Companion.Brain.MemoryStore.SignalMirrorFactory = store =>
            {
                var writer = new ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter(store);
                try { writer.Start(); }
                catch { writer.Dispose(); throw; }
                return writer;
            };
            ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.SourcesHook = w =>
            {
                w.Wire<Action<int>>(h => ProgressionBank.LevelUp += h, h => ProgressionBank.LevelUp -= h, _ => w.SafeRefresh());
                w.Wire<Action<string>>(h => FeatureUsed += h, h => FeatureUsed -= h, f => w.NoteFeatureUse(f));
                w.Wire<Action>(h => Mantra.MantraCompleted += h, h => Mantra.MantraCompleted -= h,
                    () => w.NoteFeatureUse(ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureMantra));
                if (CoreEngine.Video is { } video)
                    w.Wire<Action>(h => video.VideoStarted += h, h => video.VideoStarted -= h,
                        () => w.NoteFeatureUse(ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureVideo));
                w.Wire<Action>(h => UserMessageSent += h, h => UserMessageSent -= h, () => w.NoteChatTurn(CoreMods.ActiveModId));
            };
            // The brain raises this for every send it takes (CompanionBrain.cs:311); the tube raises the rest.
            ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.UserMessageSent = NotifyUserMessageSent;
        }

        /// <summary>The release-content pack service (WPF App.ReleaseContent), or null on the headless render path.</summary>
        internal static ReleaseContentService? ReleaseContent { get; private set; }

        /// <summary>
        /// WPF App.xaml.cs:346-356 (seams) and :2943-2951 (service + AttachReleaseContent): the pack
        /// service the mod service reads stamps and sizes from. Install stamps are written on the UI
        /// thread, blocking, and skipped once the dispatcher is shutting down (WPF HasShutdownStarted).
        /// </summary>
        private static volatile bool _exiting;

        /// <summary>
        /// Hard guard on top of WPF's rules: a sandboxed profile (CCP_USERDATA_DIR, i.e. tests and
        /// Keincheck runs) never auto-fetches from GitHub. Only an honoured loopback
        /// CCP_CONTENT_BASE_URL lets the startup check run there.
        /// </summary>
        internal static bool SkipStartupFetch(string? userDataDir, string? contentBaseUrl) =>
            !string.IsNullOrEmpty(userDataDir)
            && ReleaseContentService.ResolveBaseUrlFormat(contentBaseUrl) == ReleaseContentService.ResolveBaseUrlFormat(null);

        /// <summary>Test-only: forget the pack service, the mod service and the exit flag.</summary>
        internal static void ResetReleaseContent()
        {
            ReleaseContent = null;
            Mods = null;
            _exiting = false;
        }

        internal static void StartReleaseContent(ReleaseContentService service)
        {
            CoreReleaseContent.StampProvider = ReleaseContentService.GetStampFor;
            CoreReleaseContent.PackInfoProvider = id => ReleaseContent?.GetPackInfo(id);
            CoreReleaseContent.UiInvoke = apply =>
            {
                var ui = Dispatcher.UIThread;
                if (_exiting) return false;
                if (ui.CheckAccess()) apply();
                else ui.Invoke(apply);
                return true;
            };
            ReleaseContent = service;
            Mods?.AttachReleaseContent();
        }

        private AvaloniaCoreDispatch? _desktopDispatch;
        private int _exitHandled;
        private int _warnedMissingCustomAssetsPath;

        /// <summary>
        /// Creates the desktop session catalogue. Headless paths never call this factory; tests can
        /// override it with a manager backed by explicit temporary folders.
        /// </summary>
        protected virtual SessionManager CreateSessionManager() => new();

        /// <summary>achievements.json. Tests that run the desktop path override it with a sandbox path.</summary>
        protected virtual string AchievementsPath => AchievementStore.DefaultPath;

        /// <summary>WPF App.Chaster?.Note(id), with its swallow: inert until the tab is on and priced.</summary>
        internal static void ChasterNote(string id)
        {
            try { Platform.ChasterHead.Service?.Note(id); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] {Id} hook", id); }
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            // Load the language table before any view binds a string. The WPF head does this in
            // App.OnStartup; every head must, because LocalizationManager holds no language until
            // told. The JSON now ships with CCP.Core, so it is in this head's output too.
            //
            // Here rather than in OnFrameworkInitializationCompleted deliberately: the offscreen
            // render path uses SetupWithoutStarting(), which never reaches that callback, so a
            // view rendered for CI would show raw keys while the running app showed real strings.
            // Initialize() runs on both paths.
            //
            // The real profile is intentionally not opened here: headless rendering reaches
            // Initialize() without a desktop lifetime. The desktop path restores the saved
            // language after SettingsService is available, immediately before constructing the
            // shell below.
            if (!CoreSettings.HasProvider)
                LocalizationManager.Instance.SetLanguage("en");
        }

        /// <summary>Set by Program.Main: a real launch shows the splash. Every other desktop setup
        /// (tests, PanicCheck, VideoCheck) starts synchronously without one, as before.</summary>
        internal static bool SplashOnStartup;

        public override void OnFrameworkInitializationCompleted()
        {
            // WPF App.xaml.cs:1949 shows the splash before anything else and threads SetProgress
            // through startup. One UI thread here, so startup yields a frame per step instead.
            var splash = SplashOnStartup && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
                ? Views.Windows.SplashScreen.ShowOnOwnThread() : null;
            var start = StartBehindSplash(splash, StartDesktop,
                () => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
            // Without a splash every step completed inline, so a startup failure throws here as before;
            // behind one it is rethrown on the UI thread.
            if (start.IsCompleted) start.GetAwaiter().GetResult();
            // Behind one, end the loop with a failure code; Program.Main rethrows StartupFailure.
            else start.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
                    (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(1)),
                TaskContinuationOptions.OnlyOnFaulted);
            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>Runs <paramref name="start"/>, painting each step on the splash; then shows the
        /// shell (the lifetime found no MainWindow to show yet) and fades the splash out (WPF
        /// App.xaml.cs:3837-3867). No splash: the steps are no-ops and everything runs inline.</summary>
        /// <summary>A startup failure behind the splash; Program.Main rethrows it after the loop ends.</summary>
        internal static System.Runtime.ExceptionServices.ExceptionDispatchInfo? StartupFailure;

        internal static async Task StartBehindSplash(Views.Windows.SplashScreen? splash,
            Func<Func<double, string, Task>, Task> start, Func<global::Avalonia.Controls.Window?> shell)
        {
            if (splash is null) { await start(static (_, _) => Task.CompletedTask); return; }
            try
            {
                await start((progress, status) => { splash.ShowStep(progress, status); return splash.NextFrame(); });
            }
            catch (Exception ex)
            {
                // Recorded before the splash closes: closing the last window ends the loop with code 0.
                Serilog.Log.Fatal(ex, "Startup failed");
                StartupFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                splash.CloseImmediate();
                throw;
            }
            var window = shell();
            window?.Show();
            splash.SetProgress(1.0, "Ready!");
            splash.FadeOutAndClose(() => { if (window is { IsVisible: true }) window.Activate(); });
        }

        private async Task StartDesktop(Func<double, string, Task> step)
        {
            await step(0.0, "Starting...");
            // The app shell is the startup window. Until now this head opened the diagnostics
            // MainWindow, which was right while the shell did not exist and is wrong now that it
            // does. The diagnostics window is still reachable, from Settings, and RenderProof
            // still hosts single views inside it - neither depends on it being the startup window.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Core timers and background engine callbacks must return to this dispatcher,
                // while Core's unseeded providers remain the contract for renders and tests.
                _desktopDispatch = new AvaloniaCoreDispatch(Dispatcher.UIThread);
                _desktopDispatch.Attach();
                desktop.Exit += OnDesktopExit;

                // Real settings on this head: SettingsService now lives in Core and reads and
                // writes settings.json under CorePaths.UserData (~/.local/share on Linux), with
                // the same migrations, backups and recovery the Windows app has. Seeded here and
                // not in Initialize() on purpose: the headless render path never reaches this
                // callback, so a CI render cannot touch a user's profile. Unseeded, Core hands
                // out one default instance, which is what the renders bind against.
                await step(0.2, "Loading settings...");
                // Secrets first: SettingsService's auth-token migration asks CoreSecrets.HasStore on load.
                Platform.SecretStore.Seed();
                Settings = new SettingsService();
                CoreSettings.ServiceProvider = () => Settings;
                CorePaths.EffectiveAssetsProvider = ResolveEffectiveAssetsPath;
                LocalizationManager.Instance.SetLanguage(Settings.Current.Language);
                // WPF App.xaml.cs:3088: a run killed mid-lockdown gets its real panic key and Strict
                // Lock back from lockdown_recovery.json before anything reads them.
                LockdownService.RecoverIfNeeded();
                LockdownService.Current = new LockdownService();

                // Mod art: the same Core chain WPF's ModResourceResolver walks. Answers from the
                // active mod once StartMods (below) seeds CoreMods; before that every answer is "no override".
                // No event skin on this head yet (nothing arms LiveEventService on either head).
                CoreModArt.OverridePathProvider = p => CoreModArt.ResolveOverride(p, null, CoreMods.ActiveModPackage?.InstalledPath);
                CoreModArt.AudioOverridePathProvider = p => CoreModArt.ModAudioFile(p, CoreMods.ActiveModPackage?.InstalledPath);

                // The lock-card surface seam. The schedule and the no-repeat phrase rotation are in
                // Core now (LockCardScheduler); this is the half that draws, and on this head that
                // is LockCardWindow.ShowOnAllMonitors - which already refuses to stack a second
                // card and solves a voice-mode card by speech when the seeded engine, a model and
                // mic consent allow it (typing otherwise).
                //
                // The hop is explicit because CoreDispatch is unseeded on this head, so the
                // scheduler's tick arrives on a thread-pool thread. Everything after the hop -
                // including the phrase draw - therefore runs on the UI thread, which is what keeps
                // the scheduler's rotation state single-threaded.
                CoreLockCard.ShowHandler = isTest => global::Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => Views.Windows.LockCardWindow.ShowNext(isTest));

                // Pop quiz: Core schedules, PopQuizHost opens the window (WPF App.PopQuiz).
                CoreEngine.PopQuiz = Views.Windows.PopQuizHost.Instance.Scheduler;
                // Mandatory video: Core schedules, the overlay plays (WPF App.Video).
                CoreEngine.Video = Views.Overlays.MandatoryVideoOverlay.Instance.Scheduler;
                CoreEngine.BubbleCount = Views.Windows.BubbleCountHost.Instance.Scheduler;

                await step(0.4, "Initializing flash service...");
                // The ambient flash surface. CoreFlash owns the rhythm; a burst needs any attached
                // visual to reach Screens, and the main window is the one that always is.
                CoreFlash.IsBusyProvider = () => Views.Overlays.FlashOverlay.IsBusy;
                CoreFlash.ShowProvider = () =>
                {
                    if (desktop.MainWindow is not { } host) return;
                    Views.Overlays.FlashOverlay.TriggerOnce(host);
                    NoteFeatureUsed(ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureFlash);
                };

                // Subliminal and bouncing-text surfaces. Core owns the schedule / the motion;
                // these draw on click-through overlays (Views/Overlays), hosted like the flash.
                // SubliminalWhisperShow plays the linked whisper first, then draws SubliminalOverlay.
                CoreSubliminal.ShowProvider = text =>
                {
                    Views.Overlays.SubliminalWhisperShow.Phrase(text);
                    NoteFeatureUsed(ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureSubliminal);
                };
                CoreSubliminal.BambiFreezeProvider = Views.Overlays.SubliminalWhisperShow.Freeze;
                CoreSubliminal.RunStateChanged = running =>
                {
                    if (running) return;
                    Views.Overlays.SubliminalWhisperShow.StopAll();
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(Views.Overlays.SubliminalOverlay.CloseAll);
                };
                CoreBouncingText.StartAction = () =>
                {
                    if (desktop.MainWindow is { } host) Views.Overlays.BouncingTextOverlay.Start(host);
                };
                CoreBouncingText.StopAction = Views.Overlays.BouncingTextOverlay.Stop;
                CoreBouncingText.RefreshAction = Views.Overlays.BouncingTextOverlay.Refresh;
                CoreBouncingText.RestartAction = Views.Overlays.BouncingTextOverlay.Restart;
                CoreBubbles.StartAction = () =>
                {
                    if (desktop.MainWindow is { } host) Views.Overlays.BubbleOverlay.Start(host);
                };
                // WPF BubbleService.PauseAndClear/Resume for the bubble-count game; a Stop cancels the resume.
                var bubblesPaused = false;
                CoreBubbles.StopAction = () => { bubblesPaused = false; Views.Overlays.BubbleOverlay.Stop(); };
                // Posted, so a caller off the UI thread (an unseeded CoreDispatch in tests) is safe.
                CoreBubbles.PauseAction = () => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (Views.Overlays.BubbleOverlay.IsRunning) { bubblesPaused = true; Views.Overlays.BubbleOverlay.Stop(); }
                });
                CoreBubbles.ResumeAction = () => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (bubblesPaused && desktop.MainWindow is { } host) Views.Overlays.BubbleOverlay.Start(host);
                    bubblesPaused = false;
                });
                CoreBubbles.RefreshFrequencyAction = Views.Overlays.BubbleOverlay.RefreshFrequency;

                // Core cannot read the running build's version - the entry assembly is whichever
                // head started the process, and Core is not it. Reading the version is a head job,
                // so this head reports its own; unseeded, CoreReleaseContent answers "0.0.0".
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                CoreReleaseContent.AppVersionProvider = () =>
                    version is null ? null : $"{version.Major}.{version.Minor}.{version.Build}";
                // WPF App.xaml.cs:490/2549: the ? box's daily-free rotation that TierGate ORs in.
                // Pure ctor; the override fetch is fire-and-forget and falls back to the seeded pick.
                // A sandbox never reaches the real proxy (the SkipStartupFetch rule); the seeded pick still works.
                var dailyFree = new DailyFreeService(
                    fetchOverride: string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")));
                _ = dailyFree.RefreshAsync();
                CoreEntitlement.IsFreeTodayProvider = dailyFree.IsFreeToday;
                // WPF App.xaml.cs:494: the weekly Graded Intake pass. Its TierChanged hooks are
                // attached by AccountSeed.Seed() below, once the providers exist.
                CoreEntitlement.IntakePassAvailableProvider = () => IntakePass.IsPassAvailable;
                // WPF App.xaml.cs:2875/2941: the one HapticService, gated (premium / daily free) in its
                // mixer. Connects only to what the user runs (Intiface at ButtplugUrl, Lovense Remote).
                // ponytail: no MockToast (mock toys log only), no funscript playhead (FunScriptService.
                // SubscribePlaybackTime unseeded: no video time source here), no EMI Desk.
                CoreHaptics.Service = new Services.HapticService(Settings.Current.Haptics);
                _ = CoreHaptics.Service.AutoConnectOnStartupAsync();
                // After the version seed (the proxy client's headers carry it). Fail closed; the
                // startup validate runs on the UI thread as WPF's does (App.xaml.cs OnStartup).
                // WPF App.xaml.cs:2856. Start() arms nothing without a cached ceremony timestamp.
                // ponytail: no Speaker - the tube's speech coupling is not ported, so phase lines stay unsaid.
                DescentCountdown = new Services.Descent.DescentCountdownService();
                DescentCountdown.Start();
                if (Platform.AccountSeed.Seed())
                {
                    // Unit 7c, with the logout clear: the push, XP banking and its two triggers (WPF
                    // MainWindow OnLevelUp -> sync, ProfileSyncService.AttachXpNudge outside sessions).
                    var sync = Platform.AccountSeed.Sync = new SyncPush(
                        () => Achievements?.Progress?.UnlockedAchievements, () => Sessions?.IsRunning == true)
                        { Countdown = DescentCountdown };
                    CoreProgression.AddXPProvider = ProgressionBank.Add;
                    ProgressionBank.LevelUp += level => sync.PushAsync($"level-up {level}");
                    ProgressionBank.Awarded += (amount, source) =>
                    {
                        if (amount > 0 && Sessions?.IsRunning != true) sync.Nudge($"xp:{source}");
                    };
                    Platform.AccountSeed.RestoreSession();
                    Dispatcher.UIThread.Post(async () =>
                    {
                        await Platform.AccountSeed.InitializeAsync();
                        await Platform.AccountSeed.ValidateRestoredSessionAsync();
                        (desktop.MainWindow as Views.Windows.MainShellWindow)?.UpdateQuickLoginUI();
                    });
                }
                // After the version seed: installing / loading a mod checks its MinAppVersion.
                StartMods();
                // WPF App.xaml.cs:2513 (App.Chaster). After the version seed: it is the User-Agent.
                try
                {
                    Platform.ChasterHead.Service = Platform.ChasterHead.Create(
                        Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"), Environment.GetEnvironmentVariable(Platform.ChasterHead.EnvVar));
                    // WPF :3149-3150: the daily settle (a minute in, then every ten) and the hooks.
                    // A sandbox settles only against its loopback fake: ChasterHead routes fail closed.
                    Platform.ChasterHead.Service.StartSettle();
                    Platform.ChasterHead.Attach(Platform.ChasterHead.Service, Quests);
                    (desktop.MainWindow as Views.Windows.MainShellWindow)?.InitializeChasterFlash(Platform.ChasterHead.Service);
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[Chaster] service could not be built"); }
                // WPF App.xaml.cs FRIENDS: no request at build; a new identity is the sign-in moment to poll now.
                try
                {
                    var friends = Platform.FriendsHead.Create(
                        Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"), Environment.GetEnvironmentVariable(Platform.FriendsHead.EnvVar));
                    if (friends != null)
                    {
                        Platform.FriendsHead.Service = friends;
                        CoreAccount.UnifiedIdentityChanged += (_, _) => friends.Kick();
                        friends.Start(new Platform.FriendsHead.Timer());
                    }
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] service could not be built"); }
                // WPF App.xaml.cs:2940-2968. EnsureBaselineAsync keeps WPF's rules: no-op on a full
                // install, under a debugger, in offline mode, or when nothing is missing. A Linux dev
                // build never reads as a full install (flashes_audio is not shipped here), so an
                // undebugged run with Bambi Sleep active fetches; sandbox runs point it at a loopback
                // server with CCP_CONTENT_BASE_URL.
                try
                {
                    var releaseContent = new ReleaseContentService();
                    StartReleaseContent(releaseContent);
                    if (SkipStartupFetch(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"), Environment.GetEnvironmentVariable("CCP_CONTENT_BASE_URL")))
                        Serilog.Log.Information("ReleaseContent: sandboxed profile without a loopback CCP_CONTENT_BASE_URL - startup fetch skipped");
                    else _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try { await releaseContent.EnsureBaselineAsync(); }
                        catch (Exception ex) { Serilog.Log.Warning(ex, "ReleaseContent: baseline check failed"); }
                    });
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to initialize ReleaseContentService - downloaded content unavailable this session");
                }
                await step(0.3, "Initializing audio...");
                // Real audio through LibVLC, seeded only if libvlc loads. If it is missing,
                // CoreAudio stays unseeded: every clip "finishes" at once and nothing plays.
                // Console as well as Serilog: this head configures no Serilog sink yet.
                try
                {
                    var vlc = new Platform.LibVlcAudio();
                    vlc.Seed();
                    // Mind wipe plays through the same LibVLC (WPF App.MindWipe, App.xaml.cs:385).
                    new Platform.MindWipePlayer(vlc.PlayVoice) { CleanSlate = secs => Achievements?.TrackMindWipeDuration(secs) }.Seed();
                    Console.WriteLine("[Audio] LibVLC seeded CoreAudio");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Audio] LibVLC unavailable, audio disabled: {ex.Message}");
                    Serilog.Log.Warning(ex, "[Audio] LibVLC unavailable; audio disabled on this head");
                }
                // Speech: Core Vosk engine over parec, seeding CoreSpeech (model is a drop-in, no download).
                try { Platform.PulseMicSource.Seed(); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[Speech] engine unavailable on this head"); }
                // Webcam: this head has a tracking engine (WPF: Webcam != null). OpenCV/models/camera load at
                // Start, which fails with a message; revoke keeps all four of the consent dialog's promises.
                CoreWebcam.IsAvailableProvider = () => true;
                CoreWebcam.RevokeConsentAction = Platform.WebcamTracker.RevokeConsent;
                // The plain engine (Start/Stop) is CoreEngine; the one SessionRunner seeds
                // IsSessionRunningProvider, so the feature lock fires for a session, not a plain Start.
                CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
                Sessions = new SessionRunner(new SessionLogService());
                // WPF App.xaml.cs:2566: the media recap. FlashOverlay feeds images; the video scheduler feeds clips.
                MediaHistory = new MediaHistoryService();
                if (CoreEngine.Video is { } recapVideo)
                    recapVideo.VideoStarted += () => MediaHistory?.RecordVideo(recapVideo.LastVideoPath);
                //
                // The one escalating moderation counter (WPF App.xaml.cs:2653), filed under CorePaths.UserData
                // for the same reason the log below is not seeded: ApplicationData is a second tree here.
                var moderationCounter = new ConditioningControlPanel.Services.Moderation.ModerationCounter(
                    System.IO.Path.Combine(CorePaths.UserData, "moderation-counter.json"));
                try { moderationCounter.LoadFromDisk(); }
                catch (Exception ex) { Serilog.Log.Debug("ModerationCounter.LoadFromDisk failed: {Error}", ex.Message); }
                CoreModerationLog.CounterProvider = () => moderationCounter;
                CoreAi.IsAvailableProvider = () => Ai?.IsAvailable == true;   // WPF App.xaml.cs:380
                await step(0.85, "Initializing companion...");
                // WPF App.xaml.cs:2690: built unconditionally, UseCompanionBrain decides per send. The bark
                // echo stays unseeded (no bark engine here: CoreBark is a doorbell); command executor and
                // activities are CompanionEffects (seeded below). SeedMemorySignals seeds UserMessageSent for the memory
                // chat counter only; no companion-chat achievement listens to it on this head yet.
                SeedMemorySignals();
                try { if (Ai != null) Brain = new ConditioningControlPanel.Services.Companion.Brain.CompanionBrain(Ai); }
                catch (Exception ex) { Brain = null; Serilog.Log.Error(ex, "CompanionBrain: initialization failed, falling back to the stateless AI path"); }
                SeedCompanionTubeSeams();
                CompanionEffects.Seed();
                // WPF App.xaml.cs:554 / 2786 / 2816: legacy adapters route through the brain, a brain wipe also
                // clears the legacy local transcript, and a Local user gets the model warmed up in the background.
                ConditioningControlPanel.Services.AIService.AiServiceStrategy.BrainProvider = () => Brain;
                ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.ClearLegacyLocalHistoryHook =
                    () => (Ai as ConditioningControlPanel.Services.AIService.AiServiceStrategy)?.ClearLocalHistory();
                if (Ai is ConditioningControlPanel.Services.AIService.AiServiceStrategy strategy)
                    _ = Task.Run(async () => { try { await strategy.WarmUpLocalAsync(); } catch (Exception ex) { Serilog.Log.Debug("WarmUpLocal: {E}", ex.Message); } });
                //
                // CoreModerationLog's record half stays unseeded, and NOT because a log is unavailable here
                // - ModerationLog is in Core and would construct fine. It hardcodes
                // SpecialFolder.ApplicationData (~/.config on Linux) while this head's user data
                // lives under CorePaths.UserData (~/.local/share), so seeding it would scatter the
                // CCBill record file into a second tree. That is a fix to the class, in a later
                // layer, not a seeding decision here.

                // CoreTutorial stays unseeded, and that is the honest state rather than a gap:
                // TutorialService and its twenty-two step lists are still in the WPF head, so this
                // head has no tour to describe. Unseeded answers "not active, no step, 0 of 0", so
                // TutorialOverlay draws nothing and every "bail while a tour is running" gate stays
                // open. Seeding it with anything would put a tour on screen that nothing drives.

                await step(0.75, "Loading achievements...");
                // Achievements: the Core engine over the same achievements.json WPF uses, seeded the
                // way WPF App.xaml.cs:384/:394 seeds the two unlock seams. Unlocked is raised on the
                // caller's thread; the popup hops to the UI thread as WPF's DispatcherHelper does.
                Achievements = new AchievementEngine(new AchievementStore(AchievementsPath));
                WireAchievementUnlocks(Achievements);
                WardrobeCatalog.ProgressProvider = () => Achievements?.Progress;
                CoreProgram.UnlockAchievementProvider = id => Achievements?.TryUnlock(id);
                // WPF App.xaml.cs: the invite ladder's badges and the invites wire (friends' proxy and
                // door; a sandbox reaches only a loopback CCP_FRIENDS_API_URL, else nothing is sent).
                Services.Invites.InviteRewards.UnlockedProvider = () => Achievements?.Progress?.UnlockedAchievements;
                Services.Invites.InviteRewards.TryUnlockProvider = id => Achievements?.TryUnlock(id) == true;
                Platform.FriendsHead.SeedInvites(
                    Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"), Environment.GetEnvironmentVariable(Platform.FriendsHead.EnvVar));
                CoreProgression.TrackBubbleCountResultProvider = correct => Achievements?.TrackBubbleCountResult(correct);
                CoreProgression.TrackBubbleCountGameStartedProvider = () => Achievements?.TrackBubbleCountGameStarted();
                CoreProgression.TrackBubbleCountCompletedProvider = () => Quests?.TrackBubbleCountCompleted();
                // WPF MantraService's App.Quests / App.Chaster reads (seeded in WPF App.xaml.cs the same way).
                CoreProgression.TrackMantraCompletedProvider = () => Quests?.TrackMantraCompleted();
                MantraService.ChasterNote = reps => { try { Platform.ChasterHead.Service?.Note("mantra", reps); } catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] mantra hook"); } };
                // WPF AchievementService.TrackVideoWatched -> App.Quests.TrackVideoMinutes.
                CoreProgression.TrackVideoWatchedProvider = sec => Quests?.TrackVideoMinutes(Achievements?.TrackVideoWatched(sec) ?? sec / 60.0);
                CoreProgression.TrackAttentionCheckProvider = passed =>
                {
                    if (passed) { Achievements?.TrackAttentionCheckPassed(isVideo: true); return; }
                    // WPF: TrackAttentionCheckFailed and TrackVideoAttentionCheckFailed each note "attention".
                    Achievements?.TrackAttentionCheckFailed();
                    ChasterNote("attention");
                    Achievements?.TrackVideoAttentionCheckFailed();
                    ChasterNote("attention");
                    // WPF App.Companion?.OnAttentionCheckFailed(): the Trainer's -25 XP.
                    if (Services.Companion.CompanionPerks.ApplyAttentionFailPenalty(CoreSettings.Current))
                    {
                        CoreSettings.Save();
                        Serilog.Log.Information("Trainer penalty: -25 XP for attention check fail. Current XP: {XP:F1}", CoreSettings.Current.ActiveCompanionProgress.CurrentXP);
                    }
                };
                SeedLevelAchievements(Achievements);
                StartQuests();

                // CoreProgram: its pack-video and roadmap providers stay unseeded - this head has no
                // ContentPackService or RoadmapService, so it answers "no pack videos, no roadmap".
                // HasPremiumProvider is seeded by AccountSeed.Seed(); NotifyProvider below, once the
                // shell's toast host exists.

                // CoreAccount and CoreEntitlement are seeded by Platform.AccountSeed.Seed() (the
                // Patreon/SubscribeStar gates, WPF App.xaml.cs:487-488). If the providers cannot be
                // built, Seed() returns false and both seams stay unseeded: signed out and NOT
                // entitled, because an entitlement seam that failed open would hand every Linux
                // user the paid tier.
                //
                // CoreSpeech is deliberately left unseeded: there is no speech engine on this head
                // yet, and the seam's unseeded answers (no mic, empty device list, NotProbed) are
                // exactly what that is. Seeding it with anything else would be a lie.

                // Open the session catalogue only on the ordinary desktop path. A failed load must
                // not leak the manager that may already contain built-ins into the shell: the custom
                // folder can fail after built-in files have been read, so publish only a fully loaded
                // candidate and keep the parameterless hardcoded rack as the fallback.
                SessionManager? sessions = null;
                try
                {
                    var candidate = CreateSessionManager();
                    candidate.LoadAllSessions();
                    sessions = candidate;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Session catalogue could not be loaded; showing built-in fallback");
                }

                await step(0.95, "Opening main window...");
                // WPF decides with `Welcomed && !FirstRunClaimedThisLaunch`: the shell's constructor
                // claims Welcomed on a fresh install, so read it before the shell exists.
                bool welcomed = Settings.Current.Welcomed;
                desktop.MainWindow = sessions is null
                    ? new Views.Windows.MainShellWindow()
                    : new Views.Windows.MainShellWindow(sessions);
                if (global::Avalonia.Controls.ControlExtensions.FindControl<global::Avalonia.Controls.Panel>(desktop.MainWindow, "NotificationHost") is { } toastHost)
                    Notifications.AttachHost(toastHost);

                // The two toast seams, as WPF App.xaml.cs:390 and :453 seed them.
                CoreProgram.NotifyProvider = (message, kind, duration) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    Notifications.Show(message,
                        Enum.TryParse<Helpers.NotificationType>(kind, out var t) ? t : Helpers.NotificationType.Info,
                        duration));
                // WPF App.xaml.cs:505: the Reconnect answer for gates (Core PatreonReconnectRule).
                TierGate.ReconnectIsTheAnswerProvider = Views.Windows.MainShellWindow.ReconnectIsTheAnswerNow;
                // Dropped: WPF's EmiDesk "premiumTeaseSeen" fire - no EmiDesk service on this head.
                var shell = (Views.Windows.MainShellWindow)desktop.MainWindow;
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                Sessions.Ticked += shell.OnSessionTick;
                Sessions.SessionLog.LogReady += shell.OnSessionLogReady;
                // WPF App.xaml.cs:529 (main fbe161de2): "See tiers" opens the vault gate card at the tier this door needs.
                CoreEntitlement.ShowDeniedHandler = verdict => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    shell.ShowTierDenied(verdict, Notifications));
                // WPF App.xaml.cs:509-511 (main 2e9080399). No Arcademy host here: unseeded, so its card stays hidden.
                Models.ExclusiveFeature.JustDropDoorProvider = SettingsPaletteIndex.JustDropDoorAvailable;
                Models.ExclusiveFeature.BreakoutFullProvider = () => TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed;
                // WPF MainWindow.xaml.cs:484 (the ? box rolled over or its override landed) and
                // OnPatreonTierChanged: both move the veils, the Play bands and the lapse pass.
                // WPF MainWindow.xaml.cs:486 / UpdatePatreonUI also repaint the vault (RefreshExclusivesTab).
                void RepaintVeils() => Dispatcher.UIThread.Post(() =>
                {
                    shell.RefreshEntitlementVeils(persist: true);
                    shell.RefreshExclusivesTab();
                    shell.RefreshNavPremiumTags();
                });
                dailyFree.TodayChanged += RepaintVeils;
                // WPF NavPremiumTags.cs:118 / Lab.cs:435: a spent or refunded pass moves the star and the vault.
                // Not RepaintVeils: that persists the lapse pass, and sign-in/out raises this at startup.
                IntakePass.PassStateChanged += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    shell.RefreshExclusivesTab();
                    shell.RefreshNavPremiumTags();
                });
                if (Platform.AccountSeed.Patreon is { } patreonSub) patreonSub.TierChanged += (_, _) => RepaintVeils();
                if (Platform.AccountSeed.SubscribeStar is { } substarSub) substarSub.TierChanged += (_, _) => RepaintVeils();
                Views.Controls.Invites.InvitePanel.ArmExpiry(); // WPF MainWindow.Patreon.cs: a running invite week's end repaints
                // OnLastWindowClose counts overlay windows too: closing the shell must take the
                // desktop overlays and their schedules down, or the process lives on UI-less.
                // WPF RequestExit (MainWindow.Launcher.cs:126) stops the engine first: the lock-card
                // schedule would otherwise keep the process alive.
                desktop.MainWindow.Closed += (_, _) =>
                {
                    Serilog.Log.Information("Shell closed: stopping the session and the engine");
                    CoreEngine.StoppedHook = null;   // the shell is gone; do not repaint it
                    Sessions.Ticked -= shell.OnSessionTick;
                    Sessions.SessionLog.LogReady -= shell.OnSessionLogReady;
                    Sessions.Stop();   // restores the pre-session settings before exit
                    CoreEngine.Stop();
                    StopDesktopOverlays();
                    Views.Windows.LockCardWindow.ForceCloseAll();
                };
                // Boot surface (WPF App.xaml.cs:3403-3419): the launcher, a game or the panel. A boot
                // into the launcher shows the panel unactivated and off the taskbar only so its Opened
                // work runs (WPF ShowHiddenForBoot), then RouteBoot tucks it away. A Lockdown in force
                // keeps the panel up (PanelStartsHidden); any routing failure shows it.
                try
                {
                    Views.Windows.LauncherWindow.Boot = Services.Launcher.LauncherBoot.Decide(desktop.Args, welcomed,
                        Settings.Current.HasAcceptedAgeVerification, Settings.Current.LauncherSkipToPanel);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[Launcher] boot decision failed; booting the panel");
                    Views.Windows.LauncherWindow.Boot = Services.Launcher.BootDecision.PanelFirst;
                }
                var boot = Views.Windows.LauncherWindow.Boot;
                bool panelHidden = Services.Launcher.LauncherBoot.PanelStartsHidden(boot, Views.Windows.MainShellWindow.LockdownActive);
                Serilog.Log.Information("[Launcher] boot surface {Surface} game {GameId}, panel shown: {Shown}", boot.Surface, boot.GameId, !panelHidden);
                if (panelHidden)
                {
                    bool activated = shell.ShowActivated, inTaskbar = shell.ShowInTaskbar;
                    shell.ShowActivated = false;
                    shell.ShowInTaskbar = false;
                    shell.BuildingHiddenForBoot = true;
                    void Route(object? s, EventArgs e)
                    {
                        shell.Opened -= Route;
                        Views.Windows.LauncherWindow.RouteBoot(shell);
                        // Restored only once the panel is tucked away, so it never reaches the taskbar.
                        shell.BuildingHiddenForBoot = false;
                        shell.ShowActivated = activated;
                        shell.ShowInTaskbar = inTaskbar;
                    }
                    shell.Opened += Route;
                }
                // Tray: restore, wake, Stop everything (the no-hotkey panic control) and the real Exit.
                try
                {
                    shell.CreateTray();
                    // WPF MainWindow.xaml.cs:3594: StartMinimized sends the shown window to the tray.
                    // Only with a tray host to come back through; without one the window stays up.
                    if (!panelHidden && Settings.Current.StartMinimized && shell.TrayHostPresent())
                    {
                        void ToTray(object? s, EventArgs e) { shell.Opened -= ToTray; shell.Hide(); }
                        shell.Opened += ToTray;
                    }
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Tray icon unavailable; X closes the app"); }
                // The panic key (WPF MainWindow.xaml.cs:363 installs its hook at startup the same way).
                shell.StartPanicKey();
                // Linux: one toast naming the distro's install command for any missing runtime library
                // (docs/avalonia-linux-install.md). dlopen off the UI thread; nothing when all load.
                Dispatcher.UIThread.Post(async () =>
                {
                    if (await System.Threading.Tasks.Task.Run(() => LinuxDependencies.Check(null)) is not { } missing) return;
                    Serilog.Log.Warning("Missing Linux dependencies: {Text}", missing.Text);
                    var command = missing.Command;
                    Notifications.Show(missing.Text, Helpers.NotificationType.Warning, TimeSpan.FromSeconds(20),
                        command is null ? null : "Copy command",
                        command is null ? null : () => _ = shell.Clipboard?.SetTextAsync(command));
                });
                // WPF App.xaml.cs:4858: pending-outcome report + background update check.
                Dispatcher.UIThread.Post(async () => await Platform.AppUpdater.StartupAsync(shell));
            }
        }

        /// <summary>Stops every desktop overlay and its schedule. <paramref name="final"/> is the shell
        /// closing; the tray's Stop everything passes false so the overlays can be started again.</summary>
        internal static void StopDesktopOverlays(bool final = true)
        {
            CoreFlash.Stop();
            CoreSubliminal.Stop();
            Views.Overlays.FlashOverlay.CloseAll(final);
            Views.Overlays.SubliminalWhisperShow.StopAll();
            Views.Overlays.SubliminalOverlay.CloseAll();
            Views.Overlays.BouncingTextOverlay.Stop();
            Views.Overlays.SpiralOverlay.CloseAll();   // WPF StopEngine -> App.Overlay.Stop(); panic and exit too
        }

        /// <summary>WPF App.OnAchievementUnlocked (App.xaml.cs:3815): one popup per unlock, shown at once.
        /// ponytail: no sound, no Discord webhook - neither service exists on this head.</summary>
        /// <summary>WPF ProgressionService.cs:285 (a level-up celebrates) and App.xaml.cs:3174 (retroactive,
        /// silent). Returns the LevelUp handler so a test can detach it.</summary>
        internal static Action<int> SeedLevelAchievements(AchievementEngine engine)
        {
            Action<int> onLevel = engine.CheckLevelAchievements;
            ProgressionBank.LevelUp += onLevel;
            var was = engine.SuppressPopups;
            engine.SuppressPopups = true;
            try { engine.CheckLevelAchievements(CoreSettings.Current.PlayerLevel); }
            finally { engine.SuppressPopups = was; }
            return onLevel;
        }

        /// <summary>WPF App.ShowWardrobeRewardToasts (App.xaml.cs:3987): every item gated on this achievement gets an
        /// ItemUnlockedPopup 900ms after the achievement popup, at most three, stacked upward.
        /// Inside the quiet window the column collapses to ONE Inbox row (WPF App.xaml.cs:4200).</summary>
        internal static void ShowWardrobeRewardToasts(Models.Achievement a)
        {
            var rewards = WardrobeCatalog.Items
                .Where(i => string.Equals(i.RequiredAchievementId, a.Id, StringComparison.OrdinalIgnoreCase)).Take(3).ToList();
            if (rewards.Count == 0) return;
            Serilog.Log.Information("Achievement '{Id}' unlocked {Count} wardrobe item(s); queuing item toast(s)", a.Id, rewards.Count);
            void ShowAll()
            {
                for (int i = 0; i < rewards.Count; i++)
                {
                    try { new Views.Windows.ItemUnlockedPopup(rewards[i], i).Show(); }
                    catch (Exception ex) { Serilog.Log.Error(ex, "Failed to show item unlocked popup for: {Id}", rewards[i].Id); }
                }
            }
            Dispatcher.UIThread.Post(() => DispatcherTimer.RunOnce(() => Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
            {
                Key = "wardrobe-unlock:" + a.Id,
                Glyph = "👗",
                Title = rewards.Count == 1 ? "A new wardrobe item is yours" : rewards.Count + " new wardrobe items are yours",
                Summary = string.Join(", ", rewards.Select(static r => r.Name)),
                Open = ShowAll,
            }), TimeSpan.FromMilliseconds(900)));
        }

        /// <summary>WPF App.OnAchievementUnlocked (App.xaml.cs:4160-4205): popup, wardrobe item toasts,
        /// achievement sound, opt-in community post - in that order. <paramref name="discord"/> is for tests;
        /// startup leaves it null and the seeded account is read at unlock time.</summary>
        internal static void WireAchievementUnlocks(AchievementEngine engine, DiscordAccount? discord = null)
        {
            engine.Unlocked += (_, a) => Dispatcher.UIThread.Post(() => ShowAchievementPopup(a));
            engine.Unlocked += (_, a) => ShowWardrobeRewardToasts(a);
            engine.Unlocked += (_, a) => AnnounceAchievement(a, discord ?? Platform.AccountSeed.Discord);
        }

        /// <summary>WPF PlayAchievementSound + the DiscordShareAchievements post (App.xaml.cs:4180-4205).
        /// WPF plays SystemSounds.Asterisk; Linux has no stock one, so a bundled chime (quests use chime1).
        /// The name is always CustomDisplayName-first for privacy, as WPF.</summary>
        internal static Task<bool>? AnnounceAchievement(Models.Achievement a, DiscordAccount? discord)
        {
            CoreAudio.PlayOneShot(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "chime2.mp3"),
                Math.Clamp(CoreSettings.Current.MasterVolume / 100f, 0f, 1f), "achievement");
            if (!CoreSettings.Current.DiscordShareAchievements)
            {
                Serilog.Log.Information("Achievement '{Name}' not shared to Discord: DiscordShareAchievements is off", a.Name);
                return null;
            }
            var task = discord?.SendAchievementWebhookAsync(a,
                discord.CustomDisplayName ?? Platform.AccountSeed.Patreon?.DisplayName ?? "Someone",
                CoreAccount.UnifiedUserId, () => CoreSettings.Current.AuthToken, DiscordAccount.ModThemeId(CoreMods.ActiveModId));
            task?.ContinueWith(t =>
            {
                if (t.IsFaulted || t.IsCanceled || !t.Result)
                    Serilog.Log.Warning(t.Exception?.GetBaseException(), "Achievement '{Name}' did NOT post to Discord", a.Name);
            }, TaskContinuationOptions.ExecuteSynchronously);
            return task;
        }

        internal static void ShowAchievementPopup(Models.Achievement a)
        {
            try { new Views.Windows.AchievementPopup(a.Name, a.FlavorText, a.ImageName).Show(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "Failed to show achievement popup for: {Name}", a.Name); }
        }

        private string ResolveEffectiveAssetsPath()
        {
            var customPath = Settings?.Current.CustomAssetsPath;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                if (Directory.Exists(customPath)) return customPath;
                if (Interlocked.Exchange(ref _warnedMissingCustomAssetsPath, 1) == 0)
                    Serilog.Log.Warning("CustomAssetsPath '{Path}' does not exist — falling back to default assets folder. Imports/extractions will go to the default location.", customPath);
            }
            return _defaultAssetsPath ??= DefaultAssetsPath(OperatingSystem.IsLinux(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), CorePaths.UserData,
                sandboxed: !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")));
        }

        private string? _defaultAssetsPath;

        /// <summary>
        /// The media folder used while CustomAssetsPath is empty. Windows keeps WPF's
        /// UserData/assets; Linux uses ~/ccp media (user request), created with its subfolders on
        /// first use. A Linux profile that already has files in UserData/assets keeps using it -
        /// nothing is moved and nothing switches silently. A CCP_USERDATA_DIR sandbox (tests, live
        /// checks), an unknown home or an unreadable legacy folder also keep UserData/assets, so
        /// nothing outside the sandbox is created. Decided once per process.
        /// </summary>
        internal static string DefaultAssetsPath(bool isLinux, string home, string userData, bool sandboxed)
        {
            var legacy = Path.Combine(userData, "assets");
            if (!isLinux || sandboxed || string.IsNullOrWhiteSpace(home)) return legacy;
            try
            {
                if (Directory.Exists(legacy) && Directory.EnumerateFiles(legacy, "*",
                        new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Any())
                {
                    Serilog.Log.Information("Media folder: keeping {Legacy} (it already holds files) instead of ~/ccp media", legacy);
                    return legacy;
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Media folder: could not read {Legacy}; keeping it", legacy);
                return legacy;
            }
            var media = Path.Combine(home, "ccp media");
            CorePaths.EnsureCustomAssetsDirectories(media);
            return media;
        }

        /// <summary>The exit save, after Takeover hands back what a pulse borrowed - else a boosted
        /// pink tint is saved as the user's own (WPF StopEngine cancels pulses before the save).</summary>
        internal static void SaveSettingsOnExit(Views.Windows.MainShellWindow? shell, Action save)
        {
            try { shell?.CancelAutonomyPulses(); } catch (Exception ex) { Serilog.Log.Debug("Exit pulse cancel failed: {E}", ex.Message); }
            try { save(); }
            catch { /* SettingsService logs save failures; exit must continue */ }
        }

        /// <summary>Exit path (tray Exit and every other shutdown): close any MantraWindow, whose OnClosed
        /// stops the drone and ends the session, end the service as WPF App.OnExit's Mantra?.Dispose(),
        /// and delete the synthesised WAVs.</summary>
        internal static void StopMantra(System.Collections.Generic.IEnumerable<global::Avalonia.Controls.Window> windows)
        {
            foreach (var w in windows.OfType<Views.Windows.MantraWindow>().ToList())
                try { w.Close(); } catch (Exception ex) { Serilog.Log.Debug(ex, "MantraWindow close on exit"); }
            Mantra.Dispose();
            Platform.ToneWav.DeleteFiles();
        }

        private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            if (Interlocked.Exchange(ref _exitHandled, 1) != 0) return;
            _exiting = true;

            // WPF App.OnExit:5965: haptics FIRST and synchronously (bounded ~2 s). A Lovense level has no
            // server-side watchdog, so a toy not countermanded here keeps running after the app is gone.
            try { CoreHaptics.Service?.ShutdownStop(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Haptics shutdown stop failed"); }

            try { (((IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!).MainWindow as Views.Windows.MainShellWindow)?.Tray?.Dispose(); } catch { }

            // Before libvlc goes: close the Mantra Lab (stops and disposes its drone), then its temp WAVs.
            try { StopMantra(((IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!).Windows); } catch { }

            // Restore any app we ducked; a pending Unduck would otherwise die with the process.
            try { Platform.LibVlcAudio.Instance?.Shutdown(); } catch { }
            try { Platform.LayeredAudio.Instance?.Shutdown(); } catch { }
            try { ReleaseContent?.Dispose(); } catch { }
            try { Platform.ChasterHead.Service?.Dispose(); } catch { }

            // WPF App.OnExit: a best-effort final push, capped at 2 s (off the UI thread, as WPF's Task.Run).
            try { if (Platform.AccountSeed.Sync is { Loaded: true } sync) System.Threading.Tasks.Task.Run(() => sync.PushAsync("shutdown")).Wait(TimeSpan.FromSeconds(2)); }
            catch { /* never block exit */ }
            Platform.AccountSeed.Sync?.StopHeartbeat();

            // Flush while the dispatcher is still usable. In particular, a serialize retry from a
            // background save must not see the shutdown-safe drop provider below.
            var shell = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as Views.Windows.MainShellWindow;
            SaveSettingsOnExit(shell, () => Settings?.SaveImmediate());

            // WPF AchievementService.Dispose saves synchronously; only when dirty here, so an idle exit
            // never rewrites the file (or rotates its .bak) - it may be shared with the WPF head.
            try { if (Achievements is { IsDirty: true } a) a.Save(); } catch { /* the store logs write failures */ }
            try { Quests?.Dispose(); } catch { /* WPF App.OnExit:6104; saves only when dirty */ }
            try { MediaHistory?.Dispose(); } catch { /* WPF App.OnExit:6231; flushes the final entries */ }
            try { (Platform.FriendsHead.Service as IDisposable)?.Dispose(); } catch { /* WPF App.OnExit: the friends poll stops */ }
            try { Brain?.Dispose(); } catch { /* WPF App.OnExit:6121; flushes the turn log */ }
            try { Ai?.Dispose(); } catch { /* WPF App.OnExit:6250 (#629): unloads the local Ollama model */ }
            // WPF App.OnExit:6013/6173: zero the toys first (a Lovense level has no timeout), then dispose.
            try { CoreHaptics.Service?.Dispose(); } catch { }
            try { Views.Overlays.BlinkTrainerSession.Stop(); } catch { /* WPF Application.Exit += Stop */ }
            try { Views.Chaos.ChaosRunHost.ForceShutdown(); } catch { /* WPF App.OnExit:6241 Chaos.ForceShutdown */ }
            try { Platform.WebcamTracker.Instance.Stop(); } catch { /* WPF App.OnExit:6185 Webcam.Dispose */ }

            // Roadmap is lazy: do not construct it merely to dispose it on a profile that never
            // opened the quest page.
            try { Views.Windows.MainShellWindow.DisposeRoadmapIfCreated(); }
            catch { /* one service cannot prevent the head from exiting */ }

            DescentCountdown?.Dispose();   // its pool timer outlives the dispatcher otherwise
            ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.Instance.Stop();   // same
            _desktopDispatch?.Stop();
        }
    }
}
