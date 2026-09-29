using System.Windows;

namespace ConditioningControlPanel.Services
{
    /// <summary>The catalogue lookup (Core <see cref="CatalogueLookup"/>) fed from App state, opening on the Dispatcher.</summary>
    public class CatalogueLookupService : CatalogueLookup
    {
        public CatalogueLookupService()
            : base(() => App.EnhancementLibrary?.LibraryFolder, UpdateService.AppVersion, async open =>
            {
                var dispatcher = Application.Current?.Dispatcher;
                // No UI thread to dispatch onto (rare: shutdown race). Saved file is on
                // disk; treat as Success — Library tab will show it on next launch.
                if (dispatcher == null || dispatcher.HasShutdownStarted) return true;
                return dispatcher.CheckAccess() ? open() : await dispatcher.InvokeAsync(open);
            }) { }
    }
}
