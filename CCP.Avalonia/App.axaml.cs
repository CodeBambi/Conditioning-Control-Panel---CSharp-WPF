using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia
{
    public partial class App : Application
    {
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

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }
}
