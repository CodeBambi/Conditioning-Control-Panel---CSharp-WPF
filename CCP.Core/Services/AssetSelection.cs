using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The one signal the Library page sends after it changes which local assets are active
    /// (a tick, Select all, a preset). WPF called App.Flash.ClearFileCache, App.Video.ReloadAssets
    /// and App.BubbleCount.ReloadAssets directly (MainWindow.Assets.cs
    /// InvalidateAssetPoolsAfterSelectionChange); on this head the media services live in other
    /// lanes, so they subscribe here and drop their cached file lists. The page has already written
    /// DisabledAssetPaths / DisabledAssetFolders and asked for a save when this fires.
    /// </summary>
    public static class AssetSelection
    {
        /// <summary>Raised on the UI thread after the active asset set changed.</summary>
        public static event Action? Changed;

        public static void NotifyChanged()
        {
            var h = Changed;
            if (h == null) return;
            foreach (Action a in h.GetInvocationList())
            {
                try { a(); }
                catch (Exception ex) { Serilog.Log.Debug("AssetSelection.Changed subscriber failed: {E}", ex.Message); }
            }
        }
    }
}
