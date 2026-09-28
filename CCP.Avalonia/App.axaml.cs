using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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

        /// <summary>The achievement engine (achievements.json), or null on the headless render path.
        /// Local-only: no sync, no streak writes, no ResetProgress on this head (oracle-achievements.md).</summary>
        internal static AchievementEngine? Achievements { get; private set; }

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

        public override void OnFrameworkInitializationCompleted()
        {

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
                Settings = new SettingsService();
                CoreSettings.ServiceProvider = () => Settings;
                CorePaths.EffectiveAssetsProvider = ResolveEffectiveAssetsPath;
                LocalizationManager.Instance.SetLanguage(Settings.Current.Language);

                // The lock-card surface seam. The schedule and the no-repeat phrase rotation are in
                // Core now (LockCardScheduler); this is the half that draws, and on this head that
                // is LockCardWindow.ShowOnAllMonitors - which already refuses to stack a second
                // card and already falls a voice-mode card back to typing, honestly, because there
                // is no recognition seam here yet.
                //
                // The hop is explicit because CoreDispatch is unseeded on this head, so the
                // scheduler's tick arrives on a thread-pool thread. Everything after the hop -
                // including the phrase draw - therefore runs on the UI thread, which is what keeps
                // the scheduler's rotation state single-threaded.
                CoreLockCard.ShowHandler = isTest => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var phrase = LockCardScheduler.Instance.PickPhrase(LockCardScheduler.EnabledPhrases());
                    if (phrase is null) return;   // no enabled phrases: nothing to lock behind
                    var s = CoreSettings.Current;
                    Views.Windows.LockCardWindow.ShowOnAllMonitors(
                        phrase, s.LockCardRepeats, s.LockCardStrict, isTest, s.LockCardVoiceMode);
                });

                // The ambient flash surface. CoreFlash owns the rhythm; a burst needs any attached
                // visual to reach Screens, and the main window is the one that always is.
                CoreFlash.IsBusyProvider = () => Views.Overlays.FlashOverlay.IsBusy;
                CoreFlash.ShowProvider = () =>
                {
                    if (desktop.MainWindow is { } host) Views.Overlays.FlashOverlay.TriggerOnce(host);
                };

                // Subliminal and bouncing-text surfaces. Core owns the schedule / the motion;
                // these draw on click-through overlays (Views/Overlays), hosted like the flash.
                CoreSubliminal.ShowProvider = text =>
                {
                    if (desktop.MainWindow is { } host) Views.Overlays.SubliminalOverlay.Show(host, text);
                };
                CoreSubliminal.RunStateChanged = running =>
                {
                    if (!running) global::Avalonia.Threading.Dispatcher.UIThread.Post(Views.Overlays.SubliminalOverlay.CloseAll);
                };
                CoreBouncingText.StartAction = () =>
                {
                    if (desktop.MainWindow is { } host) Views.Overlays.BouncingTextOverlay.Start(host);
                };
                CoreBouncingText.StopAction = Views.Overlays.BouncingTextOverlay.Stop;
                CoreBouncingText.RefreshAction = Views.Overlays.BouncingTextOverlay.Refresh;
                CoreBouncingText.RestartAction = Views.Overlays.BouncingTextOverlay.Restart;

                // No CoreMods seeding here, deliberately. ModService is still in the WPF head, so
                // this head has nothing to seed the mod seam with and leaves every provider null.
                // Unseeded is the supported state: CoreMods answers from the built-in manifests,
                // which is what the WPF call sites saw with no mod active.

                // Core cannot read the running build's version - the entry assembly is whichever
                // head started the process, and Core is not it. Reading the version is a head job,
                // so this head reports its own; unseeded, CoreReleaseContent answers "0.0.0".
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                CoreReleaseContent.AppVersionProvider = () =>
                    version is null ? null : $"{version.Major}.{version.Minor}.{version.Build}";
                // Real audio through LibVLC, seeded only if libvlc loads. If it is missing,
                // CoreAudio stays unseeded: every clip "finishes" at once and nothing plays.
                // Console as well as Serilog: this head configures no Serilog sink yet.
                try { new Platform.LibVlcAudio().Seed(); Console.WriteLine("[Audio] LibVLC seeded CoreAudio"); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Audio] LibVLC unavailable, audio disabled: {ex.Message}");
                    Serilog.Log.Warning(ex, "[Audio] LibVLC unavailable; audio disabled on this head");
                }
                // CoreMindWipe stays unseeded, and it is the audio surface that is missing rather
                // than the feature: MindWipeSchedule (Core) already decides the tick interval, the
                // per-tick probability, the session escalation and which clips are candidates.
                // What this head has no answer for is the playing half - a crossfading NAudio loop
                // - so the Mind Wipe card configures correctly and plays nothing. Unseeded says
                // exactly that: every action is a no-op, IsLooping is false and ClipCount is 0, so
                // nothing reports a loop that is not running.
                //
                // CoreSession stays unseeded: this head has no session engine yet, so "not
                // running" is the truth, and the feature cards fall to their save-only branch.
                //
                // CoreModerationLog stays unseeded too, and NOT because a log is unavailable here
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

                // Achievements: the Core engine over the same achievements.json WPF uses, seeded the
                // way WPF App.xaml.cs:384/:394 seeds the two unlock seams. Unlocked is raised on the
                // caller's thread; the popup hops to the UI thread as WPF's DispatcherHelper does.
                Achievements = new AchievementEngine(new AchievementStore(AchievementsPath));
                Achievements.Unlocked += (_, a) => Dispatcher.UIThread.Post(() => ShowAchievementPopup(a));
                CoreProgram.UnlockAchievementProvider = id => Achievements?.TryUnlock(id);
                CoreProgression.TrackBubbleCountResultProvider = correct => Achievements?.TrackBubbleCountResult(correct);

                // CoreProgram: its patreon, pack-video and roadmap providers stay unseeded - this head
                // has no PatreonService, ContentPackService or RoadmapService, so it answers "no
                // premium, no pack videos, no roadmap". NotifyProvider is seeded below, once the
                // shell's toast host exists. HasPremium false still refuses a premium enrolment.

                // CoreAccount is deliberately left unseeded, and this one is a constraint rather
                // than a gap. PatreonService owns an HttpListener OAuth callback and a
                // SecureTokenStorage; this head seeds no CoreSecrets store, so by that seam's rule
                // ("unseeded means NO store, never store in the clear") there is nowhere to keep a
                // token even if the flow existed. Signed out and NOT entitled is therefore the
                // literal truth here, not a placeholder - and it is the only safe unseeded answer,
                // because an entitlement seam that failed open would hand every Linux user the
                // paid tier.
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
                // ponytail: WPF's Reconnect-Patreon branch (PatreonReconnectRule, head-only) is absent -
                // this head has no Patreon sign-in to repair, so every refusal takes the "See tiers" branch.
                // Also dropped: WPF's EmiDesk "premiumTeaseSeen" fire - no EmiDesk service on this head.
                var shell = (Views.Windows.MainShellWindow)desktop.MainWindow;
                CoreEntitlement.ShowDeniedHandler = verdict => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    Notifications.Show(verdict.Reason, Helpers.NotificationType.Warning, TimeSpan.FromSeconds(8),
                        Loc.Get("tiergate_see_tiers"), () => shell.OpenAppSettingsSection("account")));
                // OnLastWindowClose counts overlay windows too: closing the shell must take the
                // desktop overlays and their schedules down, or the process lives on UI-less.
                desktop.MainWindow.Closed += (_, _) => StopDesktopOverlays();
                // Tray: restore, wake, Stop everything (the no-hotkey panic control) and the real Exit.
                try
                {
                    shell.CreateTray();
                    // WPF MainWindow.xaml.cs:3594: StartMinimized sends the shown window to the tray.
                    // Only with a tray host to come back through; without one the window stays up.
                    if (Settings.Current.StartMinimized && shell.TrayHostPresent())
                    {
                        void ToTray(object? s, EventArgs e) { shell.Opened -= ToTray; shell.Hide(); }
                        shell.Opened += ToTray;
                    }
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Tray icon unavailable; X closes the app"); }
                // The panic key (WPF MainWindow.xaml.cs:363 installs its hook at startup the same way).
                shell.StartPanicKey();
            }
            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>Stops every desktop overlay and its schedule. <paramref name="final"/> is the shell
        /// closing; the tray's Stop everything passes false so the overlays can be started again.</summary>
        internal static void StopDesktopOverlays(bool final = true)
        {
            CoreFlash.Stop();
            CoreSubliminal.Stop();
            Views.Overlays.FlashOverlay.CloseAll(final);
            Views.Overlays.SubliminalOverlay.CloseAll();
            Views.Overlays.BouncingTextOverlay.Stop();
        }

        /// <summary>WPF App.OnAchievementUnlocked (App.xaml.cs:3815): one popup per unlock, shown at once.
        /// ponytail: no ItemUnlockedPopup - the reward map (WardrobeCatalog) is head-side in WPF;
        /// no sound, no Discord webhook - neither service exists on this head.</summary>
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
            return Path.Combine(CorePaths.UserData, "assets");
        }

        private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            if (Interlocked.Exchange(ref _exitHandled, 1) != 0) return;

            try { (((IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!).MainWindow as Views.Windows.MainShellWindow)?.Tray?.Dispose(); } catch { }

            // Restore any app we ducked; a pending Unduck would otherwise die with the process.
            try { Platform.LibVlcAudio.Instance?.Shutdown(); } catch { }
            try { Platform.LayeredAudio.Instance?.Shutdown(); } catch { }

            // Flush while the dispatcher is still usable. In particular, a serialize retry from a
            // background save must not see the shutdown-safe drop provider below.
            try { Settings?.SaveImmediate(); }
            catch { /* SettingsService logs save failures; exit must continue */ }

            // WPF AchievementService.Dispose saves synchronously; only when dirty here, so an idle exit
            // never rewrites the file (or rotates its .bak) - it may be shared with the WPF head.
            try { if (Achievements is { IsDirty: true } a) a.Save(); } catch { /* the store logs write failures */ }

            // Roadmap is lazy: do not construct it merely to dispose it on a profile that never
            // opened the quest page.
            try { Views.Windows.MainShellWindow.DisposeRoadmapIfCreated(); }
            catch { /* one service cannot prevent the head from exiting */ }

            _desktopDispatch?.Stop();
        }
    }
}
