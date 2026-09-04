using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia
{
    public partial class App : Application
    {
        /// <summary>The settings service, or null on the headless render path.</summary>
        public static SettingsService? Settings { get; private set; }

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
            // "en" is hardcoded for now: honouring the user's choice reads AppSettings, which is
            // still in the WPF head. That lands when AppSettings moves.
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
                // Real settings on this head: SettingsService now lives in Core and reads and
                // writes settings.json under CorePaths.UserData (~/.local/share on Linux), with
                // the same migrations, backups and recovery the Windows app has. Seeded here and
                // not in Initialize() on purpose: the headless render path never reaches this
                // callback, so a CI render cannot touch a user's profile. Unseeded, Core hands
                // out one default instance, which is what the renders bind against.
                Settings = new SettingsService();
                CoreSettings.ServiceProvider = () => Settings;

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

                // CoreProgram stays unseeded, and every one of its five providers is a service
                // this head does not have: no PatreonService, no NotificationService, no
                // AchievementService, no ContentPackService, no RoadmapService instance. Unseeded
                // it answers "no premium, no toast, no badge, no pack videos, no roadmap" - which
                // is the truth here, and the safe direction on the only one that gates anything:
                // HasPremium false refuses a premium enrolment rather than granting one.

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

                desktop.MainWindow = new Views.Windows.MainShellWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
