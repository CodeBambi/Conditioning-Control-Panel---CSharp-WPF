namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Runtime representation of an installed mod.
    /// Built-in mods have no InstalledPath; custom mods point to their extracted folder.
    /// </summary>
    public class ModPackage
    {
        /// <summary>
        /// The deserialized manifest from mod.json.
        /// </summary>
        public ModManifest Manifest { get; }

        /// <summary>
        /// Filesystem path where the mod is extracted (null for built-in mods).
        /// </summary>
        public string? InstalledPath { get; }

        /// <summary>
        /// Whether this is a built-in mod (BambiSleep / SissyHypno).
        /// </summary>
        public bool IsBuiltIn { get; }

        public ModPackage(ModManifest manifest, string? installedPath, bool isBuiltIn)
        {
            Manifest = manifest;
            InstalledPath = installedPath;
            IsBuiltIn = isBuiltIn;
        }

        /// <summary>
        /// Shortcut to Manifest.Id.
        /// </summary>
        public string Id => Manifest.Id;

        /// <summary>
        /// Shortcut to Manifest.Name.
        /// </summary>
        public string Name => Manifest.Name;
    }
}
