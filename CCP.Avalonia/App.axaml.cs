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

                // No CoreMods seeding here, deliberately. ModService is still in the WPF head, so
                // this head has nothing to seed the mod seam with and leaves every provider null.
                // Unseeded is the supported state: CoreMods answers from the built-in default
                // manifest, which is the same answer the WPF service gives with no mod active.

                // Core cannot read the running build's version - the entry assembly is whichever
                // head started the process, and Core is not it. Reading the version is a head job,
                // so this head reports its own; unseeded, CoreReleaseContent answers "0.0.0".
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                CoreReleaseContent.AppVersionProvider = () =>
                    version is null ? null : $"{version.Major}.{version.Minor}.{version.Build}";
                desktop.MainWindow = new Views.Windows.MainShellWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
