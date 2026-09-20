using ConditioningControlPanel.Avalonia.Views.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Reloads the local, read-only index when the Deeper view is attached. The view can also
        /// render standalone, but the shell owns repeat navigation and asks it to rescan here.
        /// </summary>
        internal void InitializeDeeperHub()
        {
            ReloadDeeperLibraryFromDisk();
        }

        private void ReloadDeeperLibraryFromDisk()
        {
            var view = Named<DeeperTabView>("DeeperTab");
            if (view?.DataContext is DeeperTabViewModel model)
                model.ReloadLibrary();
        }
    }
}
