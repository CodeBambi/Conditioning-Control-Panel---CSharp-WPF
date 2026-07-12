using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Services;
using ConditioningControlPanel.Avalonia.Services.AttentionCheck;
using ConditioningControlPanel.Avalonia.Services.Bark;
using ConditioningControlPanel.Avalonia.Services.BouncingText;
using ConditioningControlPanel.Avalonia.Services.BubbleCount;
using ConditioningControlPanel.Avalonia.Services.Commands;
using ConditioningControlPanel.Avalonia.Services.Companion;
using ConditioningControlPanel.Avalonia.Services.Flash;
using ConditioningControlPanel.Avalonia.Services.Haptics;
using ConditioningControlPanel.Avalonia.Services.InteractionQueue;
using ConditioningControlPanel.Avalonia.Services.KeywordTriggers;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Avalonia.Services.LockCard;
using ConditioningControlPanel.Avalonia.Services.Lockdown;

using ConditioningControlPanel.Avalonia.Services.MindWipe;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Avalonia.Services.Progression;
using ConditioningControlPanel.Avalonia.Services.Quiz;
using ConditioningControlPanel.Avalonia.Services.Autonomy;
using ConditioningControlPanel.Avalonia.Services.Subliminal;
using ConditioningControlPanel.Avalonia.Services.Theme;
using ConditioningControlPanel.Avalonia.Services.Webcam;
using ConditioningControlPanel.Avalonia.Services.BlinkTrainer;
using ConditioningControlPanel.Avalonia.Services.Sessions;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Core.Services.SessionLog;
using ConditioningControlPanel.Core.Services.BugReport;
using ConditioningControlPanel.Avalonia.Services.Moderation;
using ConditioningControlPanel.Avalonia.Services.Speech;
using ConditioningControlPanel.Core.Services.Speech;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Services.Avatar;
using ConditioningControlPanel.Avalonia.Services.Auth;
using ConditioningControlPanel.Core.Services.Avatar;
using ConditioningControlPanel.Core.Services.Auth;
using ConditioningControlPanel.Avalonia.Services.Content;
using ConditioningControlPanel.Core.Services.Content;
using ConditioningControlPanel.Core.Services.RemoteControl;
using ConditioningControlPanel.Avalonia.Services.RemoteControl;
using ConditioningControlPanel.Avalonia.Services.Video;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;
using ConditioningControlPanel.Core.Services.AvailableSubjects;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Core.Services.AIService.Enrichment;
using ConditioningControlPanel.Core.Services.Commands;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Quiz;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.Roadmap;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Mantra;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Core.Services.Webcam;
using ConditioningControlPanel.Core.Services.BlinkTrainer;
using ConditioningControlPanel.Core.Services.Scheduler;
using ConditioningControlPanel.Core.Services.Update;
using ConditioningControlPanel.Core.Services.Catalogue;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Deeper;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ConditioningControlPanel.Avalonia;

/// <summary>
/// Dependency-injection registration helpers for the Avalonia head.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Avalonia platform seam implementations and portable Core services
    /// used by the Conditioning Control Panel cross-platform shell.
    /// </summary>
    public static IServiceCollection ConfigureCoreServices(this IServiceCollection services)
    {
        // Platform seams - singletons unless they own per-control or per-window state.
        services.AddSingleton<IAppEnvironment, AvaloniaAppEnvironment>();
        services.AddSingleton<LibVLC>(_ =>
        {
            LibVLCSharp.Shared.Core.Initialize();
            return new LibVLC();
        });
        services.AddSingleton<ILibVlcProvider, LibVlcProvider>();
        services.AddSingleton<IScreenProvider, AvaloniaScreenProvider>();
        services.AddSingleton<IAssetLoader, AvaloniaAssetLoader>();
        services.AddSingleton<IPointerState, AvaloniaPointerState>();
        services.AddSingleton<ISfxPlayer, AvaloniaSfxPlayer>();
        services.AddSingleton<IMouseHook, AvaloniaMouseHook>();
        services.AddSingleton<IBubbleService, AvaloniaBubbleService>();
        services.AddSingleton<IBrowserHost, AvaloniaBrowserHost>();
        services.AddSingleton<ISecretStore, AvaloniaSecretStore>();
        services.AddSingleton<ISingleInstanceService, AvaloniaSingleInstanceService>();
        services.AddSingleton<IUpdateInstaller, AvaloniaUpdateInstaller>();
        services.AddSingleton<IWallpaperProvider, AvaloniaWallpaperProvider>();
        services.AddSingleton<ISystemAudioDucker, AvaloniaSystemAudioDucker>();
        services.AddSingleton<IAudioDeviceService, AvaloniaAudioDeviceService>();
        services.AddSingleton<IAudioPlayer, AvaloniaAudioPlayer>();
        services.AddSingleton<IHapticsService, AvaloniaHapticsService>();

        services.AddSingleton<IInputHook, AvaloniaInputHook>();
        services.AddSingleton<IHotkeyProvider, AvaloniaHotkeyProvider>();
        services.AddSingleton<IWindowChrome, AvaloniaWindowChrome>();
        services.AddSingleton<ITrayIcon, AvaloniaTrayIcon>();
        services.AddSingleton<IFilePickerService, DesktopFilePickerService>();
        services.AddSingleton<IPlatformCapabilities, AvaloniaPlatformCapabilities>();

        // Dialog service needs a way to reach the current TopLevel at call time.
        services.AddSingleton<IDialogService>(sp => new AvaloniaDialogService(
            () => GetCurrentTopLevel()));

        // Overlay surface is a Window, so a new instance per consumer is safer than a singleton.
        services.AddTransient<IOverlaySurface, AvaloniaOverlaySurface>();

        // IVideoSurface requires a VideoView instance at construction time and is therefore
        // not registered globally. Consumers should create AvaloniaVideoSurface directly:
        //   var surface = new AvaloniaVideoSurface(videoView);

        // Unified compositor engine (replaces multi-window overlay architecture)
        services.AddSingleton<CompositorEngine>();

        // Offline speech recognition seam. Default = no-op (unavailable); the Windows head
        // overrides with the real Vosk/NAudio implementation in App.ConfigurePlatformServices.
        services.AddSingleton<ISpeechRecognitionService, NullSpeechService>();

        // Bark-manifest slice: voiced lines for the "Hey Bambi" wake ack + voice-command
        // confirmations (loads the active mod's bark ruleset; PickVoiceLine + ResolveModAudio).
        services.AddSingleton<ConditioningControlPanel.Core.Services.Bark.IBarkManifestService,
                              ConditioningControlPanel.Core.Services.Bark.BarkManifestService>();

        // BARK-1 slice 1: the ported bark DECISION engine (WPF Services/Companion/BarkService.cs;
        // contract docs/bark-engine-contract.md). Registered but NOT started — trigger wiring is
        // slice 3; the existing AvaloniaBarkService NotifyChaos*→BarkRequested bare-string path stays
        // the live consumer until then. Factory lambdas because the engine ctor takes optional seams.
        //
        // BARK-1 slice 2: the AvatarTube-backed IBarkSpeaker REPLACES the NullBarkSpeaker default —
        // decided barks now route to the speech bubble (Giggle/GigglePriority), with the mute-egg,
        // {0} focused-app substitution and self-echo guard (WPF BarkService.cs:1578-1628). Optional
        // seams resolve to null on heads without them; the speaker degrades safely.
        services.AddSingleton<ConditioningControlPanel.Core.Services.Bark.IBarkSpeaker>(sp =>
            new AvatarBarkSpeaker(
                sp.GetService<IAvatarWindowService>(),
                sp.GetService<ConditioningControlPanel.Core.Services.Awareness.IAwarenessService>(),
                sp.GetService<ConditioningControlPanel.Core.Platform.IForegroundWindowTitleProvider>(),
                sp.GetService<IKeywordTriggerService>(),
                sp.GetService<ISettingsService>(),
                logger: sp.GetService<Microsoft.Extensions.Logging.ILogger<AvatarBarkSpeaker>>()));
        services.AddSingleton<ConditioningControlPanel.Core.Services.Bark.IBarkGateSignals>(sp =>
            new ConditioningControlPanel.Core.Services.Bark.BarkGateSignals(
                sp.GetService<IAvatarWindowService>()));
        services.AddSingleton<ConditioningControlPanel.Core.Services.Bark.IBarkLiveFields>(sp =>
            new ConditioningControlPanel.Core.Services.Bark.BarkLiveFields(
                sp.GetService<ISettingsService>(),
                sp.GetService<IVideoInfo>(),
                sp.GetService<IWebcamService>(),
                sp.GetService<ISessionService>()));
        services.AddSingleton(sp =>
            new ConditioningControlPanel.Core.Services.Bark.BarkEngine(
                sp.GetService<ISettingsService>(),
                sp.GetService<ConditioningControlPanel.Core.Services.Bark.IBarkSpeaker>(),
                sp.GetService<ConditioningControlPanel.Core.Services.Bark.IBarkLiveFields>(),
                sp.GetService<ConditioningControlPanel.Core.Services.Bark.IBarkGateSignals>(),
                sp.GetService<IModService>(),
                sp.GetService<ConditioningControlPanel.Core.Services.Bark.IBarkManifestService>(),
                logger: sp.GetService<Microsoft.Extensions.Logging.ILogger<ConditioningControlPanel.Core.Services.Bark.BarkEngine>>()));

        // Spoken-mantra dataset for the Takeover "say it for me" fallback. Audio-duration provider
        // defaults to no-op; the Windows head overrides it with NAudio.
        services.AddSingleton<ConditioningControlPanel.Core.Platform.IAudioDurationProvider,
                              ConditioningControlPanel.Core.Platform.NullAudioDurationProvider>();
        services.AddSingleton<ConditioningControlPanel.Core.Services.Mantra.IMantraVoiceService,
                              ConditioningControlPanel.Core.Services.Mantra.MantraVoiceService>();

        // Deeper "speak" effect host (cue + offline recognition + feedback). Flows into the
        // RealActionDispatcher's optional ISpeakPromptHost param.
        services.AddSingleton<ConditioningControlPanel.Core.Services.Deeper.ISpeakPromptHost,
                              ConditioningControlPanel.Avalonia.Services.Deeper.AvaloniaSpeakPromptHost>();

        // Core services that are safe to register as singletons today.
        services.AddSingleton<IPromptService, PromptService>();
        services.AddSingleton<IPromptValidator, PromptValidator>();
        services.AddSingleton<IModerationGuard, ModerationGuard>();
        services.AddSingleton<IOllamaSetupService, OllamaSetupService>();
        services.AddLogging(builder => builder.AddSerilog());

        services.AddSingleton<VideoMetadataCache>();
        // Companion AI: provider strategy selects Cloud (CoreAiService) / Local (LocalAiService) /
        //        OpenAI-compatible (OpenAiService) from CompanionPrompt.AiProvider. Cloud = codebambi-proxy
        //        V2; Local = Ollama + persistent history; OpenAI = OpenAI-compatible transport + ISecretStore
        //        key + the moderation sandwich the WPF provider omits. All 5 reactions on every provider.
        //        AI command execution (AllowAiToControlEffects): the dispatcher (AiCommandService) is
        //        registered below; command dispatch is WIRED for LocalAiService (LocalAiService.cs:218-221)
        //        and OpenAiService (OpenAiService.cs:402-405, Phase 3a). Cloud (CoreAiService) intentionally
        //        omits dispatch — WPF parity (the WPF cloud AiService.cs path has no command dispatch).
        //        The OpenAI key-entry UI is a documented follow-up.
        services.AddSingleton<ISystemPromptBuilder, SystemPromptBuilder>();
        services.AddSingleton<IAiResponseParser>(sp =>
        {
            // AiResponseParser needs a fallback phrase for when CleanText is empty after sanitize.
            var mods = sp.GetService<IModService>();
            return new AiResponseParser(() =>
            {
                var phrases = mods?.GetPhrases("Idle");
                return phrases is null || phrases.Length == 0 ? "Good girl~" : phrases[0];
            });
        });
        // AI-triggers-effects dispatcher (AllowAiToControlEffects). Resolves LAZILY to IAiService via
        // IServiceProvider (providers inject IAiCommandService? optional → no ctor cycle). Phase 3a.
        services.AddSingleton<IAiCommandService, AiCommandService>();
        // AI-7: the Companion-tab "Live actions" feed — one DI singleton shared by the producer
        // (AiCommandService appends) and the consumer (CompanionTabViewModel binds). The port's
        // DI equivalent of WPF's static App.AiLiveActions collection.
        services.AddSingleton<IAiLiveActionsFeed, AiLiveActionsFeed>();
        services.AddSingleton<CoreAiService>();
        services.AddSingleton<LocalAiService>();
        services.AddSingleton<OpenAiService>();
        services.AddSingleton<IAiService, AiServiceStrategy>();
        services.AddTransient<IQuizService, QuizService>();

        // Settings, session and achievement services (extracted to Core).
        services.AddSingleton<ISettingsBackupProvider, AvaloniaSettingsBackupProvider>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISkillTreeService, AvaloniaSkillTreeService>();
        services.AddSingleton<IProgressionService, AvaloniaProgressionService>();
        services.AddSingleton<IModService, AvaloniaModService>();
        services.AddSingleton<AvaloniaModResourceResolver>();
        services.AddSingleton<IUserIdentityProvider, AvaloniaUserIdentityProvider>();
        services.AddSingleton<ISeasonRecapService, AvaloniaSeasonRecapService>();
        services.AddSingleton<ILeaderboardService>(sp =>
        {
            var version = UpdateService.GetCurrentVersion().ToString();
            return new Core.Services.Progression.LeaderboardService(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IUserIdentityProvider>(),
                version,
                sp.GetRequiredService<ILogger<LeaderboardService>>(),
                sp.GetService<ISeasonRecapService>());
        });
        // Cloud profile sync (ProfileSync slice 7 live wiring, shipped 4f051ab0/80e1442; see docs/avalonia-ui-parity-matrix.md row 1).
        // Owns progression push/pull (/v2/user/sync IS the leaderboard submit), cloud settings
        // backup/restore, server-authoritative actions (purchase/oopsie/display-name) and GDPR
        // export. Auth stays in IV2AuthService (plan §6). SINGLE heartbeat owner: this service's
        // 120s timer (the dead one-arg AvaloniaV2AuthService.SendHeartbeatAsync seam — no timer, no
        // callers — was removed, IMP-7).
        // Sibling seams resolve via GetService (all-optional ctor) like the LeaderboardService
        // precedent above; SettingsService reaches it only lazily through ISettingsBackupProvider,
        // so no construction cycle exists.
        services.AddSingleton<IProfileSyncService>(sp => new Core.Services.Settings.ProfileSyncService(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<Core.Services.Settings.ProfileSyncService>>(),
            sp.GetService<ISessionService>(),
            sp.GetService<IAchievementService>(),
            sp.GetService<IQuestService>(),
            sp.GetService<IProgressionService>(),
            sp.GetService<ISkillTreeService>()));
        services.AddSingleton<ICatalogueService>(sp =>
        {
            var version = UpdateService.GetCurrentVersion().ToString();
            return new Core.Services.Catalogue.CatalogueService(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IUserIdentityProvider>(),
                version,
                sp.GetRequiredService<ILogger<CatalogueService>>());
        });
        services.AddSingleton<AvaloniaThemeService>();
        services.AddSingleton<IInteractionQueueService, AvaloniaInteractionQueueService>();
        services.AddSingleton<IBubbleCountService, AvaloniaBubbleCountService>();
        services.AddSingleton<IFlashService, AvaloniaFlashService>();
        services.AddSingleton<ILockCardService, AvaloniaLockCardService>();
        services.AddSingleton<ISubliminalService, AvaloniaSubliminalService>();
        services.AddSingleton<IVideoService, AvaloniaVideoService>();
        services.AddSingleton<IMindWipeService, AvaloniaMindWipeService>();
        services.AddSingleton<IBouncingTextService, AvaloniaBouncingTextService>();
        services.AddSingleton<IOverlayService, AvaloniaOverlayService>();
        services.AddSingleton<IWebcamService, AvaloniaWebcamService>();
        services.AddSingleton<IBlinkTrainerService, AvaloniaBlinkTrainerService>();
        services.AddSingleton<IGazeFocusService, AvaloniaGazeFocusService>();
        services.AddSingleton<IGazeDebugCursorService, AvaloniaGazeDebugCursorService>();
        services.AddSingleton<IPopQuizService, AvaloniaPopQuizService>();
        services.AddSingleton<IAttentionCheckService, AvaloniaAttentionCheckService>();
        services.AddSingleton<IModerationCounter, ModerationCounter>();
        services.AddSingleton<IModerationLog, AvaloniaModerationLog>();
        services.AddSingleton<ISessionPlatformBridge, AvaloniaSessionPlatformBridge>();
        services.AddSingleton<SessionFileService>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<IIntensityRampService, IntensityRampService>();
        services.AddSingleton<IRoadmapService, AvaloniaRoadmapService>();
        services.AddSingleton<IQuestService, QuestService>();
        services.AddSingleton<IQuestDefinitionService, QuestDefinitionService>();
        services.AddSingleton<IMantraService, MantraService>();
        services.AddSingleton<IAchievementService, AchievementService>();

        // Auth, Chaos, avatar, bark, video and session-log services for the Avalonia head
        // (facades over the DI-registered Core services, not stubs).
        services.AddSingleton<IV2AuthService, AvaloniaV2AuthService>();
        services.AddSingleton<IV2DeviceCodeService, AvaloniaV2DeviceCodeService>();
        services.AddSingleton<AvaloniaPatreonProvider>();
        services.AddSingleton<AvaloniaDiscordProvider>();
        services.AddSingleton<AvaloniaSubscribeStarProvider>();
        services.AddSingleton<IAuthProvider>(sp => sp.GetRequiredService<AvaloniaPatreonProvider>());
        services.AddSingleton<IAuthProvider>(sp => sp.GetRequiredService<AvaloniaDiscordProvider>());
        services.AddSingleton<IAuthProvider>(sp => sp.GetRequiredService<AvaloniaSubscribeStarProvider>());
        services.AddSingleton<IChaosService, AvaloniaChaosService>();
        services.AddSingleton<IAvatarWindowService, AvaloniaAvatarWindowService>();
        services.AddSingleton<IBarkService, AvaloniaBarkService>();
        services.AddSingleton<IVideoInfo, AvaloniaVideoInfo>();
        services.AddSingleton<IMainWindowService, AvaloniaMainWindowService>();
        // Screen-shake seam (Q15): TranslateTransform jitter on the main window content root,
        // ported from WPF Services/UI/ScreenShakeService.cs. No-ops safely when headless. The
        // impl uses only cross-platform Avalonia APIs, so CCP.Avalonia owns it directly (like
        // AvaloniaOverlayService/AvaloniaFlashService) rather than a per-head override.
        services.AddSingleton<IScreenShakeService, AvaloniaScreenShakeService>();
        // AI-1: portable window-awareness engine (WPF Services/UI/WindowAwarenessService.cs).
        // Depends on the optional IForegroundWindowTitleProvider head seam; on heads without
        // one (Linux/macOS today) Start() no-ops and the feature stays off gracefully.
        services.AddSingleton<IAwarenessService, AwarenessService>();
        services.AddSingleton<ISessionLogService, SessionLogService>();

        services.AddSingleton<IKeywordTriggerPresetService, AvaloniaKeywordTriggerPresetService>();
        services.AddSingleton<IKeywordTriggerService, AvaloniaKeywordTriggerService>();
        services.AddSingleton<IKeywordHighlightService, AvaloniaKeywordHighlightService>();
        services.AddSingleton<ICompanionPhraseService, AvaloniaCompanionPhraseService>();
        services.AddSingleton<ICommunityPromptService, AvaloniaCommunityPromptService>();
        services.AddSingleton<ICompanionService, AvaloniaCompanionService>();
        services.AddSingleton<IContentPackService, AvaloniaContentPackService>();
        services.AddSingleton<IAvatarPortraitService, AvaloniaAvatarPortraitService>();
        services.AddSingleton<IAvailableSubjectsService, AvailableSubjectsService>();
        services.AddSingleton<IRemoteCommandExecutor, AvaloniaRemoteCommandExecutor>();
        services.AddSingleton<IRemoteStatusProvider, AvaloniaRemoteStatusProvider>();
        services.AddSingleton<IRemoteControlService, RemoteControlService>();
        services.AddSingleton<ILockdownService, AvaloniaLockdownService>();
        services.AddSingleton<IAutonomyService, AvaloniaAutonomyService>();
        services.AddSingleton<ISessionEffectOrchestrator, AvaloniaSessionEffectOrchestrator>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IStartupRegistration, AvaloniaStartupRegistration>();
        services.AddSingleton<IBugReportService, BugReportService>();
        services.AddSingleton<ChaosCrashSentinel>();

        // Deeper enhancement runtime + audio waveform cache.
        services.AddTransient<RealActionDispatcher>();
        services.AddSingleton<EnhancementHostService>();
        services.AddSingleton<IAudioWaveformProvider, NullAudioWaveformProvider>();
        services.AddSingleton<AudioWaveformCache>();

        // ViewModels
        // Singleton: sole owner of app-lifetime session wiring (effect start/stop, XP grant,
        // panic) on singleton services; a second instance would double-subscribe those events.
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<AppInfoTabViewModel>();
        services.AddTransient<SettingsTabViewModel>();
        services.AddTransient<PresetsTabViewModel>();
        services.AddTransient<PresetIOTabViewModel>();
        services.AddTransient<QuestsTabViewModel>();
        services.AddTransient<LevelFeaturesTabViewModel>();
        services.AddTransient<PatreonTabViewModel>();
        services.AddTransient<DeeperTabViewModel>();
        services.AddTransient<DeeperHubTabViewModel>();
        services.AddTransient<EnhancementsTabViewModel>();
        services.AddTransient<DeeperSubmissionsTabViewModel>();
        services.AddTransient<CompanionHubTabViewModel>();
        services.AddTransient<CompanionTabViewModel>();
        services.AddTransient<BambiTakeoverTabViewModel>();
        services.AddTransient<HapticsTabViewModel>();
        services.AddTransient<AwarenessTabViewModel>();
        services.AddSingleton<LabTabViewModel>();
        services.AddTransient<BlinkTrainerTabViewModel>();
        services.AddTransient<SheListeningTabViewModel>();
        services.AddTransient<RemoteControlTabViewModel>();
        services.AddTransient<AvailableSubjectsTabViewModel>();
        services.AddTransient<ProfileTabViewModel>();
        services.AddTransient<LockdownTabViewModel>();
        services.AddTransient<AssetsTabViewModel>();
        services.AddTransient<CatalogueSubmissionsTabViewModel>();
        services.AddTransient<AchievementsTabViewModel>();
        services.AddTransient<LeaderboardTabViewModel>();
        services.AddTransient<MarqueeTabViewModel>();
        services.AddTransient<AnimationsTabViewModel>();

        return services;
    }

    private static TopLevel? GetCurrentTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            return window;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime single
            && single.MainView is { } view)
        {
            return TopLevel.GetTopLevel(view);
        }

        return null;
    }
}
